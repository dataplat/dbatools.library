#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;
using Microsoft.SqlServer.Management.Smo;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Lists connections from sys.dm_exec_connections. Port of public/Get-DbaConnection.ps1
/// (W1-058; the W1-052 sibling flow). Connect rides NestedConnect (MinimumVersion 9); the
/// verbatim SELECT runs through the Server.Query ETS hop under EngineTryScope (the
/// function's try) with its rows streaming as output; the catch maps to Stop-Function
/// -Exception $_ -Continue - the PS [Exception] bind of an ErrorRecord unwraps to its
/// Exception, so the exception-only StopFunction path carries the caught fault.
/// Surface pinned by migration/baselines/Get-DbaConnection.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaConnection")]
public sealed class GetDbaConnectionCommand : DbaInstanceCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public override DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    [Alias("Credential", "Cred")]
    public override PSCredential? SqlCredential { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        foreach (DbaInstanceParameter instance in SqlInstance)
        {
            Hashtable connectParams = new Hashtable();
            connectParams["SqlInstance"] = instance;
            connectParams["SqlCredential"] = SqlCredential;
            connectParams["MinimumVersion"] = 9;
            NestedConnect.Outcome connection = NestedConnect.Connect(this, connectParams);
            if (!connection.Ok)
            {
                StopFunction("Failure", target: instance, errorRecord: connection.Failure, category: ErrorCategory.ConnectionError, continueLoop: true);
                continue;
            }
            Server server = connection.Server!;

            WriteMessage(MessageLevel.Debug, "Getting results for the following query: " + ConnectionSql + ".");
            try
            {
                using EngineTryScope tryScope = EngineTryScope.Enter(this);
                foreach (PSObject? item in NestedCommand.InvokeScoped(this, ServerQueryScript, server, ConnectionSql))
                    WriteObject(item);
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // PS: Stop-Function -Message "Failure" -Target $server -Exception $_
                //     -Continue - the [Exception] bind of the record unwraps its Exception.
                Exception fault = (ex as IContainsErrorRecord)?.ErrorRecord?.Exception is Exception inner && inner is not ParentContainsErrorRecordException ? inner : ex;
                StopFunction("Failure", target: server, exception: fault, continueLoop: true);
                continue;
            }
        }
    }

    // PS: $db-level Query on the engine (ETS dispatch - the W1-046 seam).
    private const string ServerQueryScript = """
param($server, $query)
$server.Query($query)
""";

    private const string ConnectionSql = """
SELECT  SERVERPROPERTY('MachineName') AS ComputerName,
                            ISNULL(SERVERPROPERTY('InstanceName'), 'MSSQLSERVER') AS InstanceName,
                            SERVERPROPERTY('ServerName') AS SqlInstance,
                            session_id AS SessionId, most_recent_session_id AS MostRecentSessionId, connect_time AS ConnectTime,
                            net_transport AS Transport, protocol_type AS ProtocolType, protocol_version AS ProtocolVersion,
                            endpoint_id AS EndpointId, encrypt_option AS EncryptOption, auth_scheme AS AuthScheme, node_affinity AS NodeAffinity,
                            num_reads AS Reads, num_writes AS Writes, last_read AS LastRead, last_write AS LastWrite,
                            net_packet_size AS PacketSize, client_net_address AS ClientNetworkAddress, client_tcp_port AS ClientTcpPort,
                            local_net_address AS ServerNetworkAddress, local_tcp_port AS ServerTcpPort, connection_id AS ConnectionId,
                            parent_connection_id AS ParentConnectionId, most_recent_sql_handle AS MostRecentSqlHandle
                            FROM sys.dm_exec_connections
""";

}
