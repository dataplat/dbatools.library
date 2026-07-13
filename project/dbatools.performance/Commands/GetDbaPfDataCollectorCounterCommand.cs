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
/// Lists performance counters within data collectors. Port of
/// public/Get-DbaPfDataCollectorCounter.ps1 (W1-089). The W1-088 sibling one level down:
/// InputObject credential ADOPTION (typed coercion, statement-fault keeps prior); the
/// refetch gate appends Get-DbaPfDataCollector output per computer (W1-070 accumulation);
/// the record-less type gate reads the member-enumerated DataCollectorObject; each
/// collector object rides the VERBATIM projection hop (the Counters walk, the
/// -notcontains Counter filter, the 8-prop PSCustomObject and its Select-DefaultView
/// exclude list). Surface pinned by migration/baselines/Get-DbaPfDataCollectorCounter.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaPfDataCollectorCounter")]
public sealed class GetDbaPfDataCollectorCounterCommand : DbaBaseCmdlet
{
    /// <summary>The target computer(s); defaults to the local computer.</summary>
    [Parameter(Position = 0)]
    public DbaInstanceParameter[]? ComputerName { get; set; } = BuildDefaultComputerName();

    private static DbaInstanceParameter[]? BuildDefaultComputerName()
    {
        string? name = Environment.GetEnvironmentVariable("COMPUTERNAME");
        if (string.IsNullOrEmpty(name))
            return null;
        return new DbaInstanceParameter[] { new DbaInstanceParameter(name) };
    }

    /// <summary>Windows credential for the remote reads.</summary>
    [Parameter(Position = 1)]
    public PSCredential? Credential { get; set; }

    /// <summary>The collector set name(s) to include.</summary>
    [Parameter(Position = 2)]
    [Alias("DataCollectorSet")]
    public string[]? CollectorSet { get; set; }

    /// <summary>The collector name(s) to include.</summary>
    [Parameter(Position = 3)]
    [Alias("DataCollector")]
    public string[]? Collector { get; set; }

    /// <summary>The counter name(s) to include.</summary>
    [Parameter(Position = 4)]
    public string[]? Counter { get; set; }

    /// <summary>Collector objects piped in from Get-DbaPfDataCollector.</summary>
    [Parameter(ValueFromPipeline = true, Position = 5)]
    public object[]? InputObject { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // PS: $Credential reassignment persists across process blocks; $InputObject is
    // re-bound FRESH on every pipeline record (even the same array reference piped
    // twice via -NoEnumerate), so the += growth never survives into the next record.
    private object? _credential;
    private bool _credentialAdopted;
    private List<object?> _accumulated = new List<object?>();

    protected override void ProcessRecord()
    {
        _accumulated = new List<object?>();
        if (InputObject is not null)
        {
            foreach (object? item in InputObject)
                _accumulated.Add(item);
        }

        object? effectiveCredential = _credentialAdopted ? _credential : Credential;

        // PS: the [PSCredential]-typed adoption COERCES; a failed coercion
        // statement-faults and keeps the prior value (the W1-088 shape).
        object? inputCredential = MemberEnum(_accumulated, "Credential");
        if (LanguagePrimitives.IsTrue(inputCredential) && !TestBound("Credential"))
        {
            try
            {
                object? coerced = LanguagePrimitives.ConvertTo(inputCredential, typeof(PSCredential), CultureInfo.InvariantCulture);
                effectiveCredential = coerced;
                _credential = coerced;
                _credentialAdopted = true;
            }
            catch (PipelineStoppedException) { throw; }
            catch (Exception ex) { StatementFault.Surface(this, ex, "Get-DbaPfDataCollectorCounter"); }
        }

        bool haveInput = _accumulated.Count > 0 && PsTruthyList(_accumulated);
        if (!haveInput || (haveInput && TestBound("ComputerName")))
        {
            foreach (DbaInstanceParameter? computer in ComputerName ?? new DbaInstanceParameter[0])
            {
                try
                {
                    foreach (PSObject? fetched in NestedCommand.InvokeScoped(this, GetCollectorScript, computer, effectiveCredential, CollectorSet, Collector, BoundVerbose()))
                        _accumulated.Add(fetched);
                }
                catch (PipelineStoppedException) { throw; }
                catch (RuntimeException ex) { StatementFault.Surface(this, ex, "Get-DbaPfDataCollectorCounter"); }
            }
        }

        if (PsTruthyList(_accumulated))
        {
            if (!LanguagePrimitives.IsTrue(MemberEnum(_accumulated, "DataCollectorObject")))
            {
                StopFunction("InputObject is not of the right type. Please use Get-DbaPfDataCollector.");
                return;
            }
        }

        foreach (object? counterObject in _accumulated)
        {
            try
            {
                foreach (PSObject? item in NestedCommand.InvokeScoped(this, CounterProjectionScript, counterObject, Counter, effectiveCredential))
                    WriteObject(item);
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (RuntimeException ex)
            {
                StatementFault.Surface(this, ex, "Get-DbaPfDataCollectorCounter");
            }
        }
    }

    /// <summary>PS truthiness of the accumulated list as an array value.</summary>
    private static bool PsTruthyList(List<object?> values)
    {
        if (values.Count == 0)
            return false;
        if (values.Count == 1)
            return LanguagePrimitives.IsTrue(values[0]);
        return true;
    }

    /// <summary>PS member enumeration over the accumulated list ($list.Name).</summary>
    private static object? MemberEnum(List<object?> items, string name)
    {
        List<object?> collected = new List<object?>();
        foreach (object? item in items)
        {
            if (item is null)
                continue;
            PSObject wrapped = PSObject.AsPSObject(item);
            PSPropertyInfo? property = wrapped.Properties[name];
            if (property is not null)
            {
                object? value;
                try { value = property.Value; }
                catch { value = null; }
                if (value is PSObject psValue && psValue.BaseObject is not PSCustomObject)
                    value = psValue.BaseObject;
                // PS member enumeration FLATTENS collection values one level (W1-060 law).
                if (value is not string && LanguagePrimitives.GetEnumerable(value) is IEnumerable elements)
                {
                    foreach (object? element in elements)
                    {
                        object? unwrapped = element;
                        if (unwrapped is PSObject psElement && psElement.BaseObject is not PSCustomObject)
                            unwrapped = psElement.BaseObject;
                        collected.Add(unwrapped);
                    }
                }
                else
                {
                    collected.Add(value);
                }
            }
            else if (wrapped.BaseObject is PSCustomObject)
            {
                collected.Add(null);
            }
        }
        if (collected.Count == 0)
            return null;
        if (collected.Count == 1)
            return collected[0];
        return collected.ToArray();
    }

    /// <summary>A bound -Verbose carrier for the hop scopes (W1-044 convention).</summary>
    private object? BoundVerbose()
    {
        object? verbose;
        if (MyInvocation.BoundParameters.TryGetValue("Verbose", out verbose))
            return LanguagePrimitives.IsTrue(verbose);
        return null;
    }

    // PS: Get-DbaPfDataCollector per computer (nested public, verbose carrier).
    private const string GetCollectorScript = """
param($__computer, $Credential, $CollectorSet, $Collector, $__boundVerbose)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($__computer, $Credential, $CollectorSet, $Collector, $__boundVerbose)
    if ($null -ne $__boundVerbose) { $VerbosePreference = $(if ($__boundVerbose) { "Continue" } else { "SilentlyContinue" }) }
    Get-DbaPfDataCollector -ComputerName $__computer -Credential $Credential -CollectorSet $CollectorSet -Collector $Collector
} $__computer $Credential $CollectorSet $Collector $__boundVerbose 3>&1
""";

    // PS: the per-collector counter walk + emission VERBATIM.
    private const string CounterProjectionScript = """
param($counterobject, $Counter, $Credential)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($counterobject, $Counter, $Credential)
    foreach ($countername in $counterobject.Counters) {
        if ($Counter -and $Counter -notcontains $countername) { continue }
        [PSCustomObject]@{
            ComputerName        = $counterobject.ComputerName
            DataCollectorSet    = $counterobject.DataCollectorSet
            DataCollector       = $counterobject.Name
            DataCollectorSetXml = $counterobject.DataCollectorSetXml
            Name                = $countername
            FileName            = $counterobject.FileName
            CounterObject       = $true
            Credential          = $Credential
        } | Select-DefaultView -ExcludeProperty DataCollectorObject, Credential, CounterObject, DataCollectorSetXml
    }
} $counterobject $Counter $Credential 3>&1
""";
}
