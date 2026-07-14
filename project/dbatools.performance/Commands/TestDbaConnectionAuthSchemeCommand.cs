#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Reports the transport and negotiated authentication scheme for the current SQL Server
/// connection. Port of public/Test-DbaConnectionAuthScheme.ps1 (W1-125). The complete
/// per-record body rides one module-scoped PowerShell hop so Connect-DbaInstance, the
/// Server.Query ETS method, Stop-Function's dynamic continues, NTLM-over-Kerberos switch
/// precedence, PS property enumeration, and Select-DefaultView execute with the source's
/// observable engine semantics. Surface pinned by
/// migration/baselines/Test-DbaConnectionAuthScheme.json.
/// </summary>
[Cmdlet(VerbsDiagnostic.Test, "DbaConnectionAuthScheme")]
public sealed class TestDbaConnectionAuthSchemeCommand : DbaInstanceCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public override DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    [Alias("Credential", "Cred")]
    public override PSCredential? SqlCredential { get; set; }

    /// <summary>Return whether the negotiated authentication scheme is Kerberos.</summary>
    [Parameter]
    public SwitchParameter Kerberos { get; set; }

    /// <summary>Return whether the negotiated authentication scheme is NTLM.</summary>
    [Parameter]
    public SwitchParameter Ntlm { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BodyScript,
            SqlInstance, SqlCredential, Kerberos.ToBool(), Ntlm.ToBool(),
            EnableException.ToBool(), BoundVerbose()))
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
            }
            else
            {
                WriteObject(item);
            }
        }
    }

    /// <summary>A bound -Verbose carrier for the module-scoped process body.</summary>
    private object? BoundVerbose()
    {
        if (MyInvocation.BoundParameters.TryGetValue("Verbose", out object? verbose))
            return LanguagePrimitives.IsTrue(verbose);
        return null;
    }

    /// <summary>Remove the nested merged-pipeline copy before re-emitting the same record.</summary>
    private void RemoveHopErrorBookkeeping(ErrorRecord record)
    {
        try
        {
            if (SessionState.PSVariable.GetValue("Error") is not ArrayList errorList || errorList.Count == 0)
                return;
            if (errorList[0] is not ErrorRecord first)
                return;
            if (ReferenceEquals(first, record) || ReferenceEquals(first.Exception, record.Exception) ||
                string.Equals(first.Exception?.Message, record.Exception?.Message, StringComparison.Ordinal))
            {
                errorList.RemoveAt(0);
            }
        }
        catch
        {
            // Best-effort bookkeeping only.
        }
    }

    private const string BodyScript = """
param($SqlInstance, $SqlCredential, $Kerberos, $Ntlm, $EnableException, $__boundVerbose)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($SqlInstance, $SqlCredential, $Kerberos, $Ntlm, $EnableException, $__boundVerbose)
    if ($null -ne $__boundVerbose) { $VerbosePreference = $(if ($__boundVerbose) { "Continue" } else { "SilentlyContinue" }) }

    $sql = "SELECT  SERVERPROPERTY('MachineName') AS ComputerName,
                        ISNULL(SERVERPROPERTY('InstanceName'), 'MSSQLSERVER') AS InstanceName,
                        SERVERPROPERTY('ServerName') AS SqlInstance,
                        session_id AS SessionId, most_recent_session_id AS MostRecentSessionId, connect_time AS ConnectTime,
                        net_transport AS Transport, protocol_type AS ProtocolType, protocol_version AS ProtocolVersion,
                        endpoint_id AS EndpointId, encrypt_option AS EncryptOption, auth_scheme AS AuthScheme, node_affinity AS NodeAffinity,
                        num_reads AS NumReads, num_writes AS NumWrites, last_read AS LastRead, last_write AS LastWrite,
                        net_packet_size AS PacketSize, client_net_address AS ClientNetworkAddress, client_tcp_port AS ClientTcpPort,
                        local_net_address AS ServerNetworkAddress, local_tcp_port AS ServerTcpPort, connection_id AS ConnectionId,
                        parent_connection_id AS ParentConnectionId, most_recent_sql_handle AS MostRecentSqlHandle
                        FROM sys.dm_exec_connections WHERE session_id = @@SPID"

    foreach ($instance in $SqlInstance) {
        try {
            $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential -MinimumVersion 9
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Test-DbaConnectionAuthScheme
        }

        Write-Message -Level Verbose -Message "Getting results for the following query: $sql." -FunctionName Test-DbaConnectionAuthScheme
        try {
            $results = $server.Query($sql)
        } catch {
            Stop-Function -Message "Failure" -Target $server -ErrorRecord $_ -Continue -FunctionName Test-DbaConnectionAuthScheme
        }

        # Source quirk: when both switches are present, Ntlm wins.
        if ($Kerberos -or $Ntlm) {
            if ($Ntlm) {
                $auth = 'NTLM'
            } else {
                $auth = 'Kerberos'
            }
            [PSCustomObject]@{
                ComputerName = $results.ComputerName
                InstanceName = $results.InstanceName
                SqlInstance  = $results.SqlInstance
                Result       = ($results.AuthScheme -eq $auth)
            } | Select-DefaultView -Property SqlInstance, Result
        } else {
            Select-DefaultView -InputObject $results -Property ComputerName, InstanceName, SqlInstance, Transport, AuthScheme
        }
    }
} $SqlInstance $SqlCredential $Kerberos $Ntlm $EnableException $__boundVerbose 3>&1 2>&1
""";
}
