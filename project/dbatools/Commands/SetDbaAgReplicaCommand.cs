using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Modifies configuration properties of existing availability group replicas such as
    /// availability mode, failover behavior, backup priority, and read-only routing settings.
    /// </summary>
    [Cmdlet("Set", "DbaAgReplica", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Medium)]
    public class SetDbaAgReplicaCommand : DbaBaseCmdlet
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
        /// Specifies the name of the availability group that contains the replica to modify.
        /// </summary>
        [Parameter()]
        public string AvailabilityGroup { get; set; }

        /// <summary>
        /// Specifies the name of the availability group replica to modify.
        /// </summary>
        [Parameter()]
        public string Replica { get; set; }

        /// <summary>
        /// Controls the data synchronization mode between primary and secondary replicas.
        /// </summary>
        [Parameter()]
        [ValidateSet("AsynchronousCommit", "SynchronousCommit")]
        public string AvailabilityMode { get; set; }

        /// <summary>
        /// Determines whether the replica can automatically failover when the primary becomes unavailable.
        /// </summary>
        [Parameter()]
        [ValidateSet("Automatic", "Manual", "External")]
        public string FailoverMode { get; set; }

        /// <summary>
        /// Sets the backup priority for this replica on a scale of 0-100.
        /// </summary>
        [Parameter()]
        public int BackupPriority { get; set; }

        /// <summary>
        /// Controls what types of connections are allowed when this replica is the primary.
        /// </summary>
        [Parameter()]
        [ValidateSet("AllowAllConnections", "AllowReadWriteConnections")]
        public string ConnectionModeInPrimaryRole { get; set; }

        /// <summary>
        /// Determines connection access when this replica is secondary.
        /// </summary>
        [Parameter()]
        [ValidateSet("AllowAllConnections", "AllowNoConnections", "AllowReadIntentConnectionsOnly", "No", "Read-intent only", "Yes")]
        public string ConnectionModeInSecondaryRole { get; set; }

        /// <summary>
        /// Controls the database initialization method for new databases added to the availability group.
        /// </summary>
        [Parameter()]
        [ValidateSet("Automatic", "Manual")]
        public string SeedingMode { get; set; }

        /// <summary>
        /// Sets the timeout period in seconds for detecting communication failures between replicas.
        /// </summary>
        [Parameter()]
        public int SessionTimeout { get; set; }

        /// <summary>
        /// Specifies the URL endpoint used for data mirroring communication between replicas.
        /// </summary>
        [Parameter()]
        public string EndpointUrl { get; set; }

        /// <summary>
        /// Specifies the connection string used by the listener to route read-only connections to this secondary replica.
        /// </summary>
        [Parameter()]
        public string ReadonlyRoutingConnectionUrl { get; set; }

        /// <summary>
        /// Defines the ordered list of secondary replicas that should receive read-only connections.
        /// Accepts arrays for priority-based routing or nested arrays for load-balanced routing.
        /// </summary>
        [Parameter()]
        public object[] ReadOnlyRoutingList { get; set; }

        /// <summary>
        /// Accepts availability group replica objects from Get-DbaAgReplica for pipeline operations.
        /// Type is object[] because SMO types are loaded dynamically at runtime.
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        public object[] InputObject { get; set; }

        #endregion Parameters

        #region Static ScriptBlocks

        /// <summary>
        /// Script block for calling Get-DbaAgReplica.
        /// </summary>
        private static readonly ScriptBlock _getAgReplicaScript = ScriptBlock.Create(@"
param($si, $sc, $ag, $replica, $hasCred, $ee)
$params = @{ SqlInstance = $si; AvailabilityGroup = $ag; Replica = $replica }
if ($hasCred) { $params['SqlCredential'] = $sc }
$params['EnableException'] = $ee
Get-DbaAgReplica @params
");

        /// <summary>
        /// Script block for getting the server name from a replica's parent.
        /// </summary>
        private static readonly ScriptBlock _getServerNameScript = ScriptBlock.Create(@"
param($replica)
$replica.Parent.Parent.Name
");

        /// <summary>
        /// Script block for setting simple properties and calling Alter() on a replica.
        /// </summary>
        private static readonly ScriptBlock _setPropertiesAndAlterScript = ScriptBlock.Create(@"
param($replica, $props)
if ($props.ContainsKey('EndpointUrl')) { $replica.EndpointUrl = $props['EndpointUrl'] }
if ($props.ContainsKey('FailoverMode')) { $replica.FailoverMode = [Microsoft.SqlServer.Management.Smo.AvailabilityReplicaFailoverMode]::($props['FailoverMode']) }
if ($props.ContainsKey('AvailabilityMode')) { $replica.AvailabilityMode = [Microsoft.SqlServer.Management.Smo.AvailabilityReplicaAvailabilityMode]::($props['AvailabilityMode']) }
if ($props.ContainsKey('ConnectionModeInPrimaryRole')) { $replica.ConnectionModeInPrimaryRole = [Microsoft.SqlServer.Management.Smo.AvailabilityReplicaConnectionModeInPrimaryRole]::($props['ConnectionModeInPrimaryRole']) }
if ($props.ContainsKey('ConnectionModeInSecondaryRole')) { $replica.ConnectionModeInSecondaryRole = [Microsoft.SqlServer.Management.Smo.AvailabilityReplicaConnectionModeInSecondaryRole]::($props['ConnectionModeInSecondaryRole']) }
if ($props.ContainsKey('BackupPriority')) { $replica.BackupPriority = $props['BackupPriority'] }
if ($props.ContainsKey('ReadonlyRoutingConnectionUrl')) { $replica.ReadonlyRoutingConnectionUrl = $props['ReadonlyRoutingConnectionUrl'] }
if ($props.ContainsKey('SeedingMode')) { $replica.SeedingMode = [Microsoft.SqlServer.Management.Smo.AvailabilityReplicaSeedingMode]::($props['SeedingMode']) }
if ($props.ContainsKey('SessionTimeout')) { $replica.SessionTimeout = $props['SessionTimeout'] }
$replica.Alter()
");

        /// <summary>
        /// Script block for setting the read-only routing list on a replica.
        /// Handles both simple ordered and load-balanced routing lists.
        /// </summary>
        private static readonly ScriptBlock _setRoutingListScript = ScriptBlock.Create(@"
param($replica, $routingList, $isLoadBalanced)
# NOTE: This script mutates the in-memory SMO object but does NOT call Alter().
# The caller must call Alter() separately (done by _setPropertiesAndAlterScript).
$rorl = New-Object System.Collections.Generic.List[System.Collections.Generic.IList[string]]
if ($isLoadBalanced) {
    foreach ($rolist in $routingList) {
        $null = $rorl.Add([System.Collections.Generic.List[string]] $rolist)
    }
} else {
    foreach ($server in $routingList) {
        $serverList = New-Object System.Collections.Generic.List[string]
        $null = $serverList.Add([string]$server)
        $null = $rorl.Add($serverList)
    }
}
$null = $replica.SetLoadBalancedReadOnlyRoutingList($rorl)
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
        /// The normalized ConnectionModeInSecondaryRole value after alias mapping.
        /// </summary>
        private string _normalizedSecondaryMode;

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

            // Validate: AvailabilityGroup and Replica required when using SqlInstance without InputObject
            if (TestBound("SqlInstance") && TestBoundNot("InputObject"))
            {
                if (!TestBound("AvailabilityGroup") || !TestBound("Replica"))
                {
                    StopFunction("You must specify an AvailabilityGroup and replica or pipe in an availability group to continue.");
                    TestFunctionInterrupt();
                    return;
                }
            }

            // Normalize ConnectionModeInSecondaryRole aliases
            if (TestBound("ConnectionModeInSecondaryRole"))
            {
                _normalizedSecondaryMode = NormalizeSecondaryConnectionMode(ConnectionModeInSecondaryRole);
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
                        // Preserve PSObject wrapper so NoteProperties from Get-DbaAgReplica
                        // (AvailabilityGroup, ComputerName, etc.) survive through to output
                        _inputObjects.Add(obj);
                    }
                }
            }
        }

        /// <summary>
        /// Resolves replicas and applies modifications.
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

            // If no InputObject provided but SqlInstance is, resolve via Get-DbaAgReplica
            // (AvailabilityGroup+Replica validation was already done in BeginProcessing)
            if (_inputObjects.Count == 0 && TestBound("SqlInstance"))
            {
                Collection<PSObject> resolved = GetAgReplicas();
                if (resolved != null)
                {
                    foreach (PSObject item in resolved)
                    {
                        if (item != null)
                            _inputObjects.Add((object)item);
                    }
                }
            }
            else if (TestBound("SqlInstance") && _inputObjects.Count > 0)
            {
                // Both SqlInstance and InputObject provided - also resolve SqlInstance replicas
                Collection<PSObject> resolved = GetAgReplicas();
                if (resolved != null)
                {
                    foreach (PSObject item in resolved)
                    {
                        if (item != null)
                            _inputObjects.Add((object)item);
                    }
                }
            }

            // Process each replica (StopFunction with isContinue=true means we
            // continue to the next replica on failure, matching PS1 -Continue behavior)
            foreach (object replicaObj in _inputObjects)
            {
                ProcessReplica(replicaObj);
            }
        }

        /// <summary>
        /// Processes a single replica object: sets properties and calls Alter().
        /// </summary>
        private void ProcessReplica(object replicaObj)
        {
            PSObject replica = PSObject.AsPSObject(replicaObj);
            string replicaName = GetPropertyString(replica, "Name");
            string serverName = GetServerName(replicaObj);

            string target = serverName ?? "Server";
            string action = String.Format("Modifying replica for {0}", replicaName ?? "replica");

            if (ShouldProcess(target, action))
            {
                try
                {
                    // Set the read-only routing list first (before Alter)
                    if (TestBound("ReadOnlyRoutingList") && ReadOnlyRoutingList != null)
                    {
                        bool isLoadBalanced = IsLoadBalancedRoutingList(ReadOnlyRoutingList);
                        SetRoutingList(replicaObj, isLoadBalanced);
                    }

                    // Build properties hashtable and apply + Alter
                    System.Collections.Hashtable props = BuildPropertiesHashtable();
                    if (TestBound("SessionTimeout") && SessionTimeout < 10)
                    {
                        WriteMessageAtLevel(
                            "We recommend that you keep the time-out period at 10 seconds or greater. Setting the value to less than 10 seconds creates the possibility of a heavily loaded system missing pings and falsely declaring failure. Please see sqlps.io/agrec for more information.",
                            MessageLevel.Warning, null);
                    }

                    SetPropertiesAndAlter(replicaObj, props);
                    WriteObject(replicaObj);
                }
                catch (Exception ex)
                {
                    string parentAgName = GetParentAgName(replica);
                    StopFunction(
                        String.Format("Failed to modify replica {0} in availability group {1}", replicaName ?? "unknown", parentAgName ?? "unknown"),
                        exception: ex,
                        target: replicaObj,
                        isContinue: true);
                    // No TestFunctionInterrupt() here: isContinue=true means continue
                    // to the next replica in the loop (PS1 -Continue semantics)
                }
            }
        }

        #region Helpers

        /// <summary>
        /// Normalizes friendly ConnectionModeInSecondaryRole aliases to their SMO enum names.
        /// </summary>
        internal static string NormalizeSecondaryConnectionMode(string mode)
        {
            if (mode == null)
                return null;

            if (String.Equals(mode, "No", StringComparison.OrdinalIgnoreCase))
                return "AllowNoConnections";
            if (String.Equals(mode, "Read-intent only", StringComparison.OrdinalIgnoreCase))
                return "AllowReadIntentConnectionsOnly";
            if (String.Equals(mode, "Yes", StringComparison.OrdinalIgnoreCase))
                return "AllowAllConnections";

            return mode;
        }

        /// <summary>
        /// Detects whether the ReadOnlyRoutingList is a load-balanced (nested array) list.
        /// </summary>
        internal static bool IsLoadBalancedRoutingList(object[] routingList)
        {
            if (routingList == null || routingList.Length == 0)
                return false;

            // Check if the first element is an array (indicates load-balanced routing)
            object first = routingList[0];
            if (first is PSObject pso)
                first = pso.BaseObject;

            return first is Array;
        }

        /// <summary>
        /// Builds a Hashtable of properties to set on the replica, based on bound parameters.
        /// Returns a Hashtable directly for efficient PowerShell script consumption.
        /// </summary>
        private System.Collections.Hashtable BuildPropertiesHashtable()
        {
            System.Collections.Hashtable ht = new System.Collections.Hashtable();

            if (TestBound("EndpointUrl"))
                ht["EndpointUrl"] = EndpointUrl;
            if (TestBound("FailoverMode"))
                ht["FailoverMode"] = FailoverMode;
            if (TestBound("AvailabilityMode"))
                ht["AvailabilityMode"] = AvailabilityMode;
            if (TestBound("ConnectionModeInPrimaryRole"))
                ht["ConnectionModeInPrimaryRole"] = ConnectionModeInPrimaryRole;
            if (TestBound("ConnectionModeInSecondaryRole"))
                ht["ConnectionModeInSecondaryRole"] = _normalizedSecondaryMode;
            if (TestBound("BackupPriority"))
                ht["BackupPriority"] = BackupPriority;
            if (TestBound("ReadonlyRoutingConnectionUrl"))
                ht["ReadonlyRoutingConnectionUrl"] = ReadonlyRoutingConnectionUrl;
            if (TestBound("SeedingMode"))
                ht["SeedingMode"] = SeedingMode;
            if (TestBound("SessionTimeout"))
                ht["SessionTimeout"] = SessionTimeout;

            return ht;
        }

        /// <summary>
        /// Calls Get-DbaAgReplica to resolve replica objects from SqlInstance parameters.
        /// </summary>
        private Collection<PSObject> GetAgReplicas()
        {
            object[] args = new object[]
            {
                SqlInstance,
                SqlCredential,
                AvailabilityGroup,
                Replica,
                SqlCredential != null,
                EnableException.ToBool()
            };

            try
            {
                return InvokeCommand.InvokeScript(true, _getAgReplicaScript, null, args);
            }
            catch (Exception ex)
            {
                StopFunction(
                    String.Format("Failed to get AG replicas: {0}", ex.Message),
                    exception: ex);
                TestFunctionInterrupt();
                return null;
            }
        }

        /// <summary>
        /// Gets the server name from a replica's parent chain (replica.Parent.Parent.Name).
        /// </summary>
        private string GetServerName(object replicaObj)
        {
            try
            {
                Collection<PSObject> result = InvokeCommand.InvokeScript(
                    false, _getServerNameScript, null, new object[] { replicaObj });
                if (result != null && result.Count > 0 && result[0] != null)
                    return result[0].ToString();
            }
            catch (Exception)
            {
                // Best effort
            }
            return null;
        }

        /// <summary>
        /// Sets properties on a replica and calls Alter() via PowerShell script.
        /// </summary>
        private void SetPropertiesAndAlter(object replicaObj, System.Collections.Hashtable props)
        {
            InvokeCommand.InvokeScript(
                false, _setPropertiesAndAlterScript, null,
                new object[] { replicaObj, props });
        }

        /// <summary>
        /// Sets the read-only routing list on a replica via PowerShell script.
        /// </summary>
        private void SetRoutingList(object replicaObj, bool isLoadBalanced)
        {
            InvokeCommand.InvokeScript(
                false, _setRoutingListScript, null,
                new object[] { replicaObj, ReadOnlyRoutingList, isLoadBalanced });
        }

        /// <summary>
        /// Gets the parent availability group name from a replica object.
        /// </summary>
        internal static string GetParentAgName(PSObject replica)
        {
            if (replica == null)
                return null;

            PSObject parent = GetPropertyObject(replica, "Parent");
            if (parent != null)
                return GetPropertyString(parent, "Name");

            return null;
        }

        #endregion Helpers
    }
}
