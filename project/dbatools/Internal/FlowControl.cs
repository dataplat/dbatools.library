using Dataplat.Dbatools.Message;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Management.Automation;

namespace Dataplat.Dbatools.Internal
{
    /// <summary>
    /// Static flow control helpers that mirror the PowerShell Stop-Function, Test-Bound,
    /// Test-FunctionInterrupt, and Get-ErrorMessage functions.
    /// These are used by thin PS1 wrappers to delegate to C#.
    /// </summary>
    public static class FlowControl
    {
        #region StopFunction
        /// <summary>
        /// Static version of Stop-Function for use by PS1 wrappers.
        /// Writes a warning-level message and either throws (if EnableException) or sets the interrupt flag.
        /// </summary>
        /// <param name="cmdlet">The calling PSCmdlet context</param>
        /// <param name="message">The message to display</param>
        /// <param name="exception">Optional exception</param>
        /// <param name="errorRecord">Optional error record</param>
        /// <param name="target">Target object being processed</param>
        /// <param name="enableException">Whether to throw on error</param>
        /// <param name="isContinue">Whether to continue instead of throw</param>
        /// <param name="category">Error category</param>
        /// <param name="functionName">Calling function name (auto-resolved if null)</param>
        /// <param name="line">Calling line number</param>
        /// <param name="file">Calling file</param>
        /// <param name="tag">Tags for the message</param>
        /// <param name="overrideExceptionMessage">Skip automatic exception message append</param>
        public static void StopFunction(
            PSCmdlet cmdlet,
            string message,
            Exception exception = null,
            ErrorRecord errorRecord = null,
            object target = null,
            bool enableException = false,
            bool isContinue = false,
            ErrorCategory category = ErrorCategory.NotSpecified,
            string functionName = null,
            int line = 0,
            string file = null,
            string[] tag = null,
            bool overrideExceptionMessage = false)
        {
            if (cmdlet == null)
                throw new ArgumentNullException("cmdlet");

            if (String.IsNullOrEmpty(functionName))
                functionName = ResolveFunctionName(cmdlet);

            string moduleName = ResolveModuleName(cmdlet);

            // Build exception message
            string exceptionMessage = message;
            if (!overrideExceptionMessage)
            {
                Exception sourceEx = exception;
                if (sourceEx == null && errorRecord != null)
                    sourceEx = errorRecord.Exception;

                if (sourceEx != null)
                {
                    string innerMsg = GetDeepestExceptionMessage(sourceEx);
                    if (!String.IsNullOrEmpty(innerMsg) && !message.Contains(innerMsg))
                        exceptionMessage = String.Format("{0} | {1}", message, innerMsg);
                }
            }

            // Build error record
            Exception errorEx = exception;
            if (errorEx == null && errorRecord != null)
                errorEx = errorRecord.Exception;
            if (errorEx == null)
                errorEx = new Exception(exceptionMessage);

            ErrorRecord newRecord = new ErrorRecord(
                errorEx,
                String.Format("{0}_{1}", moduleName, functionName),
                category,
                target
            );

            // Write through Write-Message
            List<string> tags = new List<string>();
            if (tag != null)
                tags.AddRange(tag);

            string writeMessageScript = @"
param($msg, $lvl, $fn, $mn, $fi, $ln, $er, $tgt, $tag, $ee, $oem)
Write-Message -Level $lvl -Message $msg -FunctionName $fn -ModuleName $mn -File $fi -Line $ln -ErrorRecord $er -Target $tgt -Tag $tag -EnableException $ee -OverrideExceptionMessage:$oem
";
            try
            {
                cmdlet.InvokeCommand.InvokeScript(
                    false,
                    ScriptBlock.Create(writeMessageScript),
                    null,
                    exceptionMessage,
                    MessageLevel.Warning,
                    functionName,
                    moduleName,
                    file,
                    line,
                    new ErrorRecord[] { newRecord },
                    target,
                    tags.ToArray(),
                    enableException,
                    overrideExceptionMessage
                );
            }
            catch
            {
                cmdlet.WriteWarning(exceptionMessage);
            }

            if (enableException)
            {
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
                    cmdlet.WriteError(newRecord);
                    return;
                }

                cmdlet.ThrowTerminatingError(newRecord);
            }
            else
            {
                // Set interrupt variable in caller's scope
                try
                {
                    cmdlet.SessionState.PSVariable.Set(
                        new PSVariable(
                            "__dbatools_interrupt_function_78Q9VPrM6999g6zo24Qn83m09XF56InEn4hFrA8Fwhu5xJrs6r",
                            true,
                            ScopedItemOptions.None
                        )
                    );
                }
                catch
                {
                    // Scope manipulation may fail
                }
            }
        }
        #endregion StopFunction

        #region TestBound
        /// <summary>
        /// Tests whether any of the specified parameters were bound in the calling cmdlet.
        /// Static version for PS1 wrapper use.
        /// </summary>
        /// <param name="boundParameters">The bound parameters dictionary ($PSBoundParameters)</param>
        /// <param name="parameterNames">Parameter names to check</param>
        /// <returns>True if at least one was bound</returns>
        public static bool TestBound(IDictionary boundParameters, params string[] parameterNames)
        {
            if (boundParameters == null || parameterNames == null || parameterNames.Length == 0)
                return false;

            foreach (string name in parameterNames)
            {
                if (boundParameters.Contains(name))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Tests whether ALL specified parameters were bound.
        /// </summary>
        /// <param name="boundParameters">The bound parameters dictionary</param>
        /// <param name="parameterNames">Parameter names to check</param>
        /// <returns>True if all were bound</returns>
        public static bool TestBoundAll(IDictionary boundParameters, params string[] parameterNames)
        {
            if (boundParameters == null || parameterNames == null || parameterNames.Length == 0)
                return false;

            foreach (string name in parameterNames)
            {
                if (!boundParameters.Contains(name))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Full Test-Bound implementation matching the PS1 function signature.
        /// Supports Not (invert), And (require all), Min, Max range checks.
        /// </summary>
        /// <param name="boundParameters">The bound parameters dictionary</param>
        /// <param name="parameterNames">Parameter names to check</param>
        /// <param name="not">Invert the result</param>
        /// <param name="and">Require all parameters to be present</param>
        /// <param name="min">Minimum count of bound parameters required (default: 1)</param>
        /// <param name="max">Maximum count of bound parameters allowed (default: parameterNames.Length)</param>
        /// <returns>The test result</returns>
        public static bool TestBound(IDictionary boundParameters, string[] parameterNames, bool not, bool and, int min, int max)
        {
            if (boundParameters == null || parameterNames == null || parameterNames.Length == 0)
                return false;

            if (and)
                min = parameterNames.Length;

            int usedCount = 0;
            foreach (string name in parameterNames)
            {
                if (boundParameters.Contains(name))
                    usedCount++;
            }

            bool test = (usedCount >= min) && (usedCount <= max);

            // XOR with Not flag: ((-not $Not) -eq $test)
            return (!not) == test;
        }
        #endregion TestBound

        #region TestFunctionInterrupt
        /// <summary>
        /// Tests whether the function interrupt flag has been set by StopFunction.
        /// Static version for PS1 wrapper use.
        /// </summary>
        /// <param name="cmdlet">The calling PSCmdlet</param>
        /// <returns>True if the interrupt flag is set</returns>
        public static bool TestFunctionInterrupt(PSCmdlet cmdlet)
        {
            if (cmdlet == null)
                return false;

            try
            {
                PSVariable interruptVar = cmdlet.SessionState.PSVariable.Get(
                    "__dbatools_interrupt_function_78Q9VPrM6999g6zo24Qn83m09XF56InEn4hFrA8Fwhu5xJrs6r"
                );
                if (interruptVar != null && interruptVar.Value is bool)
                    return (bool)interruptVar.Value;
                if (interruptVar != null && interruptVar.Value != null)
                    return true;
            }
            catch
            {
                // Variable access may fail
            }
            return false;
        }
        #endregion TestFunctionInterrupt

        #region GetErrorMessage
        /// <summary>
        /// Gets the deepest meaningful exception message from an ErrorRecord.
        /// Mirrors Get-ErrorMessage.ps1 which unwraps up to 5 levels of InnerException.
        /// </summary>
        /// <param name="record">The error record to extract the message from</param>
        /// <returns>The deepest exception message</returns>
        public static string GetErrorMessage(ErrorRecord record)
        {
            if (record == null || record.Exception == null)
                return null;

            return GetDeepestExceptionMessage(record.Exception);
        }

        /// <summary>
        /// Gets the deepest meaningful exception message by walking the InnerException chain.
        /// </summary>
        /// <param name="ex">The exception to unwrap</param>
        /// <returns>The deepest non-null message</returns>
        public static string GetDeepestExceptionMessage(Exception ex)
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
        #endregion GetErrorMessage

        #region TestPsVersion
        /// <summary>
        /// Tests the current PowerShell version against specified criteria.
        /// Mirrors Test-PsVersion.ps1 behavior.
        /// </summary>
        /// <param name="cmdlet">The calling PSCmdlet (to access PSVersionTable)</param>
        /// <param name="isVersion">Exact version to match (major.minor as float)</param>
        /// <param name="minimum">Minimum version required</param>
        /// <param name="maximum">Maximum version allowed</param>
        /// <returns>True if version matches the criteria</returns>
        public static bool TestPsVersion(PSCmdlet cmdlet, float isVersion = 0, float minimum = 0, float maximum = 0)
        {
            float detectedVersion = GetPsVersion(cmdlet);
            bool result = true;

            if (maximum > 0)
                result = detectedVersion <= maximum;
            if (minimum > 0)
                result = detectedVersion >= minimum;
            if (isVersion > 0)
                result = Math.Abs(detectedVersion - isVersion) < 0.001f;

            return result;
        }

        /// <summary>
        /// Gets the current PowerShell version as a float (major.minor).
        /// </summary>
        /// <param name="cmdlet">The PSCmdlet to access variables</param>
        /// <returns>Version as float, e.g. 5.1f</returns>
        public static float GetPsVersion(PSCmdlet cmdlet)
        {
            try
            {
                object versionTable = cmdlet.GetVariableValue("PSVersionTable");
                if (versionTable is Hashtable)
                {
                    Hashtable ht = (Hashtable)versionTable;
                    if (ht.ContainsKey("PSVersion"))
                    {
                        Version psVersion = ht["PSVersion"] as Version;
                        if (psVersion != null)
                        {
                            string versionString = String.Format("{0}.{1}", psVersion.Major, psVersion.Minor);
                            float version;
                            if (Single.TryParse(versionString, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out version))
                                return version;
                        }
                    }
                }
            }
            catch
            {
                // Fallback
            }
            return 5.1f;
        }

        /// <summary>
        /// Tests whether the current environment is Windows.
        /// Mirrors Test-Windows.ps1 behavior.
        /// </summary>
        /// <returns>True if running on Windows</returns>
        public static bool TestWindows()
        {
#if NETFRAMEWORK
            return true;
#else
            return System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows);
#endif
        }
        #endregion TestPsVersion

        #region Private Helpers
        /// <summary>
        /// Resolves the calling function name from the cmdlet context.
        /// </summary>
        private static string ResolveFunctionName(PSCmdlet cmdlet)
        {
            try
            {
                if (cmdlet.MyInvocation != null && cmdlet.MyInvocation.MyCommand != null)
                    return cmdlet.MyInvocation.MyCommand.Name;
            }
            catch
            {
                // Ignore
            }
            return "<Unknown>";
        }

        /// <summary>
        /// Resolves the calling module name from the cmdlet context.
        /// </summary>
        private static string ResolveModuleName(PSCmdlet cmdlet)
        {
            try
            {
                if (cmdlet.MyInvocation != null && cmdlet.MyInvocation.MyCommand != null)
                    return cmdlet.MyInvocation.MyCommand.ModuleName;
            }
            catch
            {
                // Ignore
            }
            return "<Unknown>";
        }
        #endregion Private Helpers
    }
}
