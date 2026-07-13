#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Lists the bundled perfmon collector-set templates. Port of
/// public/Get-DbaPfDataCollectorSetTemplate.ps1 (W1-092). The begin block resolves
/// $script:PSModuleRoot (with the live-module fallback the source itself carries for the
/// RB-IMP-51 harness class), recomputes an UNBOUND -Path from it, Import-Clixml's the
/// metadata bag, and mutates $Pattern like-to-regex (the unbound [string] reads "" so the
/// Replace pair never faults - W1-087 law). Each -Path directory rides one VERBATIM hop:
/// the Get-ChildItem *.xml listing, the Where-Object BaseName -in Template filter, the
/// per-file [xml](Get-Content) cast under Stop-Function -Continue (contained by the hop's
/// own file loop; explicit -FunctionName per the W1-090 law), the $xml.DataCollectorSet
/// adapter walk with the metadata Where-Object Name -eq lookup, the -match Pattern pair,
/// and the 6-prop PSCustomObject + Select-DefaultView exclude pair. EE Stop-Function
/// throws propagate out of the hop uncaught. Surface pinned by
/// migration/baselines/Get-DbaPfDataCollectorSetTemplate.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaPfDataCollectorSetTemplate")]
public sealed class GetDbaPfDataCollectorSetTemplateCommand : DbaBaseCmdlet
{
    /// <summary>The template directory or directories.</summary>
    [Parameter(Position = 0)]
    public string[]? Path { get; set; }

    /// <summary>Regex (or like-mutated) filter against template name/description.</summary>
    [Parameter(Position = 1)]
    public string? Pattern { get; set; }

    /// <summary>The template base name(s) to include.</summary>
    [Parameter(Position = 2)]
    public string[]? Template { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    private object? _metadata;
    private string _pattern = "";

    protected override void BeginProcessing()
    {
        // PS: $moduleRoot = $script:PSModuleRoot (live-module fallback), unbound $Path
        // recompute, and the Import-Clixml metadata read - one begin hop. A hashtable
        // rides the seam without the enumeration collapse a bare tuple suffers.
        Collection<PSObject> results = NestedCommand.InvokeScoped(this, BeginScript);
        Hashtable? bag = results.Count == 1 ? PSObject.AsPSObject(results[0]).BaseObject as Hashtable : null;
        object? moduleRoot = bag is not null ? bag["ModuleRoot"] : null;
        _metadata = bag is not null ? bag["Metadata"] : null;

        if (!TestBound("Path"))
            Path = new string[] { PsText(moduleRoot) + "\\bin\\perfmontemplates\\collectorsets" };

        // PS: $Pattern = $Pattern.Replace("*", ".*").Replace("..*", ".*") - the unbound
        // [string] parameter reads "" so the pair never faults.
        _pattern = (Pattern ?? "").Replace("*", ".*").Replace("..*", ".*");
    }

    protected override void ProcessRecord()
    {
        foreach (string? directory in Path ?? new string[0])
        {
            // Non-terminating provider errors (Get-ChildItem on a bad -Path) merge back
            // 2>&1 and re-emit with the silent-bag compensation (the W1-045 seam); EE
            // Stop-Function throws propagate uncaught (the function's terminating path).
            foreach (PSObject? item in NestedCommand.InvokeScoped(this, DirectoryProjectionScript, directory, Template, _pattern, _metadata, EnableException.ToBool(), BoundVerbose()))
            {
                if (item?.BaseObject is ErrorRecord nestedError)
                {
                    RemoveHopErrorBookkeeping(nestedError);
                    WriteError(nestedError);
                }
                else
                {
                    WriteObject(item);
                }
            }
        }
    }

    /// <summary>Removes the silent $error copy the nested pipeline bagged for a merged-back
    /// non-terminating record (the W1-045 compensation).</summary>
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
            // best-effort bookkeeping
        }
    }

    /// <summary>PS string interpolation via LanguagePrimitives (invariant).</summary>
    private static string PsText(object? value)
    {
        if (value is null)
            return "";
        return (string)LanguagePrimitives.ConvertTo(value, typeof(string), CultureInfo.InvariantCulture);
    }

    /// <summary>A bound -Verbose carrier for the hop scopes (W1-044 convention).</summary>
    private object? BoundVerbose()
    {
        object? verbose;
        if (MyInvocation.BoundParameters.TryGetValue("Verbose", out verbose))
            return LanguagePrimitives.IsTrue(verbose);
        return null;
    }

    // PS: the begin block - module-root resolution (source-carried fallback), metadata
    // Import-Clixml - returned as a 2-tuple.
    private const string BeginScript = """
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    $moduleRoot = $script:PSModuleRoot
    if (-not $moduleRoot) {
        $moduleRoot = (Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1).ModuleBase
    }
    $metadata = Import-Clixml "$moduleRoot\bin\perfmontemplates\collectorsets.xml"
    @{ ModuleRoot = $moduleRoot; Metadata = $metadata }
} 3>&1
""";

    // PS: the per-directory process body VERBATIM (file listing, Template filter, the
    // [xml] cast under Stop-Function -Continue - contained by the hop's own file loop,
    // -FunctionName pinned per the W1-090 law - the adapter walk, the metadata lookup,
    // the Pattern pair, and the projection + Select-DefaultView exclude pair).
    private const string DirectoryProjectionScript = """
param($__directory, $Template, $Pattern, $metadata, $EnableException, $__boundVerbose)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($__directory, $Template, $Pattern, $metadata, $EnableException, $__boundVerbose)
    if ($null -ne $__boundVerbose) { $VerbosePreference = $(if ($__boundVerbose) { "Continue" } else { "SilentlyContinue" }) }
    $files = Get-ChildItem "$__directory\*.xml"

    if ($Template) {
        $files = $files | Where-Object BaseName -in $Template
    }

    foreach ($file in $files) {
        try {
            $xml = [xml](Get-Content $file)
        } catch {
            Stop-Function -Message "Failure" -ErrorRecord $_ -Target $file -Continue -FunctionName Get-DbaPfDataCollectorSetTemplate
        }

        foreach ($dataset in $xml.DataCollectorSet) {
            $meta = $metadata | Where-Object Name -eq $dataset.name
            if ($Pattern) {
                if (
                    ($dataset.Name -match $Pattern) -or
                    ($dataset.Description -match $Pattern)
                ) {
                    [PSCustomObject]@{
                        Name        = $dataset.name
                        Source      = $meta.Source
                        UserAccount = $dataset.useraccount
                        Description = $dataset.Description
                        Path        = $file
                        File        = $file.Name
                    } | Select-DefaultView -ExcludeProperty File, Path
                }
            } else {
                [PSCustomObject]@{
                    Name        = $dataset.name
                    Source      = $meta.Source
                    UserAccount = $dataset.useraccount
                    Description = $dataset.Description
                    Path        = $file
                    File        = $file.Name
                } | Select-DefaultView -ExcludeProperty File, Path
            }
        }
    }
} $__directory $Template $Pattern $metadata $EnableException $__boundVerbose 3>&1 2>&1
""";
}
