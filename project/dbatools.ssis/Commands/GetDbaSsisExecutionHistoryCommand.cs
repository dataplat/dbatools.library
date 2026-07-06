#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management.Automation;
using System.Text;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;
using Dataplat.Dbatools.Utility;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management.Smo;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves SSIS package execution history from the SSISDB catalog.
/// Port of public/Get-DbaSsisExecutionHistory.ps1 with Invoke-DbaQuery inlined as a
/// parameterized SqlCommand against SSISDB; surface pinned by
/// migration/baselines/Get-DbaSsisExecutionHistory.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaSsisExecutionHistory")]
[OutputType(typeof(PSObject))]
public sealed class GetDbaSsisExecutionHistoryCommand : DbaInstanceCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    public override DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public override PSCredential? SqlCredential { get; set; }

    /// <summary>Limits results to executions that started on or after this date and time.</summary>
    [Parameter(Position = 2)]
    public DateTime Since { get; set; }

    /// <summary>Filters results to specific execution statuses.</summary>
    [Parameter(Position = 3)]
    [ValidateSet("Created", "Running", "Cancelled", "Failed", "Pending", "Halted", "Succeeded", "Stopping", "Completed")]
    public string[]? Status { get; set; }

    /// <summary>Filters results to specific SSIS projects deployed to the catalog.</summary>
    [Parameter(Position = 4)]
    public string[]? Project { get; set; }

    /// <summary>Filters results to specific SSIS catalog folders.</summary>
    [Parameter(Position = 5)]
    public string[]? Folder { get; set; }

    /// <summary>Filters results to specific SSIS environments used during execution.</summary>
    [Parameter(Position = 6)]
    public string[]? Environment { get; set; }

    private string _sql = string.Empty;
    private readonly List<KeyValuePair<string, object>> _queryParameters = new();

    protected override void BeginProcessing()
    {
        // PS begin block: SQL text and parameter set are composed once, including the
        // tab/newline shaping of the dynamic predicates.
        // ValidateSet binds case-insensitively and the PS hashtable lookup was too.
        Dictionary<string, int> statuses = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Created"] = 1,
            ["Running"] = 2,
            ["Cancelled"] = 3,
            ["Failed"] = 4,
            ["Pending"] = 5,
            ["Halted"] = 6,
            ["Succeeded"] = 7,
            ["Stopping"] = 8,
            ["Completed"] = 9,
        };

        string statusq = string.Empty;
        if (FilterHelper.IsActive(Status))
        {
            List<string> statusCodes = new();
            foreach (string status in Status!)
            {
                statusCodes.Add(statuses[status].ToString(CultureInfo.InvariantCulture));
            }
            statusq = "\n\t\tAND e.[Status] in (" + string.Join(",", statusCodes) + ")";
        }

        string projectq = BuildValuePredicate(Project, "project", "e.[project_name]");
        string folderq = BuildValuePredicate(Folder, "folder", "e.[folder_name]");
        string environmentq = BuildValuePredicate(Environment, "environment", "e.[environment_name]");

        // PS: if ($Since) - the unbound [datetime] default is falsy on both editions, so an
        // explicit MinValue behaves exactly like an omitted parameter.
        string sinceq = string.Empty;
        if (Since != default)
        {
            sinceq = "\n\t\tAND e.[start_time] >= @since";
            _queryParameters.Add(new KeyValuePair<string, object>("since", Since));
        }

        _sql = @"
        WITH
            cteLoglevel AS (
                SELECT
                    execution_id AS ExecutionID,
                    CAST(parameter_value AS INT) AS LoggingLevel
                FROM
                    [catalog].[execution_parameter_values]
                WHERE
                    parameter_name = 'LOGGING_LEVEL'
            )
            , cteStatus AS (
                SELECT
                     [key]
                    ,[code]
                FROM (
                    VALUES
                          ( 1,'Created'  )
                        , ( 2,'Running'  )
                        , ( 3,'Cancelled')
                        , ( 4,'Failed'   )
                        , ( 5,'Pending'  )
                        , ( 6,'Halted'   )
                        , ( 7,'Succeeded')
                        , ( 8,'Stopping' )
                        , ( 9,'Completed')
                ) codes([key],[code])
            )
            SELECT
                      e.execution_id AS ExecutionID
                    , e.folder_name AS FolderName
                    , e.project_name AS ProjectName
                    , e.package_name AS PackageName
                    , e.project_lsn AS ProjectLsn
                    , Environment = ISNULL(e.environment_folder_name, '') + ISNULL('\' + e.environment_name,  '')
                    , s.code AS StatusCode
                    , start_time AS StartTime
                    , end_time AS EndTime
                    , ElapsedMinutes = DATEDIFF(mi, e.start_time, e.end_time)
                    , l.LoggingLevel
            FROM
                [catalog].executions e
                LEFT OUTER JOIN cteLoglevel l
                    ON e.execution_id = l.ExecutionID
                LEFT OUTER JOIN cteStatus s
                    ON s.[key] = e.status
            WHERE 1=1" + statusq + projectq + folderq + environmentq + sinceq + @"
            OPTION  ( RECOMPILE );
        ";

        WriteMessage(MessageLevel.Debug, "\nSQL statement: " + _sql);
        StringBuilder parameterText = new();
        foreach (KeyValuePair<string, object> parameter in _queryParameters)
        {
            parameterText.Append(parameter.Key).Append(" = ").Append(Convert.ToString(parameter.Value, CultureInfo.InvariantCulture)).Append('\n');
        }
        WriteMessage(MessageLevel.Debug, "\nParameters:" + parameterText);
    }

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        foreach (DbaInstanceParameter instance in SqlInstance)
        {
            // PS: Invoke-DbaQuery -Database SSISDB ... called WITHOUT -EnableException, so a
            // failed instance surfaces as a friendly warning and the loop continues even when
            // this command itself runs with -EnableException. The two warning texts are
            // Invoke-DbaQuery's verbatim connect/execute messages (cross-model review
            // 2026-07-06 finding 6).
            Server server;
            try
            {
                SmoConnectionRequest request = new();
                request.Instance = instance;
                request.SqlCredential = SqlCredential;
                request.Database = "SSISDB";
                server = ConnectionService.GetServer(request);
                SetActiveConnection(server.ConnectionContext);
                if (!server.ConnectionContext.IsOpen)
                {
                    server.ConnectionContext.Connect();
                }
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                WriteMessage(MessageLevel.Warning, "Failure", target: instance, exception: ex);
                continue;
            }

            try
            {
                using SqlCommand command = new(_sql, server.ConnectionContext.SqlConnectionObject);
                foreach (KeyValuePair<string, object> parameter in _queryParameters)
                {
                    command.Parameters.AddWithValue(parameter.Key, parameter.Value);
                }
                SetActiveCommand(command);
                try
                {
                    using SqlDataReader reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        PSObject row = new();
                        row.Properties.Add(new PSNoteProperty("ExecutionID", ValueOrNull(reader["ExecutionID"])));
                        row.Properties.Add(new PSNoteProperty("FolderName", ValueOrNull(reader["FolderName"])));
                        row.Properties.Add(new PSNoteProperty("ProjectName", ValueOrNull(reader["ProjectName"])));
                        row.Properties.Add(new PSNoteProperty("PackageName", ValueOrNull(reader["PackageName"])));
                        row.Properties.Add(new PSNoteProperty("ProjectLsn", ValueOrNull(reader["ProjectLsn"])));
                        row.Properties.Add(new PSNoteProperty("Environment", ValueOrNull(reader["Environment"])));
                        row.Properties.Add(new PSNoteProperty("StatusCode", ValueOrNull(reader["StatusCode"])));
                        // PS: $row.StartTime = [dbadatetime]$row.StartTime.DateTime (the raw
                        // column is datetimeoffset); DbaDateTime is a class, so a NULL
                        // end_time stays null exactly like [dbadatetime]$null.
                        row.Properties.Add(new PSNoteProperty("StartTime", ToDbaDateTime(reader["StartTime"])));
                        row.Properties.Add(new PSNoteProperty("EndTime", ToDbaDateTime(reader["EndTime"])));
                        row.Properties.Add(new PSNoteProperty("ElapsedMinutes", ValueOrNull(reader["ElapsedMinutes"])));
                        row.Properties.Add(new PSNoteProperty("LoggingLevel", ValueOrNull(reader["LoggingLevel"])));
                        WriteObject(row);
                    }
                }
                finally
                {
                    SetActiveCommand(null);
                }
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                WriteMessage(MessageLevel.Warning, $"[{instance}] Failed during execution", target: instance, exception: ex);
            }
        }
    }

    private string BuildValuePredicate(string[]? values, string parameterPrefix, string columnExpression)
    {
        // PS: parameterized collection predicate - AND ( 1=0 OR col = @name1 OR ... )
        if (!FilterHelper.IsActive(values))
        {
            return string.Empty;
        }
        StringBuilder predicate = new();
        predicate.Append("\n\t\tAND ( 1=0 ");
        int ordinal = 0;
        foreach (string value in values!)
        {
            ordinal++;
            string parameterName = parameterPrefix + ordinal.ToString(CultureInfo.InvariantCulture);
            predicate.Append("\n\t\t\tOR " + columnExpression + " = @" + parameterName);
            _queryParameters.Add(new KeyValuePair<string, object>(parameterName, value));
        }
        predicate.Append("\n\t\t)");
        return predicate.ToString();
    }

    private static object? ValueOrNull(object raw)
    {
        return raw is DBNull ? null : raw;
    }

    private static object? ToDbaDateTime(object raw)
    {
        if (raw is DateTimeOffset offsetValue)
        {
            return new DbaDateTime(offsetValue.DateTime);
        }
        return null;
    }
}
