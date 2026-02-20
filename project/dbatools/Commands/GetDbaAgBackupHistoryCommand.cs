using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Retrieves backup history from msdb across all replicas in a SQL Server Availability Group.
    /// Queries the msdb backup history tables across all replicas and aggregates the results into
    /// a unified view. Automatically discovers replicas when given a listener, or accepts individual
    /// replica instances directly.
    /// </summary>
    [Cmdlet("Get", "DbaAgBackupHistory", DefaultParameterSetName = "Default")]
    public class GetDbaAgBackupHistoryCommand : DbaInstanceCmdlet
    {
        #region Parameters

        /// <summary>
        /// Specifies the name of the availability group to query for backup history.
        /// </summary>
        [Parameter(Mandatory = true)]
        public string AvailabilityGroup { get; set; }

        /// <summary>
        /// Specifies which databases within the availability group to include in the backup history.
        /// </summary>
        [Parameter()]
        public string[] Database { get; set; }

        /// <summary>
        /// Specifies databases within the availability group to exclude from backup history results.
        /// </summary>
        [Parameter()]
        public string[] ExcludeDatabase { get; set; }

        /// <summary>
        /// Includes copy-only backups in the results, which are normally excluded by default.
        /// </summary>
        [Parameter()]
        public SwitchParameter IncludeCopyOnly { get; set; }

        /// <summary>
        /// Returns detailed backup information including additional metadata fields.
        /// </summary>
        [Parameter(ParameterSetName = "NoLast")]
        public SwitchParameter Force { get; set; }

        /// <summary>
        /// Filters backup history to only include backups taken after this date and time.
        /// Defaults to January 1, 1970 if not specified.
        /// </summary>
        [Parameter()]
        public DateTime Since { get; set; }

        /// <summary>
        /// Filters backup history to a specific recovery fork identified by its GUID.
        /// </summary>
        [Parameter()]
        [ValidatePattern(@"^$|^(\{){0,1}[0-9a-fA-F]{8}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{12}(\}){0,1}$")]
        [AllowEmptyString()]
        public string RecoveryFork { get; set; }

        /// <summary>
        /// Returns the most recent complete backup chain needed for point-in-time recovery.
        /// </summary>
        [Parameter()]
        public SwitchParameter Last { get; set; }

        /// <summary>
        /// Returns only the most recent full backup for each database.
        /// </summary>
        [Parameter()]
        public SwitchParameter LastFull { get; set; }

        /// <summary>
        /// Returns only the most recent differential backup for each database.
        /// </summary>
        [Parameter()]
        public SwitchParameter LastDiff { get; set; }

        /// <summary>
        /// Returns only the most recent transaction log backup for each database.
        /// </summary>
        [Parameter()]
        public SwitchParameter LastLog { get; set; }

        /// <summary>
        /// Filters backup history by the storage device type where backups were written.
        /// </summary>
        [Parameter()]
        public string[] DeviceType { get; set; }

        /// <summary>
        /// Returns individual backup file details instead of grouping striped backup files.
        /// </summary>
        [Parameter()]
        public SwitchParameter Raw { get; set; }

        /// <summary>
        /// Filters backup history to only include backups with Log Sequence Numbers greater than this value.
        /// </summary>
        [Parameter()]
        public long LastLsn { get; set; }

        /// <summary>
        /// Includes mirrored backup sets in the results.
        /// </summary>
        [Parameter()]
        public SwitchParameter IncludeMirror { get; set; }

        /// <summary>
        /// Filters results to specific backup types.
        /// </summary>
        [Parameter()]
        [ValidateSet("Full", "Log", "Differential", "File", "Differential File", "Partial Full", "Partial Differential")]
        public string[] Type { get; set; }

        /// <summary>
        /// Determines which LSN field to use for sorting when filtering with Last switches.
        /// </summary>
        [Parameter()]
        [ValidateSet("FirstLsn", "DatabaseBackupLsn", "LastLsn")]
        public string LsnSort { get; set; }

        #endregion Parameters

        /// <summary>
        /// The accumulated list of server objects or replica names collected in ProcessRecord.
        /// </summary>
        private List<object> _serverList = new List<object>();

        /// <summary>
        /// Initializes defaults and resolves caller info.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            WriteMessageAtLevel(
                String.Format("Active Parameter set: {0}.", ParameterSetName),
                MessageLevel.System, null);
            WriteMessageAtLevel(
                String.Format("Bound parameters: {0}", String.Join(", ", MyInvocation.BoundParameters.Keys)),
                MessageLevel.System, null);

            // Default Since if not bound
            if (!TestBound("Since"))
            {
                Since = new DateTime(1970, 1, 1);
            }

            // Default LsnSort if not bound
            if (!TestBound("LsnSort"))
            {
                LsnSort = "FirstLsn";
            }
        }

        /// <summary>
        /// Connects to each SQL Server instance, validates the availability group, and accumulates servers.
        /// </summary>
        protected override void ProcessRecord()
        {
            foreach (DbaInstanceParameter instance in SqlInstance)
            {
                object server;
                try
                {
                    server = ConnectInstance(instance);
                    if (server == null)
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
                        errorRecord: new ErrorRecord(ex, "GetDbaAgBackupHistory_ConnectionError", ErrorCategory.ConnectionError, instance),
                        target: instance,
                        isContinue: true,
                        category: ErrorCategory.ConnectionError);
                    TestFunctionInterrupt();
                    continue;
                }

                // Check if instance has availability groups
                int agCount = GetAvailabilityGroupCount(server);
                if (agCount == 0)
                {
                    StopFunction(
                        String.Format("Instance {0} has no availability groups, so skipping.", instance),
                        target: instance,
                        isContinue: true);
                    TestFunctionInterrupt();
                    continue;
                }

                // Check if specific AG exists
                if (!HasAvailabilityGroup(server, AvailabilityGroup))
                {
                    StopFunction(
                        String.Format("Instance {0} has no availability group named '{1}', so skipping.", instance, AvailabilityGroup),
                        target: instance,
                        isContinue: true);
                    TestFunctionInterrupt();
                    continue;
                }

                WriteMessageVerbose(String.Format("Added {0} to serverList", server));
                _serverList.Add(server);
            }
        }

        /// <summary>
        /// Aggregates backup history from all discovered replicas and applies filtering.
        /// </summary>
        protected override void EndProcessing()
        {
            if (_serverList.Count == 0)
            {
                StopFunction(String.Format("No instances with availability group named '{0}' found, so finishing without results.", AvailabilityGroup));
                return;
            }

            // If only one server, assume it's a listener and discover replicas
            object serverListForQuery;
            if (_serverList.Count == 1)
            {
                WriteMessageVerbose("We have one server, so it should be a listener");
                object[] replicaNames = GetReplicaNames(_serverList[0], AvailabilityGroup);
                if (replicaNames != null && replicaNames.Length > 0)
                {
                    WriteMessageVerbose(String.Format("We have found these replicas: {0}", String.Join(", ", ConvertToStringArray(replicaNames))));
                    serverListForQuery = replicaNames;
                }
                else
                {
                    // Fall back to the single server
                    serverListForQuery = new object[] { _serverList[0] };
                }
            }
            else
            {
                serverListForQuery = _serverList.ToArray();
            }

            WriteMessageVerbose("We have more than one server, so query them all and aggregate");

            // If Database not bound, get AG databases from first replica
            string[] databaseFilter = Database;
            if (!TestBound("Database"))
            {
                databaseFilter = GetAgDatabases(serverListForQuery, AvailabilityGroup);
            }

            // Build and invoke Get-DbaDbBackupHistory call
            Collection<PSObject> agResults = InvokeGetDbaDbBackupHistory(serverListForQuery, databaseFilter);

            if (agResults == null || agResults.Count == 0)
            {
                return;
            }

            // Set AvailabilityGroupName on each result
            foreach (PSObject result in agResults)
            {
                SetAvailabilityGroupName(result, AvailabilityGroup);
            }

            // Apply Last* filtering
            if (Last.IsPresent)
            {
                WriteMessageVerbose("Filtering Ag backups for Last");
                InvokeSelectDbaBackupInformation(agResults, AvailabilityGroup);
            }
            else if (LastFull.IsPresent)
            {
                WriteMessageVerbose("Filtering Ag backups for LastFull");
                OutputLastPerDatabase(agResults);
            }
            else if (LastDiff.IsPresent)
            {
                WriteMessageVerbose("Filtering Ag backups for LastDiff");
                OutputLastPerDatabase(agResults);
            }
            else if (LastLog.IsPresent)
            {
                WriteMessageVerbose("Filtering Ag backups for LastLog");
                OutputLastPerDatabase(agResults);
            }
            else
            {
                WriteMessageVerbose("Output Ag backups without filtering");
                foreach (PSObject result in agResults)
                {
                    WriteObject(result);
                }
            }
        }

        #region Helpers

        /// <summary>
        /// Connects to a SQL Server instance via Connect-DbaInstance with minimum version 9.
        /// </summary>
        private object ConnectInstance(DbaInstanceParameter instance)
        {
            string script;
            object[] args;
            if (SqlCredential != null)
            {
                script = "param($i, $c) Connect-DbaInstance -SqlInstance $i -SqlCredential $c -MinimumVersion 9";
                args = new object[] { instance, SqlCredential };
            }
            else
            {
                script = "param($i) Connect-DbaInstance -SqlInstance $i -MinimumVersion 9";
                args = new object[] { instance };
            }

            Collection<PSObject> results = InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, args);
            if (results != null && results.Count > 0)
                return results[0].BaseObject;
            return null;
        }

        /// <summary>
        /// Gets the count of availability groups on a server object.
        /// </summary>
        private int GetAvailabilityGroupCount(object server)
        {
            string script = "param($s) @($s.AvailabilityGroups).Count";
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { server });
                if (results != null && results.Count > 0 && results[0] != null)
                {
                    return Convert.ToInt32(results[0].BaseObject);
                }
            }
            catch (Exception)
            {
                // Ignore
            }
            return 0;
        }

        /// <summary>
        /// Checks if the server has a specific availability group by name.
        /// </summary>
        private bool HasAvailabilityGroup(object server, string agName)
        {
            string script = "param($s, $n) $n -in $s.AvailabilityGroups.Name";
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { server, agName });
                if (results != null && results.Count > 0 && results[0] != null)
                {
                    return Convert.ToBoolean(results[0].BaseObject);
                }
            }
            catch (Exception)
            {
                // Ignore
            }
            return false;
        }

        /// <summary>
        /// Gets replica names from the availability group on the given server.
        /// </summary>
        private object[] GetReplicaNames(object server, string agName)
        {
            string script = "param($s, $n) ($s.AvailabilityGroups | Where-Object { $_.Name -eq $n }).AvailabilityReplicas.Name";
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { server, agName });
                if (results != null && results.Count > 0)
                {
                    object[] names = new object[results.Count];
                    for (int i = 0; i < results.Count; i++)
                    {
                        names[i] = results[i].BaseObject;
                    }
                    return names;
                }
            }
            catch (Exception)
            {
                // Ignore
            }
            return null;
        }

        /// <summary>
        /// Gets database names from the availability group via Get-DbaAgDatabase.
        /// </summary>
        private string[] GetAgDatabases(object serverList, string agName)
        {
            string script = "param($sl, $ag) (Get-DbaAgDatabase -SqlInstance $sl[0] -AvailabilityGroup $ag).Name";
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { serverList, agName });
                if (results != null && results.Count > 0)
                {
                    List<string> names = new List<string>();
                    foreach (PSObject r in results)
                    {
                        if (r != null && r.BaseObject != null)
                        {
                            names.Add(r.BaseObject.ToString());
                        }
                    }
                    return names.ToArray();
                }
            }
            catch (Exception)
            {
                // Ignore - will query all databases
            }
            return null;
        }

        /// <summary>
        /// Invokes Get-DbaDbBackupHistory with all relevant parameters splatted.
        /// </summary>
        private Collection<PSObject> InvokeGetDbaDbBackupHistory(object serverList, string[] databaseFilter)
        {
            // Build a hashtable of parameters to splat
            string script = @"
param($ServerList, $Database, $ExcludeDatabase, $IncludeCopyOnly, $Force, $Since, $RecoveryFork,
      $LastFull, $LastDiff, $LastLog, $DeviceType, $Raw, $LastLsn, $IncludeMirror, $Type, $LsnSort,
      $EnableException, $SqlCredential,
      $HasDatabase, $HasExcludeDatabase, $HasIncludeCopyOnly, $HasForce, $HasSince, $HasRecoveryFork,
      $HasLastFull, $HasLastDiff, $HasLastLog, $HasDeviceType, $HasRaw, $HasLastLsn,
      $HasIncludeMirror, $HasType, $HasLsnSort, $HasSqlCredential)

$params = @{
    SqlInstance = $ServerList
    EnableException = $EnableException
}
if ($HasDatabase -and $Database) { $params['Database'] = $Database }
if ($HasExcludeDatabase) { $params['ExcludeDatabase'] = $ExcludeDatabase }
if ($HasIncludeCopyOnly) { $params['IncludeCopyOnly'] = $IncludeCopyOnly }
if ($HasForce) { $params['Force'] = $Force }
if ($HasSince) { $params['Since'] = $Since }
if ($HasRecoveryFork) { $params['RecoveryFork'] = $RecoveryFork }
if ($HasLastFull) { $params['LastFull'] = $LastFull }
if ($HasLastDiff) { $params['LastDiff'] = $LastDiff }
if ($HasLastLog) { $params['LastLog'] = $LastLog }
if ($HasDeviceType) { $params['DeviceType'] = $DeviceType }
if ($HasRaw) { $params['Raw'] = $Raw }
if ($HasLastLsn) { $params['LastLsn'] = $LastLsn }
if ($HasIncludeMirror) { $params['IncludeMirror'] = $IncludeMirror }
if ($HasType) { $params['Type'] = $Type }
if ($HasLsnSort) { $params['LsnSort'] = $LsnSort }
if ($HasSqlCredential) { $params['SqlCredential'] = $SqlCredential }
Get-DbaDbBackupHistory @params
";

            object[] args = new object[]
            {
                serverList,
                databaseFilter,
                ExcludeDatabase,
                IncludeCopyOnly,
                Force,
                Since,
                RecoveryFork,
                LastFull,
                LastDiff,
                LastLog,
                DeviceType,
                Raw,
                LastLsn,
                IncludeMirror,
                Type,
                LsnSort,
                EnableException,
                SqlCredential,
                // Boolean flags indicating which params were bound
                databaseFilter != null,
                TestBound("ExcludeDatabase"),
                IncludeCopyOnly.IsPresent,
                Force.IsPresent,
                TestBound("Since"),
                TestBound("RecoveryFork"),
                LastFull.IsPresent,
                LastDiff.IsPresent,
                LastLog.IsPresent,
                TestBound("DeviceType"),
                Raw.IsPresent,
                TestBound("LastLsn"),
                IncludeMirror.IsPresent,
                TestBound("Type"),
                TestBound("LsnSort"),
                SqlCredential != null
            };

            try
            {
                return InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, args);
            }
            catch (Exception ex)
            {
                StopFunction(
                    String.Format("Failed to get backup history: {0}", ex.Message),
                    exception: ex);
                return null;
            }
        }

        /// <summary>
        /// Sets the AvailabilityGroupName property on a backup history result object.
        /// </summary>
        internal static void SetAvailabilityGroupName(PSObject result, string agName)
        {
            if (result == null)
                return;

            try
            {
                PSPropertyInfo prop = result.Properties["AvailabilityGroupName"];
                if (prop != null)
                {
                    prop.Value = agName;
                }
                else
                {
                    result.Properties.Add(new PSNoteProperty("AvailabilityGroupName", agName));
                }
            }
            catch (Exception)
            {
                // Best-effort property assignment
            }
        }

        /// <summary>
        /// Invokes Select-DbaBackupInformation for the -Last filtering.
        /// </summary>
        private void InvokeSelectDbaBackupInformation(Collection<PSObject> agResults, string agName)
        {
            string script = "param($results, $serverName) $results | Select-DbaBackupInformation -ServerName $serverName";
            try
            {
                Collection<PSObject> filtered = InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { agResults, agName });
                if (filtered != null)
                {
                    foreach (PSObject result in filtered)
                    {
                        WriteObject(result);
                    }
                }
            }
            catch (Exception ex)
            {
                StopFunction(
                    String.Format("Failed to filter backup information: {0}", ex.Message),
                    exception: ex);
            }
        }

        /// <summary>
        /// Outputs the last backup per unique database, sorted by the configured LsnSort property.
        /// Used for LastFull, LastDiff, and LastLog filtering.
        /// </summary>
        private void OutputLastPerDatabase(Collection<PSObject> agResults)
        {
            // Collect unique database names
            Dictionary<string, List<PSObject>> byDatabase = new Dictionary<string, List<PSObject>>(StringComparer.OrdinalIgnoreCase);
            foreach (PSObject result in agResults)
            {
                string dbName = GetDatabaseName(result);
                if (dbName == null)
                    continue;

                if (!byDatabase.ContainsKey(dbName))
                {
                    byDatabase[dbName] = new List<PSObject>();
                }
                byDatabase[dbName].Add(result);
            }

            string sortProp = LsnSort ?? "FirstLsn";

            // For each database, sort by LsnSort and output the last one
            foreach (KeyValuePair<string, List<PSObject>> kvp in byDatabase)
            {
                List<PSObject> dbResults = kvp.Value;
                if (dbResults.Count == 0)
                    continue;

                // Find the max by the sort property
                PSObject best = null;
                IComparable bestValue = null;

                foreach (PSObject item in dbResults)
                {
                    IComparable val = GetComparableProperty(item, sortProp);
                    if (val == null)
                        continue;

                    if (best == null || val.CompareTo(bestValue) >= 0)
                    {
                        best = item;
                        bestValue = val;
                    }
                }

                if (best != null)
                {
                    WriteObject(best);
                }
            }
        }

        /// <summary>
        /// Gets the Database property from a PSObject.
        /// </summary>
        internal static string GetDatabaseName(PSObject obj)
        {
            if (obj == null)
                return null;

            try
            {
                PSPropertyInfo prop = obj.Properties["Database"];
                if (prop != null && prop.Value != null)
                    return prop.Value.ToString();
            }
            catch (Exception)
            {
                // Ignore
            }
            return null;
        }

        /// <summary>
        /// Gets a comparable property value from a PSObject for sorting.
        /// </summary>
        internal static IComparable GetComparableProperty(PSObject obj, string propertyName)
        {
            if (obj == null)
                return null;

            try
            {
                PSPropertyInfo prop = obj.Properties[propertyName];
                if (prop != null && prop.Value is IComparable comparable)
                    return comparable;
            }
            catch (Exception)
            {
                // Ignore
            }
            return null;
        }

        /// <summary>
        /// Converts an array of objects to a string array for display purposes.
        /// </summary>
        internal static string[] ConvertToStringArray(object[] input)
        {
            if (input == null)
                return new string[0];

            string[] result = new string[input.Length];
            for (int i = 0; i < input.Length; i++)
            {
                result[i] = input[i] != null ? input[i].ToString() : String.Empty;
            }
            return result;
        }

        #endregion Helpers
    }
}
