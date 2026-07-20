#nullable enable

using System.Management.Automation;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Parameter;
using Microsoft.SqlServer.Management.Smo;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Creates a persistent SQL Server Management Object (SMO) connection for database operations.
/// Port of public/Connect-DbaInstance.ps1; surface pinned by migration/baselines/Connect-DbaInstance.json.
/// The resolution and auth matrices live in ConnectionService (shared runtime); this class owns
/// the parameter surface, the per-call-site Stop-Function semantics, and the TEPP script loop.
/// </summary>
[Cmdlet(VerbsCommunications.Connect, "DbaInstance")]
[OutputType(typeof(Server), typeof(Microsoft.Data.SqlClient.SqlConnection))]
public sealed partial class ConnectDbaInstanceCommand : DbaInstanceCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    [Alias("Connstring", "ConnectionString")]
    public override DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Credential object used to connect to the SQL Server Instance as a different user.</summary>
    [Parameter(Position = 1)]
    public override PSCredential? SqlCredential { get; set; }

    /// <summary>Specifies the initial database context for the connection instead of connecting to the default database.</summary>
    [Parameter(Position = 2)]
    public string? Database { get; set; }

    /// <summary>Declares the application workload type when connecting to an Always On Availability Group.</summary>
    [Parameter(Position = 3)]
    [PsStringCast]
    [ValidateSet("ReadOnly", "ReadWrite")]
    public string? ApplicationIntent { get; set; }

    /// <summary>Causes the connection to fail if the target is detected as Azure SQL Database.</summary>
    [Parameter]
    public SwitchParameter AzureUnsupported { get; set; }

    /// <summary>Sets the batch separator for multi-statement SQL execution, defaulting to "GO".</summary>
    [Parameter(Position = 4)]
    public string? BatchSeparator { get; set; }

    /// <summary>Sets a custom application name in the connection string for identification in SQL Server monitoring tools.</summary>
    [Parameter(Position = 5)]
    public string? ClientName { get; set; }

    /// <summary>Sets the connection timeout in seconds before the connection attempt fails.</summary>
    [Parameter(Position = 6)]
    public int ConnectTimeout { get; set; }

    /// <summary>Forces SSL encryption for all data transmitted between client and server.</summary>
    [Parameter]
    public SwitchParameter EncryptConnection { get; set; }

    /// <summary>Specifies the failover partner server name for database mirroring configurations.</summary>
    [Parameter(Position = 7)]
    public string? FailoverPartner { get; set; }

    /// <summary>Sets the lock timeout in seconds for transactions on this connection.</summary>
    [Parameter(Position = 8)]
    public int LockTimeout { get; set; }

    /// <summary>Sets the maximum number of connections allowed in the connection pool for this connection string.</summary>
    [Parameter(Position = 9)]
    public int MaxPoolSize { get; set; }

    /// <summary>Sets the minimum number of connections maintained in the connection pool for this connection string.</summary>
    [Parameter(Position = 10)]
    public int MinPoolSize { get; set; }

    /// <summary>Specifies the minimum SQL Server version required for the connection to succeed.</summary>
    [Parameter(Position = 11)]
    public int MinimumVersion { get; set; }

    /// <summary>Enables Multiple Active Result Sets (MARS) allowing multiple commands to be executed simultaneously on a single connection.</summary>
    [Parameter]
    public SwitchParameter MultipleActiveResultSets { get; set; }

    /// <summary>Enables faster detection and connection to the active server in Always On Availability Groups across multiple subnets.</summary>
    [Parameter]
    public SwitchParameter MultiSubnetFailover { get; set; }

    /// <summary>Specifies the network protocol for connecting to SQL Server.</summary>
    [Parameter(Position = 12)]
    [PsStringCast]
    [ValidateSet("TcpIp", "NamedPipes", "Multiprotocol", "AppleTalk", "BanyanVines", "Via", "SharedMemory", "NWLinkIpxSpx")]
    public string? NetworkProtocol { get; set; }

    /// <summary>Creates a dedicated connection that bypasses connection pooling.</summary>
    [Parameter]
    public SwitchParameter NonPooledConnection { get; set; }

    /// <summary>Sets the network packet size in bytes for communication with SQL Server.</summary>
    [Parameter(Position = 13)]
    public int PacketSize { get; set; }

    /// <summary>Sets the maximum lifetime in seconds for pooled connections before they're discarded and recreated.</summary>
    [Parameter(Position = 14)]
    public int PooledConnectionLifetime { get; set; }

    /// <summary>Controls how SQL commands are processed by the connection.</summary>
    [Parameter(Position = 15)]
    [PsStringCast]
    [ValidateSet("CaptureSql", "ExecuteAndCaptureSql", "ExecuteSql")]
    public string? SqlExecutionModes { get; set; }

    /// <summary>Sets the timeout in seconds for SQL statement execution before canceling the command.</summary>
    [Parameter(Position = 16)]
    public int StatementTimeout { get; set; }

    /// <summary>Bypasses certificate validation when using encrypted connections.</summary>
    [Parameter]
    public SwitchParameter TrustServerCertificate { get; set; }

    /// <summary>Attempts connection with proper TLS validation first, then retries with TrustServerCertificate if the initial connection fails due to certificate validation.</summary>
    [Parameter]
    public SwitchParameter AllowTrustServerCertificate { get; set; }

    /// <summary>Sets the workstation name visible in SQL Server monitoring and session information.</summary>
    [Parameter(Position = 17)]
    public string? WorkstationId { get; set; }

    /// <summary>Enables Always Encrypted support for accessing encrypted columns in databases with column-level encryption.</summary>
    [Parameter]
    public SwitchParameter AlwaysEncrypted { get; set; }

    /// <summary>Adds custom connection string parameters to the generated connection string.</summary>
    [Parameter(Position = 18)]
    public string? AppendConnectionString { get; set; }

    /// <summary>Returns only a SqlConnection object instead of the full SMO server object.</summary>
    [Parameter]
    public SwitchParameter SqlConnectionOnly { get; set; }

    /// <summary>Specifies the domain for Azure SQL Database connections, defaulting to database.windows.net.</summary>
    [Parameter(Position = 19)]
    public string AzureDomain { get; set; } = "database.windows.net";

    /// <summary>Specifies the Azure Active Directory tenant ID for Azure SQL Database authentication.</summary>
    [Parameter(Position = 20)]
    public string? Tenant { get; set; }

    /// <summary>Authenticates to Azure SQL Database using an access token generated by Get-AzAccessToken or New-DbaAzAccessToken.</summary>
    [Parameter(Position = 21)]
    public PSObject? AccessToken { get; set; }

    /// <summary>Specifies the authentication method for connecting to Azure SQL or Entra ID-protected SQL Server instances.</summary>
    [Parameter(Position = 22)]
    [PsStringCast]
    [ValidateSet("ActiveDirectoryIntegrated", "ActiveDirectoryInteractive", "ActiveDirectoryPassword", "ActiveDirectoryServicePrincipal", "ActiveDirectoryManagedIdentity", "ActiveDirectoryDeviceCodeFlow")]
    public string? AuthenticationType { get; set; }

    /// <summary>Creates a dedicated administrator connection (DAC) for emergency access to SQL Server.</summary>
    [Parameter]
    public SwitchParameter DedicatedAdminConnection { get; set; }

    /// <summary>Changes exception handling from throwing errors to displaying warnings (this command has exceptions enabled by default).</summary>
    [Parameter]
    public SwitchParameter DisableException { get; set; }

    // EnableException is inherited from DbaBaseCmdlet. The PS surface only carried
    // DisableException; the inherited parameter is an additive surface gain, honored when
    // explicitly bound (see BeginProcessing).

    protected override void BeginProcessing()
    {
        /*
        Usually, the parameter type should have been not object but off the PSCredential type.
        When binding null to a PSCredential type parameter on PS3-4, it'd then show a prompt, asking for username and password.

        In order to avoid that and having to refactor lots of functions (and to avoid making regular scripts harder to read), we created this workaround.
        */
        // The compiled parameter is typed PSCredential, so the begin-block type check of the
        // PS source cannot fail here; the binder enforces it.

        // In an unusual move, Connect-DbaInstance goes the exact opposite way of all commands when it comes to exceptions
        // this means that by default it Stop-Function -Messages, but do not be tempted to Stop-Function -Message
        if (!TestBound(nameof(EnableException)))
            EnableException = new SwitchParameter(!DisableException.IsPresent);

        // [string]$Tenant = (Get-DbatoolsConfigValue -FullName 'azure.tenantid')
        if (!TestBound(nameof(Tenant)) && string.IsNullOrEmpty(Tenant))
            Tenant = ConnectionService.GetConfigurationValue("azure.tenantid") as string;
    }
}
