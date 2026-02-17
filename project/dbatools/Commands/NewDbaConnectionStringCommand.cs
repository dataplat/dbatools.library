using System;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Configuration;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Creates connection strings for SQL Server instances using PowerShell-friendly parameters.
    /// Handles authentication methods (Windows, SQL Server, Azure AD), encryption settings,
    /// timeout values, and Azure SQL Database specifics. Supports both legacy System.Data.SqlClient
    /// and modern Microsoft.Data.SqlClient providers.
    /// </summary>
    [OutputType(typeof(string))]
    [Cmdlet("New", "DbaConnectionString", SupportsShouldProcess = true)]
    public class NewDbaConnectionStringCommand : DbaBaseCmdlet
    {
        /// <summary>
        /// The target SQL Server instance or instances.
        /// </summary>
        [Parameter(Mandatory = true, ValueFromPipeline = true)]
        [Alias("ServerInstance", "SqlServer", "Server", "DataSource")]
        public DbaInstanceParameter[] SqlInstance { get; set; }

        /// <summary>
        /// Login to the target instance using alternative credentials.
        /// </summary>
        [Parameter()]
        [Alias("SqlCredential")]
        public PSCredential Credential { get; set; }

        /// <summary>
        /// Specifies that Azure Active Directory access token authentication should be used.
        /// </summary>
        [Parameter()]
        public string AccessToken { get; set; }

        /// <summary>
        /// Specifies whether the application workload is read-only or read-write.
        /// </summary>
        [Parameter()]
        [ValidateSet("ReadOnly", "ReadWrite")]
        public string ApplicationIntent { get; set; }

        /// <summary>
        /// Sets the batch separator for SQL commands.
        /// </summary>
        [Parameter()]
        public string BatchSeparator { get; set; }

        /// <summary>
        /// Sets the application name that appears in SQL Server monitoring tools.
        /// </summary>
        [Parameter()]
        public string ClientName { get; set; }

        /// <summary>
        /// Sets the number of seconds to wait while attempting to establish a connection.
        /// </summary>
        [Parameter()]
        public int ConnectTimeout { get; set; }

        /// <summary>
        /// Specifies the initial database to connect to.
        /// </summary>
        [Parameter()]
        public string Database { get; set; }

        /// <summary>
        /// Forces SSL/TLS encryption for the connection.
        /// </summary>
        [Parameter()]
        public SwitchParameter EncryptConnection { get; set; }

        /// <summary>
        /// Specifies the failover partner server name for database mirroring configurations.
        /// </summary>
        [Parameter()]
        public string FailoverPartner { get; set; }

        /// <summary>
        /// Enables Azure Active Directory Universal Authentication with MFA support.
        /// </summary>
        [Parameter()]
        public SwitchParameter IsActiveDirectoryUniversalAuth { get; set; }

        /// <summary>
        /// Sets the number of seconds to wait for locks. Not supported in connection strings.
        /// </summary>
        [Parameter()]
        public int LockTimeout { get; set; }

        /// <summary>
        /// Sets the maximum number of connections allowed in the connection pool.
        /// </summary>
        [Parameter()]
        public int MaxPoolSize { get; set; }

        /// <summary>
        /// Sets the minimum number of connections maintained in the connection pool.
        /// </summary>
        [Parameter()]
        public int MinPoolSize { get; set; }

        /// <summary>
        /// Enables Multiple Active Result Sets (MARS).
        /// </summary>
        [Parameter()]
        public SwitchParameter MultipleActiveResultSets { get; set; }

        /// <summary>
        /// Enables faster failover detection for multi-subnet availability groups.
        /// </summary>
        [Parameter()]
        public SwitchParameter MultiSubnetFailover { get; set; }

        /// <summary>
        /// Forces a specific network protocol for the connection.
        /// </summary>
        [Parameter()]
        [ValidateSet("TcpIp", "NamedPipes", "Multiprotocol", "AppleTalk", "BanyanVines", "Via", "SharedMemory", "NWLinkIpxSpx")]
        public string NetworkProtocol { get; set; }

        /// <summary>
        /// Disables connection pooling for this connection.
        /// </summary>
        [Parameter()]
        public SwitchParameter NonPooledConnection { get; set; }

        /// <summary>
        /// Sets the network packet size in bytes.
        /// </summary>
        [Parameter()]
        public int PacketSize { get; set; }

        /// <summary>
        /// Sets the maximum lifetime in seconds for pooled connections.
        /// </summary>
        [Parameter()]
        public int PooledConnectionLifetime { get; set; }

        /// <summary>
        /// Controls how SQL commands are processed. Not supported in connection strings.
        /// </summary>
        [Parameter()]
        [ValidateSet("CaptureSql", "ExecuteAndCaptureSql", "ExecuteSql")]
        public string SqlExecutionModes { get; set; }

        /// <summary>
        /// Sets the number of seconds before SQL commands timeout. Not supported in connection strings.
        /// </summary>
        [Parameter()]
        public int StatementTimeout { get; set; }

        /// <summary>
        /// Bypasses SSL certificate validation when EncryptConnection is enabled.
        /// </summary>
        [Parameter()]
        public SwitchParameter TrustServerCertificate { get; set; }

        /// <summary>
        /// Sets the workstation identifier that appears in SQL Server system views.
        /// </summary>
        [Parameter()]
        public string WorkstationId { get; set; }

        /// <summary>
        /// Forces the use of the older System.Data.SqlClient provider.
        /// </summary>
        [Parameter()]
        public SwitchParameter Legacy { get; set; }

        /// <summary>
        /// Adds custom connection string parameters to the generated connection string.
        /// </summary>
        [Parameter()]
        public string AppendConnectionString { get; set; }

        /// <summary>
        /// The Azure domain suffix used to detect Azure SQL endpoints.
        /// </summary>
        private const string AzureDomain = "database.windows.net";

        #region Script Strings
        private static readonly string ServerObjectScript = @"
param($instance, $legacy, $database, $isAzure)
if ($legacy) {
    $converted = $instance.InputObject.ConnectionContext.ConnectionString | Convert-ConnectionString
    $builder = New-Object -TypeName System.Data.SqlClient.SqlConnectionStringBuilder -ArgumentList $converted
} else {
    $builder = New-Object -TypeName Microsoft.Data.SqlClient.SqlConnectionStringBuilder -ArgumentList $instance.InputObject.ConnectionContext.ConnectionString
}
if ($isAzure -and $database) {
    $builder['Initial Catalog'] = $database
}
$builder.ConnectionString
";

        private static readonly string BuildScript = @"
param($legacy, $fullSmoName, $applicationIntent, $clientName, $connectTimeout, $database,
      $encryptConnection, $encryptBound, $failoverPartner, $maxPoolSize, $minPoolSize,
      $multipleActiveResultSets, $multiSubnetFailover, $nonPooledConnection,
      $packetSize, $pooledConnectionLifetime, $trustServerCertificate,
      $workstationId, $isLocalDb, $isAzure, $sqlCredential, $username,
      $password, $isAdUser, $connectTimeoutBound, $appendConnectionString)

if ($legacy) {
    $connStringBuilder = New-Object -TypeName System.Data.SqlClient.SqlConnectionStringBuilder
} else {
    $connStringBuilder = New-Object -TypeName Microsoft.Data.SqlClient.SqlConnectionStringBuilder
}

$connStringBuilder['Data Source'] = $fullSmoName
if ($applicationIntent) { $connStringBuilder['ApplicationIntent'] = $applicationIntent }
if ($clientName) { $connStringBuilder['Application Name'] = $clientName }
if ($connectTimeout) { $connStringBuilder['Connect Timeout'] = $connectTimeout }
if ($database) { $connStringBuilder['Initial Catalog'] = $database }

if (-not $isLocalDb) {
    if ($encryptConnection) {
        $connStringBuilder['Encrypt'] = 'Mandatory'
    }
    if (-not $encryptConnection -and $encryptBound) {
        $connStringBuilder['Encrypt'] = 'False'
    }
}

if ($failoverPartner) { $connStringBuilder['Failover Partner'] = $failoverPartner }
if ($maxPoolSize) { $connStringBuilder['Max Pool Size'] = $maxPoolSize }
if ($minPoolSize) { $connStringBuilder['Min Pool Size'] = $minPoolSize }
if ($multipleActiveResultSets) { $connStringBuilder['MultipleActiveResultSets'] = $true } else { $connStringBuilder['MultipleActiveResultSets'] = $false }
if ($multiSubnetFailover) { $connStringBuilder['MultiSubnetFailover'] = $true }
if ($nonPooledConnection) { $connStringBuilder['Pooling'] = $false }
if ($packetSize) { $connStringBuilder['Packet Size'] = $packetSize }
if ($pooledConnectionLifetime) { $connStringBuilder['Load Balance Timeout'] = $pooledConnectionLifetime }
if ($trustServerCertificate) { $connStringBuilder['TrustServerCertificate'] = $true } else { $connStringBuilder['TrustServerCertificate'] = $false }
if ($workstationId) { $connStringBuilder['Workstation Id'] = $workstationId }

if ($sqlCredential) {
    $connStringBuilder['User ID'] = $username
    $connStringBuilder['Password'] = $password
    if ($isAzure -and $isAdUser) {
        $connStringBuilder['Authentication'] = 'Active Directory Password'
    }
} else {
    if ($isAzure) {
        $connStringBuilder['Authentication'] = 'Active Directory Integrated'
    } else {
        $connStringBuilder['Integrated Security'] = $true
    }
}

# Azure defaults
if ($isAzure) {
    if (-not $connectTimeoutBound) {
        $connStringBuilder['Connect Timeout'] = 30
    }
    $connStringBuilder['Encrypt'] = $true
}

if ($legacy) {
    $connstring = $connStringBuilder.ConnectionString
} else {
    $connstring = $connStringBuilder.ToString()
}

if ($appendConnectionString) {
    $connstring = ""$connstring;$appendConnectionString""
}

$connstring
";

        private static readonly string LegacyScript = @"
param($instance, $credential, $accessToken, $applicationIntent, $batchSeparator,
      $clientName, $connectTimeout, $database, $encryptConnection,
      $failoverPartner, $isActiveDirectoryUniversalAuth, $lockTimeout,
      $maxPoolSize, $minPoolSize, $multipleActiveResultSets,
      $multiSubnetFailover, $networkProtocol, $nonPooledConnection,
      $packetSize, $pooledConnectionLifetime, $statementTimeout,
      $sqlExecutionModes, $trustServerCertificate, $workstationId,
      $appendConnectionString,
      $connectTimeoutBound, $clientNameBound, $credBound)

$isAzure = $false
if ($instance.ComputerName -match 'database\.windows\.net' -or ($instance.InputObject -and $instance.InputObject.ComputerName -match 'database\.windows\.net')) {
    if ($instance.InputObject -and $instance.InputObject.GetType() -eq [Microsoft.SqlServer.Management.Smo.Server]) {
        $connstring = $instance.InputObject.ConnectionContext.ConnectionString
        if ($database) {
            $olddb = $connstring -split ';' | Where-Object { $_.StartsWith('Initial Catalog') }
            $newdb = ""Initial Catalog=$database""
            if ($olddb) {
                $connstring = $connstring.Replace(""$olddb"", ""$newdb"")
            } else {
                $connstring = ""$connstring;$newdb;""
            }
        }
        return $connstring
    } else {
        $isAzure = $true
        if (-not $connectTimeoutBound) { $connectTimeout = 30 }
        if (-not $clientNameBound) { $clientName = 'dbatools PowerShell module - dbatools.io' }
        $encryptConnection = $true
        $instance = [DbaInstanceParameter]""tcp:$($instance.ComputerName),$($instance.Port)""
    }
}

if ($instance.GetType() -eq [Microsoft.SqlServer.Management.Smo.Server]) {
    return $instance.ConnectionContext.ConnectionString
} else {
    $guid = [System.Guid]::NewGuid()
    $server = New-Object Microsoft.SqlServer.Management.Smo.Server $guid

    if ($appendConnectionString) {
        $connstring = $server.ConnectionContext.ConnectionString
        $server.ConnectionContext.ConnectionString = ""$connstring;$appendConnectionString""
        return $server.ConnectionContext.ConnectionString
    }

    $server.ConnectionContext.ApplicationName = $clientName
    if ($batchSeparator) { $server.ConnectionContext.BatchSeparator = $batchSeparator }
    if ($connectTimeout) { $server.ConnectionContext.ConnectTimeout = $connectTimeout }
    if ($database) { $server.ConnectionContext.DatabaseName = $database }
    if ($encryptConnection) { $server.ConnectionContext.EncryptConnection = $true }
    if ($isActiveDirectoryUniversalAuth) { $server.ConnectionContext.IsActiveDirectoryUniversalAuth = $true }
    if ($lockTimeout) { $server.ConnectionContext.LockTimeout = $lockTimeout }
    if ($maxPoolSize) { $server.ConnectionContext.MaxPoolSize = $maxPoolSize }
    if ($minPoolSize) { $server.ConnectionContext.MinPoolSize = $minPoolSize }
    if ($multipleActiveResultSets) { $server.ConnectionContext.MultipleActiveResultSets = $true }
    if ($networkProtocol) { $server.ConnectionContext.NetworkProtocol = $networkProtocol }
    if ($nonPooledConnection) { $server.ConnectionContext.NonPooledConnection = $true }
    if ($packetSize) { $server.ConnectionContext.PacketSize = $packetSize }
    if ($pooledConnectionLifetime) { $server.ConnectionContext.PooledConnectionLifetime = $pooledConnectionLifetime }
    if ($statementTimeout) { $server.ConnectionContext.StatementTimeout = $statementTimeout }
    if ($sqlExecutionModes) { $server.ConnectionContext.SqlExecutionModes = $sqlExecutionModes }
    if ($trustServerCertificate) { $server.ConnectionContext.TrustServerCertificate = $true }
    if ($workstationId) { $server.ConnectionContext.WorkstationId = $workstationId }

    if ($null -ne $credential -and $null -ne $credential.username) {
        $username = ($credential.username).TrimStart('\')
        if ($username -like '*\*') {
            $username = $username.Split('\')[1]
            $server.ConnectionContext.LoginSecure = $true
            $server.ConnectionContext.ConnectAsUser = $true
            $server.ConnectionContext.ConnectAsUserName = $username
            $server.ConnectionContext.ConnectAsUserPassword = $credential.GetNetworkCredential().Password
        } else {
            $server.ConnectionContext.LoginSecure = $false
            $server.ConnectionContext.set_Login($username)
            $server.ConnectionContext.set_SecurePassword($credential.Password)
        }
    }

    $connstring = $server.ConnectionContext.ConnectionString
    if ($multiSubnetFailover) { $connstring = ""$connstring;MultiSubnetFailover=True"" }
    if ($failoverPartner) { $connstring = ""$connstring;Failover Partner=$failoverPartner"" }
    if ($applicationIntent) { $connstring = ""$connstring;ApplicationIntent=$applicationIntent;"" }

    if ($isAzure) {
        if ($credBound) {
            if ($credential.UserName -like '*\*' -or $credential.UserName -like '*@*') {
                $connstring = ""$connstring;Authentication=""""Active Directory Password""""""
            } else {
                $username = ($credential.username).TrimStart('\')
                $server.ConnectionContext.LoginSecure = $false
                $server.ConnectionContext.set_Login($username)
                $server.ConnectionContext.set_SecurePassword($credential.Password)
            }
        } else {
            $connstring = $connstring.Replace('Integrated Security=True;', 'Persist Security Info=True;')
            if (-not $accessToken) {
                $connstring = ""$connstring;Authentication=""""Active Directory Integrated""""""
            }
        }
    }

    if ($connstring -ne $server.ConnectionContext.ConnectionString) {
        $server.ConnectionContext.ConnectionString = $connstring
    }

    ($server.ConnectionContext.ConnectionString).Replace($guid, $instance)
}
";
        #endregion

        /// <summary>
        /// Cached ScriptBlock objects compiled once per invocation.
        /// </summary>
        private ScriptBlock _serverObjectScriptBlock;
        private ScriptBlock _buildScriptBlock;
        private ScriptBlock _legacyScriptBlock;

        /// <summary>
        /// Tests whether the given instance is an Azure SQL endpoint.
        /// </summary>
        internal static bool TestAzure(DbaInstanceParameter instance)
        {
            if (instance == null)
                return false;
            string computerName = instance.ComputerName;
            if (!String.IsNullOrEmpty(computerName) &&
                computerName.IndexOf(AzureDomain, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Transforms a domain\user credential to user@domain format for Azure AD.
        /// </summary>
        internal static string TransformUsername(string rawUsername)
        {
            if (String.IsNullOrEmpty(rawUsername))
                return rawUsername;

            string username = rawUsername.TrimStart('\\');
            int backslashIdx = username.IndexOf('\\');
            if (backslashIdx >= 0)
            {
                string domain = username.Substring(0, backslashIdx);
                string login = username.Substring(backslashIdx + 1);
                return String.Format("{0}@{1}", login, domain);
            }
            return username;
        }

        /// <summary>
        /// Resolves default parameter values from configuration before processing.
        /// Also pre-compiles ScriptBlock objects for reuse across pipeline items.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            // Pre-compile script blocks once per invocation
            _serverObjectScriptBlock = ScriptBlock.Create(ServerObjectScript);
            _buildScriptBlock = ScriptBlock.Create(BuildScript);
            _legacyScriptBlock = ScriptBlock.Create(LegacyScript);

            // Apply defaults from dbatools configuration for unbound parameters
            if (!TestBound("Database"))
            {
                Database = GetConfigValue("sql.connection.database") as string;
            }
            if (!TestBound("ClientName"))
            {
                object configClientName = GetConfigValue("sql.connection.clientname");
                ClientName = configClientName != null ? configClientName.ToString() : "dbatools PowerShell module - dbatools.io";
            }
            if (!TestBound("ConnectTimeout"))
            {
                ConnectTimeout = ConnectionHost.SqlConnectionTimeout;
            }
            if (!TestBound("NetworkProtocol"))
            {
                object np = GetConfigValue("sql.connection.protocol");
                if (np != null)
                {
                    string npStr = np.ToString();
                    if (!String.IsNullOrEmpty(npStr))
                    {
                        NetworkProtocol = npStr;
                    }
                }
            }
            if (!TestBound("PacketSize"))
            {
                object configPacketSize = GetConfigValue("sql.connection.packetsize");
                if (configPacketSize != null)
                {
                    int ps;
                    if (configPacketSize is int)
                    {
                        ps = (int)configPacketSize;
                    }
                    else if (Int32.TryParse(configPacketSize.ToString(), out ps))
                    {
                        // parsed successfully
                    }
                    else
                    {
                        ps = 0;
                    }
                    if (ps > 0)
                    {
                        PacketSize = ps;
                    }
                }
            }
            if (!TestBound("EncryptConnection"))
            {
                object configEncrypt = GetConfigValue("sql.connection.encrypt");
                if (configEncrypt is bool)
                {
                    EncryptConnection = (bool)configEncrypt;
                }
                else if (configEncrypt is SwitchParameter)
                {
                    EncryptConnection = ((SwitchParameter)configEncrypt).ToBool();
                }
            }
            if (!TestBound("TrustServerCertificate"))
            {
                object configTrustCert = GetConfigValue("sql.connection.trustcert");
                if (configTrustCert is bool)
                {
                    TrustServerCertificate = (bool)configTrustCert;
                }
                else if (configTrustCert is SwitchParameter)
                {
                    TrustServerCertificate = ((SwitchParameter)configTrustCert).ToBool();
                }
            }
        }

        /// <summary>
        /// Processes each SQL instance to build and output connection strings.
        /// </summary>
        protected override void ProcessRecord()
        {
            foreach (var instance in SqlInstance)
            {
                try
                {
                    // Check if we should use the legacy code path:
                    // -Legacy switch takes priority, then check the config key
                    bool useLegacy = Legacy.ToBool();
                    if (!useLegacy)
                    {
                        object legacyConfig = GetConfigValue("sql.connection.legacy");
                        if (legacyConfig is bool && (bool)legacyConfig)
                        {
                            useLegacy = true;
                        }
                    }

                    if (!useLegacy)
                    {
                        ProcessNewCodePath(instance);
                    }
                    else
                    {
                        ProcessLegacyCodePath(instance);
                    }
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("Failed to create connection string for {0}", instance),
                        exception: ex,
                        target: instance,
                        isContinue: true);
                    TestFunctionInterrupt();
                    continue;
                }
            }
        }

        /// <summary>
        /// The new (default) code path using SqlConnectionStringBuilder directly.
        /// </summary>
        private void ProcessNewCodePath(DbaInstanceParameter instance)
        {
            WriteMessageAtLevel(
                String.Format("We have to build a connect string, using these parameters: {0}",
                    String.Join(", ", MyInvocation.BoundParameters.Keys)),
                MessageLevel.Debug,
                null);

            // Warn about unsupported parameters
            if (TestBound("LockTimeout"))
            {
                WriteMessageAtLevel(
                    "Parameter LockTimeout not supported, because it is not part of a connection string.",
                    MessageLevel.Warning,
                    null);
            }
            if (TestBound("StatementTimeout"))
            {
                WriteMessageAtLevel(
                    "Parameter StatementTimeout not supported, because it is not part of a connection string.",
                    MessageLevel.Warning,
                    null);
            }
            if (TestBound("SqlExecutionModes"))
            {
                WriteMessageAtLevel(
                    "Parameter SqlExecutionModes not supported, because it is not part of a connection string.",
                    MessageLevel.Warning,
                    null);
            }

            if (!ShouldProcess(instance.ToString(), "Making a new Connection String"))
                return;

            // If a Server SMO object was passed in via pipeline, extract its connection string
            if (instance.Type == DbaInstanceInputType.Server)
            {
                WriteMessageAtLevel(
                    String.Format("server object passed in, connection string is: {0}", instance),
                    MessageLevel.Debug,
                    null);

                ProcessServerObject(instance);
                return;
            }

            // Build a new connection string from parameters
            BuildConnectionString(instance);
        }

        /// <summary>
        /// Extracts and optionally modifies a connection string from a Server SMO object.
        /// </summary>
        private void ProcessServerObject(DbaInstanceParameter instance)
        {
            string existingConnString = null;
            try
            {
                bool isAzure = TestAzure(instance);
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false,
                    _serverObjectScriptBlock,
                    null,
                    instance, Legacy.ToBool(), Database, isAzure);

                if (results != null && results.Count > 0 && results[0] != null)
                {
                    existingConnString = results[0].BaseObject as string;
                }
            }
            catch (Exception ex)
            {
                StopFunction(
                    String.Format("Failed to extract connection string from Server object for {0}", instance),
                    exception: ex,
                    target: instance,
                    isContinue: true);
                TestFunctionInterrupt();
                return;
            }

            if (!String.IsNullOrEmpty(existingConnString))
            {
                WriteObject(existingConnString);
            }
        }

        /// <summary>
        /// Builds a new connection string from individual parameters.
        /// </summary>
        private void BuildConnectionString(DbaInstanceParameter instance)
        {
            bool isLocalDb = instance.ToString().IndexOf("localdb", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isAzure = TestAzure(instance);
            bool hasCred = Credential != null;
            string username = null;
            string password = null;
            bool isAdUser = false;

            if (hasCred)
            {
                username = TransformUsername(Credential.UserName);
                // Password must be extracted as plain text for the connection string builder
                password = Credential.GetNetworkCredential().Password;
                isAdUser = username != null && username.IndexOf('@') >= 0;

                WriteMessageAtLevel("We have a SqlCredential", MessageLevel.Debug, null);
                if (isAzure && isAdUser)
                {
                    WriteMessageAtLevel(
                        "We connect to Azure with Azure AD account, so adding Authentication=Active Directory Password",
                        MessageLevel.Debug,
                        null);
                }
            }
            else
            {
                WriteMessageAtLevel("We don't have a SqlCredential", MessageLevel.Debug, null);
                if (isAzure)
                {
                    WriteMessageAtLevel(
                        "We connect to Azure, so adding Authentication=Active Directory Integrated",
                        MessageLevel.Debug,
                        null);
                }
                else
                {
                    WriteMessageAtLevel(
                        "We don't connect to Azure, so setting Integrated Security=True",
                        MessageLevel.Debug,
                        null);
                }
            }

            if (isLocalDb)
            {
                WriteMessageAtLevel(
                    "localdb detected, skipping unsupported keyword 'Encryption'",
                    MessageLevel.Verbose,
                    null);
            }

            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false,
                    _buildScriptBlock,
                    null,
                    Legacy.ToBool(),
                    instance.FullSmoName,
                    ApplicationIntent,
                    ClientName,
                    ConnectTimeout,
                    Database,
                    EncryptConnection.ToBool(),
                    TestBound("EncryptConnection"),
                    FailoverPartner,
                    MaxPoolSize,
                    MinPoolSize,
                    MultipleActiveResultSets.ToBool(),
                    MultiSubnetFailover.ToBool(),
                    NonPooledConnection.ToBool(),
                    PacketSize,
                    PooledConnectionLifetime,
                    TrustServerCertificate.ToBool(),
                    WorkstationId,
                    isLocalDb,
                    isAzure,
                    hasCred,
                    username,
                    password,
                    isAdUser,
                    TestBound("ConnectTimeout"),
                    AppendConnectionString);

                if (results != null && results.Count > 0 && results[0] != null)
                {
                    WriteObject(results[0].BaseObject);
                }
            }
            catch (Exception ex)
            {
                StopFunction(
                    String.Format("Failed to build connection string for {0}", instance),
                    exception: ex,
                    target: instance,
                    isContinue: true);
                TestFunctionInterrupt();
                return;
            }
        }

        /// <summary>
        /// The legacy code path using SMO ServerConnection objects.
        /// </summary>
        private void ProcessLegacyCodePath(DbaInstanceParameter instance)
        {
            WriteMessageAtLevel("sql.connection.legacy is used", MessageLevel.Debug, null);

            if (!ShouldProcess(instance.ToString(), "Making a new Connection String"))
                return;

            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false,
                    _legacyScriptBlock,
                    null,
                    instance,
                    Credential,
                    AccessToken,
                    ApplicationIntent,
                    BatchSeparator,
                    ClientName,
                    ConnectTimeout,
                    Database,
                    EncryptConnection.ToBool(),
                    FailoverPartner,
                    IsActiveDirectoryUniversalAuth.ToBool(),
                    LockTimeout,
                    MaxPoolSize,
                    MinPoolSize,
                    MultipleActiveResultSets.ToBool(),
                    MultiSubnetFailover.ToBool(),
                    NetworkProtocol,
                    NonPooledConnection.ToBool(),
                    PacketSize,
                    PooledConnectionLifetime,
                    StatementTimeout,
                    SqlExecutionModes,
                    TrustServerCertificate.ToBool(),
                    WorkstationId,
                    AppendConnectionString,
                    TestBound("ConnectTimeout"),
                    TestBound("ClientName"),
                    TestBound("Credential"));

                if (results != null && results.Count > 0)
                {
                    foreach (var result in results)
                    {
                        if (result != null)
                        {
                            object output = result.BaseObject;
                            if (output is string)
                            {
                                WriteObject(output);
                            }
                            else
                            {
                                WriteObject(result);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StopFunction(
                    String.Format("Failed to create legacy connection string for {0}", instance),
                    exception: ex,
                    target: instance,
                    isContinue: true);
                TestFunctionInterrupt();
                return;
            }
        }

        /// <summary>
        /// Reads a value from the dbatools configuration system.
        /// </summary>
        private static object GetConfigValue(string fullName)
        {
            Config config;
            if (ConfigurationHost.Configurations.TryGetValue(fullName, out config))
            {
                return config.Value;
            }
            return null;
        }
    }
}
