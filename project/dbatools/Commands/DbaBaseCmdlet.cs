using Dataplat.Dbatools.Message;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Base class for all dbatools cmdlets. Provides automatic EnableException support,
    /// flow control methods (StopFunction, TestBound, TestFunctionInterrupt),
    /// and messaging helpers.
    /// </summary>
    public abstract class DbaBaseCmdlet : PSCmdlet
    {
        /// <summary>
        /// This parameter disables user-friendly warnings and enables the throwing of exceptions.
        /// This is less user friendly, but allows catching exceptions in calling scripts.
        /// </summary>
        [Parameter()]
        public SwitchParameter EnableException { get; set; }

        #region Caller Info
        /// <summary>
        /// The resolved name of the calling function
        /// </summary>
        protected string CallerFunctionName;

        /// <summary>
        /// The resolved module name of the caller
        /// </summary>
        protected string CallerModuleName;

        /// <summary>
        /// The file from which the cmdlet was called
        /// </summary>
        protected string CallerFile;

        /// <summary>
        /// The line from which the cmdlet was called
        /// </summary>
        protected int CallerLine;

        /// <summary>
        /// The current callstack at begin time
        /// </summary>
        protected IEnumerable<CallStackFrame> CallerCallStack;
        #endregion Caller Info

        #region Cmdlet Lifecycle
        /// <summary>
        /// Auto-resolves caller info from the callstack. Subclasses overriding this
        /// should call base.BeginProcessing().
        /// </summary>
        protected override void BeginProcessing()
        {
            ResolveCallerInfo();
        }

        /// <summary>
        /// Resolves caller info from the PowerShell callstack.
        /// Mirrors the pattern used in WriteMessageCommand.
        /// </summary>
        protected void ResolveCallerInfo()
        {
            try
            {
                CallerCallStack = Utility.UtilityHost.Callstack;
                CallStackFrame callerFrame = null;
                if (CallerCallStack != null && CallerCallStack.Any())
                    callerFrame = CallerCallStack.First();

                if (callerFrame != null)
                {
                    if (String.IsNullOrEmpty(CallerFunctionName))
                    {
                        if (callerFrame.InvocationInfo == null)
                            CallerFunctionName = callerFrame.FunctionName;
                        else if (callerFrame.InvocationInfo.MyCommand == null)
                            CallerFunctionName = callerFrame.InvocationInfo.InvocationName;
                        else if (callerFrame.InvocationInfo.MyCommand.Name != "")
                            CallerFunctionName = callerFrame.InvocationInfo.MyCommand.Name;
                        else
                            CallerFunctionName = callerFrame.FunctionName;
                    }

                    if (String.IsNullOrEmpty(CallerModuleName))
                        if ((callerFrame.InvocationInfo != null) && (callerFrame.InvocationInfo.MyCommand != null))
                            CallerModuleName = callerFrame.InvocationInfo.MyCommand.ModuleName;

                    if (String.IsNullOrEmpty(CallerFile))
                        CallerFile = callerFrame.ScriptName;

                    if (CallerLine <= 0)
                        CallerLine = callerFrame.Position.EndLineNumber;
                }
            }
            catch
            {
                // Callstack resolution may fail in some hosting scenarios
            }

            if (String.IsNullOrEmpty(CallerFunctionName))
                CallerFunctionName = MyInvocation.MyCommand.Name;
            if (String.IsNullOrEmpty(CallerModuleName))
                CallerModuleName = MyInvocation.MyCommand.ModuleName;
            if (String.IsNullOrEmpty(CallerFunctionName))
                CallerFunctionName = "<Unknown>";
            if (String.IsNullOrEmpty(CallerModuleName))
                CallerModuleName = "<Unknown>";
        }
        #endregion Cmdlet Lifecycle

        #region Flow Control - StopFunction
        /// <summary>
        /// Implements the Stop-Function pattern: writes a warning message and either throws
        /// (if EnableException) or sets the interrupt flag (if not).
        /// </summary>
        /// <param name="message">The message to display</param>
        /// <param name="exception">Optional exception to include</param>
        /// <param name="errorRecord">Optional error record</param>
        /// <param name="target">The target object being processed</param>
        /// <param name="isContinue">If true, calls continue instead of throwing</param>
        /// <param name="category">The error category</param>
        /// <param name="tag">Tags for the message</param>
        /// <param name="overrideExceptionMessage">Skip appending exception message</param>
        protected void StopFunction(
            string message,
            Exception exception = null,
            ErrorRecord errorRecord = null,
            object target = null,
            bool isContinue = false,
            ErrorCategory category = ErrorCategory.NotSpecified,
            string[] tag = null,
            bool overrideExceptionMessage = false)
        {
            string functionName = CallerFunctionName ?? "<Unknown>";
            string moduleName = CallerModuleName ?? "<Unknown>";

            // Build the exception message
            string exceptionMessage = message;
            if (exception != null && !overrideExceptionMessage)
            {
                string innerMsg = GetDeepestExceptionMessage(exception);
                if (!String.IsNullOrEmpty(innerMsg) && !message.Contains(innerMsg))
                    exceptionMessage = String.Format("{0} | {1}", message, innerMsg);
            }
            else if (errorRecord != null && !overrideExceptionMessage && errorRecord.Exception != null)
            {
                string innerMsg = GetDeepestExceptionMessage(errorRecord.Exception);
                if (!String.IsNullOrEmpty(innerMsg) && !message.Contains(innerMsg))
                    exceptionMessage = String.Format("{0} | {1}", message, innerMsg);
            }

            // Create the error record
            Exception errorException = exception ?? (errorRecord != null ? errorRecord.Exception : null);
            if (errorException == null)
                errorException = new Exception(exceptionMessage);

            ErrorRecord newRecord = new ErrorRecord(
                errorException,
                String.Format("{0}_{1}", moduleName, functionName),
                category,
                target
            );

            // Write as warning-level message via Write-Message
            List<string> tags = new List<string>();
            if (tag != null)
                tags.AddRange(tag);

            string writeMessageScript = @"
param($msg, $lvl, $fn, $mn, $fi, $ln, $er, $ex, $tgt, $tag, $ee, $oem)
Write-Message -Level $lvl -Message $msg -FunctionName $fn -ModuleName $mn -File $fi -Line $ln -ErrorRecord $er -Target $tgt -Tag $tag -EnableException $ee -OverrideExceptionMessage:$oem
";
            try
            {
                InvokeCommand.InvokeScript(
                    false,
                    ScriptBlock.Create(writeMessageScript),
                    null,
                    exceptionMessage,
                    MessageLevel.Warning,
                    functionName,
                    moduleName,
                    CallerFile,
                    CallerLine,
                    new ErrorRecord[] { newRecord },
                    null, // exception already wrapped in error record
                    target,
                    tags.ToArray(),
                    EnableException.ToBool(),
                    overrideExceptionMessage
                );
            }
            catch
            {
                // If Write-Message is not available, fall back to WriteWarning
                WriteWarning(exceptionMessage);
            }

            if (EnableException.ToBool())
            {
                // Log the error
                LogHost.WriteErrorEntry(
                    new ErrorRecord[] { newRecord },
                    functionName,
                    moduleName,
                    tags,
                    DateTime.Now,
                    exceptionMessage,
                    System.Management.Automation.Runspaces.Runspace.DefaultRunspace != null
                        ? System.Management.Automation.Runspaces.Runspace.DefaultRunspace.InstanceId
                        : Guid.Empty,
                    Environment.MachineName
                );

                if (isContinue)
                {
                    WriteError(newRecord);
                    return;
                }

                ThrowTerminatingError(newRecord);
            }
            else
            {
                // Set interrupt variable in caller scope so Test-FunctionInterrupt works
                try
                {
                    SessionState.PSVariable.Set(
                        new PSVariable(
                            "__dbatools_interrupt_function_78Q9VPrM6999g6zo24Qn83m09XF56InEn4hFrA8Fwhu5xJrs6r",
                            true,
                            ScopedItemOptions.None
                        )
                    );
                }
                catch
                {
                    // Scope manipulation may fail in constrained scenarios
                }
            }
        }
        #endregion Flow Control - StopFunction

        #region Flow Control - TestBound
        /// <summary>
        /// Tests whether any of the specified parameters were bound (provided by the user).
        /// Returns true if at least one was bound.
        /// </summary>
        /// <param name="parameterNames">The parameter names to check</param>
        /// <returns>True if at least one parameter was bound</returns>
        protected bool TestBound(params string[] parameterNames)
        {
            if (parameterNames == null || parameterNames.Length == 0)
                return false;

            foreach (string name in parameterNames)
            {
                if (MyInvocation.BoundParameters.ContainsKey(name))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Tests whether ALL specified parameters were bound.
        /// </summary>
        /// <param name="parameterNames">The parameter names to check</param>
        /// <returns>True if all parameters were bound</returns>
        protected bool TestBoundAll(params string[] parameterNames)
        {
            if (parameterNames == null || parameterNames.Length == 0)
                return false;

            foreach (string name in parameterNames)
            {
                if (!MyInvocation.BoundParameters.ContainsKey(name))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Tests whether any of the specified parameters were bound.
        /// Alias for TestBound for readability.
        /// </summary>
        /// <param name="parameterNames">The parameter names to check</param>
        /// <returns>True if at least one parameter was bound</returns>
        protected bool TestBoundAny(params string[] parameterNames)
        {
            return TestBound(parameterNames);
        }

        /// <summary>
        /// Tests whether none of the specified parameters were bound (inverse of TestBound).
        /// </summary>
        /// <param name="parameterNames">The parameter names to check</param>
        /// <returns>True if none of the parameters were bound</returns>
        protected bool TestBoundNot(params string[] parameterNames)
        {
            return !TestBound(parameterNames);
        }
        #endregion Flow Control - TestBound

        #region Flow Control - TestFunctionInterrupt
        /// <summary>
        /// Tests whether the function interrupt flag has been set by StopFunction.
        /// Used in ProcessRecord to check if a non-terminating error in Begin should stop processing.
        /// </summary>
        /// <returns>True if the interrupt flag is set</returns>
        protected bool TestFunctionInterrupt()
        {
            try
            {
                PSVariable interruptVar = SessionState.PSVariable.Get(
                    "__dbatools_interrupt_function_78Q9VPrM6999g6zo24Qn83m09XF56InEn4hFrA8Fwhu5xJrs6r"
                );
                if (interruptVar != null && interruptVar.Value is bool boolVal)
                    return boolVal;
                if (interruptVar != null && interruptVar.Value != null)
                    return true;
            }
            catch
            {
                // Variable access may fail
            }
            return false;
        }
        #endregion Flow Control - TestFunctionInterrupt

        #region Messaging Helpers
        /// <summary>
        /// Writes a verbose-level message through the Write-Message infrastructure.
        /// </summary>
        /// <param name="message">The message text</param>
        /// <param name="tags">Optional tags for filtering</param>
        protected void WriteMessageVerbose(string message, params string[] tags)
        {
            WriteMessageAtLevel(message, MessageLevel.Verbose, tags);
        }

        /// <summary>
        /// Writes a warning-level message through the Write-Message infrastructure.
        /// </summary>
        /// <param name="message">The message text</param>
        /// <param name="tags">Optional tags for filtering</param>
        protected void WriteMessageWarning(string message, params string[] tags)
        {
            WriteMessageAtLevel(message, MessageLevel.Warning, tags);
        }

        /// <summary>
        /// Writes a message at the specified level through Write-Message.
        /// </summary>
        /// <param name="message">The message text</param>
        /// <param name="level">The message level</param>
        /// <param name="tags">Optional tags</param>
        protected void WriteMessageAtLevel(string message, MessageLevel level, string[] tags)
        {
            string script = @"
param($msg, $lvl, $fn, $mn, $fi, $ln, $tag)
Write-Message -Level $lvl -Message $msg -FunctionName $fn -ModuleName $mn -File $fi -Line $ln -Tag $tag
";
            try
            {
                InvokeCommand.InvokeScript(
                    false,
                    ScriptBlock.Create(script),
                    null,
                    message,
                    level,
                    CallerFunctionName,
                    CallerModuleName,
                    CallerFile,
                    CallerLine,
                    tags
                );
            }
            catch
            {
                // Fallback if Write-Message is not loaded yet
                if (level == MessageLevel.Warning)
                    WriteWarning(message);
                else
                    WriteVerbose(message);
            }
        }
        #endregion Messaging Helpers

        #region PSObject Property Helpers
        /// <summary>
        /// Gets a string property value from a PSObject.
        /// </summary>
        /// <param name="obj">The PSObject to read from</param>
        /// <param name="propertyName">The property name</param>
        /// <returns>The property value as a string, or null</returns>
        internal static string GetPropertyString(PSObject obj, string propertyName)
        {
            if (obj == null)
                return null;
            try
            {
                PSPropertyInfo prop = obj.Properties[propertyName];
                if (prop != null && prop.Value != null)
                    return prop.Value.ToString();
            }
            catch (Exception)
            {
                // Property may not exist or getter may throw
            }
            return null;
        }

        /// <summary>
        /// Gets an object property value from a PSObject wrapped as PSObject.
        /// </summary>
        /// <param name="obj">The PSObject to read from</param>
        /// <param name="propertyName">The property name</param>
        /// <returns>The property value as a PSObject, or null</returns>
        internal static PSObject GetPropertyObject(PSObject obj, string propertyName)
        {
            if (obj == null)
                return null;
            try
            {
                PSPropertyInfo prop = obj.Properties[propertyName];
                if (prop != null && prop.Value != null)
                    return PSObject.AsPSObject(prop.Value);
            }
            catch (Exception)
            {
                // Property may not exist or getter may throw
            }
            return null;
        }
        #endregion PSObject Property Helpers

        #region Utility
        /// <summary>
        /// Gets the deepest meaningful exception message by walking the InnerException chain.
        /// </summary>
        /// <param name="ex">The exception to unwrap</param>
        /// <returns>The deepest non-null message</returns>
        protected static string GetDeepestExceptionMessage(Exception ex)
        {
            if (ex == null)
                return null;

            Exception current = ex;
            string lastMessage = current.Message;

            while (current.InnerException != null)
            {
                current = current.InnerException;
                if (!String.IsNullOrEmpty(current.Message))
                    lastMessage = current.Message;
            }

            return lastMessage;
        }

        /// <summary>
        /// Gets the error message from an ErrorRecord, unwrapping inner exceptions.
        /// Mirrors Get-ErrorMessage.ps1 behavior.
        /// </summary>
        /// <param name="record">The error record</param>
        /// <returns>The deepest exception message</returns>
        protected static string GetErrorMessage(ErrorRecord record)
        {
            if (record == null || record.Exception == null)
                return null;

            return GetDeepestExceptionMessage(record.Exception);
        }
        #endregion Utility
    }
}
