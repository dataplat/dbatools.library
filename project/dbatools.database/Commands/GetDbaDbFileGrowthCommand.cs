#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Returns the file growth settings of each database file. Port of public/Get-DbaDbFileGrowth.ps1; the
/// workflow remains a module-scoped PowerShell compatibility hop.
///
/// A process-only port (InputObject is ValueFromPipeline). No begin/end, no accumulator. Two mechanisms.
/// (1) A Test-Bound guard: `if ((Test-Bound Database) -and -not (Test-Bound SqlInstance))` becomes
/// `if ($__boundDatabase -and -not $__boundSqlInstance)`; on that path Stop-Function (no -Continue) then
/// `return`. Because the source has no Test-FunctionInterrupt gate, the guard fires per record (its condition
/// is invocation-level), so the interrupt is deliberately NOT carried (gating would silence later records);
/// the bare return exits the hop scriptblock cleanly. (2) The whole-$PSBoundParameters splat-forward: the
/// source does `Get-DbaDbFile @PSBoundParameters` into the now-compiled Get-DbaDbFile, so the bound set is
/// reconstructed from carried boundness flags (SqlInstance/SqlCredential/Database/InputObject/EnableException
/// only when bound, plus bound Verbose/Debug and raw ErrorAction/WarningAction) and splatted -
/// the same reconstruction used for Export-DbaDbTableData. The only other edit is -FunctionName
/// Get-DbaDbFileGrowth on the Stop-Function. Surface pinned by migration/baselines/Get-DbaDbFileGrowth.json
/// (positions 0-3, InputObject VFP, no ShouldProcess).
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbFileGrowth")]
public sealed class GetDbaDbFileGrowthCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The database(s) to process.</summary>
    [Parameter(Position = 2)]
    [PsStringArrayCast]
    public string[]? Database { get; set; }

    /// <summary>Database object(s) piped in.</summary>
    [Parameter(ValueFromPipeline = true, Position = 3)]
    public Microsoft.SqlServer.Management.Smo.Database[]? InputObject { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, SqlCredential, Database, InputObject, EnableException.ToBool(),
            TestBound(nameof(SqlInstance)), TestBound(nameof(SqlCredential)), TestBound(nameof(Database)),
            TestBound(nameof(InputObject)), TestBound(nameof(EnableException)),
            NestedCommand.BoundCommonParameter(this, "Verbose"), NestedCommand.BoundCommonParameter(this, "Debug"),
            BoundCommonParameterValue("ErrorAction"), BoundCommonParameterValue("WarningAction")))
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

    // ErrorAction/WarningAction must forward their raw ActionPreference value (not a bool).
    private object? BoundCommonParameterValue(string name)
    {
        if (MyInvocation.BoundParameters.TryGetValue(name, out object? value))
            return value;
        return null;
    }
    // PS: the process block. Edits: the two Test-Bound reads in the guard -> the carried
    // boundness flags; -FunctionName on the Stop-Function; and Get-DbaDbFile @PSBoundParameters ->
    // the reconstructed bound-only @__splat (the Export-DbaDbTableData splat-forward pattern).
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $InputObject, $EnableException, $__boundSqlInstance, $__boundSqlCredential, $__boundDatabase, $__boundInputObject, $__boundEnableException, $__boundVerbose, $__boundDebug, $__boundErrorAction, $__boundWarningAction)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [string[]]$Database, [Microsoft.SqlServer.Management.Smo.Database[]]$InputObject, $EnableException, $__boundSqlInstance, $__boundSqlCredential, $__boundDatabase, $__boundInputObject, $__boundEnableException, $__boundVerbose, $__boundDebug, $__boundErrorAction, $__boundWarningAction)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        if ($__boundDatabase -and -not $__boundSqlInstance) {
            Stop-Function -Message "You must specify SqlInstance when specifying Database" -FunctionName Get-DbaDbFileGrowth
            return
        }

        $__splat = @{}
        if ($__boundSqlInstance) { $__splat.SqlInstance = $SqlInstance }
        if ($__boundSqlCredential) { $__splat.SqlCredential = $SqlCredential }
        if ($__boundDatabase) { $__splat.Database = $Database }
        if ($__boundInputObject) { $__splat.InputObject = $InputObject }
        if ($__boundEnableException) { $__splat.EnableException = [bool]$EnableException }
        if ($null -ne $__boundVerbose) { $__splat.Verbose = [bool]$__boundVerbose }
        if ($null -ne $__boundDebug) { $__splat.Debug = [bool]$__boundDebug }
        if ($null -ne $__boundErrorAction) { $__splat.ErrorAction = $__boundErrorAction }
        if ($null -ne $__boundWarningAction) { $__splat.WarningAction = $__boundWarningAction }
        $dbs = Get-DbaDbFile @__splat
        foreach ($db in $dbs) {
            $db | Select-DefaultView -Property ComputerName, InstanceName, SqlInstance, Database, MaxSize, GrowthType, Growth, 'LogicalName as File', 'PhysicalName as FileName', State
        }
} $SqlInstance $SqlCredential $Database $InputObject $EnableException $__boundSqlInstance $__boundSqlCredential $__boundDatabase $__boundInputObject $__boundEnableException $__boundVerbose $__boundDebug $__boundErrorAction $__boundWarningAction @__commonParameters 3>&1 2>&1
""";
}