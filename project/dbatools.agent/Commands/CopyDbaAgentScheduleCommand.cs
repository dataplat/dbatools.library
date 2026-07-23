#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;
using SmoAgentJobSchedule = Microsoft.SqlServer.Management.Smo.Agent.JobSchedule;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Copies shared SQL Agent schedules. Port of public/Copy-DbaAgentSchedule.ps1 (W2-007). The
/// workflow remains a module-scoped PowerShell compatibility hop; the compiled cmdlet preserves
/// begin/process pipeline lifetime and supplies the real ShouldProcess runtime. Surface pinned by
/// migration/baselines/Copy-DbaAgentSchedule.json.
/// </summary>
[Cmdlet(VerbsCommon.Copy, "DbaAgentSchedule",
    SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Medium)]
public sealed class CopyDbaAgentScheduleCommand : DbaBaseCmdlet
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

    /// <summary>Only copy schedules with these names.</summary>
    [Parameter(Position = 4)]
    public string[]? Schedule { get; set; }

    /// <summary>Only copy schedules with these identifiers.</summary>
    [Parameter(Position = 5)]
    public int[]? Id { get; set; }

    /// <summary>Shared schedules supplied directly or through the pipeline.</summary>
    [Parameter(ValueFromPipeline = true, Position = 6)]
    public SmoAgentJobSchedule[]? InputObject { get; set; }

    /// <summary>Request replacement of existing schedules.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    private readonly List<SmoAgentJobSchedule> _beginSchedules = new();
    private bool _beginInterrupted;
    private bool _inputObjectBoundAtBegin;

    // DEF-011/T22: the process-block validation Stop-Function ("You must specify either Source or
    // pipe...") sets the function-scope interrupt latch in the source, which spans the pipeline so a
    // later record's Test-FunctionInterrupt short-circuits it silently. The per-record hop starts with
    // a fresh scope and drops that latch, so a >=2-record pipe of null/falsy elements would emit one
    // warning per record instead of one total. Harvest the latch from each process hop and carry it.
    private bool _interrupted;

    protected override void BeginProcessing()
    {
        _inputObjectBoundAtBegin = TestBound("InputObject");

        bool completed = false;
        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BeginScript,
            Source, SourceSqlCredential, Schedule, Id, InputObject,
            EnableException.ToBool(), NestedCommand.BoundCommonParameter(this, "Verbose"), NestedCommand.BoundCommonParameter(this, "Debug")))
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                NestedCommand.RemoveDuplicateError(this, nestedError);
                WriteError(nestedError);
            }
            else if (item?.BaseObject is SmoAgentJobSchedule schedule)
            {
                _beginSchedules.Add(schedule);
            }
            else if (item is not null && LanguagePrimitives.IsTrue(
                item.Properties["__CopyDbaAgentScheduleBeginComplete"]?.Value))
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
        if (_beginInterrupted || _interrupted)
            return;

        // TRUTHINESS, not boundness, and the distinction is the source's not a preference. The guard
        // this feeds reads "-not $PSBoundParameters.Source -and -not $PSBoundParameters.InputObject",
        // which is false for a bound-but-FALSY value: -InputObject @( ) is bound, so a boundness
        // carrier skips the guard where the source raises it. This carrier must therefore reproduce
        // the source's test rather than the standard Test-Bound remedy - the two are mirror images and
        // only the source line says which applies. _inputObjectBoundAtBegin below is a DIFFERENT
        // question (hop-scope narrowing) and correctly stays boundness.
        bool inputObjectTruthy = LanguagePrimitives.IsTrue(InputObject);
        bool sourceTruthy = LanguagePrimitives.IsTrue(Source);
        SmoAgentJobSchedule[] schedules = !_inputObjectBoundAtBegin && InputObject is not null
            ? InputObject
            : _beginSchedules.ToArray();

        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is Hashtable sentinel && sentinel.ContainsKey("__copyDbaAgentScheduleProcess"))
            {
                if (sentinel["__copyDbaAgentScheduleProcess"] is Hashtable state)
                    _interrupted = _interrupted || LanguagePrimitives.IsTrue(state["Interrupted"]);
                return;
            }
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                NestedCommand.RemoveDuplicateError(this, nestedError);
                WriteError(nestedError);
            }
            else
            {
                WriteObject(item);
            }
        }, ProcessScript,
            Destination, DestinationSqlCredential, Schedule, schedules, Force.ToBool(),
            EnableException.ToBool(), this, sourceTruthy, inputObjectTruthy,
            NestedCommand.BoundCommonParameter(this, "Verbose"), NestedCommand.BoundCommonParameter(this, "Debug"));
    }

    private const string BeginScript = """
param($Source, $SourceSqlCredential, $Schedule, $Id, $InputObject, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter]$Source, $SourceSqlCredential, [string[]]$Schedule, [int[]]$Id, [Microsoft.SqlServer.Management.Smo.Agent.JobSchedule[]]$InputObject, $EnableException, $__boundVerbose, $__boundDebug)

    if ($Source) {
        try {
            $sourceServer = Connect-DbaInstance -SqlInstance $Source -SqlCredential $SourceSqlCredential -MinimumVersion 9
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $Source -FunctionName Copy-DbaAgentSchedule
            return
        }

        if (-not $InputObject) {
            $InputObject = Get-DbaAgentSchedule -SqlInstance $sourceServer -Schedule $Schedule -Id $Id
        }
    }
    $InputObject
    [pscustomobject]@{ __CopyDbaAgentScheduleBeginComplete = $true }
} $Source $SourceSqlCredential $Schedule $Id $InputObject $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";

    private const string ProcessScript = """
param($Destination, $DestinationSqlCredential, $Schedule, $InputObject, $Force, $EnableException, $__realCmdlet, $__boundSource, $__boundInputObject, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$Destination, $DestinationSqlCredential, [string[]]$Schedule, [Microsoft.SqlServer.Management.Smo.Agent.JobSchedule[]]$InputObject, $Force, $EnableException, $__realCmdlet, $__boundSource, $__boundInputObject, $__boundVerbose, $__boundDebug)
    if ($Force) { $ConfirmPreference = 'none' }

    # Body is dot-sourced so the validation Stop-Function's early return still reaches the interrupt
    # harvest below; the harvested latch is carried across records by the C# guard (DEF-011/T22).
    . {
    if (Test-FunctionInterrupt) { return }

    if (-not $__boundSource -and -not $__boundInputObject) {
        Stop-Function -Message "You must specify either Source or pipe in results from Get-DbaAgentSchedule" -FunctionName Copy-DbaAgentSchedule
        return
    }

    foreach ($destinstance in $Destination) {
        try {
            $destServer = Connect-DbaInstance -SqlInstance $destinstance -SqlCredential $DestinationSqlCredential -MinimumVersion 9
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $destinstance -Continue -FunctionName Copy-DbaAgentSchedule
        }

        # Refresh the cache before checking existing schedules
        $destServer.JobServer.SharedSchedules.Refresh()
        $destSchedules = Get-DbaAgentSchedule -SqlInstance $destServer -Schedule $Schedule

        foreach ($currentschedule in $InputObject) {
            $scheduleName = $currentschedule.Name
            $sourceServer = $currentschedule.Parent.Parent
            $copySharedScheduleStatus = [PSCustomObject]@{
                SourceServer      = $sourceServer.Name
                DestinationServer = $destServer.Name
                Type              = "Agent Schedule"
                Name              = $scheduleName
                Status            = $null
                Notes             = $null
                DateTime          = [Dataplat.Dbatools.Utility.DbaDateTime](Get-Date)
            }

            if ($destSchedules.Name -contains $scheduleName) {
                if ($Force -ne $true) {
                    if ($__realCmdlet.ShouldProcess($destinstance, "Shared job schedule $scheduleName exists at destination. Use -Force to drop and migrate.")) {
                        $copySharedScheduleStatus.Status = "Skipped"
                        $copySharedScheduleStatus.Notes = "Already exists on destination"
                        $copySharedScheduleStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                        Write-Message -Level Verbose -Message "Shared job schedule $scheduleName exists at destination. Use -Force to drop and migrate." -FunctionName Copy-DbaAgentSchedule -ModuleName "dbatools"
                    }
                    continue
                } else {
                    if ($__realCmdlet.ShouldProcess($destinstance, "Schedule [$scheduleName] has associated jobs. Skipping.")) {
                        if ($destServer.JobServer.Jobs.JobSchedules.Name -contains $scheduleName) {
                            $copySharedScheduleStatus.Status = "Skipped"
                            $copySharedScheduleStatus.Notes = "Schedule has associated jobs"
                            $copySharedScheduleStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                            Write-Message -Level Verbose -Message "Schedule [$scheduleName] has associated jobs. Skipping." -FunctionName Copy-DbaAgentSchedule -ModuleName "dbatools"
                        }
                        continue
                    } else {
                        if ($__realCmdlet.ShouldProcess($destinstance, "Dropping schedule $scheduleName and recreating")) {
                            try {
                                Write-Message -Level Verbose -Message "Dropping schedule $scheduleName" -FunctionName Copy-DbaAgentSchedule -ModuleName "dbatools"
                                $destServer.JobServer.SharedSchedules[$scheduleName].Drop()
                            } catch {
                                $copySharedScheduleStatus.Status = "Failed"
                                $copySharedScheduleStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                                Write-Message -Level Verbose -Message "Issue dropping schedule $scheduleName on $destinstance | $PSItem" -FunctionName Copy-DbaAgentSchedule -ModuleName "dbatools"
                                continue
                            }
                        }
                    }
                }
            }

            if ($__realCmdlet.ShouldProcess($destinstance, "Creating schedule $scheduleName")) {
                try {
                    Write-Message -Level Verbose -Message "Copying schedule $scheduleName" -FunctionName Copy-DbaAgentSchedule -ModuleName "dbatools"
                    $sql = $currentschedule.Script() | Out-String

                    Write-Message -Level Debug -Message $sql -FunctionName Copy-DbaAgentSchedule -ModuleName "dbatools"
                    $destServer.Query($sql)

                    $copySharedScheduleStatus.Status = "Successful"
                    $copySharedScheduleStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                } catch {
                    $copySharedScheduleStatus.Status = "Failed"
                    $copySharedScheduleStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                    Write-Message -Level Verbose -Message "Issue creating schedule $scheduleName on $destinstance | $PSItem" -FunctionName Copy-DbaAgentSchedule -ModuleName "dbatools"
                    continue
                }
            }
        }
    }
    }
    $__iv = Get-Variable -Name __dbatools_interrupt_function_78Q9VPrM6999g6zo24Qn83m09XF56InEn4hFrA8Fwhu5xJrs6r -Scope 0 -ErrorAction Ignore
    @{ __copyDbaAgentScheduleProcess = @{ Interrupted = [bool]($__iv -and $__iv.Value) } }
} $Destination $DestinationSqlCredential $Schedule $InputObject $Force $EnableException $__realCmdlet $__boundSource $__boundInputObject $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
