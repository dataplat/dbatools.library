using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Modifies availability group configuration settings including DTC support, backup preferences,
    /// failover conditions, and health check timeouts on SQL Server instances.
    /// </summary>
    [Cmdlet("Set", "DbaAvailabilityGroup", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Medium)]
    public class SetDbaAvailabilityGroupCommand : DbaBaseCmdlet
    {
        #region Parameters

        /// <summary>
        /// The target SQL Server instance or instances. Server version must be SQL Server version 2012 or higher.
        /// </summary>
        [Parameter()]
        public DbaInstanceParameter[] SqlInstance { get; set; }

        /// <summary>
        /// Login to the target instance using alternative credentials.
        /// </summary>
        [Parameter()]
        [Alias("Credential", "Cred")]
        public PSCredential SqlCredential { get; set; }

        /// <summary>
        /// Specifies the name(s) of specific availability groups to modify.
        /// </summary>
        [Parameter()]
        public string[] AvailabilityGroup { get; set; }

        /// <summary>
        /// Modifies configuration settings for every availability group on the target SQL Server instance.
        /// </summary>
        [Parameter()]
        public SwitchParameter AllAvailabilityGroups { get; set; }

        /// <summary>
        /// Enables or disables Distributed Transaction Coordinator (DTC) support for the availability group.
        /// </summary>
        [Parameter()]
        public SwitchParameter DtcSupportEnabled { get; set; }

        /// <summary>
        /// Specifies the clustering technology used by the availability group. Only supported in SQL Server 2017 and above.
        /// </summary>
        [Parameter()]
        [ValidateSet("External", "Wsfc", "None")]
        public string ClusterType { get; set; }

        /// <summary>
        /// Controls which replica should be preferred for automated backup operations.
        /// </summary>
        [Parameter()]
        [ValidateSet("None", "Primary", "Secondary", "SecondaryOnly")]
        public string AutomatedBackupPreference { get; set; }

        /// <summary>
        /// Sets the sensitivity level for automatic failover conditions in the availability group.
        /// </summary>
        [Parameter()]
        [ValidateSet("OnAnyQualifiedFailureCondition", "OnCriticalServerErrors", "OnModerateServerErrors", "OnServerDown", "OnServerUnresponsive")]
        public string FailureConditionLevel { get; set; }

        /// <summary>
        /// Sets the timeout in milliseconds for health check responses from sp_server_diagnostics.
        /// </summary>
        [Parameter()]
        public int HealthCheckTimeout { get; set; }

        /// <summary>
        /// Configures the availability group as a Basic AG for Standard Edition licensing.
        /// </summary>
        [Parameter()]
        public SwitchParameter BasicAvailabilityGroup { get; set; }

        /// <summary>
        /// Enables database-level health monitoring that can trigger automatic failovers.
        /// </summary>
        [Parameter()]
        public SwitchParameter DatabaseHealthTrigger { get; set; }

        /// <summary>
        /// Configures the availability group as a Distributed AG that spans multiple clusters.
        /// </summary>
        [Parameter()]
        public SwitchParameter IsDistributedAvailabilityGroup { get; set; }

        /// <summary>
        /// Specifies connection options for TDS 8.0 support in SQL Server 2025 and above.
        /// </summary>
        [Parameter()]
        public string ClusterConnectionOption { get; set; }

        /// <summary>
        /// Accepts availability group objects from Get-DbaAvailabilityGroup for pipeline operations.
        /// Type is object[] because SMO types are loaded dynamically at runtime.
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        public object[] InputObject { get; set; }

        #endregion Parameters

        #region Static ScriptBlocks

        /// <summary>
        /// Script block for calling Get-DbaAvailabilityGroup.
        /// </summary>
        private static readonly ScriptBlock _getAgScript = ScriptBlock.Create(@"
param($si, $sc, $ag, $hasCred, $ee)
$params = @{ SqlInstance = $si }
if ($hasCred) { $params['SqlCredential'] = $sc }
if ($ag) { $params['AvailabilityGroup'] = $ag }
$params['EnableException'] = $ee
Get-DbaAvailabilityGroup @params
");

        /// <summary>
        /// Script block for setting properties and calling Alter() on an AG.
        /// </summary>
        private static readonly ScriptBlock _setPropertiesAndAlterScript = ScriptBlock.Create(@"
param($ag, $props)
if ($props.ContainsKey('AutomatedBackupPreference')) { $ag.AutomatedBackupPreference = $props['AutomatedBackupPreference'] }
if ($props.ContainsKey('BasicAvailabilityGroup')) { $ag.BasicAvailabilityGroup = $props['BasicAvailabilityGroup'] }
if ($props.ContainsKey('ClusterType')) { $ag.ClusterType = $props['ClusterType'] }
if ($props.ContainsKey('DatabaseHealthTrigger')) { $ag.DatabaseHealthTrigger = $props['DatabaseHealthTrigger'] }
if ($props.ContainsKey('DtcSupportEnabled')) { $ag.DtcSupportEnabled = $props['DtcSupportEnabled'] }
if ($props.ContainsKey('FailureConditionLevel')) { $ag.FailureConditionLevel = $props['FailureConditionLevel'] }
if ($props.ContainsKey('HealthCheckTimeout')) { $ag.HealthCheckTimeout = $props['HealthCheckTimeout'] }
if ($props.ContainsKey('IsDistributedAvailabilityGroup')) { $ag.IsDistributedAvailabilityGroup = $props['IsDistributedAvailabilityGroup'] }
if ($props.ContainsKey('ClusterConnectionOptions')) { $ag.ClusterConnectionOptions = $props['ClusterConnectionOptions'] }
$ag.Alter()
");

        #endregion Static ScriptBlocks

        /// <summary>
        /// Collects InputObject items across pipeline invocations.
        /// </summary>
        private List<object> _inputObjects = new List<object>();

        /// <summary>
        /// Tracks whether any InputObject was received via pipeline.
        /// </summary>
        private bool _receivedPipelineInput;

        /// <summary>
        /// Validates parameters at the start of processing.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            // Validate: either SqlInstance or InputObject must be provided
            if (!MyInvocation.ExpectingInput && TestBoundNot("SqlInstance", "InputObject"))
            {
                StopFunction("You must supply either -SqlInstance or an Input Object");
                TestFunctionInterrupt();
                return;
            }

            // Validate: AvailabilityGroup or AllAvailabilityGroups required when using SqlInstance
            if (TestBound("SqlInstance") && TestBoundNot("AvailabilityGroup", "AllAvailabilityGroups"))
            {
                StopFunction("You must specify AllAvailabilityGroups groups or AvailabilityGroups when using the SqlInstance parameter.");
                TestFunctionInterrupt();
                return;
            }
        }

        /// <summary>
        /// Collects pipeline InputObject items.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (TestFunctionInterrupt())
                return;

            if (InputObject != null)
            {
                _receivedPipelineInput = true;
                foreach (object obj in InputObject)
                {
                    if (obj != null)
                    {
                        _inputObjects.Add(obj);
                    }
                }
            }
        }

        /// <summary>
        /// Resolves AGs and applies modifications.
        /// </summary>
        protected override void EndProcessing()
        {
            if (TestFunctionInterrupt())
                return;

            // Deferred validation: if pipeline was expected but nothing arrived and SqlInstance wasn't bound
            if (!_receivedPipelineInput && _inputObjects.Count == 0 && !TestBound("SqlInstance"))
            {
                StopFunction("You must supply either -SqlInstance or an Input Object");
                return;
            }

            // If SqlInstance is bound, resolve AGs via Get-DbaAvailabilityGroup and append
            // to any piped InputObject items. This matches the PS1 "$InputObject += Get-DbaAvailabilityGroup ..."
            // pattern, which unions both sources regardless of whether InputObject was also provided.
            if (TestBound("SqlInstance"))
            {
                Collection<PSObject> resolved = GetAvailabilityGroups();
                if (resolved != null)
                {
                    foreach (PSObject item in resolved)
                    {
                        if (item != null)
                            _inputObjects.Add(item);
                    }
                }
            }

            // Build the properties hashtable once — bound parameters don't change between AG iterations
            Hashtable props = BuildPropertiesHashtable();

            // Process each AG
            foreach (object agObj in _inputObjects)
            {
                ProcessAvailabilityGroup(agObj, props);
            }
        }

        /// <summary>
        /// Processes a single availability group object: sets properties and calls Alter().
        /// </summary>
        private void ProcessAvailabilityGroup(object agObj, Hashtable props)
        {
            PSObject ag = PSObject.AsPSObject(agObj);
            string agName = GetPropertyString(ag, "Name");
            string serverName = GetServerName(agObj);

            string target = serverName ?? "Server";
            string action = String.Format("Setting properties on {0}", agName ?? "availability group");

            if (ShouldProcess(target, action))
            {
                try
                {
                    // Handle ClusterConnectionOption - requires SQL Server 2025+ (version 17)
                    if (TestBound("ClusterConnectionOption"))
                    {
                        int versionMajor = GetVersionMajor(agObj);
                        if (versionMajor >= 17)
                        {
                            props["ClusterConnectionOptions"] = ClusterConnectionOption;
                        }
                        else
                        {
                            WriteMessageAtLevel(
                                String.Format("ClusterConnectionOption is only supported in SQL Server 2025 and above. Skipping this setting on {0}.", serverName ?? "instance"),
                                MessageLevel.Warning, null);
                        }
                    }

                    // Set properties and call Alter()
                    SetPropertiesAndAlter(agObj, props);
                    WriteObject(agObj);
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("Failed to set properties on availability group {0} on {1}",
                            agName ?? "unknown", serverName ?? "unknown"),
                        exception: ex,
                        target: agObj,
                        isContinue: true);
                    // No TestFunctionInterrupt() here: isContinue=true means continue
                    // to the next AG in the loop (PS1 -Continue semantics)
                }
            }
        }

        #region Helpers

        /// <summary>
        /// Builds a Hashtable of properties to set on the AG, based on bound parameters.
        /// </summary>
        internal Hashtable BuildPropertiesHashtable()
        {
            Hashtable ht = new Hashtable();

            if (TestBound("AutomatedBackupPreference"))
                ht["AutomatedBackupPreference"] = AutomatedBackupPreference;
            if (TestBound("BasicAvailabilityGroup"))
                ht["BasicAvailabilityGroup"] = BasicAvailabilityGroup.ToBool();
            if (TestBound("ClusterType"))
                ht["ClusterType"] = ClusterType;
            if (TestBound("DatabaseHealthTrigger"))
                ht["DatabaseHealthTrigger"] = DatabaseHealthTrigger.ToBool();
            if (TestBound("DtcSupportEnabled"))
                ht["DtcSupportEnabled"] = DtcSupportEnabled.ToBool();
            if (TestBound("FailureConditionLevel"))
                ht["FailureConditionLevel"] = FailureConditionLevel;
            if (TestBound("HealthCheckTimeout"))
                ht["HealthCheckTimeout"] = HealthCheckTimeout;
            if (TestBound("IsDistributedAvailabilityGroup"))
                ht["IsDistributedAvailabilityGroup"] = IsDistributedAvailabilityGroup.ToBool();

            return ht;
        }

        /// <summary>
        /// Calls Get-DbaAvailabilityGroup to resolve AG objects from SqlInstance parameters.
        /// </summary>
        private Collection<PSObject> GetAvailabilityGroups()
        {
            object[] args = new object[]
            {
                SqlInstance,
                SqlCredential,
                AvailabilityGroup,
                SqlCredential != null,
                EnableException.ToBool()
            };

            try
            {
                return InvokeCommand.InvokeScript(false, _getAgScript, null, args);
            }
            catch (Exception ex)
            {
                StopFunction(
                    String.Format("Failed to get availability groups: {0}", ex.Message),
                    exception: ex);
                TestFunctionInterrupt();
                return null;
            }
        }

        /// <summary>
        /// Gets the server name from an AG's parent (ag.Parent.Name).
        /// </summary>
        internal static string GetServerName(object agObj)
        {
            try
            {
                PSObject ag = PSObject.AsPSObject(agObj);
                PSObject parent = GetPropertyObject(ag, "Parent");
                if (parent != null)
                    return GetPropertyString(parent, "Name");
            }
            catch (Exception)
            {
                // Best effort
            }
            return null;
        }

        /// <summary>
        /// Gets the server version major from an AG's parent (ag.Parent.VersionMajor).
        /// </summary>
        internal static int GetVersionMajor(object agObj)
        {
            try
            {
                PSObject ag = PSObject.AsPSObject(agObj);
                PSObject parent = GetPropertyObject(ag, "Parent");
                if (parent != null)
                {
                    object versionVal = GetPropertyValue(parent, "VersionMajor");
                    if (versionVal is int intVal)
                        return intVal;
                    int parsed;
                    if (versionVal != null && int.TryParse(versionVal.ToString(), out parsed))
                        return parsed;
                }
            }
            catch (Exception)
            {
                // Best effort
            }
            return 0;
        }

        /// <summary>
        /// Sets properties on an AG and calls Alter() via PowerShell script.
        /// </summary>
        private void SetPropertiesAndAlter(object agObj, Hashtable props)
        {
            InvokeCommand.InvokeScript(
                false, _setPropertiesAndAlterScript, null,
                new object[] { agObj, props });
        }

        /// <summary>
        /// Gets a string property value from a PSObject.
        /// </summary>
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
                // Property may not exist
            }
            return null;
        }

        /// <summary>
        /// Gets a raw property value from a PSObject.
        /// </summary>
        internal static object GetPropertyValue(PSObject obj, string propertyName)
        {
            if (obj == null)
                return null;
            try
            {
                PSPropertyInfo prop = obj.Properties[propertyName];
                if (prop != null)
                    return prop.Value;
            }
            catch (Exception)
            {
                // Property may not exist
            }
            return null;
        }

        /// <summary>
        /// Gets an object property value from a PSObject wrapped as PSObject.
        /// </summary>
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
                // Property may not exist
            }
            return null;
        }

        #endregion Helpers
    }
}
