using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Management.Automation;
using System.Security;
using System.Text.RegularExpressions;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Creates a persistent SQL Server Management Object (SMO) connection for database operations.
    /// This is the core connection command that serves as the foundation for most dbatools operations.
    /// Supports Windows authentication, SQL authentication, Azure AD, access tokens, and
    /// dedicated admin connections. Returns an SMO Server object with added dbatools properties.
    /// </summary>
    [Cmdlet("Connect", "DbaInstance")]
    public class ConnectDbaInstanceCommand : DbaBaseCmdlet
    {
        #region Parameters

        /// <summary>
        /// The target SQL Server instance or instances. Accepts pipeline input.
        /// Also accepts connection strings and SMO server objects.
        /// </summary>
        [Parameter(Mandatory = true, ValueFromPipeline = true)]
        [Alias("Connstring", "ConnectionString")]
        public DbaInstanceParameter[] SqlInstance { get; set; }

        /// <summary>
        /// Credential object used to connect to the SQL Server Instance.
        /// </summary>
        [Parameter()]
        public PSCredential SqlCredential { get; set; }

        /// <summary>
        /// The initial database context for the connection.
        /// </summary>
        [Parameter()]
        public string Database { get; set; }

        /// <summary>
        /// Declares the application workload type when connecting to an Availability Group.
        /// </summary>
        [Parameter()]
        [ValidateSet("ReadOnly", "ReadWrite")]
        public string ApplicationIntent { get; set; }

        /// <summary>
        /// Causes the connection to fail if the target is detected as Azure SQL Database.
        /// </summary>
        [Parameter()]
        public SwitchParameter AzureUnsupported { get; set; }

        /// <summary>
        /// Sets the batch separator for multi-statement SQL execution, defaulting to "GO".
        /// </summary>
        [Parameter()]
        public string BatchSeparator { get; set; }

        /// <summary>
        /// Sets a custom application name in the connection string.
        /// </summary>
        [Parameter()]
        public string ClientName { get; set; }

        /// <summary>
        /// Sets the connection timeout in seconds.
        /// </summary>
        [Parameter()]
        public int ConnectTimeout { get; set; }

        /// <summary>
        /// Forces SSL encryption for the connection.
        /// </summary>
        [Parameter()]
        public SwitchParameter EncryptConnection { get; set; }

        /// <summary>
        /// Specifies the failover partner server name for database mirroring.
        /// </summary>
        [Parameter()]
        public string FailoverPartner { get; set; }

        /// <summary>
        /// Sets the lock timeout in seconds for transactions.
        /// </summary>
        [Parameter()]
        public int LockTimeout { get; set; }

        /// <summary>
        /// Sets the maximum number of connections in the connection pool.
        /// </summary>
        [Parameter()]
        public int MaxPoolSize { get; set; }

        /// <summary>
        /// Sets the minimum number of connections maintained in the connection pool.
        /// </summary>
        [Parameter()]
        public int MinPoolSize { get; set; }

        /// <summary>
        /// Specifies the minimum SQL Server version required.
        /// </summary>
        [Parameter()]
        public int MinimumVersion { get; set; }

        /// <summary>
        /// Enables Multiple Active Result Sets (MARS).
        /// </summary>
        [Parameter()]
        public SwitchParameter MultipleActiveResultSets { get; set; }

        /// <summary>
        /// Enables multi-subnet failover for Availability Groups.
        /// </summary>
        [Parameter()]
        public SwitchParameter MultiSubnetFailover { get; set; }

        /// <summary>
        /// Specifies the network protocol for connecting.
        /// </summary>
        [Parameter()]
        [ValidateSet("TcpIp", "NamedPipes", "Multiprotocol", "AppleTalk", "BanyanVines", "Via", "SharedMemory", "NWLinkIpxSpx")]
        public string NetworkProtocol { get; set; }

        /// <summary>
        /// Creates a dedicated connection bypassing connection pooling.
        /// </summary>
        [Parameter()]
        public SwitchParameter NonPooledConnection { get; set; }

        /// <summary>
        /// Sets the network packet size in bytes.
        /// </summary>
        [Parameter()]
        public int PacketSize { get; set; }

        /// <summary>
        /// Sets the maximum lifetime for pooled connections.
        /// </summary>
        [Parameter()]
        public int PooledConnectionLifetime { get; set; }

        /// <summary>
        /// Controls how SQL commands are processed.
        /// </summary>
        [Parameter()]
        [ValidateSet("CaptureSql", "ExecuteAndCaptureSql", "ExecuteSql")]
        public string SqlExecutionModes { get; set; }

        /// <summary>
        /// Sets the timeout for SQL statement execution.
        /// </summary>
        [Parameter()]
        public int StatementTimeout { get; set; }

        /// <summary>
        /// Bypasses certificate validation when using encrypted connections.
        /// </summary>
        [Parameter()]
        public SwitchParameter TrustServerCertificate { get; set; }

        /// <summary>
        /// Attempts connection with proper TLS validation first, then retries with TrustServerCertificate
        /// if the initial connection fails due to certificate validation.
        /// </summary>
        [Parameter()]
        public SwitchParameter AllowTrustServerCertificate { get; set; }

        /// <summary>
        /// Sets the workstation name visible in SQL Server monitoring.
        /// </summary>
        [Parameter()]
        public string WorkstationId { get; set; }

        /// <summary>
        /// Enables Always Encrypted support.
        /// </summary>
        [Parameter()]
        public SwitchParameter AlwaysEncrypted { get; set; }

        /// <summary>
        /// Adds custom connection string parameters.
        /// </summary>
        [Parameter()]
        public string AppendConnectionString { get; set; }

        /// <summary>
        /// Returns only a SqlConnection object instead of the full SMO server object.
        /// </summary>
        [Parameter()]
        public SwitchParameter SqlConnectionOnly { get; set; }

        /// <summary>
        /// Specifies the domain for Azure SQL Database connections.
        /// </summary>
        [Parameter()]
        public string AzureDomain { get; set; }

        /// <summary>
        /// Specifies the Azure AD tenant ID.
        /// </summary>
        [Parameter()]
        public string Tenant { get; set; }

        /// <summary>
        /// Azure access token for authentication.
        /// </summary>
        [Parameter()]
        public PSObject AccessToken { get; set; }

        /// <summary>
        /// Creates a dedicated administrator connection (DAC).
        /// </summary>
        [Parameter()]
        public SwitchParameter DedicatedAdminConnection { get; set; }

        /// <summary>
        /// Changes exception handling from throwing errors to displaying warnings.
        /// This is the opposite of EnableException - when DisableException is set,
        /// EnableException is false, and vice versa.
        /// </summary>
        [Parameter()]
        public SwitchParameter DisableException { get; set; }

        #endregion Parameters

        #region Private State

        private string _escapedAzureDomain;
        private bool _tryConnString;

        // SMO default init fields by version
        private static readonly string[] Fields2000Db = new string[] {
            "Collation", "CompatibilityLevel", "CreateDate", "ID", "IsAccessible", "IsFullTextEnabled",
            "IsSystemObject", "IsUpdateable", "LastBackupDate", "LastDifferentialBackupDate",
            "LastLogBackupDate", "Name", "Owner", "ReadOnly", "RecoveryModel",
            "ReplicationOptions", "Status", "Version"
        };
        private static readonly string[] Fields200xDb;
        private static readonly string[] Fields201xDb;
        private static readonly string[] Fields2000Login = new string[] {
            "CreateDate", "DateLastModified", "DefaultDatabase", "DenyWindowsLogin",
            "IsSystemObject", "Language", "LanguageAlias", "LoginType", "Name", "Sid",
            "WindowsLoginAccessType"
        };
        private static readonly string[] Fields200xLogin;
        private static readonly string[] Fields201xLogin;
        private static readonly string[] FieldsJob = new string[] {
            "LastRunOutcome", "CurrentRunStatus", "CurrentRunStep", "CurrentRunRetryAttempt",
            "NextRunScheduleID", "NextRunDate", "LastRunDate", "JobType", "HasStep", "HasServer",
            "CurrentRunRetryAttempt", "HasSchedule", "Category", "CategoryID", "CategoryType",
            "OperatorToEmail", "OperatorToNetSend", "OperatorToPage"
        };

        private static readonly string[] IgnoredParameters = new string[] {
            "BatchSeparator", "ClientName", "ConnectTimeout", "EncryptConnection",
            "LockTimeout", "MaxPoolSize", "MinPoolSize", "NetworkProtocol", "PacketSize",
            "PooledConnectionLifetime", "SqlExecutionModes", "TrustServerCertificate",
            "AllowTrustServerCertificate", "WorkstationId", "FailoverPartner",
            "MultipleActiveResultSets", "MultiSubnetFailover", "AppendConnectionString",
            "AccessToken"
        };

        #endregion Private State

        #region Static Constructor

        static ConnectDbaInstanceCommand()
        {
            // Build Fields200xDb = Fields2000Db + extras
            var list = new List<string>(Fields2000Db);
            list.AddRange(new string[] { "BrokerEnabled", "DatabaseSnapshotBaseName", "IsMirroringEnabled", "Trustworthy" });
            Fields200xDb = list.ToArray();

            // Build Fields201xDb = Fields200xDb + extras
            list = new List<string>(Fields200xDb);
            list.AddRange(new string[] { "ActiveConnections", "AvailabilityDatabaseSynchronizationState", "AvailabilityGroupName", "ContainmentType", "EncryptionEnabled" });
            Fields201xDb = list.ToArray();

            // Build Fields200xLogin = Fields2000Login + extras
            list = new List<string>(Fields2000Login);
            list.AddRange(new string[] { "AsymmetricKey", "Certificate", "Credential", "ID", "IsDisabled", "IsLocked", "IsPasswordExpired", "MustChangePassword", "PasswordExpirationEnabled", "PasswordPolicyEnforced" });
            Fields200xLogin = list.ToArray();

            // Build Fields201xLogin = Fields200xLogin + extras
            list = new List<string>(Fields200xLogin);
            list.AddRange(new string[] { "PasswordHashAlgorithm" });
            Fields201xLogin = list.ToArray();
        }

        #endregion Static Constructor

        #region BeginProcessing

        /// <summary>
        /// Initializes defaults and validates parameters.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            // Resolve defaults from config values (mirrors the PS1 begin block)
            ResolveDefaultsFromConfig();

            // Validate credential type
            if (SqlCredential != null)
            {
                if (SqlCredential.GetType() != typeof(PSCredential))
                {
                    StopFunction(String.Format(
                        "The credential parameter was of a non-supported type. Only specify PSCredentials such as generated from Get-Credential. Input was of type {0}",
                        SqlCredential.GetType().FullName));
                    return;
                }
            }

            // Connect-DbaInstance uses opposite exception logic: by default it THROWS (EnableException = true)
            // DisableException reverses this behavior
            if (DisableException.IsPresent)
            {
                EnableException = false;
            }
            else
            {
                EnableException = true;
            }

            // Escape the Azure domain for regex matching
            if (!String.IsNullOrEmpty(AzureDomain))
            {
                _escapedAzureDomain = Regex.Escape(AzureDomain);
            }
        }

        #endregion BeginProcessing

        #region ProcessRecord

        /// <summary>
        /// Processes each SQL Server instance.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (TestFunctionInterrupt())
                return;

            // Handle tenant + GUID username (service principal) scenario
            if (!String.IsNullOrEmpty(Tenant) && AccessToken == null && SqlCredential != null)
            {
                string credUserName = SqlCredential.UserName;
                if (Regex.IsMatch(credUserName, @"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$"))
                {
                    try
                    {
                        // Check if running on Core
                        bool isCore = IsRunningOnCore();
                        if (isCore)
                        {
                            WriteMessageAtLevel(
                                "Generating access tokens is not supported on Core. Will try connection string with Active Directory Service Principal instead. See https://github.com/dataplat/dbatools/pull/7610 for more information.",
                                MessageLevel.Verbose, null);
                            _tryConnString = true;
                        }
                        else
                        {
                            WriteMessageAtLevel("Tenant detected, getting access token", MessageLevel.Verbose, null);
                            AccessToken = InvokeNewDbaAzAccessToken();
                            Tenant = null;
                            SqlCredential = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        string errorMessage = GetDeepestExceptionMessage(ex);
                        StopFunction(String.Format("Failed to get access token for Azure SQL DB ({0})", errorMessage), ex);
                        return;
                    }
                }
            }

            WriteMessageAtLevel("Starting process block", MessageLevel.Debug, null);
            foreach (DbaInstanceParameter instance in SqlInstance)
            {
                ProcessSingleInstance(instance);
            }
        }

        #endregion ProcessRecord

        #region Instance Processing

        private void ProcessSingleInstance(DbaInstanceParameter instance)
        {
            WriteMessageAtLevel(String.Format(
                "Starting loop for '{0}': ComputerName = '{1}', InstanceName = '{2}', IsLocalHost = '{3}', Type = '{4}'",
                instance, instance.ComputerName, instance.InstanceName, instance.IsLocalHost, instance.Type),
                MessageLevel.Verbose, null);

            // Handle service principal connection string rewrite
            if (_tryConnString)
            {
                instance = RewriteForServicePrincipal(instance);
            }

            // Detect Azure
            WriteMessageAtLevel("Immediately checking for Azure", MessageLevel.Debug, null);
            bool isAzure = false;
            if (!String.IsNullOrEmpty(_escapedAzureDomain))
            {
                if (Regex.IsMatch(instance.ComputerName, _escapedAzureDomain)
                    || (instance.InputObject is DbaInstanceParameter inputParam
                        && Regex.IsMatch(inputParam.ComputerName, _escapedAzureDomain)))
                {
                    WriteMessageAtLevel("Azure detected", MessageLevel.Verbose, null);
                    isAzure = true;
                }
            }

            // Determine input type
            string inputObjectType;
            bool isNewConnection;
            object inputObject = null;
            string serverName = null;
            string connectionString = null;

            DetermineInputType(instance, out inputObjectType, out isNewConnection, out inputObject, out serverName, out connectionString);

            // Check for ignored parameters
            CheckIgnoredParameters(inputObjectType);

            // Handle DAC
            if (DedicatedAdminConnection.IsPresent && serverName != null)
            {
                WriteMessageAtLevel("Parameter DedicatedAdminConnection is used, so serverName will be changed and NonPooledConnection will be set.", MessageLevel.Debug, null);
                serverName = "ADMIN:" + serverName;
                NonPooledConnection = true;
            }

            // Create server object
            object server = null;

            if (inputObjectType == "Server")
            {
                server = HandleServerInput(instance, inputObject, ref isNewConnection);
            }
            else if (inputObjectType == "SqlConnection")
            {
                server = CreateServerFromSqlConnection(inputObject);
            }
            else if (inputObjectType == "RegisteredServer" || inputObjectType == "ConnectionString")
            {
                server = CreateServerFromConnectionString(connectionString, serverName);
            }
            else if (inputObjectType == "String")
            {
                server = CreateServerFromString(instance, serverName, isAzure, ref isNewConnection);
            }

            if (server == null)
                return;

            // Log masked connection string
            LogMaskedConnectionString(server);

            // Validate connection by running a simple query
            bool connectionSucceeded = ValidateConnection(server, instance, isNewConnection, inputObjectType, ref isAzure);
            if (!connectionSucceeded)
                return;

            // Check Azure unsupported
            if (AzureUnsupported.IsPresent)
            {
                object engineType = GetPropertyValue(server, "DatabaseEngineType");
                if (engineType != null && String.Equals(engineType.ToString(), "SqlAzureDatabase", StringComparison.OrdinalIgnoreCase))
                {
                    if (isNewConnection) DisconnectServer(server);
                    StopFunction("Azure SQL Database not supported", target: instance, isContinue: true);
                    TestFunctionInterrupt();
                    return;
                }
            }

            // Check minimum version
            if (TestBound("MinimumVersion"))
            {
                object versionMajor = GetPropertyValue(server, "VersionMajor");
                if (versionMajor != null)
                {
                    int major = Convert.ToInt32(versionMajor);
                    if (major < MinimumVersion)
                    {
                        if (isNewConnection) DisconnectServer(server);
                        StopFunction(String.Format("SQL Server version {0} required - {1} not supported.", MinimumVersion, server), target: instance, isContinue: true);
                        TestFunctionInterrupt();
                        return;
                    }
                }
            }

            // SqlConnectionOnly
            if (SqlConnectionOnly.IsPresent)
            {
                object sqlConnObj = GetSqlConnectionObject(server);
                string connStr = GetConnectionStringFromServer(server);
                AddConnectionHashValue(connStr, sqlConnObj);
                WriteMessageAtLevel("We return only SqlConnection in server.ConnectionContext.SqlConnectionObject", MessageLevel.Debug, null);
                WriteObject(sqlConnObj);
                return;
            }

            // Add NoteProperties to server object
            AddServerProperties(server, instance, isAzure);

            // Output the server object
            WriteMessageAtLevel("We return the server object", MessageLevel.Debug, null);
            WriteObject(server);

            // Post-connection work (TEPP cache, default init fields)
            if (isNewConnection && !DedicatedAdminConnection.IsPresent)
            {
                PostConnectionSetup(instance, server, isAzure);
            }

            // Add to connection hash
            string serverConnStr = GetConnectionStringFromServer(server);
            AddConnectionHashValue(serverConnStr, server);

            WriteMessageAtLevel("We are finished with this instance", MessageLevel.Debug, null);
        }

        #endregion Instance Processing

        #region Input Type Detection

        private void DetermineInputType(DbaInstanceParameter instance, out string inputObjectType,
            out bool isNewConnection, out object inputObject, out string serverName, out string connectionString)
        {
            inputObject = null;
            serverName = null;
            connectionString = null;

            DbaInstanceInputType instanceType = instance.Type;

            if (instanceType == DbaInstanceInputType.Server)
            {
                WriteMessageAtLevel("Server object passed in, will do some checks and then return the original object", MessageLevel.Verbose, null);
                inputObjectType = "Server";
                isNewConnection = false;
                inputObject = instance.InputObject;
            }
            else if (instanceType == DbaInstanceInputType.SqlConnection)
            {
                WriteMessageAtLevel("SqlConnection object passed in, will build server object from instance.InputObject, do some checks and then return the server object", MessageLevel.Verbose, null);
                inputObjectType = "SqlConnection";
                isNewConnection = false;
                inputObject = instance.InputObject;
            }
            else if (instanceType == DbaInstanceInputType.RegisteredServer)
            {
                WriteMessageAtLevel("RegisteredServer object passed in, will build empty server object, set connection string from instance.InputObject.ConnectionString, do some checks and then return the server object", MessageLevel.Verbose, null);
                inputObjectType = "RegisteredServer";
                isNewConnection = true;
                inputObject = instance.InputObject;
                serverName = instance.FullSmoName;
                // Get ConnectionString from the RegisteredServer InputObject
                connectionString = GetPropertyValue(instance.InputObject, "ConnectionString") as string;
            }
            else if (instance.IsConnectionString)
            {
                WriteMessageAtLevel("Connection string is passed in, will build empty server object, set connection string from instance.InputObject, do some checks and then return the server object", MessageLevel.Verbose, null);
                inputObjectType = "ConnectionString";
                isNewConnection = true;
                serverName = instance.FullSmoName;
                // Convert connection string using synonym mapping
                connectionString = ConvertConnectionString(instance.InputObject as string ?? instance.InputObject.ToString());
            }
            else
            {
                WriteMessageAtLevel("String is passed in, will build server object from instance object and other parameters, do some checks and then return the server object", MessageLevel.Verbose, null);
                inputObjectType = "String";
                isNewConnection = true;
                serverName = instance.FullSmoName;
            }
        }

        #endregion Input Type Detection

        #region Ignored Parameter Checks

        private void CheckIgnoredParameters(string inputObjectType)
        {
            if (inputObjectType == "Server")
            {
                if (TestBound(IgnoredParameters))
                {
                    WriteMessageAtLevel("Additional parameters are passed in, but they will be ignored", MessageLevel.Warning, null);
                }
            }
            else if (inputObjectType == "RegisteredServer" || inputObjectType == "ConnectionString")
            {
                if (TestBound("TrustServerCertificate"))
                {
                    WriteMessageAtLevel("Additional parameter TrustServerCertificate is passed in and will override other settings", MessageLevel.Verbose, null);
                }
                else
                {
                    // Build combined list: IgnoredParameters + ApplicationIntent + StatementTimeout
                    var combined = new List<string>(IgnoredParameters);
                    combined.Add("ApplicationIntent");
                    combined.Add("StatementTimeout");
                    if (TestBound(combined.ToArray()))
                    {
                        WriteMessageAtLevel("Additional parameters are passed in, but they will be ignored", MessageLevel.Warning, null);
                    }
                }
            }
            else if (inputObjectType == "SqlConnection")
            {
                var combined = new List<string>(IgnoredParameters);
                combined.Add("ApplicationIntent");
                combined.Add("StatementTimeout");
                combined.Add("DedicatedAdminConnection");
                if (TestBound(combined.ToArray()))
                {
                    WriteMessageAtLevel("Additional parameters are passed in, but they will be ignored", MessageLevel.Warning, null);
                }
            }
        }

        #endregion Ignored Parameter Checks

        #region Server Object Creation

        private object HandleServerInput(DbaInstanceParameter instance, object inputObject, ref bool isNewConnection)
        {
            bool copyContext = false;
            bool createNewConnection = false;

            // Check if Database requires context copy
            if (!String.IsNullOrEmpty(Database))
            {
                WriteMessageAtLevel(String.Format("Database [{0}] provided.", Database), MessageLevel.Debug, null);

                string currentDb = GetCurrentDatabase(inputObject);
                if (String.IsNullOrEmpty(currentDb))
                {
                    WriteMessageAtLevel("ConnectionContext.CurrentDatabase is empty, so connection will be opened to get the value", MessageLevel.Debug, null);
                    ConnectContext(inputObject);
                    currentDb = GetCurrentDatabase(inputObject);
                    WriteMessageAtLevel(String.Format("ConnectionContext.CurrentDatabase is now [{0}]", currentDb), MessageLevel.Debug, null);
                }

                if (!String.Equals(currentDb, Database, StringComparison.OrdinalIgnoreCase))
                {
                    WriteMessageAtLevel(String.Format("Database [{0}] provided. Does not match ConnectionContext.CurrentDatabase [{1}], copying ConnectionContext and setting the CurrentDatabase", Database, currentDb), MessageLevel.Verbose, null);
                    copyContext = true;

                    string connectAsUserName = GetConnectAsUserName(inputObject);
                    if (!String.IsNullOrEmpty(connectAsUserName))
                    {
                        WriteMessageAtLevel(String.Format("Using ConnectAsUserName [{0}], so changing database context is not possible without loosing this information. We will create a new connection targeting database [{1}]", connectAsUserName, Database), MessageLevel.Debug, null);
                        createNewConnection = true;
                    }
                }
            }

            // Check ApplicationIntent
            if (!String.IsNullOrEmpty(ApplicationIntent))
            {
                string currentIntent = GetConnectionContextProperty(inputObject, "ApplicationIntent") as string;
                if (!String.Equals(currentIntent, ApplicationIntent, StringComparison.OrdinalIgnoreCase))
                {
                    WriteMessageAtLevel("ApplicationIntent provided. Does not match ConnectionContext.ApplicationIntent, copying ConnectionContext and setting the ApplicationIntent", MessageLevel.Verbose, null);
                    copyContext = true;
                }
            }

            // Check NonPooledConnection
            if (NonPooledConnection.IsPresent)
            {
                object currentNpc = GetConnectionContextProperty(inputObject, "NonPooledConnection");
                if (currentNpc == null || !(bool)currentNpc)
                {
                    WriteMessageAtLevel("NonPooledConnection provided. Does not match ConnectionContext.NonPooledConnection, copying ConnectionContext and setting NonPooledConnection", MessageLevel.Verbose, null);
                    copyContext = true;
                }
            }

            // Check StatementTimeout
            if (TestBound("StatementTimeout"))
            {
                object currentSt = GetConnectionContextProperty(inputObject, "StatementTimeout");
                if (currentSt != null && Convert.ToInt32(currentSt) != StatementTimeout)
                {
                    WriteMessageAtLevel("StatementTimeout provided. Does not match ConnectionContext.StatementTimeout, copying ConnectionContext and setting the StatementTimeout", MessageLevel.Verbose, null);
                    copyContext = true;
                }
            }

            // Check DedicatedAdminConnection
            if (DedicatedAdminConnection.IsPresent)
            {
                string serverInstance = GetConnectionContextProperty(inputObject, "ServerInstance") as string;
                if (serverInstance != null && !serverInstance.StartsWith("ADMIN:", StringComparison.OrdinalIgnoreCase))
                {
                    WriteMessageAtLevel("DedicatedAdminConnection provided. Does not match ConnectionContext.ServerInstance, copying ConnectionContext and setting the ServerInstance", MessageLevel.Verbose, null);
                    copyContext = true;
                }
            }

            if (createNewConnection)
            {
                isNewConnection = true;
                return RecursiveConnectWithNewCredentials(inputObject);
            }
            else if (copyContext)
            {
                isNewConnection = true;
                return CopyAndModifyContext(inputObject);
            }
            else
            {
                return inputObject;
            }
        }

        private object RecursiveConnectWithNewCredentials(object inputObject)
        {
            // Get ConnectAsUserName and ConnectAsUserPassword, build credential, recursively call Connect-DbaInstance
            string connectAsUserName = GetConnectAsUserName(inputObject);
            string connectAsUserPassword = GetConnectionContextProperty(inputObject, "ConnectAsUserPassword") as string;
            string serverName = GetPropertyValue(inputObject, "Name") as string;

            string script = @"
param($srvName, $db, $user, $pass, $boundParams)
$secPass = ConvertTo-SecureString -String $pass -AsPlainText -Force
$cred = New-Object System.Management.Automation.PSCredential($user, $secPass)
$p = @{}
foreach ($key in $boundParams.Keys) {
    if ($key -notin 'SqlInstance','SqlCredential') { $p[$key] = $boundParams[$key] }
}
Connect-DbaInstance -SqlInstance $srvName -SqlCredential $cred @p
";
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null,
                    serverName, Database, connectAsUserName, connectAsUserPassword, MyInvocation.BoundParameters);
                if (results != null && results.Count > 0)
                    return results[0].BaseObject;
            }
            catch (Exception ex)
            {
                StopFunction(String.Format("Failed recursive connect for {0}", serverName), exception: ex, target: serverName, isContinue: true);
                TestFunctionInterrupt();
            }
            return null;
        }

        private object CopyAndModifyContext(object inputObject)
        {
            string script = @"
param($server, $appIntent, $nonPooled, $stTimeout, $stTimeoutBound, $database, $dac)
$connContext = $server.ConnectionContext.Copy()
if ($appIntent) {
    $connContext.ApplicationIntent = $appIntent
}
if ($nonPooled) {
    $connContext.NonPooledConnection = $true
}
if ($stTimeoutBound) {
    $connContext.StatementTimeout = $stTimeout
}
if ($dac -and $server.ConnectionContext.ServerInstance -notmatch '^ADMIN:') {
    $connContext.ServerInstance = 'ADMIN:' + $connContext.ServerInstance
    $connContext.NonPooledConnection = $true
}
if ($database) {
    $savedStatementTimeout = $connContext.StatementTimeout
    $connContext = $connContext.GetDatabaseConnection($database, $false)
    $connContext.StatementTimeout = $savedStatementTimeout
}
$newServer = New-Object -TypeName Microsoft.SqlServer.Management.Smo.Server -ArgumentList $connContext
if ($database -and $newServer.ConnectionContext.CurrentDatabase -ne $database) {
    Write-Message -Level Warning -Message ""Changing connection context to database $database was not successful. Current database is $($newServer.ConnectionContext.CurrentDatabase). Please open an issue on https://github.com/dataplat/dbatools/issues.""
}
$newServer
";
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null,
                    inputObject,
                    ApplicationIntent,
                    NonPooledConnection.IsPresent,
                    StatementTimeout,
                    TestBound("StatementTimeout"),
                    Database,
                    DedicatedAdminConnection.IsPresent);
                if (results != null && results.Count > 0)
                    return results[0].BaseObject;
            }
            catch (Exception ex)
            {
                StopFunction("Failed to copy connection context", exception: ex, target: inputObject, isContinue: true);
                TestFunctionInterrupt();
            }
            return null;
        }

        private object CreateServerFromSqlConnection(object inputObject)
        {
            string script = "param($conn) New-Object -TypeName Microsoft.SqlServer.Management.Smo.Server -ArgumentList $conn";
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, inputObject);
                if (results != null && results.Count > 0)
                    return results[0].BaseObject;
            }
            catch (Exception ex)
            {
                StopFunction("Failed to create server from SqlConnection", exception: ex, target: inputObject, isContinue: true);
                TestFunctionInterrupt();
            }
            return null;
        }

        private object CreateServerFromConnectionString(string connString, string serverName)
        {
            // Mirrors PS1: SqlConnectionInfo from connection string properties, then ServerConnection -> Server
            string script = @"
param($connectionString, $trustCert, $trustCertBound, $database)
$sqlConnectionInfo = New-Object -TypeName Microsoft.SqlServer.Management.Common.SqlConnectionInfo
$csb = New-Object -TypeName Microsoft.Data.SqlClient.SqlConnectionStringBuilder -ArgumentList $connectionString

if ($csb.ShouldSerialize('Data Source')) {
    Write-Message -Level Debug -Message ""ServerName will be set to '$($csb.DataSource)'""
    $sqlConnectionInfo.ServerName = $csb.DataSource
    $null = $csb.Remove('Data Source')
}
if ($csb.ShouldSerialize('User ID')) {
    Write-Message -Level Debug -Message ""UserName will be set to '$($csb.UserID)'""
    $sqlConnectionInfo.UserName = $csb.UserID
    $null = $csb.Remove('User ID')
}
if ($csb.ShouldSerialize('Password')) {
    Write-Message -Level Debug -Message ""Password will be set""
    $sqlConnectionInfo.Password = $csb.Password
    $null = $csb.Remove('Password')
}

$specifiedDatabase = $csb['Database']
if ($specifiedDatabase -eq '') {
    $specifiedDatabase = $csb['Initial Catalog']
}
if ($database -and $database -ne $specifiedDatabase) {
    Write-Message -Level Debug -Message ""Database specified in connection string '$specifiedDatabase' does not match Database parameter '$database'. Database parameter will be used.""
    if ($csb.ShouldSerialize('Database')) { $csb.Remove('Database') }
    if ($csb.ShouldSerialize('Initial Catalog')) { $csb.Remove('Initial Catalog') }
    $sqlConnectionInfo.DatabaseName = $database
}

$sqlConnectionInfo.AdditionalParameters = $csb.ConnectionString

if ($trustCertBound) {
    Write-Message -Level Debug -Message ""TrustServerCertificate will be set to '$trustCert'""
    $sqlConnectionInfo.TrustServerCertificate = $trustCert
}

$serverConnection = New-Object -TypeName Microsoft.SqlServer.Management.Common.ServerConnection -ArgumentList $sqlConnectionInfo
New-Object -TypeName Microsoft.SqlServer.Management.Smo.Server -ArgumentList $serverConnection
";
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null,
                    connString, TrustServerCertificate.IsPresent, TestBound("TrustServerCertificate"), Database);
                if (results != null && results.Count > 0)
                    return results[0].BaseObject;
            }
            catch (Exception ex)
            {
                StopFunction("Failed to create server from connection string", exception: ex, target: serverName, isContinue: true);
                TestFunctionInterrupt();
            }
            return null;
        }

        private object CreateServerFromString(DbaInstanceParameter instance, string serverName, bool isAzure, ref bool isNewConnection)
        {
            // Determine auth type
            string authType = isAzure ? "azure " : "local ";
            string username = null;

            if (SqlCredential != null)
            {
                username = SqlCredential.UserName.TrimStart('\\');
                // Swap domain\user to user@domain when domain-joined
                string userDomain = Environment.GetEnvironmentVariable("USERDOMAIN") ?? "";
                string computerName = Environment.MachineName;
                if (!String.Equals(userDomain, computerName, StringComparison.OrdinalIgnoreCase))
                {
                    if (username.Contains("\\"))
                    {
                        string[] parts = username.Split(new char[] { '\\' }, 2);
                        username = String.Format("{0}@{1}", parts[1], parts[0]);
                    }
                }
                if (username.Contains("@") || username.Contains("\\"))
                {
                    authType += "ad";
                }
                else
                {
                    authType += "sql";
                }
            }
            else if (AccessToken != null)
            {
                authType += "token";
            }
            else
            {
                authType += "integrated";
            }
            WriteMessageAtLevel(String.Format("authentication method is '{0}'", authType), MessageLevel.Verbose, null);

            // Build server via PowerShell using SqlConnectionInfo -> ServerConnection -> Server
            // This preserves exact compatibility with SMO connection pooling
            string script = BuildCreateServerScript(authType);

            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null,
                    serverName,
                    authType,
                    username,
                    SqlCredential,
                    AccessToken,
                    AppendConnectionString,
                    FailoverPartner,
                    MultiSubnetFailover.IsPresent,
                    AlwaysEncrypted.IsPresent,
                    ApplicationIntent,
                    ClientName,
                    NetworkProtocol,
                    ConnectTimeout,
                    Database,
                    instance.FullSmoName,
                    EncryptConnection.IsPresent,
                    MaxPoolSize,
                    MinPoolSize,
                    PacketSize,
                    PooledConnectionLifetime,
                    NonPooledConnection.IsPresent,
                    TrustServerCertificate.IsPresent,
                    WorkstationId,
                    BatchSeparator,
                    TestBound("BatchSeparator"),
                    LockTimeout,
                    TestBound("LockTimeout"),
                    MultipleActiveResultSets.IsPresent,
                    SqlExecutionModes,
                    TestBound("SqlExecutionModes"),
                    StatementTimeout);

                if (results != null && results.Count > 0)
                    return results[0].BaseObject;
            }
            catch (Exception ex)
            {
                StopFunction(String.Format("Failed to create server for {0}", serverName), exception: ex, target: serverName, isContinue: true);
                TestFunctionInterrupt();
            }
            return null;
        }

        private string BuildCreateServerScript(string authType)
        {
            return @"
param(
    $serverName, $authType, $username, $sqlCredential, $accessToken,
    $appendConnectionString, $failoverPartner, $multiSubnetFailover,
    $alwaysEncrypted, $applicationIntent, $clientName, $networkProtocol,
    $connectTimeout, $database, $fullSmoName, $encryptConnection,
    $maxPoolSize, $minPoolSize, $packetSize, $pooledConnectionLifetime,
    $nonPooledConnection, $trustServerCertificate, $workstationId,
    $batchSeparator, $batchSeparatorBound, $lockTimeout, $lockTimeoutBound,
    $multipleActiveResultSets, $sqlExecutionModes, $sqlExecutionModesBound,
    $statementTimeout
)

$sqlConnectionInfo = New-Object -TypeName Microsoft.SqlServer.Management.Common.SqlConnectionInfo -ArgumentList $serverName

# AdditionalParameters
if ($appendConnectionString) {
    Write-Message -Level Debug -Message ""AdditionalParameters will be appended by '$appendConnectionString;'""
    $sqlConnectionInfo.AdditionalParameters += ""$appendConnectionString;""
}
if ($failoverPartner) {
    Write-Message -Level Debug -Message ""AdditionalParameters will be appended by 'FailoverPartner=$failoverPartner;'""
    $sqlConnectionInfo.AdditionalParameters += ""FailoverPartner=$failoverPartner;""
}
if ($multiSubnetFailover) {
    Write-Message -Level Debug -Message ""AdditionalParameters will be appended by 'MultiSubnetFailover=True;'""
    $sqlConnectionInfo.AdditionalParameters += 'MultiSubnetFailover=True;'
}
if ($alwaysEncrypted) {
    Write-Message -Level Debug -Message ""AdditionalParameters will be appended by 'Column Encryption Setting=enabled;'""
    $sqlConnectionInfo.AdditionalParameters += 'Column Encryption Setting=enabled;'
}

# ApplicationIntent
if ($applicationIntent) {
    Write-Message -Level Debug -Message ""ApplicationIntent will be set to '$applicationIntent'""
    $sqlConnectionInfo.ApplicationIntent = $applicationIntent
}

# ApplicationName
if ($clientName) {
    Write-Message -Level Debug -Message ""ApplicationName will be set to '$clientName'""
    $sqlConnectionInfo.ApplicationName = $clientName
}

# Authentication for Azure
if ($authType -eq 'azure integrated') {
    Write-Message -Level Debug -Message ""Authentication will be set to 'ActiveDirectoryIntegrated'""
    $sqlConnectionInfo.Authentication = [Microsoft.SqlServer.Management.Common.SqlConnectionInfo+AuthenticationMethod]::ActiveDirectoryIntegrated
} elseif ($authType -eq 'azure ad') {
    Write-Message -Level Debug -Message ""Authentication will be set to 'ActiveDirectoryPassword'""
    $sqlConnectionInfo.Authentication = [Microsoft.SqlServer.Management.Common.SqlConnectionInfo+AuthenticationMethod]::ActiveDirectoryPassword
}

# ConnectionProtocol
if ($networkProtocol) {
    Write-Message -Level Debug -Message ""ConnectionProtocol will be set to '$networkProtocol'""
    $sqlConnectionInfo.ConnectionProtocol = $networkProtocol
}

# ConnectionTimeout
if ($connectTimeout) {
    Write-Message -Level Debug -Message ""ConnectionTimeout will be set to '$connectTimeout'""
    $sqlConnectionInfo.ConnectionTimeout = $connectTimeout
}

# DatabaseName
if ($database) {
    Write-Message -Level Debug -Message ""Database will be set to '$database'""
    $sqlConnectionInfo.DatabaseName = $database
}

# EncryptConnection (skip for localdb)
if ($fullSmoName -notmatch 'localdb') {
    Write-Message -Level Debug -Message ""EncryptConnection will be set to '$encryptConnection'""
    $sqlConnectionInfo.EncryptConnection = $encryptConnection
} else {
    Write-Message -Level Verbose -Message ""localdb detected, skipping unsupported keyword 'Encryption'""
}

# MaxPoolSize
if ($maxPoolSize) {
    Write-Message -Level Debug -Message ""MaxPoolSize will be set to '$maxPoolSize'""
    $sqlConnectionInfo.MaxPoolSize = $maxPoolSize
}

# MinPoolSize
if ($minPoolSize) {
    Write-Message -Level Debug -Message ""MinPoolSize will be set to '$minPoolSize'""
    $sqlConnectionInfo.MinPoolSize = $minPoolSize
}

# PacketSize
if ($packetSize) {
    Write-Message -Level Debug -Message ""PacketSize will be set to '$packetSize'""
    $sqlConnectionInfo.PacketSize = $packetSize
}

# PoolConnectionLifeTime
if ($pooledConnectionLifetime) {
    Write-Message -Level Debug -Message ""PoolConnectionLifeTime will be set to '$pooledConnectionLifetime'""
    $sqlConnectionInfo.PoolConnectionLifeTime = $pooledConnectionLifetime
}

# Pooled
if ($nonPooledConnection) {
    Write-Message -Level Debug -Message ""Pooled will be set to 'False'""
    $sqlConnectionInfo.Pooled = $false
} else {
    Write-Message -Level Debug -Message ""Pooled will be set to 'True'""
    $sqlConnectionInfo.Pooled = $true
}

# SecurePassword and UserName
if ($authType -in 'azure ad', 'azure sql', 'local sql') {
    Write-Message -Level Debug -Message ""SecurePassword will be set""
    $sqlConnectionInfo.SecurePassword = $sqlCredential.Password
    Write-Message -Level Debug -Message ""UserName will be set to '$username'""
    $sqlConnectionInfo.UserName = $username
}

# TrustServerCertificate
Write-Message -Level Debug -Message ""TrustServerCertificate will be set to '$trustServerCertificate'""
$sqlConnectionInfo.TrustServerCertificate = $trustServerCertificate

# WorkstationId
if ($workstationId) {
    Write-Message -Level Debug -Message ""WorkstationId will be set to '$workstationId'""
    $sqlConnectionInfo.WorkstationId = $workstationId
}

# Handle AccessToken: build SqlConnection with token
if ($accessToken) {
    # Resolve the access token value
    $tokenValue = $null
    if ($accessToken | Get-Member | Where-Object Name -eq GetAccessToken) {
        Write-Message -Level Debug -Message ""Token was generated using New-DbaAzAccessToken, executing GetAccessToken()""
        $tokenValue = $accessToken.GetAccessToken()
    } elseif ($accessToken | Get-Member | Where-Object Name -eq Token) {
        Write-Message -Level Debug -Message ""Token was generated using Get-AzAccessToken, getting .Token""
        $rawToken = $accessToken.Token
        if ($rawToken -is [System.Security.SecureString]) {
            Write-Message -Level Debug -Message ""Token is SecureString (Azure PowerShell v14+), converting to plain text""
            $tokenValue = (New-Object PSCredential -ArgumentList 'fake', $rawToken).GetNetworkCredential().Password
        } else {
            Write-Message -Level Debug -Message ""Token is plain text string (Azure PowerShell v13 and earlier)""
            $tokenValue = $rawToken
        }
    } elseif ($accessToken -is [System.Security.SecureString]) {
        Write-Message -Level Debug -Message ""AccessToken is directly provided as SecureString, converting to plain text""
        $tokenValue = (New-Object PSCredential -ArgumentList 'fake', $accessToken).GetNetworkCredential().Password
    } elseif ($accessToken -is [string]) {
        $tokenValue = $accessToken
    } else {
        $tokenValue = [string]$accessToken
    }

    Write-Message -Level Debug -Message ""We have an AccessToken and build a SqlConnection with that token""
    Write-Message -Level Debug -Message ""But we remove 'Integrated Security=True;'""
    $connectionString = $sqlConnectionInfo.ConnectionString -replace 'Integrated Security=True;', ''
    $sqlConnection = New-Object -TypeName Microsoft.Data.SqlClient.SqlConnection -ArgumentList $connectionString
    $sqlConnection.AccessToken = $tokenValue
    Write-Message -Level Debug -Message ""Building ServerConnection from SqlConnection""
    $serverConnection = New-Object -TypeName Microsoft.SqlServer.Management.Common.ServerConnection -ArgumentList $sqlConnection
} else {
    Write-Message -Level Debug -Message ""Building ServerConnection from SqlConnectionInfo""
    $serverConnection = New-Object -TypeName Microsoft.SqlServer.Management.Common.ServerConnection -ArgumentList $sqlConnectionInfo
}

# ConnectAsUser for local AD auth
if ($authType -eq 'local ad') {
    if ($IsLinux -or $IsMacOS) {
        Stop-Function -Target $serverName -Message ""Cannot use Windows credentials to connect when host is Linux or OS X. Use kinit instead. See https://github.com/dataplat/dbatools/issues/7602 for more info.""
        return
    }
    Write-Message -Level Debug -Message ""ConnectAsUser will be set to 'True'""
    $serverConnection.ConnectAsUser = $true
    Write-Message -Level Debug -Message ""ConnectAsUserName will be set to '$username'""
    $serverConnection.ConnectAsUserName = $username
    Write-Message -Level Debug -Message ""ConnectAsUserPassword will be set""
    $serverConnection.ConnectAsUserPassword = $sqlCredential.GetNetworkCredential().Password
}

Write-Message -Level Debug -Message ""Building Server from ServerConnection""
$server = New-Object -TypeName Microsoft.SqlServer.Management.Smo.Server -ArgumentList $serverConnection

# Set ConnectionContext properties not part of SqlConnectionInfo
if ($batchSeparatorBound) {
    Write-Message -Level Debug -Message ""Setting ConnectionContext.BatchSeparator to '$batchSeparator'""
    $server.ConnectionContext.BatchSeparator = $batchSeparator
}
if ($lockTimeoutBound) {
    Write-Message -Level Debug -Message ""Setting ConnectionContext.LockTimeout to '$lockTimeout'""
    $server.ConnectionContext.LockTimeout = $lockTimeout
}
if ($multipleActiveResultSets) {
    Write-Message -Level Debug -Message ""Setting ConnectionContext.MultipleActiveResultSets to 'True'""
    $server.ConnectionContext.MultipleActiveResultSets = $true
}
if ($sqlExecutionModesBound) {
    Write-Message -Level Debug -Message ""Setting ConnectionContext.SqlExecutionModes to '$sqlExecutionModes'""
    $server.ConnectionContext.SqlExecutionModes = $sqlExecutionModes
}
Write-Message -Level Debug -Message ""Setting ConnectionContext.StatementTimeout to '$statementTimeout'""
$server.ConnectionContext.StatementTimeout = $statementTimeout

$server
";
        }

        #endregion Server Object Creation

        #region Connection Validation

        private bool ValidateConnection(object server, DbaInstanceParameter instance, bool isNewConnection, string inputObjectType, ref bool isAzure)
        {
            bool connectionSucceeded = false;
            Exception connectionError = null;

            try
            {
                WriteMessageAtLevel("We connect to the instance by running SELECT 'dbatools is opening a new connection'", MessageLevel.Debug, null);
                InvokeExecuteWithResults(server, "SELECT 'dbatools is opening a new connection'");
                WriteMessageAtLevel("We have a connected server object", MessageLevel.Debug, null);
                connectionSucceeded = true;
            }
            catch (Exception ex)
            {
                connectionError = ex;
                string errorMessage = ex.Message;

                // Check for AllowTrustServerCertificate retry
                bool isCertError = errorMessage != null && (
                    errorMessage.IndexOf("certificate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    errorMessage.IndexOf("SSL", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    errorMessage.IndexOf("TLS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    errorMessage.IndexOf("trust", StringComparison.OrdinalIgnoreCase) >= 0);

                if (AllowTrustServerCertificate.IsPresent && isNewConnection && !TrustServerCertificate.IsPresent
                    && isCertError && inputObjectType == "String")
                {
                    WriteMessageAtLevel("Initial connection failed due to certificate validation. Retrying with TrustServerCertificate enabled.", MessageLevel.Verbose, null);
                    WriteMessageAtLevel(String.Format("Original error: {0}", errorMessage), MessageLevel.Debug, null);

                    try
                    {
                        RetryWithTrustServerCertificate(server);
                        WriteMessageAtLevel("Connection succeeded with TrustServerCertificate enabled", MessageLevel.Verbose, null);
                        connectionSucceeded = true;
                    }
                    catch (Exception retryEx)
                    {
                        WriteMessageAtLevel(String.Format("Retry with TrustServerCertificate also failed: {0}", retryEx.Message), MessageLevel.Debug, null);
                        connectionError = retryEx;
                    }
                }

                if (!connectionSucceeded)
                {
                    StopFunction("Failure",
                        errorRecord: new ErrorRecord(connectionError, "ConnectDbaInstance_ConnectionError", ErrorCategory.ConnectionError, instance),
                        target: instance,
                        isContinue: true,
                        category: ErrorCategory.ConnectionError);
                    TestFunctionInterrupt();
                    return false;
                }
            }

            return true;
        }

        private void RetryWithTrustServerCertificate(object server)
        {
            string script = @"
param($server)
$connString = $server.ConnectionContext.SqlConnectionObject.ConnectionString
$connString = $connString -replace 'Trust Server Certificate=False', 'Trust Server Certificate=True'
if ($connString -notmatch 'Trust Server Certificate') {
    if ($connString -match ';$') {
        $connString += 'Trust Server Certificate=True;'
    } else {
        $connString += ';Trust Server Certificate=True;'
    }
}
$server.ConnectionContext.SqlConnectionObject.ConnectionString = $connString
Write-Message -Level Debug -Message ""Retrying connection with TrustServerCertificate enabled""
$null = $server.ConnectionContext.ExecuteWithResults(""SELECT 'dbatools is opening a new connection with TrustServerCertificate'"")
";
            InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, server);
        }

        #endregion Connection Validation

        #region Post-Connection Setup

        private void AddServerProperties(object server, DbaInstanceParameter instance, bool isAzure)
        {
            // Add ComputerName if not already set
            object existingComputerName = GetPropertyValue(server, "ComputerName");
            if (existingComputerName == null || String.IsNullOrEmpty(existingComputerName.ToString()))
            {
                string computerName = ResolveComputerName(server, instance);
                AddNoteProperty(server, "ComputerName", computerName);
            }

            // Add other dbatools properties if not already set
            object existingIsAzure = GetPropertyValue(server, "IsAzure");
            if (existingIsAzure == null)
            {
                AddNoteProperty(server, "IsAzure", isAzure);
                AddNoteProperty(server, "DbaInstanceName", instance.InstanceName);
                string domainInstanceName = GetPropertyValue(server, "DomainInstanceName") as string;
                AddNoteProperty(server, "SqlInstance", domainInstanceName);
                AddNoteProperty(server, "NetPort", instance.Port);
                string trueLogin = GetTrueLogin(server);
                AddNoteProperty(server, "ConnectedAs", trueLogin);
                WriteMessageAtLevel(String.Format(
                    "We added IsAzure = '{0}', DbaInstanceName = instance.InstanceName = '{1}', SqlInstance = server.DomainInstanceName = '{2}', NetPort = instance.Port = '{3}', ConnectedAs = server.ConnectionContext.TrueLogin = '{4}'",
                    isAzure, instance.InstanceName, domainInstanceName, instance.Port, trueLogin),
                    MessageLevel.Debug, null);
            }
        }

        private string ResolveComputerName(object server, DbaInstanceParameter instance)
        {
            string computerName = null;

            // Check config for custom source
            string computerNameSource = GetConfigValue("commands.connect-dbainstance.smo.computername.source");
            if (!String.IsNullOrEmpty(computerNameSource))
            {
                WriteMessageAtLevel(String.Format("Setting ComputerName based on {0}", computerNameSource), MessageLevel.Debug, null);

                if (String.Equals(computerNameSource, "instance.ComputerName", StringComparison.OrdinalIgnoreCase))
                {
                    computerName = instance.ComputerName;
                }
                else if (String.Equals(computerNameSource, "server.ComputerNamePhysicalNetBIOS", StringComparison.OrdinalIgnoreCase))
                {
                    computerName = GetPropertyValue(server, "ComputerNamePhysicalNetBIOS") as string;
                }

                if (!String.IsNullOrEmpty(computerName))
                {
                    WriteMessageAtLevel(String.Format("ComputerName will be set to {0}", computerName), MessageLevel.Debug, null);
                }
                else
                {
                    WriteMessageAtLevel("No value found for ComputerName, so will use the default", MessageLevel.Debug, null);
                }
            }

            if (String.IsNullOrEmpty(computerName))
            {
                object engineType = GetPropertyValue(server, "DatabaseEngineType");
                string hostPlatform = GetPropertyValue(server, "HostPlatform") as string;
                string netName = GetPropertyValue(server, "NetName") as string;

                if (engineType != null && String.Equals(engineType.ToString(), "SqlAzureDatabase", StringComparison.OrdinalIgnoreCase))
                {
                    WriteMessageAtLevel("We are on Azure, so server.ComputerName will be set to instance.ComputerName", MessageLevel.Debug, null);
                    computerName = instance.ComputerName;
                }
                else if (String.Equals(hostPlatform, "Linux", StringComparison.OrdinalIgnoreCase))
                {
                    WriteMessageAtLevel("We are on Linux what is often on docker and the internal name is not useful, so server.ComputerName will be set to instance.ComputerName", MessageLevel.Debug, null);
                    computerName = instance.ComputerName;
                }
                else if (!String.IsNullOrEmpty(netName))
                {
                    WriteMessageAtLevel("We will set server.ComputerName to server.NetName", MessageLevel.Debug, null);
                    computerName = netName;
                }
                else
                {
                    WriteMessageAtLevel("We will set server.ComputerName to instance.ComputerName as server.NetName is empty", MessageLevel.Debug, null);
                    computerName = instance.ComputerName;
                }
                WriteMessageAtLevel(String.Format("ComputerName will be set to {0}", computerName), MessageLevel.Debug, null);
            }

            return computerName;
        }

        private void PostConnectionSetup(DbaInstanceParameter instance, object server, bool isAzure)
        {
            // Register the connected instance for TEPP
            string script = @"
param($instance, $server, $isAzure, $fields2000Db, $fields200xDb, $fields201xDb, $fields2000Login, $fields200xLogin, $fields201xLogin, $fieldsJob)

[Dataplat.Dbatools.TabExpansion.TabExpansionHost]::SetInstance($instance.FullSmoName.ToLowerInvariant(), $server.ConnectionContext.Copy(), ($server.ConnectionContext.FixedServerRoles -match 'SysAdmin'))

# Update cache for instance names
if ([Dataplat.Dbatools.TabExpansion.TabExpansionHost]::Cache['sqlinstance'] -notcontains $instance.FullSmoName.ToLowerInvariant()) {
    [Dataplat.Dbatools.TabExpansion.TabExpansionHost]::Cache['sqlinstance'] += $instance.FullSmoName.ToLowerInvariant()
}

# Update TEPP if enabled
if (-not [Dataplat.Dbatools.TabExpansion.TabExpansionHost]::TeppSyncDisabled) {
    $FullSmoName = $instance.FullSmoName.ToLowerInvariant()
    foreach ($scriptBlock in ([Dataplat.Dbatools.TabExpansion.TabExpansionHost]::TeppGatherScriptsFast)) {
        try {
            [ScriptBlock]::Create($scriptBlock).Invoke()
        } catch {
            if ($_.Exception.InnerException.InnerException.GetType().FullName -eq 'Microsoft.SqlServer.Management.Sdk.Sfc.InvalidVersionEnumeratorException') {
                continue
            }
            if ($ENV:APPVEYOR_BUILD_FOLDER -or ([Dataplat.Dbatools.Message.MessageHost]::DeveloperMode)) { Stop-Function -Message $_ }
            else {
                Write-Message -Level Warning -Message ""Failed TEPP Caching: $($scriptBlock.ToString() | Select-String '""(.*?)""' | ForEach-Object { $_.Matches[0].Groups[1].Value })"" -ErrorRecord $_ 3>`$null
            }
        }
    }
}

# Set default init fields for performance
$loadedSmoVersion = [AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.Fullname -like 'Microsoft.SqlServer.SMO,*' }
if ($loadedSmoVersion) {
    $loadedSmoVersion = $loadedSmoVersion | ForEach-Object {
        if ($_.Location -match '__') {
            ((Split-Path (Split-Path $_.Location) -Leaf) -split '__')[0]
        } else {
            ((Get-ChildItem -Path $_.Location).VersionInfo.ProductVersion)
        }
    }
}

if ($loadedSmoVersion -ge 11 -and -not $isAzure) {
    try {
        Write-Message -Level Debug -Message ""SetDefaultInitFields will be used""
        $initFieldsDb = New-Object System.Collections.Specialized.StringCollection
        $initFieldsLogin = New-Object System.Collections.Specialized.StringCollection
        $initFieldsJob = New-Object System.Collections.Specialized.StringCollection

        if ($server.VersionMajor -eq 8) {
            [void]$initFieldsDb.AddRange($fields2000Db)
            [void]$initFieldsLogin.AddRange($fields2000Login)
        } elseif ($server.VersionMajor -eq 9 -or $server.VersionMajor -eq 10) {
            [void]$initFieldsDb.AddRange($fields200xDb)
            [void]$initFieldsLogin.AddRange($fields200xLogin)
        } elseif ($server.VersionMajor -ge 16) {
            # 2022 and above - exclude ActiveConnections due to performance issue #9282
            $fields = New-Object System.Collections.Specialized.StringCollection
            foreach ($field in $fields201xDb) {
                if ($field -ne 'ActiveConnections') {
                    [void]$fields.Add($field)
                }
            }
            [void]$initFieldsDb.AddRange($fields)
            [void]$initFieldsLogin.AddRange($fields201xLogin)
        } else {
            [void]$initFieldsDb.AddRange($fields201xDb)
            [void]$initFieldsLogin.AddRange($fields201xLogin)
        }
        $server.SetDefaultInitFields([Microsoft.SqlServer.Management.Smo.Database], $initFieldsDb)
        $server.SetDefaultInitFields([Microsoft.SqlServer.Management.Smo.Login], $initFieldsLogin)
        [void]$initFieldsJob.AddRange($fieldsJob)
        $server.SetDefaultInitFields([Microsoft.SqlServer.Management.Smo.Agent.Job], $initFieldsJob)
    } catch {
        Write-Message -Level Debug -Message ""SetDefaultInitFields failed with $_""
    }
}
";
            try
            {
                InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null,
                    instance, server, isAzure,
                    Fields2000Db, Fields200xDb, Fields201xDb,
                    Fields2000Login, Fields200xLogin, Fields201xLogin,
                    FieldsJob);
            }
            catch (Exception ex)
            {
                WriteMessageAtLevel(String.Format("Post-connection setup failed: {0}", ex.Message), MessageLevel.Debug, null);
            }
        }

        #endregion Post-Connection Setup

        #region Utility Methods

        /// <summary>
        /// Resolves default values from dbatools configuration system.
        /// </summary>
        private void ResolveDefaultsFromConfig()
        {
            // Only set defaults for unbound parameters
            if (!TestBound("Database"))
            {
                Database = GetConfigValue("sql.connection.database");
            }
            if (!TestBound("ClientName"))
            {
                ClientName = GetConfigValue("sql.connection.clientname");
            }
            if (!TestBound("ConnectTimeout"))
            {
                ConnectTimeout = ConnectionHost.SqlConnectionTimeout;
            }
            if (!TestBound("EncryptConnection"))
            {
                string encryptVal = GetConfigValue("sql.connection.encrypt");
                if (!String.IsNullOrEmpty(encryptVal))
                {
                    bool encrypt;
                    if (Boolean.TryParse(encryptVal, out encrypt) && encrypt)
                    {
                        EncryptConnection = true;
                    }
                }
            }
            if (!TestBound("MultiSubnetFailover"))
            {
                string msfVal = GetConfigValue("sql.connection.multisubnetfailover");
                if (!String.IsNullOrEmpty(msfVal))
                {
                    bool msf;
                    if (Boolean.TryParse(msfVal, out msf) && msf)
                    {
                        MultiSubnetFailover = true;
                    }
                }
            }
            if (!TestBound("NetworkProtocol"))
            {
                NetworkProtocol = GetConfigValue("sql.connection.protocol");
            }
            if (!TestBound("PacketSize"))
            {
                string psVal = GetConfigValue("sql.connection.packetsize");
                if (!String.IsNullOrEmpty(psVal))
                {
                    int ps;
                    if (Int32.TryParse(psVal, out ps))
                    {
                        PacketSize = ps;
                    }
                }
            }
            if (!TestBound("StatementTimeout"))
            {
                string stVal = GetConfigValue("sql.execution.timeout");
                if (!String.IsNullOrEmpty(stVal))
                {
                    int st;
                    if (Int32.TryParse(stVal, out st))
                    {
                        StatementTimeout = st;
                    }
                }
            }
            if (!TestBound("TrustServerCertificate"))
            {
                string tcVal = GetConfigValue("sql.connection.trustcert");
                if (!String.IsNullOrEmpty(tcVal))
                {
                    bool tc;
                    if (Boolean.TryParse(tcVal, out tc) && tc)
                    {
                        TrustServerCertificate = true;
                    }
                }
            }
            if (!TestBound("AllowTrustServerCertificate"))
            {
                string atcVal = GetConfigValue("sql.connection.allowtrustcert");
                if (!String.IsNullOrEmpty(atcVal))
                {
                    bool atc;
                    if (Boolean.TryParse(atcVal, out atc) && atc)
                    {
                        AllowTrustServerCertificate = true;
                    }
                }
            }
            if (!TestBound("Tenant"))
            {
                Tenant = GetConfigValue("azure.tenantid");
            }
            if (!TestBound("AzureDomain"))
            {
                AzureDomain = "database.windows.net";
            }
        }

        /// <summary>
        /// Gets a dbatools configuration value by invoking Get-DbatoolsConfigValue.
        /// </summary>
        internal string GetConfigValue(string fullName)
        {
            try
            {
                string script = "param($fn) Get-DbatoolsConfigValue -FullName $fn";
                Collection<PSObject> results = InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, fullName);
                if (results != null && results.Count > 0 && results[0] != null)
                {
                    object val = results[0].BaseObject;
                    if (val != null)
                        return val.ToString();
                }
            }
            catch
            {
                // Config may not be initialized yet
            }
            return null;
        }

        /// <summary>
        /// Gets a property value from an object via reflection.
        /// </summary>
        internal static object GetPropertyValue(object obj, string propertyName)
        {
            if (obj == null) return null;

            // Unwrap PSObject
            if (obj is PSObject psObj && psObj.BaseObject != null)
            {
                // First try PSObject properties (includes NoteProperties)
                try
                {
                    PSPropertyInfo psProp = psObj.Properties[propertyName];
                    if (psProp != null) return psProp.Value;
                }
                catch { }

                obj = psObj.BaseObject;
            }

            try
            {
                var prop = obj.GetType().GetProperty(propertyName);
                if (prop != null) return prop.GetValue(obj);
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Gets a property from the ConnectionContext of a server object.
        /// </summary>
        private object GetConnectionContextProperty(object server, string propertyName)
        {
            object connCtx = GetPropertyValue(server, "ConnectionContext");
            if (connCtx == null) return null;
            return GetPropertyValue(connCtx, propertyName);
        }

        private string GetCurrentDatabase(object server)
        {
            return GetConnectionContextProperty(server, "CurrentDatabase") as string;
        }

        private string GetConnectAsUserName(object server)
        {
            return GetConnectionContextProperty(server, "ConnectAsUserName") as string;
        }

        private string GetTrueLogin(object server)
        {
            return GetConnectionContextProperty(server, "TrueLogin") as string;
        }

        private void ConnectContext(object server)
        {
            string script = "param($s) $s.ConnectionContext.Connect()";
            try
            {
                InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, server);
            }
            catch { }
        }

        private void DisconnectServer(object server)
        {
            string script = "param($s) $s.ConnectionContext.Disconnect()";
            try
            {
                InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, server);
            }
            catch { }
        }

        private void InvokeExecuteWithResults(object server, string sql)
        {
            string script = "param($s, $q) $null = $s.ConnectionContext.ExecuteWithResults($q)";
            Collection<PSObject> results = InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, server, sql);
        }

        private object GetSqlConnectionObject(object server)
        {
            return GetConnectionContextProperty(server, "SqlConnectionObject");
        }

        private string GetConnectionStringFromServer(object server)
        {
            object connCtx = GetPropertyValue(server, "ConnectionContext");
            if (connCtx == null) return null;
            return GetPropertyValue(connCtx, "ConnectionString") as string;
        }

        private void LogMaskedConnectionString(object server)
        {
            string script = @"
param($server)
try {
    $connStr = $server.ConnectionContext.ConnectionString
    $csb = New-Object Microsoft.Data.SqlClient.SqlConnectionStringBuilder $connStr
    if ($csb.Password) { $csb.Password = '********' }
    Write-Message -Level Debug -Message ""The masked server.ConnectionContext.ConnectionString is $($csb.ConnectionString)""
} catch {
    Write-Message -Level Debug -Message ""Failed to mask the connection string""
}
";
            try
            {
                InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, server);
            }
            catch { }
        }

        private void AddNoteProperty(object server, string name, object value)
        {
            string script = "param($s, $n, $v) Add-Member -InputObject $s -NotePropertyName $n -NotePropertyValue $v -Force";
            try
            {
                InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, server, name, value);
            }
            catch { }
        }

        private void AddConnectionHashValue(string key, object value)
        {
            string script = @"
param($key, $value)
try {
    if ($value.ConnectionContext.NonPooledConnection -or $value.NonPooledConnection) {
        if (-not [Dataplat.Dbatools.Connection.ConnectionHost]::ConnectionHash[$key]) {
            [Dataplat.Dbatools.Connection.ConnectionHost]::ConnectionHash[$key] = @()
        }
        [Dataplat.Dbatools.Connection.ConnectionHost]::ConnectionHash[$key] += @($value)
    } else {
        [Dataplat.Dbatools.Connection.ConnectionHost]::ConnectionHash[$key] = @($value)
    }
} catch {
    # Fallback: just set it
    [Dataplat.Dbatools.Connection.ConnectionHost]::ConnectionHash[$key] = @($value)
}
";
            try
            {
                InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, key, value);
            }
            catch { }
        }

        private bool IsRunningOnCore()
        {
            try
            {
                string script = "$PSVersionTable.PSEdition -eq 'Core'";
                Collection<PSObject> results = InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, new object[0]);
                if (results != null && results.Count > 0 && results[0] != null)
                {
                    return results[0].BaseObject is bool val && val;
                }
            }
            catch { }
            return false;
        }

        private PSObject InvokeNewDbaAzAccessToken()
        {
            string script = @"
param($tenant, $cred)
$token = (New-DbaAzAccessToken -Type RenewableServicePrincipal -Subtype AzureSqlDb -Tenant $tenant -Credential $cred -ErrorAction Stop).GetAccessToken()
$token
";
            Collection<PSObject> results = InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, Tenant, SqlCredential);
            if (results != null && results.Count > 0)
                return results[0];
            return null;
        }

        private DbaInstanceParameter RewriteForServicePrincipal(DbaInstanceParameter instance)
        {
            string azureServer = instance.InputObject != null ? instance.InputObject.ToString() : instance.ToString();
            string connStr;
            if (!String.IsNullOrEmpty(Database))
            {
                connStr = String.Format("Server={0}; Authentication=Active Directory Service Principal; Database={1}; User Id={2}; Password={3}",
                    azureServer, Database, SqlCredential.UserName, SqlCredential.GetNetworkCredential().Password);
            }
            else
            {
                connStr = String.Format("Server={0}; Authentication=Active Directory Service Principal; User Id={1}; Password={2}",
                    azureServer, SqlCredential.UserName, SqlCredential.GetNetworkCredential().Password);
            }
            return new DbaInstanceParameter(connStr);
        }

        /// <summary>
        /// Converts connection string synonyms for Microsoft.Data.SqlClient compatibility.
        /// </summary>
        internal static string ConvertConnectionString(string connectionString)
        {
            if (String.IsNullOrEmpty(connectionString))
                return connectionString;

            connectionString = connectionString.Replace("Application Intent", "ApplicationIntent");
            connectionString = connectionString.Replace("Connect Retry Count", "ConnectRetryCount");
            connectionString = connectionString.Replace("Connect Retry Interval", "ConnectRetryInterval");
            connectionString = connectionString.Replace("Pool Blocking Period", "PoolBlockingPeriod");
            connectionString = connectionString.Replace("Multiple Active Result Sets", "MultipleActiveResultSets");
            connectionString = connectionString.Replace("Multiple Subnet Failover", "MultiSubnetFailover");
            connectionString = connectionString.Replace("Trust Server Certificate", "TrustServerCertificate");
            return connectionString;
        }

        #endregion Utility Methods
    }
}
