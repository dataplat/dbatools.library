#nullable enable

using System;
using System.Globalization;
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
/// Retrieves configured SQL Agent job-step output files and remote paths. Surface pinned by
/// migration/baselines/Get-DbaAgentJobOutputFile.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaAgentJobOutputFile")]
public sealed class GetDbaAgentJobOutputFileCommand : DbaInstanceCmdlet
{
    /// <summary>Target SQL Server instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true, Position = 0)]
    [PsDbaInstanceArrayCast]
    [ValidateNotNullOrEmpty]
    public override DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(ValueFromPipelineByPropertyName = true, Position = 1)]
    public override PSCredential? SqlCredential { get; set; }

    /// <summary>Job names to include.</summary>
    [Parameter]
    public object[]? Job { get; set; }

    /// <summary>Job names to exclude.</summary>
    [Parameter]
    public object[]? ExcludeJob { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        foreach (DbaInstanceParameter instance in SqlInstance)
        {
            if (Interrupted)
                return;

            Server? server = ConnectInstance(instance, "Failure");
            if (server is null)
                continue;

            foreach (AgentJob job in server.JobServer.Jobs)
            {
                if (Interrupted)
                    return;
                if (FilterHelper.IsActive(Job) && !Contains(Job!, job.Name))
                    continue;
                if (FilterHelper.IsActive(ExcludeJob) && Contains(ExcludeJob!, job.Name))
                    continue;

                foreach (JobStep step in job.JobSteps)
                {
                    if (Interrupted)
                        return;

                    if (!string.IsNullOrEmpty(step.OutputFileName))
                    {
                        PSObject result = new();
                        result.Properties.Add(new PSNoteProperty("ComputerName", SmoServerExtensions.GetComputerName(server)));
                        result.Properties.Add(new PSNoteProperty("InstanceName", server.ServiceName));
                        result.Properties.Add(new PSNoteProperty("SqlInstance", SmoServerExtensions.GetDomainInstanceName(server)));
                        result.Properties.Add(new PSNoteProperty("Job", job.Name));
                        result.Properties.Add(new PSNoteProperty("JobStep", step.Name));
                        result.Properties.Add(new PSNoteProperty("OutputFileName", step.OutputFileName));
                        result.Properties.Add(new PSNoteProperty("RemoteOutputFileName", JoinAdminUnc(SmoServerExtensions.GetComputerName(server), step.OutputFileName)));
                        result.Properties.Add(new PSNoteProperty("StepId", step.ID));
                        OutputHelper.SetDefaultDisplayPropertySet(result,
                            "ComputerName", "InstanceName", "Job", "JobStep", "OutputFileName",
                            "RemoteOutputFileName", "SqlInstance");
                        WriteObject(result);
                    }
                    else
                    {
                        WriteMessage(MessageLevel.Verbose, $"{step} for {job} has no output file");
                    }
                }
            }
        }
    }

    private static bool Contains(object[] values, string name)
    {
        foreach (object? value in values)
        {
            if (LanguagePrimitives.Equals(name, value, true, CultureInfo.InvariantCulture))
                return true;
        }
        return false;
    }

    private static string JoinAdminUnc(string? serverName, string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || filePath.StartsWith("\\\\", StringComparison.Ordinal) ||
            Environment.OSVersion.Platform != PlatformID.Win32NT)
            return filePath;

        string host = (serverName ?? string.Empty).Split('\\')[0];
        // The PS source is Join-Path "\\server\" (filePath with : -> $), which normalizes forward
        // slashes to backslashes and collapses the single duplicate separator at the parent/child
        // boundary. Replicate both so a root-relative ("\logs\x") or forward-slash ("/logs/x")
        // output-file path yields "\\server\logs\x" rather than a malformed UNC.
        string child = filePath.Replace(':', '$').Replace('/', '\\');
        if (child.Length > 0 && child[0] == '\\')
        {
            child = child.Substring(1);
        }
        return "\\\\" + host + "\\" + child;
    }
}

/// <summary>Reproduces an advanced function's typed-array conversion before validation.</summary>
internal sealed class PsDbaInstanceArrayCastAttribute : ArgumentTransformationAttribute
{
    public override object? Transform(EngineIntrinsics engineIntrinsics, object? inputData)
    {
        if (inputData is null)
            return null;

        try
        {
            return LanguagePrimitives.ConvertTo(inputData, typeof(DbaInstanceParameter[]), CultureInfo.InvariantCulture);
        }
        catch (PSInvalidCastException ex)
        {
            throw new ArgumentTransformationMetadataException(ex.Message, ex);
        }
    }
}
