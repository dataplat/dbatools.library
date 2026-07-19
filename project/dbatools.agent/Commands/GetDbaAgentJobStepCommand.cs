#nullable enable

using System;
using System.Collections;
using System.Globalization;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;
using SmoAgentJob = Microsoft.SqlServer.Management.Smo.Agent.Job;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves and decorates SQL Agent job steps. The process/end accumulation and output-shaping
/// workflow remains a module-scoped PowerShell compatibility hop. Surface pinned by
/// migration/baselines/Get-DbaAgentJobStep.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaAgentJobStep")]
public sealed class GetDbaAgentJobStepCommand : DbaBaseCmdlet
{
    /// <summary>Target SQL Server instances.</summary>
    [Parameter(ValueFromPipeline = true, Position = 0)]
    [PsAgentStepDbaInstanceArrayCast]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Job names to include.</summary>
    [Parameter(Position = 2)]
    public string[]? Job { get; set; }

    /// <summary>Job names to exclude.</summary>
    [Parameter(Position = 3)]
    public string[]? ExcludeJob { get; set; }

    /// <summary>SQL Agent jobs supplied directly or through the pipeline.</summary>
    [Parameter(ValueFromPipeline = true, Position = 4)]
    [PsAgentJobArrayCast]
    public SmoAgentJob[]? InputObject { get; set; }

    /// <summary>Exclude jobs whose IsEnabled property is false.</summary>
    [Parameter]
    public SwitchParameter ExcludeDisabledJobs { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    private object? _server;

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
            }
            else if (item is not null && LanguagePrimitives.IsTrue(
                item.Properties["__GetDbaAgentJobStepProcessComplete"]?.Value))
            {
                object? inputState = item.Properties["InputObject"]?.Value;
                InputObject = inputState is null
                    ? null
                    : (SmoAgentJob[]?)LanguagePrimitives.ConvertTo(
                        inputState, typeof(SmoAgentJob[]), CultureInfo.InvariantCulture);
                _server = UnwrapHopValue(item.Properties["Server"]?.Value);
            }
            else
            {
                WriteObject(item);
            }
        }, ProcessScript,
            SqlInstance, SqlCredential, InputObject, _server, EnableException.ToBool(),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug"));
    }

    protected override void EndProcessing()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, EndScript,
            InputObject, Job, ExcludeJob, ExcludeDisabledJobs.ToBool(), _server,
            EnableException.ToBool(), BoundCommonParameter("Verbose"),
            BoundCommonParameter("Debug")))
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

    // Carried hop state arrives PSObject-wrapped. A PSCustomObject carries its content on the
    // wrapper rather than the BaseObject, so unwrapping one would discard it - keep it wrapped.
    private static object? UnwrapHopValue(object? value)
    {
        if (value is null || ReferenceEquals(value, System.Management.Automation.Internal.AutomationNull.Value))
            return null;
        if (value is not PSObject wrapper)
            return value;
        return wrapper.BaseObject is PSCustomObject ? wrapper : wrapper.BaseObject;
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

    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $InputObject, $Server, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, $SqlCredential,
        [Microsoft.SqlServer.Management.Smo.Agent.Job[]]$InputObject, $Server, $EnableException)
    $server = $Server
    foreach ($instance in $SqlInstance) {
        try {
            $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaAgentJobStep
        }
        Write-Message -Level Verbose -Message "Collecting jobs on $instance" -FunctionName Get-DbaAgentJobStep -ModuleName "dbatools"
        $InputObject += $server.JobServer.Jobs
    }
    [pscustomobject]@{
        __GetDbaAgentJobStepProcessComplete = $true
        InputObject = $InputObject
        Server = $server
    }
} $SqlInstance $SqlCredential $InputObject $Server $EnableException @__commonParameters 3>&1 2>&1
""";

    private const string EndScript = """
param($InputObject, $Job, $ExcludeJob, $ExcludeDisabledJobs, $Server, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Microsoft.SqlServer.Management.Smo.Agent.Job[]]$InputObject, [string[]]$Job,
        [string[]]$ExcludeJob, $ExcludeDisabledJobs, $Server, $EnableException)
    $server = $Server
    if ($Job) {
        $InputObject = $InputObject | Where-Object Name -In $Job
    }
    if ($ExcludeJob) {
        $InputObject = $InputObject | Where-Object Name -NotIn $ExcludeJob
    }
    if ($ExcludeDisabledJobs) {
        $InputObject = $InputObject | Where-Object IsEnabled -eq $true
    }
    Write-Message -Level Verbose -Message "Collecting job steps on ($server.Name)" -FunctionName Get-DbaAgentJobStep -ModuleName "dbatools"
    foreach ($agentJobStep in $InputObject.jobsteps) {
        Add-Member -Force -InputObject $agentJobStep -MemberType NoteProperty -Name ComputerName -value $agentJobStep.Parent.Parent.Parent.ComputerName
        Add-Member -Force -InputObject $agentJobStep -MemberType NoteProperty -Name InstanceName -value $agentJobStep.Parent.Parent.Parent.ServiceName
        Add-Member -Force -InputObject $agentJobStep -MemberType NoteProperty -Name SqlInstance -value $agentJobStep.Parent.Parent.Parent.DomainInstanceName
        Add-Member -Force -InputObject $agentJobStep -MemberType NoteProperty -Name AgentJob -value $agentJobStep.Parent.Name

        Select-DefaultView -InputObject $agentJobStep -Property ComputerName, InstanceName, SqlInstance, AgentJob, Name, SubSystem, LastRunDate, LastRunOutcome, State
    }
} $InputObject $Job $ExcludeJob $ExcludeDisabledJobs $Server $EnableException @__commonParameters 3>&1 2>&1
""";
}

/// <summary>Reproduces the advanced function's typed DbaInstanceParameter array conversion.</summary>
internal sealed class PsAgentStepDbaInstanceArrayCastAttribute : ArgumentTransformationAttribute
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

/// <summary>Reproduces the advanced function's typed SQL Agent Job array conversion.</summary>
internal sealed class PsAgentJobArrayCastAttribute : ArgumentTransformationAttribute
{
    public override object? Transform(EngineIntrinsics engineIntrinsics, object? inputData)
    {
        if (inputData is null)
            return null;
        try
        {
            return LanguagePrimitives.ConvertTo(inputData, typeof(SmoAgentJob[]), CultureInfo.InvariantCulture);
        }
        catch (PSInvalidCastException ex)
        {
            throw new ArgumentTransformationMetadataException(ex.Message, ex);
        }
    }
}
