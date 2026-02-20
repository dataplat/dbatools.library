using System;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Disables the High Availability Disaster Recovery (HADR) service setting on SQL Server instances.
    /// Changes the WMI setting but requires a service restart to take effect. Use -Force to automatically restart.
    /// </summary>
    [Cmdlet("Disable", "DbaAgHadr", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.High)]
    public class DisableDbaAgHadrCommand : DbaBaseCmdlet
    {
        #region Parameters

        /// <summary>
        /// The target SQL Server instance or instances.
        /// </summary>
        [Parameter(Mandatory = true, ValueFromPipeline = true)]
        public DbaInstanceParameter[] SqlInstance { get; set; }

        /// <summary>
        /// Windows credential object used to connect to the target server as a different user.
        /// </summary>
        [Parameter()]
        public PSCredential Credential { get; set; }

        /// <summary>
        /// Automatically restarts both SQL Server and SQL Server Agent services to immediately apply the HADR setting change.
        /// Without this switch, the HADR disable setting is changed but requires manual service restart to take effect.
        /// </summary>
        [Parameter()]
        public SwitchParameter Force { get; set; }

        #endregion Parameters

        #region Static ScriptBlocks

        /// <summary>
        /// Tests whether the console has elevation when targeting localhost.
        /// </summary>
        private static readonly ScriptBlock _testElevationScript = ScriptBlock.Create(@"
param($instance)
Test-ElevationRequirement -ComputerName $instance
");

        /// <summary>
        /// Gets the current HADR state via WMI.
        /// </summary>
        private static readonly ScriptBlock _getWmiHadrScript = ScriptBlock.Create(@"
param($instance, $credential, $hasCred)
$params = @{ SqlInstance = $instance }
if ($hasCred) { $params['Credential'] = $credential }
Get-WmiHadr @params
");

        /// <summary>
        /// Disables HADR via Invoke-ManagedComputerCommand (sets ChangeHadrServiceSetting to 0).
        /// </summary>
        private static readonly ScriptBlock _disableHadrScript = ScriptBlock.Create(@"
param($computerFullName, $credential, $hasCred, $instanceName)
$scriptBlock = {
    $instance = $args[0]
    $sqlService = $wmi.Services | Where-Object DisplayName -eq ""SQL Server ($instance)""
    $sqlService.ChangeHadrServiceSetting(0)
}
$params = @{
    ComputerName  = $computerFullName
    ScriptBlock   = $scriptBlock
    ArgumentList  = $instanceName
}
if ($hasCred) { $params['Credential'] = $credential }
Invoke-ManagedComputerCommand @params
");

        /// <summary>
        /// Stops SQL Server Agent and Engine services.
        /// </summary>
        private static readonly ScriptBlock _stopServicesScript = ScriptBlock.Create(@"
param($computerFullName, $instanceName)
$null = Stop-DbaService -ComputerName $computerFullName -InstanceName $instanceName -Type Agent, Engine
");

        /// <summary>
        /// Starts SQL Server Agent and Engine services.
        /// </summary>
        private static readonly ScriptBlock _startServicesScript = ScriptBlock.Create(@"
param($computerFullName, $instanceName)
$null = Start-DbaService -ComputerName $computerFullName -InstanceName $instanceName -Type Agent, Engine
");

        #endregion Static ScriptBlocks

        /// <summary>
        /// Overrides ConfirmPreference when Force is specified.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            if (Force.IsPresent)
            {
                // Match PS1: if ($Force) { $ConfirmPreference = 'none' }
                SessionState.PSVariable.Set("ConfirmPreference", "None");
            }
        }

        /// <summary>
        /// Processes each SQL Server instance to disable the HADR setting.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (TestFunctionInterrupt())
                return;

            foreach (DbaInstanceParameter instance in SqlInstance)
            {
                ProcessInstance(instance);
            }
        }

        /// <summary>
        /// Processes a single instance to disable HADR.
        /// </summary>
        private void ProcessInstance(DbaInstanceParameter instance)
        {
            string computer = instance.ComputerName;
            string instanceName = instance.InstanceName;

            // Test elevation requirement for localhost
            if (!TestElevation(instance))
            {
                return;
            }

            // Check current HADR state
            PSObject currentState;
            try
            {
                WriteMessageVerbose(String.Format("Checking current Hadr setting for {0}", computer));
                currentState = GetWmiHadr(instance);
            }
            catch (Exception ex)
            {
                StopFunction(
                    String.Format("Failure to pull current state of Hadr setting on {0}", computer),
                    errorRecord: new ErrorRecord(ex, "DisableDbaAgHadr_GetState", ErrorCategory.ConnectionError, instance),
                    target: instance, isContinue: true, category: ErrorCategory.ConnectionError);
                TestFunctionInterrupt();
                return;
            }

            string isHadrEnabled = currentState != null
                ? (GetPropertyString(currentState, "IsHadrEnabled") ?? "Unknown")
                : "Unknown";
            WriteMessageAtLevel(
                String.Format("{0} Hadr current value: {1}", instance, isHadrEnabled),
                MessageLevel.InternalComment, null);

            // Disable HADR via WMI
            if (ShouldProcess(instance.ToString(),
                String.Format("Changing Hadr from {0} to 0 for {1}", isHadrEnabled, instance)))
            {
                try
                {
                    InvokeCommand.InvokeScript(true, _disableHadrScript, null,
                        new object[] { computer, Credential, Credential != null, instanceName });
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("Failure on {0} | This may be because AlwaysOn Availability Groups feature requires " +
                            "the x86(non-WOW) or x64 Enterprise Edition of SQL Server 2012 (or later version) running on " +
                            "Windows Server 2008 (or later version) with WSFC hotfix KB 2494036 installed.", instance.FullName),
                        errorRecord: new ErrorRecord(ex, "DisableDbaAgHadr_DisableHadr", ErrorCategory.InvalidOperation, instance),
                        target: instance, isContinue: true);
                    TestFunctionInterrupt();
                    return;
                }
            }

            // Force restart services
            if (TestBound("Force"))
            {
                if (ShouldProcess(instance.ToString(),
                    String.Format("Force provided, restarting Engine and Agent service for {0} on {1}", instance, computer)))
                {
                    try
                    {
                        InvokeCommand.InvokeScript(true, _stopServicesScript, null,
                            new object[] { computer, instanceName });
                        InvokeCommand.InvokeScript(true, _startServicesScript, null,
                            new object[] { computer, instanceName });
                    }
                    catch (Exception ex)
                    {
                        StopFunction(
                            String.Format("Issue restarting {0}", instance),
                            errorRecord: new ErrorRecord(ex, "DisableDbaAgHadr_Restart", ErrorCategory.InvalidOperation, instance),
                            target: instance, isContinue: true);
                        TestFunctionInterrupt();
                        return;
                    }
                }
            }

            // Get new state
            PSObject newState = null;
            try
            {
                newState = GetWmiHadr(instance);
            }
            catch (Exception)
            {
                WriteMessageWarning(String.Format("Could not retrieve updated HADR state for {0}. The change may still be pending a service restart.", instance));
            }

            // Warn if restart is needed
            if (TestBoundNot("Force"))
            {
                WriteMessageWarning("You must restart the SQL Server for it to take effect.");
            }

            // Output result
            PSObject output = new PSObject();
            string compName = newState != null ? GetPropertyString(newState, "ComputerName") : computer;
            string instName = newState != null ? GetPropertyString(newState, "InstanceName") : instanceName;
            string sqlInst = newState != null ? GetPropertyString(newState, "SqlInstance") : instance.FullName;

            output.Properties.Add(new PSNoteProperty("ComputerName", compName));
            output.Properties.Add(new PSNoteProperty("InstanceName", instName));
            output.Properties.Add(new PSNoteProperty("SqlInstance", sqlInst));
            output.Properties.Add(new PSNoteProperty("IsHadrEnabled", false));
            WriteObject(output);
        }

        #region Helpers

        /// <summary>
        /// Tests elevation requirement for the given instance.
        /// Returns false if elevation is required but not available.
        /// </summary>
        private bool TestElevation(DbaInstanceParameter instance)
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, _testElevationScript, null,
                    new object[] { instance });

                // If no result returned, assume check passed (remote host)
                if (results == null || results.Count == 0 || results[0] == null)
                    return true;

                object val = results[0].BaseObject ?? results[0];
                if (val is bool boolVal)
                    return boolVal;

                // Non-bool result: treat as pass
                return true;
            }
            catch (Exception)
            {
                // Test-ElevationRequirement calls Stop-Function internally when not elevated,
                // which throws. The error is already reported to the user via that path.
            }
            return false;
        }

        /// <summary>
        /// Gets the current HADR state from WMI.
        /// </summary>
        private PSObject GetWmiHadr(DbaInstanceParameter instance)
        {
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, _getWmiHadrScript, null,
                new object[] { instance, Credential, Credential != null });

            if (results != null && results.Count > 0 && results[0] != null)
                return results[0] as PSObject ?? PSObject.AsPSObject(results[0]);
            return null;
        }

        #endregion Helpers
    }
}
