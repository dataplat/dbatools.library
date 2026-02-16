#!/bin/bash
# Categorize all 698 dbatools public commands into migration domains
# Output: domain<TAB>command sorted by domain then command

set -eu

PUBDIR="/c/github/dbatools/public"

for f in "$PUBDIR"/*.ps1; do
    cmd=$(basename "$f" .ps1)

    # Already migrated to C# - skip
    case "$cmd" in
        Write-DbaMessage|Set-DbatoolsConfig|Import-DbaCmdlet|Select-DbaObject) echo "DONE	$cmd"; continue ;;
        Get-DbaRunspace|Register-DbaRunspace|Start-DbaRunspace|Stop-DbaRunspace) echo "DONE	$cmd"; continue ;;
        Register-DbaMaintenanceTask) echo "DONE	$cmd"; continue ;;
    esac

    # Extract verb and noun
    verb="${cmd%%-*}"
    noun="${cmd#*-}"

    # Categorize by noun pattern
    domain="misc"

    case "$noun" in
        # === FOUNDATION: Core connectivity, paths, query ===
        DbaInstance)
            case "$verb" in
                Connect|Disconnect) domain="foundation" ;;
                *) domain="instance" ;;
            esac ;;
        DbaConnectedInstance) domain="foundation" ;;
        DbaConnectionString|DbaConnectionStringBuilder|DbaConnectionPool) domain="foundation" ;;
        DbaAzAccessToken) domain="foundation" ;;
        DbaQuery) domain="foundation" ;;
        DbaSqlParameter) domain="foundation" ;;
        DbaNetworkName) domain="foundation" ;;
        DbaPath) domain="foundation" ;;
        DbaScriptingOption) domain="foundation" ;;
        DbaConnection) domain="foundation" ;;

        # === AG: Availability Groups ===
        DbaAvailabilityGroup) domain="ag" ;;
        DbaAgDatabase) domain="ag" ;;
        DbaAgReplica) domain="ag" ;;
        DbaAgListener) domain="ag" ;;
        DbaAgHadr) domain="ag" ;;
        DbaAgPermission) domain="ag" ;;
        DbaAgDbDataMovement) domain="ag" ;;
        DbaAgFailover) domain="ag" ;;
        DbaAgSpn) domain="ag" ;;
        DbaAgDatabaseReplicaState) domain="ag" ;;
        DbaAgBackupHistory) domain="ag" ;;
        DbaAgReplicaSync) domain="ag" ;;
        DbaAgReplicaOperator) domain="ag" ;;
        DbaAgReplicaLogin) domain="ag" ;;
        DbaAgReplicaCredential) domain="ag" ;;
        DbaAgReplicaAgentJob) domain="ag" ;;

        # === AGENT: SQL Agent ===
        DbaAgentJob) domain="agent" ;;
        DbaAgentJobStep) domain="agent" ;;
        DbaAgentJobCategory) domain="agent" ;;
        DbaAgentJobOwner) domain="agent" ;;
        DbaAgentJobOutputFile) domain="agent" ;;
        DbaAgentJobHistory) domain="agent" ;;
        DbaAgentSchedule) domain="agent" ;;
        DbaAgentOperator) domain="agent" ;;
        DbaAgentAlert) domain="agent" ;;
        DbaAgentAlertCategory) domain="agent" ;;
        DbaAgentProxy) domain="agent" ;;
        DbaAgentServer) domain="agent" ;;
        DbaAgentLog) domain="agent" ;;
        DbaAgentAdminAlert) domain="agent" ;;
        DbaRunningJob) domain="agent" ;;

        # === BACKUP: Backup & Restore ===
        DbaBackup)
            case "$verb" in
                Backup) domain="backup" ;;
                Remove) domain="backup" ;;
                *) domain="backup" ;;
            esac ;;
        DbaBackupInformation) domain="backup" ;;
        DbaBackupHistory) domain="backup" ;;
        DbaBackupDevice) domain="backup" ;;
        DbaBackupHeader) domain="backup" ;;
        DbaBackupEncrypted) domain="backup" ;;
        DbaBackupThroughput) domain="backup" ;;
        DbaLastBackup) domain="backup" ;;
        DbaAdvancedRestore) domain="backup" ;;
        DbaDbBackupRestoreHistory) domain="backup" ;;
        DbaDbBackupHistory) domain="backup" ;;
        DbaDbRestoreHistory) domain="backup" ;;
        DbaDatabase)
            case "$verb" in
                Backup|Restore) domain="backup" ;;
                Copy) domain="migration" ;;
                *) domain="database" ;;
            esac ;;
        DbaDbSnapshot)
            case "$verb" in
                Restore) domain="backup" ;;
                *) domain="database" ;;
            esac ;;

        # === DATABASE: Database-level operations ===
        DbaDbOwner) domain="database" ;;
        DbaDbCompatibility|DbaDbCompat) domain="database" ;;
        DbaDbState) domain="database" ;;
        DbaDbRecoveryModel) domain="database" ;;
        DbaDbFile|DbaDbFileGroup|DbaDbFileGrowth|DbaDbFileMapping) domain="database" ;;
        DbaDbSpace|DbaDbLogSpace|DbaDbLogFile) domain="database" ;;
        DbaDbVirtualLogFile) domain="database" ;;
        DbaDbShrink) domain="database" ;;
        DbaDbClone) domain="database" ;;
        DbaDbUpgrade) domain="database" ;;
        DbaDbIdentity) domain="database" ;;
        DbaDbCompression) domain="database" ;;
        DbaDbQueryStoreOption|DbaDbQueryStore) domain="database" ;;
        DbaDbGrowthEvent) domain="database" ;;
        DbaDbExtentDiff) domain="database" ;;
        DbaDbPageInfo) domain="database" ;;
        DbaDbFeatureUsage) domain="database" ;;
        DbaDbMemoryUsage) domain="database" ;;
        DbaDbDetachedFileInfo) domain="database" ;;
        DbaDbCollation|DbaAvailableCollation) domain="database" ;;
        DbaDependency) domain="database" ;;
        DbaDefaultPath) domain="database" ;;
        DbaDbData|DbaDbTableData|DbaDbViewData) domain="database" ;;
        DbaDataTable) domain="database" ;;
        DbaDbTransfer) domain="database" ;;
        DbaBalanceDataFiles) domain="database" ;;
        DbaDbSharePoint) domain="database" ;;
        DbaDbLogin) domain="database" ;;
        DbaDbList) domain="database" ;;
        DbaTimeline) domain="database" ;;
        DbaSimilarTable) domain="database" ;;
        DbaOrphanedFile) domain="database" ;;
        DbaDbDuplicateIndex|DbaDbDisabledIndex|DbaDbUnusedIndex|DbaHelpIndex) domain="database" ;;
        DbaDbPiiScan) domain="database" ;;
        DbaDbDataMasking|DbaDbDataMaskingConfig|DbaDbMaskingConfig) domain="database" ;;
        DbaDbDataGenerator|DbaDbDataGeneratorConfig) domain="database" ;;
        DbaDbDecryptObject) domain="database" ;;
        DbaRandomizedDataset|DbaRandomizedDatasetTemplate|DbaRandomizedType|DbaRandomizedValue) domain="database" ;;
        DbaSuspectPage) domain="database" ;;
        DbaSchemaChangeHistory) domain="database" ;;
        DbaIdentityUsage) domain="database" ;;
        DbaUserObject|DbaSystemDbUserObject|DbaSysDbUserObject) domain="database" ;;
        DbaBinaryFile|DbaBinaryFileTable) domain="database" ;;
        DbaCsv) domain="database" ;;
        DbaDbAzSqlTip) domain="database" ;;
        DbaLastGoodCheckDb) domain="database" ;;

        # === DBOBJECT: Database child objects ===
        DbaDbTable) domain="dbobject" ;;
        DbaDbView) domain="dbobject" ;;
        DbaDbStoredProcedure|DbaStoredProcedure) domain="dbobject" ;;
        DbaDbTrigger|DbaTrigger|DbaDbObjectTrigger) domain="dbobject" ;;
        DbaDbUdf) domain="dbobject" ;;
        DbaDbSchema) domain="dbobject" ;;
        DbaDbSequence|DbaDbSequenceNextValue) domain="dbobject" ;;
        DbaDbSynonym) domain="dbobject" ;;
        DbaDbForeignKey) domain="dbobject" ;;
        DbaDbCheckConstraint) domain="dbobject" ;;
        DbaDbPartitionFunction|DbaDbPartitionScheme) domain="dbobject" ;;
        DbaDbUserDefinedTableType) domain="dbobject" ;;
        DbaDbAssembly) domain="dbobject" ;;
        DbaExtendedProperty) domain="dbobject" ;;
        DbaExtendedStoredProcedure) domain="dbobject" ;;
        DbaModule) domain="dbobject" ;;
        DbaDbServiceBrokerService|DbaDbServiceBrokerQueue) domain="dbobject" ;;
        DbaView) domain="dbobject" ;;
        DbaStartupProcedure) domain="dbobject" ;;
        DbaCustomError) domain="dbobject" ;;

        # === DBCC: DBCC commands ===
        DbaDbDbccCheckConstraint|DbaDbDbccCleanTable|DbaDbDbccUpdateUsage|DbaDbDbccOpenTran) domain="database" ;;
        DbaDbccDropCleanBuffer|DbaDbccFreeCache) domain="database" ;;
        DbaDbccUserOption|DbaDbccStatistic|DbaDbccSessionBuffer) domain="database" ;;
        DbaDbccProcCache|DbaDbccMemoryStatus|DbaDbccHelp) domain="database" ;;

        # === SECURITY: Authentication & authorization ===
        DbaLogin) domain="security" ;;
        DbaDbUser) domain="security" ;;
        DbaDbRole|DbaDbRoleMember) domain="security" ;;
        DbaServerRole|DbaServerRoleMember) domain="security" ;;
        DbaCredential) domain="security" ;;
        DbaPermission) domain="security" ;;
        DbaUserPermission) domain="security" ;;
        DbaLoginPassword) domain="security" ;;
        DbaAdmin) domain="security" ;;
        DbaWindowsLogin) domain="security" ;;
        DbaLoginInGroup) domain="security" ;;
        DbaLoginPermission) domain="security" ;;
        DbaKerberos) domain="security" ;;
        DbaSpn) domain="security" ;;
        DbaDbCertificate) domain="security" ;;
        DbaDbAsymmetricKey) domain="security" ;;
        DbaDbMasterKey) domain="security" ;;
        DbaDbEncryption|DbaDbEncryptionKey) domain="security" ;;
        DbaServiceMasterKey) domain="security" ;;
        DbaServiceAccount) domain="security" ;;
        DbaDbOrphanUser) domain="security" ;;

        # === INSTANCE: Instance-level management ===
        DbaMaxMemory) domain="instance" ;;
        DbaMaxDop) domain="instance" ;;
        DbaSpConfigure) domain="instance" ;;
        DbaErrorLog|DbaErrorLogConfig) domain="instance" ;;
        DbaTrace|DbaTraceFlag|DbaTraceFile) domain="instance" ;;
        DbaTransactionLog) domain="instance" ;;
        DbaUptime) domain="instance" ;;
        DbaFeature|DbaDeprecatedFeature) domain="instance" ;;
        DbaInstanceAudit|DbaInstanceAuditSpecification) domain="instance" ;;
        DbaInstanceProperty|DbaInstanceProtocol) domain="instance" ;;
        DbaInstanceTrigger|DbaInstanceUserOption) domain="instance" ;;
        DbaInstanceName) domain="instance" ;;
        DbaInstanceInstallDate) domain="instance" ;;
        DbaInstanceFileSystem) domain="instance" ;;
        DbaProductKey) domain="instance" ;;
        DbaBuild|DbaBuildReference) domain="instance" ;;
        DbaManagementObject) domain="instance" ;;
        DbaProcess) domain="instance" ;;
        DbaLinkedServer|DbaLinkedServerLogin|DbaLinkedServerConnection) domain="instance" ;;
        DbaOleDbProvider) domain="instance" ;;
        DbaStartupParameter) domain="instance" ;;
        DbaOpenTransaction) domain="instance" ;;
        DbaEstimatedCompletionTime) domain="instance" ;;
        DbaFile) domain="instance" ;;
        DbaDirectory) domain="instance" ;;
        DbaDump) domain="instance" ;;
        DbaRegistryRoot) domain="instance" ;;
        DbaExternalProcess) domain="instance" ;;
        DbaTempDbConfig|DbaTempdbUsage) domain="instance" ;;
        DbaCommand) domain="instance" ;;
        DbaScript) domain="instance" ;;

        # === MONITORING: Wait stats, IO, performance ===
        DbaWaitStatistic|DbaWaitStatistics|DbaWaitResource|DbaWaitingTask) domain="instance" ;;
        DbaIoLatency) domain="instance" ;;
        DbaLatchStatistic|DbaLatchStatistics) domain="instance" ;;
        DbaSpinLockStatistic) domain="instance" ;;
        DbaTopResourceUsage) domain="instance" ;;
        DbaQueryExecutionTime) domain="instance" ;;
        DbaExecutionPlan) domain="instance" ;;
        DbaPlanCache) domain="instance" ;;
        DbaNetworkActivity) domain="instance" ;;
        DbaCpuUsage|DbaCpuRingBuffer) domain="instance" ;;
        DbaInstalledPatch) domain="instance" ;;
        DbaCycleErrorLog) domain="instance" ;;

        # === NETWORK: Endpoints, TCP, certificates, encryption ===
        DbaEndpoint) domain="network" ;;
        DbaTcpPort) domain="network" ;;
        DbaNetworkCertificate) domain="network" ;;
        DbaNetworkConfiguration) domain="network" ;;
        DbaForceNetworkEncryption) domain="network" ;;
        DbaExtendedProtection) domain="network" ;;
        DbaHideInstance) domain="network" ;;
        DbaNetworkLatency) domain="network" ;;
        DbaFirewallRule) domain="network" ;;
        DbaFilestream) domain="network" ;;
        DbaConnectionAuthScheme) domain="network" ;;

        # === XEVENT: Extended Events ===
        DbaXESession) domain="xevent" ;;
        DbaXEStore) domain="xevent" ;;
        DbaXEObject) domain="xevent" ;;
        DbaXEFile) domain="xevent" ;;
        DbaXESessionTarget) domain="xevent" ;;
        DbaXESessionTargetFile) domain="xevent" ;;
        DbaXESessionTemplate) domain="xevent" ;;
        DbaXEReplay) domain="xevent" ;;
        DbaAuditFile) domain="xevent" ;;

        # === CONFIG: dbatools configuration ===
        DbatoolsConfig) domain="config" ;;
        DbatoolsConfigValue) domain="config" ;;
        DbatoolsPath) domain="config" ;;
        DbatoolsLog) domain="config" ;;
        DbatoolsError) domain="config" ;;
        DbatoolsChangeLog) domain="config" ;;
        DbatoolsInsecureConnection) domain="config" ;;
        DbatoolsFormatter) domain="config" ;;
        DbatoolsRenameHelper) domain="config" ;;
        DbatoolsSupportPackage) domain="config" ;;
        DbatoolsImport) domain="config" ;;
        Dbatools) domain="config" ;;

        # === REPLICATION ===
        DbaReplArticle|DbaReplArticleColumn) domain="replication" ;;
        DbaReplPublication) domain="replication" ;;
        DbaReplSubscription) domain="replication" ;;
        DbaReplDistributor) domain="replication" ;;
        DbaReplPublisher) domain="replication" ;;
        DbaReplServer|DbaReplServerSetting) domain="replication" ;;
        DbaReplLatency) domain="replication" ;;
        DbaReplCreationScriptOptions) domain="replication" ;;
        DbaReplPublishing) domain="replication" ;;

        # === COMPUTER: OS-level, WMI, WSFC ===
        DbaComputerCertificate|DbaComputerCertificateSigningRequest|DbaComputerCertificateExpiration) domain="computer" ;;
        DbaComputerSystem) domain="computer" ;;
        DbaCmConnection|DbaCmObject) domain="computer" ;;
        DbaDiskSpace|DbaDiskAlignment|DbaDiskAllocation|DbaDiskSpeed|DbaDiskSpaceRequirement) domain="computer" ;;
        DbaPageFileSetting) domain="computer" ;;
        DbaOperatingSystem) domain="computer" ;;
        DbaMemoryUsage|DbaMemoryCondition) domain="computer" ;;
        DbaPowerPlan) domain="computer" ;;
        DbaPrivilege) domain="computer" ;;
        DbaWindowsLog) domain="computer" ;;
        DbaWsfcAvailableDisk|DbaWsfcCluster|DbaWsfcDisk|DbaWsfcNetwork) domain="computer" ;;
        DbaWsfcNetworkInterface|DbaWsfcNode|DbaWsfcResource|DbaWsfcResourceGroup) domain="computer" ;;
        DbaWsfcResourceType|DbaWsfcRole|DbaWsfcSharedVolume) domain="computer" ;;
        DbaMsdtc) domain="computer" ;;
        DbaClientAlias|DbaClientProtocol) domain="computer" ;;
        DbaLocaleSetting) domain="computer" ;;
        DbaService) domain="computer" ;;

        # === REGSERVER: Registered servers ===
        DbaRegServer) domain="regserver" ;;
        DbaRegServerGroup) domain="regserver" ;;
        DbaRegServerStore) domain="regserver" ;;

        # === RESOURCEGOVERNOR + PBM ===
        DbaResourceGovernor) domain="resourcegovernor" ;;
        DbaRgClassifierFunction) domain="resourcegovernor" ;;
        DbaRgResourcePool) domain="resourcegovernor" ;;
        DbaRgWorkloadGroup) domain="resourcegovernor" ;;
        DbaPbmCategory|DbaPbmCategorySubscription|DbaPbmCondition) domain="resourcegovernor" ;;
        DbaPbmObjectSet|DbaPbmPolicy|DbaPbmStore) domain="resourcegovernor" ;;
        DbaPolicyManagement) domain="resourcegovernor" ;;

        # === PERFCOUNTER: Performance data collectors ===
        DbaPfAvailableCounter) domain="perfcounter" ;;
        DbaPfDataCollector) domain="perfcounter" ;;
        DbaPfDataCollectorCounter|DbaPfDataCollectorCounterSample) domain="perfcounter" ;;
        DbaPfDataCollectorSet|DbaPfDataCollectorSetTemplate) domain="perfcounter" ;;
        DbaPfRelog) domain="perfcounter" ;;
        DbaDataCollector) domain="perfcounter" ;;

        # === LOGSHIPPING + MIRRORING ===
        DbaDbLogShipping|DbaDbLogShipStatus|DbaDbLogShipRecovery|DbaDbLogShipError) domain="logshipping" ;;
        DbaDbMirror|DbaDbMirroring|DbaDbMirrorFailover|DbaDbMirrorMonitor) domain="logshipping" ;;

        # === DBMAIL ===
        DbaDbMail|DbaDbMailAccount|DbaDbMailProfile) domain="dbmail" ;;
        DbaDbMailServer|DbaDbMailLog|DbaDbMailHistory|DbaDbMailConfig) domain="dbmail" ;;

        # === MIGRATION: Copy-Dba* commands ===
        DbaMigration) domain="migration" ;;
        DbaMigrationConstraint) domain="migration" ;;

        # === MAINTENANCE: Community tools, install, diagnostics ===
        DbaWhoIsActive) domain="maintenance" ;;
        DbaFirstResponderKit) domain="maintenance" ;;
        DbaDarlingData) domain="maintenance" ;;
        DbaSqlWatch) domain="maintenance" ;;
        DbaMultiTool) domain="maintenance" ;;
        DbaMaintenanceSolution|DbaMaintenanceSolutionLog) domain="maintenance" ;;
        DbaSqlPackage) domain="maintenance" ;;
        DbaDacPackage|DbaDacProfile|DbaDacOption) domain="maintenance" ;;
        DbaDiagnosticQuery|DbaDiagnosticQueryScript|DbaDiagnosticAdsNotebook) domain="maintenance" ;;
        DbaCommunitySoftware) domain="maintenance" ;;
        DbaKbUpdate) domain="maintenance" ;;
        DbaAdvancedInstall|DbaAdvancedUpdate) domain="maintenance" ;;
        DbaReportingService) domain="maintenance" ;;

        # === SSIS ===
        DbaSsisCatalog|DbaSsisEnvironmentVariable|DbaSsisExecutionHistory) domain="maintenance" ;;

        # === Catch migration (Copy-*) by verb ===
        *)
            case "$verb" in
                Copy) domain="migration" ;;
            esac ;;
    esac

    echo "$domain	$cmd"
done | sort -t'	' -k1,1 -k2,2
