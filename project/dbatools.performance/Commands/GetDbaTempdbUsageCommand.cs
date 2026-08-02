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
/// Lists active tempdb space consumers. Port of public/Get-DbaTempdbUsage.ps1 (W1-100).
/// The W1-086 bare-emission shape: the loop-local $sql is constant and the un-tried
/// $server.Query($sql) statement emits RAW through the hop unfiltered (the ETS
/// empty-resultset real-null shape included); a fault surfaces statement-conditionally.
/// Surface pinned by migration/baselines/Get-DbaTempdbUsage.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaTempdbUsage")]
public sealed class GetDbaTempdbUsageCommand : DbaInstanceCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public override DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public override PSCredential? SqlCredential { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    private const string Sql = @"SELECT  SERVERPROPERTY('MachineName') AS ComputerName,
        ISNULL(SERVERPROPERTY('InstanceName'), 'MSSQLSERVER') AS InstanceName,
        SERVERPROPERTY('ServerName') AS SqlInstance,
        t.session_id AS Spid,
        r.command AS StatementCommand,
        SUBSTRING(   est.[text], (r.statement_start_offset / 2) + 1,
            ((CASE r.statement_end_offset
                WHEN-1 THEN DATALENGTH(est.[text]) ELSE r.statement_end_offset
                END - r.statement_start_offset
            ) / 2 ) + 1 ) AS QueryText,
        QUOTENAME(DB_NAME(r.database_id)) + N'.' + QUOTENAME(OBJECT_SCHEMA_NAME(est.objectid, est.dbid)) + N'.'
        + QUOTENAME(OBJECT_NAME(est.objectid, est.dbid)) AS ProcedureName,
        r.start_time AS StartTime,
        tdb.UserObjectAllocated * 8 AS CurrentUserAllocatedKB,
        (t.user_objects_alloc_page_count + tdb.UserObjectAllocated) * 8 AS TotalUserAllocatedKB,
        tdb.UserObjectDeallocated * 8 AS UserDeallocatedKB,
        (t.user_objects_dealloc_page_count + tdb.UserObjectDeallocated) * 8 AS TotalUserDeallocatedKB,
        tdb.InternalObjectAllocated * 8 AS InternalAllocatedKB,
        (t.internal_objects_alloc_page_count + tdb.InternalObjectAllocated) * 8 AS TotalInternalAllocatedKB,
        tdb.InternalObjectDeallocated * 8 AS InternalDeallocatedKB,
        (t.internal_objects_dealloc_page_count + tdb.InternalObjectDeallocated) * 8 AS TotalInternalDeallocatedKB,
        r.reads AS RequestedReads,
        r.writes AS RequestedWrites,
        r.logical_reads AS RequestedLogicalReads,
        r.cpu_time AS RequestedCPUTime,
        s.is_user_process AS IsUserProcess,
        s.[status] AS [Status],
        DB_NAME(r.database_id) AS [Database],
        s.login_name AS LoginName,
        s.original_login_name AS OriginalLoginName,
        s.nt_domain AS NTDomain,
        s.nt_user_name AS NTUserName,
        s.[host_name] AS HostName,
        s.[program_name] AS ProgramName,
        s.login_time AS LoginTime,
        s.last_request_start_time AS LastRequestedStartTime,
        s.last_request_end_time AS LastRequestedEndTime
FROM    sys.dm_db_session_space_usage AS t
INNER JOIN sys.dm_exec_sessions AS s
    ON s.session_id = t.session_id
LEFT JOIN sys.dm_exec_requests AS r
    ON r.session_id = s.session_id
LEFT JOIN (
    SELECT _tsu.session_id,
        _tsu.request_id,
        SUM(_tsu.user_objects_alloc_page_count)       AS UserObjectAllocated,
        SUM(_tsu.user_objects_dealloc_page_count)     AS UserObjectDeallocated,
        SUM(_tsu.internal_objects_alloc_page_count)   AS InternalObjectAllocated,
        SUM(_tsu.internal_objects_dealloc_page_count) AS InternalObjectDeallocated
    FROM tempdb.sys.dm_db_task_space_usage AS _tsu
    GROUP BY _tsu.session_id, _tsu.request_id
) AS tdb ON  tdb.session_id = r.session_id AND  tdb.request_id = r.request_id
OUTER APPLY sys.dm_exec_sql_text(r.[sql_handle]) AS est
WHERE   t.session_id != @@SPID
AND   (tdb.UserObjectAllocated - tdb.UserObjectDeallocated + tdb.InternalObjectAllocated - tdb.InternalObjectDeallocated) != 0
OPTION (RECOMPILE);";

    protected override void ProcessRecord()
    {
        foreach (DbaInstanceParameter instance in SqlInstance)
        {
            Hashtable connectParams = new Hashtable();
            connectParams["SqlInstance"] = instance;
            connectParams["SqlCredential"] = SqlCredential;
            connectParams["MinimumVersion"] = 10;
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
                StatementFault.Surface(this, ex, "Get-DbaTempdbUsage");
            }
        }
    }

    // PS: $server.Query($query) on the engine (the W1-046 seam).
    private const string ServerQueryScript = """
param($server, $query)
$server.Query($query)
""";
}
