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
/// Samples performance counters within data collectors. Port of
/// public/Get-DbaPfDataCollectorCounterSample.ps1 (W1-090). The W1-089 sibling one level
/// further down: InputObject credential AND counter ADOPTION (typed coercion,
/// statement-fault keeps prior, sticky across records); the refetch gate appends
/// Get-DbaPfDataCollectorCounter output per computer; the record-less type gate reads the
/// member-enumerated CounterObject; each counter object rides the VERBATIM sample hop (the
/// $params splat build with its [dbainstance] IsLocalHost cast, the no-op Get-Counter
/// -Credential/-ListSet pass-throughs, Get-Counter -ErrorAction Stop under the in-hop
/// Stop-Function -Continue - absorbed by the foreach shell - and the per-sample 18-prop
/// PSCustomObject + Select-DefaultView exclude pair). The -Counter filter applies ONLY
/// when -Counter is explicitly BOUND (Test-Bound), never via adoption. Surface pinned by
/// migration/baselines/Get-DbaPfDataCollectorCounterSample.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaPfDataCollectorCounterSample")]
public sealed class GetDbaPfDataCollectorCounterSampleCommand : DbaBaseCmdlet
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

    /// <summary>Streams samples continuously until CTRL+C.</summary>
    [Parameter]
    public SwitchParameter Continuous { get; set; }

    /// <summary>Passes through to Get-Counter -ListSet (the [switch[]] surface).</summary>
    [Parameter(Position = 5)]
    public SwitchParameter[]? ListSet { get; set; }

    /// <summary>The maximum number of samples per counter.</summary>
    [Parameter(Position = 6)]
    public int MaxSamples { get; set; }

    /// <summary>Seconds between samples.</summary>
    [Parameter(Position = 7)]
    public int SampleInterval { get; set; }

    /// <summary>Counter objects piped in from Get-DbaPfDataCollectorCounter.</summary>
    [Parameter(ValueFromPipeline = true, Position = 8)]
    public object[]? InputObject { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // PS: the $Credential and $Counter reassignments persist across process blocks;
    // $InputObject is re-bound FRESH on every pipeline record (even the same array
    // reference piped twice via -NoEnumerate), so the += growth never survives into
    // the next record.
    private object? _credential;
    private bool _credentialAdopted;
    private object? _counter;
    private bool _counterAdopted;
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
            catch (Exception ex) { StatementFault.Surface(this, ex, "Get-DbaPfDataCollectorCounterSample"); }
        }

        // PS: $Counter = $InputObject.Counter - a [string[]]-typed adoption; the value is
        // only ever READ by the Test-Bound-gated filter, so the coercion fault (kept
        // prior value) is the sole observable.
        object? inputCounter = MemberEnum(_accumulated, "Counter");
        if (LanguagePrimitives.IsTrue(inputCounter) && !TestBound("Counter"))
        {
            try
            {
                object? coerced = LanguagePrimitives.ConvertTo(inputCounter, typeof(string[]), CultureInfo.InvariantCulture);
                _counter = coerced;
                _counterAdopted = true;
            }
            catch (PipelineStoppedException) { throw; }
            catch (Exception ex) { StatementFault.Surface(this, ex, "Get-DbaPfDataCollectorCounterSample"); }
        }
        string[]? effectiveCounter = _counterAdopted ? (string[]?)_counter : Counter;

        bool haveInput = _accumulated.Count > 0 && PsTruthyList(_accumulated);
        if (!haveInput || (haveInput && TestBound("ComputerName")))
        {
            foreach (DbaInstanceParameter? computer in ComputerName ?? new DbaInstanceParameter[0])
            {
                try
                {
                    foreach (PSObject? fetched in NestedCommand.InvokeScoped(this, GetCounterObjectScript, computer, effectiveCredential, CollectorSet, Collector, BoundVerbose(), BoundDebug()))
                        _accumulated.Add(fetched);
                }
                catch (PipelineStoppedException) { throw; }
                catch (RuntimeException ex) { StatementFault.Surface(this, ex, "Get-DbaPfDataCollectorCounterSample"); }
            }
        }

        if (PsTruthyList(_accumulated))
        {
            if (!LanguagePrimitives.IsTrue(MemberEnum(_accumulated, "CounterObject")))
            {
                StopFunction("InputObject is not of the right type. Please use Get-DbaPfDataCollectorCounter.");
                return;
            }
        }

        foreach (object? counterObject in _accumulated)
        {
            // PS: if ((Test-Bound Counter) -and ($Counter -notcontains $counterobject.Name)) { continue }
            if (TestBound("Counter") && !ContainsValue(effectiveCounter, DotAccess(counterObject, "Name")))
                continue;

            // The hop owns the whole per-object body; the only throw-through is the
            // EE Stop-Function (the function's terminating path), which propagates.
            NestedCommand.InvokeScopedStreaming(this, item => WriteObject(item), SampleProjectionScript,
                    counterObject, effectiveCredential, Continuous.ToBool(), ListSet, MaxSamples, SampleInterval, EnableException.ToBool(), BoundVerbose(), BoundDebug());
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

    /// <summary>The PS dot operator on a single object (transit unwrap).</summary>
    private static object? DotAccess(object? item, string name)
    {
        if (item is null)
            return null;
        PSObject wrapped = PSObject.AsPSObject(item);
        PSPropertyInfo? direct = wrapped.Properties[name];
        if (direct is null)
            return null;
        object? value;
        try { value = direct.Value; }
        catch { return null; }
        if (value is PSObject psValue && psValue.BaseObject is not PSCustomObject)
            return psValue.BaseObject;
        return value;
    }

    /// <summary>PS -contains over the -Counter filter (elementwise -eq).</summary>
    private static bool ContainsValue(string[]? values, object? name)
    {
        if (values is null)
            return false;
        foreach (string? value in values)
        {
            if (PsOps.Eq(value, name))
                return true;
        }
        return false;
    }

    /// <summary>A bound -Debug carrier for the hop scopes (W1-044 convention).</summary>
    private object? BoundDebug()
    {
        object? debug;
        if (MyInvocation.BoundParameters.TryGetValue("Debug", out debug))
            return LanguagePrimitives.IsTrue(debug);
        return null;
    }

    /// <summary>A bound -Verbose carrier for the hop scopes (W1-044 convention).</summary>
    private object? BoundVerbose()
    {
        object? verbose;
        if (MyInvocation.BoundParameters.TryGetValue("Verbose", out verbose))
            return LanguagePrimitives.IsTrue(verbose);
        return null;
    }

    // PS: Get-DbaPfDataCollectorCounter per computer (nested public, verbose carrier).
    private const string GetCounterObjectScript = """
param($__computer, $Credential, $CollectorSet, $Collector, $__boundVerbose, $__boundDebug)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($__computer, $Credential, $CollectorSet, $Collector, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundVerbose) { $VerbosePreference = $(if ($__boundVerbose) { "Continue" } else { "SilentlyContinue" }) }
    if ($null -ne $__boundDebug) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }
    Get-DbaPfDataCollectorCounter -ComputerName $__computer -Credential $Credential -CollectorSet $CollectorSet -Collector $Collector
} $__computer $Credential $CollectorSet $Collector $__boundVerbose $__boundDebug 3>&1
""";

    // PS: the per-counterobject sample body VERBATIM ($params build incl. the
    // [dbainstance] IsLocalHost cast statement-fault, the Get-Counter splat with the
    // no-op -Credential/-ListSet pass-through quirks, the try/catch Stop-Function
    // -Continue, and the per-sample projection + Select-DefaultView exclude pair).
    private const string SampleProjectionScript = """
param($counterobject, $Credential, $Continuous, $ListSet, $MaxSamples, $SampleInterval, $EnableException, $__boundVerbose, $__boundDebug)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($counterobject, $Credential, $Continuous, $ListSet, $MaxSamples, $SampleInterval, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundVerbose) { $VerbosePreference = $(if ($__boundVerbose) { "Continue" } else { "SilentlyContinue" }) }
    if ($null -ne $__boundDebug) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }
    # the foreach shell absorbs Stop-Function -Continue's `continue` the way the
    # function's counterobject loop did - the caller then moves to the next object
    foreach ($__w1090Shell in 1) {
        $params = @{
            Counter = $counterobject.Name
        }

        if (-not ([dbainstance]$counterobject.ComputerName).IsLocalHost) {
            $params.Add("ComputerName", $counterobject.ComputerName)
        }

        if ($Credential) {
            $params.Add("Credential", $Credential)
        }

        if ($Continuous) {
            $params.Add("Continuous", $Continuous)
        }

        if ($ListSet) {
            $params.Add("ListSet", $ListSet)
        }

        if ($MaxSamples) {
            $params.Add("MaxSamples", $MaxSamples)
        }

        if ($SampleInterval) {
            $params.Add("SampleInterval", $SampleInterval)
        }

        if ($Continuous) {
            Get-Counter @params
        } else {
            try {
                $pscounters = Get-Counter @params -ErrorAction Stop
            } catch {
                # -FunctionName pins the attribution the function world gets from its
                # own call stack (the anonymous hop scriptblock reads <ScriptBlock>)
                Stop-Function -Message "Failure for $($counterobject.Name) on $($counterobject.ComputerName)." -ErrorRecord $_ -Continue -FunctionName Get-DbaPfDataCollectorCounterSample
            }

            foreach ($pscounter in $pscounters) {
                foreach ($sample in $pscounter.CounterSamples) {
                    [PSCustomObject]@{
                        ComputerName           = $counterobject.ComputerName
                        DataCollectorSet       = $counterobject.DataCollectorSet
                        DataCollector          = $counterobject.DataCollector
                        Name                   = $counterobject.Name
                        Timestamp              = $pscounter.Timestamp
                        Path                   = $sample.Path
                        InstanceName           = $sample.InstanceName
                        CookedValue            = $sample.CookedValue
                        RawValue               = $sample.RawValue
                        SecondValue            = $sample.SecondValue
                        MultipleCount          = $sample.MultipleCount
                        CounterType            = $sample.CounterType
                        SampleTimestamp        = $sample.Timestamp
                        SampleTimestamp100NSec = $sample.Timestamp100NSec
                        Status                 = $sample.Status
                        DefaultScale           = $sample.DefaultScale
                        TimeBase               = $sample.TimeBase
                        Sample                 = $pscounter.CounterSamples
                        CounterSampleObject    = $true
                    } | Select-DefaultView -ExcludeProperty Sample, CounterSampleObject
                }
            }
        }
    }
} $counterobject $Credential $Continuous $ListSet $MaxSamples $SampleInterval $EnableException $__boundVerbose $__boundDebug 3>&1
""";
}
