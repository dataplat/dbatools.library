#nullable enable

using System;
using System.Collections.Generic;
using System.Data;
using System.Management.Automation;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;
using Dataplat.Dbatools.Utility;
using Microsoft.SqlServer.Management.Smo;
using Microsoft.SqlServer.Management.Smo.Agent;
using AgentJob = Microsoft.SqlServer.Management.Smo.Agent.Job;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves SQL Agent jobs with filtering by name, database, category, type and enabled state.
/// Port of public/Get-DbaAgentJob.ps1; surface pinned by migration/baselines/Get-DbaAgentJob.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaAgentJob")]
[OutputType(typeof(AgentJob))]
public sealed class GetDbaAgentJobCommand : DbaInstanceCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public override DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public override PSCredential? SqlCredential { get; set; }

    /// <summary>Returns only the named jobs.</summary>
    [Parameter(Position = 2)]
    public string[]? Job { get; set; }

    /// <summary>Excludes the named jobs.</summary>
    [Parameter(Position = 3)]
    public string[]? ExcludeJob { get; set; }

    /// <summary>Returns only jobs with a step targeting one of the named databases.</summary>
    [Parameter(Position = 4)]
    public string[]? Database { get; set; }

    /// <summary>Returns only jobs in the named categories.</summary>
    [Parameter(Position = 5)]
    public string[]? Category { get; set; }

    /// <summary>Excludes jobs in the named categories.</summary>
    [Parameter(Position = 6)]
    public string[]? ExcludeCategory { get; set; }

    /// <summary>Excludes disabled jobs from the results.</summary>
    [Parameter]
    public SwitchParameter ExcludeDisabledJobs { get; set; }

    /// <summary>Adds a StartDate property for currently executing jobs.</summary>
    [Parameter]
    public SwitchParameter IncludeExecution { get; set; }

    /// <summary>Filters by job type.</summary>
    [Parameter(Position = 7)]
    [ValidateSet("MultiServer", "Local")]
    public string[] Type { get; set; } = new[] { "MultiServer", "Local" };

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        foreach (DbaInstanceParameter instance in SqlInstance)
        {
            // PS: try { Connect-DbaInstance } catch { Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue }
            Server? server = ConnectInstance(instance, "Failure");
            if (server is null)
            {
                continue;
            }

            DataTable? jobExecutionResults = null;
            if (TestBound(nameof(IncludeExecution)))
            {
                const string query = @"SELECT [job].[job_id] AS [JobId], [activity].[start_execution_date] AS [StartDate]
                FROM [msdb].[dbo].[sysjobs_view] AS [job]
                    INNER JOIN [msdb].[dbo].[sysjobactivity] AS [activity] ON [job].[job_id] = [activity].[job_id]
                WHERE [activity].[run_requested_date] IS NOT NULL
                    AND [activity].[start_execution_date] IS NOT NULL
                    AND [activity].[stop_execution_date] IS NULL;";

                DataSet resultSet = server.ConnectionContext.ExecuteWithResults(query);
                if (resultSet.Tables.Count > 0)
                {
                    jobExecutionResults = resultSet.Tables[0];
                }
            }

            // Check if Job parameter is bound with null, empty, or whitespace-only values
            string[]? jobFilter = Job;
            if (TestBound(nameof(Job)))
            {
                // Filter out any null/empty/whitespace values
                jobFilter = FilterBlank(Job);

                // If all values were null/empty/whitespace, skip processing
                if (jobFilter.Length == 0)
                {
                    WriteMessage(MessageLevel.Verbose, "The -Job parameter was explicitly provided but contains only null, empty, or whitespace values. No jobs will be returned.");
                    continue;
                }
            }

            // Check if ExcludeJob parameter is bound with null, empty, or whitespace-only values
            string[]? excludeJobFilter = ExcludeJob;
            if (TestBound(nameof(ExcludeJob)))
            {
                // Filter out any null/empty/whitespace values
                excludeJobFilter = FilterBlank(ExcludeJob);

                // If all values were null/empty/whitespace, ignore the parameter
                if (excludeJobFilter.Length == 0)
                {
                    WriteMessage(MessageLevel.Verbose, "The -ExcludeJob parameter was explicitly provided but contains only null, empty, or whitespace values. Parameter will be ignored.");
                    excludeJobFilter = null;
                }
            }

            foreach (AgentJob agentJob in server.JobServer.Jobs)
            {
                if (!ContainsName(Type, agentJob.JobType.ToString()))
                {
                    continue;
                }
                if (jobFilter is { Length: > 0 } && !ContainsName(jobFilter, agentJob.Name))
                {
                    continue;
                }
                if (excludeJobFilter is { Length: > 0 } && ContainsName(excludeJobFilter, agentJob.Name))
                {
                    continue;
                }
                if (ExcludeDisabledJobs.ToBool() && !agentJob.IsEnabled)
                {
                    continue;
                }
                if (Database is { Length: > 0 } && !HasStepInDatabase(agentJob, Database))
                {
                    continue;
                }
                if (Category is { Length: > 0 } && !ContainsName(Category, agentJob.Category))
                {
                    continue;
                }
                if (ExcludeCategory is { Length: > 0 } && ContainsName(ExcludeCategory, agentJob.Category))
                {
                    continue;
                }

                List<string> defaults = new()
                {
                    "ComputerName", "InstanceName", "SqlInstance", "Name", "Category", "OwnerLoginName",
                    "CurrentRunStatus", "CurrentRunRetryAttempt", "Enabled", "LastRunDate", "LastRunOutcome",
                    "HasSchedule", "OperatorToEmail", "CreateDate"
                };

                PSObject wrapped = PSObject.AsPSObject(agentJob);

                // JobId is adapter-only on SMO Job (same class of property as DomainInstanceName)
                Guid currentJobId = SmoServerExtensions.GetPSProperty(agentJob, "JobId") is Guid adapterJobId ? adapterJobId : Guid.Empty;
                DateTime? startDate = FindLatestStartDate(jobExecutionResults, currentJobId);
                if (startDate is not null)
                {
                    ReplaceNoteProperty(wrapped, "StartDate", new DbaDateTime(startDate.Value));
                    defaults.Add("StartDate");
                }

                ReplaceNoteProperty(wrapped, "ComputerName", SmoServerExtensions.GetComputerName(server));
                ReplaceNoteProperty(wrapped, "InstanceName", server.ServiceName);
                ReplaceNoteProperty(wrapped, "SqlInstance", SmoServerExtensions.GetDomainInstanceName(server));

                // PS: Select-DefaultView -Property ... 'IsEnabled as Enabled' ... 'DateCreated as CreateDate'
                AddAliasIfMissing(wrapped, "Enabled", "IsEnabled");
                AddAliasIfMissing(wrapped, "CreateDate", "DateCreated");
                OutputHelper.SetDefaultDisplayPropertySet(wrapped, defaults.ToArray());

                WriteObject(wrapped);
            }
        }
    }

    private static string[] FilterBlank(string[]? values)
    {
        List<string> kept = new();
        foreach (string value in values ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                kept.Add(value);
            }
        }
        return kept.ToArray();
    }

    private static bool ContainsName(string[] values, string? name)
    {
        foreach (string value in values)
        {
            if (string.Equals(value, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasStepInDatabase(AgentJob agentJob, string[] databases)
    {
        foreach (JobStep step in agentJob.JobSteps)
        {
            if (ContainsName(databases, step.DatabaseName))
            {
                return true;
            }
        }
        return false;
    }

    private static DateTime? FindLatestStartDate(DataTable? jobExecutionResults, Guid jobId)
    {
        if (jobExecutionResults is null)
        {
            return null;
        }
        DateTime? latest = null;
        foreach (DataRow row in jobExecutionResults.Rows)
        {
            if (row["JobId"] is Guid rowJobId && rowJobId == jobId && row["StartDate"] is DateTime rowStart)
            {
                if (latest is null || rowStart > latest.Value)
                {
                    latest = rowStart;
                }
            }
        }
        return latest;
    }

    private static void AddAliasIfMissing(PSObject wrapped, string aliasName, string referencedName)
    {
        if (wrapped.Properties[aliasName] is null)
        {
            OutputHelper.AddAliasProperty(wrapped, aliasName, referencedName);
        }
    }

    private static void ReplaceNoteProperty(PSObject wrapped, string name, object? value)
    {
        PSPropertyInfo? existing = wrapped.Properties[name];
        if (existing is PSNoteProperty)
        {
            wrapped.Properties.Remove(name);
        }
        wrapped.Properties.Add(new PSNoteProperty(name, value));
    }
}
