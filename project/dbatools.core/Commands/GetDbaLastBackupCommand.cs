#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Gets the last full, differential, and log backup dates for databases. Port of
/// public/Get-DbaLastBackup.ps1 (W3-041). A READ-ONLY getter (queries backup history via
/// Get-DbaDbBackupHistory and emits a status object per database; no mutation). The begin block
/// defines the nested Get-DbaDateOrNull function and the $StartOfTime constant, both read-only and
/// deterministic, consumed inside the same process body - so they inline into the process script
/// (recomputed identically per pipeline record; no cross-record state, no sentinel). DEF-001
/// cond1+cond2: the process foreach EMITS a decorated result per database (Select-DefaultView) AND has
/// a reachable Stop-Function -Continue at Connect-DbaInstance, so the hop STREAMS via
/// InvokeScopedStreaming. Cross-record-state check: every per-db variable ($Last*/$Since*/$Status/
/// $result) is set before it is read within each db iteration - no stale carry. No ShouldProcess.
/// Positions match the retired function (SqlInstance=0, SqlCredential=1, Database=2, ExcludeDatabase=3;
/// ExcludeReplica/EnableException=switch/null). Substitution only: explicit -FunctionName
/// Get-DbaLastBackup on Stop-Function (W1-090); the body is otherwise verbatim. Surface pinned by
/// migration/baselines/Get-DbaLastBackup.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaLastBackup")]
public sealed class GetDbaLastBackupCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Database(s) to include.</summary>
    [Parameter(Position = 2)]
    public object[]? Database { get; set; }

    /// <summary>Database(s) to exclude.</summary>
    [Parameter(Position = 3)]
    public object[]? ExcludeDatabase { get; set; }

    /// <summary>Excludes databases on non-preferred backup replicas (Availability Groups).</summary>
    [Parameter]
    public SwitchParameter ExcludeReplica { get; set; }

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
            SqlInstance, SqlCredential, Database, ExcludeDatabase, ExcludeReplica.ToBool(), EnableException.ToBool(),
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

    // PS: the begin block (the nested Get-DbaDateOrNull function + the $StartOfTime constant) inlines
    // ahead of the process body, which is VERBATIM per record. Substitution only: explicit -FunctionName
    // Get-DbaLastBackup on Stop-Function (W1-090).
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $ExcludeDatabase, $ExcludeReplica, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [object[]]$Database, [object[]]$ExcludeDatabase, $ExcludeReplica, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    function Get-DbaDateOrNull ($TimeSpan) {
        if ($TimeSpan -eq 0) {
            return $null
        }
        return $TimeSpan
    }
    $StartOfTime = [DbaTimeSpan](New-TimeSpan -Start ([datetime]0))

    foreach ($instance in $SqlInstance) {
        try {
            $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential -MinimumVersion 9
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaLastBackup
        }

        $dbs = $server.Databases | Where-Object { $_.name -ne 'tempdb' }

        if ($Database) {
            $dbs = $dbs | Where-Object Name -In $Database
        }

        if ($ExcludeDatabase) {
            $dbs = $dbs | Where-Object Name -NotIn $ExcludeDatabase
        }

        if ($ExcludeReplica -and $server.IsHadrEnabled) {
            Write-Message -Level Verbose -Message "Excluding non-preferred backup replicas for $instance"
            $notPreferredQuery = "SELECT DB_NAME(database_id) AS DatabaseName FROM sys.databases WHERE sys.fn_hadr_backup_is_preferred_replica(DB_NAME(database_id)) = 0"
            $notPreferredDbs = ($server.Query($notPreferredQuery)).DatabaseName
            if ($notPreferredDbs) {
                $dbs = $dbs | Where-Object { $_.Name -notin $notPreferredDbs }
            }
        }

        if (-not $dbs) {
            Write-Message -Level Verbose -Message "No databases remain to process for $instance after filtering"
            continue
        }

        # Get-DbaDbBackupHistory -Last would make the job in one query but SMO's (and this) report the last backup of this type regardless of the chain
        $FullHistory = Get-DbaDbBackupHistory -SqlInstance $server -Database $dbs.Name -LastFull -IncludeCopyOnly -Raw
        $DiffHistory = Get-DbaDbBackupHistory -SqlInstance $server -Database $dbs.Name -LastDiff -IncludeCopyOnly -Raw
        $LogHistory = Get-DbaDbBackupHistory -SqlInstance $server -Database $dbs.Name -LastLog -IncludeCopyOnly -Raw
        foreach ($db in $dbs) {
            Write-Message -Level Verbose -Message "Processing $db on $instance"

            $LastFullBackup = ($FullHistory | Where-Object Database -EQ $db.Name | Sort-Object -Property End -Descending | Select-Object -First 1).End
            if ($null -ne $LastFullBackup) {
                $SinceFull_ = [DbaTimeSpan](New-TimeSpan -Start $LastFullBackup)
            } else {
                $SinceFull_ = $StartOfTime
            }

            $LastFullBackupIsCopyOnly = ($FullHistory | Where-Object Database -EQ $db.Name | Sort-Object -Property End -Descending | Select-Object -First 1).is_copy_only

            $LastDiffBackup = ($DiffHistory | Where-Object Database -EQ $db.Name | Sort-Object -Property End -Descending | Select-Object -First 1).End
            if ($null -ne $LastDiffBackup) {
                $SinceDiff_ = [DbaTimeSpan](New-TimeSpan -Start $LastDiffBackup)
            } else {
                $SinceDiff_ = $StartOfTime
            }

            # LastDiffBackupIsCopyOnly is always false because copy_only is not allowed with differential backups: https://docs.microsoft.com/en-us/sql/t-sql/statements/backup-transact-sql
            # It is tempting to not include this property in the result object, however, it is low-cost to do so and makes the command more self-documenting.
            $LastDiffBackupIsCopyOnly = ($DiffHistory | Where-Object Database -EQ $db.Name | Sort-Object -Property End -Descending | Select-Object -First 1).is_copy_only

            $LastLogBackup = ($LogHistory | Where-Object Database -EQ $db.Name | Sort-Object -Property End -Descending | Select-Object -First 1).End
            if ($null -ne $LastLogBackup) {
                $SinceLog_ = [DbaTimeSpan](New-TimeSpan -Start $LastLogBackup)
            } else {
                $SinceLog_ = $StartOfTime
            }

            $LastLogBackupIsCopyOnly = ($LogHistory | Where-Object Database -EQ $db.Name | Sort-Object -Property End -Descending | Select-Object -First 1).is_copy_only

            $daysSinceDbCreated = (New-TimeSpan -Start $db.createDate).Days

            if ($daysSinceDbCreated -lt 1 -and $SinceFull_ -eq 0) {
                $Status = 'New database, not backed up yet'
            } elseif ($SinceFull_.Days -gt 0 -and $SinceDiff_.Days -gt 0) {
                $Status = 'No Full or Diff Back Up in the last day'
            } elseif ($db.RecoveryModel -eq "Full" -and $SinceLog_.Hours -gt 0) {
                $Status = 'No Log Back Up in the last hour'
            } else {
                $Status = 'OK'
            }

            $result = [PSCustomObject]@{
                ComputerName             = $server.ComputerName
                InstanceName             = $server.ServiceName
                SqlInstance              = $server.DomainInstanceName
                Database                 = $db.Name
                RecoveryModel            = $db.RecoveryModel
                LastFullBackup           = [DbaDateTime]$LastFullBackup
                LastDiffBackup           = [DbaDateTime]$LastDiffBackup
                LastLogBackup            = [DbaDateTime]$LastLogBackup
                SinceFull                = Get-DbaDateOrNull -TimeSpan $SinceFull_
                SinceDiff                = Get-DbaDateOrNull -TimeSpan $SinceDiff_
                SinceLog                 = Get-DbaDateOrNull -TimeSpan $SinceLog_
                LastFullBackupIsCopyOnly = $LastFullBackupIsCopyOnly
                LastDiffBackupIsCopyOnly = $LastDiffBackupIsCopyOnly # always false per https://docs.microsoft.com/en-us/sql/t-sql/statements/backup-transact-sql See comments above.
                LastLogBackupIsCopyOnly  = $LastLogBackupIsCopyOnly
                DatabaseCreated          = $db.createDate
                DaysSinceDbCreated       = $daysSinceDbCreated
                Status                   = $status
            }

            Select-DefaultView -InputObject $result -Property ComputerName, InstanceName, SqlInstance, Database, LastFullBackup, LastDiffBackup, LastLogBackup
        }
    }
} $SqlInstance $SqlCredential $Database $ExcludeDatabase $ExcludeReplica $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
