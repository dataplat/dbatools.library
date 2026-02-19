using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Retrieves the runtime state of databases participating in availability groups across all replicas.
    /// Returns comprehensive health monitoring information similar to the SSMS AG Dashboard,
    /// including synchronization state, failover readiness, data loss estimates, and LSN details
    /// for each database on each replica in the availability group.
    /// </summary>
    [Cmdlet("Get", "DbaAgDatabaseReplicaState")]
    public class GetDbaAgDatabaseReplicaStateCommand : DbaBaseCmdlet
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
        public PSCredential SqlCredential { get; set; }

        /// <summary>
        /// Specifies which availability groups to query for database replica state information.
        /// </summary>
        [Parameter()]
        public string[] AvailabilityGroup { get; set; }

        /// <summary>
        /// Specifies which availability group databases to return replica state information for.
        /// </summary>
        [Parameter()]
        public string[] Database { get; set; }

        /// <summary>
        /// Accepts availability group objects from Get-DbaAvailabilityGroup via pipeline input.
        /// Type is object[] because SMO types are loaded dynamically at runtime.
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        public object[] InputObject { get; set; }

        #endregion Parameters

        /// <summary>
        /// AG objects obtained from SqlInstance parameter, processed in EndProcessing.
        /// </summary>
        private List<object> _sqlInstanceObjects = new List<object>();

        /// <summary>
        /// Database name filter built from the Database parameter.
        /// </summary>
        private HashSet<string> _databaseFilter;

        /// <summary>
        /// Tracks whether any InputObject was received via pipeline.
        /// </summary>
        private bool _receivedPipelineInput;

        /// <summary>
        /// Validates parameters and resolves SqlInstance-derived AG objects.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            // Only validate when we know for sure neither parameter will be provided.
            // When pipeline input is expected, InputObject won't be bound yet in BeginProcessing,
            // so we skip the check here and validate in EndProcessing instead.
            if (!MyInvocation.ExpectingInput && TestBoundNot("SqlInstance", "InputObject"))
            {
                StopFunction("You must supply either -SqlInstance or an Input Object");
                TestFunctionInterrupt();
                return;
            }

            // Build database name filter once
            if (TestBound("Database") && Database != null)
            {
                _databaseFilter = BuildDatabaseFilter(Database);
            }

            // If SqlInstance is provided, call Get-DbaAvailabilityGroup once to get AG objects
            if (TestBound("SqlInstance"))
            {
                Collection<PSObject> agObjects = GetAvailabilityGroups();
                if (agObjects != null)
                {
                    foreach (PSObject ag in agObjects)
                    {
                        if (ag != null)
                            _sqlInstanceObjects.Add(ag.BaseObject ?? ag);
                    }
                }
            }
        }

        /// <summary>
        /// Processes each pipeline InputObject item immediately for streaming output.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (TestFunctionInterrupt())
                return;

            // Process pipeline InputObject items as they arrive for streaming output
            if (InputObject != null)
            {
                _receivedPipelineInput = true;
                foreach (object obj in InputObject)
                {
                    if (obj != null)
                        ProcessAgObject(obj);
                }
            }
        }

        /// <summary>
        /// Processes AG objects obtained from SqlInstance parameter and validates pipeline input was received.
        /// </summary>
        protected override void EndProcessing()
        {
            if (TestFunctionInterrupt())
                return;

            // Deferred validation: if pipeline was expected but nothing arrived and SqlInstance wasn't bound
            if (!_receivedPipelineInput && _sqlInstanceObjects.Count == 0 && !TestBound("SqlInstance"))
            {
                StopFunction("You must supply either -SqlInstance or an Input Object");
                return;
            }

            // Process SqlInstance-derived AG objects
            foreach (object agObj in _sqlInstanceObjects)
            {
                ProcessAgObject(agObj);
            }
        }

        /// <summary>
        /// Processes a single AG object: iterates replicas and databases to build
        /// database replica state output objects matching the PS1 output shape.
        /// </summary>
        private void ProcessAgObject(object agObj)
        {
            PSObject ag = PSObject.AsPSObject(agObj);

            // Get AG-level properties
            string agComputerName = GetPropertyString(ag, "ComputerName");
            string agInstanceName = GetPropertyString(ag, "InstanceName");
            string agSqlInstance = GetPropertyString(ag, "SqlInstance");
            string agName = GetPropertyString(ag, "Name");
            string primaryReplica = GetPropertyString(ag, "PrimaryReplica");

            // Get replicas - create ScriptBlock per invocation to avoid thread-safety issues
            Collection<PSObject> replicas = InvokeScriptSafe(
                "param($ag) $ag.AvailabilityReplicas", agObj,
                String.Format("AvailabilityReplicas for AG '{0}'", agName));
            if (replicas == null || replicas.Count == 0)
                return;

            // Get all DatabaseReplicaStates
            Collection<PSObject> allDatabaseReplicaStates = InvokeScriptSafe(
                "param($ag) $ag.DatabaseReplicaStates", agObj,
                String.Format("DatabaseReplicaStates for AG '{0}'", agName));

            // Get all AvailabilityDatabases
            Collection<PSObject> availabilityDatabases = InvokeScriptSafe(
                "param($ag) $ag.AvailabilityDatabases", agObj,
                String.Format("AvailabilityDatabases for AG '{0}'", agName));
            if (availabilityDatabases == null || availabilityDatabases.Count == 0)
                return;

            foreach (PSObject replica in replicas)
            {
                if (replica == null)
                    continue;

                // Get replica UniqueId for filtering DatabaseReplicaStates
                string replicaId = GetPropertyString(replica, "UniqueId");

                // Get replica properties
                object replicaAvailabilityMode = GetPropertyValue(replica, "AvailabilityMode");
                object replicaFailoverMode = GetPropertyValue(replica, "FailoverMode");
                object replicaConnectionState = GetPropertyValue(replica, "ConnectionState");
                object replicaJoinState = GetPropertyValue(replica, "JoinState");
                object replicaSyncState = GetPropertyValue(replica, "RollupSynchronizationState");

                // Filter DatabaseReplicaStates for this replica
                List<PSObject> replicaStates = FilterByReplicaId(allDatabaseReplicaStates, replicaId);

                foreach (PSObject db in availabilityDatabases)
                {
                    if (db == null)
                        continue;

                    string dbName = GetPropertyString(db, "Name");

                    // Apply Database filter
                    if (_databaseFilter != null && dbName != null && !_databaseFilter.Contains(dbName))
                        continue;

                    // Match DatabaseReplicaState by AvailabilityDateabaseId (typo is in SMO)
                    string dbUniqueId = GetPropertyString(db, "UniqueId");
                    PSObject databaseReplicaState = FindByDatabaseId(replicaStates, dbUniqueId);

                    if (databaseReplicaState == null)
                        continue;

                    // Build the output PSCustomObject matching the PS1 output shape exactly
                    PSObject output = new PSObject();
                    output.Properties.Add(new PSNoteProperty("ComputerName", agComputerName));
                    output.Properties.Add(new PSNoteProperty("InstanceName", agInstanceName));
                    output.Properties.Add(new PSNoteProperty("SqlInstance", agSqlInstance));
                    output.Properties.Add(new PSNoteProperty("AvailabilityGroup", agName));
                    output.Properties.Add(new PSNoteProperty("PrimaryReplica", primaryReplica));
                    output.Properties.Add(new PSNoteProperty("ReplicaServerName", GetPropertyValue(databaseReplicaState, "AvailabilityReplicaServerName")));
                    output.Properties.Add(new PSNoteProperty("ReplicaRole", GetPropertyValue(databaseReplicaState, "ReplicaRole")));
                    output.Properties.Add(new PSNoteProperty("ReplicaAvailabilityMode", replicaAvailabilityMode));
                    output.Properties.Add(new PSNoteProperty("ReplicaFailoverMode", replicaFailoverMode));
                    output.Properties.Add(new PSNoteProperty("ReplicaConnectionState", replicaConnectionState));
                    output.Properties.Add(new PSNoteProperty("ReplicaJoinState", replicaJoinState));
                    output.Properties.Add(new PSNoteProperty("ReplicaSynchronizationState", replicaSyncState));
                    output.Properties.Add(new PSNoteProperty("DatabaseName", GetPropertyValue(databaseReplicaState, "AvailabilityDatabaseName")));
                    output.Properties.Add(new PSNoteProperty("SynchronizationState", GetPropertyValue(databaseReplicaState, "SynchronizationState")));
                    output.Properties.Add(new PSNoteProperty("IsFailoverReady", GetPropertyValue(databaseReplicaState, "IsFailoverReady")));
                    output.Properties.Add(new PSNoteProperty("IsJoined", GetPropertyValue(databaseReplicaState, "IsJoined")));
                    output.Properties.Add(new PSNoteProperty("IsSuspended", GetPropertyValue(databaseReplicaState, "IsSuspended")));
                    output.Properties.Add(new PSNoteProperty("SuspendReason", GetPropertyValue(databaseReplicaState, "SuspendReason")));
                    output.Properties.Add(new PSNoteProperty("EstimatedRecoveryTime", GetPropertyValue(databaseReplicaState, "EstimatedRecoveryTime")));
                    output.Properties.Add(new PSNoteProperty("EstimatedDataLoss", GetPropertyValue(databaseReplicaState, "EstimatedDataLoss")));
                    output.Properties.Add(new PSNoteProperty("SynchronizationPerformance", GetPropertyValue(databaseReplicaState, "SynchronizationPerformance")));
                    output.Properties.Add(new PSNoteProperty("LogSendQueueSize", GetPropertyValue(databaseReplicaState, "LogSendQueueSize")));
                    output.Properties.Add(new PSNoteProperty("LogSendRate", GetPropertyValue(databaseReplicaState, "LogSendRate")));
                    output.Properties.Add(new PSNoteProperty("RedoQueueSize", GetPropertyValue(databaseReplicaState, "RedoQueueSize")));
                    output.Properties.Add(new PSNoteProperty("RedoRate", GetPropertyValue(databaseReplicaState, "RedoRate")));
                    output.Properties.Add(new PSNoteProperty("FileStreamSendRate", GetPropertyValue(databaseReplicaState, "FileStreamSendRate")));
                    output.Properties.Add(new PSNoteProperty("EndOfLogLSN", GetPropertyValue(databaseReplicaState, "EndOfLogLSN")));
                    output.Properties.Add(new PSNoteProperty("RecoveryLSN", GetPropertyValue(databaseReplicaState, "RecoveryLSN")));
                    output.Properties.Add(new PSNoteProperty("TruncationLSN", GetPropertyValue(databaseReplicaState, "TruncationLSN")));
                    output.Properties.Add(new PSNoteProperty("LastCommitLSN", GetPropertyValue(databaseReplicaState, "LastCommitLSN")));
                    output.Properties.Add(new PSNoteProperty("LastCommitTime", GetPropertyValue(databaseReplicaState, "LastCommitTime")));
                    output.Properties.Add(new PSNoteProperty("LastHardenedLSN", GetPropertyValue(databaseReplicaState, "LastHardenedLSN")));
                    output.Properties.Add(new PSNoteProperty("LastHardenedTime", GetPropertyValue(databaseReplicaState, "LastHardenedTime")));
                    output.Properties.Add(new PSNoteProperty("LastReceivedLSN", GetPropertyValue(databaseReplicaState, "LastReceivedLSN")));
                    output.Properties.Add(new PSNoteProperty("LastReceivedTime", GetPropertyValue(databaseReplicaState, "LastReceivedTime")));
                    output.Properties.Add(new PSNoteProperty("LastRedoneLSN", GetPropertyValue(databaseReplicaState, "LastRedoneLSN")));
                    output.Properties.Add(new PSNoteProperty("LastRedoneTime", GetPropertyValue(databaseReplicaState, "LastRedoneTime")));
                    output.Properties.Add(new PSNoteProperty("LastSentLSN", GetPropertyValue(databaseReplicaState, "LastSentLSN")));
                    output.Properties.Add(new PSNoteProperty("LastSentTime", GetPropertyValue(databaseReplicaState, "LastSentTime")));

                    WriteObject(output);
                }
            }
        }

        #region Helpers

        /// <summary>
        /// Builds a case-insensitive HashSet from an array of database names for filtering.
        /// Returns null if the input is null or empty.
        /// </summary>
        internal static HashSet<string> BuildDatabaseFilter(string[] databases)
        {
            if (databases == null || databases.Length == 0)
                return null;

            HashSet<string> filter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string db in databases)
            {
                filter.Add(db);
            }
            return filter;
        }

        /// <summary>
        /// Calls Get-DbaAvailabilityGroup with the current SqlInstance, SqlCredential, and AvailabilityGroup parameters.
        /// </summary>
        private Collection<PSObject> GetAvailabilityGroups()
        {
            string script = @"
param($si, $sc, $ag, $hasCred, $hasAg)
$params = @{ SqlInstance = $si }
if ($hasCred) { $params['SqlCredential'] = $sc }
if ($hasAg) { $params['AvailabilityGroup'] = $ag }
Get-DbaAvailabilityGroup @params
";
            object[] args = new object[]
            {
                SqlInstance,
                SqlCredential,
                AvailabilityGroup,
                SqlCredential != null,
                TestBound("AvailabilityGroup")
            };

            try
            {
                return InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, args);
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
        /// Invokes a ScriptBlock safely with a single argument, returning null on failure.
        /// Creates a new ScriptBlock per invocation to avoid thread-safety issues with shared instances.
        /// </summary>
        private Collection<PSObject> InvokeScriptSafe(string script, object arg, string context)
        {
            try
            {
                return InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, new object[] { arg });
            }
            catch (Exception ex)
            {
                WriteMessageAtLevel(
                    String.Format("Failed to access {0}: {1}", context, ex.Message),
                    MessageLevel.Warning, null);
                return null;
            }
        }

        /// <summary>
        /// Filters a collection of DatabaseReplicaState objects by AvailabilityReplicaId.
        /// </summary>
        internal static List<PSObject> FilterByReplicaId(Collection<PSObject> allStates, string replicaId)
        {
            List<PSObject> result = new List<PSObject>();
            if (allStates == null || replicaId == null)
                return result;

            foreach (PSObject state in allStates)
            {
                if (state == null)
                    continue;

                string stateReplicaId = GetPropertyString(state, "AvailabilityReplicaId");
                if (String.Equals(stateReplicaId, replicaId, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(state);
                }
            }
            return result;
        }

        /// <summary>
        /// Finds a DatabaseReplicaState matching the given database UniqueId via the
        /// AvailabilityDateabaseId property (note: typo is intentional, matches SMO API).
        /// </summary>
        internal static PSObject FindByDatabaseId(List<PSObject> replicaStates, string databaseId)
        {
            if (replicaStates == null || databaseId == null)
                return null;

            foreach (PSObject state in replicaStates)
            {
                if (state == null)
                    continue;

                // AvailabilityDateabaseId is a typo in SMO but we have to use it as-is
                string stateDatabaseId = GetPropertyString(state, "AvailabilityDateabaseId");
                if (String.Equals(stateDatabaseId, databaseId, StringComparison.OrdinalIgnoreCase))
                {
                    return state;
                }
            }
            return null;
        }

        /// <summary>
        /// Gets a property value from a PSObject as-is (preserving type).
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

        #endregion Helpers
    }
}
