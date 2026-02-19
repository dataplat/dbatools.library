using System;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Text.RegularExpressions;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Adds a replica to an availability group on one or more SQL Server instances.
    /// Automatically creates database mirroring endpoints if required.
    /// </summary>
    [Cmdlet("Add", "DbaAgReplica", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low)]
    public class AddDbaAgReplicaCommand : DbaBaseCmdlet
    {
        #region Parameters

        /// <summary>
        /// The target SQL Server instance or instances. Server version must be SQL Server version 2012 or higher.
        /// </summary>
        [Parameter(Mandatory = true)]
        public DbaInstanceParameter[] SqlInstance { get; set; }

        /// <summary>
        /// Login to the target instances using alternative credentials.
        /// </summary>
        [Parameter()]
        public PSCredential SqlCredential { get; set; }

        /// <summary>
        /// Sets the display name for the availability group replica being added.
        /// Defaults to the SQL Server instance's domain instance name.
        /// </summary>
        [Parameter()]
        public string Name { get; set; }

        /// <summary>
        /// Specifies the underlying clustering technology for the availability group.
        /// Only supported in SQL Server 2017 and above.
        /// </summary>
        [Parameter()]
        [ValidateSet("Wsfc", "External", "None")]
        public string ClusterType { get; set; }

        /// <summary>
        /// Controls how the replica commits transactions relative to the primary replica.
        /// Defaults to SynchronousCommit.
        /// </summary>
        [Parameter()]
        [ValidateSet("AsynchronousCommit", "SynchronousCommit")]
        public string AvailabilityMode { get; set; } = "SynchronousCommit";

        /// <summary>
        /// Determines whether the replica can automatically fail over when the primary becomes unavailable.
        /// Defaults to Automatic.
        /// </summary>
        [Parameter()]
        [ValidateSet("Automatic", "Manual", "External")]
        public string FailoverMode { get; set; } = "Automatic";

        /// <summary>
        /// Sets the replica's preference for hosting backups within the availability group (0-100).
        /// Defaults to 50.
        /// </summary>
        [Parameter()]
        [ValidateRange(0, 100)]
        public int BackupPriority { get; set; } = 50;

        /// <summary>
        /// Controls which client connections are allowed when this replica is the primary.
        /// Defaults to AllowAllConnections.
        /// </summary>
        [Parameter()]
        [ValidateSet("AllowAllConnections", "AllowReadWriteConnections")]
        public string ConnectionModeInPrimaryRole { get; set; } = "AllowAllConnections";

        /// <summary>
        /// Controls client access to secondary replicas for read operations.
        /// Supports friendly aliases: No, Read-intent only, Yes.
        /// </summary>
        [Parameter()]
        [ValidateSet("AllowNoConnections", "AllowReadIntentConnectionsOnly", "AllowAllConnections", "No", "Read-intent only", "Yes")]
        public string ConnectionModeInSecondaryRole { get; set; }

        /// <summary>
        /// Controls how databases are initially synchronized on the secondary replica.
        /// Requires SQL Server 2016 or later.
        /// </summary>
        [Parameter()]
        [ValidateSet("Automatic", "Manual")]
        public string SeedingMode { get; set; }

        /// <summary>
        /// Specifies the name of the database mirroring endpoint to use for availability group communication.
        /// </summary>
        [Parameter()]
        public string Endpoint { get; set; }

        /// <summary>
        /// Overrides the default endpoint URL with custom network addresses for availability group communication.
        /// Must be in format 'TCP://system-address:port' with one entry per instance.
        /// </summary>
        [Parameter()]
        public string[] EndpointUrl { get; set; }

        /// <summary>
        /// Returns the replica object without actually creating it in the availability group.
        /// </summary>
        [Parameter()]
        public SwitchParameter Passthru { get; set; }

        /// <summary>
        /// Defines the priority order of replica server names for routing read-only connections.
        /// Requires SQL Server 2016 or later.
        /// </summary>
        [Parameter()]
        public string[] ReadOnlyRoutingList { get; set; }

        /// <summary>
        /// Specifies the connection URL for read-only routing operations.
        /// Must be in format 'TCP://system-address:port'.
        /// </summary>
        [Parameter()]
        public string ReadonlyRoutingConnectionUrl { get; set; }

        /// <summary>
        /// Configures certificate-based authentication for the database mirroring endpoint.
        /// </summary>
        [Parameter()]
        public string Certificate { get; set; }

        /// <summary>
        /// Automatically configures the AlwaysOn_health extended events session.
        /// </summary>
        [Parameter()]
        public SwitchParameter ConfigureXESession { get; set; }

        /// <summary>
        /// Sets the timeout period in seconds for detecting replica connectivity failures.
        /// </summary>
        [Parameter()]
        [ValidateRange(1, 3600)]
        public int SessionTimeout { get; set; }

        /// <summary>
        /// Accepts an availability group object from Get-DbaAvailabilityGroup for pipeline operations.
        /// Type is object because SMO types are loaded dynamically at runtime.
        /// </summary>
        [Parameter(ValueFromPipeline = true, Mandatory = true)]
        public object InputObject { get; set; }

        #endregion Parameters

        #region Static ScriptBlocks

        /// <summary>
        /// Connects to a SQL Server instance using Connect-DbaInstance with MinimumVersion 11.
        /// </summary>
        private static readonly ScriptBlock _connectInstanceScript = ScriptBlock.Create(@"
param($instance, $credential, $hasCred)
if ($hasCred) {
    Connect-DbaInstance -SqlInstance $instance -SqlCredential $credential -MinimumVersion 11
} else {
    Connect-DbaInstance -SqlInstance $instance -MinimumVersion 11
}
");

        /// <summary>
        /// Gets a certificate from a SQL Server instance.
        /// </summary>
        private static readonly ScriptBlock _getCertificateScript = ScriptBlock.Create(@"
param($server, $certName)
Get-DbaDbCertificate -SqlInstance $server -Certificate $certName
");

        /// <summary>
        /// Gets database mirroring endpoints from a SQL Server instance.
        /// </summary>
        private static readonly ScriptBlock _getEndpointScript = ScriptBlock.Create(@"
param($server)
Get-DbaEndpoint -SqlInstance $server -Type DatabaseMirroring
");

        /// <summary>
        /// Creates a new database mirroring endpoint and starts it.
        /// When an IPv4 address is provided in the endpoint URL, it is used for the endpoint configuration.
        /// </summary>
        private static readonly ScriptBlock _createEndpointScript = ScriptBlock.Create(@"
param($server, $endpointName, $certificate, $hasCert, $hasIPv4, $ipAddress, $port)
$epParams = @{
    SqlInstance         = $server
    Name                = $endpointName
    Type                = 'DatabaseMirroring'
    EndpointEncryption  = 'Supported'
    EncryptionAlgorithm = 'Aes'
}
if ($hasCert) { $epParams['Certificate'] = $certificate }
if ($hasIPv4) {
    $epParams['IPAddress'] = $ipAddress
    $epParams['Port'] = $port
}
$ep = New-DbaEndpoint @epParams
$null = $ep | Start-DbaEndpoint
$ep
");

        /// <summary>
        /// Gets a dbatools configuration value with a fallback.
        /// </summary>
        private static readonly ScriptBlock _getConfigValueScript = ScriptBlock.Create(@"
param($name, $fallback)
Get-DbatoolsConfigValue -FullName $name -Fallback $fallback
");

        /// <summary>
        /// Creates an AvailabilityReplica SMO object with all configured properties.
        /// </summary>
        private static readonly ScriptBlock _createReplicaScript = ScriptBlock.Create(@"
param($ag, $name, $epUrl, $failoverMode, $availabilityMode, $isStandard,
      $connModePrimary, $connModeSecondary, $backupPriority,
      $hasRoutingList, $routingList, $hasRoutingUrl, $routingUrl,
      $hasSeedingMode, $seedingMode, $hasSessionTimeout, $sessionTimeout, $versionMajor)
$replica = New-Object Microsoft.SqlServer.Management.Smo.AvailabilityReplica -ArgumentList $ag, $name
$replica.EndpointUrl = $epUrl
$replica.FailoverMode = [Microsoft.SqlServer.Management.Smo.AvailabilityReplicaFailoverMode]::$failoverMode
$replica.AvailabilityMode = [Microsoft.SqlServer.Management.Smo.AvailabilityReplicaAvailabilityMode]::$availabilityMode
if (-not $isStandard) {
    $replica.ConnectionModeInPrimaryRole = [Microsoft.SqlServer.Management.Smo.AvailabilityReplicaConnectionModeInPrimaryRole]::$connModePrimary
    $replica.ConnectionModeInSecondaryRole = [Microsoft.SqlServer.Management.Smo.AvailabilityReplicaConnectionModeInSecondaryRole]::$connModeSecondary
}
$replica.BackupPriority = $backupPriority
if ($hasRoutingList -and $versionMajor -ge 13) {
    $replica.ReadonlyRoutingList = $routingList
}
if ($hasRoutingUrl -and $versionMajor -ge 13) {
    $replica.ReadonlyRoutingConnectionUrl = $routingUrl
}
if ($hasSeedingMode -and $versionMajor -ge 13) {
    $replica.SeedingMode = $seedingMode
}
if ($hasSessionTimeout) {
    $replica.SessionTimeout = $sessionTimeout
}
$replica
");

        /// <summary>
        /// Finds the SYSTEM login by SID on a server and creates it if missing,
        /// then grants the required server permissions for WSFC clustering.
        /// </summary>
        private static readonly ScriptBlock _wsfcPermissionsScript = ScriptBlock.Create(@"
param($server)
$systemLoginSidString = '1-1-0-0-0-0-0-5-18-0-0-0'
$systemLoginName = ($server.Logins | Where-Object { ($_.Sid -join '-') -eq $systemLoginSidString }).Name
if (-not $systemLoginName) {
    Write-Message -Level Verbose -Message 'SYSTEM login not found, so we hope system language is english and create login [NT AUTHORITY\SYSTEM]'
    try {
        $null = New-DbaLogin -SqlInstance $server -Login 'NT AUTHORITY\SYSTEM'
        $systemLoginName = 'NT AUTHORITY\SYSTEM'
    } catch {
        throw ""Failed to add login [NT AUTHORITY\SYSTEM]. If it's a non-english system you have to add the equivalent login manually.""
    }
}
$permissionSet = New-Object -TypeName Microsoft.SqlServer.Management.SMO.ServerPermissionSet(
    [Microsoft.SqlServer.Management.SMO.ServerPermission]::AlterAnyAvailabilityGroup,
    [Microsoft.SqlServer.Management.SMO.ServerPermission]::ConnectSql,
    [Microsoft.SqlServer.Management.SMO.ServerPermission]::ViewServerState
)
try {
    $server.Grant($permissionSet, $systemLoginName)
} catch {
    throw ""Failure adding cluster service account permissions.""
}
");

        /// <summary>
        /// Configures the AlwaysOn_health extended events session: sets AutoStart and starts it if not running.
        /// </summary>
        private static readonly ScriptBlock _configureXESessionScript = ScriptBlock.Create(@"
param($server, $ee, $instanceName)
$xeSession = Get-DbaXESession -SqlInstance $server -Session AlwaysOn_health -EnableException:$ee
if ($xeSession) {
    if (-not $xeSession.AutoStart) {
        Write-Message -Level Debug -Message ""Setting autostart for session 'AlwaysOn_health' on $instanceName.""
        $xeSession.AutoStart = $true
        $xeSession.Alter()
    }
    if (-not $xeSession.IsRunning) {
        Write-Message -Level Debug -Message ""Starting session 'AlwaysOn_health' on $instanceName.""
        $null = $xeSession | Start-DbaXESession -EnableException:$ee
    }
    @{ Found = $true; Running = $true }
} else {
    @{ Found = $false; Running = $false }
}
");

        /// <summary>
        /// Adds the replica to the AG, invokes Create if AG already exists,
        /// joins the secondary to the AG, and calls Alter.
        /// </summary>
        private static readonly ScriptBlock _createAndJoinScript = ScriptBlock.Create(@"
param($ag, $replica, $name, $instance, $credential, $hasCred, $agState)
$ag.AvailabilityReplicas.Add($replica)
$agreplica = $ag.AvailabilityReplicas[$name]
if ($agState -eq 'Existing') {
    Invoke-Create -Object $replica
    $joinParams = @{
        SqlInstance       = $instance
        AvailabilityGroup = $ag.Name
    }
    if ($hasCred) { $joinParams['SqlCredential'] = $credential }
    $null = Join-DbaAvailabilityGroup @joinParams
    $agreplica.Alter()
}
$agreplica
");

        /// <summary>
        /// Grants CreateAnyDatabase permission to the availability group.
        /// </summary>
        private static readonly ScriptBlock _grantSeedingScript = ScriptBlock.Create(@"
param($server, $agName)
Grant-DbaAgPermission -SqlInstance $server -Type AvailabilityGroup -AvailabilityGroup $agName -Permission CreateAnyDatabase -EnableException
");

        /// <summary>
        /// Grants Connect permission on the endpoint for the service account.
        /// </summary>
        private static readonly ScriptBlock _grantEndpointScript = ScriptBlock.Create(@"
param($server, $serviceAccount)
Grant-DbaAgPermission -SqlInstance $server -Type Endpoint -Login $serviceAccount -Permission Connect -EnableException
");

        /// <summary>
        /// Adds NoteProperty members and applies Select-DefaultView to the output replica object.
        /// </summary>
        private static readonly ScriptBlock _formatOutputScript = ScriptBlock.Create(@"
param($agreplica)
Add-Member -Force -InputObject $agreplica -MemberType NoteProperty -Name ComputerName -Value $agreplica.Parent.ComputerName
Add-Member -Force -InputObject $agreplica -MemberType NoteProperty -Name InstanceName -Value $agreplica.Parent.InstanceName
Add-Member -Force -InputObject $agreplica -MemberType NoteProperty -Name SqlInstance -Value $agreplica.Parent.SqlInstance
Add-Member -Force -InputObject $agreplica -MemberType NoteProperty -Name AvailabilityGroup -Value $agreplica.Parent.Name
Add-Member -Force -InputObject $agreplica -MemberType NoteProperty -Name Replica -Value $agreplica.Name
$defaults = 'ComputerName', 'InstanceName', 'SqlInstance', 'AvailabilityGroup', 'Name', 'Role', 'RollupSynchronizationState', 'AvailabilityMode', 'BackupPriority', 'EndpointUrl', 'SessionTimeout', 'FailoverMode', 'ReadonlyRoutingList'
Select-DefaultView -InputObject $agreplica -Property $defaults
");

        /// <summary>
        /// Gets server properties needed for replica configuration.
        /// Returns a hashtable with EngineEdition, VersionMajor, DomainInstanceName, HostPlatform, Name, ServiceAccount.
        /// </summary>
        private static readonly ScriptBlock _getServerPropertiesScript = ScriptBlock.Create(@"
param($server)
@{
    EngineEdition      = [string]$server.EngineEdition
    VersionMajor       = [int]$server.VersionMajor
    DomainInstanceName = [string]$server.DomainInstanceName
    HostPlatform       = [string]$server.HostPlatform
    Name               = [string]$server.Name
    ServiceAccount     = [string]$server.ServiceAccount
}
");

        /// <summary>
        /// Gets the endpoint FQDN from an endpoint object.
        /// </summary>
        private static readonly ScriptBlock _getEndpointFqdnScript = ScriptBlock.Create(@"
param($ep)
$ep.Fqdn
");

        /// <summary>
        /// Gets the State property from an InputObject (AvailabilityGroup).
        /// </summary>
        private static readonly ScriptBlock _getAgStateScript = ScriptBlock.Create(@"
param($ag)
[string]$ag.State
");

        /// <summary>
        /// Gets the Name property from an InputObject (AvailabilityGroup).
        /// </summary>
        private static readonly ScriptBlock _getAgNameScript = ScriptBlock.Create(@"
param($ag)
[string]$ag.Name
");

        #endregion Static ScriptBlocks

        #region Private State

        /// <summary>
        /// Resolved ClusterType value (from config or parameter).
        /// </summary>
        private string _resolvedClusterType;

        /// <summary>
        /// Resolved ConnectionModeInSecondaryRole value (from config or parameter, normalized).
        /// </summary>
        private string _resolvedConnectionModeInSecondaryRole;

        /// <summary>
        /// Index counter for consuming EndpointUrl array entries, one per SqlInstance iteration.
        /// Reset each time ProcessRecord is called (i.e., each pipeline AG).
        /// </summary>
        private int _endpointUrlIndex;

        /// <summary>
        /// Working copy of EndpointUrl for the current ProcessRecord invocation.
        /// </summary>
        private string[] _currentEndpointUrls;

        #endregion Private State

        #region Lifecycle Methods

        /// <summary>
        /// Resolves configuration defaults for ClusterType and ConnectionModeInSecondaryRole.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            // Resolve ClusterType from config if not bound
            if (TestBound("ClusterType"))
            {
                _resolvedClusterType = ClusterType;
            }
            else
            {
                _resolvedClusterType = GetConfigString("AvailabilityGroups.Default.ClusterType", "Wsfc");
            }

            // Resolve ConnectionModeInSecondaryRole from config if not bound
            if (TestBound("ConnectionModeInSecondaryRole"))
            {
                _resolvedConnectionModeInSecondaryRole = NormalizeSecondaryConnectionMode(ConnectionModeInSecondaryRole);
            }
            else
            {
                string configValue = GetConfigString("AvailabilityGroups.Default.ConnectionModeInSecondaryRole", "AllowNoConnections");
                _resolvedConnectionModeInSecondaryRole = NormalizeSecondaryConnectionMode(configValue);
            }
        }

        /// <summary>
        /// Processes each piped AvailabilityGroup object. InputObject arrives one at a time via pipeline.
        /// Inside, loops over each SqlInstance to add replicas to this AG.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (TestFunctionInterrupt())
                return;

            // Reset the endpoint URL index for each AG piped in
            _endpointUrlIndex = 0;
            _currentEndpointUrls = EndpointUrl;

            // Validate EndpointUrl count matches SqlInstance count
            if (_currentEndpointUrls != null && _currentEndpointUrls.Length > 0)
            {
                if (_currentEndpointUrls.Length != SqlInstance.Length)
                {
                    StopFunction("The number of elements in EndpointUrl is not correct");
                    return;
                }

                foreach (string epUrl in _currentEndpointUrls)
                {
                    if (!IsValidEndpointUrl(epUrl))
                    {
                        StopFunction(String.Format("EndpointUrl '{0}' not in correct format 'TCP://system-address:port'", epUrl));
                        return;
                    }
                }
            }

            // Validate ReadonlyRoutingConnectionUrl format
            if (TestBound("ReadonlyRoutingConnectionUrl"))
            {
                if (!IsValidEndpointUrl(ReadonlyRoutingConnectionUrl))
                {
                    StopFunction("ReadonlyRoutingConnectionUrl not in correct format 'TCP://system-address:port'");
                    return;
                }
            }

            // Get AG name and state for use in processing
            string agName = GetAgName(InputObject);
            string agState = GetAgState(InputObject);

            foreach (DbaInstanceParameter instance in SqlInstance)
            {
                ProcessInstance(instance, agName, agState);
            }
        }

        #endregion Lifecycle Methods

        #region Core Processing

        /// <summary>
        /// Processes a single SqlInstance: connects, sets up endpoint, creates replica, handles permissions and output.
        /// </summary>
        private void ProcessInstance(DbaInstanceParameter instance, string agName, string agState)
        {
            // Connect to the instance
            object server;
            try
            {
                server = ConnectInstance(instance);
                if (server == null)
                {
                    StopFunction(String.Format("Failed to connect to {0}", instance), target: instance, isContinue: true, category: ErrorCategory.ConnectionError);
                    TestFunctionInterrupt();
                    return;
                }
            }
            catch (Exception ex)
            {
                StopFunction("Failure",
                    errorRecord: new ErrorRecord(ex, "AddDbaAgReplica_ConnectionError", ErrorCategory.ConnectionError, instance),
                    target: instance, isContinue: true, category: ErrorCategory.ConnectionError);
                TestFunctionInterrupt();
                return;
            }

            // Get server properties
            ServerInfo serverInfo = GetServerInfo(server);
            if (serverInfo == null)
            {
                StopFunction(String.Format("Failed to retrieve server properties from {0}", instance),
                    target: instance, isContinue: true);
                TestFunctionInterrupt();
                return;
            }

            // Validate certificate if specified
            if (!String.IsNullOrEmpty(Certificate))
            {
                if (!CertificateExists(server, Certificate))
                {
                    StopFunction(String.Format("Certificate {0} does not exist on {1}", Certificate, instance),
                        target: Certificate, isContinue: true);
                    TestFunctionInterrupt();
                    return;
                }
            }

            // Get the endpoint URL for this instance (shift from the array)
            string epUrl = null;
            if (_currentEndpointUrls != null && _endpointUrlIndex < _currentEndpointUrls.Length)
            {
                epUrl = _currentEndpointUrls[_endpointUrlIndex];
            }
            _endpointUrlIndex++;

            // Get or create database mirroring endpoint
            object endpoint = GetMirroringEndpoint(server);
            if (endpoint == null)
            {
                // No endpoint exists, need to create one
                string endpointName = !String.IsNullOrEmpty(Endpoint) ? Endpoint : "hadr_endpoint";

                if (ShouldProcess(serverInfo.Name, String.Format("Adding endpoint named {0} to {1}", endpointName, instance)))
                {
                    try
                    {
                        endpoint = CreateEndpoint(server, endpointName, epUrl);
                        if (endpoint != null)
                        {
                            epUrl = GetEndpointFqdn(endpoint);
                        }
                    }
                    catch (Exception ex)
                    {
                        StopFunction(String.Format("Failed to create endpoint on {0}", instance),
                            exception: ex, target: instance, isContinue: true);
                        TestFunctionInterrupt();
                        return;
                    }
                }
            }
            else
            {
                // Endpoint exists -- PS1 always uses the endpoint's FQDN regardless of EndpointUrl parameter.
                // The EndpointUrl parameter only influences new endpoint creation (IPv4 extraction).
                epUrl = GetEndpointFqdn(endpoint);
            }

            // Default the Name to server.DomainInstanceName if not bound
            string replicaName;
            if (TestBound("Name"))
            {
                replicaName = Name;
            }
            else
            {
                replicaName = serverInfo.DomainInstanceName;
            }

            if (ShouldProcess(serverInfo.Name, String.Format("Creating a replica for {0} named {1}", agName, replicaName)))
            {
                try
                {
                    // Create the replica object
                    object replica = CreateReplica(
                        InputObject, replicaName, epUrl,
                        FailoverMode, AvailabilityMode,
                        String.Equals(serverInfo.EngineEdition, "Standard", StringComparison.OrdinalIgnoreCase),
                        ConnectionModeInPrimaryRole, _resolvedConnectionModeInSecondaryRole,
                        BackupPriority,
                        TestBound("ReadOnlyRoutingList"), ReadOnlyRoutingList,
                        TestBound("ReadonlyRoutingConnectionUrl"), ReadonlyRoutingConnectionUrl,
                        TestBound("SeedingMode"), SeedingMode,
                        TestBound("SessionTimeout"), SessionTimeout,
                        serverInfo.VersionMajor);

                    if (replica == null)
                    {
                        StopFunction(String.Format("Failed to create replica object for {0}", instance),
                            target: instance, isContinue: true);
                        TestFunctionInterrupt();
                        return;
                    }

                    // Warn about low SessionTimeout
                    if (TestBound("SessionTimeout") && SessionTimeout < 10)
                    {
                        WriteMessageWarning(
                            "We recommend that you keep the time-out period at 10 seconds or greater. " +
                            "Setting the value to less than 10 seconds creates the possibility of a heavily " +
                            "loaded system missing pings and falsely declaring failure. " +
                            "Please see sqlps.io/agrec for more information.");
                    }

                    // Add cluster permissions for WSFC
                    if (String.Equals(_resolvedClusterType, "Wsfc", StringComparison.OrdinalIgnoreCase))
                    {
                        if (ShouldProcess(serverInfo.Name, String.Format("Adding cluster permissions for availability group named {0}", agName)))
                        {
                            WriteMessageVerbose("WSFC Cluster requires granting [NT AUTHORITY\\SYSTEM] a few things. Setting now.");
                            try
                            {
                                InvokeCommand.InvokeScript(false, _wsfcPermissionsScript, null, new object[] { server });
                            }
                            catch (Exception ex)
                            {
                                // The WSFC script has separate try/catch for login creation vs grant,
                                // each throwing a descriptive message. Surface the script's throw message.
                                StopFunction(ex.Message,
                                    errorRecord: new ErrorRecord(ex, "AddDbaAgReplica_WsfcPermissions", ErrorCategory.SecurityError, server),
                                    target: server);
                                TestFunctionInterrupt();
                            }
                        }
                    }

                    // Configure XE session
                    if (ConfigureXESession.IsPresent)
                    {
                        ConfigureXEHealthSession(server, instance.ToString());
                    }

                    // Passthru: return the replica before Create/Join
                    if (Passthru.IsPresent)
                    {
                        WriteObject(replica);
                        return;
                    }

                    // Add replica to AG, Create, Join, Alter
                    object agreplica = CreateAndJoinReplica(InputObject, replica, replicaName, instance, agState);

                    // Grant seeding permission if needed (only on non-Linux, only if AG already exists)
                    if (!String.Equals(serverInfo.HostPlatform, "Linux", StringComparison.OrdinalIgnoreCase))
                    {
                        if (TestBound("SeedingMode") && String.Equals(SeedingMode, "Automatic", StringComparison.OrdinalIgnoreCase)
                            && String.Equals(agState, "Existing", StringComparison.OrdinalIgnoreCase))
                        {
                            if (ShouldProcess(serverInfo.Name, "Granting CreateAnyDatabase permission to the availability group"))
                            {
                                try
                                {
                                    InvokeCommand.InvokeScript(false, _grantSeedingScript, null,
                                        new object[] { server, agName });
                                }
                                catch (Exception ex)
                                {
                                    StopFunction("Failure granting CreateAnyDatabase permission to the availability group",
                                        errorRecord: new ErrorRecord(ex, "AddDbaAgReplica_GrantSeeding", ErrorCategory.SecurityError, server),
                                        target: server);
                                }
                            }
                        }

                        // Grant endpoint Connect permission to service account (unless using certificate auth)
                        if (String.IsNullOrEmpty(Certificate))
                        {
                            string serviceAccount = serverInfo.ServiceAccount;
                            if (ShouldProcess(serverInfo.Name, String.Format("Granting Connect permission for the endpoint to service account {0}", serviceAccount)))
                            {
                                try
                                {
                                    InvokeCommand.InvokeScript(false, _grantEndpointScript, null,
                                        new object[] { server, serviceAccount });
                                }
                                catch (Exception ex)
                                {
                                    StopFunction(
                                        String.Format("Failure granting Connect permission for the endpoint to service account {0}", serviceAccount),
                                        errorRecord: new ErrorRecord(ex, "AddDbaAgReplica_GrantEndpoint", ErrorCategory.SecurityError, server),
                                        target: server);
                                }
                            }
                        }
                    }

                    // Format and output the replica
                    if (agreplica != null)
                    {
                        FormatAndOutputReplica(agreplica);
                    }
                }
                catch (Exception ex)
                {
                    string msg = GetInnerExceptionMessage(ex);
                    if (String.IsNullOrEmpty(msg))
                    {
                        msg = ex.Message;
                    }
                    StopFunction(msg,
                        errorRecord: new ErrorRecord(ex, "AddDbaAgReplica_CreateReplica", ErrorCategory.InvalidOperation, instance),
                        target: instance, isContinue: true);
                    TestFunctionInterrupt();
                }
            }
        }

        #endregion Core Processing

        #region Helper Methods

        /// <summary>
        /// Normalizes friendly ConnectionModeInSecondaryRole aliases to their SMO enum names.
        /// </summary>
        internal static string NormalizeSecondaryConnectionMode(string mode)
        {
            if (mode == null)
                return null;

            if (String.Equals(mode, "No", StringComparison.OrdinalIgnoreCase))
                return "AllowNoConnections";
            if (String.Equals(mode, "Read-intent only", StringComparison.OrdinalIgnoreCase))
                return "AllowReadIntentConnectionsOnly";
            if (String.Equals(mode, "Yes", StringComparison.OrdinalIgnoreCase))
                return "AllowAllConnections";

            return mode;
        }

        /// <summary>
        /// Validates an endpoint URL matches the required TCP://address:port format with valid port range.
        /// </summary>
        internal static bool IsValidEndpointUrl(string epUrl)
        {
            if (String.IsNullOrEmpty(epUrl) || epUrl.Length > 512)
                return false;
            Match m = Regex.Match(epUrl, @"^TCP://[^:]+:(\d{1,5})$", RegexOptions.IgnoreCase);
            if (!m.Success)
                return false;
            int port;
            if (!int.TryParse(m.Groups[1].Value, out port))
                return false;
            return port >= 1 && port <= 65535;
        }

        /// <summary>
        /// Gets the inner exception message by unwrapping two levels, matching PS1 behavior:
        /// $_.Exception.InnerException.InnerException.Message
        /// </summary>
        internal static string GetInnerExceptionMessage(Exception ex)
        {
            if (ex == null)
                return null;
            if (ex.InnerException != null && ex.InnerException.InnerException != null)
                return ex.InnerException.InnerException.Message;
            if (ex.InnerException != null)
                return ex.InnerException.Message;
            return null;
        }

        /// <summary>
        /// Gets a dbatools configuration value string with a fallback.
        /// </summary>
        private string GetConfigString(string name, string fallback)
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, _getConfigValueScript, null,
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
        /// Connects to a SQL Server instance using Connect-DbaInstance.
        /// </summary>
        private object ConnectInstance(DbaInstanceParameter instance)
        {
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, _connectInstanceScript, null,
                new object[] { instance, SqlCredential, SqlCredential != null });

            if (results != null && results.Count > 0 && results[0] != null)
                return results[0].BaseObject ?? results[0];
            return null;
        }

        /// <summary>
        /// Gets server properties needed for replica configuration.
        /// </summary>
        private ServerInfo GetServerInfo(object server)
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, _getServerPropertiesScript, null,
                    new object[] { server });

                if (results != null && results.Count > 0 && results[0] != null)
                {
                    PSObject psObj = results[0] as PSObject;
                    if (psObj == null) psObj = PSObject.AsPSObject(results[0]);

                    // The script returns a Hashtable
                    object baseObj = psObj.BaseObject;
                    if (baseObj is System.Collections.Hashtable ht)
                    {
                        ServerInfo info = new ServerInfo();
                        info.EngineEdition = ht["EngineEdition"] != null ? ht["EngineEdition"].ToString() : "";
                        info.DomainInstanceName = ht["DomainInstanceName"] != null ? ht["DomainInstanceName"].ToString() : "";
                        info.HostPlatform = ht["HostPlatform"] != null ? ht["HostPlatform"].ToString() : "";
                        info.Name = ht["Name"] != null ? ht["Name"].ToString() : "";
                        info.ServiceAccount = ht["ServiceAccount"] != null ? ht["ServiceAccount"].ToString() : "";

                        object versionObj = ht["VersionMajor"];
                        if (versionObj is int intVal)
                        {
                            info.VersionMajor = intVal;
                        }
                        else if (versionObj != null)
                        {
                            int parsed;
                            if (int.TryParse(versionObj.ToString(), out parsed))
                                info.VersionMajor = parsed;
                        }

                        return info;
                    }
                }
            }
            catch (Exception)
            {
                // Failed to get server info
            }
            return null;
        }

        /// <summary>
        /// Checks if a certificate exists on the server.
        /// </summary>
        private bool CertificateExists(object server, string certName)
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, _getCertificateScript, null,
                    new object[] { server, certName });
                return results != null && results.Count > 0 && results[0] != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Gets the database mirroring endpoint from a server. Returns null if none exists.
        /// </summary>
        private object GetMirroringEndpoint(object server)
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, _getEndpointScript, null,
                    new object[] { server });
                if (results != null && results.Count > 0 && results[0] != null)
                    return results[0].BaseObject ?? results[0];
            }
            catch (Exception)
            {
                // No endpoint found
            }
            return null;
        }

        /// <summary>
        /// Creates a new database mirroring endpoint, optionally with IPv4 address from the endpoint URL.
        /// </summary>
        private object CreateEndpoint(object server, string endpointName, string epUrl)
        {
            // Parse IPv4 address and port from endpoint URL if it contains an IPv4 address
            bool hasIPv4 = false;
            string ipAddress = null;
            string port = null;

            if (!String.IsNullOrEmpty(epUrl) && Regex.IsMatch(epUrl, @"^TCP://\d+\.\d+\.\d+\.\d+:\d{1,5}$", RegexOptions.IgnoreCase))
            {
                ipAddress = Regex.Replace(epUrl, @"^TCP://([^:]+):\d+$", "$1", RegexOptions.IgnoreCase);
                port = Regex.Replace(epUrl, @"^TCP://[^:]+:(\d+)$", "$1", RegexOptions.IgnoreCase);

                // Validate port range
                int portNum;
                if (int.TryParse(port, out portNum) && portNum >= 1 && portNum <= 65535)
                {
                    hasIPv4 = true;
                }
                else
                {
                    hasIPv4 = false;
                }
            }

            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, _createEndpointScript, null,
                new object[]
                {
                    server,
                    endpointName,
                    String.IsNullOrEmpty(Certificate) ? null : Certificate,
                    !String.IsNullOrEmpty(Certificate),
                    hasIPv4,
                    ipAddress,
                    port
                });

            if (results != null && results.Count > 0 && results[0] != null)
                return results[0].BaseObject ?? results[0];
            return null;
        }

        /// <summary>
        /// Gets the FQDN of an endpoint object.
        /// </summary>
        private string GetEndpointFqdn(object endpoint)
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, _getEndpointFqdnScript, null,
                    new object[] { endpoint });
                if (results != null && results.Count > 0 && results[0] != null)
                {
                    object val = results[0].BaseObject ?? results[0];
                    return val.ToString();
                }
            }
            catch (Exception)
            {
                // Best effort
            }
            return null;
        }

        /// <summary>
        /// Creates the AvailabilityReplica SMO object with all configured properties.
        /// </summary>
        private object CreateReplica(
            object ag, string name, string epUrl,
            string failoverMode, string availabilityMode,
            bool isStandard,
            string connModePrimary, string connModeSecondary,
            int backupPriority,
            bool hasRoutingList, string[] routingList,
            bool hasRoutingUrl, string routingUrl,
            bool hasSeedingMode, string seedingMode,
            bool hasSessionTimeout, int sessionTimeout,
            int versionMajor)
        {
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, _createReplicaScript, null,
                new object[]
                {
                    ag, name, epUrl, failoverMode, availabilityMode, isStandard,
                    connModePrimary, connModeSecondary, backupPriority,
                    hasRoutingList, routingList, hasRoutingUrl, routingUrl,
                    hasSeedingMode, seedingMode, hasSessionTimeout, sessionTimeout, versionMajor
                });

            if (results != null && results.Count > 0 && results[0] != null)
                return results[0].BaseObject ?? results[0];
            return null;
        }

        /// <summary>
        /// Configures the AlwaysOn_health XE session on the instance.
        /// </summary>
        private void ConfigureXEHealthSession(object server, string instanceName)
        {
            try
            {
                WriteMessageAtLevel(
                    String.Format("Getting session 'AlwaysOn_health' on {0}.", instanceName),
                    MessageLevel.Debug, null);

                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, _configureXESessionScript, null,
                    new object[] { server, true, instanceName });

                if (results != null && results.Count > 0 && results[0] != null)
                {
                    PSObject psObj = results[0] as PSObject;
                    if (psObj == null) psObj = PSObject.AsPSObject(results[0]);

                    object baseObj = psObj.BaseObject;
                    if (baseObj is System.Collections.Hashtable ht)
                    {
                        bool found = false;
                        if (ht["Found"] is bool foundVal) found = foundVal;

                        if (found)
                        {
                            WriteMessageVerbose(String.Format(
                                "ConfigureXESession was set, session 'AlwaysOn_health' is now configured and running on {0}.",
                                instanceName));
                        }
                        else
                        {
                            WriteMessageWarning(String.Format(
                                "ConfigureXESession was set, but no session named 'AlwaysOn_health' was found on {0}.",
                                instanceName));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WriteMessageWarning(String.Format(
                    "ConfigureXESession was set, but configuration failed on {0} with this error: {1}",
                    instanceName, ex.Message));
            }
        }

        /// <summary>
        /// Adds the replica to the AG, calls Create/Join if the AG already exists, and returns the agreplica.
        /// </summary>
        private object CreateAndJoinReplica(object ag, object replica, string replicaName, DbaInstanceParameter instance, string agState)
        {
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, _createAndJoinScript, null,
                new object[]
                {
                    ag, replica, replicaName, instance,
                    SqlCredential, SqlCredential != null, agState
                });

            if (results != null && results.Count > 0 && results[0] != null)
                return results[0].BaseObject ?? results[0];
            return null;
        }

        /// <summary>
        /// Adds NoteProperty members and outputs the replica with Select-DefaultView.
        /// </summary>
        private void FormatAndOutputReplica(object agreplica)
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, _formatOutputScript, null,
                    new object[] { agreplica });

                if (results != null)
                {
                    foreach (PSObject r in results)
                    {
                        if (r != null)
                            WriteObject(r);
                    }
                }
            }
            catch (Exception)
            {
                // Fallback: output the raw object
                WriteObject(agreplica);
            }
        }

        /// <summary>
        /// Gets the Name of the AvailabilityGroup InputObject.
        /// </summary>
        private string GetAgName(object ag)
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, _getAgNameScript, null,
                    new object[] { ag });
                if (results != null && results.Count > 0 && results[0] != null)
                {
                    object val = results[0].BaseObject ?? results[0];
                    return val.ToString();
                }
            }
            catch (Exception)
            {
                // Best effort
            }
            return null;
        }

        /// <summary>
        /// Gets the State of the AvailabilityGroup InputObject.
        /// </summary>
        private string GetAgState(object ag)
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, _getAgStateScript, null,
                    new object[] { ag });
                if (results != null && results.Count > 0 && results[0] != null)
                {
                    object val = results[0].BaseObject ?? results[0];
                    return val.ToString();
                }
            }
            catch (Exception)
            {
                // Best effort
            }
            return null;
        }

        #endregion Helper Methods

        #region Internal Types

        /// <summary>
        /// Holds server properties retrieved in a single script call for efficiency.
        /// </summary>
        private class ServerInfo
        {
            public string EngineEdition { get; set; }
            public int VersionMajor { get; set; }
            public string DomainInstanceName { get; set; }
            public string HostPlatform { get; set; }
            public string Name { get; set; }
            public string ServiceAccount { get; set; }
        }

        #endregion Internal Types
    }
}
