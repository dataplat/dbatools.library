#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Searches stored-procedure definitions (sys.sql_modules) for a regex pattern across instances and
/// databases, returning matching procedures with per-line match context. Port of
/// public/Find-DbaStoredProcedure.ps1; the workflow remains a module-scoped PowerShell compatibility hop.
///
/// A 3-hop begin+process+end port (SqlInstance is ValueFromPipeline, so process fires per record). begin
/// builds the constant query $sql (sys.sql_modules joined to sys.procedures, with the is_ms_shipped filter
/// unless IncludeSystemObjects) and seeds the counter $everyserverspcount = 0. process, per instance, runs
/// $sql per database and emits a PSCustomObject for each procedure whose TextBody matches the pattern. end
/// writes the grand total. Only $everyserverspcount is a cross-record accumulator (never reset, read in
/// end); $totalcount (reset per record) and $sproccount (reset per database) stay local. The constant $sql
/// and the one counter ride a sentinel (_state) carried begin->process(xN)->end: begin seeds
/// { Sql; EveryServerSpCount = 0 }, each process record restores + accumulates + re-emits, end restores the
/// counter. Body edits: -FunctionName Find-DbaStoredProcedure on process's Stop-Function and six
/// Write-Message and end's Write-Message (begin has none). No ShouldProcess, no early return (the version
/// Continue is a loop keyword), no interrupt (the one Stop-Function is -Continue). Surface pinned by
/// migration/baselines/Find-DbaStoredProcedure.json (positions 0-4, no sets, no ShouldProcess).
/// </summary>
[Cmdlet(VerbsCommon.Find, "DbaStoredProcedure")]
public sealed class FindDbaStoredProcedureCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The database(s) to search.</summary>
    [Parameter(Position = 2)]
    public object[]? Database { get; set; }

    /// <summary>The database(s) to exclude.</summary>
    [Parameter(Position = 3)]
    public object[]? ExcludeDatabase { get; set; }

    /// <summary>The regular-expression pattern to match against procedure definitions.</summary>
    [Parameter(Mandatory = true, Position = 4)]
    [PsStringCast]
    public string Pattern { get; set; } = null!;

    /// <summary>Include system stored procedures in the search.</summary>
    [Parameter]
    public SwitchParameter IncludeSystemObjects { get; set; }

    /// <summary>Include system databases in the search.</summary>
    [Parameter]
    public SwitchParameter IncludeSystemDatabases { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // The constant query $sql plus the one never-reset counter, carried begin->process(xN)->end.
    private Hashtable? _state;

    protected override void BeginProcessing()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BeginScript,
            IncludeSystemObjects.ToBool(), EnableException.ToBool(),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (item?.BaseObject is Hashtable sentinel && sentinel.ContainsKey("__findDbaStoredProcedureBegin"))
            {
                _state = sentinel["__findDbaStoredProcedureBegin"] as Hashtable;
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
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, SqlCredential, Database, ExcludeDatabase, Pattern, IncludeSystemDatabases.ToBool(),
            EnableException.ToBool(), _state, BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (item?.BaseObject is Hashtable sentinel && sentinel.ContainsKey("__findDbaStoredProcedureProcess"))
            {
                if (sentinel["__findDbaStoredProcedureProcess"] is Hashtable state)
                {
                    _state = state["State"] as Hashtable ?? _state;
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

    protected override void EndProcessing()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, EndScript,
            _state, EnableException.ToBool(), BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
                continue;
            }
            WriteObject(item);
        }
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
    // PS: the begin block VERBATIM (builds $sql and seeds the counter) plus a sentinel carrying them to
    // the process hop. begin has no Stop-Function/Write-Message.
    private const string BeginScript = """
param($IncludeSystemObjects, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param($IncludeSystemObjects, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        $sql =
        "SELECT OBJECT_SCHEMA_NAME(p.object_id) AS ProcSchema, p.name, m.definition AS TextBody
          FROM sys.sql_modules AS m
           INNER JOIN sys.procedures AS p
            ON m.object_id = p.object_id"

        if (!$IncludeSystemObjects) { $sql = "$sql WHERE p.is_ms_shipped = 0;" }

        $everyserverspcount = 0

    @{ __findDbaStoredProcedureBegin = @{ Sql = $sql; EveryServerSpCount = $everyserverspcount } }
} $IncludeSystemObjects $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
    // PS: the process block VERBATIM. Edits: -FunctionName on the Stop-Function and six Write-Message.
    // $sql and the counter are restored from the carried state; the counter is accumulated and re-emitted.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $ExcludeDatabase, $Pattern, $IncludeSystemDatabases, $EnableException, $__state, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [object[]]$Database, [object[]]$ExcludeDatabase, [string]$Pattern, $IncludeSystemDatabases, $EnableException, $__state, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    $sql = $__state.Sql
    $everyserverspcount = $__state.EveryServerSpCount

        foreach ($Instance in $SqlInstance) {
            try {
                $server = Connect-DbaInstance -SqlInstance $Instance -SqlCredential $SqlCredential
            } catch {
                Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Find-DbaStoredProcedure
            }

            if ($server.versionMajor -lt 9) {
                Write-Message -Level Warning -Message "This command only supports SQL Server 2005 and above." -FunctionName Find-DbaStoredProcedure
                Continue
            }

            if ($IncludeSystemDatabases) {
                $dbs = $server.Databases | Where-Object { $_.Status -eq "normal" }
            } else {
                $dbs = $server.Databases | Where-Object { $_.Status -eq "normal" -and $_.IsSystemObject -eq $false }
            }

            if ($Database) {
                $dbs = $dbs | Where-Object Name -In $Database
            }

            if ($ExcludeDatabase) {
                $dbs = $dbs | Where-Object Name -NotIn $ExcludeDatabase
            }

            $totalcount = 0
            $dbcount = $dbs.count
            foreach ($db in $dbs) {
                Write-Message -Level Verbose -Message "Searching on database $db" -FunctionName Find-DbaStoredProcedure

                Write-Message -Level Debug -Message $sql -FunctionName Find-DbaStoredProcedure
                $rows = $db.ExecuteWithResults($sql).Tables.Rows
                $sproccount = 0

                foreach ($row in $rows) {
                    $totalcount++; $sproccount++; $everyserverspcount++

                    $procSchema = $row.ProcSchema
                    $proc = $row.Name

                    Write-Message -Level Verbose -Message "Looking in stored procedure: $procSchema.$proc textBody for $pattern" -FunctionName Find-DbaStoredProcedure
                    if ($row.TextBody -match $Pattern) {
                        $sp = $db.StoredProcedures | Where-Object { $_.Schema -eq $procSchema -and $_.Name -eq $proc }

                        $StoredProcedureText = $row.TextBody
                        $splitOn = [string[]]@("`r`n", "`r", "`n" )
                        $spTextFound = $StoredProcedureText.Split( $splitOn , [System.StringSplitOptions]::None ) |
                            Select-String -Pattern $Pattern | ForEach-Object { "(LineNumber: $($_.LineNumber)) $($_.ToString().Trim())" }

                        [PSCustomObject]@{
                            ComputerName             = $server.ComputerName
                            SqlInstance              = $server.ServiceName
                            Database                 = $db.Name
                            DatabaseId               = $db.ID
                            Schema                   = $sp.Schema
                            Name                     = $sp.Name
                            Owner                    = $sp.Owner
                            IsSystemObject           = $sp.IsSystemObject
                            CreateDate               = $sp.CreateDate
                            LastModified             = $sp.DateLastModified
                            StoredProcedureTextFound = $spTextFound -join [System.Environment]::NewLine
                            StoredProcedure          = $sp
                            StoredProcedureFullText  = $StoredProcedureText
                        } | Select-DefaultView -ExcludeProperty StoredProcedure, StoredProcedureFullText
                    }
                }

                Write-Message -Level Verbose -Message "Evaluated $sproccount stored procedures in $db" -FunctionName Find-DbaStoredProcedure
            }
            Write-Message -Level Verbose -Message "Evaluated $totalcount total stored procedures in $dbcount databases" -FunctionName Find-DbaStoredProcedure
        }

    $__state.EveryServerSpCount = $everyserverspcount
    @{ __findDbaStoredProcedureProcess = @{ State = $__state } }
} $SqlInstance $SqlCredential $Database $ExcludeDatabase $Pattern $IncludeSystemDatabases $EnableException $__state $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
    // PS: the end block VERBATIM. Edit: -FunctionName on the Write-Message. $everyserverspcount is
    // restored from the carried state so the grand total reflects all records.
    private const string EndScript = """
param($__state, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param($__state, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    $everyserverspcount = $__state.EveryServerSpCount

        Write-Message -Level Verbose -Message "Evaluated $everyserverspcount total stored procedures" -FunctionName Find-DbaStoredProcedure
} $__state $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}