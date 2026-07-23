#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves view objects and metadata from databases. Port of public/Get-DbaDbView.ps1;
/// the workflow remains a module-scoped PowerShell compatibility hop.
///
/// A begin+process port and the structural twin of Get-DbaDbTable (W2-112) - the SAME dual begin->process carry.
/// The begin block parses -View into a $fqtns list carried begin->process through a sentinel (_fqtns); the begin
/// body is DOT-SOURCED so its early "No Valid View" Stop-Function+return guard still lets the sentinel emit; the
/// loop-bound Continue inside foreach ($v in $View) is unaffected. The source process OPENS with
/// if (Test-FunctionInterrupt) { return }, so the begin interrupt IS carried: after the dot-source the begin hop
/// captures the module interrupt variable (Get-Variable -Scope 0) and emits Interrupted; the C# field
/// _beginInterrupted gates ProcessRecord (the verbatim Test-FunctionInterrupt line stays in the body but is inert
/// in the fresh process scope). TWO Test-Bound become carried flags: Test-Bound SqlInstance -> $__boundSqlInstance
/// (gates the Get-DbaDatabase gather into the local $InputObject) and Test-Bound -ParameterName ExcludeSystemView
/// -> $__boundExcludeSystemView (ExcludeSystemView is Test-Bound-only, an untyped carried flag - no value-passed
/// switch, no positional-binding hazard). The three continues are loop-bound. No ShouldProcess. Edits:
/// -FunctionName Get-DbaDbView on the begin Write-Message + Stop-Function and the process's five Write-Message.
/// Surface pinned by migration/baselines/Get-DbaDbView.json (positions 0-6, ExcludeSystemView switch non-positional,
/// two VFP, no ShouldProcess).
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbView")]
public sealed class GetDbaDbViewCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The database(s) to process.</summary>
    [Parameter(Position = 2)]
    public object[]? Database { get; set; }

    /// <summary>The database(s) to exclude.</summary>
    [Parameter(Position = 3)]
    public object[]? ExcludeDatabase { get; set; }

    /// <summary>Exclude system views from the results.</summary>
    [Parameter]
    public SwitchParameter ExcludeSystemView { get; set; }

    /// <summary>Filter to the specified view(s) by (optionally schema/database-qualified) name.</summary>
    [Parameter(Position = 4)]
    public string[]? View { get; set; }

    /// <summary>Filter to views in the specified schema(s).</summary>
    [Parameter(Position = 5)]
    public string[]? Schema { get; set; }

    /// <summary>Database object(s) piped in from Get-DbaDatabase.</summary>
    [Parameter(ValueFromPipeline = true, Position = 6)]
    public Microsoft.SqlServer.Management.Smo.Database[]? InputObject { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // Carried begin->process: the parsed fully-qualified view list from -View, and the begin interrupt state.
    private object? _fqtns;
    private bool _beginInterrupted;

    protected override void BeginProcessing()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BeginScript,
            View, EnableException.ToBool(), NestedCommand.BoundCommonParameter(this, "Verbose"), NestedCommand.BoundCommonParameter(this, "Debug")))
        {
            if (item?.BaseObject is Hashtable sentinel && sentinel.ContainsKey("__viewBegin"))
            {
                if (sentinel["__viewBegin"] is Hashtable state)
                {
                    _fqtns = state["Fqtns"];
                    _beginInterrupted = LanguagePrimitives.IsTrue(state["Interrupted"]);
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
        // Replicates the source process block's opening if (Test-FunctionInterrupt) { return }:
        // a begin Stop-Function (no -Continue) sets the interrupt, carried here as _beginInterrupted.
        if (Interrupted || _beginInterrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, SqlCredential, Database, ExcludeDatabase, Schema, InputObject, EnableException.ToBool(),
            _fqtns, TestBound(nameof(SqlInstance)), TestBound(nameof(ExcludeSystemView)),
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
    // PS: the begin block VERBATIM (DOT-SOURCED so the early no-valid-view Stop-Function+return still lets the
    // sentinel emit). Captures both the $fqtns list AND the begin interrupt (Get-Variable -Scope 0) - the process
    // block gates on the latter. Edits: -FunctionName Get-DbaDbView on the Write-Message + Stop-Function.
    private const string BeginScript = """
param($View, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([string[]]$View, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }
    . {
        if ($View) {
            $fqtns = @()
            foreach ($v in $View) {
                $fqtn = Get-ObjectNameParts -ObjectName $v

                if (-not $fqtn.Parsed) {
                    Write-Message -Level Warning -Message "Please check you are using proper three-part names. If your search value contains special characters you must use [ ] to wrap the name. The value $t could not be parsed as a valid name." -FunctionName Get-DbaDbView -ModuleName "dbatools"
                    Continue
                }

                $fqtns += [PSCustomObject] @{
                    Database   = $fqtn.Database
                    Schema     = $fqtn.Schema
                    View       = $fqtn.Name
                    InputValue = $fqtn.InputValue
                }
            }
            if (-not $fqtns) {
                Stop-Function -Message "No Valid View specified" -FunctionName Get-DbaDbView
                return
            }
        }
    }
    $__iv = Get-Variable -Name __dbatools_interrupt_function_78Q9VPrM6999g6zo24Qn83m09XF56InEn4hFrA8Fwhu5xJrs6r -Scope 0 -ErrorAction Ignore
    @{ __viewBegin = @{ Fqtns = $fqtns; Interrupted = [bool]($__iv -and $__iv.Value) } }
} $View $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";

    // PS: the process block VERBATIM. Edits: -FunctionName Get-DbaDbView on the five Write-Message; Test-Bound
    // SqlInstance -> $__boundSqlInstance and Test-Bound -ParameterName ExcludeSystemView -> $__boundExcludeSystemView.
    // $fqtns arrives carried from begin. The opening if (Test-FunctionInterrupt) { return } is inert here (fresh
    // scope) - the real begin-interrupt gate is the C# _beginInterrupted check. The three continues are loop-bound.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $ExcludeDatabase, $Schema, $InputObject, $EnableException, $fqtns, $__boundSqlInstance, $__boundExcludeSystemView, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [object[]]$Database, [object[]]$ExcludeDatabase, [string[]]$Schema, [Microsoft.SqlServer.Management.Smo.Database[]]$InputObject, $EnableException, $fqtns, $__boundSqlInstance, $__boundExcludeSystemView, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        if (Test-FunctionInterrupt) { return }

        if ($__boundSqlInstance) {
            $InputObject = Get-DbaDatabase -SqlInstance $SqlInstance -SqlCredential $SqlCredential -Database $Database -ExcludeDatabase $ExcludeDatabase
        }

        foreach ($db in $InputObject) {
            Write-Message -Level Verbose -Message "processing $db" -FunctionName Get-DbaDbView -ModuleName "dbatools"

            # Let the SMO read all properties referenced in this command for all views in the database in one query.
            # Downside: If some other properties were already read outside of this command in the used SMO, they are cleared.
            try {
                $db.Views.ClearAndInitialize('', [string[]]('Name', 'Schema', 'IsSystemObject', 'CreateDate', 'DateLastModified'))
            } catch {
                Write-Message -Level Verbose -Message "ClearAndInitialize failed: $_" -FunctionName Get-DbaDbView -ModuleName "dbatools"
            }

            if ($fqtns) {
                $views = @()
                foreach ($fqtn in $fqtns) {
                    # If the user specified a database in a three-part name, and it's not the
                    # database currently being processed, skip this view.
                    if ($fqtn.Database) {
                        if ($fqtn.Database -ne $db.Name) {
                            continue
                        }
                    }

                    $vw = $db.Views | Where-Object { $_.Name -in $fqtn.View -and $fqtn.Schema -in ($_.Schema, $null) -and $fqtn.Database -in ($_.Parent.Name, $null) }

                    if (-not $vw) {
                        Write-Message -Level Verbose -Message "Could not find view $($fqtn.View) in $db on $($db.Parent.DomainInstanceName)" -FunctionName Get-DbaDbView -ModuleName "dbatools"
                    }
                    $views += $vw
                }
            } else {
                $views = $db.Views
            }

            if (-not $db.IsAccessible) {
                Write-Message -Level Warning -Message "Database $db is not accessible. Skipping" -FunctionName Get-DbaDbView -ModuleName "dbatools"
                continue
            }

            if (-not $views) {
                Write-Message -Message "No views exist in the $db database on $($db.Parent.DomainInstanceName)" -Target $db -Level Verbose -FunctionName Get-DbaDbView -ModuleName "dbatools"
                continue
            }

            if ($Schema) {
                $views = $views | Where-Object Schema -in $Schema
            }

            if ($__boundExcludeSystemView) {
                $views = $views | Where-Object { -not $_.IsSystemObject }
            }

            $defaults = 'ComputerName', 'InstanceName', 'SqlInstance', 'Database', 'Schema', 'CreateDate', 'DateLastModified', 'Name'
            foreach ($sqlview in $views) {

                Add-Member -Force -InputObject $sqlview -MemberType NoteProperty -Name ComputerName -Value $db.Parent.ComputerName
                Add-Member -Force -InputObject $sqlview -MemberType NoteProperty -Name InstanceName -Value $db.Parent.ServiceName
                Add-Member -Force -InputObject $sqlview -MemberType NoteProperty -Name SqlInstance -Value $db.Parent.DomainInstanceName
                Add-Member -Force -InputObject $sqlview -MemberType NoteProperty -Name Database -Value $db.Name

                Select-DefaultView -InputObject $sqlview -Property $defaults
            }
        }
} $SqlInstance $SqlCredential $Database $ExcludeDatabase $Schema $InputObject $EnableException $fqtns $__boundSqlInstance $__boundExcludeSystemView $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
