#nullable enable

using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Returns database mirroring monitor history via sp_dbmmonitorresults, optionally
/// refreshing the monitor table first. Port of public/Get-DbaDbMirrorMonitor.ps1;
/// surface pinned by migration/baselines/Get-DbaDbMirrorMonitor.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbMirrorMonitor")]
public sealed class GetDbaDbMirrorMonitorCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instances using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Restricts results to these databases.</summary>
    [Parameter(Position = 2)]
    [PsStringArrayCast]
    public string[]? Database { get; set; }

    /// <summary>Database objects piped from Get-DbaDatabase.</summary>
    [Parameter(ValueFromPipeline = true, Position = 3)]
    public Microsoft.SqlServer.Management.Smo.Database[]? InputObject { get; set; }

    /// <summary>Refreshes the monitor table before reading results.</summary>
    [Parameter]
    public SwitchParameter Update { get; set; }

    /// <summary>How much monitor history to return.</summary>
    [Parameter(Position = 4)]
    [ValidateSet("LastRow", "LastTwoHours", "LastFourHours", "LastEightHours", "LastDay", "LastTwoDays", "Last100Rows", "Last500Rows", "Last1000Rows", "Last1000000Rows")]
    [PsStringCast]
    public string LimitResults { get; set; } = "LastTwoHours";

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        // Whole-record hop; the begin block's two switch mappings are pure functions of
        // bound parameters and ride verbatim at the hop top (idempotent per record) -
        // including the source's own gap where ValidateSet admits Last1000Rows but the
        // switch never maps it ($rows stays null, verbatim in both worlds). The plain
        // catch Stop-Function's interrupt flag is WRITE-ONLY in this source (no
        // Test-FunctionInterrupt) - inert in both worlds, no carry.
        // [DEF-001] closed via InvokeScopedStreaming (ab7492c). Streaming changes -WhatIf transcript
        // capture (documented observability change, not behaviour); the parity runner strips the
        // transcript gate-message. Fleet-confirmed non-blocker (C's streamed ShouldProcess wave, MSTest 487/487).
        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                NestedCommand.RemoveDuplicateError(this, nestedError);
                WriteError(nestedError);
                return;
            }
            WriteObject(item);
        }, ProcessScript,
            SqlInstance, SqlCredential, Database, InputObject,
            Update.ToBool(), LimitResults, EnableException.ToBool(),
            NestedCommand.BoundCommonParameter(this, "Verbose"), NestedCommand.BoundCommonParameter(this, "Debug"));
    }

    // PS: the begin block's switch mappings then the process block, both VERBATIM and
    // CRLF-preserved (process cmp-proven byte-exact after stripping the two
    // -FunctionName appends; begin byte-verbatim with no substitutions). $Update rides
    // as bool - the begin switch compares against $false/$true, which matches.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $InputObject, $Update, $LimitResults, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [string[]]$Database, [Microsoft.SqlServer.Management.Smo.Database[]]$InputObject, $Update, [string]$LimitResults, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        $rows = switch ($LimitResults) {
            'LastRow' { 0 }
            'LastTwoHours' { 1 }
            'LastFourHours' { 2 }
            'LastEightHours' { 3 }
            'LastDay' { 4 }
            'LastTwoDays' { 5 }
            'Last100Rows' { 6 }
            'Last500Rows' { 7 }
            'Last1000000Rows' { 8 }
        }
        $updatebool = switch ($Update) {
            $false { 0 }
            $true { 1 }
        }

        foreach ($instance in $SqlInstance) {
            $InputObject += Get-DbaDatabase -SqlInstance $instance -SqlCredential $SqlCredential -Database $Database
        }

        foreach ($db in $InputObject) {
            if (-not ($db.Parent.Databases['msdb'].Tables['dbm_monitor_data'].Name)) {
                Stop-Function -Continue -Message "msdb.dbo.dbm_monitor_data not found. Please run Add-DbaDbMirrorMonitor then you can get monitor stats." -FunctionName Get-DbaDbMirrorMonitor
            }
            try {
                $sql = "EXEC msdb.dbo.sp_dbmmonitorresults @database_name = '$db', @mode = $rows, @update_table = $updatebool"
                $results = $db.Parent.Query($sql)

                foreach ($result in $results) {
                    [PSCustomObject]@{
                        ComputerName          = $db.Parent.ComputerName
                        InstanceName          = $db.Parent.ServiceName
                        SqlInstance           = $db.Parent.DomainInstanceName
                        DatabaseName          = $result.database_name
                        Role                  = $result.role
                        MirroringState        = $result.mirroring_state
                        WitnessStatus         = $result.witness_status
                        LogGenerationRate     = $result.log_generation_rate
                        UnsentLog             = $result.unsent_log
                        SendRate              = $result.send_rate
                        UnrestoredLog         = $result.unrestored_log
                        RecoveryRate          = $result.recovery_rate
                        TransactionDelay      = $result.transaction_delay
                        TransactionsPerSecond = $result.transactions_per_sec
                        AverageDelay          = $result.average_delay
                        TimeRecorded          = $result.time_recorded
                        TimeBehind            = $result.time_behind
                        LocalTime             = $result.local_time
                    }
                }
            } catch {
                Stop-Function -Message "Failure" -ErrorRecord $_ -FunctionName Get-DbaDbMirrorMonitor
            }
        }
} $SqlInstance $SqlCredential $Database $InputObject $Update $LimitResults $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
