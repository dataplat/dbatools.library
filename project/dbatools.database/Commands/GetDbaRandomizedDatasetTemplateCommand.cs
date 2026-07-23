#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Lists available randomized-dataset templates. Port of public/Get-DbaRandomizedDatasetTemplate.ps1;
/// the workflow remains a module-scoped PowerShell compatibility hop.
///
/// A process-only port; there is NO ValueFromPipeline parameter, so process fires once. The source begin block
/// builds the $templates list (from the default template directory unless -ExcludeDefault) which the process block
/// then extends with -Path templates, filters by -Template, and emits. Since $templates is param-derived and process
/// fires once, the begin block is PREPENDED (verbatim) into the process hop so $templates is in scope. ExcludeDefault
/// is a switch consumed as a VALUE (if (-not $ExcludeDefault)), so it is passed as a marshaled bool (.ToBool()) into
/// an UNTYPED inner param - typing it [switch] would shift positional binding (switch-in-hop-param law; binding-probed
/// BOUND-OK). There is NO Stop-Function anywhere, so the verbatim `if (Test-FunctionInterrupt) { return }` is inert
/// (nothing ever sets the interrupt) and needs no carry. The body uses $script:PSModuleRoot, which resolves in the
/// real module scope. No Test-Bound, no -FunctionName edits, no ShouldProcess. Surface pinned by
/// migration/baselines/Get-DbaRandomizedDatasetTemplate.json (Template pos0, Path pos1, ExcludeDefault switch non-positional, no ShouldProcess).
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaRandomizedDatasetTemplate")]
public sealed class GetDbaRandomizedDatasetTemplateCommand : DbaBaseCmdlet
{
    /// <summary>Filter to the specified template(s) by base name.</summary>
    [Parameter(Position = 0)]
    public string[]? Template { get; set; }

    /// <summary>Additional path(s) to search for template files.</summary>
    [Parameter(Position = 1)]
    public string[]? Path { get; set; }

    /// <summary>Exclude the built-in default templates.</summary>
    [Parameter]
    public SwitchParameter ExcludeDefault { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            Template, Path, ExcludeDefault.ToBool(), EnableException.ToBool(),
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
    // PS: the source begin block ( default-template gather, VERBATIM) PREPENDED to the process block
    // (VERBATIM). No edits needed - no Stop-Function/Write-Message/Test-Bound. $ExcludeDefault arrives as a marshaled
    // bool (if (-not $ExcludeDefault)); its inner param is UNTYPED. $script:PSModuleRoot resolves in module scope.
    private const string ProcessScript = """
param($Template, $Path, $ExcludeDefault, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([string[]]$Template, [string[]]$Path, $ExcludeDefault, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        $templates = @()

        # Get all the default templates
        if (-not $ExcludeDefault) {
            $templates += Get-ChildItem (Resolve-Path -Path "$script:PSModuleRoot\bin\randomizer\templates\*.json")
        }

        if (Test-FunctionInterrupt) { return }

        # Get the templates from the file path
        foreach ($p in $Path) {
            $templates += Get-ChildItem (Resolve-Path -Path "$Path\*.json")
        }

        # Filter the template if neccesary
        if ($Template) {
            $templates = $templates | Where-Object BaseName -in $Template
        }

        $templates | Select-Object BaseName, FullName
} $Template $Path $ExcludeDefault $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}