#nullable enable

using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Imports a perfmon collector-set template. Port of
/// public/Import-DbaPfDataCollectorSetTemplate.ps1 (W1-107). The ENTIRE process body
/// rides one VERBATIM module hop per record (both loops and every contained
/// Stop-Function -Continue), with $Pscmdlet.ShouldProcess routed to the REAL cmdlet
/// (the W1-085 pattern) and Test-Bound modeled as carried flags. Function-scope
/// mutations persist across records through a state bag: $Path grows via += (the
/// ReferenceEquals reset detects a pipeline-by-property rebind - FullName alias),
/// Set-Variable DisplayName sticks once set, the `foreach ($instance in $instances)`
/// loop variable SHADOWS the -Instance parameter and its leftover gates later service
/// discovery, and a WhatIf-denied branch re-emits the STALE prior $output - all
/// preserved. The begin block resolves the module root with the source-carried
/// RB-IMP-51 fallback. Surface pinned by
/// migration/baselines/Import-DbaPfDataCollectorSetTemplate.json.
/// </summary>
[Cmdlet(VerbsData.Import, "DbaPfDataCollectorSetTemplate", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low)]
public sealed class ImportDbaPfDataCollectorSetTemplateCommand : DbaBaseCmdlet
{
    /// <summary>The target computer(s); defaults to the local computer.</summary>
    [Parameter(ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[]? ComputerName { get; set; } = BuildDefaultComputerName();

    private static DbaInstanceParameter[]? BuildDefaultComputerName()
    {
        string? name = Environment.GetEnvironmentVariable("COMPUTERNAME");
        if (string.IsNullOrEmpty(name))
            return null;
        return new DbaInstanceParameter[] { new DbaInstanceParameter(name) };
    }

    /// <summary>Windows credential for the remote work.</summary>
    [Parameter(Position = 1)]
    public PSCredential? Credential { get; set; }

    /// <summary>Display name for the imported set.</summary>
    [Parameter(Position = 2)]
    public string? DisplayName { get; set; }

    /// <summary>Enables the set's schedules.</summary>
    [Parameter]
    public SwitchParameter SchedulesEnabled { get; set; }

    /// <summary>Root output path.</summary>
    [Parameter(Position = 3)]
    public string? RootPath { get; set; }

    /// <summary>Enables segmenting.</summary>
    [Parameter]
    public SwitchParameter Segment { get; set; }

    /// <summary>Maximum segment duration.</summary>
    [Parameter(Position = 4)]
    public int SegmentMaxDuration { get; set; }

    /// <summary>Maximum segment size.</summary>
    [Parameter(Position = 5)]
    public int SegmentMaxSize { get; set; }

    /// <summary>Output subdirectory.</summary>
    [Parameter(Position = 6)]
    public string? Subdirectory { get; set; }

    /// <summary>Subdirectory format value.</summary>
    [Parameter(Position = 7)]
    public int SubdirectoryFormat { get; set; } = 3;

    /// <summary>Subdirectory format pattern.</summary>
    [Parameter(Position = 8)]
    public string SubdirectoryFormatPattern { get; set; } = "yyyyMMdd\\-NNNNNN";

    /// <summary>Associated task name.</summary>
    [Parameter(Position = 9)]
    public string? Task { get; set; }

    /// <summary>Runs the task as self.</summary>
    [Parameter]
    public SwitchParameter TaskRunAsSelf { get; set; }

    /// <summary>Task arguments.</summary>
    [Parameter(Position = 10)]
    public string? TaskArguments { get; set; }

    /// <summary>Task user text arguments.</summary>
    [Parameter(Position = 11)]
    public string? TaskUserTextArguments { get; set; }

    /// <summary>Stops the set on completion.</summary>
    [Parameter]
    public SwitchParameter StopOnCompletion { get; set; }

    /// <summary>Template file path(s).</summary>
    [Parameter(ValueFromPipelineByPropertyName = true, Position = 12)]
    [Alias("FullName")]
    public string[]? Path { get; set; }

    /// <summary>Bundled template name(s).</summary>
    [Parameter(Position = 13)]
    public string[]? Template { get; set; }

    /// <summary>SQL instance name(s) for counter cloning.</summary>
    [Parameter(Position = 14)]
    public string[]? Instance { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    private object? _moduleRoot;

    // PS function-scope locals persisting across records (the state bag rides the hop
    // and comes back as the sentinel item).
    private Hashtable? _state;
    private object? _pathState;
    private object? _lastBoundPath;
    private object? _displayNameState;
    private bool _displayNameInitialized;

    protected override void BeginProcessing()
    {
        Collection<PSObject> results = NestedCommand.InvokeScoped(this, ModuleRootScript);
        _moduleRoot = results.Count == 1 ? results[0] : null;
    }

    protected override void ProcessRecord()
    {
        // PS: named $Path keeps ONE array reference across records (the += growth
        // persists); pipeline-by-property (FullName) re-binds a fresh array per record.
        if (!ReferenceEquals(Path, _lastBoundPath) || _pathState is null)
        {
            _pathState = Path;
            _lastBoundPath = Path;
        }

        if (!_displayNameInitialized)
        {
            // PS: an unbound [string] parameter reads "".
            _displayNameState = DisplayName ?? "";
            _displayNameInitialized = true;
        }

        Collection<PSObject> results = NestedCommand.InvokeScoped(this, ProcessScript,
            ComputerName, Credential, _displayNameState, SchedulesEnabled.ToBool(), RootPath ?? "",
            Segment.ToBool(), SegmentMaxDuration, SegmentMaxSize, Subdirectory ?? "",
            SubdirectoryFormat, SubdirectoryFormatPattern, Task ?? "", TaskRunAsSelf.ToBool(),
            TaskArguments ?? "", TaskUserTextArguments ?? "", StopOnCompletion.ToBool(),
            _pathState, Template, Instance, _moduleRoot, _state,
            TestBound("Path"), TestBound("Template"), TestBound("DisplayName"), TestBound("RootPath"),
            EnableException.ToBool(), this, BoundVerbose());

        foreach (PSObject? item in results)
        {
            Hashtable? sentinel = item?.BaseObject as Hashtable;
            if (sentinel is not null && sentinel.ContainsKey("__w1107State"))
            {
                _state = sentinel["__w1107State"] as Hashtable;
                if (_state is not null)
                {
                    _pathState = _state["Path"];
                    _displayNameState = _state["DisplayName"];
                }
                continue;
            }
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
                continue;
            }
            WriteObject(item);
        }
    }

    /// <summary>Removes the silent $error copy the nested pipeline bagged for a merged-back
    /// non-terminating record (the W1-045 compensation).</summary>
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
            // best-effort bookkeeping
        }
    }

    /// <summary>A bound -Verbose carrier for the hop scopes (W1-044 convention).</summary>
    private object? BoundVerbose()
    {
        object? verbose;
        if (MyInvocation.BoundParameters.TryGetValue("Verbose", out verbose))
            return LanguagePrimitives.IsTrue(verbose);
        return null;
    }

    // PS: the begin-block module-root resolution (source-carried RB-IMP-51 fallback).
    private const string ModuleRootScript = """
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    $moduleRoot = $script:PSModuleRoot
    if (-not $moduleRoot) {
        $moduleRoot = (Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1).ModuleBase
    }
    $moduleRoot
}
""";

    // PS: the ENTIRE process body VERBATIM (both loops, the template resolution and
    // += Path growth, the replace pipeline, ShouldProcess routing via $__realCmdlet,
    // the $instance shadowing, the counter cloning) plus the begin-block scriptblocks
    // it invokes; the trailing sentinel carries the mutated fn-scope state back.
    private const string ProcessScript = """
param($ComputerName, $Credential, $DisplayName, $SchedulesEnabled, $RootPath, $Segment, $SegmentMaxDuration, $SegmentMaxSize, $Subdirectory, $SubdirectoryFormat, $SubdirectoryFormatPattern, $Task, $TaskRunAsSelf, $TaskArguments, $TaskUserTextArguments, $StopOnCompletion, $Path, $Template, $Instance, $__moduleRoot, $__state, $__pathBound, $__templateBound, $__displayNameBound, $__rootPathBound, $EnableException, $__realCmdlet, $__boundVerbose)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($ComputerName, $Credential, $DisplayName, $SchedulesEnabled, $RootPath, $Segment, $SegmentMaxDuration, $SegmentMaxSize, $Subdirectory, $SubdirectoryFormat, $SubdirectoryFormatPattern, $Task, $TaskRunAsSelf, $TaskArguments, $TaskUserTextArguments, $StopOnCompletion, $Path, $Template, $Instance, $__moduleRoot, $__state, $__pathBound, $__templateBound, $__displayNameBound, $__rootPathBound, $EnableException, $__realCmdlet, $__boundVerbose)
    if ($null -ne $__boundVerbose) { $VerbosePreference = $(if ($__boundVerbose) { "Continue" } else { "SilentlyContinue" }) }
    $moduleRoot = $__moduleRoot

    $setscript = {
        $setname = $args[0]; $templatexml = $args[1]
        $collectorset = New-Object -ComObject Pla.DataCollectorSet
        $collectorset.SetXml($templatexml)
        $null = $collectorset.Commit($setname, $null, 0x0003) #add or modify.
        $null = $collectorset.Query($setname, $Null)
    }

    $instancescript = {
        $services = Get-Service -DisplayName *sql* | Select-Object -ExpandProperty DisplayName
        [regex]::matches($services, '(?<=\().+?(?=\))').Value | Where-Object { $PSItem -ne 'MSSQLSERVER' } | Select-Object -Unique
    }

    # restore fn-scope locals mutated by earlier records
    if ($null -ne $__state) {
        $Name = $__state.Name
        $RootName = $__state.RootName
        $output = $__state.output
        $instance = $__state.instance
        $xml = $__state.xml
        $plainxml = $__state.plainxml
        $contents = $__state.contents
        $instances = $__state.instances
        $datacollector = $__state.datacollector
        $sqlcounters = $__state.sqlcounters
        $newcollection = $__state.newcollection
        $templatepath = $__state.templatepath
    }



    if ((-not $__pathBound) -and (-not $__templateBound)) {
        Stop-Function -Message "You must specify Path or Template" -FunctionName Import-DbaPfDataCollectorSetTemplate
    }

    if (($Path.Count -gt 1 -or $Template.Count -gt 1) -and ($__templateBound)) {
        Stop-Function -Message "Name cannot be specified with multiple files or templates because the Session will already exist" -FunctionName Import-DbaPfDataCollectorSetTemplate
    }

    foreach ($computer in $ComputerName) {
        $null = Test-ElevationRequirement -ComputerName $computer -Continue

        foreach ($file in $template) {
            $templatepath = "$moduleRoot\bin\perfmontemplates\collectorsets\$file.xml"
            if ((Test-Path $templatepath)) {
                $Path += $templatepath
            } else {
                Stop-Function -Message "Invalid template ($templatepath does not exist)" -Continue -FunctionName Import-DbaPfDataCollectorSetTemplate
            }
        }

        foreach ($file in $Path) {

            if ((-not $__displayNameBound)) {
                Set-Variable -Name DisplayName -Value (Get-ChildItem -Path $file).BaseName
            }

            $Name = $DisplayName

            Write-Message -Level Verbose -Message "Processing $file for $computer"

            if ((-not $__rootPathBound)) {
                Set-Variable -Name RootName -Value "%systemdrive%\PerfLogs\Admin\$Name"
            }

            # Perform replace
            $temp = ([System.IO.Path]::GetTempPath()).TrimEnd("").TrimEnd("\")
            $tempfile = "$temp\import-dbatools-perftemplate.xml"

            try {
                # Get content
                $contents = Get-Content $file -ErrorAction Stop

                # Replace content
                $replacements = 'RootPath', 'DisplayName', 'SchedulesEnabled', 'Segment', 'SegmentMaxDuration', 'SegmentMaxSize', 'SubdirectoryFormat', 'SubdirectoryFormatPattern', 'Task', 'TaskRunAsSelf', 'TaskArguments', 'TaskUserTextArguments', 'StopOnCompletion', 'DisplayNameUnresolved'

                foreach ($replacement in $replacements) {
                    $phrase = "<$replacement></$replacement>"
                    $value = (Get-Variable -Name $replacement -ErrorAction SilentlyContinue).Value
                    if ($value -eq $false) {
                        $value = "0"
                    }
                    if ($value -eq $true) {
                        $value = "1"
                    }
                    $replacephrase = "<$replacement>$value</$replacement>"
                    $contents = $contents.Replace($phrase, $replacephrase)
                }

                # Set content
                $null = Set-Content -Path $tempfile -Value $contents -Encoding Unicode
                $xml = [xml](Get-Content $tempfile -ErrorAction Stop)
                $plainxml = Get-Content $tempfile -ErrorAction Stop -Raw
                $file = $tempfile
            } catch {
                Stop-Function -Message "Failure" -ErrorRecord $_ -Target $file -Continue -FunctionName Import-DbaPfDataCollectorSetTemplate
            }
            if (-not $xml.DataCollectorSet) {
                Stop-Function -Message "$file is not a valid Performance Monitor template document" -Continue -FunctionName Import-DbaPfDataCollectorSetTemplate
            }

            try {
                Write-Message -Level Verbose -Message "Importing $file as $name "

                if ($instance) {
                    $instances = $instance
                } else {
                    $instances = Invoke-Command2 -ComputerName $computer -Credential $Credential -ScriptBlock $instancescript -ErrorAction Stop -Raw
                }

                $scriptBlock = {
                    try {
                        $results = Invoke-Command2 -ComputerName $computer -Credential $Credential -ScriptBlock $setscript -ArgumentList $Name, $plainxml -ErrorAction Stop
                        Write-Message -Level Verbose -Message " $results"
                    } catch {
                        Stop-Function -Message "Failure starting $setname on $computer" -ErrorRecord $_ -Target $computer -Continue -FunctionName Import-DbaPfDataCollectorSetTemplate
                    }
                }

                if ((Get-DbaPfDataCollectorSet -ComputerName $computer -CollectorSet $Name)) {
                    if ($__realCmdlet.ShouldProcess($computer, "CollectorSet $Name already exists. Modify?")) {
                        Invoke-Command -Scriptblock $scriptBlock
                        $output = Get-DbaPfDataCollectorSet -ComputerName $computer -CollectorSet $Name
                    }
                } else {
                    if ($__realCmdlet.ShouldProcess($computer, "Importing collector set $Name")) {
                        Invoke-Command -Scriptblock $scriptBlock
                        $output = Get-DbaPfDataCollectorSet -ComputerName $computer -CollectorSet $Name
                    }
                }

                $newcollection = @()
                foreach ($instance in $instances) {
                    $datacollector = Get-DbaPfDataCollectorSet -ComputerName $computer -CollectorSet $Name | Get-DbaPfDataCollector
                    $sqlcounters = $datacollector | Get-DbaPfDataCollectorCounter | Where-Object { $_.Name -match 'sql.*\:' -and $_.Name -notmatch 'sqlclient' } | Select-Object -ExpandProperty Name

                    foreach ($counter in $sqlcounters) {
                        $split = $counter.Split(":")
                        $firstpart = switch ($split[0]) {
                            'SQLServer' { 'MSSQL' }
                            '\SQLServer' { '\MSSQL' }
                            default { $split[0] }
                        }
                        $secondpart = $split[-1]
                        $finalcounter = "$firstpart`$$instance`:$secondpart"
                        $newcollection += $finalcounter
                    }
                }

                if ($newcollection.Count) {
                    if ($__realCmdlet.ShouldProcess($computer, "Adding $($newcollection.Count) additional counters")) {
                        $null = Add-DbaPfDataCollectorCounter -InputObject $datacollector -Counter $newcollection
                    }
                }

                Remove-Item $tempfile -ErrorAction SilentlyContinue
                $output
            } catch {
                Stop-Function -Message "Failure" -ErrorRecord $_ -Target $store -Continue -FunctionName Import-DbaPfDataCollectorSetTemplate
            }
        }
    }

    @{ __w1107State = @{ Path = $Path; DisplayName = $DisplayName; Name = $Name; RootName = $RootName; output = $output; instance = $instance; xml = $xml; plainxml = $plainxml; contents = $contents; instances = $instances; datacollector = $datacollector; sqlcounters = $sqlcounters; newcollection = $newcollection; templatepath = $templatepath } }
} $ComputerName $Credential $DisplayName $SchedulesEnabled $RootPath $Segment $SegmentMaxDuration $SegmentMaxSize $Subdirectory $SubdirectoryFormat $SubdirectoryFormatPattern $Task $TaskRunAsSelf $TaskArguments $TaskUserTextArguments $StopOnCompletion $Path $Template $Instance $__moduleRoot $__state $__pathBound $__templateBound $__displayNameBound $__rootPathBound $EnableException $__realCmdlet $__boundVerbose 3>&1 2>&1
""";
}
