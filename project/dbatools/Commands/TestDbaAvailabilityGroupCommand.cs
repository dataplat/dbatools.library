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
    /// Validates Availability Group replica connectivity and database prerequisites for AG operations.
    /// Verifies that all replicas are connected and communicating properly. When used with AddDatabase,
    /// performs comprehensive prerequisite validation before adding databases to an AG.
    /// </summary>
    [Cmdlet("Test", "DbaAvailabilityGroup")]
    public class TestDbaAvailabilityGroupCommand : DbaBaseCmdlet
    {
        #region Parameters

        /// <summary>
        /// The primary replica of the Availability Group.
        /// </summary>
        [Parameter(Mandatory = true, Position = 0)]
        public DbaInstanceParameter SqlInstance { get; set; }

        /// <summary>
        /// Login to the target instance using alternative credentials.
        /// </summary>
        [Parameter()]
        public PSCredential SqlCredential { get; set; }

        /// <summary>
        /// Specifies the Availability Group name to validate for replica connectivity and database prerequisites.
        /// </summary>
        [Parameter(Mandatory = true)]
        public string AvailabilityGroup { get; set; }

        /// <summary>
        /// Specifies secondary replica endpoints when they use non-standard ports or custom connection strings.
        /// </summary>
        [Parameter()]
        public DbaInstanceParameter[] Secondary { get; set; }

        /// <summary>
        /// Specifies credentials for connecting to secondary replica instances during validation.
        /// </summary>
        [Parameter()]
        public PSCredential SecondarySqlCredential { get; set; }

        /// <summary>
        /// Specifies database names to validate for Availability Group addition prerequisites.
        /// </summary>
        [Parameter()]
        public string[] AddDatabase { get; set; }

        /// <summary>
        /// Specifies the database seeding method for validation when using AddDatabase parameter.
        /// </summary>
        [Parameter()]
        [ValidateSet("Automatic", "Manual")]
        public string SeedingMode { get; set; }

        /// <summary>
        /// Specifies the network path accessible by all replicas for backup and restore operations.
        /// </summary>
        [Parameter()]
        public string SharedPath { get; set; }

        /// <summary>
        /// Validates that the most recent database backup chain can be used for AG database addition.
        /// </summary>
        [Parameter()]
        public SwitchParameter UseLastBackup { get; set; }

        #endregion Parameters

        #region Script Blocks

        private static readonly ScriptBlock _connectScript =
            ScriptBlock.Create("param($i, $c) Connect-DbaInstance -SqlInstance $i -SqlCredential $c -EnableException");

        private static readonly ScriptBlock _connectNoCred =
            ScriptBlock.Create("param($i) Connect-DbaInstance -SqlInstance $i -EnableException");

        private static readonly ScriptBlock _getAgScript =
            ScriptBlock.Create("param($s, $ag) Get-DbaAvailabilityGroup -SqlInstance $s -AvailabilityGroup $ag -EnableException");

        private static readonly ScriptBlock _getReplicasScript =
            ScriptBlock.Create("param($ag) $ag.AvailabilityReplicas");

        private static readonly ScriptBlock _getDatabasesScript =
            ScriptBlock.Create("param($s, $name) $s.Databases[$name]");

        private static readonly ScriptBlock _refreshDbScript =
            ScriptBlock.Create("param($db) $db.Refresh()");

        private static readonly ScriptBlock _getBackupHistoryScript =
            ScriptBlock.Create("param($s, $db) Get-DbaDbBackupHistory -SqlInstance $s -Database $db -IncludeCopyOnly -Last -EnableException");

        private static readonly ScriptBlock _getSecondaryReplicaNames =
            ScriptBlock.Create("param($ag) ($ag.AvailabilityReplicas | Where-Object { $_.Role -eq 'Secondary' }).Name");

        private static readonly ScriptBlock _getReplicaSeedingMode =
            ScriptBlock.Create("param($ag, $name) $ag.AvailabilityReplicas[$name].SeedingMode");

        private static readonly ScriptBlock _getVersionMajor =
            ScriptBlock.Create("param($s) $s.VersionMajor");

        private static readonly ScriptBlock _getDomainInstanceName =
            ScriptBlock.Create("param($s) $s.DomainInstanceName");

        private static readonly ScriptBlock _getParentScript =
            ScriptBlock.Create("param($ag) $ag.Parent");

        private static readonly ScriptBlock _getLastBackupDate =
            ScriptBlock.Create("param($db) $db.LastBackupDate.Year");

        #endregion Script Blocks

        /// <summary>
        /// Processes the availability group test.
        /// </summary>
        protected override void ProcessRecord()
        {
            // Connect to primary
            object server;
            try
            {
                server = ConnectInstance(SqlInstance, SqlCredential);
                if (server == null)
                {
                    StopFunction(
                        "Failure",
                        target: SqlInstance,
                        category: ErrorCategory.ConnectionError);
                    return;
                }
            }
            catch (Exception ex)
            {
                StopFunction(
                    "Failure",
                    errorRecord: new ErrorRecord(ex, "TestDbaAvailabilityGroup_ConnectionError", ErrorCategory.ConnectionError, SqlInstance),
                    target: SqlInstance,
                    category: ErrorCategory.ConnectionError);
                return;
            }

            // Get Availability Group
            PSObject ag;
            try
            {
                Collection<PSObject> agResults = InvokeCommand.InvokeScript(
                    false, _getAgScript, null,
                    new object[] { server, AvailabilityGroup });
                ag = (agResults != null && agResults.Count > 0) ? agResults[0] : null;
            }
            catch (Exception ex)
            {
                StopFunction(
                    String.Format("Availability Group {0} not found on {1}.", AvailabilityGroup, SqlInstance),
                    errorRecord: new ErrorRecord(ex, "TestDbaAvailabilityGroup_AgNotFound", ErrorCategory.ObjectNotFound, SqlInstance));
                return;
            }

            if (ag == null)
            {
                StopFunction(
                    String.Format("Availability Group {0} not found on {1}.", AvailabilityGroup, SqlInstance));
                return;
            }

            // Check local replica is primary
            string localReplicaRole = GetPropertyString(ag, "LocalReplicaRole");
            if (!String.Equals(localReplicaRole, "Primary", StringComparison.OrdinalIgnoreCase))
            {
                string primaryReplica = GetPropertyString(ag, "PrimaryReplica");
                if (String.IsNullOrEmpty(primaryReplica))
                    primaryReplica = GetPropertyString(ag, "PrimaryReplicaServerName");
                StopFunction(
                    String.Format("LocalReplicaRole of replica {0} is not Primary, but {1}. Please connect to the current primary replica {2}.",
                        SqlInstance, localReplicaRole, primaryReplica));
                return;
            }

            // Test replica connectivity
            bool connectivityFailure = false;
            Collection<PSObject> replicas = null;
            try
            {
                replicas = InvokeCommand.InvokeScript(
                    false, _getReplicasScript, null,
                    new object[] { ag });
            }
            catch (Exception ex)
            {
                StopFunction(
                    String.Format("Failed to retrieve replicas for {0}.", AvailabilityGroup),
                    exception: ex,
                    target: AvailabilityGroup);
                return;
            }

            if (replicas != null)
            {
                foreach (PSObject replica in replicas)
                {
                    if (replica == null) continue;
                    string connectionState = GetPropertyString(replica, "ConnectionState");
                    if (!String.Equals(connectionState, "Connected", StringComparison.OrdinalIgnoreCase))
                    {
                        connectivityFailure = true;
                        StopFunction(
                            String.Format("ConnectionState of replica {0} is not Connected, but {1}.", replica, connectionState),
                            target: replica,
                            isContinue: true);
                        TestFunctionInterrupt();
                    }
                }
            }

            if (connectivityFailure)
            {
                StopFunction("ConnectionState of one or more replicas is not Connected.");
                return;
            }

            // If no AddDatabase, output basic info and return
            if (AddDatabase == null || AddDatabase.Length == 0)
            {
                PSObject output = new PSObject();
                output.Properties.Add(new PSNoteProperty("ComputerName", GetPropertyString(ag, "ComputerName")));
                output.Properties.Add(new PSNoteProperty("InstanceName", GetPropertyString(ag, "InstanceName")));
                output.Properties.Add(new PSNoteProperty("SqlInstance", GetPropertyString(ag, "SqlInstance")));
                output.Properties.Add(new PSNoteProperty("AvailabilityGroup", GetPropertyString(ag, "AvailabilityGroup")));
                WriteObject(output);
                return;
            }

            // AddDatabase mode - validate each database
            int versionMajor = GetVersionMajor(server);

            foreach (string dbName in AddDatabase)
            {
                // Check automatic seeding version requirement (SQL 2016+ = version 13)
                if (String.Equals(SeedingMode, "Automatic", StringComparison.OrdinalIgnoreCase) && versionMajor < 13)
                {
                    StopFunction(
                        "Automatic seeding mode only supported in SQL Server 2016 and above",
                        target: server);
                    return;
                }

                // Get the database
                PSObject db = GetDatabase(server, dbName);
                if (db == null)
                {
                    StopFunction(
                        String.Format("Database [{0}] is not found on {1}.", dbName, SqlInstance),
                        target: dbName,
                        isContinue: true);
                    TestFunctionInterrupt();
                    continue;
                }

                // Refresh database
                try
                {
                    InvokeCommand.InvokeScript(false, _refreshDbScript, null, new object[] { db });
                }
                catch (Exception)
                {
                    // Best effort
                }

                // Check recovery model
                string recoveryModel = GetPropertyString(db, "RecoveryModel");
                if (!String.Equals(recoveryModel, "Full", StringComparison.OrdinalIgnoreCase))
                {
                    StopFunction(
                        String.Format("RecoveryModel of database {0} is not Full, but {1}.", db, recoveryModel),
                        target: db,
                        isContinue: true);
                    TestFunctionInterrupt();
                    continue;
                }

                // Check status
                string dbStatus = GetPropertyString(db, "Status");
                if (!String.Equals(dbStatus, "Normal", StringComparison.OrdinalIgnoreCase))
                {
                    StopFunction(
                        String.Format("Status of database {0} is not Normal, but {1}.", db, dbStatus),
                        target: db,
                        isContinue: true);
                    TestFunctionInterrupt();
                    continue;
                }

                // Check backup history if UseLastBackup
                Collection<PSObject> backups = new Collection<PSObject>();
                if (UseLastBackup.IsPresent)
                {
                    try
                    {
                        string dbNameStr = GetPropertyString(db, "Name");
                        Collection<PSObject> backupResults = InvokeCommand.InvokeScript(
                            false, _getBackupHistoryScript, null,
                            new object[] { server, dbNameStr });
                        if (backupResults != null)
                        {
                            foreach (PSObject b in backupResults)
                                backups.Add(b);
                        }
                    }
                    catch (Exception ex)
                    {
                        StopFunction(
                            String.Format("Failed to get backup history for database {0}.", db),
                            errorRecord: new ErrorRecord(ex, "TestDbaAvailabilityGroup_BackupHistoryError", ErrorCategory.ReadError, db),
                            target: db,
                            isContinue: true);
                        TestFunctionInterrupt();
                        continue;
                    }

                    if (!BackupsContainLog(backups))
                    {
                        StopFunction(
                            String.Format("Cannot use last backup for database {0}. A log backup must be the last backup taken.", db),
                            target: db,
                            isContinue: true);
                        TestFunctionInterrupt();
                        continue;
                    }
                }

                // Get secondary replicas
                List<object> secondaryReplicas = new List<object>();
                if (Secondary != null && Secondary.Length > 0)
                {
                    foreach (DbaInstanceParameter sec in Secondary)
                        secondaryReplicas.Add(sec);
                }
                else
                {
                    Collection<PSObject> secNames = InvokeCommand.InvokeScript(
                        false, _getSecondaryReplicaNames, null,
                        new object[] { ag });
                    if (secNames != null)
                    {
                        foreach (PSObject secName in secNames)
                        {
                            if (secName != null)
                                secondaryReplicas.Add(secName.BaseObject ?? secName);
                        }
                    }
                }

                Hashtable replicaServerSMO = new Hashtable();
                Hashtable restoreNeeded = new Hashtable();
                bool backupNeeded = false;
                bool failure = false;

                foreach (object replica in secondaryReplicas)
                {
                    // Connect to secondary
                    object replicaServer;
                    try
                    {
                        replicaServer = ConnectInstance(replica, SecondarySqlCredential);
                        if (replicaServer == null)
                        {
                            failure = true;
                            StopFunction(
                                "Failure",
                                target: replica,
                                isContinue: true,
                                category: ErrorCategory.ConnectionError);
                            TestFunctionInterrupt();
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        failure = true;
                        StopFunction(
                            "Failure",
                            errorRecord: new ErrorRecord(ex, "TestDbaAvailabilityGroup_SecondaryConnectionError", ErrorCategory.ConnectionError, replica),
                            target: replica,
                            isContinue: true,
                            category: ErrorCategory.ConnectionError);
                        TestFunctionInterrupt();
                        continue;
                    }

                    // Get AG on replica
                    PSObject replicaAg = null;
                    string replicaName = null;
                    try
                    {
                        Collection<PSObject> replicaAgResults = InvokeCommand.InvokeScript(
                            false, _getAgScript, null,
                            new object[] { replicaServer, AvailabilityGroup });
                        replicaAg = (replicaAgResults != null && replicaAgResults.Count > 0) ? replicaAgResults[0] : null;
                        if (replicaAg != null)
                        {
                            replicaName = GetDomainInstanceName(replicaAg);
                            if (replicaName == null)
                            {
                                replicaName = replica != null ? replica.ToString() : "Unknown";
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        failure = true;
                        StopFunction(
                            String.Format("Availability Group {0} not found on replica {1}.", AvailabilityGroup, replicaServer),
                            errorRecord: new ErrorRecord(ex, "TestDbaAvailabilityGroup_ReplicaAgNotFound", ErrorCategory.ObjectNotFound, replica),
                            target: replica,
                            isContinue: true);
                        TestFunctionInterrupt();
                        continue;
                    }

                    if (replicaAg == null)
                    {
                        failure = true;
                        StopFunction(
                            String.Format("Availability Group {0} not found on replica {1}.", AvailabilityGroup, replicaServer),
                            target: replica,
                            isContinue: true);
                        TestFunctionInterrupt();
                        continue;
                    }

                    // Check replica role
                    string replicaRole = GetPropertyString(replicaAg, "LocalReplicaRole");
                    if (!String.Equals(replicaRole, "Secondary", StringComparison.OrdinalIgnoreCase))
                    {
                        failure = true;
                        StopFunction(
                            String.Format("LocalReplicaRole of replica {0} is not Secondary, but {1}.", replicaServer, replicaRole),
                            target: replica,
                            isContinue: true);
                        TestFunctionInterrupt();
                        continue;
                    }

                    // Get the parent server object from the replica AG
                    object replicaParent = GetParentFromAg(replicaAg);
                    string dbNameStr2 = GetPropertyString(db, "Name");

                    // Check if database exists on replica
                    PSObject replicaDb = null;
                    if (replicaParent != null && dbNameStr2 != null)
                    {
                        replicaDb = GetDatabase(replicaParent, dbNameStr2);
                    }

                    if (replicaDb != null)
                    {
                        // Database already present on replica
                        string replicaDbAgName = GetPropertyString(replicaDb, "AvailabilityGroupName");
                        if (String.Equals(replicaDbAgName, AvailabilityGroup, StringComparison.OrdinalIgnoreCase))
                        {
                            WriteMessageVerbose(String.Format("Database {0} is already part of the Availability Group on replica {1}.", db, replicaName));
                        }
                        else
                        {
                            string replicaDbStatus = GetPropertyString(replicaDb, "Status");
                            if (!String.Equals(replicaDbStatus, "Restoring", StringComparison.OrdinalIgnoreCase))
                            {
                                failure = true;
                                StopFunction(
                                    String.Format("Status of database {0} on replica {1} is not Restoring, but {2}", db, replicaName, replicaDbStatus),
                                    target: replica,
                                    isContinue: true);
                                TestFunctionInterrupt();
                                continue;
                            }
                            if (UseLastBackup.IsPresent)
                            {
                                failure = true;
                                StopFunction(
                                    String.Format("Database {0} is already present on {1}, so -UseLastBackup must not be used. Please remove database from replica to use -UseLastBackup.", db, replicaName),
                                    target: replica,
                                    isContinue: true);
                                TestFunctionInterrupt();
                                continue;
                            }
                            WriteMessageVerbose(String.Format("Database {0} is already present in restoring status on replica {1}.", db, replicaName));
                        }
                    }
                    else
                    {
                        // No database on replica - check seeding mode
                        string currentReplicaSeedingMode = GetReplicaSeedingMode(ag, replicaName);
                        int lastBackupYear = GetLastBackupYear(db);

                        if (String.Equals(SeedingMode, "Automatic", StringComparison.OrdinalIgnoreCase))
                        {
                            if (String.Equals(currentReplicaSeedingMode, "Automatic", StringComparison.OrdinalIgnoreCase))
                            {
                                WriteMessageVerbose(String.Format("Database {0} will use automatic seeding on replica {1}. The replica is already configured accordingly.", db, replicaName));
                            }
                            else
                            {
                                WriteMessageVerbose(String.Format("Database {0} will use automatic seeding on replica {1}. The replica will be configured accordingly.", db, replicaName));
                            }
                            if (lastBackupYear == 1)
                            {
                                WriteMessageVerbose(String.Format("Database {0} will need a backup first. This is ok if one of the other replicas uses manual seeding.", db));
                                backupNeeded = true;
                            }
                        }
                        else if (String.Equals(SeedingMode, "Manual", StringComparison.OrdinalIgnoreCase))
                        {
                            if (String.Equals(currentReplicaSeedingMode, "Manual", StringComparison.OrdinalIgnoreCase))
                            {
                                WriteMessageVerbose(String.Format("Database {0} will need a restore on replica {1}. The replica is already configured accordingly.", db, replicaName));
                            }
                            else
                            {
                                WriteMessageVerbose(String.Format("Database {0} will need a restore on replica {1}. The replica will be configured accordingly.", db, replicaName));
                            }
                            restoreNeeded[replicaName] = true;
                        }
                        else
                        {
                            // SeedingMode not specified - use current replica configuration
                            if (String.Equals(currentReplicaSeedingMode, "Automatic", StringComparison.OrdinalIgnoreCase))
                            {
                                WriteMessageVerbose(String.Format("Database {0} will use automatic seeding on replica {1}.", db, replicaName));
                                if (lastBackupYear == 1)
                                {
                                    WriteMessageVerbose(String.Format("Database {0} will need a backup first. This is ok if one of the other replicas uses manual seeding.", db));
                                    backupNeeded = true;
                                }
                            }
                            else
                            {
                                WriteMessageVerbose(String.Format("Database {0} will need a restore on replica {1}.", db, replicaName));
                                restoreNeeded[replicaName] = true;
                            }
                        }
                    }
                    replicaServerSMO[replicaName] = replicaParent;
                }

                if (failure)
                {
                    StopFunction(
                        String.Format("Availability Group {0} or database {1} not found in suitable state on all secondary replicas.", AvailabilityGroup, db),
                        target: db,
                        isContinue: true);
                    TestFunctionInterrupt();
                    continue;
                }

                if (restoreNeeded.Count > 0 && String.IsNullOrEmpty(SharedPath) && !UseLastBackup.IsPresent)
                {
                    StopFunction(
                        String.Format("A restore of database {0} is needed on one or more replicas, but -SharedPath or -UseLastBackup are missing.", db),
                        target: db,
                        isContinue: true);
                    TestFunctionInterrupt();
                    continue;
                }

                if (backupNeeded && restoreNeeded.Count == 0)
                {
                    StopFunction(
                        String.Format("All replicas are configured to use automatic seeding, but the database {0} was never backed up. Please backup the database or use manual seeding.", db),
                        target: db,
                        isContinue: true);
                    TestFunctionInterrupt();
                    continue;
                }

                // Build output object
                string agName = GetPropertyString(ag, "Name");
                string dbOutputName = GetPropertyString(db, "Name");

                PSObject output = new PSObject();
                output.Properties.Add(new PSNoteProperty("ComputerName", GetPropertyString(ag, "ComputerName")));
                output.Properties.Add(new PSNoteProperty("InstanceName", GetPropertyString(ag, "InstanceName")));
                output.Properties.Add(new PSNoteProperty("SqlInstance", GetPropertyString(ag, "SqlInstance")));
                output.Properties.Add(new PSNoteProperty("AvailabilityGroupName", agName));
                output.Properties.Add(new PSNoteProperty("DatabaseName", dbOutputName));
                output.Properties.Add(new PSNoteProperty("AvailabilityGroupSMO", ag.BaseObject ?? ag));
                output.Properties.Add(new PSNoteProperty("DatabaseSMO", db.BaseObject ?? db));
                output.Properties.Add(new PSNoteProperty("PrimaryServerSMO", server));
                output.Properties.Add(new PSNoteProperty("ReplicaServerSMO", replicaServerSMO));
                output.Properties.Add(new PSNoteProperty("RestoreNeeded", restoreNeeded));

                // Convert backups to array
                object[] backupArray;
                if (backups != null && backups.Count > 0)
                {
                    backupArray = new object[backups.Count];
                    for (int i = 0; i < backups.Count; i++)
                        backupArray[i] = backups[i];
                }
                else
                {
                    backupArray = new object[0];
                }
                output.Properties.Add(new PSNoteProperty("Backups", backupArray));

                WriteObject(output);
            }
        }

        #region Helpers

        /// <summary>
        /// Connects to a SQL Server instance using Connect-DbaInstance.
        /// </summary>
        private object ConnectInstance(object instance, PSCredential credential)
        {
            Collection<PSObject> results;
            if (credential != null)
            {
                results = InvokeCommand.InvokeScript(
                    false, _connectScript, null,
                    new object[] { instance, credential });
            }
            else
            {
                results = InvokeCommand.InvokeScript(
                    false, _connectNoCred, null,
                    new object[] { instance });
            }

            if (results != null && results.Count > 0)
                return results[0].BaseObject;
            return null;
        }

        /// <summary>
        /// Gets a database object from a server by name.
        /// </summary>
        private PSObject GetDatabase(object server, string dbName)
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, _getDatabasesScript, null,
                    new object[] { server, dbName });
                if (results != null && results.Count > 0 && results[0] != null)
                    return results[0];
            }
            catch (Exception)
            {
                // Database not found
            }
            return null;
        }

        /// <summary>
        /// Gets the VersionMajor from a server object.
        /// </summary>
        private int GetVersionMajor(object server)
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, _getVersionMajor, null,
                    new object[] { server });
                if (results != null && results.Count > 0 && results[0] != null)
                {
                    object val = results[0].BaseObject ?? results[0];
                    if (val is int intVal)
                        return intVal;
                    int parsed;
                    if (int.TryParse(val.ToString(), out parsed))
                        return parsed;
                }
            }
            catch (Exception)
            {
                // Default to 0
            }
            return 0;
        }

        /// <summary>
        /// Gets the DomainInstanceName from an AG's parent server (replicaAg.Parent.DomainInstanceName).
        /// </summary>
        private string GetDomainInstanceName(PSObject agObj)
        {
            try
            {
                object parent = GetParentFromAg(agObj);
                if (parent != null)
                {
                    Collection<PSObject> results = InvokeCommand.InvokeScript(
                        false, _getDomainInstanceName, null,
                        new object[] { parent });
                    if (results != null && results.Count > 0 && results[0] != null)
                        return results[0].BaseObject != null ? results[0].BaseObject.ToString() : results[0].ToString();
                }
            }
            catch (Exception)
            {
                // Fallback
            }
            return null;
        }

        /// <summary>
        /// Gets the Parent (Server) object from an AG object.
        /// </summary>
        private object GetParentFromAg(PSObject agObj)
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, _getParentScript, null,
                    new object[] { agObj });
                if (results != null && results.Count > 0 && results[0] != null)
                    return results[0].BaseObject ?? results[0];
            }
            catch (Exception)
            {
                // Fallback
            }
            return null;
        }

        /// <summary>
        /// Gets the seeding mode for a replica in an AG.
        /// </summary>
        private string GetReplicaSeedingMode(PSObject agObj, string replicaName)
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, _getReplicaSeedingMode, null,
                    new object[] { agObj, replicaName });
                if (results != null && results.Count > 0 && results[0] != null)
                {
                    object val = results[0].BaseObject ?? results[0];
                    return val.ToString();
                }
            }
            catch (Exception)
            {
                // Fallback
            }
            return null;
        }

        /// <summary>
        /// Gets the year from a database's LastBackupDate property.
        /// </summary>
        private int GetLastBackupYear(PSObject db)
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, _getLastBackupDate, null,
                    new object[] { db });
                if (results != null && results.Count > 0 && results[0] != null)
                {
                    object val = results[0].BaseObject ?? results[0];
                    if (val is int intVal)
                        return intVal;
                    int parsed;
                    if (int.TryParse(val.ToString(), out parsed))
                        return parsed;
                }
            }
            catch (Exception)
            {
                // Default
            }
            return 0;
        }

        /// <summary>
        /// Checks if backups collection contains a Log type backup.
        /// </summary>
        internal static bool BackupsContainLog(Collection<PSObject> backups)
        {
            if (backups == null || backups.Count == 0)
                return false;

            foreach (PSObject backup in backups)
            {
                if (backup == null) continue;
                string type = GetPropertyStringStatic(backup, "Type");
                if (String.Equals(type, "Log", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Gets a string property value from a PSObject (static version).
        /// </summary>
        internal static string GetPropertyStringStatic(PSObject obj, string propertyName)
        {
            if (obj == null) return null;
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

        #endregion Helpers
    }
}
