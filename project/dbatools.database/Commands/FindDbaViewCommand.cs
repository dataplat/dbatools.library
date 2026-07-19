#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Searches view definitions (sys.sql_modules) for a regex pattern across instances and databases,
/// returning matching views with per-line match context. Port of public/Find-DbaView.ps1; the workflow
/// remains a module-scoped PowerShell compatibility hop.
///
/// A 3-hop begin+process+end port (SqlInstance is ValueFromPipeline, so process fires per record) - the
/// Find-DbaStoredProcedure accumulator pattern with an added $eol constant. begin builds the constant query
/// $sql (sys.sql_modules joined to sys.views, with the is_ms_shipped filter unless IncludeSystemObjects),
/// the $eol separator, and seeds $everyservervwcount = 0. process, per instance, runs $sql per database and
/// emits a PSCustomObject for each view whose TextBody matches the pattern. end writes the grand total. Only
/// $everyservervwcount is a cross-record accumulator (never reset, read in end); $totalcount (reset per
/// record) and $vwcount (reset per database) stay local. The $sql and $eol constants and the one counter
/// ride a sentinel (_state) carried begin->process(xN)->end: begin seeds { Sql; Eol; EveryServerVwCount = 0 },
/// each process record restores + accumulates + re-emits, end restores the counter. Body edits:
/// -FunctionName Find-DbaView on process's Stop-Function and seven Write-Message and end's Write-Message
/// (begin has none). No ShouldProcess, no early return, no interrupt (the one Stop-Function is -Continue).
/// Surface pinned by migration/baselines/Find-DbaView.json (positions 0-4, no sets, no ShouldProcess).
/// </summary>
[Cmdlet(VerbsCommon.Find, "DbaView")]
public sealed class FindDbaViewCommand : DbaBaseCmdlet
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

    /// <summary>The regular-expression pattern to match against view definitions.</summary>
    [Parameter(Mandatory = true, Position = 4)]
    [PsStringCast]
    public string Pattern { get; set; } = null!;

    /// <summary>Include system views in the search.</summary>
    [Parameter]
    public SwitchParameter IncludeSystemObjects { get; set; }

    /// <summary>Include system databases in the search.</summary>
    [Parameter]
    public SwitchParameter IncludeSystemDatabases { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // The constant query $sql + $eol plus the one never-reset counter, carried begin->process(xN)->end.
    private Hashtable? _state;

    protected override void BeginProcessing()
    {
        if (Interrupted)
            return;

        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is Hashtable sentinel && sentinel.ContainsKey("__findDbaViewBegin"))
            {
                _state = sentinel["__findDbaViewBegin"] as Hashtable;
                return;
            }
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
                return;
            }
            WriteObject(item);
        }, BeginScript,
            IncludeSystemObjects.ToBool(), EnableException.ToBool(),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug"));
    }

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is Hashtable sentinel && sentinel.ContainsKey("__findDbaViewProcess"))
            {
                if (sentinel["__findDbaViewProcess"] is Hashtable state)
                {
                    _state = state["State"] as Hashtable ?? _state;
                }
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
            SqlInstance, SqlCredential, Database, ExcludeDatabase, Pattern, IncludeSystemDatabases.ToBool(),
            EnableException.ToBool(), _state, BoundCommonParameter("Verbose"), BoundCommonParameter("Debug"));
    }

    protected override void EndProcessing()
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
        }, EndScript,
            _state, EnableException.ToBool(), BoundCommonParameter("Verbose"), BoundCommonParameter("Debug"));
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
    // PS: the begin block VERBATIM (builds $sql, $eol, and seeds the counter) plus a sentinel carrying
    // them to the process hop. begin has no Stop-Function/Write-Message.
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

        $sql = "SELECT OBJECT_SCHEMA_NAME(vw.object_id) AS ViewSchema, vw.name, m.definition AS TextBody FROM sys.sql_modules m, sys.views vw WHERE m.object_id = vw.object_id"
        if (!$IncludeSystemObjects) { $sql = "$sql AND vw.is_ms_shipped = 0" }
        $everyservervwcount = 0

        $eol = [System.Environment]::NewLine

    @{ __findDbaViewBegin = @{ Sql = $sql; Eol = $eol; EveryServerVwCount = $everyservervwcount } }
} $IncludeSystemObjects $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
    // PS: the process block VERBATIM. Edits: -FunctionName on the Stop-Function and seven Write-Message.
    // $sql, $eol, and the counter are restored from the carried state; the counter is accumulated and
    // re-emitted so it persists record-to-record and into end.
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
    $eol = $__state.Eol
    $everyservervwcount = $__state.EveryServerVwCount

        foreach ($instance in $SqlInstance) {
            try {
                $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
            } catch {
                Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Find-DbaView
            }

            if ($server.versionMajor -lt 9) {
                Write-Message -Level Warning -Message "This command only supports SQL Server 2005 and above." -FunctionName Find-DbaView -ModuleName "dbatools"
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
                Write-Message -Level Verbose -Message "Searching on database $db" -FunctionName Find-DbaView -ModuleName "dbatools"

                # If system objects aren't needed, find view text using SQL
                # This prevents SMO from having to enumerate

                if (!$IncludeSystemObjects) {
                    Write-Message -Level Debug -Message $sql -FunctionName Find-DbaView -ModuleName "dbatools"
                    $rows = $db.ExecuteWithResults($sql).Tables.Rows
                    $vwcount = 0

                    foreach ($row in $rows) {
                        $totalcount++; $vwcount++; $everyservervwcount++

                        $viewSchema = $row.ViewSchema
                        $view = $row.name

                        Write-Message -Level Verbose -Message "Looking in View: $viewSchema.$view TextBody for $pattern" -FunctionName Find-DbaView -ModuleName "dbatools"
                        if ($row.TextBody -match $Pattern) {
                            $vw = $db.Views | Where-Object { $_.Schema -eq $viewSchema -and $_.Name -eq $view }

                            $viewText = $vw.TextBody.split($eol)
                            $vwTextFound = $viewText | Select-String -Pattern $Pattern | ForEach-Object { "(LineNumber: $($_.LineNumber)) $($_.ToString().Trim())" }

                            [PSCustomObject]@{
                                ComputerName   = $server.ComputerName
                                SqlInstance    = $server.ServiceName
                                Database       = $db.Name
                                DatabaseId     = $db.ID
                                Schema         = $vw.Schema
                                Name           = $vw.Name
                                Owner          = $vw.Owner
                                IsSystemObject = $vw.IsSystemObject
                                CreateDate     = $vw.CreateDate
                                LastModified   = $vw.DateLastModified
                                ViewTextFound  = $vwTextFound -join "`n"
                                View           = $vw
                                ViewFullText   = $vw.TextBody
                            } | Select-DefaultView -ExcludeProperty View, ViewFullText
                        }
                    }
                } else {
                    $Views = $db.Views

                    foreach ($vw in $Views) {
                        $totalcount++; $vwcount++; $everyservervwcount++

                        $viewSchema = $row.ViewSchema
                        $view = $vw.Name

                        Write-Message -Level Verbose -Message "Looking in View: $viewSchema.$view TextBody for $pattern" -FunctionName Find-DbaView -ModuleName "dbatools"
                        if ($vw.TextBody -match $Pattern) {

                            $viewText = $vw.TextBody.split($eol)
                            $vwTextFound = $viewText | Select-String -Pattern $Pattern | ForEach-Object { "(LineNumber: $($_.LineNumber)) $($_.ToString().Trim())" }

                            [PSCustomObject]@{
                                ComputerName   = $server.ComputerName
                                SqlInstance    = $server.ServiceName
                                Database       = $db.Name
                                DatabaseId     = $db.ID
                                Schema         = $vw.Schema
                                Name           = $vw.Name
                                Owner          = $vw.Owner
                                IsSystemObject = $vw.IsSystemObject
                                CreateDate     = $vw.CreateDate
                                LastModified   = $vw.DateLastModified
                                ViewTextFound  = $vwTextFound -join "`n"
                                View           = $vw
                                ViewFullText   = $vw.TextBody
                            } | Select-DefaultView -ExcludeProperty View, ViewFullText
                        }
                    }
                }
                Write-Message -Level Verbose -Message "Evaluated $vwcount views in $db" -FunctionName Find-DbaView -ModuleName "dbatools"
            }
            Write-Message -Level Verbose -Message "Evaluated $totalcount total views in $dbcount databases" -FunctionName Find-DbaView -ModuleName "dbatools"
        }

    $__state.EveryServerVwCount = $everyservervwcount
    @{ __findDbaViewProcess = @{ State = $__state } }
} $SqlInstance $SqlCredential $Database $ExcludeDatabase $Pattern $IncludeSystemDatabases $EnableException $__state $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
    // PS: the end block VERBATIM. Edit: -FunctionName on the Write-Message. $everyservervwcount is
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

    $everyservervwcount = $__state.EveryServerVwCount

        Write-Message -Level Verbose -Message "Evaluated $everyservervwcount total views" -FunctionName Find-DbaView -ModuleName "dbatools"
} $__state $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
