#nullable enable

using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Sets up log shipping from a source database to one or more secondaries: backup,
/// copy and restore jobs, schedules, monitor configuration and optional seeding.
/// Port of public/Invoke-DbaDbLogShipping.ps1; surface pinned by
/// migration/baselines/Invoke-DbaDbLogShipping.json. Parameters live in the
/// .Parameters partial; the begin/process scripts in the .BeginScript*/.ProcessScript*
/// partials.
/// </summary>
[Cmdlet(VerbsLifecycle.Invoke, "DbaDbLogShipping", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Medium)]
public sealed partial class InvokeDbaDbLogShippingCommand : DbaBaseCmdlet
{
    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // The begin hop's 47-variable carry (40 default-mutated params + 7 begin locals,
    // AST-inventoried) plus the begin-latch flag; ProcessRecord re-injects the carry
    // and merges each hop's reported interrupt into _hopInterrupted (the source's
    // process-top Test-FunctionInterrupt reads the fn-scope flag that begin's 28
    // plain validation Stop-Function sites and process's 10 plain sites latch).
    private Hashtable? _state;
    private bool _hopInterrupted;

    protected override void BeginProcessing()
    {
        // W3-102 CONTINUE RELAY on the BEGIN hop: the source's line-692
        // `Stop-Function ... -Continue` is loop-less in begin (it fires when Database
        // arrives via pipeline, since begin runs before pipeline binding) - the escape
        // must abort the caller's pipeline exactly like the function world.
        object continueMarker = new object();
        bool continueEscaped = false;
        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BeginScript,
            BuildParameterTable(), ExactlyOneSharedOrAzure(), continueMarker,
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (ReferenceEquals(item?.BaseObject, continueMarker))
            {
                continueEscaped = true;
                continue;
            }
            if (DrainSentinelOrError(item))
            {
                continue;
            }
            WriteObject(item);
        }
        if (continueEscaped)
        {
            NestedCommand.InvokeScoped(this, ContinueScript);
        }
    }

    protected override void ProcessRecord()
    {
        if (Interrupted || _hopInterrupted)
        {
            return;
        }

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            BuildParameterTable(), _state,
            BoundCommonParameter("WhatIf"), BoundCommonParameter("Confirm"),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (DrainSentinelOrError(item))
            {
                continue;
            }
            WriteObject(item);
        }
    }

    protected override void EndProcessing()
    {
        if (Interrupted || _hopInterrupted)
        {
            return;
        }

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, EndScript,
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (DrainSentinelOrError(item))
            {
                continue;
            }
            WriteObject(item);
        }
    }

    private bool ExactlyOneSharedOrAzure()
    {
        int bound = (TestBound(nameof(SharedPath)) ? 1 : 0) + (TestBound(nameof(AzureBaseUrl)) ? 1 : 0);
        return bound == 1;
    }

    private bool DrainSentinelOrError(PSObject? item)
    {
        if (item?.BaseObject is Hashtable sentinel && sentinel.ContainsKey("__w4038State"))
        {
            _state = sentinel["__w4038State"] as Hashtable;
            if (_state is not null && _state["interrupted"] is bool interrupted && interrupted)
            {
                _hopInterrupted = true;
            }
            return true;
        }
        if (item?.BaseObject is ErrorRecord nestedError)
        {
            RemoveHopErrorBookkeeping(nestedError);
            WriteError(nestedError);
            return true;
        }
        return false;
    }

    private object? BoundCommonParameter(string name)
    {
        if (MyInvocation.BoundParameters.TryGetValue(name, out object? value))
        {
            return LanguagePrimitives.IsTrue(value);
        }
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
                string.Equals(first.Exception?.Message, record.Exception?.Message, System.StringComparison.Ordinal))
            {
                errorList.RemoveAt(0);
            }
        }
        catch
        {
            // Best-effort bookkeeping only.
        }
    }

    // PS: the engine-authored `continue` for the begin relay above.
    private const string ContinueScript = """
continue
""";

    // PS: the end block VERBATIM (single Write-Message + append).
    private const string EndScript = """
param($__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param($__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        Write-Message -Message "Finished setting up log shipping." -Level Verbose -FunctionName Invoke-DbaDbLogShipping
} $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";

    private Hashtable BuildParameterTable()
    {
        Hashtable parameters = new Hashtable(System.StringComparer.OrdinalIgnoreCase);
        parameters["SourceSqlInstance"] = SourceSqlInstance;
        parameters["DestinationSqlInstance"] = DestinationSqlInstance;
        parameters["SourceSqlCredential"] = SourceSqlCredential;
        parameters["SourceCredential"] = SourceCredential;
        parameters["DestinationSqlCredential"] = DestinationSqlCredential;
        parameters["DestinationCredential"] = DestinationCredential;
        parameters["Database"] = Database;
        parameters["SharedPath"] = SharedPath;
        parameters["LocalPath"] = LocalPath;
        parameters["AzureBaseUrl"] = AzureBaseUrl;
        parameters["AzureCredential"] = AzureCredential;
        parameters["BackupJob"] = BackupJob;
        parameters["BackupRetention"] = BackupRetention;
        parameters["BackupSchedule"] = BackupSchedule;
        parameters["BackupScheduleFrequencyType"] = BackupScheduleFrequencyType;
        parameters["BackupScheduleFrequencyInterval"] = BackupScheduleFrequencyInterval;
        parameters["BackupScheduleFrequencySubdayType"] = BackupScheduleFrequencySubdayType;
        parameters["BackupScheduleFrequencySubdayInterval"] = BackupScheduleFrequencySubdayInterval;
        parameters["BackupScheduleFrequencyRelativeInterval"] = BackupScheduleFrequencyRelativeInterval;
        parameters["BackupScheduleFrequencyRecurrenceFactor"] = BackupScheduleFrequencyRecurrenceFactor;
        parameters["BackupScheduleStartDate"] = BackupScheduleStartDate;
        parameters["BackupScheduleEndDate"] = BackupScheduleEndDate;
        parameters["BackupScheduleStartTime"] = BackupScheduleStartTime;
        parameters["BackupScheduleEndTime"] = BackupScheduleEndTime;
        parameters["BackupThreshold"] = BackupThreshold;
        parameters["CopyDestinationFolder"] = CopyDestinationFolder;
        parameters["CopyJob"] = CopyJob;
        parameters["CopyRetention"] = CopyRetention;
        parameters["CopySchedule"] = CopySchedule;
        parameters["CopyScheduleFrequencyType"] = CopyScheduleFrequencyType;
        parameters["CopyScheduleFrequencyInterval"] = CopyScheduleFrequencyInterval;
        parameters["CopyScheduleFrequencySubdayType"] = CopyScheduleFrequencySubdayType;
        parameters["CopyScheduleFrequencySubdayInterval"] = CopyScheduleFrequencySubdayInterval;
        parameters["CopyScheduleFrequencyRelativeInterval"] = CopyScheduleFrequencyRelativeInterval;
        parameters["CopyScheduleFrequencyRecurrenceFactor"] = CopyScheduleFrequencyRecurrenceFactor;
        parameters["CopyScheduleStartDate"] = CopyScheduleStartDate;
        parameters["CopyScheduleEndDate"] = CopyScheduleEndDate;
        parameters["CopyScheduleStartTime"] = CopyScheduleStartTime;
        parameters["CopyScheduleEndTime"] = CopyScheduleEndTime;
        parameters["FullBackupPath"] = FullBackupPath;
        parameters["HistoryRetention"] = HistoryRetention;
        parameters["PrimaryMonitorServer"] = PrimaryMonitorServer;
        parameters["PrimaryMonitorCredential"] = PrimaryMonitorCredential;
        parameters["PrimaryMonitorServerSecurityMode"] = PrimaryMonitorServerSecurityMode;
        parameters["RestoreDataFolder"] = RestoreDataFolder;
        parameters["RestoreLogFolder"] = RestoreLogFolder;
        parameters["RestoreDelay"] = RestoreDelay;
        parameters["RestoreAlertThreshold"] = RestoreAlertThreshold;
        parameters["RestoreJob"] = RestoreJob;
        parameters["RestoreRetention"] = RestoreRetention;
        parameters["RestoreSchedule"] = RestoreSchedule;
        parameters["RestoreScheduleFrequencyType"] = RestoreScheduleFrequencyType;
        parameters["RestoreScheduleFrequencyInterval"] = RestoreScheduleFrequencyInterval;
        parameters["RestoreScheduleFrequencySubdayType"] = RestoreScheduleFrequencySubdayType;
        parameters["RestoreScheduleFrequencySubdayInterval"] = RestoreScheduleFrequencySubdayInterval;
        parameters["RestoreScheduleFrequencyRelativeInterval"] = RestoreScheduleFrequencyRelativeInterval;
        parameters["RestoreScheduleFrequencyRecurrenceFactor"] = RestoreScheduleFrequencyRecurrenceFactor;
        parameters["RestoreScheduleStartDate"] = RestoreScheduleStartDate;
        parameters["RestoreScheduleEndDate"] = RestoreScheduleEndDate;
        parameters["RestoreScheduleStartTime"] = RestoreScheduleStartTime;
        parameters["RestoreScheduleEndTime"] = RestoreScheduleEndTime;
        parameters["RestoreThreshold"] = RestoreThreshold;
        parameters["SecondaryDatabasePrefix"] = SecondaryDatabasePrefix;
        parameters["SecondaryDatabaseSuffix"] = SecondaryDatabaseSuffix;
        parameters["SecondaryMonitorServer"] = SecondaryMonitorServer;
        parameters["SecondaryMonitorCredential"] = SecondaryMonitorCredential;
        parameters["SecondaryMonitorServerSecurityMode"] = SecondaryMonitorServerSecurityMode;
        parameters["StandbyDirectory"] = StandbyDirectory;
        parameters["UseBackupFolder"] = UseBackupFolder;
        parameters["BackupScheduleDisabled"] = BackupScheduleDisabled.ToBool();
        parameters["CompressBackup"] = CompressBackup.ToBool();
        parameters["CopyScheduleDisabled"] = CopyScheduleDisabled.ToBool();
        parameters["DisconnectUsers"] = DisconnectUsers.ToBool();
        parameters["Force"] = Force.ToBool();
        parameters["GenerateFullBackup"] = GenerateFullBackup.ToBool();
        parameters["IgnoreFileChecks"] = IgnoreFileChecks.ToBool();
        parameters["NoInitialization"] = NoInitialization.ToBool();
        parameters["NoRecovery"] = NoRecovery.ToBool();
        parameters["PrimaryThresholdAlertEnabled"] = PrimaryThresholdAlertEnabled.ToBool();
        parameters["RestoreScheduleDisabled"] = RestoreScheduleDisabled.ToBool();
        parameters["SecondaryThresholdAlertEnabled"] = SecondaryThresholdAlertEnabled.ToBool();
        parameters["Standby"] = Standby.ToBool();
        parameters["UseExistingFullBackup"] = UseExistingFullBackup.ToBool();

        parameters["EnableException"] = EnableException.ToBool();
        return parameters;
    }
}
