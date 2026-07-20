#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Gets the Policy Based Management conditions from a SQL Server instance or policy store.
/// </summary>
/// <remarks>
/// <para>
/// The workflow remains a module-scoped PowerShell compatibility hop so that the DMF library load, the
/// store resolution, the condition filtering, the added note properties, the default view, and dbatools
/// stream and error handling stay observable-identical to the script implementation.
/// </para>
/// <para>
/// The script guards PowerShell Core with a Stop-Function that has NO -Continue, so it latches the
/// interrupt and returns. That latch lives in the function scope and spans the whole pipeline: the first
/// record warns, and every later record returns immediately at the process block's Test-FunctionInterrupt
/// without warning again. A per-record hop scope would lose it and warn once per record, so the latch is
/// carried - the body runs dot-sourced (its early return stays local) and the trailing sentinel reports
/// Test-FunctionInterrupt, which the demux latches into a field that short-circuits later records.
/// </para>
/// <para>
/// InputObject is the only ValueFromPipeline parameter and the body's `$InputObject +=` appends the
/// instance-resolved stores WITHIN a record, so nothing accumulates across records - the parameter rebinds
/// per record exactly as the script sees it, and a per-record hop is faithful. Add-PbmLibrary runs ONCE
/// from BeginProcessing, matching the script's begin block: it warns and returns when the DMF assemblies
/// cannot be loaded, so a per-record call would repeat that warning once per record, and an empty pipeline
/// must still run it. The body streams through InvokeScopedStreaming rather than buffering, because
/// Stop-Function -Continue THROWS under -EnableException instead of taking its continue branch: a record
/// can emit conditions for an early store and then terminate on a later one, and a buffered call would
/// discard what was already produced (DEF-001). EnableException is carried as a plain (untyped) value,
/// because a switch in the inner CmdletBinding scriptblock is excluded from positional binding.
/// </para>
/// </remarks>
// No [OutputType] is declared: the emitted DMF Condition type ships in assemblies that
// Add-PbmLibrary loads at RUNTIME, so it cannot be referenced at compile time.
[Cmdlet(VerbsCommon.Get, "DbaPbmCondition")]
public sealed class GetDbaPbmConditionCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The conditions to return.</summary>
    [Parameter(Position = 2)]
    [PsStringArrayCast]
    public string[]? Condition { get; set; }

    /// <summary>Policy stores from Get-DbaPbmStore for pipeline operations.</summary>
    [Parameter(ValueFromPipeline = true, Position = 3)]
    public PSObject[]? InputObject { get; set; }

    /// <summary>Includes conditions flagged as system objects.</summary>
    [Parameter]
    public SwitchParameter IncludeSystemObject { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    /// <summary>Set once the body has latched the dbatools interrupt, mirroring the script's function scope.</summary>
    private bool _bodyInterrupted;

    /// <summary>Loads the DMF libraries once, as the script's begin block does.</summary>
    protected override void BeginProcessing()
    {
        foreach (PSObject? item in NestedCommand.InvokeScoped(this, AddLibraryScript,
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
            }
            else if (item is not null)
            {
                WriteObject(item);
            }
        }
    }

    /// <summary>Returns the conditions for the stores bound to the current record.</summary>
    protected override void ProcessRecord()
    {
        if (_bodyInterrupted || Interrupted)
            return;

        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
            }
            else if (item is not null && LanguagePrimitives.IsTrue(
                item.Properties["__GetDbaPbmConditionProcessComplete"]?.Value))
            {
                _bodyInterrupted = LanguagePrimitives.IsTrue(item.Properties["Interrupted"]?.Value);
            }
            else if (item is not null)
            {
                WriteObject(item);
            }
        }, ProcessScript,
            SqlInstance, SqlCredential, Condition, InputObject, IncludeSystemObject.ToBool(),
            EnableException.ToBool(), BoundCommonParameter("Verbose"), BoundCommonParameter("Debug"));
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

    // PS: the script's begin block, run ONCE from BeginProcessing. Add-PbmLibrary warns and returns when
    // the DMF assemblies cannot be loaded, so calling it per record would repeat that warning and its
    // $error entry once per record instead of once - and an empty pipeline must still run it, as the
    // script's begin block does.
    private const string AddLibraryScript = """
param($__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param($__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    Add-PbmLibrary
} $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1

""";

    // PS: the process body VERBATIM inside a dot-sourced block so its early return stays local and the
    // trailing sentinel still runs. The sentinel reports the dbatools interrupt latch so the next record
    // can skip exactly as the script's function-scoped latch makes it. Substitutions only: -FunctionName
    // on the single DIRECT Stop-Function call, and -FunctionName plus -ModuleName "dbatools" on the
    // single DIRECT Write-Message call. EnableException received untyped.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Condition, $InputObject, $IncludeSystemObject, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [System.Management.Automation.PSCredential]$SqlCredential, [string[]]$Condition, [psobject[]]$InputObject, $IncludeSystemObject, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    . {
        if (Test-FunctionInterrupt) { return }
        if ($PSVersionTable.PSEdition -eq "Core") {
            Stop-Function -Message "This command is not yet supported in PowerShell Core" -FunctionName Get-DbaPbmCondition
            return
        }
        foreach ($instance in $SqlInstance) {
            $InputObject += Get-DbaPbmStore -SqlInstance $instance -SqlCredential $SqlCredential
        }
        foreach ($store in $InputObject) {
            $allconditions = $store.Conditions

            if (-not $IncludeSystemObject) {
                $allconditions = $allconditions | Where-Object IsSystemObject -eq $false
            }

            if ($Condition) {
                $allconditions = $allconditions | Where-Object Name -in $Condition
            }

            foreach ($currentcondition in $allconditions) {
                Write-Message -Level Verbose -Message "Processing $currentcondition" -FunctionName Get-DbaPbmCondition -ModuleName "dbatools"
                Add-Member -Force -InputObject $currentcondition -MemberType NoteProperty ComputerName -value $store.ComputerName
                Add-Member -Force -InputObject $currentcondition -MemberType NoteProperty InstanceName -value $store.InstanceName
                Add-Member -Force -InputObject $currentcondition -MemberType NoteProperty SqlInstance -value $store.SqlInstance
                Select-DefaultView -InputObject $currentcondition -Property ComputerName, InstanceName, SqlInstance, Id, Name, CreateDate, CreatedBy, DateModified, Description, ExpressionNode, Facet, HasScript, IsSystemObject, ModifiedBy
            }
        }
    }

    [pscustomobject]@{ __GetDbaPbmConditionProcessComplete = $true; Interrupted = (Test-FunctionInterrupt) }
} $SqlInstance $SqlCredential $Condition $InputObject $IncludeSystemObject $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1

""";
}

