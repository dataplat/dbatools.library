#nullable enable

using System;
using System.Collections;
using System.Management.Automation;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Finds SQL Server backup files on disk older than a retention period. Port of
/// public/Find-DbaBackup.ps1 (W3-018). A READ-ONLY getter - it emits FileInfo objects (any deletion
/// is done by a downstream "| Remove-Item", not here). Filesystem-based, no SqlInstance. The command
/// takes no pipeline input, so the process block runs once; the begin block defines the nested
/// Convert-UserFriendlyRetentionToDatetime function and runs the -BackupFileExtension / -Path
/// validations, all consumed within the same single invocation, so begin folds into the process
/// script (splitting would strand the nested function). The begin Path-not-found Stop-Function sets
/// the interrupt that the process's Test-FunctionInterrupt then honors - order preserved by inlining
/// begin ahead of that check. DEF-001 IS reachable here (codex): the post-emit warning path can
/// TERMINATE under -WarningAction Stop after files have already streamed - a buffered hop would
/// lose them, so InvokeScopedStreaming is REQUIRED, not merely uniform. Positions match the retired function (Path=0, BackupFileExtension=1, RetentionPeriod=2;
/// CheckArchiveBit/EnableException=switch/null) and the Path alias (BackupFolder) is preserved.
/// Substitution only: explicit -FunctionName Find-DbaBackup on Stop-Function (W1-090); the body is
/// otherwise verbatim. Surface pinned by migration/baselines/Find-DbaBackup.json.
/// </summary>
[Cmdlet(VerbsCommon.Find, "DbaBackup")]
public sealed class FindDbaBackupCommand : DbaBaseCmdlet
{
    /// <summary>Full path to the root level backup folder.</summary>
    [Parameter(Mandatory = true, Position = 0, HelpMessage = "Full path to the root level backup folder (ex. 'C:\\SQL\\Backups'")]
    [Alias("BackupFolder")]
    public string? Path { get; set; }

    /// <summary>Backup file extension to find (ex. bak, trn, dif).</summary>
    [Parameter(Mandatory = true, Position = 1, HelpMessage = "Backup File extension to remove (ex. bak, trn, dif)")]
    public string? BackupFileExtension { get; set; }

    /// <summary>Backup retention period (ex. 24h, 7d, 4w, 6m).</summary>
    [Parameter(Mandatory = true, Position = 2, HelpMessage = "Backup retention period. (ex. 24h, 7d, 4w, 6m)")]
    public string? RetentionPeriod { get; set; }

    /// <summary>Only includes files whose Archive bit is clear (already backed up elsewhere).</summary>
    [Parameter]
    public SwitchParameter CheckArchiveBit { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

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
                return;
            }
            WriteObject(item);
        }, ProcessScript,
            Path, BackupFileExtension, RetentionPeriod, CheckArchiveBit.ToBool(), EnableException.ToBool(),
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

    // PS: the begin block (the nested Convert-UserFriendlyRetentionToDatetime function + the
    // -BackupFileExtension / -Path validations) inlines ahead of the process body, which is VERBATIM
    // (the command has no pipeline input, so both blocks run once). Substitution only: explicit
    // -FunctionName Find-DbaBackup on Stop-Function (W1-090).
    private const string ProcessScript = """
param($Path, $BackupFileExtension, $RetentionPeriod, $CheckArchiveBit, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([string]$Path, [string]$BackupFileExtension, [string]$RetentionPeriod, $CheckArchiveBit, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    ### Local Functions
    function Convert-UserFriendlyRetentionToDatetime {
        [cmdletbinding()]
        param (
            [string]$UserFriendlyRetention
        )

        <#
        Convert a user friendly retention value into a datetime.
        The last character of the string will indicate units (validated)
        Valid units are: (h = hours, d = days, w = weeks, m = months)

        The preceeding characters are the value and must be an integer (validated)

        Examples:
            '48h' = 48 hours
            '7d' = 7 days
            '4w' = 4 weeks
            '1m' = 1 month
        #>

        [int]$Length = ($UserFriendlyRetention).Length
        $Value = ($UserFriendlyRetention).Substring(0, $Length - 1)
        $Units = ($UserFriendlyRetention).Substring($Length - 1, 1)

        # Validate that $Units is an accepted unit of measure
        if ( $Units -notin @('h', 'd', 'w', 'm') ) {
            throw "RetentionPeriod '$UserFriendlyRetention' units invalid! See Get-Help for correct formatting and examples."
        }

        # Validate that $Value is an INT
        if ( ![int]::TryParse($Value, [ref]"") ) {
            throw "RetentionPeriod '$UserFriendlyRetention' format invalid! See Get-Help for correct formatting and examples."
        }

        switch ($Units) {
            #Variable marked as unused by PSScriptAnalyzer
            'h' { <# $UnitString = 'Hours';#> [datetime]$ReturnDatetime = (Get-Date).AddHours( - $Value) }
            'd' { <# $UnitString = 'Days';#> [datetime]$ReturnDatetime = (Get-Date).AddDays( - $Value) }
            'w' { <# $UnitString = 'Weeks';#> [datetime]$ReturnDatetime = (Get-Date).AddDays( - $Value * 7) }
            'm' { <# $UnitString = 'Months';#> [datetime]$ReturnDatetime = (Get-Date).AddMonths( - $Value) }
        }
        $ReturnDatetime
    }

    # Validations
    # Ensure BackupFileExtension does not begin with a .
    if ($BackupFileExtension -match "^[.]") {
        Write-Message -Level Warning -Message "Parameter -BackupFileExtension begins with a period '$BackupFileExtension'. A period is automatically prepended to -BackupFileExtension and need not be passed in." -FunctionName Find-DbaBackup -ModuleName "dbatools"
    }
    # Ensure Path is a proper path
    if (!(Test-Path $Path -PathType 'Container')) {
        Stop-Function -Message "$Path not found" -FunctionName Find-DbaBackup
    }

    if (Test-FunctionInterrupt) { return }
    # Process stuff
    Write-Message -Message "Finding backups on $Path" -Level Verbose -FunctionName Find-DbaBackup -ModuleName "dbatools"
    # Convert Retention Value to an actual DateTime
    try {
        $RetentionDate = Convert-UserFriendlyRetentionToDatetime -UserFriendlyRetention $RetentionPeriod
        Write-Message -Message "Backup Retention Date set to $RetentionDate" -Level Verbose -FunctionName Find-DbaBackup -ModuleName "dbatools"
    } catch {
        Stop-Function -Message "Failed to interpret retention time." -ErrorRecord $_ -FunctionName Find-DbaBackup
    }

    # Filter out unarchived files if -CheckArchiveBit parameter is used
    if ($CheckArchiveBit) {
        Write-Message -Message "Removing only archived files." -Level Verbose -FunctionName Find-DbaBackup -ModuleName "dbatools"
        filter DbaArchiveBitFilter {
            if ($_.Attributes -notmatch "Archive") {
                $_
            }
        }
    } else {
        filter DbaArchiveBitFilter {
            $_
        }
    }
    # Enumeration may take a while. Without resorting to "esoteric" file listing facilities
    # and given we need to fetch at least the LastWriteTime, let's just use "streaming" processing
    # here to avoid issues like described in #970
    Get-ChildItem $Path -Filter "*.$BackupFileExtension" -File -Recurse -ErrorAction SilentlyContinue -ErrorVariable EnumErrors |
        Where-Object LastWriteTime -lt $RetentionDate | DbaArchiveBitFilter
    if ($EnumErrors) {
        Write-Message "Errors encountered enumerating files." -Level Warning -ErrorRecord $EnumErrors -FunctionName Find-DbaBackup -ModuleName "dbatools"
    }
} $Path $BackupFileExtension $RetentionPeriod $CheckArchiveBit $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
