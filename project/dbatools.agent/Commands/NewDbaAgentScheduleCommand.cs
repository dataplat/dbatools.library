#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Creates a new SQL Server Agent job schedule (optionally attaching it to jobs).
/// </summary>
/// <remarks>
/// The FrequencyText parsing, the frequency/subday/relative-interval normalization, the date/time
/// validation and construction, the instance connection, the JobSchedule creation, the property
/// assignments, the job attachment, and the output all run the original dbatools PowerShell body
/// VERBATIM inside the dbatools module scope rather than being reimplemented in C#, so the engine
/// decides the observable details.
///
/// begin/process/end. The begin block runs one-shot validation + normalization whose no-Continue
/// Stop-Functions set the function-scope interrupt and return; both process AND end short-circuit via
/// Test-FunctionInterrupt, so the interrupt is carried begin -> (process, end) via _interrupted (read
/// with Get-Variable -Scope 0 after a dot-sourced body, reported in the begin sentinel; BOTH
/// ProcessRecord and EndProcessing guard on it). Under -EnableException the begin Stop-Functions throw
/// instead, terminating before any record.
///
/// The begin block computes FIFTEEN values the process block reads and that are pure functions of the
/// NON-pipeline parameters: $FrequencyType/$FrequencySubdayType/$FrequencyRelativeInterval normalized
/// to ints, $FrequencyRecurrenceFactor and $FrequencySubdayInterval possibly defaulted/derived,
/// $interval derived, $Schedule possibly derived from FrequencyText, $StartDate/$EndDate/$StartTime/
/// $EndTime possibly defaulted, and the four constructed $activeStartDate/$activeEndDate (DateTime) /
/// $activeStartTimeOfDay/$activeEndTimeOfDay (TimeSpan). These are captured ONCE from the begin
/// sentinel into C# fields and threaded into every process record. None of them is mutated by the
/// process body, so they are plain constants (no per-record re-emit).
///
/// $jobschedule DOES need a cross-record carry: it is created inside the process ShouldProcess block
/// but read afterwards (the job-attach branch and the trailing output) OUTSIDE that block, so on a
/// record whose creation ShouldProcess is declined the source reuses the PREVIOUS record's function-
/// scope $jobschedule. The hop reproduces this: $jobschedule is seeded from _jobschedule, re-emitted
/// in the process sentinel, and fed to the next record. The process body is dot-sourced so any early
/// exit still reaches that sentinel.
///
/// The begin block's "if (\$Force) { \$ConfirmPreference = 'none' }" is folded to the top of the
/// process hop with \$__gate = if (\$Force) { \$PSCmdlet } else { \$__realCmdlet }; both ShouldProcess
/// sites use \$__gate. IMPORTANT: the fold uses the USER-passed -Force (this cmdlet's Force property),
/// NOT the begin-local "\$Force = \$true" that FrequencyText sets at line 406 - in the source that
/// mutation happens AFTER the confirm-preference line and so never affects process confirmation.
/// There is no Test-Bound in this function.
///
/// Output streams: each created schedule is emitted before a later record may fail under
/// -EnableException (DEF-001), so the process hop uses InvokeScopedStreaming. This cmdlet supplies the
/// real ShouldProcess runtime (ConfirmImpact Low). Surface pinned by
/// migration/baselines/New-DbaAgentSchedule.json.
/// </remarks>
[Cmdlet(VerbsCommon.New, "DbaAgentSchedule", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low)]
public sealed class NewDbaAgentScheduleCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    [PsDbaInstanceArrayCast]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The job(s) to attach the new schedule to.</summary>
    [Parameter(Position = 2)]
    public object[]? Job { get; set; }

    /// <summary>The name of the schedule.</summary>
    [Parameter(Position = 3)]
    public object? Schedule { get; set; }

    /// <summary>Create the schedule in a disabled state.</summary>
    [Parameter]
    public SwitchParameter Disabled { get; set; }

    /// <summary>The overall frequency pattern for the schedule.</summary>
    [Parameter(Position = 4)]
    [ValidateSet("Once", "OneTime", "Daily", "Weekly", "Monthly", "MonthlyRelative", "AgentStart", "AutoStart", "IdleComputer", "OnIdle")]
    public object? FrequencyType { get; set; }

    /// <summary>The days or intervals the job runs, based on FrequencyType.</summary>
    [Parameter(Position = 5)]
    public object[]? FrequencyInterval { get; set; }

    /// <summary>The unit of time for running jobs multiple times within a day.</summary>
    [Parameter(Position = 6)]
    [ValidateSet("Once", "Time", "Seconds", "Second", "Minutes", "Minute", "Hours", "Hour")]
    public object? FrequencySubdayType { get; set; }

    /// <summary>How many subday units to wait between executions.</summary>
    [Parameter(Position = 7)]
    public int FrequencySubdayInterval { get; set; }

    /// <summary>Which occurrence within the month for MonthlyRelative schedules.</summary>
    [Parameter(Position = 8)]
    [ValidateSet("Unused", "First", "Second", "Third", "Fourth", "Last")]
    public object? FrequencyRelativeInterval { get; set; }

    /// <summary>The interval between occurrences for weekly/monthly schedules.</summary>
    [Parameter(Position = 9)]
    public int FrequencyRecurrenceFactor { get; set; }

    /// <summary>A natural-language schedule description (e.g. "every 5 minutes").</summary>
    [Parameter(Position = 10)]
    public string? FrequencyText { get; set; }

    /// <summary>The earliest date the schedule becomes active (yyyyMMdd).</summary>
    [Parameter(Position = 11)]
    public string? StartDate { get; set; }

    /// <summary>The last date the schedule is active (yyyyMMdd).</summary>
    [Parameter(Position = 12)]
    public string? EndDate { get; set; }

    /// <summary>The daily start time (HHmmss).</summary>
    [Parameter(Position = 13)]
    public string? StartTime { get; set; }

    /// <summary>The daily end time (HHmmss).</summary>
    [Parameter(Position = 14)]
    public string? EndTime { get; set; }

    /// <summary>The owner login for the schedule.</summary>
    [Parameter(Position = 15)]
    public string? Owner { get; set; }

    /// <summary>Apply sensible defaults for unset parameters instead of erroring.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - the source declares it bare (every set), which
    // the inherited [Parameter] (no ParameterSetName) already matches; no override needed.

    // The function-scope interrupt set by the begin validations; carried to guard process + end.
    private bool _interrupted;

    // begin-computed values the process block reads. All one-shot (pure functions of the non-pipeline
    // parameters), captured once from the begin sentinel and threaded into every process record.
    private object? _frequencyType;
    private object? _frequencySubdayType;
    private object? _frequencySubdayInterval;
    private object? _frequencyRelativeInterval;
    private object? _frequencyRecurrenceFactor;
    private object? _interval;
    private object? _schedule;
    private object? _startDate;
    private object? _endDate;
    private object? _startTime;
    private object? _endTime;
    private object? _activeStartDate;
    private object? _activeEndDate;
    private object? _activeStartTimeOfDay;
    private object? _activeEndTimeOfDay;

    // $jobschedule persists across pipeline records via function scope in the source (created inside
    // the ShouldProcess block but read outside it), so it is carried record-to-record.
    private object? _jobschedule;

    protected override void BeginProcessing()
    {
        if (Interrupted)
        {
            return;
        }

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BeginScript,
            SqlInstance, Schedule, FrequencyType, FrequencyInterval, FrequencySubdayType,
            FrequencySubdayInterval, FrequencyRelativeInterval, FrequencyRecurrenceFactor, FrequencyText,
            StartDate, EndDate, StartTime, EndTime, Force.ToBool(), EnableException.ToBool(),
            NestedCommand.BoundCommonParameter(this, "Verbose"), NestedCommand.BoundCommonParameter(this, "Debug")))
        {
            if (item?.BaseObject is Hashtable sentinel && sentinel.ContainsKey("__newDbaAgentScheduleBegin"))
            {
                if (sentinel["__newDbaAgentScheduleBegin"] is Hashtable state)
                {
                    _interrupted = LanguagePrimitives.IsTrue(state["Interrupted"]);
                    _frequencyType = state["FrequencyType"];
                    _frequencySubdayType = state["FrequencySubdayType"];
                    _frequencySubdayInterval = state["FrequencySubdayInterval"];
                    _frequencyRelativeInterval = state["FrequencyRelativeInterval"];
                    _frequencyRecurrenceFactor = state["FrequencyRecurrenceFactor"];
                    _interval = state["Interval"];
                    _schedule = state["Schedule"];
                    _startDate = state["StartDate"];
                    _endDate = state["EndDate"];
                    _startTime = state["StartTime"];
                    _endTime = state["EndTime"];
                    _activeStartDate = state["ActiveStartDate"];
                    _activeEndDate = state["ActiveEndDate"];
                    _activeStartTimeOfDay = state["ActiveStartTimeOfDay"];
                    _activeEndTimeOfDay = state["ActiveEndTimeOfDay"];
                }
                continue;
            }
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                NestedCommand.RemoveDuplicateError(this, nestedError);
                WriteError(nestedError);
                continue;
            }
            WriteObject(item);
        }
    }

    protected override void ProcessRecord()
    {
        if (Interrupted || _interrupted)
        {
            return;
        }

        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is Hashtable sentinel && sentinel.ContainsKey("__newDbaAgentScheduleProcess"))
            {
                if (sentinel["__newDbaAgentScheduleProcess"] is Hashtable state)
                {
                    _jobschedule = state["JobSchedule"];
                }
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
            SqlInstance, SqlCredential, Job, Disabled.ToBool(), Owner, _schedule, _interval,
            _frequencyType, _frequencySubdayType, _frequencySubdayInterval, _frequencyRelativeInterval,
            _frequencyRecurrenceFactor, _startDate, _endDate, _startTime, _endTime, _activeStartDate,
            _activeEndDate, _activeStartTimeOfDay, _activeEndTimeOfDay, _jobschedule, Force.ToBool(),
            EnableException.ToBool(), this,
            NestedCommand.BoundCommonParameter(this, "WhatIf"), NestedCommand.BoundCommonParameter(this, "Confirm"),
            NestedCommand.BoundCommonParameter(this, "Verbose"), NestedCommand.BoundCommonParameter(this, "Debug"));
    }

    protected override void EndProcessing()
    {
        if (Interrupted || _interrupted)
        {
            return;
        }

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, EndScript,
            EnableException.ToBool(),
            NestedCommand.BoundCommonParameter(this, "Verbose"), NestedCommand.BoundCommonParameter(this, "Debug")))
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                NestedCommand.RemoveDuplicateError(this, nestedError);
                WriteError(nestedError);
                continue;
            }
            WriteObject(item);
        }
    }

    // PS: the begin block VERBATIM apart from -FunctionName New-DbaAgentSchedule on the direct
    // Stop-Function/Write-Message sites. The "if (\$Force) { \$ConfirmPreference = 'none' }" line is
    // folded into the process hop. Dot-sourced so the validation returns still emit the sentinel, which
    // carries both the interrupt flag and the fifteen computed values the process block reads.
    private const string BeginScript = """
param($SqlInstance, $Schedule, $FrequencyType, $FrequencyInterval, $FrequencySubdayType, $FrequencySubdayInterval, $FrequencyRelativeInterval, $FrequencyRecurrenceFactor, $FrequencyText, $StartDate, $EndDate, $StartTime, $EndTime, $Force, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [object]$Schedule, [object]$FrequencyType, [object[]]$FrequencyInterval, [object]$FrequencySubdayType, [int]$FrequencySubdayInterval, [object]$FrequencyRelativeInterval, [int]$FrequencyRecurrenceFactor, [string]$FrequencyText, [string]$StartDate, [string]$EndDate, [string]$StartTime, [string]$EndTime, $Force, $EnableException)
    . {
        if ($FrequencyText) {
            if ($FrequencyText -match 'every(\s+(?<interval>\d+))?\s+(?<unit>minute|hour|day|sunday|monday|tuesday|wednesday|thursday|friday|saturday)s?(\s+starting)?(\s+at\s+(?<start>\d\d:\d\d:\d\d))?') {
                $textInterval = $Matches['interval']
                $textUnit = $Matches['unit']
                $textStart = $Matches['start']

                if (-not $textInterval) {
                    $textInterval = 1
                }

                if ($textUnit -in 'minute', 'hour', 'day') {
                    $FrequencyType = 'Daily'
                    if ($textUnit -in 'minute', 'hour') {
                        $FrequencySubdayType = $textUnit
                        $FrequencySubdayInterval = $textInterval
                    }
                } else {
                    $FrequencyType = 'Weekly'
                    $FrequencyInterval = $textUnit
                }

                if ($textStart) {
                    $StartTime = $textStart.Replace(':', '')
                }

                if (-not $Schedule) {
                    $Schedule = $FrequencyText
                }

                $Force = $true
            } else {
                Stop-Function -Message "FrequencyText can not be parsed." -FunctionName New-DbaAgentSchedule
                return
            }
        }

        if ($FrequencyType -eq "Daily" -and -not $FrequencyInterval) {
            $FrequencyInterval = 1
        }

        # if a Schedule is not provided there is no much point
        if (-not $Schedule) {
            Stop-Function -Message "A schedule was not provided! Please provide a schedule name." -FunctionName New-DbaAgentSchedule
            return
        }

        [int]$interval = 0

        # Translate FrequencyType value from string to the integer value
        [int]$FrequencyType =
        switch ($FrequencyType) {
            "Once" { 1 }
            "OneTime" { 1 }
            "Daily" { 4 }
            "Weekly" { 8 }
            "Monthly" { 16 }
            "MonthlyRelative" { 32 }
            "AgentStart" { 64 }
            "AutoStart" { 64 }
            "IdleComputer" { 128 }
            "OnIdle" { 128 }
            default { 1 }
        }

        # Translate FrequencySubdayType value from string to the integer value
        [int]$FrequencySubdayType =
        switch ($FrequencySubdayType) {
            "Once" { 1 }
            "Time" { 1 }
            "Seconds" { 2 }
            "Second" { 2 }
            "Minutes" { 4 }
            "Minute" { 4 }
            "Hours" { 8 }
            "Hour" { 8 }
            default { 1 }
        }

        # Check if the relative FrequencyInterval value is of type string and set the integer value
        [int]$FrequencyRelativeInterval =
        switch ($FrequencyRelativeInterval) {
            "First" { 1 }
            "Second" { 2 }
            "Third" { 4 }
            "Fourth" { 8 }
            "Last" { 16 }
            "Unused" { 0 }
            default { 0 }
        }

        # Check if the interval for daily frequency is valid
        if (($FrequencyType -eq 4) -and ($FrequencyInterval -lt 1 -or $FrequencyInterval -ge 365) -and (-not ($FrequencyInterval -eq "EveryDay")) -and (-not $Force)) {
            Stop-Function -Message "The daily frequency type requires a frequency interval to be between 1 and 365 or 'EveryDay'." -Target $SqlInstance -FunctionName New-DbaAgentSchedule
            return
        }

        # Check if the recurrence factor is set for weekly or monthly interval
        if (($FrequencyType -in (16, 8)) -and $FrequencyRecurrenceFactor -lt 1) {
            if ($Force) {
                $FrequencyRecurrenceFactor = 1
                Write-Message -Message "Recurrence factor not set for weekly or monthly interval. Setting it to $FrequencyRecurrenceFactor." -Level Verbose -FunctionName New-DbaAgentSchedule -ModuleName "dbatools"
            } else {
                Stop-Function -Message "The recurrence factor $FrequencyRecurrenceFactor (parameter FrequencyRecurrenceFactor) needs to be at least one when using a weekly or monthly interval." -Target $SqlInstance -FunctionName New-DbaAgentSchedule
                return
            }
        }

        # Check the subday interval
        if (($FrequencySubdayType -in 2, "Seconds") -and (-not ($FrequencySubdayInterval -ge 10 -or $FrequencySubdayInterval -le 59))) {
            Stop-Function -Message "Subday interval $FrequencySubdayInterval must be between 10 and 59 when subday type is 'Seconds'" -Target $SqlInstance -FunctionName New-DbaAgentSchedule
            return
        } elseif (($FrequencySubdayType -in 4, "Minutes") -and (-not ($FrequencySubdayInterval -ge 1 -or $FrequencySubdayInterval -le 59))) {
            Stop-Function -Message "Subday interval $FrequencySubdayInterval must be between 1 and 59 when subday type is 'Minutes'" -Target $SqlInstance -FunctionName New-DbaAgentSchedule
            return
        } elseif (($FrequencySubdayType -eq 8, "Hours") -and (-not ($FrequencySubdayInterval -ge 1 -and $FrequencySubdayInterval -le 23))) {
            Stop-Function -Message "Subday interval $FrequencySubdayInterval must be between 1 and 23 when subday type is 'Hours'" -Target $SqlInstance -FunctionName New-DbaAgentSchedule
            return
        }

        # If the FrequencyInterval is set for the daily FrequencyType
        if ($FrequencyType -eq 4) {
            # Create the interval to hold the value(s)
            [int]$interval = 1

            if ($FrequencyInterval -and $FrequencyInterval[0].GetType().Name -eq 'Int32') {
                $interval = $FrequencyInterval[0]
            }
        }

        # If the FrequencyInterval is set for the weekly FrequencyType
        if ($FrequencyType -in 8, 'Weekly') {
            # Create the interval to hold the value(s)
            [int]$interval = 0

            # Loop through the array
            foreach ($item in $FrequencyInterval) {

                switch ($item) {
                    "Sunday" { $interval += 1 }
                    "Monday" { $interval += 2 }
                    "Tuesday" { $interval += 4 }
                    "Wednesday" { $interval += 8 }
                    "Thursday" { $interval += 16 }
                    "Friday" { $interval += 32 }
                    "Saturday" { $interval += 64 }
                    "Weekdays" { $interval = 62 }
                    "Weekend" { $interval = 65 }
                    "EveryDay" { $interval = 127 }
                    1 { $interval += 1 }
                    2 { $interval += 2 }
                    4 { $interval += 4 }
                    8 { $interval += 8 }
                    16 { $interval += 16 }
                    32 { $interval += 32 }
                    64 { $interval += 64 }
                    62 { $interval = 62 }
                    65 { $interval = 65 }
                    120 { $interval = 120 }
                    121 { $interval = 121 }
                    122 { $interval = 122 }
                    123 { $interval = 123 }
                    124 { $interval = 124 }
                    125 { $interval = 125 }
                    126 { $interval = 126 }
                    127 { $interval = 127 }
                    default { $interval = 0 }
                }
            }
        }

        # If the FrequencyInterval is set for the monthly FrequencyInterval
        if ($FrequencyType -in 16, 'Monthly') {
            # Create the interval to hold the value(s)
            [int]$interval = 0

            # Loop through the array
            foreach ($item in $FrequencyInterval) {
                switch ($item) {
                    { [int]$_ -ge 1 -and [int]$_ -le 31 } { $interval = [int]$item }
                }
            }
        }

        # If the FrequencyInterval is set for the relative monthly FrequencyInterval
        if ($FrequencyType -eq 32) {
            # Create the interval to hold the value(s)
            [int]$interval = 0

            # Loop through the array
            foreach ($item in $FrequencyInterval) {
                switch ($item) {
                    "Sunday" { $interval += 1 }
                    "Monday" { $interval += 2 }
                    "Tuesday" { $interval += 3 }
                    "Wednesday" { $interval += 4 }
                    "Thursday" { $interval += 5 }
                    "Friday" { $interval += 6 }
                    "Saturday" { $interval += 7 }
                    "Day" { $interval += 8 }
                    "Weekdays" { $interval += 9 }
                    "WeekendDay" { $interval += 10 }
                    1 { $interval += 1 }
                    2 { $interval += 2 }
                    3 { $interval += 3 }
                    4 { $interval += 4 }
                    5 { $interval += 5 }
                    6 { $interval += 6 }
                    7 { $interval += 7 }
                    8 { $interval += 8 }
                    9 { $interval += 9 }
                    10 { $interval += 10 }
                }
            }
        }

        # Check if the interval is valid for the frequency
        if ($FrequencyType -eq 0) {
            if ($Force) {
                Write-Message -Message "Parameter FrequencyType must be set to at least [Once]. Setting it to 'Once'." -Level Warning -FunctionName New-DbaAgentSchedule -ModuleName "dbatools"
                $FrequencyType = 1
            } else {
                Stop-Function -Message "Parameter FrequencyType must be set to at least [Once]" -Target $SqlInstance -FunctionName New-DbaAgentSchedule
                return
            }
        }

        # Check if the interval is valid for the frequency
        if (($FrequencyType -in 4, 8, 32) -and ($interval -lt 1)) {
            if ($Force) {
                Write-Message -Message "Parameter FrequencyInterval must be provided for a recurring schedule. Setting it to first day of the week." -Level Warning -FunctionName New-DbaAgentSchedule -ModuleName "dbatools"
                $interval = 1
            } else {
                Stop-Function -Message "Parameter FrequencyInterval must be provided for a recurring schedule." -Target $SqlInstance -FunctionName New-DbaAgentSchedule
                return
            }
        }

        # Check the start date
        if (-not $StartDate -and $Force) {
            $StartDate = Get-Date -Format 'yyyyMMdd'
            Write-Message -Message "Start date was not set. Force is being used. Setting it to $StartDate" -Level Verbose -FunctionName New-DbaAgentSchedule -ModuleName "dbatools"
        } elseif (-not $StartDate) {
            Stop-Function -Message "Please enter a start date or use -Force to use defaults." -Target $SqlInstance -FunctionName New-DbaAgentSchedule
            return
        }
        try {
            $activeStartDate = New-Object System.DateTime($StartDate.Substring(0, 4), $StartDate.Substring(4, 2), $StartDate.Substring(6, 2))
        } catch {
            Stop-Function -Message "Start date $StartDate needs to be a valid date with format yyyyMMdd." -Target $SqlInstance -FunctionName New-DbaAgentSchedule
            return
        }

        # Check the end date
        if (-not $EndDate -and $Force) {
            $EndDate = '99991231'
            Write-Message -Message "End date was not set. Force is being used. Setting it to $EndDate" -Level Verbose -FunctionName New-DbaAgentSchedule -ModuleName "dbatools"
        } elseif (-not $EndDate) {
            Stop-Function -Message "Please enter an end date or use -Force to use defaults." -Target $SqlInstance -FunctionName New-DbaAgentSchedule
            return
        }
        try {
            $activeEndDate = New-Object System.DateTime($EndDate.Substring(0, 4), $EndDate.Substring(4, 2), $EndDate.Substring(6, 2))
        } catch {
            Stop-Function -Message "End date $EndDate needs to be a valid date with format yyyyMMdd." -Target $SqlInstance -FunctionName New-DbaAgentSchedule
            return
        }
        if ($activeEndDate -lt $activeStartDate) {
            Stop-Function -Message "End date $EndDate cannot be before start date $StartDate." -Target $SqlInstance -FunctionName New-DbaAgentSchedule
            return
        }

        # Check the start time
        if (-not $StartTime -and $Force) {
            $StartTime = '000000'
            Write-Message -Message "Start time was not set. Force is being used. Setting it to $StartTime" -Level Verbose -FunctionName New-DbaAgentSchedule -ModuleName "dbatools"
        } elseif (-not $StartTime) {
            Stop-Function -Message "Please enter a start time or use -Force to use defaults." -Target $SqlInstance -FunctionName New-DbaAgentSchedule
            return
        }
        try {
            $activeStartTimeOfDay = New-Object System.TimeSpan($StartTime.Substring(0, 2), $StartTime.Substring(2, 2), $StartTime.Substring(4, 2))
        } catch {
            Stop-Function -Message "Start time $StartTime needs to be a valid time with format HHmmss." -Target $SqlInstance -FunctionName New-DbaAgentSchedule
            return
        }

        # Check the end time
        if (-not $EndTime -and $Force) {
            $EndTime = '235959'
            Write-Message -Message "End time was not set. Force is being used. Setting it to $EndTime" -Level Verbose -FunctionName New-DbaAgentSchedule -ModuleName "dbatools"
        } elseif (-not $EndTime) {
            Stop-Function -Message "Please enter an end time or use -Force to use defaults." -Target $SqlInstance -FunctionName New-DbaAgentSchedule
            return
        }
        try {
            $activeEndTimeOfDay = New-Object System.TimeSpan($EndTime.Substring(0, 2), $EndTime.Substring(2, 2), $EndTime.Substring(4, 2))
        } catch {
            Stop-Function -Message "End time $EndTime needs to be a valid time with format HHmmss." -Target $SqlInstance -FunctionName New-DbaAgentSchedule
            return
        }
    }
    $__iv = Get-Variable -Name __dbatools_interrupt_function_78Q9VPrM6999g6zo24Qn83m09XF56InEn4hFrA8Fwhu5xJrs6r -Scope 0 -ErrorAction Ignore
    @{ __newDbaAgentScheduleBegin = @{ Interrupted = [bool]($__iv -and $__iv.Value); FrequencyType = $FrequencyType; FrequencySubdayType = $FrequencySubdayType; FrequencySubdayInterval = $FrequencySubdayInterval; FrequencyRelativeInterval = $FrequencyRelativeInterval; FrequencyRecurrenceFactor = $FrequencyRecurrenceFactor; Interval = $interval; Schedule = $Schedule; StartDate = $StartDate; EndDate = $EndDate; StartTime = $StartTime; EndTime = $EndTime; ActiveStartDate = $activeStartDate; ActiveEndDate = $activeEndDate; ActiveStartTimeOfDay = $activeStartTimeOfDay; ActiveEndTimeOfDay = $activeEndTimeOfDay } }
} $SqlInstance $Schedule $FrequencyType $FrequencyInterval $FrequencySubdayType $FrequencySubdayInterval $FrequencyRelativeInterval $FrequencyRecurrenceFactor $FrequencyText $StartDate $EndDate $StartTime $EndTime $Force $EnableException @__commonParameters 3>&1 2>&1
""";

    // PS: the process block VERBATIM apart from $PSCmdlet.ShouldProcess -> $__gate.ShouldProcess and
    // -FunctionName New-DbaAgentSchedule on the direct Stop-Function/Write-Message sites. The begin
    // Force/ConfirmPreference line + gate selection are prepended (using the USER -Force). The body is
    // dot-sourced so any early exit still reaches the $jobschedule sentinel. Test-FunctionInterrupt is
    // preserved verbatim but inert (the C# guard already short-circuits an interrupted record).
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Job, $Disabled, $Owner, $Schedule, $interval, $FrequencyType, $FrequencySubdayType, $FrequencySubdayInterval, $FrequencyRelativeInterval, $FrequencyRecurrenceFactor, $StartDate, $EndDate, $StartTime, $EndTime, $activeStartDate, $activeEndDate, $activeStartTimeOfDay, $activeEndTimeOfDay, $jobschedule, $Force, $EnableException, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = "Low")]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, $SqlCredential, [object[]]$Job, $Disabled, [string]$Owner, $Schedule, $interval, $FrequencyType, $FrequencySubdayType, $FrequencySubdayInterval, $FrequencyRelativeInterval, $FrequencyRecurrenceFactor, $StartDate, $EndDate, $StartTime, $EndTime, $activeStartDate, $activeEndDate, $activeStartTimeOfDay, $activeEndTimeOfDay, $jobschedule, $Force, $EnableException, $__realCmdlet)
    if ($Force) { $ConfirmPreference = 'none' }
    $__gate = if ($Force) { $PSCmdlet } else { $__realCmdlet }
    . {
        if (Test-FunctionInterrupt) { return }

        foreach ($instance in $SqlInstance) {
            try {
                $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
            } catch {
                Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName New-DbaAgentSchedule
            }
            # Create the schedule
            if ($__gate.ShouldProcess($instance, "Adding the schedule $schedule")) {
                try {
                    Write-Message -Message "Adding the schedule $jobschedule on instance $instance" -Level Verbose -FunctionName New-DbaAgentSchedule -ModuleName "dbatools"

                    # Create the schedule
                    try {
                        $jobschedule = New-Object Microsoft.SqlServer.Management.Smo.Agent.JobSchedule($Server.JobServer, $Schedule)
                    } catch {
                        if ($_.Exception.Message -match "newParent") {
                            Stop-Function -Message "Cannot create agent schedule through a contained availability group listener. SQL Server Agent objects are instance-level and must be managed on the instance directly. Please connect to the primary replica instead of the listener. Use Get-DbaAvailabilityGroup to find the current primary replica." -ErrorRecord $_ -Target $instance -Continue -FunctionName New-DbaAgentSchedule
                            return
                        } else {
                            throw
                        }
                    }

                    #region job schedule options
                    if ($Disabled) {
                        Write-Message -Message "Setting job schedule to disabled" -Level Verbose -FunctionName New-DbaAgentSchedule -ModuleName "dbatools"
                        $jobschedule.IsEnabled = $false
                    } else {
                        Write-Message -Message "Setting job schedule to enabled" -Level Verbose -FunctionName New-DbaAgentSchedule -ModuleName "dbatools"
                        $jobschedule.IsEnabled = $true
                    }

                    if ($interval -ge 1) {
                        Write-Message -Message "Setting job schedule frequency interval to $interval" -Level Verbose -FunctionName New-DbaAgentSchedule -ModuleName "dbatools"
                        $jobschedule.FrequencyInterval = $interval
                    }

                    if ($FrequencyType -ge 1) {
                        Write-Message -Message "Setting job schedule frequency to $FrequencyType" -Level Verbose -FunctionName New-DbaAgentSchedule -ModuleName "dbatools"
                        $jobschedule.FrequencyTypes = $FrequencyType
                    }

                    if ($FrequencySubdayType -ge 1) {
                        Write-Message -Message "Setting job schedule frequency subday type to $FrequencySubdayType" -Level Verbose -FunctionName New-DbaAgentSchedule -ModuleName "dbatools"
                        $jobschedule.FrequencySubDayTypes = $FrequencySubdayType
                    }

                    if ($FrequencySubdayInterval -ge 1) {
                        Write-Message -Message "Setting job schedule frequency subday interval to $FrequencySubdayInterval" -Level Verbose -FunctionName New-DbaAgentSchedule -ModuleName "dbatools"
                        $jobschedule.FrequencySubDayInterval = $FrequencySubdayInterval
                    }

                    if (($FrequencyRelativeInterval -ge 1) -and ($FrequencyType -eq 32)) {
                        Write-Message -Message "Setting job schedule frequency relative interval to $FrequencyRelativeInterval" -Level Verbose -FunctionName New-DbaAgentSchedule -ModuleName "dbatools"
                        $jobschedule.FrequencyRelativeIntervals = $FrequencyRelativeInterval
                    }

                    if (($FrequencyRecurrenceFactor -ge 1) -and ($FrequencyType -in 8, 16, 32)) {
                        Write-Message -Message "Setting job schedule frequency recurrence factor to $FrequencyRecurrenceFactor" -Level Verbose -FunctionName New-DbaAgentSchedule -ModuleName "dbatools"
                        $jobschedule.FrequencyRecurrenceFactor = $FrequencyRecurrenceFactor
                    }

                    Write-Message -Message "Setting job schedule start date to $StartDate / $activeStartDate" -Level Verbose -FunctionName New-DbaAgentSchedule -ModuleName "dbatools"
                    $jobschedule.ActiveStartDate = $activeStartDate

                    Write-Message -Message "Setting job schedule end date to $EndDate / $activeEndDate" -Level Verbose -FunctionName New-DbaAgentSchedule -ModuleName "dbatools"
                    $jobschedule.ActiveEndDate = $activeEndDate

                    Write-Message -Message "Setting job schedule start time to $StartTime / $activeStartTimeOfDay" -Level Verbose -FunctionName New-DbaAgentSchedule -ModuleName "dbatools"
                    $jobschedule.ActiveStartTimeOfDay = $activeStartTimeOfDay

                    Write-Message -Message "Setting job schedule end time to $EndTime / $activeEndTimeOfDay" -Level Verbose -FunctionName New-DbaAgentSchedule -ModuleName "dbatools"
                    $jobschedule.ActiveEndTimeOfDay = $activeEndTimeOfDay

                    if ($Owner) {
                        $jobschedule.OwnerLoginName = $Owner
                    }

                    $jobschedule.Create()

                    Write-Message -Message "Job schedule created with UID $($jobschedule.ScheduleUid)" -Level Verbose -FunctionName New-DbaAgentSchedule -ModuleName "dbatools"
                } catch {
                    Stop-Function -Message "Something went wrong adding the schedule." -Target $instance -ErrorRecord $_ -Continue -FunctionName New-DbaAgentSchedule
                }
                $null = $server.Refresh()
                $null = $server.JobServer.Refresh()
                $null = $server.JobServer.SharedSchedules.Refresh()
                Add-TeppCacheItem -SqlInstance $server -Type schedule -Name $Schedule
            }
            if ($Job) {
                $jobs = Get-DbaAgentJob -SqlInstance $server -Job $Job
                foreach ($j in $jobs) {
                    if ($__gate.ShouldProcess($instance, "Adding the schedule $schedule to job $($j.Name)")) {
                        Write-Message -Message "Adding schedule $Schedule to job $($j.Name)" -Level Verbose -FunctionName New-DbaAgentSchedule -ModuleName "dbatools"
                        $j.AddSharedSchedule($jobschedule.Id)
                        $jobschedule.Refresh()
                    }
                }
            }
            # Output the job schedule
            if ($jobschedule) {
                Get-DbaAgentSchedule -SqlInstance $server -ScheduleUid $jobschedule.ScheduleUid
            }
        }
    }
    @{ __newDbaAgentScheduleProcess = @{ JobSchedule = $jobschedule } }
} $SqlInstance $SqlCredential $Job $Disabled $Owner $Schedule $interval $FrequencyType $FrequencySubdayType $FrequencySubdayInterval $FrequencyRelativeInterval $FrequencyRecurrenceFactor $StartDate $EndDate $StartTime $EndTime $activeStartDate $activeEndDate $activeStartTimeOfDay $activeEndTimeOfDay $jobschedule $Force $EnableException $__realCmdlet @__commonParameters 3>&1 2>&1
""";

    // PS: the end block VERBATIM apart from -FunctionName New-DbaAgentSchedule on the direct
    // Write-Message. The Test-FunctionInterrupt check is preserved verbatim but inert (the C#
    // EndProcessing guard already short-circuits an interrupted pipeline).
    private const string EndScript = """
param($EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param($EnableException)
    if (Test-FunctionInterrupt) { return }
    Write-Message -Message "Finished creating job schedule(s)." -Level Verbose -FunctionName New-DbaAgentSchedule -ModuleName "dbatools"
} $EnableException @__commonParameters 3>&1 2>&1
""";
}
