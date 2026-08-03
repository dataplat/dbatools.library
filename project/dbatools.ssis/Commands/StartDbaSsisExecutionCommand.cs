#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;
using Dataplat.Dbatools.Utility;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management.Smo;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// <para type="synopsis">Starts a package in the SSIS catalog.</para>
/// <para type="description">Runs a package that has been deployed to the SSIS catalog (SSISDB), optionally against an environment and with execution parameters, and returns the execution decorated exactly like Get-DbaSsisExecution emits it.</para>
/// <para type="description">Calls catalog.create_execution, then catalog.set_execution_parameter_value once per supplied parameter, then catalog.start_execution - with parameterized T-SQL rather than the Integration Services object model, so it works on both PowerShell editions and on Linux. The execution id the first proc hands back is the only thing that identifies the run: the parameter calls need it, the start needs it, and the caller needs it to poll the run or to stop it later.</para>
/// <para type="description">Without -Synchronous the command returns as soon as the run has been started, so the status is normally Pending or Running. With it, the command polls the catalog until the run reaches a terminal state and a package that ends Failed, Cancelled or Halted is reported as a failure rather than as a success that happens to carry a bad status.</para>
/// <para type="description">-Package is the package name as the catalog stores it, which includes the .dtsx extension.</para>
/// </summary>
/// <example>
///   <code>PS C:\&gt; Start-DbaSsisExecution -SqlInstance sql2019 -Folder Finance -Project Ledger -Package Load.dtsx</code>
///   <para>Starts Load.dtsx and returns the execution without waiting for it.</para>
/// </example>
/// <example>
///   <code>PS C:\&gt; Start-DbaSsisExecution -SqlInstance sql2019 -Folder Finance -Project Ledger -Package Load.dtsx -Environment Production -Synchronous -Timeout 600</code>
///   <para>Runs the package against the Production environment and waits up to ten minutes for it to finish. On expiry the command stops waiting and says so; it does not stop the execution.</para>
/// </example>
/// <example>
///   <code>PS C:\&gt; Start-DbaSsisExecution -SqlInstance sql2019 -Folder Finance -Project Ledger -Package Load.dtsx -Parameter @{ BatchSize = 500 } -ProjectParameter @{ Region = "EU" } -LoggingLevel 3</code>
///   <para>Sets a package parameter, a project parameter and the logging level for this run only. The two hashtables are separate because the catalog discriminates the scopes and a name can exist in both.</para>
/// </example>
[Cmdlet(VerbsLifecycle.Start, "DbaSsisExecution", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Medium)]
[OutputType(typeof(PSObject))]
public sealed class StartDbaSsisExecutionCommand : DbaInstanceCmdlet
{
    // set_execution_parameter_value discriminates the scope on one smallint, so the two hashtables
    // and the logging level are the caller's way of saying which one a name belongs to. Guessing
    // by looking the name up in catalog.object_parameters would break on a project parameter and a
    // package parameter that share a name.
    private const short ProjectParameterScope = 20;
    private const short PackageParameterScope = 30;
    private const short SystemParameterScope = 50;

    // catalog.executions.status; the rest (Created, Running, Pending, Stopping) mean still going.
    private static readonly HashSet<int> TerminalStatuses = new() { 3, 4, 6, 7, 9 };
    private static readonly HashSet<int> FailedStatuses = new() { 3, 4, 6 };

    private const int PollIntervalSeconds = 3;

    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
    public override DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public override PSCredential? SqlCredential { get; set; }

    /// <summary>The SSIS catalog folder the project is deployed to.</summary>
    [Parameter(Mandatory = true, Position = 2)]
    public string Folder { get; set; } = null!;

    /// <summary>The deployed project the package belongs to.</summary>
    [Parameter(Mandatory = true, Position = 3)]
    public string Project { get; set; } = null!;

    /// <summary>The package to run, named as the catalog stores it - including the .dtsx extension.</summary>
    [Parameter(Mandatory = true, Position = 4)]
    public string Package { get; set; } = null!;

    /// <summary>The environment to run against. The project must already reference it; references are what the catalog binds an execution to, not environments directly.</summary>
    [Parameter(Position = 5)]
    public string? Environment { get; set; }

    /// <summary>The folder the environment lives in, when it is not the project's own folder.</summary>
    [Parameter(Position = 6)]
    public string? EnvironmentFolder { get; set; }

    /// <summary>Package-scoped parameter values for this run, as a name/value hashtable.</summary>
    [Parameter(Position = 7)]
    public Hashtable? Parameter { get; set; }

    /// <summary>Project-scoped parameter values for this run, as a name/value hashtable.</summary>
    [Parameter(Position = 8)]
    public Hashtable? ProjectParameter { get; set; }

    /// <summary>The LOGGING_LEVEL system parameter for this run: 0 None, 1 Basic, 2 Performance, 3 Verbose.</summary>
    [Parameter(Position = 9)]
    public int LoggingLevel { get; set; }

    /// <summary>How many seconds to wait with -Synchronous before giving up on the wait. On expiry the command stops waiting; the execution keeps running.</summary>
    [Parameter(Position = 10)]
    public int Timeout { get; set; }

    /// <summary>Waits for the run to reach a terminal state and reports a failed package as a failure.</summary>
    [Parameter]
    public SwitchParameter Synchronous { get; set; }

    /// <summary>Runs the package in the 32-bit runtime.</summary>
    [Parameter]
    public SwitchParameter Use32BitRuntime { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void BeginProcessing()
    {
        // A timeout on a call that never waits is not a harmless no-op: the caller asked for a
        // bound on something this shape of call does not do, so saying nothing would leave them
        // believing the run was waited for and cut short.
        if (TestBound(nameof(Timeout)) && !Synchronous.ToBool())
        {
            StopFunction("-Timeout applies only with -Synchronous: without it the command returns as soon as the run has started and never waits", category: ErrorCategory.InvalidArgument);
        }
    }

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        foreach (DbaInstanceParameter instance in SqlInstance)
        {
            // The SSIS catalog schema arrived in SQL 2012, so an older instance is refused with
            // the version message rather than failing later on a missing catalog schema.
            Server server = ConnectInstance(instance, "Failure", minimumVersion: 11);
            if (server == null)
            {
                continue;
            }

            try
            {
                // ConnectionService hands back a Server whose ConnectionContext may still be
                // lazy; SqlConnectionObject is only usable once it has actually connected.
                if (!server.ConnectionContext.IsOpen)
                {
                    server.ConnectionContext.Connect();
                }

                if (!CatalogExists(server))
                {
                    StopFunction($"No SSIS catalog (SSISDB) found on {instance}", target: instance, continueLoop: true);
                    continue;
                }

                long? referenceId = null;
                if (TestBound(nameof(Environment)))
                {
                    referenceId = ReadReferenceId(server);
                    if (referenceId == null)
                    {
                        // Starting the package anyway would run it with its design-time values and
                        // fail somewhere inside the package, which reads as a package bug rather
                        // than as the missing reference it is.
                        string environmentDescription = TestBound(nameof(EnvironmentFolder)) ? $"{EnvironmentFolder}\\{Environment}" : Environment!;
                        StopFunction($"Project {Project} in folder {Folder} on {instance} has no reference to environment {environmentDescription}; create the reference before running the package against it", target: instance, continueLoop: true);
                        continue;
                    }
                }

                string target = Dataplat.Dbatools.Connection.SmoServerExtensions.GetDomainInstanceName(server);
                if (!ShouldProcess(target, $"Starting SSIS package {Package} from project {Project} in folder {Folder}"))
                {
                    continue;
                }

                long executionId;
                try
                {
                    executionId = CreateExecution(server, referenceId);
                    SetParameterValues(server, executionId);
                    StartExecution(server, executionId);
                }
                catch (PipelineStoppedException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    StopFunction($"Failure starting SSIS package {Package} from project {Project} in folder {Folder} on {instance}", target: instance, exception: ex, continueLoop: true);
                    continue;
                }

                if (Synchronous.ToBool() && !WaitForCompletion(server, instance, executionId))
                {
                    continue;
                }

                EmitExecution(server, executionId);
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                StopFunction($"Failure starting SSIS packages on {instance}", target: instance, exception: ex, continueLoop: true);
            }
        }
    }

    /// <summary>
    /// Presence is asked of the instance rather than of SMO's Databases collection: enumerating
    /// every database to answer a one-name question drags in whatever state the other databases
    /// are in, and a single-user database is enough to poison the shared connection.
    /// </summary>
    private bool CatalogExists(Server server)
    {
        using SqlCommand presence = new("SELECT DB_ID('SSISDB')", server.ConnectionContext.SqlConnectionObject);
        SetActiveCommand(presence);
        try
        {
            object? result = presence.ExecuteScalar();
            return result != null && result is not DBNull;
        }
        finally
        {
            SetActiveCommand(null);
        }
    }

    /// <summary>
    /// An execution binds to a reference, not to an environment: a project has to reference an
    /// environment before it can run against it, and the same environment name can be referenced
    /// out of two folders, which is what -EnvironmentFolder disambiguates.
    /// </summary>
    private long? ReadReferenceId(Server server)
    {
        string sql = "SELECT TOP 1 references_.reference_id FROM [SSISDB].[catalog].[environment_references] references_ JOIN [SSISDB].[catalog].[projects] projects ON projects.project_id = references_.project_id JOIN [SSISDB].[catalog].[folders] folders ON folders.folder_id = projects.folder_id WHERE folders.name = @folderName AND projects.name = @projectName AND references_.environment_name = @environmentName";
        if (TestBound(nameof(EnvironmentFolder)))
        {
            sql += " AND references_.environment_folder_name = @environmentFolderName";
        }

        using SqlCommand command = new(sql, server.ConnectionContext.SqlConnectionObject);
        command.Parameters.AddWithValue("@folderName", Folder);
        command.Parameters.AddWithValue("@projectName", Project);
        command.Parameters.AddWithValue("@environmentName", Environment);
        if (TestBound(nameof(EnvironmentFolder)))
        {
            command.Parameters.AddWithValue("@environmentFolderName", EnvironmentFolder);
        }

        SetActiveCommand(command);
        try
        {
            object? result = command.ExecuteScalar();
            return result == null || result is DBNull ? null : Convert.ToInt64(result);
        }
        finally
        {
            SetActiveCommand(null);
        }
    }

    private long CreateExecution(Server server, long? referenceId)
    {
        using SqlCommand command = new("EXEC [SSISDB].[catalog].[create_execution] @folder_name = @folderName, @project_name = @projectName, @package_name = @packageName, @reference_id = @referenceId, @use32bitruntime = @use32BitRuntime, @execution_id = @executionId OUTPUT", server.ConnectionContext.SqlConnectionObject);
        command.Parameters.AddWithValue("@folderName", Folder);
        command.Parameters.AddWithValue("@projectName", Project);
        command.Parameters.AddWithValue("@packageName", Package);
        command.Parameters.AddWithValue("@referenceId", (object?)referenceId ?? DBNull.Value);
        command.Parameters.AddWithValue("@use32BitRuntime", Use32BitRuntime.ToBool());
        SqlParameter executionIdParameter = command.Parameters.Add("@executionId", System.Data.SqlDbType.BigInt);
        executionIdParameter.Direction = System.Data.ParameterDirection.Output;

        SetActiveCommand(command);
        try
        {
            command.ExecuteNonQuery();
        }
        finally
        {
            SetActiveCommand(null);
        }

        return Convert.ToInt64(executionIdParameter.Value);
    }

    private void SetParameterValues(Server server, long executionId)
    {
        if (Parameter != null)
        {
            foreach (DictionaryEntry entry in Parameter)
            {
                SetParameterValue(server, executionId, PackageParameterScope, Convert.ToString(entry.Key)!, entry.Value);
            }
        }

        if (ProjectParameter != null)
        {
            foreach (DictionaryEntry entry in ProjectParameter)
            {
                SetParameterValue(server, executionId, ProjectParameterScope, Convert.ToString(entry.Key)!, entry.Value);
            }
        }

        if (TestBound(nameof(LoggingLevel)))
        {
            SetParameterValue(server, executionId, SystemParameterScope, "LOGGING_LEVEL", LoggingLevel);
        }
    }

    private void SetParameterValue(Server server, long executionId, short scope, string parameterName, object? parameterValue)
    {
        using SqlCommand command = new("EXEC [SSISDB].[catalog].[set_execution_parameter_value] @execution_id = @executionId, @object_type = @objectType, @parameter_name = @parameterName, @parameter_value = @parameterValue", server.ConnectionContext.SqlConnectionObject);
        command.Parameters.AddWithValue("@executionId", executionId);
        command.Parameters.AddWithValue("@objectType", scope);
        command.Parameters.AddWithValue("@parameterName", parameterName);
        command.Parameters.AddWithValue("@parameterValue", Unwrap(parameterValue) ?? DBNull.Value);

        SetActiveCommand(command);
        try
        {
            command.ExecuteNonQuery();
        }
        finally
        {
            SetActiveCommand(null);
        }
    }

    private void StartExecution(Server server, long executionId)
    {
        using SqlCommand command = new("EXEC [SSISDB].[catalog].[start_execution] @execution_id = @executionId", server.ConnectionContext.SqlConnectionObject);
        command.Parameters.AddWithValue("@executionId", executionId);

        SetActiveCommand(command);
        try
        {
            command.ExecuteNonQuery();
        }
        finally
        {
            SetActiveCommand(null);
        }
    }

    /// <summary>
    /// Polls rather than setting the SYNCHRONIZED system parameter, which would make
    /// start_execution itself block for the whole package run with no client-side way to give up -
    /// and no way to implement -Timeout at all.
    /// </summary>
    /// <returns>False when the caller should not be handed an execution: the wait expired, or the
    /// package ended in a failing state, both of which are reported here.</returns>
    private bool WaitForCompletion(Server server, DbaInstanceParameter instance, long executionId)
    {
        DateTime deadline = TestBound(nameof(Timeout)) ? DateTime.UtcNow.AddSeconds(Timeout) : DateTime.MaxValue;

        while (true)
        {
            int status = ReadStatus(server, executionId);
            if (TerminalStatuses.Contains(status))
            {
                if (!FailedStatuses.Contains(status))
                {
                    return true;
                }

                // The status alone names no cause, and the catalog's own log is where the cause
                // is. A caller told only "Failed" has to go and find this view themselves.
                string reason = ReadOperationMessages(server, executionId);
                string detail = reason.Length == 0 ? string.Empty : $": {reason}";
                StopFunction($"SSIS package {Package} from project {Project} in folder {Folder} on {instance} finished as {StatusName(status)} (execution {executionId}){detail}", target: instance, continueLoop: true);
                return false;
            }

            if (DateTime.UtcNow >= deadline)
            {
                // Stopping the run would be a destructive act the caller did not ask for; that is
                // Stop-DbaSsisExecution's job. The execution id is in the message so they can.
                StopFunction($"Timed out after {Timeout} seconds waiting for SSIS package {Package} from project {Project} in folder {Folder} on {instance}; execution {executionId} is still {StatusName(ReadStatus(server, executionId))} and has NOT been stopped", target: instance, continueLoop: true);
                return false;
            }

            // Sleeping through the cmdlet's own cancellation check keeps Ctrl-C responsive during
            // a long wait; a bare Thread.Sleep would swallow it for the whole interval.
            for (int elapsed = 0; elapsed < PollIntervalSeconds; elapsed++)
            {
                if (Stopping)
                {
                    throw new PipelineStoppedException();
                }
                System.Threading.Thread.Sleep(1000);
            }
        }
    }

    private int ReadStatus(Server server, long executionId)
    {
        using SqlCommand command = new("SELECT status FROM [SSISDB].[catalog].[executions] WHERE execution_id = @executionId", server.ConnectionContext.SqlConnectionObject);
        command.Parameters.AddWithValue("@executionId", executionId);

        SetActiveCommand(command);
        try
        {
            object? result = command.ExecuteScalar();
            return result == null || result is DBNull ? 0 : Convert.ToInt32(result);
        }
        finally
        {
            SetActiveCommand(null);
        }
    }

    private static string StatusName(int status)
    {
        return status switch
        {
            1 => "Created",
            2 => "Running",
            3 => "Cancelled",
            4 => "Failed",
            5 => "Pending",
            6 => "Halted",
            7 => "Succeeded",
            8 => "Stopping",
            9 => "Completed",
            _ => "Unknown"
        };
    }

    /// <summary>
    /// The failure is the news; losing the explanation must not replace it with a failure to read
    /// the explanation, so a read that goes wrong here reports nothing rather than throwing.
    /// </summary>
    private string ReadOperationMessages(Server server, long executionId)
    {
        try
        {
            using SqlCommand command = new("SELECT TOP 10 message FROM [SSISDB].[catalog].[operation_messages] WHERE operation_id = @executionId AND message_type = 120 ORDER BY operation_message_id", server.ConnectionContext.SqlConnectionObject);
            command.Parameters.AddWithValue("@executionId", executionId);

            List<string> messages = new();
            SetActiveCommand(command);
            try
            {
                using SqlDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    if (reader["message"] is string message)
                    {
                        messages.Add(message);
                    }
                }
            }
            finally
            {
                SetActiveCommand(null);
            }

            return string.Join(" | ", messages);
        }
        catch (PipelineStoppedException)
        {
            throw;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// The started execution is re-emitted in Get-DbaSsisExecution's shape and decorated
    /// identically, so Start -&gt; Get pipelines compose and the status names are the same words in
    /// both commands.
    /// </summary>
    private void EmitExecution(Server server, long executionId)
    {
        const string sql = @"
        WITH
            cteLoglevel AS (
                SELECT
                    execution_id AS ExecutionID,
                    CAST(parameter_value AS INT) AS LoggingLevel
                FROM
                    [SSISDB].[catalog].[execution_parameter_values]
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
                    , e.start_time AS StartTime
                    , e.end_time AS EndTime
                    , ElapsedMinutes = DATEDIFF(mi, e.start_time, e.end_time)
                    , l.LoggingLevel
                    , e.reference_id AS ReferenceId
                    , e.reference_type AS ReferenceType
                    , e.environment_folder_name AS EnvironmentFolderName
                    , e.environment_name AS EnvironmentName
                    , e.executed_as_name AS ExecutedAsName
                    , e.use32bitruntime AS Use32BitRuntime
                    , e.operation_type AS OperationType
                    , e.created_time AS CreatedTime
                    , e.object_type AS ObjectType
                    , e.object_id AS ObjectId
                    , e.status AS Status
                    , e.caller_name AS CallerName
                    , e.process_id AS ProcessId
                    , e.stopped_by_name AS StoppedByName
                    , e.dump_id AS DumpId
                    , e.server_name AS ServerName
                    , e.machine_name AS MachineName
                    , e.worker_agent_id AS WorkerAgentId
                    , e.total_physical_memory_kb AS TotalPhysicalMemoryKb
                    , e.available_physical_memory_kb AS AvailablePhysicalMemoryKb
                    , e.total_page_file_kb AS TotalPageFileKb
                    , e.available_page_file_kb AS AvailablePageFileKb
                    , e.cpu_count AS CpuCount
                    , e.executed_count AS ExecutedCount
            FROM
                [SSISDB].[catalog].[executions] e
                LEFT OUTER JOIN cteLoglevel l
                    ON e.execution_id = l.ExecutionID
                LEFT OUTER JOIN cteStatus s
                    ON s.[key] = e.status
            WHERE e.execution_id = @executionId
            OPTION  ( RECOMPILE );
        ";

        using SqlCommand command = new(sql, server.ConnectionContext.SqlConnectionObject);
        command.Parameters.AddWithValue("@executionId", executionId);

        SetActiveCommand(command);
        try
        {
            using SqlDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                PSObject execution = new();
                OutputHelper.AddInstanceProperties(execution, server);
                for (int ordinal = 0; ordinal < reader.FieldCount; ordinal++)
                {
                    string name = reader.GetName(ordinal);
                    object raw = reader.GetValue(ordinal);
                    object? value = name is "StartTime" or "EndTime" or "CreatedTime" ? ToDbaDateTime(raw) : ValueOrNull(raw);
                    execution.Properties.Add(new PSNoteProperty(name, value));
                }
                OutputHelper.InsertTypeName(execution, "SsisExecution");
                OutputHelper.SetDefaultDisplayPropertySet(execution, "ComputerName", "InstanceName", "SqlInstance", "ExecutionID", "FolderName", "ProjectName", "PackageName", "StatusCode", "StartTime", "EndTime", "ElapsedMinutes");
                WriteObject(execution);
            }
        }
        finally
        {
            SetActiveCommand(null);
        }
    }

    private static object? Unwrap(object? value)
    {
        // PowerShell hands hashtable values over wrapped often enough that unwrapping is not
        // optional; SqlClient cannot bind a PSObject to a sql_variant.
        return value is PSObject wrapper ? wrapper.BaseObject : value;
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
        if (raw is DateTime dateTimeValue)
        {
            return new DbaDateTime(dateTimeValue);
        }
        return null;
    }
}
