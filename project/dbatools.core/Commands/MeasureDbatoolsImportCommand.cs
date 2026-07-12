#nullable enable

using System.Collections;
using System.Management.Automation;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Returns the dbatools module-import timing ledger, filtered to steps that took
/// measurable time. Port of public/Measure-DbatoolsImport.ps1 (a parameterless one-liner:
/// $script:dbatools_ImportPerformance | Where-Object Duration -ne '00:00:00'). The module
/// variable is read LIVE off the dbatools script module; a blank ledger flows through the
/// pipeline as one $null and PASSES the filter, exactly like the function (lab-proven under
/// the RB-IMP-51 harness blanking). Surface pinned by
/// migration/baselines/Measure-DbatoolsImport.json (no declared parameters).
/// </summary>
[Cmdlet(VerbsDiagnostic.Measure, "DbatoolsImport")]
public sealed class MeasureDbatoolsImportCommand : DbaBaseCmdlet
{
    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void EndProcessing()
    {
        object? performance = GetModuleVariable("dbatools_ImportPerformance");
        IEnumerable? steps = LanguagePrimitives.GetEnumerable(performance);
        if (steps is null)
        {
            // A scalar (including the blank $null ledger) pipes through as one item.
            EmitWhenNonZero(performance);
            return;
        }
        foreach (object? step in steps)
            EmitWhenNonZero(step);
    }

    /// <summary>PS: Where-Object Duration -ne '00:00:00' (a null step's null Duration
    /// compares unequal and passes).</summary>
    private void EmitWhenNonZero(object? step)
    {
        if (!PsOps.Eq(PsProperty.Get(step, "Duration"), "00:00:00"))
            WriteObject(step);
    }

    /// <summary>Reads a $script:-scoped variable LIVE off the dbatools script module.</summary>
    private object? GetModuleVariable(string variableName)
    {
        Hashtable getModuleParams = new();
        getModuleParams["Name"] = "dbatools";
        foreach (PSObject wrapped in NestedCommand.Invoke(this, "Get-Module", getModuleParams))
        {
            if (wrapped?.BaseObject is PSModuleInfo module && module.ModuleType == ModuleType.Script)
                return module.SessionState.PSVariable.GetValue("script:" + variableName);
        }
        return null;
    }
}
