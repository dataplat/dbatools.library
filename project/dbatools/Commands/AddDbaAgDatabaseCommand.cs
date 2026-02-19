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
    /// Adds databases to an Availability Group with automated backup, restore, and synchronization handling.
    /// Handles the complete process from backup through synchronization including seeding mode configuration,
    /// backup/restore operations, and replica synchronization with progress reporting.
    /// </summary>
    [Cmdlet("Add", "DbaAgDatabase", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low, DefaultParameterSetName = "NonPipeline")]
    public class AddDbaAgDatabaseCommand : DbaBaseCmdlet
    {
        #region Parameters

        /// <summary>
        /// The primary replica of the Availability Group. Server version must be SQL Server version 2012 or higher.
        /// </summary>
        [Parameter(ParameterSetName = "NonPipeline", Mandatory = true, Position = 0)]
        public DbaInstanceParameter SqlInstance { get; set; }

        /// <summary>
        /// Login to the target instance using alternative credentials.
        /// </summary>
        [Parameter(ParameterSetName = "NonPipeline")]
        public PSCredential SqlCredential { get; set; }

        /// <summary>
        /// The target Availability Group name where databases will be added.
        /// </summary>
        [Parameter(ParameterSetName = "NonPipeline", Mandatory = true)]
        [Parameter(ParameterSetName = "Pipeline", Mandatory = true, Position = 0)]
        public string AvailabilityGroup { get; set; }

        /// <summary>
        /// Databases to add to the Availability Group.
        /// </summary>
        [Parameter(ParameterSetName = "NonPipeline", Mandatory = true)]
        public string[] Database { get; set; }

        /// <summary>
        /// Secondary replica instances to target. Auto-discovered if not specified.
        /// </summary>
        [Parameter(ParameterSetName = "NonPipeline")]
        [Parameter(ParameterSetName = "Pipeline")]
        public DbaInstanceParameter[] Secondary { get; set; }

        /// <summary>
        /// Authentication credentials for connecting to secondary replica instances.
        /// </summary>
        [Parameter(ParameterSetName = "NonPipeline")]
        [Parameter(ParameterSetName = "Pipeline")]
        public PSCredential SecondarySqlCredential { get; set; }

        /// <summary>
        /// Accepts database objects from pipeline input, typically from Get-DbaDatabase.
        /// </summary>
        [Parameter(ValueFromPipeline = true, ParameterSetName = "Pipeline", Mandatory = true)]
        public object[] InputObject { get; set; }

        /// <summary>
        /// Controls how database data is transferred to secondary replicas during AG addition.
        /// </summary>
        [Parameter(ParameterSetName = "NonPipeline")]
        [Parameter(ParameterSetName = "Pipeline")]
        [ValidateSet("Automatic", "Manual")]
        public string SeedingMode { get; set; }

        /// <summary>
        /// UNC network path where backups are stored during manual seeding operations.
        /// </summary>
        [Parameter(ParameterSetName = "NonPipeline")]
        [Parameter(ParameterSetName = "Pipeline")]
        public string SharedPath { get; set; }

        /// <summary>
        /// Uses existing backup history instead of creating new backups for manual seeding.
        /// </summary>
        [Parameter(ParameterSetName = "NonPipeline")]
        [Parameter(ParameterSetName = "Pipeline")]
        public SwitchParameter UseLastBackup { get; set; }

        /// <summary>
        /// Additional parameters for Backup-DbaDatabase as a hashtable.
        /// </summary>
        [Parameter(ParameterSetName = "NonPipeline")]
        [Parameter(ParameterSetName = "Pipeline")]
        public Hashtable AdvancedBackupParams { get; set; }

        /// <summary>
        /// Skips waiting for database synchronization to complete on secondary replicas.
        /// </summary>
        [Parameter(ParameterSetName = "NonPipeline")]
        [Parameter(ParameterSetName = "Pipeline")]
        public SwitchParameter NoWait { get; set; }

        /// <summary>
        /// Prevents restores from using the source server's folder structure on secondary replicas.
        /// </summary>
        [Parameter(ParameterSetName = "NonPipeline")]
        [Parameter(ParameterSetName = "Pipeline")]
        public SwitchParameter SkipReuseSourceFolderStructure { get; set; }

        #endregion Parameters

        #region Script Blocks

        private static readonly ScriptBlock _getConfigValue =
            ScriptBlock.Create("param($name, $fallback) Get-DbatoolsConfigValue -FullName $name -Fallback $fallback");

        private static readonly ScriptBlock _testDbaAg =
            ScriptBlock.Create("param($splat) Test-DbaAvailabilityGroup @splat");

        private static readonly ScriptBlock _setSeedingModeAlter = ScriptBlock.Create(@"
param($replica, $mode)
$replica.SeedingMode = $mode
$replica.Alter()");

        private static readonly ScriptBlock _grantAgPermission = ScriptBlock.Create(@"
param($si, $ag)
$null = Grant-DbaAgPermission -SqlInstance $si -Type AvailabilityGroup -AvailabilityGroup $ag -Permission CreateAnyDatabase");

        private static readonly ScriptBlock _backupDb = ScriptBlock.Create(@"
param($db, $path, $type)
$db | Backup-DbaDatabase -BackupDirectory $path -Type $type -EnableException");

        private static readonly ScriptBlock _backupDbAdv = ScriptBlock.Create(@"
param($db, $path, $type, $advParams)
$db | Backup-DbaDatabase -BackupDirectory $path -Type $type -EnableException @advParams");

        private static readonly ScriptBlock _restoreDb = ScriptBlock.Create(@"
param($backups, $restoreParams)
$null = $backups | Restore-DbaDatabase @restoreParams");

        private static readonly ScriptBlock _getAgDb = ScriptBlock.Create(@"
param($si, $ag, $db, $ee)
if ($ee) {
    Get-DbaAgDatabase -SqlInstance $si -AvailabilityGroup $ag -Database $db -EnableException
} else {
    Get-DbaAgDatabase -SqlInstance $si -AvailabilityGroup $ag -Database $db
}");

        private static readonly ScriptBlock _getAgDbNames =
            ScriptBlock.Create("param($ag) @($ag.AvailabilityDatabases.Name)");

        private static readonly ScriptBlock _createAgDb = ScriptBlock.Create(@"
param($ag, $dbName)
$agDb = New-Object Microsoft.SqlServer.Management.Smo.AvailabilityDatabase($ag, $dbName)
$agDb.Create()
$agDb");

        private static readonly ScriptBlock _getObjState =
            ScriptBlock.Create("param($obj) $obj.State.ToString()");

        private static readonly ScriptBlock _refreshObj =
            ScriptBlock.Create("param($obj) $obj.Refresh()");

        private static readonly ScriptBlock _joinAgDb =
            ScriptBlock.Create("param($agDb) $agDb.JoinAvailablityGroup()");

        private static readonly ScriptBlock _getReplicaProps = ScriptBlock.Create(@"
param($ag, $name)
$r = $ag.AvailabilityReplicas[$name]
@{
    SeedingMode = $r.SeedingMode.ToString()
    AvailabilityMode = $r.AvailabilityMode.ToString()
    EndpointUrl = $r.EndpointUrl
    UniqueId = $r.UniqueId.Guid.ToUpper()
}");

        private static readonly ScriptBlock _getReplicaObj =
            ScriptBlock.Create("param($ag, $name) $ag.AvailabilityReplicas[$name]");

        private static readonly ScriptBlock _getHostPlatform =
            ScriptBlock.Create("param($s) $s.HostPlatform");

        private static readonly ScriptBlock _getDbOwner =
            ScriptBlock.Create("param($db) $db.Owner");

        private static readonly ScriptBlock _getConnectedAs =
            ScriptBlock.Create("param($s) $s.ConnectedAs");

        private static readonly ScriptBlock _hasLogin =
            ScriptBlock.Create("param($s, $name) $null -ne $s.Logins[$name]");

        private static readonly ScriptBlock _querySeedingStats = ScriptBlock.Create(@"
param($server, $dbName, $epUrl)
$safeName = $dbName -replace ""'"", ""''""
$safeUrl  = $epUrl  -replace ""'"", ""''""
$server.Query(""SELECT TOP 1 * FROM sys.dm_hadr_physical_seeding_stats WHERE local_database_name = '$safeName' COLLATE SQL_Latin1_General_CP1_CI_AS AND remote_machine_name = '$safeUrl' COLLATE SQL_Latin1_General_CP1_CI_AS ORDER BY start_time_utc DESC"")");

        private static readonly ScriptBlock _queryAutoSeeding = ScriptBlock.Create(@"
param($server, $agId, $agDbId, $repId)
$guidPattern = '^[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}$'
if ($agId -notmatch $guidPattern -or $agDbId -notmatch $guidPattern -or $repId -notmatch $guidPattern) { return }
$server.Query(""SELECT TOP 1 * FROM sys.dm_hadr_automatic_seeding WHERE ag_id = '$agId' COLLATE SQL_Latin1_General_CP1_CI_AS AND ag_db_id = '$agDbId' COLLATE SQL_Latin1_General_CP1_CI_AS AND ag_remote_replica_id = '$repId' COLLATE SQL_Latin1_General_CP1_CI_AS ORDER BY start_time DESC"")");

        private static readonly ScriptBlock _getAgUniqueId =
            ScriptBlock.Create("param($ag) $ag.UniqueId.Guid.ToUpper()");

        private static readonly ScriptBlock _getAgDbUniqueId =
            ScriptBlock.Create("param($ag, $name) $ag.AvailabilityDatabases[$name].UniqueId.Guid.ToUpper()");

        #endregion Script Blocks

        #region Private State

        private int _timeoutExisting;
        private int _timeoutSynchronization;
        private int _waitWhile;
        private bool _reportSeeding;
        private int _progressId;

        #endregion Private State

        /// <summary>
        /// Reads configuration values for timeouts and wait intervals.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            _timeoutExisting = GetConfigInt("commands.add-dbaagdatabase.timeout.existing", 60);
            _timeoutSynchronization = GetConfigInt("commands.add-dbaagdatabase.timeout.synchronization", 86400);
            _waitWhile = GetConfigInt("commands.add-dbaagdatabase.wait.while", 100);
            _reportSeeding = GetConfigBool("commands.add-dbaagdatabase.report.seeding", true);
            _progressId = new Random().Next();
        }

        /// <summary>
        /// Processes databases for addition to the availability group.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (TestFunctionInterrupt())
                return;

            List<PSObject> testResults = new List<PSObject>();

            // Process Database parameter (NonPipeline set)
            if (Database != null)
            {
                foreach (string dbName in Database)
                {
                    try
                    {
                        WriteMainProgress(String.Format("Test prerequisites for joining database {0}", dbName));
                        Hashtable testSplat = BuildTestSplat(dbName, SqlInstance, SqlCredential);
                        Collection<PSObject> results = InvokeCommand.InvokeScript(
                            false, _testDbaAg, null, new object[] { testSplat });
                        AddNonNullResults(testResults, results);
                    }
                    catch (Exception ex)
                    {
                        StopFunction(
                            String.Format("Testing prerequisites for joining database {0} to Availability Group {1} failed.", dbName, AvailabilityGroup),
                            errorRecord: new ErrorRecord(ex, "AddDbaAgDatabase_TestPrereqs", ErrorCategory.InvalidOperation, dbName),
                            target: dbName, isContinue: true);
                        TestFunctionInterrupt();
                    }
                }
            }

            // Process InputObject parameter (Pipeline set)
            if (InputObject != null)
            {
                foreach (object dbObj in InputObject)
                {
                    if (dbObj == null) continue;
                    string dbName = GetPSPropertyString(dbObj, "Name");
                    try
                    {
                        WriteMainProgress(String.Format("Test prerequisites for joining database {0}", dbName));
                        object parent = GetPSPropertyValue(dbObj, "Parent");
                        Hashtable testSplat = BuildTestSplat(dbName, parent, null);
                        Collection<PSObject> results = InvokeCommand.InvokeScript(
                            false, _testDbaAg, null, new object[] { testSplat });
                        AddNonNullResults(testResults, results);
                    }
                    catch (Exception ex)
                    {
                        StopFunction(
                            String.Format("Testing prerequisites for joining database {0} to Availability Group {1} failed.", dbName ?? "unknown", AvailabilityGroup),
                            errorRecord: new ErrorRecord(ex, "AddDbaAgDatabase_TestPrereqs", ErrorCategory.InvalidOperation, dbObj),
                            target: dbObj, isContinue: true);
                        TestFunctionInterrupt();
                    }
                }
            }

            WriteMessageAtLevel(
                String.Format("Test for prerequisites returned {0} databases that will be joined to the Availability Group {1}.", testResults.Count, AvailabilityGroup),
                MessageLevel.Verbose, null);

            // Process each test result through the 5-step workflow
            foreach (PSObject result in testResults)
            {
                ProcessTestResult(result);
            }

            CompleteMainProgress();
        }

        #region 5-Step Workflow

        /// <summary>
        /// Processes a single test result through the 5-step AG database addition workflow.
        /// </summary>
        private void ProcessTestResult(PSObject result)
        {
            object server = GetPSPropertyValue(result, "PrimaryServerSMO");
            object ag = GetPSPropertyValue(result, "AvailabilityGroupSMO");
            object db = GetPSPropertyValue(result, "DatabaseSMO");
            Hashtable replicaServerSMO = UnwrapHashtable(GetPSPropertyValue(result, "ReplicaServerSMO"));
            Hashtable restoreNeeded = UnwrapHashtable(GetPSPropertyValue(result, "RestoreNeeded"));
            object[] backups = UnwrapObjectArray(GetPSPropertyValue(result, "Backups"));

            string dbName = GetPSPropertyString(db, "Name");
            List<object> output = new List<object>();
            Hashtable replicaAgDbSMO = new Hashtable(StringComparer.OrdinalIgnoreCase);
            Hashtable targetSynchronizationState = new Hashtable(StringComparer.OrdinalIgnoreCase);

            WriteMainProgressActivity(String.Format("Adding database {0} to Availability Group {1}", dbName, AvailabilityGroup));

            // ===== Step 1: Setting seeding mode if needed =====
            if (!Step1SetSeedingMode(server, ag, replicaServerSMO, dbName))
                return;

            // ===== Step 2: Running backup and restore if needed =====
            if (!Step2BackupRestore(server, db, dbName, replicaServerSMO, restoreNeeded, ref backups))
                return;

            // ===== Step 3: Add the database to the AG on the primary replica =====
            if (!Step3AddPrimary(server, ag, dbName, output))
                return;

            // ===== Step 4: Add the database to the AG on the secondary replicas =====
            List<string> replicaKeys = GetHashtableKeys(replicaServerSMO);
            if (!Step4AddSecondaries(ag, dbName, replicaServerSMO, replicaKeys, replicaAgDbSMO, targetSynchronizationState, output))
                return;

            // ===== Step 5: Wait for synchronization =====
            Step5WaitSync(server, ag, dbName, replicaServerSMO, replicaKeys, replicaAgDbSMO, targetSynchronizationState);

            // Output results
            foreach (object obj in output)
            {
                if (obj != null) WriteObject(obj);
            }
        }

        /// <summary>
        /// Step 1: Set seeding mode on replicas if SeedingMode parameter was specified.
        /// </summary>
        /// <returns>True to continue, false to abort this database.</returns>
        private bool Step1SetSeedingMode(object server, object ag, Hashtable replicaServerSMO, string dbName)
        {
            WriteMainProgress("Step 1/5: Setting seeding mode if needed");
            WriteMessageAtLevel("Step 1/5: Setting seeding mode if needed", MessageLevel.Verbose, null);

            if (!TestBound("SeedingMode"))
                return true;

            WriteMessageAtLevel(String.Format("Setting seeding mode to {0}.", SeedingMode), MessageLevel.Verbose, null);
            bool failure = false;

            foreach (string replicaName in GetHashtableKeys(replicaServerSMO))
            {
                Hashtable replicaProps = GetReplicaProperties(ag, replicaName);
                string currentMode = replicaProps != null ? GetHashtableString(replicaProps, "SeedingMode") : null;

                if (String.Equals(currentMode, SeedingMode, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (ShouldProcess(server != null ? server.ToString() : "primary",
                    String.Format("Setting seeding mode for replica {0} to {1}", replicaName, SeedingMode)))
                {
                    try
                    {
                        WriteMessageAtLevel(String.Format("Setting seeding mode for replica {0} to {1}.", replicaName, SeedingMode), MessageLevel.Verbose, null);
                        object replica = GetReplicaObject(ag, replicaName);
                        InvokeCommand.InvokeScript(false, _setSeedingModeAlter, null, new object[] { replica, SeedingMode });

                        if (String.Equals(SeedingMode, "Automatic", StringComparison.OrdinalIgnoreCase))
                        {
                            object replicaServer = replicaServerSMO[replicaName];
                            WriteMessageAtLevel(
                                String.Format("Setting GrantAvailabilityGroupCreateDatabasePrivilege on server {0} for Availability Group {1}.", replicaServer, AvailabilityGroup),
                                MessageLevel.Verbose, null);
                            InvokeCommand.InvokeScript(false, _grantAgPermission, null, new object[] { replicaServer, AvailabilityGroup });
                        }
                    }
                    catch (Exception ex)
                    {
                        failure = true;
                        StopFunction(
                            String.Format("Failed setting seeding mode for replica {0} to {1}.", replicaName, SeedingMode),
                            errorRecord: new ErrorRecord(ex, "AddDbaAgDatabase_SeedingMode", ErrorCategory.InvalidOperation, replicaName),
                            target: replicaName, isContinue: true);
                        TestFunctionInterrupt();
                    }
                }
            }

            if (failure)
            {
                StopFunction(String.Format("Failed setting seeding mode to {0}.", SeedingMode));
                return false;
            }

            return true;
        }

        /// <summary>
        /// Step 2: Run backup and restore if needed for manual seeding.
        /// </summary>
        /// <returns>True to continue, false to abort this database.</returns>
        private bool Step2BackupRestore(object server, object db, string dbName, Hashtable replicaServerSMO, Hashtable restoreNeeded, ref object[] backups)
        {
            WriteMainProgress("Step 2/5: Running backup and restore if needed");
            WriteMessageAtLevel("Step 2/5: Running backup and restore if needed", MessageLevel.Verbose, null);

            if (restoreNeeded == null || restoreNeeded.Count == 0)
                return true;

            bool skipReuseSourceFolderStructure = SkipReuseSourceFolderStructure.IsPresent;

            // Take backups if not using last backup
            if (backups == null || backups.Length == 0)
            {
                if (ShouldProcess(server != null ? server.ToString() : "primary",
                    String.Format("Taking full and log backup of database {0}", dbName)))
                {
                    try
                    {
                        WriteMessageAtLevel(String.Format("Taking full and log backup of database {0}.", dbName), MessageLevel.Verbose, null);
                        object fullbackup;
                        object logbackup;

                        if (AdvancedBackupParams != null)
                        {
                            fullbackup = InvokeScriptSingle(_backupDbAdv, db, SharedPath, "Full", AdvancedBackupParams);
                            logbackup = InvokeScriptSingle(_backupDbAdv, db, SharedPath, "Log", AdvancedBackupParams);
                        }
                        else
                        {
                            fullbackup = InvokeScriptSingle(_backupDb, db, SharedPath, "Full");
                            logbackup = InvokeScriptSingle(_backupDb, db, SharedPath, "Log");
                        }
                        backups = new object[] { fullbackup, logbackup };
                    }
                    catch (Exception ex)
                    {
                        StopFunction(
                            String.Format("Failed to take full and log backup of database {0}.", dbName),
                            errorRecord: new ErrorRecord(ex, "AddDbaAgDatabase_Backup", ErrorCategory.InvalidOperation, dbName),
                            target: dbName, isContinue: true);
                        TestFunctionInterrupt();
                        return false;
                    }
                }
            }

            // Restore to replicas
            bool failure = false;
            foreach (string replicaName in GetHashtableKeys(restoreNeeded))
            {
                object replicaServer = replicaServerSMO[replicaName];
                if (ShouldProcess(replicaServer != null ? replicaServer.ToString() : replicaName,
                    String.Format("Restore database {0} to replica {1}", dbName, replicaName)))
                {
                    try
                    {
                        WriteMessageAtLevel(String.Format("Restore database {0} to replica {1}.", dbName, replicaName), MessageLevel.Verbose, null);

                        Hashtable restoreParams = new Hashtable();
                        restoreParams["SqlInstance"] = replicaServer;
                        restoreParams["NoRecovery"] = true;
                        restoreParams["TrustDbBackupHistory"] = true;
                        restoreParams["EnableException"] = true;

                        // Check platform mismatch
                        if (!skipReuseSourceFolderStructure)
                        {
                            string primaryPlatform = GetStringFromScript(_getHostPlatform, server);
                            string replicaPlatform = GetStringFromScript(_getHostPlatform, replicaServer);
                            if (!String.Equals(primaryPlatform, replicaPlatform, StringComparison.OrdinalIgnoreCase))
                            {
                                WriteMessageAtLevel(
                                    String.Format("Primary platform ({0}) does not match replica platform ({1}). Setting SkipReuseSourceFolderStructure.", primaryPlatform, replicaPlatform),
                                    MessageLevel.Verbose, null);
                                skipReuseSourceFolderStructure = true;
                            }
                        }

                        if (!skipReuseSourceFolderStructure)
                        {
                            WriteMessageAtLevel("Using ReuseSourceFolderStructure to maintain consistent folder layout.", MessageLevel.Verbose, null);
                            restoreParams["ReuseSourceFolderStructure"] = true;
                        }
                        else
                        {
                            WriteMessageAtLevel("Using replica's default paths for database files.", MessageLevel.Verbose, null);
                        }

                        // Check database owner
                        string sourceOwner = GetStringFromScript(_getDbOwner, db);
                        string replicaOwner = GetStringFromScript(_getConnectedAs, replicaServer);
                        if (!String.Equals(sourceOwner, replicaOwner, StringComparison.OrdinalIgnoreCase))
                        {
                            WriteMessageAtLevel(
                                String.Format("Source database owner is {0}, replica database owner would be {1}.", sourceOwner, replicaOwner),
                                MessageLevel.Verbose, null);

                            if (GetBoolFromScript(_hasLogin, replicaServer, sourceOwner))
                            {
                                WriteMessageAtLevel("Source database owner is found on replica, so using ExecuteAs with Restore-DbaDatabase to set correct owner.", MessageLevel.Verbose, null);
                                restoreParams["ExecuteAs"] = sourceOwner;
                            }
                            else
                            {
                                WriteMessageAtLevel("Source database owner is not found on replica, so there is nothing we can do.", MessageLevel.Verbose, null);
                            }
                        }

                        InvokeCommand.InvokeScript(false, _restoreDb, null, new object[] { backups, restoreParams });
                    }
                    catch (Exception ex)
                    {
                        failure = true;
                        StopFunction(
                            String.Format("Failed to restore database {0} to replica {1}.", dbName, replicaName),
                            errorRecord: new ErrorRecord(ex, "AddDbaAgDatabase_Restore", ErrorCategory.InvalidOperation, replicaName),
                            target: replicaName, isContinue: true);
                        TestFunctionInterrupt();
                    }
                }
            }

            if (failure)
            {
                StopFunction(String.Format("Failed to restore database {0}.", dbName));
                return false;
            }

            return true;
        }

        /// <summary>
        /// Step 3: Add the database to the AG on the primary replica.
        /// </summary>
        /// <returns>True to continue, false to abort this database.</returns>
        private bool Step3AddPrimary(object server, object ag, string dbName, List<object> output)
        {
            WriteMainProgress("Step 3/5: Add the database to the Availability Group on the primary replica");
            WriteMessageAtLevel("Step 3/5: Add the database to the Availability Group on the primary replica", MessageLevel.Verbose, null);

            if (!ShouldProcess(server != null ? server.ToString() : "primary",
                String.Format("Add database {0} to Availability Group {1} on the primary replica", dbName, AvailabilityGroup)))
                return true;

            try
            {
                // Check if already in AG
                if (IsDbAlreadyInAg(ag, dbName))
                {
                    WriteMessageAtLevel(
                        String.Format("Database {0} is already joined to Availability Group {1}. No action will be taken on the primary replica.", dbName, AvailabilityGroup),
                        MessageLevel.Verbose, null);
                    return true;
                }

                WriteMessageAtLevel(
                    String.Format("Object of type AvailabilityDatabase for {0} will be created.", dbName),
                    MessageLevel.Verbose, null);

                // Create AvailabilityDatabase on primary
                Collection<PSObject> createResults = InvokeCommand.InvokeScript(
                    false, _createAgDb, null, new object[] { ag, dbName });
                object agDb = (createResults != null && createResults.Count > 0 && createResults[0] != null)
                    ? (createResults[0].BaseObject ?? createResults[0])
                    : null;

                if (agDb != null)
                {
                    string state = GetObjectState(agDb);
                    WriteMessageAtLevel(
                        String.Format("Object of type AvailabilityDatabase for {0} is created. State is {1}", dbName, state),
                        MessageLevel.Verbose, null);

                    // Wait for state to become Existing
                    DateTime timeout = DateTime.Now.AddSeconds(_timeoutExisting);
                    while (!String.Equals(state, "Existing", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteMainProgress(
                            String.Format("State of AvailabilityDatabase for {0} is {1}, waiting for Existing", dbName, state));
                        WriteMessageAtLevel(
                            String.Format("State of AvailabilityDatabase for {0} is {1}, waiting for Existing", dbName, state),
                            MessageLevel.Verbose, null);

                        if (DateTime.Now > timeout)
                        {
                            StopFunction(
                                String.Format("Failed to add database {0} to Availability Group {1}. Timeout of {2} seconds is reached. State of AvailabilityDatabase for {0} is still {3}.",
                                    dbName, AvailabilityGroup, _timeoutExisting, state),
                                isContinue: true);
                            TestFunctionInterrupt();
                            return false;
                        }
                        System.Threading.Thread.Sleep(_waitWhile);
                        RefreshObject(agDb);
                        state = GetObjectState(agDb);
                    }
                }

                // Get customized SMO for output
                object outputObj = GetAgDatabase(server, AvailabilityGroup, dbName, true);
                if (outputObj != null) output.Add(outputObj);
            }
            catch (Exception ex)
            {
                StopFunction(
                    String.Format("Failed to add database {0} to Availability Group {1}", dbName, AvailabilityGroup),
                    errorRecord: new ErrorRecord(ex, "AddDbaAgDatabase_AddPrimary", ErrorCategory.InvalidOperation, dbName),
                    target: dbName, isContinue: true);
                TestFunctionInterrupt();
                return false;
            }

            return true;
        }

        /// <summary>
        /// Step 4: Add the database to the AG on secondary replicas.
        /// </summary>
        /// <returns>True to continue, false to abort this database.</returns>
        private bool Step4AddSecondaries(object ag, string dbName, Hashtable replicaServerSMO, List<string> replicaKeys,
            Hashtable replicaAgDbSMO, Hashtable targetSynchronizationState, List<object> output)
        {
            WriteMainProgress("Step 4/5: Add the database to the Availability Group on the secondary replicas");
            WriteMessageAtLevel("Step 4/5: Add the database to the Availability Group on the secondary replicas", MessageLevel.Verbose, null);

            bool failure = false;

            foreach (string replicaName in replicaKeys)
            {
                object replicaServer = replicaServerSMO[replicaName];
                if (!ShouldProcess(replicaServer != null ? replicaServer.ToString() : replicaName,
                    String.Format("Add database {0} to Availability Group {1} on replica {2}", dbName, AvailabilityGroup, replicaName)))
                    continue;

                WriteMessageAtLevel(
                    String.Format("State of AvailabilityDatabase for {0} on replica {1} is not yet known", dbName, replicaName),
                    MessageLevel.Verbose, null);

                // Get AG database on replica
                object replicaAgDb;
                try
                {
                    replicaAgDb = GetAgDatabase(replicaServer, AvailabilityGroup, dbName, true);
                }
                catch (Exception ex)
                {
                    failure = true;
                    StopFunction(
                        String.Format("Failed to get database {0} on replica {1}.", dbName, replicaName),
                        errorRecord: new ErrorRecord(ex, "AddDbaAgDatabase_GetReplicaDb", ErrorCategory.InvalidOperation, replicaName),
                        target: replicaName, isContinue: true);
                    TestFunctionInterrupt();
                    continue;
                }

                bool isJoined = GetBoolProperty(replicaAgDb, "IsJoined");

                if (isJoined)
                {
                    WriteMessageAtLevel(
                        String.Format("Database {0} is already joined to Availability Group {1}. No action will be taken on the replica {2}.", dbName, AvailabilityGroup, replicaName),
                        MessageLevel.Verbose, null);
                    replicaAgDbSMO[replicaName] = replicaAgDb;
                    continue;
                }

                // Save for output and further processing
                output.Add(replicaAgDb);
                replicaAgDbSMO[replicaName] = replicaAgDb;

                // Determine target synchronization state
                Hashtable replicaProps = GetReplicaProperties(ag, replicaName);
                string availabilityMode = replicaProps != null ? GetHashtableString(replicaProps, "AvailabilityMode") : null;

                if (String.Equals(availabilityMode, "AsynchronousCommit", StringComparison.OrdinalIgnoreCase))
                {
                    targetSynchronizationState[replicaName] = "Synchronizing";
                }
                else if (String.Equals(availabilityMode, "SynchronousCommit", StringComparison.OrdinalIgnoreCase))
                {
                    targetSynchronizationState[replicaName] = "Synchronized";
                }
                else
                {
                    failure = true;
                    StopFunction(
                        String.Format("Unexpected value '{0}' for AvailabilityMode on replica {1}.", availabilityMode, replicaName),
                        isContinue: true);
                    TestFunctionInterrupt();
                    continue;
                }

                // Wait for state to become Existing
                string state = GetObjectState(replicaAgDb);
                WriteMessageAtLevel(
                    String.Format("State of AvailabilityDatabase for {0} on replica {1} is {2}", dbName, replicaName, state),
                    MessageLevel.Verbose, null);

                DateTime timeout = DateTime.Now.AddSeconds(_timeoutExisting);
                while (!String.Equals(state, "Existing", StringComparison.OrdinalIgnoreCase))
                {
                    WriteMessageAtLevel(
                        String.Format("State of AvailabilityDatabase for {0} on replica {1} is {2}, waiting for Existing.", dbName, replicaName, state),
                        MessageLevel.Verbose, null);

                    if (DateTime.Now > timeout)
                    {
                        StopFunction(
                            String.Format("Failed to add database {0} on replica {1}. Timeout of {2} seconds is reached. State of AvailabilityDatabase for {3} is still {4}.",
                                dbName, replicaName, _timeoutExisting, dbName, state),
                            isContinue: true);
                        TestFunctionInterrupt();
                        break;
                    }
                    System.Threading.Thread.Sleep(_waitWhile);
                    RefreshObject(replicaAgDb);
                    state = GetObjectState(replicaAgDb);
                }

                // With automatic seeding, JoinAvailablityGroup() is not needed
                string seedingMode = replicaProps != null ? GetHashtableString(replicaProps, "SeedingMode") : null;
                if (!String.Equals(seedingMode, "Automatic", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        WriteMessageAtLevel(
                            String.Format("Joining database {0} on replica {1}", dbName, replicaName),
                            MessageLevel.Verbose, null);
                        // NOTE: JoinAvailablityGroup() is a typo in SMO - do NOT fix
                        InvokeCommand.InvokeScript(false, _joinAgDb, null, new object[] { replicaAgDb });
                    }
                    catch (Exception ex)
                    {
                        failure = true;
                        StopFunction(
                            String.Format("Failed to join database {0} on replica {1}.", dbName, replicaName),
                            errorRecord: new ErrorRecord(ex, "AddDbaAgDatabase_JoinReplica", ErrorCategory.InvalidOperation, replicaName),
                            target: replicaName, isContinue: true);
                        TestFunctionInterrupt();
                    }
                }
            }

            if (failure)
            {
                StopFunction(String.Format("Failed to add or join database {0}.", dbName));
                return false;
            }

            return true;
        }

        /// <summary>
        /// Step 5: Wait for the database to finish joining the AG on secondary replicas.
        /// </summary>
        private void Step5WaitSync(object server, object ag, string dbName, Hashtable replicaServerSMO,
            List<string> replicaKeys, Hashtable replicaAgDbSMO, Hashtable targetSynchronizationState)
        {
            WriteMainProgress("Step 5/5: Wait for the database to finish joining the Availability Group on the secondary replicas");
            WriteMessageAtLevel("Step 5/5: Wait for the database to finish joining the Availability Group on the secondary replicas", MessageLevel.Verbose, null);

            if (NoWait.IsPresent)
            {
                WriteMessageAtLevel(
                    String.Format("NoWait parameter specified. Skipping wait for database {0} to finish joining the Availability Group {1} on the secondary replicas. Synchronization will continue in the background.", dbName, AvailabilityGroup),
                    MessageLevel.Verbose, null);
                return;
            }

            if (!ShouldProcess(server != null ? server.ToString() : "primary",
                String.Format("Wait for the database {0} to finish joining the Availability Group {1} on the secondary replicas.", dbName, AvailabilityGroup)))
                return;

            // Set up sync progress IDs and cache replica properties
            Hashtable syncProgressId = new Hashtable(StringComparer.OrdinalIgnoreCase);
            Hashtable cachedReplicaProps = new Hashtable(StringComparer.OrdinalIgnoreCase);
            Random rng = new Random();
            foreach (string replicaName in replicaKeys)
            {
                syncProgressId[replicaName] = rng.Next();
                cachedReplicaProps[replicaName] = GetReplicaProperties(ag, replicaName);
            }

            bool stillWaiting = true;
            DateTime syncTimeout = DateTime.Now.AddSeconds(_timeoutSynchronization);
            while (stillWaiting)
            {
                stillWaiting = false;
                bool syncFailure = false;

                foreach (string replicaName in replicaKeys)
                {
                    if (!targetSynchronizationState.ContainsKey(replicaName) || targetSynchronizationState[replicaName] == null)
                    {
                        WriteMessageAtLevel(
                            String.Format("Database {0} is already joined to Availability Group {1}. No action will be taken on the replica {2}.", dbName, AvailabilityGroup, replicaName),
                            MessageLevel.Verbose, null);
                        continue;
                    }

                    object replicaAgDb = replicaAgDbSMO[replicaName];
                    bool isJoined = GetBoolProperty(replicaAgDb, "IsJoined");
                    string syncState = GetPSPropertyString(replicaAgDb, "SynchronizationState");
                    string targetState = targetSynchronizationState[replicaName] as string;

                    if (!isJoined || !String.Equals(syncState, targetState, StringComparison.OrdinalIgnoreCase))
                    {
                        stillWaiting = true;
                    }

                    // Build progress status
                    string status;
                    if (!String.Equals(syncState, targetState, StringComparison.OrdinalIgnoreCase))
                    {
                        status = String.Format("IsJoined is {0}, SynchronizationState is {1}, waiting for {2}", isJoined, syncState, targetState);
                    }
                    else
                    {
                        status = String.Format("IsJoined is {0}, SynchronizationState is {1}, replica is in desired state", isJoined, syncState);
                    }

                    // Seeding progress for automatic seeding
                    string currentOperation = null;
                    int percentComplete = -1;
                    int secondsRemaining = -1;

                    Hashtable replicaProps = cachedReplicaProps[replicaName] as Hashtable;
                    string repSeedingMode = replicaProps != null ? GetHashtableString(replicaProps, "SeedingMode") : null;

                    if (String.Equals(repSeedingMode, "Automatic", StringComparison.OrdinalIgnoreCase) && _reportSeeding)
                    {
                        string endpointUrl = replicaProps != null ? GetHashtableString(replicaProps, "EndpointUrl") : null;
                        PSObject seedingStats = QuerySeedingStats(server, dbName, endpointUrl);
                        if (seedingStats != null)
                        {
                            object failureMessageRaw = GetPSPropertyValue(seedingStats, "failure_message");
                            if (IsNonNullNonDbNull(failureMessageRaw))
                            {
                                syncFailure = true;
                                StopFunction(
                                    String.Format("Failed while seeding database {0} to {1}. failure_message: {2}.", dbName, replicaName, failureMessageRaw),
                                    isContinue: true);
                                TestFunctionInterrupt();
                                continue;
                            }

                            long transferred = GetPSPropertyLong(seedingStats, "transferred_size_bytes");
                            long dbSize = GetPSPropertyLong(seedingStats, "database_size_bytes");
                            string internalState = GetPSPropertyString(seedingStats, "internal_state_desc");

                            if (dbSize > 0)
                            {
                                percentComplete = (int)(transferred * 100.0 / dbSize);
                                currentOperation = String.Format("Seeding state: {0}, {1} out of {2} MB transferred",
                                    internalState, transferred / 1024 / 1024, dbSize / 1024 / 1024);
                            }

                            object estTime = GetPSPropertyValue(seedingStats, "estimate_time_complete_utc");
                            if (estTime is DateTime estDateTime)
                            {
                                secondsRemaining = (int)(estDateTime - DateTime.UtcNow).TotalSeconds;
                                if (secondsRemaining < 0) secondsRemaining = 0;
                            }
                        }

                        // Check automatic seeding state
                        string agUniqueId = GetUniqueIdFromScript(_getAgUniqueId, ag);
                        string agDbUniqueId = GetAgDbUniqueId(ag, dbName);
                        string replicaUniqueId = replicaProps != null ? GetHashtableString(replicaProps, "UniqueId") : null;
                        PSObject autoSeeding = QueryAutoSeeding(server, agUniqueId, agDbUniqueId, replicaUniqueId);
                        if (autoSeeding != null)
                        {
                            string currentState = GetPSPropertyString(autoSeeding, "current_state");
                            WriteMessageAtLevel(String.Format("Current automatic seeding state: {0}", currentState), MessageLevel.Verbose, null);
                            if (String.Equals(currentState, "FAILED", StringComparison.OrdinalIgnoreCase))
                            {
                                string failStateDesc = GetPSPropertyString(autoSeeding, "failure_state_desc");
                                syncFailure = true;
                                StopFunction(
                                    String.Format("Failed while seeding database {0} to {1}. failure_message: {2}.", dbName, replicaName, failStateDesc),
                                    isContinue: true);
                                TestFunctionInterrupt();
                                continue;
                            }
                        }
                    }

                    WriteMessageAtLevel(status + (currentOperation != null ? " " + currentOperation : ""), MessageLevel.Verbose, null);
                    WriteSyncProgress(syncProgressId, replicaName, dbName, status, currentOperation, percentComplete, secondsRemaining);
                }

                if (syncFailure)
                {
                    StopFunction(String.Format("Failed while seeding database {0}.", dbName));
                    break;
                }

                if (DateTime.Now > syncTimeout)
                {
                    StopFunction(
                        String.Format("Failed to join or synchronize database {0}. Timeout of {1} seconds is reached.", dbName, _timeoutSynchronization),
                        isContinue: true);
                    TestFunctionInterrupt();
                    break;
                }

                if (stillWaiting)
                {
                    System.Threading.Thread.Sleep(_waitWhile);
                    // Refresh all replica AG databases
                    foreach (string replicaName in replicaKeys)
                    {
                        object replicaAgDb = replicaAgDbSMO[replicaName];
                        if (replicaAgDb != null)
                        {
                            RefreshObject(replicaAgDb);
                        }
                    }
                }
            }

            // Complete sync progress bars
            foreach (string replicaName in replicaKeys)
            {
                CompleteSyncProgress(syncProgressId, replicaName);
            }
        }

        #endregion 5-Step Workflow

        #region Helper Methods

        /// <summary>
        /// Builds the test splat hashtable for Test-DbaAvailabilityGroup.
        /// </summary>
        private Hashtable BuildTestSplat(string dbName, object sqlInstance, PSCredential credential)
        {
            Hashtable splat = new Hashtable();
            splat["SqlInstance"] = sqlInstance;
            if (credential != null) splat["SqlCredential"] = credential;
            if (Secondary != null) splat["Secondary"] = Secondary;
            if (SecondarySqlCredential != null) splat["SecondarySqlCredential"] = SecondarySqlCredential;
            splat["AvailabilityGroup"] = AvailabilityGroup;
            splat["AddDatabase"] = dbName;
            splat["UseLastBackup"] = UseLastBackup.IsPresent;
            splat["EnableException"] = true;
            if (TestBound("SeedingMode")) splat["SeedingMode"] = SeedingMode;
            if (TestBound("SharedPath")) splat["SharedPath"] = SharedPath;
            return splat;
        }

        /// <summary>
        /// Gets a configuration integer value from dbatools config.
        /// </summary>
        private int GetConfigInt(string name, int fallback)
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, _getConfigValue, null, new object[] { name, fallback });
                if (results != null && results.Count > 0 && results[0] != null)
                {
                    object val = results[0].BaseObject ?? results[0];
                    if (val is int intVal) return intVal;
                    int parsed;
                    if (int.TryParse(val.ToString(), out parsed)) return parsed;
                }
            }
            catch (Exception)
            {
                // Use fallback
            }
            return fallback;
        }

        /// <summary>
        /// Gets a configuration boolean value from dbatools config.
        /// </summary>
        private bool GetConfigBool(string name, bool fallback)
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, _getConfigValue, null, new object[] { name, fallback });
                if (results != null && results.Count > 0 && results[0] != null)
                {
                    object val = results[0].BaseObject ?? results[0];
                    if (val is bool boolVal) return boolVal;
                    return Convert.ToBoolean(val);
                }
            }
            catch (Exception)
            {
                // Use fallback
            }
            return fallback;
        }

        /// <summary>
        /// Gets replica properties (SeedingMode, AvailabilityMode, EndpointUrl, UniqueId) as a hashtable.
        /// </summary>
        private Hashtable GetReplicaProperties(object ag, string replicaName)
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, _getReplicaProps, null, new object[] { ag, replicaName });
                if (results != null && results.Count > 0 && results[0] != null)
                    return UnwrapHashtable(results[0].BaseObject ?? results[0]);
            }
            catch (Exception)
            {
                // Property not available
            }
            return null;
        }

        /// <summary>
        /// Gets a replica SMO object from the AG.
        /// </summary>
        private object GetReplicaObject(object ag, string replicaName)
        {
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, _getReplicaObj, null, new object[] { ag, replicaName });
            if (results != null && results.Count > 0 && results[0] != null)
                return results[0].BaseObject ?? results[0];
            return null;
        }

        /// <summary>
        /// Gets an AG database object via Get-DbaAgDatabase.
        /// </summary>
        private object GetAgDatabase(object sqlInstance, string agName, string dbName, bool enableException)
        {
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, _getAgDb, null, new object[] { sqlInstance, agName, dbName, enableException });
            if (results != null && results.Count > 0 && results[0] != null)
                return results[0];
            return null;
        }

        /// <summary>
        /// Checks if a database is already in the AG.
        /// </summary>
        private bool IsDbAlreadyInAg(object ag, string dbName)
        {
            try
            {
                Collection<PSObject> names = InvokeCommand.InvokeScript(
                    false, _getAgDbNames, null, new object[] { ag });
                if (names != null)
                {
                    foreach (PSObject nameObj in names)
                    {
                        if (nameObj != null && String.Equals(
                            nameObj.BaseObject != null ? nameObj.BaseObject.ToString() : nameObj.ToString(),
                            dbName, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Not found
            }
            return false;
        }

        /// <summary>
        /// Gets the State property from an SMO object.
        /// </summary>
        private string GetObjectState(object obj)
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, _getObjState, null, new object[] { obj });
                if (results != null && results.Count > 0 && results[0] != null)
                    return results[0].BaseObject != null ? results[0].BaseObject.ToString() : results[0].ToString();
            }
            catch (Exception)
            {
                // Unknown
            }
            return "Unknown";
        }

        /// <summary>
        /// Calls Refresh() on an SMO object.
        /// </summary>
        private void RefreshObject(object obj)
        {
            try
            {
                InvokeCommand.InvokeScript(false, _refreshObj, null, new object[] { obj });
            }
            catch (Exception)
            {
                // Best effort
            }
        }

        /// <summary>
        /// Queries physical seeding stats DMV.
        /// </summary>
        private PSObject QuerySeedingStats(object server, string dbName, string endpointUrl)
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, _querySeedingStats, null, new object[] { server, dbName, endpointUrl });
                if (results != null && results.Count > 0 && results[0] != null)
                    return results[0];
            }
            catch (Exception)
            {
                // Not available
            }
            return null;
        }

        /// <summary>
        /// Queries automatic seeding DMV.
        /// </summary>
        private PSObject QueryAutoSeeding(object server, string agId, string agDbId, string repId)
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, _queryAutoSeeding, null, new object[] { server, agId, agDbId, repId });
                if (results != null && results.Count > 0 && results[0] != null)
                    return results[0];
            }
            catch (Exception)
            {
                // Not available
            }
            return null;
        }

        /// <summary>
        /// Gets the AG unique ID as a string.
        /// </summary>
        private string GetUniqueIdFromScript(ScriptBlock script, object arg)
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, script, null, new object[] { arg });
                if (results != null && results.Count > 0 && results[0] != null)
                    return results[0].BaseObject != null ? results[0].BaseObject.ToString() : results[0].ToString();
            }
            catch (Exception)
            {
                // Not available
            }
            return null;
        }

        /// <summary>
        /// Gets the AG database unique ID.
        /// </summary>
        private string GetAgDbUniqueId(object ag, string dbName)
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, _getAgDbUniqueId, null, new object[] { ag, dbName });
                if (results != null && results.Count > 0 && results[0] != null)
                    return results[0].BaseObject != null ? results[0].BaseObject.ToString() : results[0].ToString();
            }
            catch (Exception)
            {
                // Not available
            }
            return null;
        }

        /// <summary>
        /// Gets a string result from running a script with one argument.
        /// </summary>
        private string GetStringFromScript(ScriptBlock script, object arg)
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, script, null, new object[] { arg });
                if (results != null && results.Count > 0 && results[0] != null)
                    return results[0].BaseObject != null ? results[0].BaseObject.ToString() : results[0].ToString();
            }
            catch (Exception)
            {
                // Default
            }
            return null;
        }

        /// <summary>
        /// Gets a bool result from running a script with two arguments.
        /// </summary>
        private bool GetBoolFromScript(ScriptBlock script, object arg1, object arg2)
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, script, null, new object[] { arg1, arg2 });
                if (results != null && results.Count > 0 && results[0] != null)
                {
                    object val = results[0].BaseObject ?? results[0];
                    if (val is bool boolVal) return boolVal;
                    return Convert.ToBoolean(val);
                }
            }
            catch (Exception)
            {
                // Default
            }
            return false;
        }

        /// <summary>
        /// Invokes a script and returns the first result as a single object.
        /// </summary>
        private object InvokeScriptSingle(ScriptBlock script, params object[] args)
        {
            Collection<PSObject> results = InvokeCommand.InvokeScript(false, script, null, args);
            if (results != null && results.Count > 0 && results[0] != null)
                return results[0].BaseObject ?? results[0];
            return null;
        }

        #endregion Helper Methods

        #region PSObject Property Helpers

        /// <summary>
        /// Gets a string property from a PSObject or wrapped object.
        /// </summary>
        internal static string GetPSPropertyString(object obj, string propertyName)
        {
            if (obj == null) return null;
            try
            {
                PSObject psObj = obj as PSObject;
                if (psObj == null) psObj = PSObject.AsPSObject(obj);
                PSPropertyInfo prop = psObj.Properties[propertyName];
                if (prop != null && prop.Value != null)
                    return prop.Value.ToString();
            }
            catch (Exception)
            {
                // Property not found
            }
            return null;
        }

        /// <summary>
        /// Gets a raw property value from a PSObject or wrapped object.
        /// </summary>
        internal static object GetPSPropertyValue(object obj, string propertyName)
        {
            if (obj == null) return null;
            try
            {
                PSObject psObj = obj as PSObject;
                if (psObj == null) psObj = PSObject.AsPSObject(obj);
                PSPropertyInfo prop = psObj.Properties[propertyName];
                if (prop != null) return prop.Value;
            }
            catch (Exception)
            {
                // Property not found
            }
            return null;
        }

        /// <summary>
        /// Gets a bool property from a PSObject.
        /// </summary>
        internal static bool GetBoolProperty(object obj, string propertyName)
        {
            object val = GetPSPropertyValue(obj, propertyName);
            if (val is bool boolVal) return boolVal;
            if (val != null)
            {
                bool parsed;
                if (bool.TryParse(val.ToString(), out parsed)) return parsed;
            }
            return false;
        }

        /// <summary>
        /// Gets a long property value from a PSObject.
        /// </summary>
        internal static long GetPSPropertyLong(object obj, string propertyName)
        {
            object val = GetPSPropertyValue(obj, propertyName);
            if (val is long longVal) return longVal;
            if (val is int intVal) return intVal;
            if (val != null)
            {
                long parsed;
                if (long.TryParse(val.ToString(), out parsed)) return parsed;
            }
            return 0;
        }

        #endregion PSObject Property Helpers

        #region Collection Helpers

        /// <summary>
        /// Adds non-null PSObjects from a collection to a list.
        /// </summary>
        private static void AddNonNullResults(List<PSObject> target, Collection<PSObject> source)
        {
            if (source == null) return;
            foreach (PSObject item in source)
            {
                if (item != null) target.Add(item);
            }
        }

        /// <summary>
        /// Unwraps a Hashtable from a potentially PSObject-wrapped value.
        /// </summary>
        internal static Hashtable UnwrapHashtable(object obj)
        {
            if (obj == null) return null;
            if (obj is Hashtable ht) return ht;
            PSObject psObj = obj as PSObject;
            if (psObj != null && psObj.BaseObject is Hashtable baseHt) return baseHt;
            return null;
        }

        /// <summary>
        /// Unwraps an object array from a potentially PSObject-wrapped value.
        /// </summary>
        internal static object[] UnwrapObjectArray(object obj)
        {
            if (obj == null) return null;
            if (obj is object[] arr) return arr.Length > 0 ? arr : null;
            PSObject psObj = obj as PSObject;
            if (psObj != null && psObj.BaseObject is object[] baseArr)
                return baseArr.Length > 0 ? baseArr : null;
            return null;
        }

        /// <summary>
        /// Gets the keys from a Hashtable as a list of strings.
        /// </summary>
        internal static List<string> GetHashtableKeys(Hashtable ht)
        {
            List<string> keys = new List<string>();
            if (ht == null) return keys;
            foreach (object key in ht.Keys)
            {
                if (key != null) keys.Add(key.ToString());
            }
            return keys;
        }

        /// <summary>
        /// Gets a string value from a Hashtable.
        /// </summary>
        internal static string GetHashtableString(Hashtable ht, string key)
        {
            if (ht == null || !ht.ContainsKey(key)) return null;
            object val = ht[key];
            return val != null ? val.ToString() : null;
        }

        /// <summary>
        /// Checks if a value is a non-empty, meaningful string (not null, not empty, not DBNull).
        /// Used to check DMV result columns that may contain DBNull or empty error messages.
        /// </summary>
        internal static bool IsNonNullNonDbNull(object value)
        {
            if (value == null || value is DBNull) return false;
            string str = value.ToString();
            return !String.IsNullOrEmpty(str);
        }

        #endregion Collection Helpers

        #region Progress Helpers

        /// <summary>
        /// Writes the main progress bar.
        /// </summary>
        private void WriteMainProgress(string status)
        {
            try
            {
                WriteProgress(new ProgressRecord(_progressId,
                    String.Format("Adding database(s) to Availability Group {0}", AvailabilityGroup),
                    status));
            }
            catch (Exception)
            {
                // Best effort
            }
        }

        /// <summary>
        /// Updates the main progress activity.
        /// </summary>
        private void WriteMainProgressActivity(string activity)
        {
            try
            {
                WriteProgress(new ProgressRecord(_progressId, activity, "Processing"));
            }
            catch (Exception)
            {
                // Best effort
            }
        }

        /// <summary>
        /// Completes the main progress bar.
        /// </summary>
        private void CompleteMainProgress()
        {
            try
            {
                WriteProgress(new ProgressRecord(_progressId,
                    String.Format("Adding database(s) to Availability Group {0}", AvailabilityGroup),
                    "Complete")
                { RecordType = ProgressRecordType.Completed });
            }
            catch (Exception)
            {
                // Best effort
            }
        }

        /// <summary>
        /// Writes sync progress for a specific replica.
        /// </summary>
        private void WriteSyncProgress(Hashtable syncProgressId, string replicaName, string dbName,
            string status, string currentOperation, int percentComplete, int secondsRemaining)
        {
            try
            {
                int childId = (int)syncProgressId[replicaName];
                ProgressRecord pr = new ProgressRecord(childId,
                    String.Format("Adding database {0} to Availability Group {1} on replica {2}", dbName, AvailabilityGroup, replicaName),
                    status)
                {
                    ParentActivityId = _progressId
                };
                if (percentComplete >= 0) pr.PercentComplete = percentComplete;
                if (secondsRemaining >= 0) pr.SecondsRemaining = secondsRemaining;
                if (currentOperation != null) pr.CurrentOperation = currentOperation;
                WriteProgress(pr);
            }
            catch (Exception)
            {
                // Best effort
            }
        }

        /// <summary>
        /// Completes sync progress for a specific replica.
        /// </summary>
        private void CompleteSyncProgress(Hashtable syncProgressId, string replicaName)
        {
            try
            {
                int childId = (int)syncProgressId[replicaName];
                WriteProgress(new ProgressRecord(childId, "Completed", "Completed")
                {
                    ParentActivityId = _progressId,
                    RecordType = ProgressRecordType.Completed
                });
            }
            catch (Exception)
            {
                // Best effort
            }
        }

        #endregion Progress Helpers
    }
}
