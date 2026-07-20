#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Gets databases from one or more SQL Server instances, with rich filtering. Port of
/// public/Get-DbaDatabase.ps1 (W3-027, WAVE-3 remnant); the workflow remains a module-scoped
/// PowerShell compatibility hop. READ-ONLY (no SupportsShouldProcess).
///
/// BEGIN+PROCESS. -SqlInstance is Mandatory ValueFromPipeline, so process fires per piped instance.
///
/// NO CROSS-RECORD VALUE CARRY, and NO begin -> process value carry - established by triage, not
/// assumed. begin (:244-250) holds only one guard. The three Find-ConditionalCarry candidates were
/// all REFUTED before coding:
///   $pattern - the "foreach ($pattern ...)" loop variable INSIDE the $matchesPattern scriptblock
///     (:357-366); the reads at :370/:393/:403 are the PARAMETER $Pattern (capital P). A detector
///     case-insensitivity false positive, not a carry.
///   $NoLogBackupSince - defaulted at :427-428 only when empty, to "New-Object DateTime" (DateTime
///     .MinValue) + 1ms - a DETERMINISTIC value, so re-computing it per record is idempotent and
///     equals what a carry would hold. No carry.
///   $hasCopyOnly - assigned at :421 inside "if ($NoFullBackup -or $NoFullBackupSince)" (:413) and
///     read at :506 inside the SAME "if ($NoFullBackup -or $NoFullBackupSince)"; both are non-pipeline
///     parameters, constant across records, so it is always-assigned-before-read or never-read - the
///     W2-155 $deploymentReport constant-gate pattern. No carry.
///   $server (codex r1 raised it; REFUTED) - assigned in the connection try (:256), whose catch
///     (:258) is Stop-Function -Continue. The "foreach ($instance in $SqlInstance)" loop opens at
///     :254 and CLOSES at :537, wrapping the whole body, so every $server read (:298/:327/.../:355
///     Invoke-QueryRawDatabases) is a STATEMENT AFTER that catch, inside the loop. The catch's
///     -Continue issues "continue", which is dynamically scoped and unwinds that foreach, so on a
///     connection failure those reads are UNREACHABLE - no stale cross-record $server is observed.
///     The settled continue-in-catch class (read AFTER a -Continue catch is unreachable on failure);
///     measured in migration/logs/probe-20260718-continue-propagation.
///
/// UNBOUND [datetime] SEMANTICS (codex r1, real fix). The source's -NoFullBackupSince and
/// -NoLogBackupSince are $null when the caller omits them; a non-nullable C# DateTime property is
/// MinValue instead, which is TRUTHY in PowerShell - it would wrongly enable the :413 backup filter
/// and defeat the :426 "if (!$NoLogBackupSince)" default init. So the property stays DateTime (the
/// surface is unchanged), but ProcessRecord passes $null into the hop when the parameter was NOT
/// bound (the two inner hop params are received UNTYPED so $null survives), and the real value -
/// including an explicitly-passed MinValue - when it was.
///
/// The query helper functions Invoke-QueryDBlastUsed (:297), Invoke-QueryRawDatabases (:325),
/// Invoke-QueryDatabaseSizes (:467) and the $matchesPattern scriptblock (:357) are all defined IN
/// PROCESS, so they ride verbatim in the process hop - no begin -> process recreation needed.
///
/// INTERRUPT CARRIES ON BOTH AXES, bridged conservatively. begin's only guard (:246, ExcludeUser +
/// ExcludeSystem) is Stop-Function -Continue and so does NOT set the module latch (a latent source
/// quirk - the guard warns but does not actually halt). The LIVE latch source is process: the
/// non-Continue "Stop-Function -Message 'Failure' -ErrorRecord $_" at :351 (a catch with NO following
/// return, inside Invoke-QueryRawDatabases called from the instance loop) sets the latch mid-record,
/// and process opens with "if (Test-FunctionInterrupt) { return }" at :252, so a Failure on one
/// record silences later records. The begin hop and each process hop read the latch at
/// Get-Variable -Scope 0 and carry it; a persisted C# _interrupted field bridges it across records.
/// Mechanism measured in migration/logs/probe-20260718-latch-sentinel.
///
/// ONE Test-Bound ("Encrypted" at :321, "$Encrypt = switch (Test-Bound -Parameter 'Encrypted')")
/// becomes a carried boundness flag $__boundEncrypted. The nine switches (ExcludeUser, ExcludeSystem,
/// Encrypted, NoFullBackup, NoLogBackup, IncludeLastUsed, OnlyAccessible) and inherited
/// EnableException cross as SwitchParameter OBJECTS received untyped. -ExcludeUser and -ExcludeSystem
/// carry their aliases. In-hop Stop-Function/Write-Message calls carry -FunctionName. Positions 0-10
/// are made explicit per the W2-071 law and confirmed against the exported baseline; SqlInstance is
/// Mandatory VFP at 0. Streaming (DEF-001): emits per database. Surface pinned by
/// migration/baselines/Get-DbaDatabase.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDatabase", DefaultParameterSetName = "Default")]
public sealed class GetDbaDatabaseCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Filter to these databases.</summary>
    [Parameter(Position = 2)]
    [PsStringArrayCast]
    public string[]? Database { get; set; }

    /// <summary>Exclude these databases.</summary>
    [Parameter(Position = 3)]
    [PsStringArrayCast]
    public string[]? ExcludeDatabase { get; set; }

    /// <summary>Regex patterns to match database names against.</summary>
    [Parameter(Position = 4)]
    [PsStringArrayCast]
    public string[]? Pattern { get; set; }

    /// <summary>Exclude user databases (system only).</summary>
    [Parameter]
    [Alias("SystemDbOnly", "NoUserDb", "ExcludeAllUserDb")]
    public SwitchParameter ExcludeUser { get; set; }

    /// <summary>Exclude system databases (user only).</summary>
    [Parameter]
    [Alias("UserDbOnly", "NoSystemDb", "ExcludeAllSystemDb")]
    public SwitchParameter ExcludeSystem { get; set; }

    /// <summary>Filter to databases owned by these logins.</summary>
    [Parameter(Position = 5)]
    [PsStringArrayCast]
    public string[]? Owner { get; set; }

    /// <summary>Filter to encrypted databases (bound-sensitive).</summary>
    [Parameter]
    public SwitchParameter Encrypted { get; set; }

    /// <summary>Filter to these database states.</summary>
    [Parameter(Position = 6)]
    [ValidateSet("EmergencyMode", "Normal", "Offline", "Recovering", "RecoveryPending", "Restoring", "Standby", "Suspect")]
    [PsStringArrayCast]
    public string[] Status { get; set; } = new[] { "EmergencyMode", "Normal", "Offline", "Recovering", "RecoveryPending", "Restoring", "Standby", "Suspect" };

    /// <summary>Filter by read-only or read-write access.</summary>
    [Parameter(Position = 7)]
    [ValidateSet("ReadOnly", "ReadWrite")]
    [PsStringCast]
    public string? Access { get; set; }

    /// <summary>Filter to these recovery models.</summary>
    [Parameter(Position = 8)]
    [ValidateSet("Full", "Simple", "BulkLogged")]
    [PsStringArrayCast]
    public string[] RecoveryModel { get; set; } = new[] { "Full", "Simple", "BulkLogged" };

    /// <summary>Filter to databases with no full backup.</summary>
    [Parameter]
    public SwitchParameter NoFullBackup { get; set; }

    /// <summary>Filter to databases with no full backup since this time.</summary>
    [Parameter(Position = 9)]
    public DateTime NoFullBackupSince { get; set; }

    /// <summary>Filter to databases with no log backup.</summary>
    [Parameter]
    public SwitchParameter NoLogBackup { get; set; }

    /// <summary>Filter to databases with no log backup since this time.</summary>
    [Parameter(Position = 10)]
    public DateTime NoLogBackupSince { get; set; }

    /// <summary>Include the last-used timestamp (extra query).</summary>
    [Parameter]
    public SwitchParameter IncludeLastUsed { get; set; }

    /// <summary>Only return accessible databases.</summary>
    [Parameter]
    public SwitchParameter OnlyAccessible { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // a Failure on one record silences later records.
    private bool _interrupted;

    protected override void BeginProcessing()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BeginScript,
            ExcludeUser, ExcludeSystem, EnableException,
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (item?.BaseObject is Hashtable sentinel && sentinel.ContainsKey("__getDbaDatabaseBegin"))
            {
                if (sentinel["__getDbaDatabaseBegin"] is Hashtable state)
                    _interrupted = LanguagePrimitives.IsTrue(state["Interrupted"]);
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

    protected override void ProcessRecord()
    {
        if (Interrupted || _interrupted)
            return;

        // Streaming, not buffered (DEF-001): databases are emitted per instance as found, so a
        // buffered hop would discard results already produced when a later instance failed.
        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is Hashtable sentinel && sentinel.ContainsKey("__getDbaDatabaseProcess"))
            {
                if (sentinel["__getDbaDatabaseProcess"] is Hashtable state)
                    _interrupted = LanguagePrimitives.IsTrue(state["Interrupted"]);
                return;
            }
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
                return;
            }
            WriteObject(item);
        }, ProcessScript,
            SqlInstance, SqlCredential, Database, ExcludeDatabase, Pattern, ExcludeUser, ExcludeSystem,
            Owner, Encrypted, Status, Access, RecoveryModel, NoFullBackup,
            // The source's [datetime] params are $null when UNBOUND; a non-nullable DateTime property
            // defaults to MinValue, which is TRUTHY in PowerShell and would (codex r1) wrongly enable
            // the :413 backup filter and defeat the :426 "if (!$NoLogBackupSince)" default. Pass null
            // when the caller did not bind them (the inner hop params are untyped so $null survives),
            // and the actual value - including an explicit MinValue - when they did.
            MyInvocation.BoundParameters.ContainsKey("NoFullBackupSince") ? (object)NoFullBackupSince : null,
            NoLogBackup,
            MyInvocation.BoundParameters.ContainsKey("NoLogBackupSince") ? (object)NoLogBackupSince : null,
            IncludeLastUsed, OnlyAccessible, EnableException,
            MyInvocation.BoundParameters.ContainsKey("Encrypted"),
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

    // PS: the begin block VERBATIM, dot-sourced. Only edit is -FunctionName on the guard. The guard
    // is Stop-Function -Continue (does not set the latch); the sentinel still carries the interrupt
    // for uniformity. Begin holds no value state that process depends on.
    private const string BeginScript = """
param($ExcludeUser, $ExcludeSystem, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param($ExcludeUser, $ExcludeSystem, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    . {

        if ($ExcludeUser -and $ExcludeSystem) {
            Stop-Function -Message "You cannot specify both ExcludeUser and ExcludeSystem." -Continue -EnableException $EnableException -FunctionName Get-DbaDatabase
        }

    }

    $__iv = Get-Variable -Name __dbatools_interrupt_function_78Q9VPrM6999g6zo24Qn83m09XF56InEn4hFrA8Fwhu5xJrs6r -Scope 0 -ErrorAction Ignore
    @{ __getDbaDatabaseBegin = @{ Interrupted = [bool]($__iv -and $__iv.Value) } }
} $ExcludeUser $ExcludeSystem $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
    // PS: the process block VERBATIM, dot-sourced so the :252 early return exits only the body.
    // Edits: Test-Bound -Parameter 'Encrypted' becomes $__boundEncrypted, plus -FunctionName stamps.
    // The in-process helper functions (Invoke-QueryDBlastUsed / Invoke-QueryRawDatabases /
    // Invoke-QueryDatabaseSizes / $matchesPattern) ride verbatim. The latch is read at Scope 0 after
    // the body so a :351 Failure on this record silences later records.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $ExcludeDatabase, $Pattern, $ExcludeUser, $ExcludeSystem, $Owner, $Encrypted, $Status, $Access, $RecoveryModel, $NoFullBackup, $NoFullBackupSince, $NoLogBackup, $NoLogBackupSince, $IncludeLastUsed, $OnlyAccessible, $EnableException, $__boundEncrypted, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [string[]]$Database, [string[]]$ExcludeDatabase, [string[]]$Pattern, $ExcludeUser, $ExcludeSystem, [string[]]$Owner, $Encrypted, [string[]]$Status, [string]$Access, [string[]]$RecoveryModel, $NoFullBackup, $NoFullBackupSince, $NoLogBackup, $NoLogBackupSince, $IncludeLastUsed, $OnlyAccessible, $EnableException, $__boundEncrypted, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    . {
        if (Test-FunctionInterrupt) { return }

        foreach ($instance in $SqlInstance) {
            try {
                $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
            } catch {
                Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaDatabase
            }

            if (!$IncludeLastUsed) {
                $dblastused = $null
            } else {
                ## Get last used information from the DMV
                $querylastused = "WITH agg AS
                (
                  SELECT
                       MAX(last_user_seek) last_user_seek,
                       MAX(last_user_scan) last_user_scan,
                       MAX(last_user_lookup) last_user_lookup,
                       MAX(last_user_update) last_user_update,
                       sd.name dbname
                   FROM
                       sys.dm_db_index_usage_stats, master..sysdatabases sd
                   WHERE
                     database_id = sd.dbid AND database_id > 4
                      GROUP BY sd.name
                )
                SELECT
                   dbname,
                   last_read = MAX(last_read),
                   last_write = MAX(last_write)
                FROM
                (
                   SELECT dbname, last_user_seek, NULL FROM agg
                   UNION ALL
                   SELECT dbname, last_user_scan, NULL FROM agg
                   UNION ALL
                   SELECT dbname, last_user_lookup, NULL FROM agg
                   UNION ALL
                   SELECT dbname, NULL, last_user_update FROM agg
                ) AS x (dbname, last_read, last_write)
                GROUP BY
                   dbname
                ORDER BY 1;"
                # put a function around this to enable Pester Testing and also to ease any future changes
                function Invoke-QueryDBlastUsed {
                    $server.Query($querylastused)
                }
                $dblastused = Invoke-QueryDBlastUsed
            }

            if ($ExcludeUser) {
                $DBType = @($true)
            } elseif ($ExcludeSystem) {
                $DBType = @($false)
            } else {
                $DBType = @($false, $true)
            }

            $AccessibleFilter = switch ($OnlyAccessible) {
                $true { @($true) }
                default { @($true, $false) }
            }

            $Readonly = switch ($Access) {
                'Readonly' { @($true) }
                'ReadWrite' { @($false) }
                default { @($true, $false) }
            }
            $Encrypt = switch ($__boundEncrypted) {
                $true { @($true) }
                default { @($true, $false, $null) }
            }
            function Invoke-QueryRawDatabases {
                try {
                    if ($server.isAzure) {
                        $dbquery = "SELECT db.name, db.state, dp.name AS [Owner] FROM sys.databases AS db LEFT JOIN sys.database_principals AS dp ON dp.sid = db.owner_sid"
                        $server.ConnectionContext.ExecuteWithResults($dbquery).Tables
                    } elseif ($server.VersionMajor -eq 8) {
                        $server.Query("
                            SELECT name,
                                CASE DATABASEPROPERTYEX(name,'status')
                                    WHEN 'ONLINE'     THEN 0
                                    WHEN 'RESTORING'  THEN 1
                                    WHEN 'RECOVERING' THEN 2
                                    WHEN 'SUSPECT'    THEN 4
                                    WHEN 'EMERGENCY'  THEN 5
                                    WHEN 'OFFLINE'    THEN 6
                                END AS state,
                                SUSER_SNAME(sid) AS [Owner]
                            FROM master.dbo.sysdatabases
                        ")
                    } elseif ($server.VersionMajor -eq 9) {
                        # CDC did not exist in version 9, but did afterwards.
                        $server.Query("SELECT name, state, SUSER_SNAME(owner_sid) AS [Owner] FROM sys.databases")
                    } else {
                        $server.Query("SELECT name, state, SUSER_SNAME(owner_sid) AS [Owner], is_cdc_enabled FROM sys.databases")
                    }
                } catch {
                    Stop-Function -Message "Failure" -ErrorRecord $_ -FunctionName Get-DbaDatabase
                }
            }

            $backed_info = Invoke-QueryRawDatabases

            # Helper function to test if a name matches any of the provided regex patterns
            $matchesPattern = {
                param($name, $patterns)
                if (!$patterns) { return $true }
                foreach ($pattern in $patterns) {
                    if ($name -match $pattern) { return $true }
                }
                return $false
            }

            $backed_info = $backed_info | Where-Object {
                ($_.name -in $Database -or !$Database) -and
                ($_.name -notin $ExcludeDatabase -or !$ExcludeDatabase) -and
                (& $matchesPattern $_.name $Pattern) -and
                ($_.Owner -in $Owner -or !$Owner) -and
                ($_.state -ne 6 -or !$OnlyAccessible)
            }


            $inputObject = @()
            foreach ($dt in $backed_info) {
                try {
                    $inputObject += $server.Databases | Where-Object Name -ceq $dt.name
                } catch {
                    # I've seen this only once and can not reproduce:
                    # The following exception occurred while trying to enumerate the collection: "Failed to connect to server XXXXX.database.windows.net.".
                    # So we implement the fallback that was used before #8333.
                    Write-Message -Level Verbose -Message "Failure: $_" -FunctionName Get-DbaDatabase
                    $inputObject += $server.Databases[$dt.name]
                }
            }
            if ($server.isAzure) {
                $inputObject = $inputObject |
                    Where-Object {
                        ($_.Name -in $Database -or !$Database) -and
                        ($_.Name -notin $ExcludeDatabase -or !$ExcludeDatabase) -and
                        (& $matchesPattern $_.Name $Pattern) -and
                        ($_.Owner -in $Owner -or !$Owner) -and
                        ($_.RecoveryModel -in $RecoveryModel -or !$_.RecoveryModel) -and
                        $_.EncryptionEnabled -in $Encrypt
                    }
            } else {
                $inputObject = $inputObject |
                    Where-Object {
                        ($_.Name -in $Database -or !$Database) -and
                        ($_.Name -notin $ExcludeDatabase -or !$ExcludeDatabase) -and
                        (& $matchesPattern $_.Name $Pattern) -and
                        ($_.Owner -in $Owner -or !$Owner) -and
                        $_.ReadOnly -in $Readonly -and
                        $_.IsAccessible -in $AccessibleFilter -and
                        $_.IsSystemObject -in $DBType -and
                        ((Compare-Object @($_.Status.tostring().split(',').trim()) $Status -ExcludeDifferent -IncludeEqual).inputobject.count -ge 1 -or !$status) -and
                        ($_.RecoveryModel -in $RecoveryModel -or !$_.RecoveryModel) -and
                        $_.EncryptionEnabled -in $Encrypt
                    }
            }
            if ($NoFullBackup -or $NoFullBackupSince) {
                $lastFullBackups = Get-DbaDbBackupHistory -SqlInstance $server -LastFull
                $lastCopyOnlyBackups = Get-DbaDbBackupHistory -SqlInstance $server -LastFull -IncludeCopyOnly | Where-Object IsCopyOnly
                if ($NoFullBackupSince) {
                    $lastFullBackups = $lastFullBackups | Where-Object End -gt $NoFullBackupSince
                    $lastCopyOnlyBackups = $lastCopyOnlyBackups | Where-Object End -gt $NoFullBackupSince
                }

                $hasCopyOnly = $inputObject | Compare-DbaCollationSensitiveObject -Property Name -In -Value $lastCopyOnlyBackups.Database -Collation $server.Collation
                $inputObject = $inputObject | Where-Object Name -cne 'tempdb'
                $inputObject = $inputObject | Compare-DbaCollationSensitiveObject -Property Name -NotIn -Value $lastFullBackups.Database -Collation $server.Collation
            }
            if ($NoLogBackup -or $NoLogBackupSince) {
                if (!$NoLogBackupSince) {
                    $NoLogBackupSince = New-Object -TypeName DateTime
                    $NoLogBackupSince = $NoLogBackupSince.AddMilliSeconds(1)
                }
                $inputObject = $inputObject | Where-Object { $_.LastLogBackupDate -lt $NoLogBackupSince -and $_.RecoveryModel -ne 'Simple' }
            }

            $defaults = 'ComputerName', 'InstanceName', 'SqlInstance', 'Name', 'Status', 'IsAccessible', 'RecoveryModel',
            'LogReuseWaitStatus', 'Size as SizeMB', 'CompatibilityLevel as Compatibility', 'Collation', 'Owner', 'EncryptionEnabled as Encrypted',
            'LastBackupDate as LastFullBackup', 'LastDifferentialBackupDate as LastDiffBackup',
            'LastLogBackupDate as LastLogBackup'

            if ($NoFullBackup -or $NoFullBackupSince) {
                $defaults += ('BackupStatus')
            }
            if ($IncludeLastUsed) {
                # Add Last Used to the default view
                $defaults += ('LastRead as LastIndexRead', 'LastWrite as LastIndexWrite')
            }

            # Get database sizes via T-SQL for fallback when SMO Size is null/0
            # This query works for SQL Server 2000+ and calculates size from sys.master_files or sysaltfiles
            # Azure SQL Database doesn't have sys.master_files, so we use sys.database_files instead
            $querySizes = if ($server.DatabaseEngineType -eq "SqlAzureDatabase") {
                # Azure SQL Database doesn't have sys.master_files
                # Use sys.database_files which is database-scoped
                "SELECT DB_NAME() AS name,
                    CAST(SUM(CAST(size AS BIGINT)) * 8.0 / 1024 AS DECIMAL(18,2)) AS SizeMB
                FROM sys.database_files"
            } elseif ($server.VersionMajor -ge 9) {
                "SELECT DB_NAME(database_id) AS name,
                    CAST(SUM(CAST(size AS BIGINT)) * 8.0 / 1024 AS DECIMAL(18,2)) AS SizeMB
                FROM sys.master_files
                GROUP BY database_id"
            } else {
                "SELECT dbname = DB_NAME(dbid),
                    SizeMB = CAST(SUM(CAST(size AS BIGINT)) * 8.0 / 1024 AS DECIMAL(18,2))
                FROM master.dbo.sysaltfiles
                GROUP BY dbid"
            }

            function Invoke-QueryDatabaseSizes {
                try {
                    if ($server.DatabaseEngineType -eq "SqlAzureDatabase") {
                        # For Azure, we need to query each database individually
                        # since sys.database_files is database-scoped
                        $results = @()
                        foreach ($db in $inputObject) {
                            try {
                                $splatQuery = @{
                                    SqlInstance     = $server
                                    Database        = $db.Name
                                    Query           = $querySizes
                                    EnableException = $true
                                }
                                $result = Invoke-DbaQuery @splatQuery
                                if ($result) {
                                    $results += $result
                                }
                            } catch {
                                # Skip databases that can't be queried (offline, etc.)
                            }
                        }
                        $results
                    } else {
                        $server.Query($querySizes)
                    }
                } catch {
                    Write-Message -Level Warning -Message "Could not retrieve database sizes via T-SQL: $_" -FunctionName Get-DbaDatabase
                    $null
                }
            }

            $dbSizes = Invoke-QueryDatabaseSizes

            try {
                foreach ($db in $inputObject) {

                    $backupStatus = $null
                    if ($NoFullBackup -or $NoFullBackupSince) {
                        if ($db -cin $hasCopyOnly) {
                            $backupStatus = "Only CopyOnly backups"
                        }
                    }

                    # Use T-SQL size if SMO Size is null or 0
                    $sizeValue = $db.Size
                    if ($null -eq $sizeValue -or $sizeValue -eq 0) {
                        $dbSizeInfo = $dbSizes | Where-Object { $_.name -eq $db.Name }
                        if ($dbSizeInfo) {
                            $sizeValue = $dbSizeInfo.SizeMB
                        }
                    }

                    $lastusedinfo = $dblastused | Where-Object { $_.dbname -eq $db.name }
                    Add-Member -Force -InputObject $db -MemberType NoteProperty -Name BackupStatus -Value $backupStatus
                    Add-Member -Force -InputObject $db -MemberType NoteProperty -Name ComputerName -Value $server.ComputerName
                    Add-Member -Force -InputObject $db -MemberType NoteProperty -Name InstanceName -Value $server.ServiceName
                    Add-Member -Force -InputObject $db -MemberType NoteProperty -Name SqlInstance -Value $server.DomainInstanceName
                    Add-Member -Force -InputObject $db -MemberType NoteProperty -Name LastRead -Value $lastusedinfo.last_read
                    Add-Member -Force -InputObject $db -MemberType NoteProperty -Name LastWrite -Value $lastusedinfo.last_write
                    Add-Member -Force -InputObject $db -MemberType NoteProperty -Name IsCdcEnabled -Value ($backed_info | Where-Object { $_.name -ceq $db.name }).is_cdc_enabled
                    # Override Size property with calculated value if SMO returned null/0
                    if ($null -ne $sizeValue) {
                        Add-Member -Force -InputObject $db -MemberType NoteProperty -Name Size -Value $sizeValue
                    }
                    Select-DefaultView -InputObject $db -Property $defaults
                }
            } catch {
                Stop-Function -ErrorRecord $_ -Target $instance -Message "Failure. Collection may have been modified. If so, please use parens (Get-DbaDatabase ....) | when working with commands that modify the collection such as Remove-DbaDatabase." -Continue -FunctionName Get-DbaDatabase
            }
        }
    }

    $__iv = Get-Variable -Name __dbatools_interrupt_function_78Q9VPrM6999g6zo24Qn83m09XF56InEn4hFrA8Fwhu5xJrs6r -Scope 0 -ErrorAction Ignore
    @{ __getDbaDatabaseProcess = @{ Interrupted = [bool]($__iv -and $__iv.Value) } }
} $SqlInstance $SqlCredential $Database $ExcludeDatabase $Pattern $ExcludeUser $ExcludeSystem $Owner $Encrypted $Status $Access $RecoveryModel $NoFullBackup $NoFullBackupSince $NoLogBackup $NoLogBackupSince $IncludeLastUsed $OnlyAccessible $EnableException $__boundEncrypted $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}