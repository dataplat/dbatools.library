using System;
using System.Management.Automation;

namespace Dataplat.Dbatools.Internal
{
    /// <summary>
    /// Static system utility helpers mirroring PS1 functions like
    /// Write-ProgressHelper, Select-DefaultView, Test-ElevationRequirement,
    /// Test-DbaDeprecation, etc.
    /// </summary>
    public static class SystemHelpers
    {
        /// <summary>
        /// Helper to simplify Write-Progress calls with step tracking.
        /// Mirrors Write-ProgressHelper.ps1.
        /// </summary>
        /// <param name="cmdlet">The PSCmdlet for Write-Progress</param>
        /// <param name="activity">The activity description</param>
        /// <param name="stepNumber">Current step number</param>
        /// <param name="totalSteps">Total number of steps</param>
        /// <param name="message">Status message</param>
        /// <param name="id">Progress bar ID</param>
        public static void WriteProgressHelper(PSCmdlet cmdlet, string activity, int stepNumber, int totalSteps, string message, int id = 1)
        {
            if (cmdlet == null || totalSteps <= 0)
                return;

            int percentComplete = (int)((double)stepNumber / totalSteps * 100);
            if (percentComplete > 100)
                percentComplete = 100;
            if (percentComplete < 0)
                percentComplete = 0;

            ProgressRecord record = new ProgressRecord(id, activity, message);
            record.PercentComplete = percentComplete;
            cmdlet.WriteProgress(record);
        }

        /// <summary>
        /// Tests whether the current PowerShell session has elevated (admin) privileges.
        /// Mirrors Test-ElevationRequirement.ps1.
        /// </summary>
        /// <returns>True if running as administrator</returns>
        public static bool TestElevation()
        {
            // Only relevant on Windows
            if (!FlowControl.TestWindows())
                return true;

            try
            {
#if NETFRAMEWORK
                var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
#else
                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.Windows))
                {
                    var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                    var principal = new System.Security.Principal.WindowsPrincipal(identity);
                    return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                }
                // On non-Windows, check if effective user ID is 0 (root)
                return false;
#endif
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if a command is deprecated and writes an appropriate warning.
        /// Mirrors Test-DbaDeprecation.ps1.
        /// </summary>
        /// <param name="cmdlet">The PSCmdlet for messaging</param>
        /// <param name="deprecatedOn">Date the command was deprecated</param>
        /// <param name="enableException">Whether to throw</param>
        /// <param name="customMessage">Optional custom deprecation message</param>
        /// <returns>True if the command is deprecated</returns>
        public static bool TestDeprecation(PSCmdlet cmdlet, DateTime deprecatedOn, bool enableException = false, string customMessage = null)
        {
            if (cmdlet == null)
                return false;

            string functionName = cmdlet.MyInvocation.MyCommand.Name;
            string message = !String.IsNullOrEmpty(customMessage)
                ? customMessage
                : String.Format("The command '{0}' has been deprecated on {1:yyyy-MM-dd}. Please use the recommended alternative.", functionName, deprecatedOn);

            if (enableException)
            {
                cmdlet.ThrowTerminatingError(
                    new ErrorRecord(
                        new InvalidOperationException(message),
                        String.Format("dbatools_{0}_deprecated", functionName),
                        ErrorCategory.InvalidOperation,
                        null
                    )
                );
            }
            else
            {
                cmdlet.WriteWarning(message);
            }

            return true;
        }

        /// <summary>
        /// Applies a default display property set to a PSObject.
        /// Mirrors Select-DefaultView.ps1 - sets which properties are shown by default.
        /// </summary>
        /// <param name="inputObject">The PSObject to modify</param>
        /// <param name="propertyNames">Properties to display by default</param>
        /// <param name="typeName">Optional custom typename to assign</param>
        /// <returns>The modified PSObject</returns>
        public static PSObject SelectDefaultView(PSObject inputObject, string[] propertyNames, string typeName = null)
        {
            if (inputObject == null)
                return null;

            if (propertyNames != null && propertyNames.Length > 0)
            {
                PSPropertySet displaySet = new PSPropertySet("DefaultDisplayPropertySet", propertyNames);
                PSMemberSet standardMembers = new PSMemberSet("PSStandardMembers", new PSMemberInfo[] { displaySet });
                try
                {
                    inputObject.Members.Remove("PSStandardMembers");
                }
                catch
                {
                    // May not exist
                }
                inputObject.Members.Add(standardMembers);
            }

            if (!String.IsNullOrEmpty(typeName))
            {
                inputObject.TypeNames.Insert(0, typeName);
            }

            return inputObject;
        }
    }
}
