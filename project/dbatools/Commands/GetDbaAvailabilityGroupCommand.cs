using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Retrieves Availability Group configuration and status information from SQL Server instances.
    /// Returns AvailabilityGroup objects with replica roles, cluster configuration, database membership,
    /// and listener details from SQL Server 2012+ instances.
    /// </summary>
    [Cmdlet("Get", "DbaAvailabilityGroup")]
    public class GetDbaAvailabilityGroupCommand : DbaInstanceCmdlet
    {
        #region Parameters

        /// <summary>
        /// Specifies one or more Availability Group names to filter results to specific AGs.
        /// </summary>
        [Parameter()]
        public string[] AvailabilityGroup { get; set; }

        /// <summary>
        /// Returns a boolean value indicating whether the queried SQL Server instance is currently
        /// serving as the Primary replica for each Availability Group.
        /// </summary>
        [Parameter()]
        public SwitchParameter IsPrimary { get; set; }

        #endregion Parameters

        /// <summary>
        /// Cached script block for retrieving AvailabilityGroups from a server object.
        /// </summary>
        private static readonly ScriptBlock _getAvailabilityGroupsScript =
            ScriptBlock.Create("param($s) $s.AvailabilityGroups");

        /// <summary>
        /// Cached script block for refreshing AvailabilityDatabases on an AG object.
        /// </summary>
        private static readonly ScriptBlock _refreshAvailabilityDatabasesScript =
            ScriptBlock.Create("param($ag) $ag.AvailabilityDatabases.Refresh()");

        /// <summary>
        /// Default display properties for standard output (without -IsPrimary).
        /// </summary>
        private static readonly string[] DefaultDisplayProperties = new string[]
        {
            "ComputerName", "InstanceName", "SqlInstance", "LocalReplicaRole",
            "AvailabilityGroup", "PrimaryReplica", "ClusterType",
            "DtcSupportEnabled", "AutomatedBackupPreference",
            "AvailabilityReplicas", "AvailabilityDatabases", "AvailabilityGroupListeners"
        };

        /// <summary>
        /// Default display properties when -IsPrimary is specified.
        /// </summary>
        private static readonly string[] IsPrimaryDisplayProperties = new string[]
        {
            "ComputerName", "InstanceName", "SqlInstance", "AvailabilityGroup", "IsPrimary"
        };

        /// <summary>
        /// Processes each SQL Server instance to retrieve availability group information.
        /// </summary>
        protected override void ProcessRecord()
        {
            foreach (DbaInstanceParameter instance in SqlInstance)
            {
                object rawServer;
                try
                {
                    rawServer = ConnectInstance(instance);
                    if (rawServer == null)
                    {
                        StopFunction(
                            "Failure",
                            target: instance,
                            isContinue: true,
                            category: ErrorCategory.ConnectionError);
                        TestFunctionInterrupt();
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    StopFunction(
                        "Failure",
                        errorRecord: new ErrorRecord(ex, "GetDbaAvailabilityGroup_ConnectionError", ErrorCategory.ConnectionError, instance),
                        target: instance,
                        isContinue: true,
                        category: ErrorCategory.ConnectionError);
                    TestFunctionInterrupt();
                    continue;
                }

                try
                {
                    PSObject server = PSObject.AsPSObject(rawServer);

                    // Check if HADR is enabled
                    object isHadrEnabled = GetPropertyValue(server, "IsHadrEnabled");
                    if (isHadrEnabled == null || !(isHadrEnabled is bool && (bool)isHadrEnabled))
                    {
                        StopFunction(
                            String.Format("Availability Group (HADR) is not configured for the instance: {0}.", instance),
                            target: instance,
                            isContinue: true);
                        TestFunctionInterrupt();
                        continue;
                    }

                    // Read server properties for NoteProperties
                    string computerName = GetPropertyString(server, "ComputerName");
                    string serviceName = GetPropertyString(server, "ServiceName");
                    string domainInstanceName = GetPropertyString(server, "DomainInstanceName");

                    // Get AvailabilityGroups collection
                    Collection<PSObject> ags = GetAvailabilityGroups(rawServer);
                    if (ags == null || ags.Count == 0)
                        continue;

                    // Build filter set if AvailabilityGroup parameter is specified
                    HashSet<string> agFilter = BuildAgFilter(AvailabilityGroup);

                    foreach (PSObject ag in ags)
                    {
                        if (ag == null)
                            continue;

                        // Apply name filter
                        string agName = GetPropertyString(ag, "Name");
                        if (agFilter != null && (agName == null || !agFilter.Contains(agName)))
                            continue;

                        // Refresh AvailabilityDatabases to fix #9094
                        RefreshAvailabilityDatabases(ag);

                        // Add NoteProperties matching PS1 Add-Member -Force behavior
                        AddOrSetProperty(ag, "ComputerName", computerName);
                        AddOrSetProperty(ag, "InstanceName", serviceName);
                        AddOrSetProperty(ag, "SqlInstance", domainInstanceName);

                        if (IsPrimary.IsPresent)
                        {
                            // Get LocalReplicaRole and compare to "Primary"
                            string localReplicaRole = GetPropertyString(ag, "LocalReplicaRole");
                            bool isPrimaryValue = String.Equals(localReplicaRole, "Primary", StringComparison.OrdinalIgnoreCase);
                            AddOrSetProperty(ag, "IsPrimary", isPrimaryValue);

                            // Add AliasProperty: AvailabilityGroup -> Name
                            AddAliasProperty(ag, "AvailabilityGroup", "Name");

                            SetDefaultDisplayPropertySet(ag, IsPrimaryDisplayProperties);
                        }
                        else
                        {
                            // Add AliasProperties matching PS1 "Name as AvailabilityGroup" pattern
                            AddAliasProperty(ag, "AvailabilityGroup", "Name");
                            AddAliasProperty(ag, "PrimaryReplica", "PrimaryReplicaServerName");

                            SetDefaultDisplayPropertySet(ag, DefaultDisplayProperties);
                        }

                        WriteObject(ag);
                    }
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("Failed to process {0}", instance),
                        exception: ex,
                        target: instance,
                        isContinue: true);
                    TestFunctionInterrupt();
                    continue;
                }
            }
        }

        #region Helpers

        /// <summary>
        /// Connects to a SQL Server instance using Connect-DbaInstance with MinimumVersion 11 (SQL 2012+).
        /// Returns the BaseObject from the SMO Server result, or null on failure.
        /// </summary>
        private object ConnectInstance(DbaInstanceParameter instance)
        {
            string script;
            object[] args;
            if (SqlCredential != null)
            {
                script = "param($i, $c) Connect-DbaInstance -SqlInstance $i -SqlCredential $c -MinimumVersion 11";
                args = new object[] { instance, SqlCredential };
            }
            else
            {
                script = "param($i) Connect-DbaInstance -SqlInstance $i -MinimumVersion 11";
                args = new object[] { instance };
            }

            Collection<PSObject> results = InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, args);
            if (results != null && results.Count > 0)
                return results[0].BaseObject;
            return null;
        }

        /// <summary>
        /// Gets the AvailabilityGroups collection from a server object.
        /// Exceptions propagate to the caller for proper Stop-Function handling.
        /// </summary>
        private Collection<PSObject> GetAvailabilityGroups(object serverObject)
        {
            return InvokeCommand.InvokeScript(
                false,
                _getAvailabilityGroupsScript,
                null,
                new object[] { serverObject });
        }

        /// <summary>
        /// Refreshes the AvailabilityDatabases collection on an AG object.
        /// This fixes issue #9094 where stale database lists were returned.
        /// </summary>
        private void RefreshAvailabilityDatabases(PSObject ag)
        {
            try
            {
                InvokeCommand.InvokeScript(
                    false,
                    _refreshAvailabilityDatabasesScript,
                    null,
                    new object[] { ag });
            }
            catch (Exception)
            {
                // Best-effort refresh, non-fatal if it fails
            }
        }

        /// <summary>
        /// Builds a case-insensitive HashSet from an array of AG names for filtering.
        /// Returns null if the input is null or empty.
        /// </summary>
        internal static HashSet<string> BuildAgFilter(string[] agNames)
        {
            if (agNames == null || agNames.Length == 0)
                return null;

            HashSet<string> filter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in agNames)
            {
                filter.Add(name);
            }
            return filter;
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
        /// Adds an AliasProperty to a PSObject, matching the PS1 "X as Y" pattern in Select-DefaultView.
        /// This creates an alias so that the display property name maps to the actual property.
        /// </summary>
        internal static void AddAliasProperty(PSObject obj, string aliasName, string referencedPropertyName)
        {
            if (obj == null)
                return;
            try
            {
                // Remove existing member if present (force behavior)
                try { obj.Members.Remove(aliasName); }
                catch (Exception) { /* May not exist */ }

                obj.Members.Add(new PSAliasProperty(aliasName, referencedPropertyName));
            }
            catch (Exception)
            {
                // Best-effort - if alias fails, try NoteProperty as fallback
                try
                {
                    object value = GetPropertyValue(obj, referencedPropertyName);
                    AddOrSetProperty(obj, aliasName, value);
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
