#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Management.Automation;
using System.Text.RegularExpressions;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;
using Microsoft.SqlServer.Management.Smo;
// the sibling namespace Dataplat.Dbatools.Database shadows Smo.Database inside
// Dataplat.Dbatools.Commands (the W1-020 namespace trap) - alias the SMO type
using SmoDatabase = Microsoft.SqlServer.Management.Smo.Database;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Finds auto-growth/shrink events in the default trace. Port of
/// public/Find-DbaDbGrowthEvent.ps1 (W1-054). Connect rides NestedConnect (NO
/// MinimumVersion in this function); the begin-block template assembly is reproduced
/// exactly (the event-class ArrayList whittling under the ANY-bound Test-Bound gate, the
/// UseLocalTime fragment split, the -replace _DatabaseList_ with PS regex-substitution
/// semantics intact); Select-DefaultView over the Server.Query ETS result rides ONE module
/// hop under EngineTryScope (the function's try{}); the catch maps to Stop-Function with
/// the triple InnerException walk (null-propagating) and -Continue. SQL fragments are
/// tooling-extracted byte-verbatim.
/// Surface pinned by migration/baselines/Find-DbaDbGrowthEvent.json.
/// </summary>
[Cmdlet(VerbsCommon.Find, "DbaDbGrowthEvent")]
public sealed class FindDbaDbGrowthEventCommand : DbaInstanceCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public override DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public override PSCredential? SqlCredential { get; set; }

    /// <summary>The database(s) to process.</summary>
    [Parameter(Position = 2)]
    public object[]? Database { get; set; }

    /// <summary>The database(s) to exclude.</summary>
    [Parameter(Position = 3)]
    public object[]? ExcludeDatabase { get; set; }

    /// <summary>Growth or Shrink.</summary>
    [Parameter(Position = 4)]
    [PsStringCast]
    [ValidateSet("Growth", "Shrink")]
    public string? EventType { get; set; }

    /// <summary>Data or Log.</summary>
    [Parameter(Position = 5)]
    [PsStringCast]
    [ValidateSet("Data", "Log")]
    public string? FileType { get; set; }

    /// <summary>Keeps trace times in server-local time instead of UTC.</summary>
    [Parameter]
    public SwitchParameter UseLocalTime { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    private string _sqlTemplate = "";

    protected override void BeginProcessing()
    {
        // PS: 92..95 ArrayList; ANY of EventType/FileType bound gates BOTH whittles.
        List<int> eventClass = new List<int> { 92, 93, 94, 95 };
        if (TestBound("EventType", "FileType"))
        {
            if (PsString.Eq(FileType, "Data"))
            {
                eventClass.Remove(93);
                eventClass.Remove(95);
            }
            else if (PsString.Eq(FileType, "Log"))
            {
                eventClass.Remove(92);
                eventClass.Remove(94);
            }
            if (PsString.Eq(EventType, "Growth"))
            {
                eventClass.Remove(94);
                eventClass.Remove(95);
            }
            else if (PsString.Eq(EventType, "Shrink"))
            {
                eventClass.Remove(92);
                eventClass.Remove(93);
            }
        }
        string eventClassFilter = string.Join(",", eventClass);

        string timeFragment = UseLocalTime.ToBool() ? LocalTimeFragment : UtcTimeFragment;
        _sqlTemplate = SqlTemplateHead + timeFragment + SqlTemplateTailA + eventClassFilter + SqlTemplateTailB;
    }

    protected override void ProcessRecord()
    {
        foreach (DbaInstanceParameter instance in SqlInstance)
        {
            Hashtable connectParams = new Hashtable();
            connectParams["SqlInstance"] = instance;
            connectParams["SqlCredential"] = SqlCredential;
            NestedConnect.Outcome connection = NestedConnect.Connect(this, connectParams);
            if (!connection.Ok)
            {
                StopFunction("Failure", target: instance, errorRecord: connection.Failure, category: ErrorCategory.ConnectionError, continueLoop: true);
                continue;
            }
            Server server = connection.Server!;

            // PS: $dbs = $server.Databases (| Where Name -In $Database) (| Where Name -NotIn $ExcludeDatabase)
            List<SmoDatabase> dbs = new List<SmoDatabase>();
            foreach (SmoDatabase candidate in server.Databases)
            {
                if (PsOps.IsTrue(Database) && !PsOps.In(candidate.Name, Database))
                    continue;
                if (PsOps.IsTrue(ExcludeDatabase) && PsOps.In(candidate.Name, ExcludeDatabase))
                    continue;
                dbs.Add(candidate);
            }

            // PS: $dbsList = "'$(names -join "','")'"
            List<string> names = new List<string>();
            foreach (SmoDatabase db in dbs)
                names.Add(db.Name);
            string dbsList = "'" + string.Join("','", names) + "'";
            WriteMessage(MessageLevel.Verbose, "Executing query against " + dbsList + " on " + PsText(instance));

            // PS: -replace keeps regex substitution semantics on BOTH sides.
            string sql = Regex.Replace(_sqlTemplate, "_DatabaseList_", dbsList);
            WriteMessage(MessageLevel.Debug, "Executing SQL Statement:\n " + sql);

            // PS: Select-DefaultView -InputObject $server.Query($sql) -Property $defaults -
            // ONE statement inside the try: both ride the engine hop.
            try
            {
                using EngineTryScope tryScope = EngineTryScope.Enter(this);
                foreach (PSObject? item in NestedCommand.InvokeScoped(this, QueryWithDefaultViewScript, server, sql))
                    WriteObject(item);
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // PS: Stop-Function -Message "Issue collecting data on $server" -Target
                //     $server -ErrorRecord $_ -Exception
                //     $_.Exception.InnerException.InnerException.InnerException -Continue
                Exception? tripleInner = ex.InnerException?.InnerException?.InnerException;
                StopFunction("Issue collecting data on " + PsText(server), target: server, errorRecord: ToCaughtRecord(ex), exception: tripleInner, continueLoop: true);
                continue;
            }
        }
    }

    /// <summary>PS string interpolation of a value.</summary>
    private static string PsText(object? value)
    {
        if (value is null)
            return "";
        return PSObject.AsPSObject(value).ToString();
    }

    /// <summary>PS: catch { $_ } with the flattened-record rebuild (W1-009 class).</summary>
    private static ErrorRecord ToCaughtRecord(Exception ex)
    {
        ErrorRecord? inner = (ex as IContainsErrorRecord)?.ErrorRecord;
        if (inner is not null && inner.Exception is not ParentContainsErrorRecordException)
            return inner;
        if (inner is not null)
            return new ErrorRecord(ex, FirstErrorIdComponent(inner.FullyQualifiedErrorId), inner.CategoryInfo.Category, inner.TargetObject);
        return new ErrorRecord(ex, "Find-DbaDbGrowthEvent", ErrorCategory.NotSpecified, null);
    }

    private static string FirstErrorIdComponent(string? fullyQualifiedErrorId)
    {
        if (string.IsNullOrEmpty(fullyQualifiedErrorId))
            return "Find-DbaDbGrowthEvent";
        int comma = fullyQualifiedErrorId!.IndexOf(',');
        return comma < 0 ? fullyQualifiedErrorId : fullyQualifiedErrorId.Substring(0, comma);
    }

    // PS: Select-DefaultView -InputObject $server.Query($sql) -Property $defaults - the
    // whole statement on the engine (ETS Query dispatch + the private view decorator).
    private const string QueryWithDefaultViewScript = """
param($server, $query)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($server, $query)
    $defaults = 'ComputerName', 'InstanceName', 'SqlInstance', 'EventClass', 'DatabaseName', 'Filename', 'Duration', 'StartTime', 'EndTime', 'ChangeInSize', 'ApplicationName', 'HostName'
    Select-DefaultView -InputObject $server.Query($query) -Property $defaults
} $server $query 3>&1
""";

    private const string SqlTemplateHead = """

            BEGIN TRY
                IF (SELECT CONVERT(INT,[value_in_use]) FROM sys.configurations WHERE [name] = 'default trace enabled' ) = 1
                    BEGIN
                        DECLARE @curr_tracefilename VARCHAR(500);
                        DECLARE @base_tracefilename VARCHAR(500);
                        DECLARE @indx INT;

                        SELECT @curr_tracefilename = [path]
                        FROM sys.traces
                        WHERE is_default = 1 ;

                        SET @curr_tracefilename = REVERSE(@curr_tracefilename);
                        SELECT @indx  = PATINDEX('%\%', @curr_tracefilename);
                        SET @curr_tracefilename = REVERSE(@curr_tracefilename);
                        SET @base_tracefilename = LEFT( @curr_tracefilename,LEN(@curr_tracefilename) - @indx) + '\log.trc';

                        SELECT
                            SERVERPROPERTY('MachineName') AS ComputerName,
                            ISNULL(SERVERPROPERTY('InstanceName'), 'MSSQLSERVER') AS InstanceName,
                            SERVERPROPERTY('ServerName') AS SqlInstance,
                            CONVERT(INT,(DENSE_RANK() OVER (ORDER BY [StartTime] DESC))%2) AS OrderRank,
                                CONVERT(INT, [EventClass]) AS EventClass,
                            [DatabaseName],
                            (SELECT database_id FROM sys.databases WHERE Name = [DatabaseName]) AS DatabaseId,
                            [Filename],
                            CONVERT(INT,(Duration/1000)) AS Duration,
                            
""";

    private const string UtcTimeFragment = """

                            DATEADD (MINUTE, DATEDIFF(MINUTE, GETDATE(), GETUTCDATE()), [StartTime]) AS StartTime,  -- Convert to UTC time
                            DATEADD (MINUTE, DATEDIFF(MINUTE, GETDATE(), GETUTCDATE()), [EndTime]) AS EndTime,  -- Convert to UTC time
""";

    private const string LocalTimeFragment = """

                            [StartTime] AS StartTime,
                            [EndTime] AS EndTime,
""";

    private const string SqlTemplateTailA = """

                            ([IntegerData]*8.0/1024) AS ChangeInSize,
                            ApplicationName,
                            HostName,
                            SessionLoginName,
                            SPID
                        FROM ::fn_trace_gettable( @base_tracefilename, DEFAULT )
                        WHERE
                            [EventClass] IN (
""";

    private const string SqlTemplateTailB = """
)
                            AND [DatabaseName] IN (_DatabaseList_)
                        ORDER BY [StartTime] DESC;
                    END
                ELSE
                    SELECT
                        SERVERPROPERTY('MachineName') AS ComputerName,
                        ISNULL(SERVERPROPERTY('InstanceName'), 'MSSQLSERVER') AS InstanceName,
                        SERVERPROPERTY('ServerName') AS SqlInstance,
                        -100 AS [OrderRank],
                        -1 AS [OrderRank],
                        0 AS [EventClass],
                        0 [DatabaseName],
                        0 AS [Filename],
                        0 AS [Duration],
                        0 AS [StartTime],
                        0 AS [EndTime],
                        0 AS ChangeInSize,
                        0 AS [ApplicationName],
                        0 AS [HostName],
                        0 AS [SessionLoginName],
                        0 AS [SPID]
            END    TRY
            BEGIN CATCH
                SELECT
                    SERVERPROPERTY('MachineName') AS ComputerName,
                    ISNULL(SERVERPROPERTY('InstanceName'), 'MSSQLSERVER') AS InstanceName,
                    SERVERPROPERTY('ServerName') AS SqlInstance,
                    -100 AS [OrderRank],
                    -100 AS [OrderRank],
                    ERROR_NUMBER() AS [EventClass],
                    ERROR_SEVERITY() AS [DatabaseName],
                    ERROR_STATE() AS [Filename],
                    ERROR_MESSAGE() AS [Duration],
                    1 AS [StartTime],
                    1 AS [EndTime],
                    1 AS [ChangeInSize],
                    1 AS [ApplicationName],
                    1 AS [HostName],
                    1 AS [SessionLoginName],
                    1 AS [SPID]
            END CATCH
""";

}
