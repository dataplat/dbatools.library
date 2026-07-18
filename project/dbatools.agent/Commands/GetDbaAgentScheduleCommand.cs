#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves and decorates shared SQL Agent schedules. Description generation, union filtering,
/// process-scope server state, and SMO output shaping remain a module-scoped PowerShell
/// compatibility hop. Surface pinned by migration/baselines/Get-DbaAgentSchedule.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaAgentSchedule")]
public sealed class GetDbaAgentScheduleCommand : DbaBaseCmdlet
{
    /// <summary>Target SQL Server instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    [PsDbaInstanceArrayCast]
    public DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Schedule names to include.</summary>
    [Parameter(Position = 2)]
    public string[]? Schedule { get; set; }

    /// <summary>Schedule unique identifiers to include.</summary>
    [Parameter(Position = 3)]
    public string[]? ScheduleUid { get; set; }

    /// <summary>Numeric schedule identifiers to include.</summary>
    [Parameter(Position = 4)]
    [PsAgentLogIntArrayCast]
    public int[]? Id { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    private object? _server;

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        foreach (DbaInstanceParameter instance in SqlInstance)
        {
            if (Interrupted)
                return;

            foreach (PSObject? item in NestedCommand.InvokeScoped(this, BodyScript,
                new[] { instance }, SqlCredential, Schedule, ScheduleUid, Id, EnableException.ToBool(),
                _server, BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
            {
                if (item?.BaseObject is ErrorRecord nestedError)
                {
                    RemoveHopErrorBookkeeping(nestedError);
                    WriteError(nestedError);
                }
                else if (item is not null && LanguagePrimitives.IsTrue(
                    item.Properties["__GetDbaAgentScheduleProcessComplete"]?.Value))
                {
                    _server = UnwrapHopValue(item.Properties["Server"]?.Value);
                }
                else
                {
                    WriteObject(item);
                }
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

    private const string BodyScript = """
param($SqlInstance, $SqlCredential, $Schedule, $ScheduleUid, $Id, $EnableException, $Server, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, $SqlCredential,
        [string[]]$Schedule, [string[]]$ScheduleUid, [int[]]$Id, $EnableException, $Server)

    function Get-ScheduleDescription {
        param (
            [Parameter(Mandatory)]
            [ValidateNotNullOrEmpty()]
            [object]$currentschedule
        )

        $datetimeFormat = (Get-Culture).DateTimeFormat
        $description = ""
        $startDate = Get-Date $currentschedule.ActiveStartDate -format $datetimeFormat.ShortDatePattern
        $startTime = Get-Date ($currentschedule.ActiveStartTimeOfDay.ToString()) -format $datetimeFormat.LongTimePattern
        $endDate = Get-Date $currentschedule.ActiveEndDate -format $datetimeFormat.ShortDatePattern
        $endTime = Get-Date ($currentschedule.ActiveEndTimeOfDay.ToString()) -format $datetimeFormat.LongTimePattern

        switch ($currentschedule.FrequencyTypes) {
            { ($_ -eq 1) -or ($_ -eq "Once") } { $description += "Occurs on $startDate at $startTime" }
            { ($_ -in 4, 8, 16, 32) -or ($_ -in "Daily", "Weekly", "Monthly") } { $description += "Occurs every " }
            { ($_ -eq 64) -or ($_ -eq "AutoStart") } { $description += "Start automatically when SQL Server Agent starts " }
            { ($_ -eq 128) -or ($_ -eq "OnIdle") } { $description += "Start whenever the CPUs become idle" }
        }

        switch ($currentschedule.FrequencyTypes) {
            { $_ -in 4, "Daily" } {
                if ($currentschedule.FrequencyInterval -eq 1) {
                    $description += "day "
                } elseif ($currentschedule.FrequencyInterval -gt 1) {
                    $description += "$($currentschedule.FrequencyInterval) day(s) "
                }
            }

            { $_ -in 8, "Weekly" } {
                if ($currentschedule.FrequencyRecurrenceFactor -eq 1) {
                    $description += "week on "
                } elseif ($currentschedule.FrequencyRecurrenceFactor -gt 1) {
                    $description += "$($currentschedule.FrequencyRecurrenceFactor) week(s) on "
                }

                $frequencyInterval = $currentschedule.FrequencyInterval
                $days = ($false, $false, $false, $false, $false, $false, $false)

                while ($frequencyInterval -gt 0) {
                    switch (1) {
                        { ($frequencyInterval - 64) -ge 0 } {
                            $days[5] = "Saturday"
                            $frequencyInterval -= 64
                        }
                        { ($frequencyInterval - 32) -ge 0 } {
                            $days[4] = "Friday"
                            $frequencyInterval -= 32
                        }
                        { ($frequencyInterval - 16) -ge 0 } {
                            $days[3] = "Thursday"
                            $frequencyInterval -= 16
                        }
                        { ($frequencyInterval - 8) -ge 0 } {
                            $days[2] = "Wednesday"
                            $frequencyInterval -= 8
                        }
                        { ($frequencyInterval - 4) -ge 0 } {
                            $days[1] = "Tuesday"
                            $frequencyInterval -= 4
                        }
                        { ($frequencyInterval - 2) -ge 0 } {
                            $days[0] = "Monday"
                            $frequencyInterval -= 2
                        }
                        { ($frequencyInterval - 1) -ge 0 } {
                            $days[6] = "Sunday"
                            $frequencyInterval -= 1
                        }
                    }
                }

                $description += ($days | Where-Object { $_ -ne $false }) -join ", "
                $description += " "
            }

            { $_ -in 16, "Monthly" } {
                if ($currentschedule.FrequencyRecurrenceFactor -eq 1) {
                    $description += "month "
                } elseif ($currentschedule.FrequencyRecurrenceFactor -gt 1) {
                    $description += "$($currentschedule.FrequencyRecurrenceFactor) month(s) "
                }
                $description += "on day $($currentschedule.FrequencyInterval) of that month "
            }

            { $_ -in 32, "MonthlyRelative" } {
                switch ($currentschedule.FrequencyRelativeIntervals) {
                    { $_ -in 1, "First" } { $description += "first " }
                    { $_ -in 2, "Second" } { $description += "second " }
                    { $_ -in 4, "Third" } { $description += "third " }
                    { $_ -in 8, "Fourth" } { $description += "fourth " }
                    { $_ -in 16, "Last" } { $description += "last " }
                }

                switch ($currentschedule.FrequencyInterval) {
                    1 { $description += "Sunday " }
                    2 { $description += "Monday " }
                    3 { $description += "Tuesday " }
                    4 { $description += "Wednesday " }
                    5 { $description += "Thursday " }
                    6 { $description += "Friday " }
                    7 { $description += "Saturday " }
                    8 { $description += "Day " }
                    9 { $description += "Weekday " }
                    10 { $description += "Weekend day " }
                }

                $description += "of every $($currentschedule.FrequencyRecurrenceFactor) month(s) "
            }
        }

        if ($currentschedule.FrequencyTypes -notin 64, 128) {
            if ($currentschedule.FrequencySubDayTypes -in 0, 1) {
                $description += "at $startTime. "
            } else {
                switch ($currentschedule.FrequencySubDayTypes) {
                    { $_ -in 2, "Seconds" } { $description += "every $($currentschedule.FrequencySubDayInterval) second(s) " }
                    { $_ -in 4, "Minutes" } { $description += "every $($currentschedule.FrequencySubDayInterval) minute(s) " }
                    { $_ -in 8, "Hours" } { $description += "every $($currentschedule.FrequencySubDayInterval) hour(s) " }
                }
                $description += "between $startTime and $endTime. "
            }

            if ($currentschedule.ActiveEndDate.Year -eq 9999) {
                $description += "Schedule will be used starting on $startDate."
            } else {
                $description += "Schedule will used between $startDate and $endDate."
            }
        }

        return $description
    }

    $server = $Server
    foreach ($instance in $SqlInstance) {
        try {
            $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaAgentSchedule
        }

        if ($server.Edition -like 'Express*') {
            Stop-Function -Message "$($server.Edition) does not support SQL Server Agent. Skipping $server." -Continue -FunctionName Get-DbaAgentSchedule
        }

        $scheduleCollection = @()
        $server.JobServer.SharedSchedules.Refresh()

        if ($Schedule -or $ScheduleUid -or $Id) {
            if ($Schedule) {
                $scheduleCollection += $server.JobServer.SharedSchedules | Where-Object { $_.Name -in $Schedule }
            }
            if ($ScheduleUid) {
                $scheduleCollection += $server.JobServer.SharedSchedules | Where-Object { $_.ScheduleUid -in $ScheduleUid }
            }
            if ($Id) {
                $scheduleCollection += $server.JobServer.SharedSchedules | Where-Object { $_.Id -in $Id }
            }
        } else {
            $scheduleCollection = $server.JobServer.SharedSchedules
        }

        $defaults = "ComputerName", "InstanceName", "SqlInstance", "Name as ScheduleName", "ActiveEndDate", "ActiveEndTimeOfDay", "ActiveStartDate", "ActiveStartTimeOfDay", "DateCreated", "FrequencyInterval", "FrequencyRecurrenceFactor", "FrequencyRelativeIntervals", "FrequencySubDayInterval", "FrequencySubDayTypes", "FrequencyTypes", "IsEnabled", "JobCount", "Description", "ScheduleUid"

        foreach ($currentschedule in $scheduleCollection) {
            $description = Get-ScheduleDescription -CurrentSchedule $currentschedule
            $currentschedule | Add-Member -Type NoteProperty -Name ComputerName -Value $server.ComputerName -Force
            $currentschedule | Add-Member -Type NoteProperty -Name InstanceName -Value $server.ServiceName -Force
            $currentschedule | Add-Member -Type NoteProperty -Name SqlInstance -Value $server.DomainInstanceName -Force
            $currentschedule | Add-Member -Type NoteProperty -Name Description -Value $description -Force
            Select-DefaultView -InputObject $currentschedule -Property $defaults
        }
    }

    [pscustomobject]@{
        __GetDbaAgentScheduleProcessComplete = $true
        Server = $server
    }
} $SqlInstance $SqlCredential $Schedule $ScheduleUid $Id $EnableException $Server @__commonParameters 3>&1 2>&1
""";
}
