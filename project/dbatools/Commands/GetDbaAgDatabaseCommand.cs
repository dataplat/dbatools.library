using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Retrieves availability group database information and synchronization status from SQL Server instances.
    /// Returns AvailabilityDatabase objects with properties including synchronization state,
    /// failover readiness, join status, and suspension state for each database in the availability group.
    /// </summary>
    [Cmdlet("Get", "DbaAgDatabase")]
    public class GetDbaAgDatabaseCommand : DbaBaseCmdlet
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
        /// Specifies which availability groups to query for database information.
        /// </summary>
        [Parameter()]
        public string[] AvailabilityGroup { get; set; }

        /// <summary>
        /// Specifies which availability group databases to return information for.
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
        /// Default display properties for the output objects.
        /// </summary>
        private static readonly string[] DefaultDisplayProperties = new string[]
        {
            "ComputerName", "InstanceName", "SqlInstance", "AvailabilityGroup",
            "LocalReplicaRole", "Name", "SynchronizationState", "IsFailoverReady",
            "IsJoined", "IsSuspended"
        };

        /// <summary>
        /// Pre-compiled ScriptBlock for retrieving AvailabilityDatabases from an AG object.
        /// </summary>
        private static readonly ScriptBlock _getAvailabilityDatabasesScript =
            ScriptBlock.Create("param($ag) $ag.AvailabilityDatabases");

        /// <summary>
        /// AG objects obtained from SqlInstance parameter, processed in EndProcessing.
        /// </summary>
        private List<object> _sqlInstanceObjects = new List<object>();

        /// <summary>
        /// Database name filter built from the Database parameter.
        /// </summary>
        private HashSet<string> _databaseFilter;

        /// <summary>
        /// Validates parameters and resolves SqlInstance-derived AG objects.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            // Validate that at least one of SqlInstance or InputObject is provided
            if (TestBoundNot("SqlInstance", "InputObject"))
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
                foreach (object obj in InputObject)
                {
                    if (obj != null)
                        ProcessAgObject(obj);
                }
            }
        }

        /// <summary>
        /// Processes AG objects obtained from SqlInstance parameter.
        /// </summary>
        protected override void EndProcessing()
        {
            if (TestFunctionInterrupt())
                return;

            // Process SqlInstance-derived AG objects
            foreach (object agObj in _sqlInstanceObjects)
            {
                ProcessAgObject(agObj);
            }
        }

        /// <summary>
        /// Processes a single AG object: retrieves its AvailabilityDatabases,
        /// applies filters, adds custom properties, and outputs results.
        /// </summary>
        private void ProcessAgObject(object agObj)
        {
            // Get AvailabilityDatabases from this AG object
            Collection<PSObject> databases = GetAvailabilityDatabases(agObj);
            if (databases == null || databases.Count == 0)
                return;

            foreach (PSObject db in databases)
            {
                if (db == null)
                    continue;

                // Get database name for filtering
                string dbName = GetPropertyString(db, "Name");

                // Apply Database filter
                if (_databaseFilter != null && dbName != null && !_databaseFilter.Contains(dbName))
                    continue;

                // Get parent AG and Server info
                PSObject parent = GetPropertyObject(db, "Parent");
                PSObject server = null;
                string agName = null;
                string localReplicaRole = null;
                string computerName = null;
                string serviceName = null;
                string domainInstanceName = null;

                if (parent != null)
                {
                    agName = GetPropertyString(parent, "Name");
                    localReplicaRole = GetPropertyString(parent, "LocalReplicaRole");

                    server = GetPropertyObject(parent, "Parent");
                    if (server != null)
                    {
                        computerName = GetPropertyString(server, "ComputerName");
                        serviceName = GetPropertyString(server, "ServiceName");
                        domainInstanceName = GetPropertyString(server, "DomainInstanceName");
                    }
                }

                // Add custom NoteProperties
                AddOrSetProperty(db, "ComputerName", computerName);
                AddOrSetProperty(db, "InstanceName", serviceName);
                AddOrSetProperty(db, "SqlInstance", domainInstanceName);
                AddOrSetProperty(db, "AvailabilityGroup", agName);
                AddOrSetProperty(db, "LocalReplicaRole", localReplicaRole);

                // Set default display properties
                SetDefaultDisplayPropertySet(db, DefaultDisplayProperties);

                WriteObject(db);
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
        /// Uses a single splatted script to handle all parameter combinations.
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
        /// Gets the AvailabilityDatabases collection from an AG object.
        /// </summary>
        private Collection<PSObject> GetAvailabilityDatabases(object agObject)
        {
            try
            {
                return InvokeCommand.InvokeScript(false, _getAvailabilityDatabasesScript, null, new object[] { agObject });
            }
            catch (Exception ex)
            {
                WriteMessageAtLevel(
                    String.Format("Failed to retrieve AvailabilityDatabases: {0}", ex.Message),
                    MessageLevel.Warning, null);
                return null;
            }
        }

        /// <summary>
        /// Adds or updates a NoteProperty on a PSObject, matching Add-Member -Force behavior.
        /// </summary>
        internal static void AddOrSetProperty(PSObject obj, string name, object value)
        {
            if (obj == null)
                return;
            try
            {
                PSPropertyInfo existing = obj.Properties[name];
                if (existing != null)
                {
                    existing.Value = value;
                }
                else
                {
                    obj.Properties.Add(new PSNoteProperty(name, value));
                }
            }
            catch (Exception)
            {
                try
                {
                    obj.Properties.Remove(name);
                    obj.Properties.Add(new PSNoteProperty(name, value));
                }
                catch (Exception)
                {
                    // Best-effort
                }
            }
        }

        /// <summary>
        /// Sets the DefaultDisplayPropertySet on a PSObject for formatted output.
        /// </summary>
        internal static void SetDefaultDisplayPropertySet(PSObject obj, string[] properties)
        {
            if (obj == null || properties == null)
                return;

            try { obj.Members.Remove("PSStandardMembers"); }
            catch (Exception) { /* May not exist yet */ }

            try
            {
                obj.Members.Add(new PSMemberSet("PSStandardMembers", new PSMemberInfo[]
                {
                    new PSPropertySet("DefaultDisplayPropertySet", properties)
                }));
            }
            catch (Exception)
            {
                // Best-effort
            }
        }

        #endregion Helpers
    }
}
