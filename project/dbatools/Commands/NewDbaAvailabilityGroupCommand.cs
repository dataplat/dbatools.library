using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Net;
using System.Text.RegularExpressions;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Creates SQL Server availability groups with automated replica setup, database seeding, and listener configuration.
    /// Handles the entire workflow from initial validation through final configuration including endpoint setup,
    /// replica joining, database seeding, and listener creation.
    /// </summary>
    [Cmdlet("New", "DbaAvailabilityGroup", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.High)]
    public class NewDbaAvailabilityGroupCommand : DbaBaseCmdlet
    {
        #region Parameters

        /// <summary>
        /// The SQL Server instance that will host the primary replica of the availability group.
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        public DbaInstanceParameter Primary { get; set; }

        /// <summary>
        /// Login to the primary instance using alternative credentials.
        /// </summary>
        [Parameter()]
        public PSCredential PrimarySqlCredential { get; set; }

        /// <summary>
        /// One or more SQL Server instances that will host secondary replicas.
        /// </summary>
        [Parameter()]
        public DbaInstanceParameter[] Secondary { get; set; }

        /// <summary>
        /// Login to secondary instances using alternative credentials.
        /// </summary>
        [Parameter()]
        public PSCredential SecondarySqlCredential { get; set; }

        /// <summary>
        /// The name for the new availability group.
        /// </summary>
        [Parameter(Mandatory = true)]
        public string Name { get; set; }

        /// <summary>
        /// Creates a contained availability group (SQL Server 2022+).
        /// </summary>
        [Parameter()]
        public SwitchParameter IsContained { get; set; }

        /// <summary>
        /// Reuses existing system databases when recreating a contained availability group.
        /// </summary>
        [Parameter()]
        public SwitchParameter ReuseSystemDatabases { get; set; }

        /// <summary>
        /// Enables DTC support for distributed transactions.
        /// </summary>
        [Parameter()]
        public SwitchParameter DtcSupport { get; set; }

        /// <summary>
        /// The clustering technology used by the availability group.
        /// </summary>
        [Parameter()]
        [ValidateSet("Wsfc", "External", "None")]
        public string ClusterType { get; set; }

        /// <summary>
        /// Controls which replicas are preferred for automated backups.
        /// </summary>
        [Parameter()]
        [ValidateSet("None", "Primary", "Secondary", "SecondaryOnly")]
        public string AutomatedBackupPreference { get; set; }

        /// <summary>
        /// Determines what conditions trigger automatic failover.
        /// </summary>
        [Parameter()]
        [ValidateSet("OnAnyQualifiedFailureCondition", "OnCriticalServerErrors", "OnModerateServerErrors", "OnServerDown", "OnServerUnresponsive")]
        public string FailureConditionLevel { get; set; }

        /// <summary>
        /// Timeout in milliseconds for health check responses.
        /// </summary>
        [Parameter()]
        public int HealthCheckTimeout { get; set; }

        /// <summary>
        /// Creates a Basic Availability Group (SQL Server 2016 Standard+).
        /// </summary>
        [Parameter()]
        public SwitchParameter Basic { get; set; }

        /// <summary>
        /// Enables database-level health monitoring that can trigger failover.
        /// </summary>
        [Parameter()]
        public SwitchParameter DatabaseHealthTrigger { get; set; }

        /// <summary>
        /// Returns the AG object without creating it for further customization.
        /// </summary>
        [Parameter()]
        public SwitchParameter Passthru { get; set; }

        /// <summary>
        /// Databases to add to the availability group during creation.
        /// </summary>
        [Parameter()]
        public string[] Database { get; set; }

        /// <summary>
        /// Network path for database backups during secondary initialization.
        /// </summary>
        [Parameter()]
        public string SharedPath { get; set; }

        /// <summary>
        /// Uses existing backup files instead of creating new ones.
        /// </summary>
        [Parameter()]
        public SwitchParameter UseLastBackup { get; set; }

        /// <summary>
        /// Removes existing databases on secondaries before restoring.
        /// </summary>
        [Parameter()]
        public SwitchParameter Force { get; set; }

        /// <summary>
        /// Controls whether transaction commits wait for secondary acknowledgment.
        /// </summary>
        [Parameter()]
        [ValidateSet("AsynchronousCommit", "SynchronousCommit")]
        public string AvailabilityMode { get; set; }

        /// <summary>
        /// Determines how failover occurs for the availability group.
        /// </summary>
        [Parameter()]
        [ValidateSet("Automatic", "Manual", "External")]
        public string FailoverMode { get; set; }

        /// <summary>
        /// Backup priority for this replica (0-100).
        /// </summary>
        [Parameter()]
        public int BackupPriority { get; set; }

        /// <summary>
        /// Name for the database mirroring endpoint.
        /// </summary>
        [Parameter()]
        public string Endpoint { get; set; }

        /// <summary>
        /// Custom TCP URLs for availability group endpoints.
        /// </summary>
        [Parameter()]
        public string[] EndpointUrl { get; set; }

        /// <summary>
        /// Controls what connections are allowed to the primary replica.
        /// </summary>
        [Parameter()]
        [ValidateSet("AllowAllConnections", "AllowReadWriteConnections")]
        public string ConnectionModeInPrimaryRole { get; set; }

        /// <summary>
        /// Controls what connections are allowed to secondary replicas.
        /// </summary>
        [Parameter()]
        [ValidateSet("AllowNoConnections", "AllowReadIntentConnectionsOnly", "AllowAllConnections", "No", "Read-intent only", "Yes")]
        public string ConnectionModeInSecondaryRole { get; set; }

        /// <summary>
        /// Determines how databases are initialized on secondary replicas.
        /// </summary>
        [Parameter()]
        [ValidateSet("Automatic", "Manual")]
        public string SeedingMode { get; set; }

        /// <summary>
        /// Certificate name for endpoint authentication.
        /// </summary>
        [Parameter()]
        public string Certificate { get; set; }

        /// <summary>
        /// Starts the AlwaysOn_health Extended Events session on all replicas.
        /// </summary>
        [Parameter()]
        public SwitchParameter ConfigureXESession { get; set; }

        /// <summary>
        /// Static IP addresses for the AG listener.
        /// </summary>
        [Parameter()]
        public IPAddress[] IPAddress { get; set; }

        /// <summary>
        /// Subnet mask for static IP listener configuration.
        /// </summary>
        [Parameter()]
        public IPAddress SubnetMask { get; set; }

        /// <summary>
        /// TCP port for the AG listener.
        /// </summary>
        [Parameter()]
        public int Port { get; set; }

        /// <summary>
        /// Configures the listener to use DHCP.
        /// </summary>
        [Parameter()]
        public SwitchParameter Dhcp { get; set; }

        /// <summary>
        /// Connection options for TDS 8.0 support in SQL Server 2025+.
        /// </summary>
        [Parameter()]
        public string ClusterConnectionOption { get; set; }

        #endregion Parameters

        #region Script Blocks

        private static readonly ScriptBlock _connectScript =
            ScriptBlock.Create("param($i, $c) Connect-DbaInstance -SqlInstance $i -SqlCredential $c");

        private static readonly ScriptBlock _connectNoCred =
            ScriptBlock.Create("param($i) Connect-DbaInstance -SqlInstance $i");

        private static readonly ScriptBlock _getConfigValue =
            ScriptBlock.Create("param($name, $fallback) Get-DbatoolsConfigValue -FullName $name -Fallback $fallback");

        private static readonly ScriptBlock _getDefaultPath =
            ScriptBlock.Create("param($s) Get-DbaDefaultPath -SqlInstance $s");

        private static readonly ScriptBlock _getAgScript =
            ScriptBlock.Create("param($s, $c, $ag) Get-DbaAvailabilityGroup -SqlInstance $s -SqlCredential $c -AvailabilityGroup $ag");

        private static readonly ScriptBlock _getCertScript =
            ScriptBlock.Create("param($s, $c, $cert) Get-DbaDbCertificate -SqlInstance $s -SqlCredential $c -Certificate $cert");

        private static readonly ScriptBlock _testPathScript =
            ScriptBlock.Create("param($s, $c, $p) Test-DbaPath -SqlInstance $s -SqlCredential $c -Path $p");

        private static readonly ScriptBlock _getDatabaseScript =
            ScriptBlock.Create("param($s, $c, $db) Get-DbaDatabase -SqlInstance $s -SqlCredential $c -Database $db");

        private static readonly ScriptBlock _setRecoveryModelScript =
            ScriptBlock.Create("param($s, $c, $db) Set-DbaDbRecoveryModel -SqlInstance $s -SqlCredential $c -Database $db -RecoveryModel Full");

        private static readonly ScriptBlock _newAgObject =
            ScriptBlock.Create("param($s, $name) New-Object Microsoft.SqlServer.Management.Smo.AvailabilityGroup -ArgumentList $s, $name");

        private static readonly ScriptBlock _setAgProperties =
            ScriptBlock.Create(@"param($ag, $abp, $fcl, $hct)
$ag.AutomatedBackupPreference = [Microsoft.SqlServer.Management.Smo.AvailabilityGroupAutomatedBackupPreference]::$abp
$ag.FailureConditionLevel = [Microsoft.SqlServer.Management.Smo.AvailabilityGroupFailureConditionLevel]::$fcl
$ag.HealthCheckTimeout = $hct");

        private static readonly ScriptBlock _setAgV13Props =
            ScriptBlock.Create(@"param($ag, $basic, $dht, $dtc)
$ag.BasicAvailabilityGroup = $basic
$ag.DatabaseHealthTrigger = $dht
$ag.DtcSupportEnabled = $dtc");

        private static readonly ScriptBlock _setAgClusterType =
            ScriptBlock.Create("param($ag, $ct) $ag.ClusterType = $ct");

        private static readonly ScriptBlock _setAgContained =
            ScriptBlock.Create("param($ag, $ic, $rsd) $ag.IsContained = $ic; $ag.ReuseSystemDatabases = $rsd");

        private static readonly ScriptBlock _setAgClusterConnectionOptions =
            ScriptBlock.Create("param($ag, $cco) $ag.ClusterConnectionOptions = $cco");

        private static readonly ScriptBlock _selectDefaultViewPassthru =
            ScriptBlock.Create(@"param($ag)
$defaults = 'LocalReplicaRole', 'Name as AvailabilityGroup', 'PrimaryReplicaServerName as PrimaryReplica', 'AutomatedBackupPreference', 'AvailabilityReplicas', 'AvailabilityDatabases', 'AvailabilityGroupListeners'
Select-DefaultView -InputObject $ag -Property $defaults");

        private static readonly ScriptBlock _addReplicaScript =
            ScriptBlock.Create(@"param($ag, $ct, $am, $fm, $bp, $cmpr, $cmsr, $ep, $cert, $cxe, $epUrl, $sm, $server)
$replicaparams = @{
    InputObject                   = $ag
    ClusterType                   = $ct
    AvailabilityMode              = $am
    FailoverMode                  = $fm
    BackupPriority                = $bp
    ConnectionModeInPrimaryRole   = $cmpr
    ConnectionModeInSecondaryRole = $cmsr
    EnableException               = $true
    SqlInstance                   = $server
}
if ($ep) { $replicaparams['Endpoint'] = $ep }
if ($cert) { $replicaparams['Certificate'] = $cert }
if ($cxe) { $replicaparams['ConfigureXESession'] = $cxe }
if ($epUrl) { $replicaparams['EndpointUrl'] = $epUrl }
if ($sm) { $replicaparams['SeedingMode'] = $sm }
Add-DbaAgReplica @replicaparams");

        private static readonly ScriptBlock _invokeCreateScript =
            ScriptBlock.Create("param($ag) Invoke-Create -Object $ag");

        private static readonly ScriptBlock _addListenerIPScript =
            ScriptBlock.Create("param($ag, $ip, $sm, $port) Add-DbaAgListener -InputObject $ag -IPAddress $ip -SubnetMask $sm -Port $port");

        private static readonly ScriptBlock _addListenerDhcpScript =
            ScriptBlock.Create("param($ag, $port) Add-DbaAgListener -InputObject $ag -Port $port -Dhcp");

        private static readonly ScriptBlock _joinAgScript =
            ScriptBlock.Create("param($s, $ag) Join-DbaAvailabilityGroup -SqlInstance $s -InputObject $ag -EnableException");

        private static readonly ScriptBlock _refreshAgScript =
            ScriptBlock.Create("param($s) $s.AvailabilityGroups.Refresh()");

        private static readonly ScriptBlock _getReplicaStates =
            ScriptBlock.Create("param($secs) Get-DbaAgReplica -SqlInstance $secs | Where-Object Role -notin 'Primary', 'Unknown'");

        private static readonly ScriptBlock _grantAgPermission =
            ScriptBlock.Create("param($s, $agName) Grant-DbaAgPermission -SqlInstance $s -Type AvailabilityGroup -AvailabilityGroup $agName -Permission CreateAnyDatabase -EnableException");

        private static readonly ScriptBlock _removeDatabaseScript =
            ScriptBlock.Create("param($secs, $db) Get-DbaDatabase -SqlInstance $secs -Database $db -EnableException | Remove-DbaDatabase -EnableException");

        private static readonly ScriptBlock _addAgDatabaseScript =
            ScriptBlock.Create(@"param($s, $agName, $db, $secs, $ulb, $sm, $sp)
$addDatabaseParams = @{
    SqlInstance       = $s
    AvailabilityGroup = $agName
    Database          = $db
    Secondary         = $secs
    UseLastBackup     = $ulb
    EnableException   = $true
}
if ($sm) { $addDatabaseParams['SeedingMode'] = $sm }
if ($sp) { $addDatabaseParams['SharedPath'] = $sp }
Add-DbaAgDatabase @addDatabaseParams");

        private static readonly ScriptBlock _getAgResultScript =
            ScriptBlock.Create("param($s, $c, $agName) Get-DbaAvailabilityGroup -SqlInstance $s -SqlCredential $c -AvailabilityGroup $agName");

        private static readonly ScriptBlock _getVersionMajor =
            ScriptBlock.Create("param($s) $s.VersionMajor");

        private static readonly ScriptBlock _getIsHadrEnabled =
            ScriptBlock.Create("param($s) $s.IsHadrEnabled");

        private static readonly ScriptBlock _getHostPlatform =
            ScriptBlock.Create("param($s) $s.HostPlatform");

        private static readonly ScriptBlock _getEngineEdition =
            ScriptBlock.Create("param($s) $s.EngineEdition");

        private static readonly ScriptBlock _getServerName =
            ScriptBlock.Create("param($s) $s.Name");

        private static readonly ScriptBlock _getMirroringStatus =
            ScriptBlock.Create("param($db) $db.MirroringStatus");

        private static readonly ScriptBlock _getDbStatus =
            ScriptBlock.Create("param($db) $db.Status");

        private static readonly ScriptBlock _getDbRecoveryModel =
            ScriptBlock.Create("param($db) $db.RecoveryModel");

        private static readonly ScriptBlock _getDbName =
            ScriptBlock.Create("param($db) $db.Name");

        #endregion Script Blocks

        #region Private State

        private bool _defaultsResolved;
        private string _resolvedClusterType;
        private string _resolvedAutomatedBackupPreference;
        private string _resolvedFailureConditionLevel;
        private int _resolvedHealthCheckTimeout;
        private string _resolvedAvailabilityMode;
        private string _resolvedFailoverMode;
        private int _resolvedBackupPriority;
        private string _resolvedConnectionModeInPrimaryRole;
        private string _resolvedConnectionModeInSecondaryRole;
        private string _resolvedSeedingMode;
        private int _resolvedPort;

        #endregion Private State

        /// <summary>
        /// Resolves defaults from config and parameters in BeginProcessing.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            if (Force.IsPresent)
            {
                // Equivalent of $ConfirmPreference = 'none' in PS1 begin block
                try
                {
                    SessionState.PSVariable.Set("ConfirmPreference", "None");
                }
                catch (Exception)
                {
                    // Best effort
                }
            }

            ResolveDefaults();
        }

        /// <summary>
        /// Processes each pipeline input to create the availability group.
        /// </summary>
        protected override void ProcessRecord()
        {
            int stepCounter = 0;

            // Validate Force requires SharedPath or UseLastBackup
            if (Force.IsPresent && Secondary != null && Secondary.Length > 0
                && !TestBound("SharedPath") && !UseLastBackup.IsPresent
                && !String.Equals(_resolvedSeedingMode, "Automatic", StringComparison.OrdinalIgnoreCase))
            {
                StopFunction("SharedPath or UseLastBackup is required when Force is used");
                return;
            }

            // Validate EndpointUrl
            if (EndpointUrl != null && EndpointUrl.Length > 0)
            {
                int expectedCount = 1 + (Secondary != null ? Secondary.Length : 0);
                if (EndpointUrl.Length != expectedCount)
                {
                    StopFunction("The number of elements in EndpointUrl is not correct");
                    return;
                }
                foreach (string epUrl in EndpointUrl)
                {
                    if (!IsValidEndpointUrl(epUrl))
                    {
                        StopFunction(String.Format("EndpointUrl '{0}' not in correct format 'TCP://system-address:port'", epUrl));
                        return;
                    }
                }
            }

            // Normalize ConnectionModeInSecondaryRole
            string connModeSecondary = _resolvedConnectionModeInSecondaryRole;
            connModeSecondary = NormalizeConnectionModeInSecondaryRole(connModeSecondary);

            // Validate IPAddress and Dhcp are mutually exclusive
            if (IPAddress != null && IPAddress.Length > 0 && Dhcp.IsPresent)
            {
                StopFunction("You cannot specify both an IP address and the Dhcp switch for the listener.");
                return;
            }

            // Connect to primary
            object server;
            try
            {
                server = ConnectInstance(Primary, PrimarySqlCredential);
                if (server == null)
                {
                    StopFunction("Failure", category: ErrorCategory.ConnectionError, target: Primary);
                    return;
                }
            }
            catch (Exception ex)
            {
                StopFunction("Failure",
                    errorRecord: new ErrorRecord(ex, "NewDbaAvailabilityGroup_ConnectionError", ErrorCategory.ConnectionError, Primary),
                    target: Primary, category: ErrorCategory.ConnectionError);
                return;
            }

            int versionMajor = GetVersionMajor(server);

            // Version checks
            if (String.Equals(_resolvedSeedingMode, "Automatic", StringComparison.OrdinalIgnoreCase) && versionMajor < 13)
            {
                StopFunction("Automatic seeding mode only supported in SQL Server 2016 and above", target: Primary);
                return;
            }

            if (Basic.IsPresent && versionMajor < 13)
            {
                StopFunction("Basic availability groups are only supported in SQL Server 2016 and above", target: Primary);
                return;
            }

            if (IsContained.IsPresent && versionMajor < 16)
            {
                StopFunction("Contained availability groups are only supported in SQL Server 2022 and above", target: Primary);
                return;
            }

            if (ReuseSystemDatabases.IsPresent && !IsContained.IsPresent)
            {
                StopFunction("Reuse system databases is only applicable in contained availability groups", target: Primary);
                return;
            }

            // Check requirements
            WriteProgress(stepCounter++, "Checking requirements");
            bool requirementsFailed = false;

            bool isHadrEnabled = GetBoolProperty(server, _getIsHadrEnabled);
            if (!isHadrEnabled)
            {
                requirementsFailed = true;
                WriteMessageWarning(String.Format("Availability Group (HADR) is not configured for the instance: {0}. Use Enable-DbaAgHadr to configure the instance.", Primary));
            }

            List<object> secondaries = new List<object>();
            if (Secondary != null && Secondary.Length > 0)
            {
                object primaryPath = null;
                if (String.Equals(_resolvedSeedingMode, "Automatic", StringComparison.OrdinalIgnoreCase))
                {
                    primaryPath = GetDefaultPath(server);
                }

                foreach (DbaInstanceParameter instance in Secondary)
                {
                    object second;
                    try
                    {
                        second = ConnectInstance(instance, SecondarySqlCredential);
                        if (second == null)
                        {
                            CompleteProgress();
                            StopFunction("Failure", target: instance, isContinue: true, category: ErrorCategory.ConnectionError);
                            TestFunctionInterrupt();
                            continue;
                        }
                        secondaries.Add(second);
                    }
                    catch (Exception ex)
                    {
                        CompleteProgress();
                        StopFunction("Failure",
                            errorRecord: new ErrorRecord(ex, "NewDbaAvailabilityGroup_SecondaryConnectionError", ErrorCategory.ConnectionError, instance),
                            target: instance, isContinue: true, category: ErrorCategory.ConnectionError);
                        TestFunctionInterrupt();
                        continue;
                    }

                    bool secondHadr = GetBoolProperty(second, _getIsHadrEnabled);
                    if (!secondHadr)
                    {
                        requirementsFailed = true;
                        WriteMessageWarning(String.Format("Availability Group (HADR) is not configured for the instance: {0}. Use Enable-DbaAgHadr to configure the instance.", instance));
                    }

                    if (String.Equals(_resolvedSeedingMode, "Automatic", StringComparison.OrdinalIgnoreCase) && primaryPath != null)
                    {
                        object secondaryPath = GetDefaultPath(second);
                        if (secondaryPath != null && primaryPath != null)
                        {
                            string primaryData = GetPSObjectPropertyString(primaryPath, "Data");
                            string secondaryData = GetPSObjectPropertyString(secondaryPath, "Data");
                            string primaryLog = GetPSObjectPropertyString(primaryPath, "Log");
                            string secondaryLog = GetPSObjectPropertyString(secondaryPath, "Log");

                            if (!String.Equals(primaryData, secondaryData, StringComparison.OrdinalIgnoreCase))
                            {
                                WriteMessageWarning(String.Format("Primary and secondary ({0}) default data paths do not match. Trying anyway.", instance));
                            }
                            if (!String.Equals(primaryLog, secondaryLog, StringComparison.OrdinalIgnoreCase))
                            {
                                WriteMessageWarning(String.Format("Primary and secondary ({0}) default log paths do not match. Trying anyway.", instance));
                            }
                        }
                    }
                }
            }

            if (requirementsFailed)
            {
                CompleteProgress();
                StopFunction("Prerequisites are not completely met, so stopping here. See warning messages for details.");
                return;
            }

            // Check if AG already exists
            if (AgExists(Primary, PrimarySqlCredential, Name))
            {
                CompleteProgress();
                StopFunction(String.Format("Availability group named {0} already exists on {1}", Name, Primary));
                return;
            }

            // Check certificate
            if (!String.IsNullOrEmpty(Certificate))
            {
                if (!CertificateExists(Primary, PrimarySqlCredential, Certificate))
                {
                    CompleteProgress();
                    StopFunction(String.Format("Certificate {0} does not exist on {1}", Certificate, Primary), target: Primary);
                    return;
                }
            }

            // Check shared path
            if (!String.IsNullOrEmpty(SharedPath))
            {
                if (!TestPath(Primary, PrimarySqlCredential, SharedPath))
                {
                    CompleteProgress();
                    StopFunction(String.Format("Cannot access {0} from {1}", SharedPath, Primary));
                    return;
                }
            }

            // Check database requires SharedPath for manual seeding
            if (Database != null && Database.Length > 0 && !UseLastBackup.IsPresent
                && String.IsNullOrEmpty(SharedPath) && Secondary != null && Secondary.Length > 0
                && !String.Equals(_resolvedSeedingMode, "Automatic", StringComparison.OrdinalIgnoreCase))
            {
                CompleteProgress();
                StopFunction("You must specify a SharedPath when adding databases to a manually seeded availability group");
                return;
            }

            // Linux checks
            string hostPlatform = GetStringProperty(server, _getHostPlatform);
            if (String.Equals(hostPlatform, "Linux", StringComparison.OrdinalIgnoreCase))
            {
                if (!String.Equals(_resolvedClusterType, "External", StringComparison.OrdinalIgnoreCase)
                    && !String.Equals(_resolvedClusterType, "None", StringComparison.OrdinalIgnoreCase))
                {
                    CompleteProgress();
                    StopFunction("Linux only supports ClusterType of External or None");
                    return;
                }
                if (DtcSupport.IsPresent)
                {
                    CompleteProgress();
                    StopFunction("Microsoft Distributed Transaction Coordinator (DTC) is not supported under Linux");
                    return;
                }
            }

            // ClusterType None version check
            if (String.Equals(_resolvedClusterType, "None", StringComparison.OrdinalIgnoreCase) && versionMajor < 14)
            {
                CompleteProgress();
                StopFunction("ClusterType of None only supported in SQL Server 2017 and above");
                return;
            }

            // ConnectionModeInSecondaryRole Standard Edition warning
            if (!String.IsNullOrEmpty(connModeSecondary)
                && !String.Equals(connModeSecondary, "AllowNoConnections", StringComparison.OrdinalIgnoreCase))
            {
                CheckStandardEditionWarning(server, secondaries);
            }

            // Database checks
            List<PSObject> dbs = new List<PSObject>();
            if (Database != null && Database.Length > 0)
            {
                Collection<PSObject> dbResults = GetDatabases(Primary, PrimarySqlCredential, Database);
                if (dbResults != null)
                {
                    foreach (PSObject d in dbResults)
                    {
                        if (d != null) dbs.Add(d);
                    }
                }
            }

            foreach (PSObject primarydb in dbs)
            {
                string mirroringStatus = GetStringFromScript(_getMirroringStatus, primarydb);
                if (!String.Equals(mirroringStatus, "None", StringComparison.OrdinalIgnoreCase))
                {
                    CompleteProgress();
                    string dbName = GetStringFromScript(_getDbName, primarydb);
                    StopFunction(String.Format("Cannot setup mirroring on database ({0}) due to its current mirroring state: {1}", dbName, mirroringStatus));
                    return;
                }

                string dbStatus = GetStringFromScript(_getDbStatus, primarydb);
                if (!String.Equals(dbStatus, "Normal", StringComparison.OrdinalIgnoreCase))
                {
                    CompleteProgress();
                    string dbName = GetStringFromScript(_getDbName, primarydb);
                    StopFunction(String.Format("Cannot setup mirroring on database ({0}) due to its current state: {1}", dbName, dbStatus));
                    return;
                }

                string recoveryModel = GetStringFromScript(_getDbRecoveryModel, primarydb);
                if (!String.Equals(recoveryModel, "Full", StringComparison.OrdinalIgnoreCase))
                {
                    if (TestBound("UseLastBackup"))
                    {
                        CompleteProgress();
                        string dbName = GetStringFromScript(_getDbName, primarydb);
                        StopFunction(String.Format("{0} not set to full recovery. UseLastBackup cannot be used.", dbName));
                        return;
                    }
                    else
                    {
                        string dbName = GetStringFromScript(_getDbName, primarydb);
                        SetRecoveryModelFull(Primary, PrimarySqlCredential, dbName);
                    }
                }
            }

            // Start creating the AG
            WriteProgress(stepCounter++, String.Format("Creating availability group named {0} on {1}", Name, Primary));

            object ag = null;
            if (ShouldProcess(Primary != null ? Primary.ToString() : "Primary", String.Format("Setting up availability group named {0} and adding primary replica", Name)))
            {
                try
                {
                    // Create AG object
                    ag = CreateAgObject(server, Name);
                    if (ag == null)
                    {
                        CompleteProgress();
                        StopFunction("Failed to create availability group object", target: Primary);
                        return;
                    }

                    // Set AG properties
                    SetAgProperties(ag, _resolvedAutomatedBackupPreference, _resolvedFailureConditionLevel, _resolvedHealthCheckTimeout);

                    // Version-specific properties
                    if (versionMajor >= 13)
                    {
                        SetAgV13Properties(ag, Basic.IsPresent, DatabaseHealthTrigger.IsPresent, DtcSupport.IsPresent);
                    }

                    if (versionMajor >= 14)
                    {
                        SetAgClusterType(ag, _resolvedClusterType);
                    }

                    if (versionMajor >= 16)
                    {
                        SetAgContained(ag, IsContained.IsPresent, ReuseSystemDatabases.IsPresent);
                    }

                    if (versionMajor >= 17 && !String.IsNullOrEmpty(ClusterConnectionOption))
                    {
                        SetAgClusterConnectionOptions(ag, ClusterConnectionOption);
                    }

                    // Passthru - return AG object before creating
                    if (Passthru.IsPresent)
                    {
                        CompleteProgress();
                        OutputPassthru(ag);
                        return;
                    }

                    // Get primary endpoint URL (first in array)
                    string primaryEpUrl = (EndpointUrl != null && EndpointUrl.Length > 0) ? EndpointUrl[0] : null;

                    // Add primary replica
                    string seedingModeForReplica = versionMajor >= 13 ? _resolvedSeedingMode : null;
                    AddReplica(ag, _resolvedClusterType, _resolvedAvailabilityMode, _resolvedFailoverMode,
                        _resolvedBackupPriority, _resolvedConnectionModeInPrimaryRole, connModeSecondary,
                        Endpoint, Certificate, ConfigureXESession.IsPresent, primaryEpUrl, seedingModeForReplica, server);
                }
                catch (Exception ex)
                {
                    string msg = GetInnerExceptionMessage(ex);
                    if (String.IsNullOrEmpty(msg))
                    {
                        msg = ex.Message;
                    }
                    CompleteProgress();
                    StopFunction(msg,
                        errorRecord: new ErrorRecord(ex, "NewDbaAvailabilityGroup_CreateError", ErrorCategory.InvalidOperation, Primary),
                        target: Primary);
                    return;
                }
            }

            if (ag == null) return;

            // Add secondary replicas
            WriteProgress(stepCounter++, "Adding secondary replicas");

            string[] remainingUrls = null;
            if (EndpointUrl != null && EndpointUrl.Length > 1)
            {
                remainingUrls = new string[EndpointUrl.Length - 1];
                Array.Copy(EndpointUrl, 1, remainingUrls, 0, EndpointUrl.Length - 1);
            }

            int epIndex = 0;
            foreach (object second in secondaries)
            {
                string secondName = GetStringProperty(second, _getServerName);
                if (ShouldProcess(secondName ?? "secondary", String.Format("Adding replica to availability group named {0}", Name)))
                {
                    try
                    {
                        string epUrl = null;
                        if (remainingUrls != null && epIndex < remainingUrls.Length)
                        {
                            epUrl = remainingUrls[epIndex];
                        }
                        epIndex++;

                        string seedingModeForReplica = versionMajor >= 13 ? _resolvedSeedingMode : null;
                        AddReplica(ag, _resolvedClusterType, _resolvedAvailabilityMode, _resolvedFailoverMode,
                            _resolvedBackupPriority, _resolvedConnectionModeInPrimaryRole, connModeSecondary,
                            Endpoint, Certificate, ConfigureXESession.IsPresent, epUrl, seedingModeForReplica, second);
                    }
                    catch (Exception ex)
                    {
                        CompleteProgress();
                        StopFunction("Failure",
                            errorRecord: new ErrorRecord(ex, "NewDbaAvailabilityGroup_AddReplicaError", ErrorCategory.InvalidOperation, second),
                            target: second, isContinue: true);
                        TestFunctionInterrupt();
                    }
                }
            }

            // Create AG via Invoke-Create
            try
            {
                InvokeCreate(ag);
            }
            catch (Exception ex)
            {
                string msg = GetInnerExceptionMessage(ex);
                if (String.IsNullOrEmpty(msg))
                {
                    msg = ex.Message;
                }
                CompleteProgress();
                StopFunction(msg,
                    errorRecord: new ErrorRecord(ex, "NewDbaAvailabilityGroup_InvokeCreateError", ErrorCategory.InvalidOperation, Primary),
                    target: Primary);
                return;
            }

            // Add listener
            string progressMsg;
            if ((IPAddress != null && IPAddress.Length > 0) || Dhcp.IsPresent)
            {
                progressMsg = "Adding listener";
            }
            else
            {
                progressMsg = "Joining availability group";
            }
            WriteProgress(stepCounter++, progressMsg);

            if (IPAddress != null && IPAddress.Length > 0)
            {
                if (ShouldProcess(Primary != null ? Primary.ToString() : "Primary", String.Format("Adding static IP listener for {0} to the primary replica", Name)))
                {
                    AddListenerIP(ag, IPAddress, SubnetMask, _resolvedPort);
                }
            }
            else if (Dhcp.IsPresent)
            {
                if (ShouldProcess(Primary != null ? Primary.ToString() : "Primary", String.Format("Adding DHCP listener for {0} to the primary replica", Name)))
                {
                    AddListenerDhcp(ag, _resolvedPort);
                }
            }

            // Join secondaries
            WriteProgress(stepCounter++, "Joining availability group");

            foreach (object second in secondaries)
            {
                string secondName = GetStringProperty(second, _getServerName);
                if (ShouldProcess(secondName ?? "secondary", String.Format("Joining to availability group named {0}", Name)))
                {
                    try
                    {
                        JoinAg(second, ag);
                    }
                    catch (Exception ex)
                    {
                        CompleteProgress();
                        StopFunction("Failure",
                            errorRecord: new ErrorRecord(ex, "NewDbaAvailabilityGroup_JoinError", ErrorCategory.InvalidOperation, second),
                            target: second, isContinue: true);
                        TestFunctionInterrupt();
                    }
                    RefreshAvailabilityGroups(second);
                }
            }

            // Wait for replicas to be connected
            WriteProgress(stepCounter++, "Waiting for replicas to be connected and ready");
            WaitForReplicasReady(secondaries);

            // Grant CreateAnyDatabase for automatic seeding
            if (String.Equals(_resolvedSeedingMode, "Automatic", StringComparison.OrdinalIgnoreCase))
            {
                string secondNameForShouldProcess = secondaries.Count > 0 ? GetStringProperty(secondaries[secondaries.Count - 1], _getServerName) : "replicas";
                if (ShouldProcess(secondNameForShouldProcess ?? "replicas", "Granting CreateAnyDatabase permission to the availability group on every replica"))
                {
                    try
                    {
                        GrantAgPermission(server, Name);
                        foreach (object second in secondaries)
                        {
                            GrantAgPermission(second, Name);
                        }
                    }
                    catch (Exception ex)
                    {
                        CompleteProgress();
                        StopFunction("Failure",
                            errorRecord: new ErrorRecord(ex, "NewDbaAvailabilityGroup_GrantPermissionError", ErrorCategory.SecurityError, null));
                    }
                }
            }

            // Add databases
            WriteProgress(stepCounter++, "Adding databases");
            if (Database != null && Database.Length > 0)
            {
                string serverName = GetStringProperty(server, _getServerName);
                if (ShouldProcess(serverName ?? "primary", "Adding databases to Availability Group."))
                {
                    if (Force.IsPresent)
                    {
                        try
                        {
                            RemoveDatabasesFromSecondaries(secondaries, Database);
                        }
                        catch (Exception ex)
                        {
                            CompleteProgress();
                            StopFunction("Failed to remove databases from secondary replicas.",
                                errorRecord: new ErrorRecord(ex, "NewDbaAvailabilityGroup_RemoveDbError", ErrorCategory.InvalidOperation, null));
                        }
                    }

                    try
                    {
                        AddAgDatabases(server, Name, Database, secondaries, UseLastBackup.IsPresent, _resolvedSeedingMode, SharedPath);
                    }
                    catch (Exception ex)
                    {
                        CompleteProgress();
                        StopFunction("Failed to add databases to Availability Group.",
                            errorRecord: new ErrorRecord(ex, "NewDbaAvailabilityGroup_AddDbError", ErrorCategory.InvalidOperation, null));
                    }
                }
            }

            CompleteProgress();

            // Get and output results
            GetAgResult(Primary, PrimarySqlCredential, Name);
        }

        #region Helper Methods

        /// <summary>
        /// Resolves default values from dbatools configuration and parameter defaults.
        /// </summary>
        private void ResolveDefaults()
        {
            if (_defaultsResolved) return;

            _resolvedClusterType = TestBound("ClusterType")
                ? ClusterType
                : GetConfigString("AvailabilityGroups.Default.ClusterType", "Wsfc");

            _resolvedAutomatedBackupPreference = TestBound("AutomatedBackupPreference")
                ? AutomatedBackupPreference
                : "Secondary";

            _resolvedFailureConditionLevel = TestBound("FailureConditionLevel")
                ? FailureConditionLevel
                : GetConfigString("AvailabilityGroups.Default.FailureConditionLevel", "OnCriticalServerErrors");

            _resolvedHealthCheckTimeout = TestBound("HealthCheckTimeout")
                ? HealthCheckTimeout
                : 30000;

            _resolvedAvailabilityMode = TestBound("AvailabilityMode")
                ? AvailabilityMode
                : "SynchronousCommit";

            _resolvedFailoverMode = TestBound("FailoverMode")
                ? FailoverMode
                : "Automatic";

            _resolvedBackupPriority = TestBound("BackupPriority")
                ? BackupPriority
                : 50;

            _resolvedConnectionModeInPrimaryRole = TestBound("ConnectionModeInPrimaryRole")
                ? ConnectionModeInPrimaryRole
                : "AllowAllConnections";

            _resolvedConnectionModeInSecondaryRole = TestBound("ConnectionModeInSecondaryRole")
                ? ConnectionModeInSecondaryRole
                : GetConfigString("AvailabilityGroups.Default.ConnectionModeInSecondaryRole", "AllowNoConnections");

            _resolvedSeedingMode = TestBound("SeedingMode")
                ? SeedingMode
                : "Manual";

            _resolvedPort = TestBound("Port")
                ? Port
                : 1433;

            if (!TestBound("SubnetMask"))
            {
                SubnetMask = System.Net.IPAddress.Parse("255.255.255.0");
            }

            _defaultsResolved = true;
        }

        /// <summary>
        /// Gets a configuration value string from dbatools config.
        /// </summary>
        private string GetConfigString(string name, string fallback)
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, _getConfigValue, null,
                    new object[] { name, fallback });
                if (results != null && results.Count > 0 && results[0] != null)
                {
                    object val = results[0].BaseObject ?? results[0];
                    return val.ToString();
                }
            }
            catch (Exception ex)
            {
                WriteMessageVerbose(String.Format("Failed to read config value '{0}', using fallback '{1}': {2}", name, fallback, ex.Message));
            }
            return fallback;
        }

        /// <summary>
        /// Validates endpoint URL format.
        /// </summary>
        internal static bool IsValidEndpointUrl(string epUrl)
        {
            if (String.IsNullOrEmpty(epUrl)) return false;
            return Regex.IsMatch(epUrl, @"^TCP://[^:]+:\d+$", RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// Normalizes friendly ConnectionModeInSecondaryRole values to their enum equivalents.
        /// </summary>
        internal static string NormalizeConnectionModeInSecondaryRole(string value)
        {
            if (String.IsNullOrEmpty(value)) return value;
            if (String.Equals(value, "No", StringComparison.OrdinalIgnoreCase))
                return "AllowNoConnections";
            if (String.Equals(value, "Read-intent only", StringComparison.OrdinalIgnoreCase))
                return "AllowReadIntentConnectionsOnly";
            if (String.Equals(value, "Yes", StringComparison.OrdinalIgnoreCase))
                return "AllowAllConnections";
            return value;
        }

        /// <summary>
        /// Gets the inner exception message by unwrapping two levels, like the PS1 does.
        /// </summary>
        internal static string GetInnerExceptionMessage(Exception ex)
        {
            if (ex == null) return null;
            if (ex.InnerException != null && ex.InnerException.InnerException != null)
            {
                return ex.InnerException.InnerException.Message;
            }
            if (ex.InnerException != null)
            {
                return ex.InnerException.Message;
            }
            return null;
        }

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

            if (results != null && results.Count > 0 && results[0] != null)
                return results[0].BaseObject ?? results[0];
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
                    if (val is int intVal) return intVal;
                    int parsed;
                    if (int.TryParse(val.ToString(), out parsed)) return parsed;
                }
            }
            catch (Exception)
            {
                // Default
            }
            return 0;
        }

        /// <summary>
        /// Gets a boolean property from a server using a script block.
        /// </summary>
        private bool GetBoolProperty(object server, ScriptBlock script)
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, script, null,
                    new object[] { server });
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
        /// Gets a string property from a server using a script block.
        /// </summary>
        private string GetStringProperty(object server, ScriptBlock script)
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, script, null,
                    new object[] { server });
                if (results != null && results.Count > 0 && results[0] != null)
                {
                    object val = results[0].BaseObject ?? results[0];
                    return val.ToString();
                }
            }
            catch (Exception)
            {
                // Default
            }
            return null;
        }

        /// <summary>
        /// Gets the Get-DbaDefaultPath result.
        /// </summary>
        private object GetDefaultPath(object server)
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, _getDefaultPath, null,
                    new object[] { server });
                if (results != null && results.Count > 0 && results[0] != null)
                    return results[0];
            }
            catch (Exception)
            {
                // Default
            }
            return null;
        }

        /// <summary>
        /// Gets a property string from a PSObject.
        /// </summary>
        private string GetPSObjectPropertyString(object obj, string propertyName)
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
        /// Gets a string result from running a script with one argument.
        /// </summary>
        private string GetStringFromScript(ScriptBlock script, object arg)
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, script, null,
                    new object[] { arg });
                if (results != null && results.Count > 0 && results[0] != null)
                {
                    object val = results[0].BaseObject ?? results[0];
                    return val.ToString();
                }
            }
            catch (Exception)
            {
                // Default
            }
            return null;
        }

        /// <summary>
        /// Checks if an availability group already exists on the primary.
        /// </summary>
        private bool AgExists(object primary, PSCredential credential, string agName)
        {
            try
            {
                Collection<PSObject> results;
                if (credential != null)
                {
                    results = InvokeCommand.InvokeScript(
                        false, _getAgScript, null,
                        new object[] { primary, credential, agName });
                }
                else
                {
                    results = InvokeCommand.InvokeScript(
                        false, _getAgScript, null,
                        new object[] { primary, null, agName });
                }
                return results != null && results.Count > 0 && results[0] != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if a certificate exists on the primary.
        /// </summary>
        private bool CertificateExists(object primary, PSCredential credential, string certName)
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, _getCertScript, null,
                    new object[] { primary, credential, certName });
                return results != null && results.Count > 0 && results[0] != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Tests if a path is accessible from a SQL Server instance.
        /// </summary>
        private bool TestPath(object primary, PSCredential credential, string path)
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, _testPathScript, null,
                    new object[] { primary, credential, path });
                if (results != null && results.Count > 0 && results[0] != null)
                {
                    object val = results[0].BaseObject ?? results[0];
                    if (val is bool boolVal) return boolVal;
                    return Convert.ToBoolean(val);
                }
            }
            catch (Exception)
            {
                return false;
            }
            return false;
        }

        /// <summary>
        /// Gets databases from the primary.
        /// </summary>
        private Collection<PSObject> GetDatabases(object primary, PSCredential credential, string[] databases)
        {
            try
            {
                return InvokeCommand.InvokeScript(
                    false, _getDatabaseScript, null,
                    new object[] { primary, credential, databases });
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Sets database recovery model to Full.
        /// </summary>
        private void SetRecoveryModelFull(object primary, PSCredential credential, string dbName)
        {
            InvokeCommand.InvokeScript(
                false, _setRecoveryModelScript, null,
                new object[] { primary, credential, dbName });
        }

        /// <summary>
        /// Creates the SMO AvailabilityGroup object.
        /// </summary>
        private object CreateAgObject(object server, string name)
        {
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, _newAgObject, null,
                new object[] { server, name });
            if (results != null && results.Count > 0 && results[0] != null)
                return results[0].BaseObject ?? results[0];
            return null;
        }

        /// <summary>
        /// Sets core AG properties.
        /// </summary>
        private void SetAgProperties(object ag, string automatedBackupPreference, string failureConditionLevel, int healthCheckTimeout)
        {
            InvokeCommand.InvokeScript(
                false, _setAgProperties, null,
                new object[] { ag, automatedBackupPreference, failureConditionLevel, healthCheckTimeout });
        }

        /// <summary>
        /// Sets SQL 2016+ AG properties.
        /// </summary>
        private void SetAgV13Properties(object ag, bool basic, bool databaseHealthTrigger, bool dtcSupport)
        {
            InvokeCommand.InvokeScript(
                false, _setAgV13Props, null,
                new object[] { ag, basic, databaseHealthTrigger, dtcSupport });
        }

        /// <summary>
        /// Sets ClusterType on the AG.
        /// </summary>
        private void SetAgClusterType(object ag, string clusterType)
        {
            InvokeCommand.InvokeScript(
                false, _setAgClusterType, null,
                new object[] { ag, clusterType });
        }

        /// <summary>
        /// Sets contained AG properties.
        /// </summary>
        private void SetAgContained(object ag, bool isContained, bool reuseSystemDatabases)
        {
            InvokeCommand.InvokeScript(
                false, _setAgContained, null,
                new object[] { ag, isContained, reuseSystemDatabases });
        }

        /// <summary>
        /// Sets ClusterConnectionOptions on the AG.
        /// </summary>
        private void SetAgClusterConnectionOptions(object ag, string clusterConnectionOption)
        {
            InvokeCommand.InvokeScript(
                false, _setAgClusterConnectionOptions, null,
                new object[] { ag, clusterConnectionOption });
        }

        /// <summary>
        /// Outputs the AG with Select-DefaultView for Passthru mode.
        /// </summary>
        private void OutputPassthru(object ag)
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, _selectDefaultViewPassthru, null,
                    new object[] { ag });
                if (results != null)
                {
                    foreach (PSObject r in results)
                    {
                        if (r != null) WriteObject(r);
                    }
                }
            }
            catch (Exception)
            {
                // Fallback: output the raw AG object
                WriteObject(ag);
            }
        }

        /// <summary>
        /// Adds a replica to the AG using Add-DbaAgReplica.
        /// </summary>
        private void AddReplica(object ag, string clusterType, string availabilityMode, string failoverMode,
            int backupPriority, string connModePrimary, string connModeSecondary,
            string endpoint, string certificate, bool configureXESession,
            string endpointUrl, string seedingMode, object sqlInstance)
        {
            InvokeCommand.InvokeScript(
                false, _addReplicaScript, null,
                new object[] {
                    ag, clusterType, availabilityMode, failoverMode, backupPriority,
                    connModePrimary, connModeSecondary,
                    String.IsNullOrEmpty(endpoint) ? null : endpoint,
                    String.IsNullOrEmpty(certificate) ? null : certificate,
                    configureXESession ? (object)true : null,
                    String.IsNullOrEmpty(endpointUrl) ? null : endpointUrl,
                    String.IsNullOrEmpty(seedingMode) ? null : seedingMode,
                    sqlInstance
                });
        }

        /// <summary>
        /// Creates the AG using Invoke-Create.
        /// </summary>
        private void InvokeCreate(object ag)
        {
            InvokeCommand.InvokeScript(
                false, _invokeCreateScript, null,
                new object[] { ag });
        }

        /// <summary>
        /// Adds a static IP listener.
        /// </summary>
        private void AddListenerIP(object ag, IPAddress[] ipAddresses, IPAddress subnetMask, int port)
        {
            InvokeCommand.InvokeScript(
                false, _addListenerIPScript, null,
                new object[] { ag, ipAddresses, subnetMask, port });
        }

        /// <summary>
        /// Adds a DHCP listener.
        /// </summary>
        private void AddListenerDhcp(object ag, int port)
        {
            InvokeCommand.InvokeScript(
                false, _addListenerDhcpScript, null,
                new object[] { ag, port });
        }

        /// <summary>
        /// Joins a secondary to the AG.
        /// </summary>
        private void JoinAg(object second, object ag)
        {
            InvokeCommand.InvokeScript(
                false, _joinAgScript, null,
                new object[] { second, ag });
        }

        /// <summary>
        /// Refreshes availability groups on a server.
        /// </summary>
        private void RefreshAvailabilityGroups(object server)
        {
            try
            {
                InvokeCommand.InvokeScript(
                    false, _refreshAgScript, null,
                    new object[] { server });
            }
            catch (Exception)
            {
                // Best effort
            }
        }

        /// <summary>
        /// Waits for replicas to be connected and ready (up to 20 seconds).
        /// </summary>
        private void WaitForReplicasReady(List<object> secondaryList)
        {
            if (secondaryList == null || secondaryList.Count == 0) return;

            int wait = 0;
            bool ready = false;

            // Convert list to array for script
            object[] secondaryArray = secondaryList.ToArray();

            do
            {
                System.Threading.Thread.Sleep(500);
                wait++;
                ready = true;

                try
                {
                    Collection<PSObject> states = InvokeCommand.InvokeScript(
                        false, _getReplicaStates, null,
                        new object[] { secondaryArray });
                    if (states != null)
                    {
                        foreach (PSObject state in states)
                        {
                            if (state == null) continue;
                            string connState = GetPSObjectPropertyString(state, "ConnectionState");
                            if (!String.Equals(connState, "Connected", StringComparison.OrdinalIgnoreCase))
                            {
                                ready = false;
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    ready = false;
                }
            } while (!ready && wait <= 40);

            if (!ready || wait > 40)
            {
                WriteMessageWarning("One or more replicas are still not connected and ready. If you encounter this error often, please let us know and we'll increase the timeout. Moving on and trying the next step.");
            }
        }

        /// <summary>
        /// Grants CreateAnyDatabase permission on the AG.
        /// </summary>
        private void GrantAgPermission(object server, string agName)
        {
            InvokeCommand.InvokeScript(
                false, _grantAgPermission, null,
                new object[] { server, agName });
        }

        /// <summary>
        /// Removes databases from secondary replicas.
        /// </summary>
        private void RemoveDatabasesFromSecondaries(List<object> secondaryList, string[] databases)
        {
            object[] secondaryArray = secondaryList.ToArray();
            InvokeCommand.InvokeScript(
                false, _removeDatabaseScript, null,
                new object[] { secondaryArray, databases });
        }

        /// <summary>
        /// Adds databases to the AG.
        /// </summary>
        private void AddAgDatabases(object server, string agName, string[] databases, List<object> secondaryList,
            bool useLastBackup, string seedingMode, string sharedPath)
        {
            object[] secondaryArray = secondaryList.ToArray();
            InvokeCommand.InvokeScript(
                false, _addAgDatabaseScript, null,
                new object[] {
                    server, agName, databases, secondaryArray,
                    useLastBackup,
                    String.IsNullOrEmpty(seedingMode) ? null : seedingMode,
                    String.IsNullOrEmpty(sharedPath) ? null : sharedPath
                });
        }

        /// <summary>
        /// Gets and outputs the final AG result.
        /// </summary>
        private void GetAgResult(object primary, PSCredential credential, string agName)
        {
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, _getAgResultScript, null,
                new object[] { primary, credential, agName });
            if (results != null)
            {
                foreach (PSObject r in results)
                {
                    if (r != null) WriteObject(r);
                }
            }
        }

        /// <summary>
        /// Checks Standard Edition instances and warns about ConnectionModeInSecondaryRole.
        /// </summary>
        private void CheckStandardEditionWarning(object server, List<object> secondaryList)
        {
            List<object> instances = new List<object>();
            instances.Add(server);
            instances.AddRange(secondaryList);

            foreach (object instance in instances)
            {
                string edition = GetStringProperty(instance, _getEngineEdition);
                if (String.Equals(edition, "Standard", StringComparison.OrdinalIgnoreCase))
                {
                    string name = GetStringProperty(instance, _getServerName);
                    WriteMessageWarning(String.Format("ConnectionModeInSecondaryRole is not supported on Standard Edition. The setting will be ignored on {0}. Consider using Enterprise or Developer Edition for read-only secondary replicas.", name));
                }
            }
        }

        private const int TotalProgressSteps = 7;

        /// <summary>
        /// Writes progress for the current activity.
        /// </summary>
        private void WriteProgress(int step, string message)
        {
            int pct = Math.Min(99, (step * 100) / TotalProgressSteps);
            WriteProgress(new ProgressRecord(0, "Adding new availability group", message)
            {
                PercentComplete = pct
            });
        }

        /// <summary>
        /// Completes the progress bar.
        /// </summary>
        private void CompleteProgress()
        {
            try
            {
                WriteProgress(new ProgressRecord(0, "Adding new availability group", "Complete")
                {
                    RecordType = ProgressRecordType.Completed
                });
            }
            catch (Exception)
            {
                // Best effort
            }
        }

        #endregion Helper Methods
    }
}
