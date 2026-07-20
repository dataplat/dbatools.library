#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Lists available randomizer types and subtypes. Port of public/Get-DbaRandomizedType.ps1;
/// the workflow remains a module-scoped PowerShell compatibility hop.
///
/// A process-only port; there is NO ValueFromPipeline parameter, so process fires once. The source begin block
/// imports the randomizer-types CSV into $randomizerTypes, which process filters (by -Pattern / -RandomizedType /
/// -RandomizedSubType) and emits. Since $randomizerTypes is param-independent and process fires once, the begin
/// block is PREPENDED (verbatim) into the process hop so $randomizerTypes is in scope. The begin catch fires
/// Stop-Function -Continue on a CSV import failure; that catch sits at begin-top with NO enclosing loop, so its
/// internal `continue` would escape the module scriptblock (the bare-continue-in-hop hazard) - the whole body is
/// therefore wrapped in `foreach ($__continueGuard in @(1)) { ... }` so the continue is loop-bound (on CSV failure it
/// skips the body and emits nothing, matching the source's effective behavior where $randomizerTypes is $null and the
/// filters produce no output). The verbatim `if (Test-FunctionInterrupt) { return }` is inert (Stop-Function -Continue
/// does not set the interrupt). The body uses $script:PSModuleRoot, which resolves in the real module scope. Edit:
/// -FunctionName Get-DbaRandomizedType on the begin Stop-Function. No value-passed switch, no Test-Bound, no
/// ShouldProcess. Surface pinned by migration/baselines/Get-DbaRandomizedType.json (RandomizedType pos0,
/// RandomizedSubType pos1, Pattern pos2, no ShouldProcess).
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaRandomizedType")]
public sealed class GetDbaRandomizedTypeCommand : DbaBaseCmdlet
{
    /// <summary>Filter to the specified randomizer type(s).</summary>
    [Parameter(Position = 0)]
    public string[]? RandomizedType { get; set; }

    /// <summary>Filter to the specified randomizer subtype(s).</summary>
    [Parameter(Position = 1)]
    public string[]? RandomizedSubType { get; set; }

    /// <summary>A regex pattern to match against Type or SubType.</summary>
    [Parameter(Position = 2)]
    public string? Pattern { get; set; }

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
            RandomizedType, RandomizedSubType, Pattern, EnableException.ToBool(),
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
    // PS: the source begin block ($randomizerTypes CSV import, VERBATIM + -FunctionName on its Stop-Function)
    // PREPENDED to the process block (VERBATIM). The whole body is wrapped in a continue-guard foreach because the
    // begin catch Stop-Function -Continue sits at begin-top with no enclosing loop. $script:PSModuleRoot resolves in
    // module scope. No value-passed switch, no Test-Bound.
    private const string ProcessScript = """
param($RandomizedType, $RandomizedSubType, $Pattern, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([string[]]$RandomizedType, [string[]]$RandomizedSubType, [string]$Pattern, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }
    foreach ($__continueGuard in @(1)) {

        # Get all the random possibilities
        try {
            $randomizerTypes = Import-Csv (Resolve-Path -Path "$script:PSModuleRoot\bin\randomizer\en.randomizertypes.csv")
        } catch {
            Stop-Function -Message "Could not import randomized types" -FunctionName Get-DbaRandomizedType -Continue
        }



        if (Test-FunctionInterrupt) { return }

        $types = @()

        if ($Pattern) {
            $types += $randomizerTypes | Where-Object Type -match $Pattern
            $types += $randomizerTypes | Where-Object SubType -match $Pattern
        } else {
            $types = $randomizerTypes
        }

        if ($RandomizedType) {
            $types = $types | Where-Object Type -in $RandomizedType
        }

        if ($RandomizedSubType) {
            $types = $types | Where-Object SubType -in $RandomizedSubType
        }

        $types | Select-Object Type, SubType -Unique | Sort-Object Type, SubType

    }
} $RandomizedType $RandomizedSubType $Pattern $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
