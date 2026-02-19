using Dataplat.Dbatools.Message;
using System;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;
using System.Text.RegularExpressions;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Updates PowerShell scripts to replace deprecated dbatools command and parameter names
    /// with current equivalents. Scans through files for over 200 deprecated command names
    /// and dozens of parameter renames, then updates the file content with current naming conventions.
    /// </summary>
    [Cmdlet("Invoke", "DbatoolsRenameHelper", SupportsShouldProcess = true)]
    [OutputType(typeof(PSObject))]
    public class InvokeDbatoolsRenameHelperCommand : DbaBaseCmdlet
    {
        /// <summary>
        /// Specifies the PowerShell script files to scan and update for deprecated dbatools
        /// command and parameter names. Accepts file objects from Get-ChildItem.
        /// </summary>
        [Parameter(Mandatory = true, ValueFromPipeline = true)]
        public FileInfo[] InputObject { get; set; }

        /// <summary>
        /// Sets the character encoding used when writing the updated script files back to disk.
        /// Defaults to UTF8.
        /// </summary>
        [Parameter()]
        [ValidateSet("ASCII", "BigEndianUnicode", "Byte", "String", "Unicode", "UTF7", "UTF8", "Unknown")]
        public string Encoding { get; set; } = "UTF8";

        private Dictionary<string, string> _paramRenames;
        private Dictionary<string, string> _commandRenames;

        /// <summary>
        /// Initializes the rename dictionaries.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();
            _paramRenames = GetParamRenames();
            _commandRenames = GetCommandRenames();
        }

        /// <summary>
        /// Processes each input file, checking for deprecated names and replacing them.
        /// </summary>
        protected override void ProcessRecord()
        {
            foreach (var fileObject in InputObject)
            {
                string file = fileObject.FullName;

                foreach (var entry in _paramRenames)
                {
                    ProcessRename(file, entry.Key, entry.Value);
                }

                foreach (var entry in _commandRenames)
                {
                    ProcessRename(file, entry.Key, entry.Value);
                }
            }
        }

        /// <summary>
        /// Checks if a file contains a deprecated name and replaces it if found.
        /// </summary>
        private void ProcessRename(string file, string oldName, string newName)
        {
            string pattern = String.Format(@"\b{0}\b", oldName);
            string content;
            try
            {
                content = File.ReadAllText(file);
            }
            catch (Exception ex)
            {
                StopFunction(String.Format("Failed to read file {0}", file), exception: ex, target: file, isContinue: true);
                TestFunctionInterrupt();
                return;
            }

            if (!Regex.IsMatch(content, pattern, RegexOptions.IgnoreCase))
                return;

            if (!ShouldProcess(file, String.Format("Replacing {0} with {1}", oldName, newName)))
                return;

            content = Regex.Replace(content, pattern, newName, RegexOptions.IgnoreCase).Trim();

            try
            {
                // Use Set-Content via InvokeCommand for exact parity with PS1 behavior
                // (encoding handling and trailing newline)
                InvokeCommand.InvokeScript(
                    false,
                    ScriptBlock.Create("param($p, $c, $e) Set-Content -Path $p -Value $c -Encoding $e"),
                    null,
                    file, content, Encoding
                );
            }
            catch (Exception ex)
            {
                StopFunction(String.Format("Failed to write file {0}", file), exception: ex, target: file, isContinue: true);
                TestFunctionInterrupt();
                return;
            }

            var result = new PSObject();
            result.Properties.Add(new PSNoteProperty("Path", file));
            result.Properties.Add(new PSNoteProperty("Pattern", oldName));
            result.Properties.Add(new PSNoteProperty("ReplacedWith", newName));
            WriteObject(result);
        }

        /// <summary>
        /// Returns the dictionary of deprecated parameter names and their current equivalents.
        /// </summary>
        internal static Dictionary<string, string> GetParamRenames()
        {
            return new Dictionary<string, string>
            {
                { "ExcludeAllSystemDb", "ExcludeSystem" },
                { "ExcludeAllUserDb", "ExcludeUser" },
                { "Invoke-Sqlcmd2", "Invoke-DbaQuery" },
                { "NetworkShare", "SharedPath" },
                { "NoDatabases", "ExcludeDatabases" },
                { "NoDisabledJobs", "ExcludeDisabledJobs" },
                { "NoJobs", "ExcludeJobs" },
                { "NoJobSteps", "ExcludeJobSteps" },
                { "NoQueryTextColumn", "ExcludeQueryTextColumn" },
                { "NoSystem", "ExcludeSystemLogins" },
                { "NoSystemDb", "ExcludeSystem" },
                { "NoSystemLogins", "ExcludeSystemLogins" },
                { "NoSystemObjects", "ExcludeSystemObjects" },
                { "NoSystemSpid", "ExcludeSystemSpids" },
                { "UseLastBackups", "UseLastBackup" },
                { "PasswordExpiration", "PasswordExpirationEnabled" },
                { "PasswordPolicy", "PasswordPolicyEnforced" },
                { "ServerInstance", "SqlInstance" }
            };
        }

        /// <summary>
        /// Returns the dictionary of deprecated command names and their current equivalents.
        /// </summary>
        internal static Dictionary<string, string> GetCommandRenames()
        {
            return new Dictionary<string, string>
            {
                { "Find-DbaDuplicateIndex", "Find-DbaDbDuplicateIndex" },
                { "Find-DbaDisabledIndex", "Find-DbaDbDisabledIndex" },
                { "Add-DbaRegisteredServer", "Add-DbaRegServer" },
                { "Add-DbaRegisteredServerGroup", "Add-DbaRegServerGroup" },
                { "Backup-DbaDatabaseCertificate", "Backup-DbaDbCertificate" },
                { "Backup-DbaDatabaseMasterKey", "Backup-DbaDbMasterKey" },
                { "Clear-DbaSqlConnectionPool", "Clear-DbaConnectionPool" },
                { "Connect-DbaServer", "Connect-DbaInstance" },
                { "Copy-DbaAgentCategory", "Copy-DbaAgentJobCategory" },
                { "Copy-DbaAgentProxyAccount", "Copy-DbaAgentProxy" },
                { "Copy-DbaAgentSharedSchedule", "Copy-DbaAgentSchedule" },
                { "Copy-DbaCentralManagementServer", "Copy-DbaRegServer" },
                { "Copy-DbaDatabaseAssembly", "Copy-DbaDbAssembly" },
                { "Copy-DbaDatabaseMail", "Copy-DbaDbMail" },
                { "Copy-DbaExtendedEvent", "Copy-DbaXESession" },
                { "Copy-DbaQueryStoreConfig", "Copy-DbaDbQueryStoreOption" },
                { "Copy-DbaSqlDataCollector", "Copy-DbaDataCollector" },
                { "Copy-DbaSqlPolicyManagement", "Copy-DbaPolicyManagement" },
                { "Copy-DbaSqlServerAgent", "Copy-DbaAgentServer" },
                { "Copy-DbaTableData", "Copy-DbaDbTableData" },
                { "Copy-SqlAgentCategory", "Copy-DbaAgentJobCategory" },
                { "Copy-SqlAlert", "Copy-DbaAgentAlert" },
                { "Copy-SqlAudit", "Copy-DbaInstanceAudit" },
                { "Copy-SqlAuditSpecification", "Copy-DbaInstanceAuditSpecification" },
                { "Copy-SqlBackupDevice", "Copy-DbaBackupDevice" },
                { "Copy-SqlCentralManagementServer", "Copy-DbaRegServer" },
                { "Copy-SqlCredential", "Copy-DbaCredential" },
                { "Copy-SqlCustomError", "Copy-DbaCustomError" },
                { "Copy-SqlDatabase", "Copy-DbaDatabase" },
                { "Copy-SqlDatabaseAssembly", "Copy-DbaDbAssembly" },
                { "Copy-SqlDatabaseMail", "Copy-DbaDbMail" },
                { "Copy-SqlDataCollector", "Copy-DbaDataCollector" },
                { "Copy-SqlEndpoint", "Copy-DbaEndpoint" },
                { "Copy-SqlExtendedEvent", "Copy-DbaXESession" },
                { "Copy-SqlJob", "Copy-DbaAgentJob" },
                { "Copy-SqlJobServer", "Copy-SqlInstanceAgent" },
                { "Copy-SqlLinkedServer", "Copy-DbaLinkedServer" },
                { "Copy-SqlLogin", "Copy-DbaLogin" },
                { "Copy-SqlOperator", "Copy-DbaAgentOperator" },
                { "Copy-SqlPolicyManagement", "Copy-DbaPolicyManagement" },
                { "Copy-SqlProxyAccount", "Copy-DbaAgentProxy" },
                { "Copy-SqlResourceGovernor", "Copy-DbaResourceGovernor" },
                { "Copy-SqlInstanceAgent", "Copy-DbaAgentServer" },
                { "Copy-SqlInstanceTrigger", "Copy-DbaInstanceTrigger" },
                { "Copy-SqlSharedSchedule", "Copy-DbaAgentSchedule" },
                { "Copy-SqlSpConfigure", "Copy-DbaSpConfigure" },
                { "Copy-SqlSsisCatalog", "Copy-DbaSsisCatalog" },
                { "Copy-SqlSysDbUserObjects", "Copy-DbaSystemDbUserObject" },
                { "Copy-SqlUserDefinedMessage", "Copy-SqlCustomError" },
                { "Expand-DbaTLogResponsibly", "Expand-DbaDbLogFile" },
                { "Expand-SqlTLogResponsibly", "Expand-DbaDbLogFile" },
                { "Export-DbaDacpac", "Export-DbaDacPackage" },
                { "Export-DbaRegisteredServer", "Export-DbaRegServer" },
                { "Export-SqlLogin", "Export-DbaLogin" },
                { "Export-SqlSpConfigure", "Export-DbaSpConfigure" },
                { "Export-SqlUser", "Export-DbaUser" },
                { "Find-DbaDatabaseGrowthEvent", "Find-DbaDbGrowthEvent" },
                { "Find-SqlDuplicateIndex", "Find-DbaDbDuplicateIndex" },
                { "Find-SqlUnusedIndex", "Find-DbaDbUnusedIndex" },
                { "Get-DbaRegServerName", "Get-DbaRegServer" },
                { "Get-DbaConfig", "Get-DbatoolsConfig" },
                { "Get-DbaConfigValue", "Get-DbatoolsConfigValue" },
                { "Get-DbaDatabaseAssembly", "Get-DbaDbAssembly" },
                { "Get-DbaDatabaseCertificate", "Get-DbaDbCertificate" },
                { "Get-DbaDatabaseEncryption", "Get-DbaDbEncryption" },
                { "Get-DbaDatabaseFile", "Get-DbaDbFile" },
                { "Get-DbaDatabaseFreeSpace", "Get-DbaDbSpace" },
                { "Get-DbaDatabaseMasterKey", "Get-DbaDbMasterKey" },
                { "Get-DbaDatabasePartitionFunction", "Get-DbaDbPartitionFunction" },
                { "Get-DbaDatabasePartitionScheme", "Get-DbaDbPartitionScheme" },
                { "Get-DbaDatabaseSnapshot", "Get-DbaDbSnapshot" },
                { "Get-DbaDatabaseSpace", "Get-DbaDbSpace" },
                { "Get-DbaDatabaseState", "Get-DbaDbState" },
                { "Get-DbaDatabaseUdf", "Get-DbaDbUdf" },
                { "Get-DbaDatabaseUser", "Get-DbaDbUser" },
                { "Get-DbaDatabaseView", "Get-DbaDbView" },
                { "Get-DbaDbQueryStoreOptions", "Get-DbaDbQueryStoreOption" },
                { "Get-DbaDistributor", "Get-DbaRepDistributor" },
                { "Get-DbaInstance", "Connect-DbaInstance" },
                { "Get-DbaJobCategory", "Get-DbaAgentJobCategory" },
                { "Get-DbaLog", "Get-DbaErrorLog" },
                { "Get-DbaLogShippingError", "Get-DbaDbLogShipError" },
                { "Get-DbaOrphanUser", "Get-DbaDbOrphanUser" },
                { "Get-DbaPolicy", "Get-DbaPbmPolicy" },
                { "Get-DbaQueryStoreConfig", "Get-DbaDbQueryStoreOption" },
                { "Get-DbaRegisteredServerGroup", "Get-DbaRegServerGroup" },
                { "Get-DbaRegisteredServerStore", "Get-DbaRegServerStore" },
                { "Get-DbaRestoreHistory", "Get-DbaDbRestoreHistory" },
                { "Get-DbaRoleMember", "Get-DbaDbRoleMember" },
                { "Get-DbaSqlBuildReference", "Get-DbaBuild" },
                { "Get-DbaSqlFeature", "Get-DbaFeature" },
                { "Get-DbaSqlInstanceProperty", "Get-DbaInstanceProperty" },
                { "Get-DbaSqlInstanceUserOption", "Get-DbaInstanceUserOption" },
                { "Get-DbaSqlManagementObject", "Get-DbaManagementObject" },
                { "Get-DbaSqlModule", "Get-DbaModule" },
                { "Get-DbaSqlProductKey", "Get-DbaProductKey" },
                { "Get-DbaSqlRegistryRoot", "Get-DbaRegistryRoot" },
                { "Get-DbaSqlService", "Get-DbaService" },
                { "Get-DbaTable", "Get-DbaDbTable" },
                { "Get-DbaTraceFile", "Get-DbaTrace" },
                { "Get-DbaUserLevelPermission", "Get-DbaUserPermission" },
                { "Get-DbaXEventSession", "Get-DbaXESession" },
                { "Get-DbaXEventSessionTarget", "Get-DbaXESessionTarget" },
                { "Get-DiskSpace", "Get-DbaDiskSpace" },
                { "Get-SqlMaxMemory", "Get-DbaMaxMemory" },
                { "Get-SqlRegisteredServerName", "Get-DbaRegServer" },
                { "Get-SqlInstanceKey", "Get-DbaProductKey" },
                { "Import-DbaCsvToSql", "Import-DbaCsv" },
                { "Import-DbaRegisteredServer", "Import-DbaRegServer" },
                { "Import-SqlSpConfigure", "Import-DbaSpConfigure" },
                { "Install-SqlWhoIsActive", "Install-DbaWhoIsActive" },
                { "Invoke-DbaCmd", "Invoke-DbaQuery" },
                { "Invoke-DbaDatabaseClone", "Invoke-DbaDbClone" },
                { "Invoke-DbaDatabaseShrink", "Invoke-DbaDbShrink" },
                { "Invoke-DbaDatabaseUpgrade", "Invoke-DbaDbUpgrade" },
                { "Invoke-DbaLogShipping", "Invoke-DbaDbLogShipping" },
                { "Invoke-DbaLogShippingRecovery", "Invoke-DbaDbLogShipRecovery" },
                { "Invoke-DbaSqlQuery", "Invoke-DbaQuery" },
                { "Move-DbaRegisteredServer", "Move-DbaRegServer" },
                { "Move-DbaRegisteredServerGroup", "Move-DbaRegServerGroup" },
                { "New-DbaDatabaseCertificate", "New-DbaDbCertificate" },
                { "New-DbaDatabaseMasterKey", "New-DbaDbMasterKey" },
                { "New-DbaDatabaseSnapshot", "New-DbaDbSnapshot" },
                { "New-DbaPublishProfile", "New-DbaDacProfile" },
                { "New-DbaSqlConnectionString", "New-DbaConnectionString" },
                { "New-DbaSqlConnectionStringBuilder", "New-DbaConnectionStringBuilder" },
                { "New-DbaSqlDirectory", "New-DbaDirectory" },
                { "Out-DbaDataTable", "ConvertTo-DbaDataTable" },
                { "Publish-DbaDacpac", "Publish-DbaDacPackage" },
                { "Read-DbaXEventFile", "Read-DbaXEFile" },
                { "Register-DbaConfig", "Register-DbatoolsConfig" },
                { "Remove-DbaDatabaseCertificate", "Remove-DbaDbCertificate" },
                { "Remove-DbaDatabaseMasterKey", "Remove-DbaDbMasterKey" },
                { "Remove-DbaDatabaseSnapshot", "Remove-DbaDbSnapshot" },
                { "Remove-DbaOrphanUser", "Remove-DbaDbOrphanUser" },
                { "Remove-DbaRegisteredServer", "Remove-DbaRegServer" },
                { "Remove-DbaRegisteredServerGroup", "Remove-DbaRegServerGroup" },
                { "Remove-SqlDatabaseSafely", "Remove-DbaDatabaseSafely" },
                { "Remove-SqlOrphanUser", "Remove-DbaDbOrphanUser" },
                { "Repair-DbaOrphanUser", "Repair-DbaDbOrphanUser" },
                { "Repair-SqlOrphanUser", "Repair-DbaDbOrphanUser" },
                { "Reset-SqlAdmin", "Reset-DbaAdmin" },
                { "Reset-SqlSaPassword", "Reset-SqlAdmin" },
                { "Restart-DbaSqlService", "Restart-DbaService" },
                { "Restore-DbaDatabaseCertificate", "Restore-DbaDbCertificate" },
                { "Restore-DbaDatabaseSnapshot", "Restore-DbaDbSnapshot" },
                { "Restore-HallengrenBackup", "Restore-SqlBackupFromDirectory" },
                { "Set-DbaConfig", "Set-DbatoolsConfig" },
                { "Get-DbaBackupHistory", "Get-DbaDbBackupHistory" },
                { "Set-DbaDatabaseOwner", "Set-DbaDbOwner" },
                { "Set-DbaDatabaseState", "Set-DbaDbState" },
                { "Set-DbaDbQueryStoreOptions", "Set-DbaDbQueryStoreOption" },
                { "Set-DbaJobOwner", "Set-DbaAgentJobOwner" },
                { "Set-DbaQueryStoreConfig", "Set-DbaDbQueryStoreOption" },
                { "Set-DbaTempDbConfiguration", "Set-DbaTempdbConfig" },
                { "Set-SqlMaxMemory", "Set-DbaMaxMemory" },
                { "Set-SqlTempDbConfiguration", "Set-DbaTempdbConfig" },
                { "Show-DbaDatabaseList", "Show-DbaDbList" },
                { "Show-SqlDatabaseList", "Show-DbaDbList" },
                { "Show-SqlMigrationConstraint", "Test-SqlMigrationConstraint" },
                { "Show-SqlInstanceFileSystem", "Show-DbaInstanceFileSystem" },
                { "Show-SqlWhoIsActive", "Invoke-DbaWhoIsActive" },
                { "Start-DbaSqlService", "Start-DbaService" },
                { "Start-SqlMigration", "Start-DbaMigration" },
                { "Stop-DbaSqlService", "Stop-DbaService" },
                { "Sync-DbaSqlLoginPermission", "Sync-DbaLoginPermission" },
                { "Sync-SqlLoginPermissions", "Sync-DbaLoginPermission" },
                { "Test-DbaDatabaseCollation", "Test-DbaDbCollation" },
                { "Test-DbaDatabaseCompatibility", "Test-DbaDbCompatibility" },
                { "Test-DbaDatabaseOwner", "Test-DbaDbOwner" },
                { "Test-DbaDbVirtualLogFile", "Measure-DbaDbVirtualLogFile" },
                { "Test-DbaFullRecoveryModel", "Test-DbaDbRecoveryModel" },
                { "Test-DbaJobOwner", "Test-DbaAgentJobOwner" },
                { "Test-DbaLogShippingStatus", "Test-DbaDbLogShipStatus" },
                { "Test-DbaRecoveryModel", "Test-DbaDbRecoveryModel" },
                { "Test-DbaSqlBuild", "Test-DbaBuild" },
                { "Test-DbaSqlManagementObject", "Test-DbaManagementObject" },
                { "Test-DbaSqlPath", "Test-DbaPath" },
                { "Test-DbaTempDbConfiguration", "Test-DbaTempdbConfig" },
                { "Test-DbaValidLogin", "Test-DbaWindowsLogin" },
                { "Test-DbaVirtualLogFile", "Measure-DbaDbVirtualLogFile" },
                { "Test-SqlConnection", "Test-DbaConnection" },
                { "Test-SqlDiskAllocation", "Test-DbaDiskAllocation" },
                { "Test-SqlMigrationConstraint", "Test-DbaMigrationConstraint" },
                { "Test-SqlNetworkLatency", "Test-DbaNetworkLatency" },
                { "Test-SqlPath", "Test-DbaPath" },
                { "Test-SqlTempDbConfiguration", "Test-DbaTempdbConfig" },
                { "Update-DbaSqlServiceAccount", "Update-DbaServiceAccount" },
                { "Watch-DbaXEventSession", "Watch-DbaXESession" },
                { "Watch-SqlDbLogin", "Watch-DbaDbLogin" },
                { "Add-DbaCmsRegServer", "Add-DbaRegServer" },
                { "Add-DbaCmsRegServerGroup", "Add-DbaRegServerGroup" },
                { "Copy-DbaCmsRegServer", "Copy-DbaRegServer" },
                { "Export-DbaCmsRegServer", "Export-DbaRegServer" },
                { "Get-DbaCmsRegistryRoot", "Get-DbaRegistryRoot" },
                { "Get-DbaCmsRegServer", "Get-DbaRegServer" },
                { "Get-DbaCmsRegServerGroup", "Get-DbaRegServerGroup" },
                { "Get-DbaCmsRegServerStore", "Get-DbaRegServerStore" },
                { "Import-DbaCmsRegServer", "Import-DbaRegServer" },
                { "Move-DbaCmsRegServer", "Move-DbaRegServer" },
                { "Move-DbaCmsRegServerGroup", "Move-DbaRegServerGroup" },
                { "Remove-DbaCmsRegServer", "Remove-DbaRegServer" },
                { "Remove-DbaCmsRegServerGroup", "Remove-DbaRegServerGroup" },
                { "Copy-DbaServerAuditSpecification", "Copy-DbaInstanceAuditSpecification" },
                { "Copy-DbaServerAudit", "Copy-DbaInstanceAudit" },
                { "Copy-DbaServerTrigger", "Copy-DbaInstanceTrigger" },
                { "Test-DbaServerName", "Test-DbaInstanceName" },
                { "Test-DbaInstanceName", "Repair-DbaInstanceName" },
                { "Get-DbaServerTrigger", "Get-DbaInstanceTrigger" },
                { "Get-DbaServerAudit", "Get-DbaInstanceAudit" },
                { "Get-DbaServerAuditSpecification", "Get-DbaInstanceAuditSpecification" },
                { "Get-DbaServerInstallDate", "Get-DbaInstanceInstallDate" },
                { "Show-DbaServerFileSystem", "Show-DbaInstanceFileSystem" },
                { "Install-DbaWatchUpdate", "Install-DbatoolsWatchUpdate" },
                { "Uninstall-DbaWatchUpdate", "Uninstall-DbatoolsWatchUpdate" }
            };
        }
    }
}
