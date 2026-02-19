using System;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Enables the High Availability Disaster Recovery (HADR) service setting on SQL Server instances.
    /// This is a prerequisite before creating Availability Groups. Requires a service restart to take effect.
    /// Use -Force to automatically restart services.
    /// </summary>
    [Cmdlet("Enable", "DbaAgHadr", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.High)]
    public class EnableDbaAgHadrCommand : DbaBaseCmdlet
    {
        #region Parameters

        /// <summary>
        /// The target SQL Server instance or instances.
        /// </summary>
        [Parameter(Mandatory = true, ValueFromPipeline = true)]
        public DbaInstanceParameter[] SqlInstance { get; set; }

        /// <summary>
        /// Windows credential object used to connect to the target server with different authentication context.
        /// Required when the current user lacks administrative privileges on the SQL Server host.
        /// </summary>
        [Parameter()]
        public PSCredential Credential { get; set; }

        /// <summary>
        /// Automatically restarts the SQL Server Database Engine and SQL Server Agent services
        /// to immediately apply the HADR setting change. Without this parameter, the HADR setting
        /// change requires a manual service restart before Availability Groups can be created.
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
        /// Enables HADR via Invoke-ManagedComputerCommand (sets ChangeHadrServiceSetting to 1).
        /// </summary>
        private static readonly ScriptBlock _enableHadrScript = ScriptBlock.Create(@"
param($computerFullName, $credential, $hasCred, $instanceName)
$scriptBlock = {
    $instance = $args[0]
    $sqlService = $wmi.Services | Where-Object DisplayName -eq ""SQL Server ($instance)""
    $sqlService.ChangeHadrServiceSetting(1)
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
        /// Processes each SQL Server instance to enable the HADR setting.
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
        /// Processes a single instance to enable HADR.
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
                    errorRecord: new ErrorRecord(ex, "EnableDbaAgHadr_GetState", ErrorCategory.ConnectionError, instance),
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

            // Enable HADR via WMI
            if (ShouldProcess(instance.ToString(),
                String.Format("Changing Hadr from {0} to 1 for {1}", isHadrEnabled, instance)))
            {
                try
                {
                    InvokeCommand.InvokeScript(false, _enableHadrScript, null,
                        new object[] { computer, Credential, Credential != null, instanceName });
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("Failed to enable HADR on {0} | AlwaysOn Availability Groups requires Enterprise Edition " +
                            "of SQL Server 2012 or later running on Windows Server 2012 or later with Windows Server Failover " +
                            "Clustering (WSFC) configured.", instance.FullName),
                        errorRecord: new ErrorRecord(ex, "EnableDbaAgHadr_EnableHadr", ErrorCategory.InvalidOperation, instance),
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
                        InvokeCommand.InvokeScript(false, _stopServicesScript, null,
                            new object[] { computer, instanceName });
                        InvokeCommand.InvokeScript(false, _startServicesScript, null,
                            new object[] { computer, instanceName });
                    }
                    catch (Exception ex)
                    {
                        StopFunction(
                            String.Format("Issue restarting {0}", instance),
                            errorRecord: new ErrorRecord(ex, "EnableDbaAgHadr_Restart", ErrorCategory.InvalidOperation, instance),
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
            output.Properties.Add(new PSNoteProperty("IsHadrEnabled", true));
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
