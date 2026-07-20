#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Lists performance-counter data collector sets. Port of
/// public/Get-DbaPfDataCollectorSet.ps1 (W1-091). The W1-080 shape: the begin-block
/// $setscript rides the hop VERBATIM (the Schedule.Service PLA task walk with its
/// GetTasks-until-catch accumulation, the Where-Object Name -in filter, the switch
/// WITHOUT a default whose $state goes STALE for unknown task states, the
/// $sets.Query + PSObject.Copy() projection with the STALE-$remotelatest else-branch
/// bug - it clears $remote instead - the in-scriptblock Write-Warning + contained
/// continue, and Credential = $args[1]); the local leg replaces Invoke-Command2's
/// nested-pipeline-hostile local Invoke-Command with the scope-equivalent invocation
/// under a Stop preference (W1-080 law); remote legs ride Invoke-Command2 verbatim.
/// The process loop member-enumerates $ComputerName.ComputerName. Surface pinned by
/// migration/baselines/Get-DbaPfDataCollectorSet.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaPfDataCollectorSet")]
public sealed class GetDbaPfDataCollectorSetCommand : DbaBaseCmdlet
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

    /// <summary>Windows credential for the remote invocation.</summary>
    [Parameter(Position = 1)]
    public PSCredential? Credential { get; set; }

    /// <summary>The collector set name(s) to include.</summary>
    [Parameter(Position = 2)]
    [Alias("DataCollectorSet")]
    public string[]? CollectorSet { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        // PS: foreach ($computer in $ComputerName.ComputerName) - member enumeration
        // (null elements are SKIPPED, pinned law).
        List<object?> computers = new List<object?>();
        foreach (DbaInstanceParameter? item in ComputerName ?? new DbaInstanceParameter[0])
        {
            if (item is null)
                continue;
            computers.Add(item.ComputerName);
        }

        foreach (object? computer in computers)
        {
            try
            {
                foreach (PSObject? item in NestedCommand.InvokeScoped(this, InvokeScript, computer, Credential, CollectorSet, BoundVerbose(), BoundDebug()))
                    WriteObject(item);
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                StopFunction("Failure", target: computer, errorRecord: StatementFault.Record(ex, "Get-DbaPfDataCollectorSet"), continueLoop: true);
                continue;
            }
        }
    }

    /// <summary>A bound -Verbose carrier for the hop scopes (W1-044 convention).</summary>
    private object? BoundDebug()
    {
        object? debug;
        if (MyInvocation.BoundParameters.TryGetValue("Debug", out debug))
            return LanguagePrimitives.IsTrue(debug);
        return null;
    }

    private object? BoundVerbose()
    {
        object? verbose;
        if (MyInvocation.BoundParameters.TryGetValue("Verbose", out verbose))
            return LanguagePrimitives.IsTrue(verbose);
        return null;
    }

    // PS: the begin-block $setscript VERBATIM + the local/remote invocation split and
    // the Select-DefaultView $columns pipe.
    private const string InvokeScript = """
param($__computer, $Credential, $CollectorSet, $__boundVerbose, $__boundDebug)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($__computer, $Credential, $CollectorSet, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundVerbose) { $VerbosePreference = $(if ($__boundVerbose) { "Continue" } else { "SilentlyContinue" }) }
    if ($null -ne $__boundDebug) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }
    $scriptBlock = {
        # Get names / status info
        $schedule = New-Object -ComObject "Schedule.Service"
        $schedule.Connect()
        $folder = $schedule.GetFolder("Microsoft\Windows\PLA")
        $tasks = @()
        $tasknumber = 0
        $done = $false
        do {
            try {
                $task = $folder.GetTasks($tasknumber)
                $tasknumber++
                if ($task) {
                    $tasks += $task
                }
            } catch {
                $done = $true
            }
        }
        while ($done -eq $false)
        $null = [System.Runtime.Interopservices.Marshal]::ReleaseComObject($schedule)

        if ($args[0]) {
            $tasks = $tasks | Where-Object Name -in $args[0]
        }

        $sets = New-Object -ComObject Pla.DataCollectorSet
        foreach ($task in $tasks) {
            $setname = $task.Name
            switch ($task.State) {
                0 { $state = "Unknown" }
                1 { $state = "Disabled" }
                2 { $state = "Queued" }
                3 { $state = "Ready" }
                4 { $state = "Running" }
            }

            try {
                # Query changes $sets so work from there
                $sets.Query($setname, $null)
                $set = $sets.PSObject.Copy()

                $outputlocation = $set.OutputLocation
                $latestoutputlocation = $set.LatestOutputLocation

                if ($outputlocation) {
                    $dir = (Split-Path $outputlocation).Replace(':', '$')
                    $remote = "\\$env:COMPUTERNAME\$dir"
                } else {
                    $remote = $null
                }

                if ($latestoutputlocation) {
                    $dir = ($latestoutputlocation).Replace(':', '$')
                    $remotelatest = "\\$env:COMPUTERNAME\$dir"
                } else {
                    $remote = $null
                }

                [PSCustomObject]@{
                    ComputerName               = $env:COMPUTERNAME
                    Name                       = $setname
                    LatestOutputLocation       = $set.LatestOutputLocation
                    OutputLocation             = $set.OutputLocation
                    RemoteOutputLocation       = $remote
                    RemoteLatestOutputLocation = $remotelatest
                    RootPath                   = $set.RootPath
                    Duration                   = $set.Duration
                    Description                = $set.Description
                    DescriptionUnresolved      = $set.DescriptionUnresolved
                    DisplayName                = $set.DisplayName
                    DisplayNameUnresolved      = $set.DisplayNameUnresolved
                    Keywords                   = $set.Keywords
                    Segment                    = $set.Segment
                    SegmentMaxDuration         = $set.SegmentMaxDuration
                    SegmentMaxSize             = $set.SegmentMaxSize
                    SerialNumber               = $set.SerialNumber
                    Server                     = $set.Server
                    Status                     = $set.Status
                    Subdirectory               = $set.Subdirectory
                    SubdirectoryFormat         = $set.SubdirectoryFormat
                    SubdirectoryFormatPattern  = $set.SubdirectoryFormatPattern
                    Task                       = $set.Task
                    TaskRunAsSelf              = $set.TaskRunAsSelf
                    TaskArguments              = $set.TaskArguments
                    TaskUserTextArguments      = $set.TaskUserTextArguments
                    Schedules                  = $set.Schedules
                    SchedulesEnabled           = $set.SchedulesEnabled
                    UserAccount                = $set.UserAccount
                    Xml                        = $set.Xml
                    Security                   = $set.Security
                    StopOnCompletion           = $set.StopOnCompletion
                    State                      = $state.Trim()
                    DataCollectorSetObject     = $true
                    TaskObject                 = $task
                    Credential                 = $args[1]
                }
            } catch {
                <# DO NOT use Write-Message as this is inside of a script block #>
                Write-Warning -Message "Issue with getting Collector Set $setname on $env:Computername : $_."
                continue
            }
        }
    }

    $columns = 'ComputerName', 'Name', 'DisplayName', 'Description', 'State', 'Duration', 'OutputLocation', 'LatestOutputLocation',
'RootPath', 'SchedulesEnabled', 'Segment', 'SegmentMaxDuration', 'SegmentMaxSize',
'SerialNumber', 'Server', 'StopOnCompletion', 'Subdirectory', 'SubdirectoryFormat',
'SubdirectoryFormatPattern', 'Task', 'TaskArguments', 'TaskRunAsSelf', 'TaskUserTextArguments', 'UserAccount'

    if (([dbainstance]$__computer).IsLocalHost) {
        # local Invoke-Command cannot host inside this nested pipeline; the
        # scope-equivalent invocation with a Stop preference matches -ErrorAction Stop
        $ErrorActionPreference = "Stop"
        & $scriptBlock $CollectorSet $Credential | Select-DefaultView -Property $columns
    } else {
        Invoke-Command2 -ComputerName $__computer -Credential $Credential -ScriptBlock $scriptBlock -ArgumentList $CollectorSet, $Credential -ErrorAction Stop | Select-DefaultView -Property $columns
    }
} $__computer $Credential $CollectorSet $__boundVerbose $__boundDebug 3>&1
""";
}
