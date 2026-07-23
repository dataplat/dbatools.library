#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves the last successful DBCC CHECKDB timestamp and integrity status for databases. The
/// input-type dispatch, the DBCC DBINFO / SMO branch, and the per-database output shaping remain a
/// module-scoped PowerShell compatibility hop; the compiled cmdlet preserves the advanced function's
/// process lifetime and typed pipeline surface. Surface pinned by
/// migration/baselines/Get-DbaLastGoodCheckDb.json (positions 0-4, no parameter sets, no ShouldProcess).
///
/// <para>
/// Both SqlInstance and InputObject are ValueFromPipeline. Measured on the engine: a piped value that
/// converts to DbaInstanceParameter binds to BOTH parameters, and one that does not (an SMO Database)
/// binds to InputObject alone - identically for the compiled cmdlet and for the source advanced
/// function. The body's `if ($SqlInstance) { $InputObject = $SqlInstance }` then makes the double-bind
/// unobservable, so InputObject stays object[] (as the surface requires) with no transform attribute.
/// </para>
///
/// <para>
/// Cross-record carriers: $createVersion and $dbccFlags are assigned ONLY in the
/// `VersionMajor -lt 10 -or $isAdmin` branch, and read UNCONDITIONALLY by the emit, with no
/// -Continue on the else path to dominate that read. In the source those locals are function-scoped
/// and persist across piped records, so a record taking the else branch emits the PRIOR record's
/// values. Each hop record runs in a fresh scope that would reset them to null, so the values are
/// captured out through the sentinel and seeded back in - reproducing the stale carry rather than
/// sanitizing it, because a silent behavior change is not parity. Plain value carry, no assigned-flag.
/// Every other process-block local is assigned on every path that reads it: $databases (every switch
/// arm except the one that returns), $isAdmin / $dataPurityEnabled / $lastKnownGood (both branches),
/// $Status (all three arms), $datecreated / $daysSince* (unconditional). The Remove-Variable pair is
/// re-assigned before its next read, so it carries nothing. The source never calls
/// Test-FunctionInterrupt, so the latch the non-continue Stop-Function sets is never read back and
/// needs no carrier.
/// </para>
///
/// <para>
/// The body emits per database AND has a reachable `Stop-Function -Continue`, so the hop streams.
/// It also has two early `return`s, so the body is dot-sourced: `return` then exits only the body and
/// the trailing sentinel still emits, keeping the carry alive across a record that bailed early.
/// </para>
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaLastGoodCheckDb")]
public sealed class GetDbaLastGoodCheckDbCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The databases to check for their last good CHECKDB status.</summary>
    [Parameter(Position = 2)]
    public object[]? Database { get; set; }

    /// <summary>The databases to exclude from the CHECKDB status check.</summary>
    [Parameter(Position = 3)]
    public object[]? ExcludeDatabase { get; set; }

    /// <summary>Instance, server, or database object(s) piped in.</summary>
    [Parameter(ValueFromPipeline = true, Position = 4)]
    public object[]? InputObject { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // Cross-record carriers for the branch-assigned $createVersion / $dbccFlags. Null (never a typed
    // 0) so the first record's else branch emits $null exactly as the unassigned source local does.
    private object? _carriedCreateVersion;
    private object? _carriedDbccFlags;

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is Hashtable sentinel && sentinel.ContainsKey("__getLastGoodCheckDbCarry"))
            {
                if (sentinel["__getLastGoodCheckDbCarry"] is Hashtable state)
                {
                    _carriedCreateVersion = state["CreateVersion"];
                    _carriedDbccFlags = state["DbccFlags"];
                }
                return;
            }
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                NestedCommand.RemoveDuplicateError(this, nestedError);
                WriteError(nestedError);
                return;
            }
            WriteObject(item);
        }, ProcessScript,
            SqlInstance, SqlCredential, Database, ExcludeDatabase, InputObject, EnableException.ToBool(),
            TestBound(nameof(SqlInstance)), TestBound(nameof(InputObject)),
            _carriedCreateVersion, _carriedDbccFlags,
            NestedCommand.BoundCommonParameter(this, "Verbose"), NestedCommand.BoundCommonParameter(this, "Debug"));
    }

    // PS: the process block verbatim (original indentation preserved) inside a dot-sourced block so its
    // two early returns do not skip the trailing carry sentinel. Edits: the neither-bound Test-Bound
    // guard reads the carried bound flags, the branch-assigned $createVersion / $dbccFlags are seeded
    // from and captured back to the carriers, and -FunctionName Get-DbaLastGoodCheckDb rides every
    // direct Stop-Function / Write-Message (plus -ModuleName on Write-Message only).
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $ExcludeDatabase, $InputObject, $EnableException, $__boundSqlInstance, $__boundInputObject, $__carriedCreateVersion, $__carriedDbccFlags, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [object[]]$Database, [object[]]$ExcludeDatabase, [object[]]$InputObject, $EnableException, $__boundSqlInstance, $__boundInputObject, $__carriedCreateVersion, $__carriedDbccFlags)

    # Seed the branch-assigned locals with the prior record's values (plain assignment: a type
    # constraint here would turn an unassigned $null into 0 and sanitize the carry).
    $createVersion = $__carriedCreateVersion
    $dbccFlags = $__carriedDbccFlags

    . {
        if ((-not $__boundSqlInstance) -and (-not $__boundInputObject)) {
            Write-Message -Level Warning -Message "You must specify either a SQL instance or supply an InputObject" -FunctionName Get-DbaLastGoodCheckDb -ModuleName "dbatools"
            return
        }

        if ($SqlInstance) {
            $InputObject = $SqlInstance
        }

        foreach ($input in $InputObject) {
            $inputType = $input.GetType().FullName
            switch ($inputType) {
                'Dataplat.Dbatools.Parameter.DbaInstanceParameter' {
                    Write-Message -Level Verbose -Message "Processing DbaInstanceParameter through InputObject" -FunctionName Get-DbaLastGoodCheckDb -ModuleName "dbatools"
                    $databases = Get-DbaDatabase -SqlInstance $input -SqlCredential $SqlCredential -Database $Database -ExcludeDatabase $ExcludeDatabase
                }
                'Microsoft.SqlServer.Management.Smo.Server' {
                    Write-Message -Level Verbose -Message "Processing Server through InputObject" -FunctionName Get-DbaLastGoodCheckDb -ModuleName "dbatools"
                    $databases = Get-DbaDatabase -SqlInstance $SqlInstance -SqlCredential $SqlCredential -Database $Database -ExcludeDatabase $ExcludeDatabase
                }
                'Microsoft.SqlServer.Management.Smo.Database' {
                    Write-Message -Level Verbose -Message "Processing Database through InputObject" -FunctionName Get-DbaLastGoodCheckDb -ModuleName "dbatools"
                    $databases = $input
                }
                default {
                    Stop-Function -Message "InputObject is not a server or database." -FunctionName Get-DbaLastGoodCheckDb
                    return
                }
            }

            foreach ($db in $databases) {
                $server = $db.Parent
                Write-Message -Level Verbose -Message "Processing $($db.Name) on $($server.Name)." -FunctionName Get-DbaLastGoodCheckDb -ModuleName "dbatools"

                if ($db.IsAccessible -eq $false) {
                    Stop-Function -Message "The database $($db.Name) is not accessible. Skipping database." -Continue -Target $db -FunctionName Get-DbaLastGoodCheckDb
                }

                $isAzure = $db.Parent.DatabaseEngineType -match "Azure"

                if (-not $isAzure) {
                    $isAdmin = $db.Parent.ConnectionContext.ExecuteScalar("SELECT IS_SRVROLEMEMBER('sysadmin')")
                } else {
                    $isAdmin = $false
                }

                if ($db.Parent.VersionMajor -lt 10 -or $isAdmin) {
                    $dbNameQuoted = '[' + $db.Name.Replace(']', ']]') + ']'
                    $sql = "DBCC DBINFO ($dbNameQuoted) WITH TABLERESULTS"
                    Write-Message -Level Debug -Message "T-SQL: $sql" -FunctionName Get-DbaLastGoodCheckDb -ModuleName "dbatools"

                    $resultTable = $db.ExecuteWithResults($sql).Tables[0]
                    [datetime[]]$lastKnownGoodArray = $resultTable | Where-Object Field -eq 'dbi_dbccLastKnownGood' | Select-Object -ExpandProperty Value

                    ## look for databases with two or more occurrences of the field dbi_dbccLastKnownGood
                    if ($lastKnownGoodArray.count -ge 2) {
                        Write-Message -Level Verbose -Message "The database $db has $($lastKnownGoodArray.count) dbi_dbccLastKnownGood fields. This script will only use the newest." -FunctionName Get-DbaLastGoodCheckDb -ModuleName "dbatools"
                    }
                    [datetime]$lastKnownGood = $lastKnownGoodArray | Sort-Object -Descending | Select-Object -First 1

                    [int]$createVersion = ($resultTable | Where-Object Field -eq 'dbi_createVersion').Value
                    [int]$dbccFlags = ($resultTable | Where-Object Field -eq 'dbi_dbccFlags').Value

                    if (($createVersion -lt 611) -and ($dbccFlags -eq 0)) {
                        $dataPurityEnabled = $false
                    } else {
                        $dataPurityEnabled = $true
                    }
                } else {
                    $lastKnownGood = $db.LastGoodCheckDbTime
                    $dataPurityEnabled = $null
                }

                if ($lastKnownGood -isnot [datetime]) {
                    $lastKnownGood = Get-Date '1/1/1900 12:00:00 AM'
                }

                $datecreated = $db.createDate
                if ($datecreated -isnot [datetime]) {
                    $datecreated = Get-Date '1/1/1900 12:00:00 AM'
                }

                $daysSinceCheckDb = (New-TimeSpan -Start $lastKnownGood -End (Get-Date)).Days
                $daysSinceDbCreated = (New-TimeSpan -Start $datecreated -End (Get-Date)).TotalDays

                if ($daysSinceCheckDb -lt 7) {
                    $Status = 'Ok'
                } elseif ($daysSinceDbCreated -lt 7) {
                    $Status = 'New database, not checked yet'
                } else {
                    $Status = 'CheckDB should be performed'
                }

                if ($lastKnownGood -eq '1/1/1900 12:00:00 AM') {
                    Remove-Variable -Name lastKnownGood, daysSinceCheckDb
                }

                if ($datecreated -eq '1/1/1900 12:00:00 AM') {
                    Remove-Variable -Name datecreated
                }


                [PSCustomObject]@{
                    ComputerName             = $server.ComputerName
                    InstanceName             = $server.ServiceName
                    SqlInstance              = $server.DomainInstanceName
                    Database                 = $db.name
                    DatabaseCreated          = $db.createDate
                    LastGoodCheckDb          = $lastKnownGood
                    DaysSinceDbCreated       = $daysSinceDbCreated
                    DaysSinceLastGoodCheckDb = $daysSinceCheckDb
                    Status                   = $status
                    DataPurityEnabled        = $dataPurityEnabled
                    CreateVersion            = $createVersion
                    DbccFlags                = $dbccFlags
                }
            }
        }
    }

    @{ __getLastGoodCheckDbCarry = @{ CreateVersion = $createVersion; DbccFlags = $dbccFlags } }
} $SqlInstance $SqlCredential $Database $ExcludeDatabase $InputObject $EnableException $__boundSqlInstance $__boundInputObject $__carriedCreateVersion $__carriedDbccFlags @__commonParameters 3>&1 2>&1
""";
}
