using System;
using System.Collections.Specialized;
using Dataplat.Dbatools.Message;
using Microsoft.SqlServer.Management.Smo;

namespace Dataplat.Dbatools.Connection
{
    public static partial class ConnectionService
    {
        #region SetDefaultInitFields priming
        //'PrimaryFilePath' seems the culprit for slow SMO on databases
        private static readonly string[] Fields2000_Db = new string[] { "Collation", "CompatibilityLevel", "CreateDate", "ID", "IsAccessible", "IsFullTextEnabled", "IsSystemObject", "IsUpdateable", "LastBackupDate", "LastDifferentialBackupDate", "LastLogBackupDate", "Name", "Owner", "ReadOnly", "RecoveryModel", "ReplicationOptions", "Status", "Version" };
        private static readonly string[] Fields200x_Db = new string[] { "Collation", "CompatibilityLevel", "CreateDate", "ID", "IsAccessible", "IsFullTextEnabled", "IsSystemObject", "IsUpdateable", "LastBackupDate", "LastDifferentialBackupDate", "LastLogBackupDate", "Name", "Owner", "ReadOnly", "RecoveryModel", "ReplicationOptions", "Status", "Version", "BrokerEnabled", "DatabaseSnapshotBaseName", "IsMirroringEnabled", "Trustworthy" };
        private static readonly string[] Fields201x_Db = new string[] { "Collation", "CompatibilityLevel", "CreateDate", "ID", "IsAccessible", "IsFullTextEnabled", "IsSystemObject", "IsUpdateable", "LastBackupDate", "LastDifferentialBackupDate", "LastLogBackupDate", "Name", "Owner", "ReadOnly", "RecoveryModel", "ReplicationOptions", "Status", "Version", "BrokerEnabled", "DatabaseSnapshotBaseName", "IsMirroringEnabled", "Trustworthy", "ActiveConnections", "AvailabilityDatabaseSynchronizationState", "AvailabilityGroupName", "ContainmentType", "EncryptionEnabled" };

        private static readonly string[] Fields2000_Login = new string[] { "CreateDate", "DateLastModified", "DefaultDatabase", "DenyWindowsLogin", "IsSystemObject", "Language", "LanguageAlias", "LoginType", "Name", "Sid", "WindowsLoginAccessType" };
        private static readonly string[] Fields200x_Login = new string[] { "CreateDate", "DateLastModified", "DefaultDatabase", "DenyWindowsLogin", "IsSystemObject", "Language", "LanguageAlias", "LoginType", "Name", "Sid", "WindowsLoginAccessType", "AsymmetricKey", "Certificate", "Credential", "ID", "IsDisabled", "IsLocked", "IsPasswordExpired", "MustChangePassword", "PasswordExpirationEnabled", "PasswordPolicyEnforced" };
        private static readonly string[] Fields201x_Login = new string[] { "CreateDate", "DateLastModified", "DefaultDatabase", "DenyWindowsLogin", "IsSystemObject", "Language", "LanguageAlias", "LoginType", "Name", "Sid", "WindowsLoginAccessType", "AsymmetricKey", "Certificate", "Credential", "ID", "IsDisabled", "IsLocked", "IsPasswordExpired", "MustChangePassword", "PasswordExpirationEnabled", "PasswordPolicyEnforced", "PasswordHashAlgorithm" };

        //see #7753
        private static readonly string[] Fields_Job = new string[] { "LastRunOutcome", "CurrentRunStatus", "CurrentRunStep", "CurrentRunRetryAttempt", "NextRunScheduleID", "NextRunDate", "LastRunDate", "JobType", "HasStep", "HasServer", "CurrentRunRetryAttempt", "HasSchedule", "Category", "CategoryID", "CategoryType", "OperatorToEmail", "OperatorToNetSend", "OperatorToPage" };

        private static int _loadedSmoMajorVersion = -1;

        private static int GetLoadedSmoMajorVersion()
        {
            // PS begin block: $loadedSmoVersion from the loaded Microsoft.SqlServer.SMO
            // assembly's location/ProductVersion, later compared -ge 11. The compiled port
            // links the vendored SMO directly (file version 18.x for SqlManagementObjects
            // 181.x), so this resolves once and stays.
            if (_loadedSmoMajorVersion < 0)
            {
                try
                {
                    string location = typeof(Server).Assembly.Location;
                    System.Diagnostics.FileVersionInfo info = System.Diagnostics.FileVersionInfo.GetVersionInfo(location);
                    _loadedSmoMajorVersion = info.ProductMajorPart;
                }
                catch
                {
                    // Single-file or reflection-restricted hosts: the linked SMO is modern.
                    _loadedSmoMajorVersion = 18;
                }
            }
            return _loadedSmoMajorVersion;
        }

        /// <summary>
        /// The SetDefaultInitFields priming of Connect-DbaInstance.ps1, with the exact
        /// per-version field lists (BP-201/BP-202).
        /// </summary>
        /// <param name="server">The connected server</param>
        /// <param name="isAzure">Whether the target is Azure (skips priming)</param>
        /// <param name="messageCallback">Optional verbatim-message sink</param>
        public static void ApplyDefaultInitFields(Server server, bool isAzure, Action<MessageLevel, string> messageCallback)
        {
            // By default, SMO initializes several properties. We push it to the limit and gather a bit more
            // this slows down the connect a smidge but drastically improves overall performance
            // especially when dealing with a multitude of servers
            if (GetLoadedSmoMajorVersion() >= 11 && !isAzure)
            {
                try
                {
                    if (messageCallback != null)
                        messageCallback(MessageLevel.Debug, "SetDefaultInitFields will be used");
                    StringCollection initFieldsDb = new StringCollection();
                    StringCollection initFieldsLogin = new StringCollection();
                    StringCollection initFieldsJob = new StringCollection();
                    if (server.VersionMajor == 8)
                    {
                        // 2000
                        initFieldsDb.AddRange(Fields2000_Db);
                        initFieldsLogin.AddRange(Fields2000_Login);
                    }
                    else if (server.VersionMajor == 9 || server.VersionMajor == 10)
                    {
                        // 2005 and 2008
                        initFieldsDb.AddRange(Fields200x_Db);
                        initFieldsLogin.AddRange(Fields200x_Login);
                    }
                    else if (server.VersionMajor >= 16)
                    {
                        // 2022 and above - exclude ActiveConnections due to performance issue #9282
                        foreach (string field in Fields201x_Db)
                        {
                            if (!String.Equals(field, "ActiveConnections", StringComparison.Ordinal))
                                initFieldsDb.Add(field);
                        }
                        initFieldsLogin.AddRange(Fields201x_Login);
                    }
                    else
                    {
                        // 2012 to 2019
                        initFieldsDb.AddRange(Fields201x_Db);
                        initFieldsLogin.AddRange(Fields201x_Login);
                    }
                    server.SetDefaultInitFields(typeof(Microsoft.SqlServer.Management.Smo.Database), initFieldsDb);
                    server.SetDefaultInitFields(typeof(Microsoft.SqlServer.Management.Smo.Login), initFieldsLogin);
                    //see 7753
                    initFieldsJob.AddRange(Fields_Job);
                    server.SetDefaultInitFields(typeof(Microsoft.SqlServer.Management.Smo.Agent.Job), initFieldsJob);
                }
                catch (Exception ex)
                {
                    if (messageCallback != null)
                        messageCallback(MessageLevel.Debug, String.Format("SetDefaultInitFields failed with {0}", ex.Message));
                    // perhaps a DLL issue, continue going
                }
            }
        }
        #endregion SetDefaultInitFields priming
    }
}
