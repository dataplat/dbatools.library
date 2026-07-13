#nullable enable

using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Parameter;
using Microsoft.SqlServer.Management.Smo;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Lists the oldest open transactions. Port of public/Get-DbaOpenTransaction.ps1
/// (W1-086). The begin-block query emits RAW as a bare statement - the Server.Query hop
/// output writes through unfiltered (the empty-resultset shape included) and a fault
/// surfaces statement-conditionally. Surface pinned by
/// migration/baselines/Get-DbaOpenTransaction.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaOpenTransaction")]
public sealed class GetDbaOpenTransactionCommand : DbaInstanceCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public override DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public override PSCredential? SqlCredential { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    private const string Sql = @"
            SELECT  SERVERPROPERTY('MachineName') AS ComputerName,
            ISNULL(SERVERPROPERTY('InstanceName'), 'MSSQLSERVER') AS InstanceName,
            SERVERPROPERTY('ServerName') AS SqlInstance,
            [s_tst].[session_id] AS Spid,
            [s_es].[login_name] AS Login,
            DB_NAME (s_tdt.database_id) AS [Database],
            [s_tdt].[database_transaction_begin_time] AS [BeginTime],
            [s_tdt].[database_transaction_log_bytes_used] AS [LogBytesUsed],
            [s_tdt].[database_transaction_log_bytes_reserved] AS [LogBytesReserved],
            [s_est].text AS [LastQuery],
            [s_eqp].[query_plan] AS [LastPlan]
            FROM
                sys.dm_tran_database_transactions [s_tdt]
            JOIN
                sys.dm_tran_session_transactions [s_tst]
            ON
                [s_tst].[transaction_id] = [s_tdt].[transaction_id]
            JOIN
                sys.dm_exec_sessions [s_es]
            ON
                [s_es].[session_id] = [s_tst].[session_id]
            JOIN
                sys.dm_exec_connections [s_ec]
            ON
                [s_ec].[session_id] = [s_tst].[session_id]
            LEFT OUTER JOIN
                sys.dm_exec_requests [s_er]
            ON
                [s_er].[session_id] = [s_tst].[session_id]
            CROSS APPLY
                sys.dm_exec_sql_text ([s_ec].[most_recent_sql_handle]) AS [s_est]
            OUTER APPLY
                sys.dm_exec_query_plan ([s_er].[plan_handle]) AS [s_eqp]
            ORDER BY
                [BeginTime] ASC";

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

            try
            {
                foreach (PSObject? item in NestedCommand.InvokeScoped(this, ServerQueryScript, server, Sql))
                    WriteObject(item);
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (RuntimeException ex)
            {
                StatementFault.Surface(this, ex, "Get-DbaOpenTransaction");
            }
        }
    }

    // PS: $server.Query($query) on the engine (the W1-046 seam).
    private const string ServerQueryScript = """
param($server, $query)
$server.Query($query)
""";
}
