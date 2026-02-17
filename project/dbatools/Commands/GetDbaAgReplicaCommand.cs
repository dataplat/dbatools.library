using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Retrieves availability group replica configuration and status information from SQL Server instances.
    /// Returns AvailabilityReplica objects with replica names, roles, connection states, synchronization
    /// status, failover configuration, backup priority, endpoint URLs, and read-only routing lists.
    /// </summary>
    [Cmdlet("Get", "DbaAgReplica")]
    public class GetDbaAgReplicaCommand : DbaBaseCmdlet
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
        /// Specifies which availability groups to query for replica information.
        /// </summary>
        [Parameter()]
        public string[] AvailabilityGroup { get; set; }

        /// <summary>
        /// Filters results to return only the specified replica names.
        /// </summary>
        [Parameter()]
        public string[] Replica { get; set; }

        /// <summary>
        /// Accepts availability group objects piped from Get-DbaAvailabilityGroup via pipeline input.
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
            "Name", "Role", "ConnectionState", "RollupSynchronizationState",
            "AvailabilityMode", "BackupPriority", "EndpointUrl", "SessionTimeout",
            "FailoverMode", "ReadonlyRoutingList"
        };

        /// <summary>
        /// Cached script block for retrieving AvailabilityReplicas from an AG object.
        /// </summary>
        private static readonly ScriptBlock _getAvailabilityReplicasScript =
            ScriptBlock.Create("param($ag) $ag.AvailabilityReplicas");

        /// <summary>
        /// AG objects obtained from SqlInstance parameter in BeginProcessing, emitted in EndProcessing.
        /// </summary>
        private List<object> _sqlInstanceObjects = new List<object>();

        /// <summary>
        /// Replica name filter built from the Replica parameter.
        /// </summary>
        private HashSet<string> _replicaFilter;

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

            // Build replica name filter once
            if (TestBound("Replica") && Replica != null)
            {
                _replicaFilter = BuildReplicaFilter(Replica);
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
        /// Processes a single AG object: retrieves its AvailabilityReplicas,
        /// applies filters, adds custom properties, and outputs results.
        /// </summary>
        private void ProcessAgObject(object agObj)
        {
            // Get AvailabilityReplicas from this AG object
            Collection<PSObject> replicas = GetAvailabilityReplicas(agObj);
            if (replicas == null || replicas.Count == 0)
                return;

            foreach (PSObject replica in replicas)
            {
                if (replica == null)
                    continue;

                // Get replica name for filtering
                string replicaName = GetPropertyString(replica, "Name");

                // Apply Replica filter
                if (_replicaFilter != null)
                {
                    if (replicaName == null || !_replicaFilter.Contains(replicaName))
                        continue;
                }

                // Get parent AG info
                // Replica.Parent = AvailabilityGroup (has NoteProperties from Get-DbaAvailabilityGroup)
                PSObject parent = GetPropertyObject(replica, "Parent");
                string agName = null;
                string computerName = null;
                string serviceName = null;
                string domainInstanceName = null;

                if (parent != null)
                {
                    agName = GetPropertyString(parent, "Name");
                    computerName = GetPropertyString(parent, "ComputerName");
                    serviceName = GetPropertyString(parent, "InstanceName");
                    domainInstanceName = GetPropertyString(parent, "SqlInstance");
                }

                // Add custom NoteProperties (matching PS1 behavior)
                AddOrSetProperty(replica, "ComputerName", computerName);
                AddOrSetProperty(replica, "InstanceName", serviceName);
                AddOrSetProperty(replica, "SqlInstance", domainInstanceName);
                AddOrSetProperty(replica, "AvailabilityGroup", agName);
                AddOrSetProperty(replica, "Replica", replicaName); // backwards compat

                // Set default display properties
                SetDefaultDisplayPropertySet(replica, DefaultDisplayProperties);

                WriteObject(replica);
            }
        }

        #region Helpers

        /// <summary>
        /// Builds a case-insensitive HashSet from an array of replica names for filtering.
        /// Returns null if the input is null or empty.
        /// </summary>
        internal static HashSet<string> BuildReplicaFilter(string[] replicas)
        {
            if (replicas == null || replicas.Length == 0)
                return null;

            HashSet<string> filter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in replicas)
            {
                filter.Add(name);
            }
            return filter;
        }

        /// <summary>
        /// Calls Get-DbaAvailabilityGroup with the current SqlInstance, SqlCredential,
        /// AvailabilityGroup, and EnableException parameters.
        /// </summary>
        private Collection<PSObject> GetAvailabilityGroups()
        {
            string script = @"
param($si, $sc, $ag, $hasCred, $hasAg, $ee)
$params = @{ SqlInstance = $si }
if ($hasCred) { $params['SqlCredential'] = $sc }
if ($hasAg) { $params['AvailabilityGroup'] = $ag }
$params['EnableException'] = $ee
Get-DbaAvailabilityGroup @params
";
            object[] args = new object[]
            {
                SqlInstance,
                SqlCredential,
                AvailabilityGroup,
                SqlCredential != null,
                TestBound("AvailabilityGroup"),
                EnableException.ToBool()
            };

            try
            {
                return InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, args);
            }
            catch (Exception ex)
            {
                StopFunction(
                    String.Format("Failure on {0} to obtain the availability group {1}",
                        SqlInstance != null ? String.Join(", ", (object[])SqlInstance) : "unknown",
                        AvailabilityGroup != null ? String.Join(", ", AvailabilityGroup) : ""),
                    exception: ex);
                TestFunctionInterrupt();
                return null;
            }
        }

        /// <summary>
        /// Gets the AvailabilityReplicas collection from an AG object.
        /// </summary>
        private Collection<PSObject> GetAvailabilityReplicas(object agObject)
        {
            try
            {
                return InvokeCommand.InvokeScript(
                    false,
                    _getAvailabilityReplicasScript,
                    null,
                    new object[] { agObject });
            }
            catch (Exception ex)
            {
                WriteMessageAtLevel(
                    String.Format("Failed to retrieve AvailabilityReplicas: {0}", ex.Message),
                    MessageLevel.Warning, null);
                return null;
            }
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
