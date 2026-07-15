#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;
using SmoAgentJob = Microsoft.SqlServer.Management.Smo.Agent.Job;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Synchronizes SQL Agent job steps while preserving the destination job. Port of
/// public/Copy-DbaAgentJobStep.ps1 (W2-004). The complete synchronization workflow remains a
/// module-scoped PowerShell compatibility hop; the compiled cmdlet preserves the advanced
/// function's begin/process pipeline lifetime and supplies the real ShouldProcess runtime.
/// Surface pinned by migration/baselines/Copy-DbaAgentJobStep.json.
/// </summary>
[Cmdlet(VerbsCommon.Copy, "DbaAgentJobStep", DefaultParameterSetName = "Default",
    SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Medium)]
public sealed class CopyDbaAgentJobStepCommand : DbaBaseCmdlet
{
    /// <summary>Source SQL Server instance.</summary>
    [Parameter(Position = 0)]
    public DbaInstanceParameter? Source { get; set; }

    /// <summary>Alternative credential for the source instance.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SourceSqlCredential { get; set; }

    /// <summary>Destination SQL Server instances.</summary>
    [Parameter(Mandatory = true, Position = 2)]
    public DbaInstanceParameter[] Destination { get; set; } = null!;

    /// <summary>Alternative credential for destination instances.</summary>
    [Parameter(Position = 3)]
    public PSCredential? DestinationSqlCredential { get; set; }

    /// <summary>Only synchronize jobs with these names.</summary>
    [Parameter(Position = 4)]
    public object[]? Job { get; set; }

    /// <summary>Exclude jobs with these names.</summary>
    [Parameter(Position = 5)]
    public object[]? ExcludeJob { get; set; }

    /// <summary>Only synchronize steps with these names.</summary>
    [Parameter(Position = 6)]
    public string[]? Step { get; set; }

    /// <summary>SQL Agent jobs supplied directly or through the pipeline.</summary>
    [Parameter(ValueFromPipeline = true, Position = 7)]
    public SmoAgentJob[]? InputObject { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    private readonly List<SmoAgentJob> _beginJobs = new();
    private bool _beginInterrupted;
    private bool _inputObjectBoundAtBegin;
    private bool _boundJob;
    private bool _boundExcludeJob;
    private bool _boundStep;

    protected override void BeginProcessing()
    {
        _inputObjectBoundAtBegin = TestBound("InputObject");
        _boundJob = TestBound("Job");
        _boundExcludeJob = TestBound("ExcludeJob");
        _boundStep = TestBound("Step");

        bool completed = false;
        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BeginScript,
            Source, SourceSqlCredential, Job, ExcludeJob, InputObject,
            EnableException.ToBool(), _boundJob, _boundExcludeJob,
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
            }
            else if (item?.BaseObject is SmoAgentJob job)
            {
                _beginJobs.Add(job);
            }
            else if (item is not null && LanguagePrimitives.IsTrue(
                item.Properties["__CopyDbaAgentJobStepBeginComplete"]?.Value))
            {
                completed = true;
            }
            else if (item is not null)
            {
                WriteObject(item);
            }
        }
        _beginInterrupted = !completed;
    }

    protected override void ProcessRecord()
    {
        if (_beginInterrupted)
            return;

        SmoAgentJob[] jobs = !_inputObjectBoundAtBegin && InputObject is not null
            ? InputObject
            : _beginJobs.ToArray();

        NestedCommand.InvokeScopedStreaming(this, item =>
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
        }, ProcessScript,
            Destination, DestinationSqlCredential, Job, ExcludeJob, Step, jobs,
            EnableException.ToBool(), this, _boundJob, _boundExcludeJob, _boundStep,
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug"));
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

    private const string BeginScript = """
param($Source, $SourceSqlCredential, $Job, $ExcludeJob, $InputObject, $EnableException, $__boundJob, $__boundExcludeJob, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter]$Source, $SourceSqlCredential, [object[]]$Job, [object[]]$ExcludeJob, [Microsoft.SqlServer.Management.Smo.Agent.Job[]]$InputObject, $EnableException, $__boundJob, $__boundExcludeJob, $__boundVerbose, $__boundDebug)

    if ($Source) {
        try {
            $splatGetJob = @{
                SqlInstance   = $Source
                SqlCredential = $SourceSqlCredential
            }
            if ($__boundJob) { $splatGetJob["Job"] = $Job }
            if ($__boundExcludeJob) { $splatGetJob["ExcludeJob"] = $ExcludeJob }
            $InputObject = Get-DbaAgentJob @splatGetJob
        } catch {
            Stop-Function -Message "Error occurred while establishing connection to $Source" -Category ConnectionError -ErrorRecord $_ -Target $Source -FunctionName Copy-DbaAgentJobStep
            return
        }
    }
    $InputObject
    [pscustomobject]@{ __CopyDbaAgentJobStepBeginComplete = $true }
} $Source $SourceSqlCredential $Job $ExcludeJob $InputObject $EnableException $__boundJob $__boundExcludeJob $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";

    private const string ProcessScript = """
param($Destination, $DestinationSqlCredential, $Job, $ExcludeJob, $Step, $InputObject, $EnableException, $__realCmdlet, $__boundJob, $__boundExcludeJob, $__boundStep, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$Destination, $DestinationSqlCredential, [object[]]$Job, [object[]]$ExcludeJob, [string[]]$Step, [Microsoft.SqlServer.Management.Smo.Agent.Job[]]$InputObject, $EnableException, $__realCmdlet, $__boundJob, $__boundExcludeJob, $__boundStep, $__boundVerbose, $__boundDebug)

    if (Test-FunctionInterrupt) { return }
    foreach ($destinstance in $Destination) {
        try {
            $destServer = Connect-DbaInstance -SqlInstance $destinstance -SqlCredential $DestinationSqlCredential
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $destinstance -Continue -FunctionName Copy-DbaAgentJobStep
        }
        $destJobs = $destServer.JobServer.Jobs

        foreach ($sourceJob in $InputObject) {
            $jobName = $sourceJob.Name
            $sourceserver = $sourceJob.Parent.Parent

            $copyJobStepStatus = [PSCustomObject]@{
                SourceServer      = $sourceserver.Name
                DestinationServer = $destServer.Name
                Name              = $jobName
                Type              = "Agent Job Steps"
                Status            = $null
                Notes             = $null
                DateTime          = [Dataplat.Dbatools.Utility.DbaDateTime](Get-Date)
            }

            if ($__boundJob -and $jobName -notin $Job) {
                Write-Message -Level Verbose -Message "Job [$jobName] filtered. Skipping." -FunctionName Copy-DbaAgentJobStep
                continue
            }
            if ($__boundExcludeJob -and $jobName -in $ExcludeJob) {
                Write-Message -Level Verbose -Message "Job [$jobName] excluded. Skipping." -FunctionName Copy-DbaAgentJobStep
                continue
            }
            Write-Message -Message "Working on job: $jobName" -Level Verbose -FunctionName Copy-DbaAgentJobStep

            if ($destJobs.name -notcontains $sourceJob.name) {
                if ($__realCmdlet.ShouldProcess($destinstance, "Job $jobName does not exist on destination. Skipping step synchronization.")) {
                    $copyJobStepStatus.Status = "Skipped"
                    $copyJobStepStatus.Notes = "Job does not exist on destination. Use Copy-DbaAgentJob to create it first."
                    $copyJobStepStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                    Write-Message -Level Warning -Message "Job $jobName does not exist on destination $destinstance. Use Copy-DbaAgentJob to create it first." -FunctionName Copy-DbaAgentJobStep
                }
                continue
            }

            $sourceSteps = $sourceJob.JobSteps
            if ($__boundStep) {
                $sourceSteps = $sourceSteps | Where-Object Name -in $Step
                if (-not $sourceSteps) {
                    Write-Message -Level Warning -Message "No matching steps found in job $jobName for specified step names: $($Step -join ', ')" -FunctionName Copy-DbaAgentJobStep
                    continue
                }
            }

            if ($__realCmdlet.ShouldProcess($destinstance, "Synchronizing steps for job $jobName")) {
                try {
                    $destJob = $destServer.JobServer.Jobs[$jobName]
                    $stepsToRemove = @($destJob.JobSteps | ForEach-Object { $_ })
                    if ($__boundStep) {
                        $stepsToRemove = $stepsToRemove | Where-Object Name -in $Step
                    }

                    Write-Message -Message "Removing $($stepsToRemove.Count) existing step(s) from $jobName on $destinstance" -Level Verbose -FunctionName Copy-DbaAgentJobStep
                    foreach ($stepToRemove in $stepsToRemove) {
                        Write-Message -Message "Removing step $($stepToRemove.Name) from $jobName on $destinstance" -Level Verbose -FunctionName Copy-DbaAgentJobStep
                        $stepToRemove.Drop()
                    }
                    $destJob.JobSteps.Refresh()

                    Write-Message -Message "Copying $($sourceSteps.Count) step(s) from $jobName to $destinstance" -Level Verbose -FunctionName Copy-DbaAgentJobStep
                    foreach ($sourceStep in $sourceSteps) {
                        Write-Message -Message "Creating step $($sourceStep.Name) in $jobName on $destinstance" -Level Verbose -FunctionName Copy-DbaAgentJobStep
                        $sql = $sourceStep.Script() | Out-String
                        $sql = $sql -replace "@job_id=N'[0-9a-fA-F-]+'", "@job_name=N'$($jobName -replace "'", "''")'"
                        Write-Message -Message $sql -Level Debug -FunctionName Copy-DbaAgentJobStep
                        $destServer.Query($sql)
                    }

                    $destJob.JobSteps.Refresh()
                    $copyJobStepStatus.Status = "Successful"
                    $copyJobStepStatus.Notes = "Synchronized $($sourceSteps.Count) job step(s)"
                    $copyJobStepStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                } catch {
                    $copyJobStepStatus.Status = "Failed"
                    $copyJobStepStatus.Notes = (Get-ErrorMessage -Record $_)
                    $copyJobStepStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                    Stop-Function -Message "Failed to synchronize steps for job $jobName on $destinstance" -ErrorRecord $_ -Target $destinstance -Continue -FunctionName Copy-DbaAgentJobStep
                }
            }
        }
    }
} $Destination $DestinationSqlCredential $Job $ExcludeJob $Step $InputObject $EnableException $__realCmdlet $__boundJob $__boundExcludeJob $__boundStep $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
