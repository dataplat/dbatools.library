using Dataplat.Dbatools.Message;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Text.RegularExpressions;

namespace Dataplat.Dbatools.Commands
{
    public partial class WriteMessageCommand
    {
        /// <summary>
        /// Processes the target transform rules on an input object
        /// </summary>
        /// <param name="Item">The item to transform</param>
        /// <returns>The transformed object</returns>
        private object ResolveTarget(object Item)
        {
            if (Item == null)
                return null;

            string lowTypeName = Item.GetType().FullName.ToLower();

            if (MessageHost.TargetTransforms.ContainsKey(lowTypeName))
            {
                try { return InvokeCommand.InvokeScript(false, ScriptBlock.Create(MessageHost.TargetTransforms[lowTypeName].ToString()), null, Item); }
                catch (Exception e)
                {
                    MessageHost.WriteTransformError(new ErrorRecord(e, "Write-Message", ErrorCategory.OperationStopped, null), FunctionName, ModuleName, Item, TransformType.Target, System.Management.Automation.Runspaces.Runspace.DefaultRunspace.InstanceId);
                    return Item;
                }
            }

            TransformCondition transform = MessageHost.TargetTransformlist.Get(lowTypeName, ModuleName, FunctionName);
            if (transform != null)
            {
                try { return InvokeCommand.InvokeScript(false, ScriptBlock.Create(transform.ScriptBlock.ToString()), null, Item); }
                catch (Exception e)
                {
                    MessageHost.WriteTransformError(new ErrorRecord(e, "Write-Message", ErrorCategory.OperationStopped, null), FunctionName, ModuleName, Item, TransformType.Target, System.Management.Automation.Runspaces.Runspace.DefaultRunspace.InstanceId);
                    return Item;
                }
            }

            return Item;
        }

        /// <summary>
        /// Processes the specified exception specified
        /// </summary>
        /// <param name="Item">The exception to process</param>
        /// <returns>The transformed exception</returns>
        private Exception ResolveException(Exception Item)
        {
            if (Item == null)
                return Item;

            string lowTypeName = Item.GetType().FullName.ToLower();

            if (MessageHost.ExceptionTransforms.ContainsKey(lowTypeName))
            {
                try { return (Exception)InvokeCommand.InvokeScript(false, ScriptBlock.Create(MessageHost.ExceptionTransforms[lowTypeName].ToString()), null, Item)[0].BaseObject; }
                catch (Exception e)
                {
                    MessageHost.WriteTransformError(new ErrorRecord(e, "Write-Message", ErrorCategory.OperationStopped, null), FunctionName, ModuleName, Item, TransformType.Exception, System.Management.Automation.Runspaces.Runspace.DefaultRunspace.InstanceId);
                    return Item;
                }
            }

            TransformCondition transform = MessageHost.ExceptionTransformList.Get(lowTypeName, ModuleName, FunctionName);
            if (transform != null)
            {
                try { return (Exception)InvokeCommand.InvokeScript(false, ScriptBlock.Create(transform.ScriptBlock.ToString()), null, Item)[0].BaseObject; }
                catch (Exception e)
                {
                    MessageHost.WriteTransformError(new ErrorRecord(e, "Write-Message", ErrorCategory.OperationStopped, null), FunctionName, ModuleName, Item, TransformType.Exception, System.Management.Automation.Runspaces.Runspace.DefaultRunspace.InstanceId);
                    return Item;
                }
            }

            return Item;
        }

        /// <summary>
        /// Processes the input level and apply policy and rules
        /// </summary>
        /// <param name="Level">The original level of the message</param>
        /// <returns>The processed level</returns>
        private MessageLevel ResolveLevel(MessageLevel Level)
        {
            int tempLevel = (int)Level;

            if (MessageHost.NestedLevelDecrement > 0)
            {
                int depth = _stackDepth - 2;
                if (_fromStopFunction)
                    depth--;
                tempLevel = tempLevel + depth * MessageHost.NestedLevelDecrement;
            }

            if (MessageHost.MessageLevelModifiers.Count > 0)
                foreach (MessageLevelModifier modifier in MessageHost.MessageLevelModifiers.Values)
                    if (modifier.AppliesTo(FunctionName, ModuleName, _Tags))
                        tempLevel = tempLevel + modifier.Modifier;

            if (tempLevel > 9)
                tempLevel = 9;
            if (tempLevel < 1)
                tempLevel = 1;

            return (MessageLevel)tempLevel;
        }

        /// <summary>
        /// Builds the message item for display of Verbose, Warning and Debug streams
        /// </summary>
        /// <returns>The message to return</returns>
        private string GetMessage()
        {
            if (!String.IsNullOrEmpty(_message))
                return _message;
            if (MessageHost.EnableMessageTimestamp && MessageHost.EnableMessageBreadcrumbs)
                _message = String.Format("[{0}]{1}{2}", _timestamp.ToString("HH:mm:ss"), _BreadCrumbsString, GetMessageSimple());
            else if (MessageHost.EnableMessageTimestamp && MessageHost.EnableMessageDisplayCommand)
                _message = String.Format("[{0}][{1}] {2}", _timestamp.ToString("HH:mm:ss"), FunctionName, GetMessageSimple());
            else if (MessageHost.EnableMessageTimestamp)
                _message = String.Format("[{0}] {1}", _timestamp.ToString("HH:mm:ss"), GetMessageSimple());
            else if (MessageHost.EnableMessageBreadcrumbs)
                _message = String.Format("{0}{1}", _BreadCrumbsString, GetMessageSimple());
            else if (MessageHost.EnableMessageDisplayCommand)
                _message = String.Format("[{0}] {1}", FunctionName, GetMessageSimple());
            else
                _message = GetMessageSimple();

            return _message;
        }

        /// <summary>
        /// Builds the base message for internal system use.
        /// </summary>
        /// <returns>The message to return</returns>
        private string GetMessageSimple()
        {
            if (!String.IsNullOrEmpty(_messageSimple))
                return _messageSimple;

            string baseMessage = _errorQualifiedMessage;
            foreach (Match match in Regex.Matches(baseMessage, "<c=[\"'](.*?)[\"']>(.*?)</c>"))
                baseMessage = Regex.Replace(baseMessage, Regex.Escape(match.Value), match.Groups[2].Value);
            _messageSimple = baseMessage;

            return _messageSimple;
        }

        /// <summary>
        /// Builds the message item if needed and returns it
        /// </summary>
        /// <returns>The message to return</returns>
        private string GetMessageColor()
        {
            if (!String.IsNullOrEmpty(_messageColor))
                return _messageColor;

            if (MessageHost.EnableMessageTimestamp && MessageHost.EnableMessageBreadcrumbs)
                _messageColor = String.Format("[<c='sub'>{0}</c>]{1} {2}", _timestamp.ToString("HH:mm:ss"), _BreadCrumbsStringColored, _errorQualifiedMessage);
            else if (MessageHost.EnableMessageTimestamp && MessageHost.EnableMessageDisplayCommand)
                _messageColor = String.Format("[<c='sub'>{0}</c>][<c='sub'>{1}</c>] {2}", _timestamp.ToString("HH:mm:ss"), FunctionName, _errorQualifiedMessage);
            else if (MessageHost.EnableMessageTimestamp)
                _messageColor = String.Format("[<c='sub'>{0}</c>] {1}", _timestamp.ToString("HH:mm:ss"), _errorQualifiedMessage);
            else if (MessageHost.EnableMessageBreadcrumbs)
                _messageColor = String.Format("{0}{1}", _BreadCrumbsStringColored, _errorQualifiedMessage);
            else if (MessageHost.EnableMessageDisplayCommand)
                _messageColor = String.Format("[<c='sub'>{0}</c>] {1}", FunctionName, _errorQualifiedMessage);
            else
                _messageColor = _errorQualifiedMessage;

            return _messageColor;
        }

        /// <summary>
        /// Non-host output in developermode
        /// </summary>
        /// <returns>The string to write on messages that don't go straight to Write-HostColor</returns>
        private string GetMessageDeveloper()
        {
            if (!String.IsNullOrEmpty(_messageDeveloper))
                return _messageDeveloper;

            string targetString = "";
            if (Target != null)
            {
                if (Target.ToString() != Target.GetType().FullName)
                    targetString = String.Format(" [T: {0}] ", Target.ToString());
                else
                    targetString = String.Format(" [T: <{0}>] ", Target.GetType().Name);
            }

            List<string> channelList = new List<string>();
            if (!_silent)
            {
                if (Level == MessageLevel.Warning)
                    channelList.Add("Warning");
                if ((MessageHost.MaximumInformation >= (int)Level) && (MessageHost.MinimumInformation <= (int)Level))
                    channelList.Add("Information");
            }
            if ((MessageHost.MaximumVerbose >= (int)Level) && (MessageHost.MinimumVerbose <= (int)Level))
                channelList.Add("Verbose");
            if ((MessageHost.MaximumDebug >= (int)Level) && (MessageHost.MinimumDebug <= (int)Level))
                channelList.Add("Debug");

            _messageDeveloper = String.Format(@"[{0}][{1}][L: {2}]{3}[C: {4}][EE: {5}][O: {6}]
    {7}", _timestamp.ToString("HH:mm:ss"), FunctionName, Level, targetString, String.Join(",", channelList), EnableException, (!String.IsNullOrEmpty(Once)), GetMessageSimple());

            return _messageDeveloper;
        }

        /// <summary>
        /// Host output in developermode
        /// </summary>
        /// <returns>The string to write on messages that go straight to Write-HostColor</returns>
        private string GetMessageDeveloperColor()
        {
            if (!String.IsNullOrEmpty(_messageDeveloperColor))
                return _messageDeveloperColor;

            string targetString = "";
            if (Target != null)
            {
                if (Target.ToString() != Target.GetType().FullName)
                    targetString = String.Format(" [<c='sub'>T:</c> <c='em'>{0}</c>] ", Target.ToString());
                else
                    targetString = String.Format(" [<c='sub'>T:</c> <c='em'><{0}></c>] ", Target.GetType().Name);
            }

            List<string> channelList = new List<string>();
            if (!_silent)
            {
                if (Level == MessageLevel.Warning)
                    channelList.Add("Warning");
                if ((MessageHost.MaximumInformation >= (int)Level) && (MessageHost.MinimumInformation <= (int)Level))
                    channelList.Add("Information");
            }
            if ((MessageHost.MaximumVerbose >= (int)Level) && (MessageHost.MinimumVerbose <= (int)Level))
                channelList.Add("Verbose");
            if ((MessageHost.MaximumDebug >= (int)Level) && (MessageHost.MinimumDebug <= (int)Level))
                channelList.Add("Debug");

            _messageDeveloperColor = String.Format(@"[<c='sub'>{0}</c>][<c='sub'>{1}</c>][<c='sub'>L:</c> <c='em'>{2}</c>]{3}[<c='sub'>C: <c='em'>{4}</c>][<c='sub'>EE: <c='em'>{5}</c>][<c='sub'>O: <c='em'>{6}</c>]
    {7}", _timestamp.ToString("HH:mm:ss"), FunctionName, Level, targetString, String.Join(",", channelList), EnableException, (!String.IsNullOrEmpty(Once)), _errorQualifiedMessage);

            return _messageDeveloperColor;
        }
    }
}
