#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves configured SQL Agent job-step output files and remote paths. The complete filtering
/// and output-shaping workflow remains a module-scoped PowerShell compatibility hop. Surface
/// pinned by migration/baselines/Get-DbaAgentJobOutputFile.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaAgentJobOutputFile")]
public sealed class GetDbaAgentJobOutputFileCommand : DbaBaseCmdlet
{
    /// <summary>Target SQL Server instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true, Position = 0)]
    [ValidateNotNull]
    [ValidateNotNullOrEmpty]
    public DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(ValueFromPipelineByPropertyName = true, Position = 1)]
    public PSCredential? SqlCredential { get; set; }

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

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BodyScript,
            SqlInstance, SqlCredential, Job, ExcludeJob, EnableException.ToBool(),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
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

    private object? BoundCommonParameter(string name)
    {
        if (MyInvocation.BoundParameters.TryGetValue(name, out object? value))
            return LanguagePrimitives.IsTrue(value);
        return null;
    }

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
param($SqlInstance, $SqlCredential, $Job, $ExcludeJob, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, $SqlCredential, [object[]]$Job, [object[]]$ExcludeJob, $EnableException)

    foreach ($instance in $SqlInstance) {
        try {
            $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaAgentJobOutputFile
        }
        $jobs = $server.JobServer.Jobs
        if ($Job) {
            $jobs = $jobs | Where-Object Name -In $Job
        }
        if ($ExcludeJob) {
            $jobs = $jobs | Where-Object Name -NotIn $ExcludeJob
        }
        foreach ($j in $jobs) {
            foreach ($step in $j.JobSteps) {
                if ($step.OutputFileName) {
                    [PSCustomObject]@{
                        ComputerName         = $server.ComputerName
                        InstanceName         = $server.ServiceName
                        SqlInstance          = $server.DomainInstanceName
                        Job                  = $j.Name
                        JobStep              = $step.Name
                        OutputFileName       = $step.OutputFileName
                        RemoteOutputFileName = Join-AdminUNC $server.ComputerName $step.OutputFileName
                        StepId               = $step.Id
                    } | Select-DefaultView -ExcludeProperty StepId
                } else {
                    Write-Message -Level Verbose -Message "$step for $j has no output file" -FunctionName Get-DbaAgentJobOutputFile
                }
            }
        }
    }
} $SqlInstance $SqlCredential $Job $ExcludeJob $EnableException @__commonParameters 3>&1 2>&1
""";
}
