using Dataplat.Dbatools.Message;
using System;
using System.Collections.Generic;
using System.Management.Automation;

namespace Dataplat.Dbatools.Internal
{
    /// <summary>
    /// Static message helpers that mirror Convert-DbaMessageException, Convert-DbaMessageLevel,
    /// and Convert-DbaMessageTarget PowerShell functions.
    /// </summary>
    public static class MessageHelpers
    {
        /// <summary>
        /// Converts/transforms an exception according to registered transform rules.
        /// Mirrors Convert-DbaMessageException.ps1.
        /// </summary>
        /// <param name="cmdlet">The PSCmdlet for script invocation</param>
        /// <param name="exception">The exception to transform</param>
        /// <param name="functionName">The calling function name</param>
        /// <param name="moduleName">The calling module name</param>
        /// <returns>The transformed (or original) exception</returns>
        public static Exception ConvertException(PSCmdlet cmdlet, Exception exception, string functionName, string moduleName)
        {
            if (exception == null)
                return null;

            string lowTypeName = exception.GetType().FullName.ToLower();

            // Check direct type transforms
            if (MessageHost.ExceptionTransforms.ContainsKey(lowTypeName))
            {
                try
                {
                    var result = cmdlet.InvokeCommand.InvokeScript(
                        false,
                        ScriptBlock.Create(MessageHost.ExceptionTransforms[lowTypeName].ToString()),
                        null,
                        exception
                    );
                    if (result != null && result.Count > 0)
                        return (Exception)result[0].BaseObject;
                }
                catch (Exception e)
                {
                    MessageHost.WriteTransformError(
                        new ErrorRecord(e, "MessageHelpers", ErrorCategory.OperationStopped, null),
                        functionName, moduleName, exception, TransformType.Exception,
                        System.Management.Automation.Runspaces.Runspace.DefaultRunspace != null
                            ? System.Management.Automation.Runspaces.Runspace.DefaultRunspace.InstanceId
                            : Guid.Empty
                    );
                    return exception;
                }
            }

            // Check conditional transforms
            TransformCondition transform = MessageHost.ExceptionTransformList.Get(lowTypeName, moduleName, functionName);
            if (transform != null)
            {
                try
                {
                    var result = cmdlet.InvokeCommand.InvokeScript(
                        false,
                        ScriptBlock.Create(transform.ScriptBlock.ToString()),
                        null,
                        exception
                    );
                    if (result != null && result.Count > 0)
                        return (Exception)result[0].BaseObject;
                }
                catch (Exception e)
                {
                    MessageHost.WriteTransformError(
                        new ErrorRecord(e, "MessageHelpers", ErrorCategory.OperationStopped, null),
                        functionName, moduleName, exception, TransformType.Exception,
                        System.Management.Automation.Runspaces.Runspace.DefaultRunspace != null
                            ? System.Management.Automation.Runspaces.Runspace.DefaultRunspace.InstanceId
                            : Guid.Empty
                    );
                    return exception;
                }
            }

            return exception;
        }

        /// <summary>
        /// Converts/transforms a target object according to registered transform rules.
        /// Mirrors Convert-DbaMessageTarget.ps1.
        /// </summary>
        /// <param name="cmdlet">The PSCmdlet for script invocation</param>
        /// <param name="target">The target to transform</param>
        /// <param name="functionName">The calling function name</param>
        /// <param name="moduleName">The calling module name</param>
        /// <returns>The transformed (or original) target</returns>
        public static object ConvertTarget(PSCmdlet cmdlet, object target, string functionName, string moduleName)
        {
            if (target == null)
                return null;

            string lowTypeName = target.GetType().FullName.ToLower();

            // Check direct type transforms
            if (MessageHost.TargetTransforms.ContainsKey(lowTypeName))
            {
                try
                {
                    var result = cmdlet.InvokeCommand.InvokeScript(
                        false,
                        ScriptBlock.Create(MessageHost.TargetTransforms[lowTypeName].ToString()),
                        null,
                        target
                    );
                    if (result != null && result.Count > 0)
                        return result[0].BaseObject;
                }
                catch (Exception e)
                {
                    MessageHost.WriteTransformError(
                        new ErrorRecord(e, "MessageHelpers", ErrorCategory.OperationStopped, null),
                        functionName, moduleName, target, TransformType.Target,
                        System.Management.Automation.Runspaces.Runspace.DefaultRunspace != null
                            ? System.Management.Automation.Runspaces.Runspace.DefaultRunspace.InstanceId
                            : Guid.Empty
                    );
                    return target;
                }
            }

            // Check conditional transforms
            TransformCondition transform = MessageHost.TargetTransformlist.Get(lowTypeName, moduleName, functionName);
            if (transform != null)
            {
                try
                {
                    var result = cmdlet.InvokeCommand.InvokeScript(
                        false,
                        ScriptBlock.Create(transform.ScriptBlock.ToString()),
                        null,
                        target
                    );
                    if (result != null && result.Count > 0)
                        return result[0].BaseObject;
                }
                catch (Exception e)
                {
                    MessageHost.WriteTransformError(
                        new ErrorRecord(e, "MessageHelpers", ErrorCategory.OperationStopped, null),
                        functionName, moduleName, target, TransformType.Target,
                        System.Management.Automation.Runspaces.Runspace.DefaultRunspace != null
                            ? System.Management.Automation.Runspaces.Runspace.DefaultRunspace.InstanceId
                            : Guid.Empty
                    );
                    return target;
                }
            }

            return target;
        }

        /// <summary>
        /// Resolves the effective message level applying nested level decrements and modifiers.
        /// Mirrors Convert-DbaMessageLevel.ps1.
        /// </summary>
        /// <param name="originalLevel">The original message level</param>
        /// <param name="fromStopFunction">Whether called from Stop-Function</param>
        /// <param name="tags">Tags attached to the message</param>
        /// <param name="functionName">The calling function name</param>
        /// <param name="moduleName">The calling module name</param>
        /// <param name="stackDepth">Current callstack depth</param>
        /// <returns>The resolved message level</returns>
        public static MessageLevel ConvertLevel(MessageLevel originalLevel, bool fromStopFunction, List<string> tags, string functionName, string moduleName, int stackDepth)
        {
            int number = (int)originalLevel;

            if (MessageHost.NestedLevelDecrement > 0)
            {
                int depth = stackDepth - 3;
                if (fromStopFunction)
                    depth--;
                if (depth > 0)
                    number += depth * MessageHost.NestedLevelDecrement;
            }

            if (MessageHost.MessageLevelModifiers.Count > 0)
            {
                foreach (MessageLevelModifier modifier in MessageHost.MessageLevelModifiers.Values)
                {
                    if (modifier.AppliesTo(functionName, moduleName, tags))
                        number += modifier.Modifier;
                }
            }

            if (number > 9)
                number = 9;
            if (number < 1)
                number = 1;

            return (MessageLevel)number;
        }
    }
}
