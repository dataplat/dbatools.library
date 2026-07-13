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
/// Lists performance-counter data collectors within collector sets. Port of
/// public/Get-DbaPfDataCollector.ps1 (W1-088). Quirks preserved: an InputObject
/// Credential is ADOPTED when -Credential is unbound (member-enum read, sticky across
/// records); the refetch gate (no input OR input plus bound -ComputerName) appends
/// Get-DbaPfDataCollectorSet output per computer to the accumulated InputObject
/// (W1-070 reference-reset law); the type gate's record-less Stop-Function fires when
/// the member-enumerated DataCollectorSetObject reads falsy; each SET rides the VERBATIM
/// XML-walk hop ([xml]$set.Xml adapter walk, the -notcontains Collector filter, the
/// UNC remote-location build, the 21-prop PSCustomObject and its Select-DefaultView
/// column list). Surface pinned by migration/baselines/Get-DbaPfDataCollector.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaPfDataCollector")]
public sealed class GetDbaPfDataCollectorCommand : DbaBaseCmdlet
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

    /// <summary>Collector-set objects piped in from Get-DbaPfDataCollectorSet.</summary>
    [Parameter(ValueFromPipeline = true, Position = 4)]
    public object[]? InputObject { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // PS: $Credential reassignment and $InputObject += persist across process blocks.
    private object? _credential;
    private bool _credentialAdopted;
    private List<object?> _accumulated = new List<object?>();
    private object? _lastBoundInputObject;

    protected override void ProcessRecord()
    {
        if (!ReferenceEquals(InputObject, _lastBoundInputObject))
        {
            _accumulated = new List<object?>();
            if (InputObject is not null)
            {
                foreach (object? item in InputObject)
                    _accumulated.Add(item);
            }
            _lastBoundInputObject = InputObject;
        }

        object? effectiveCredential = _credentialAdopted ? _credential : Credential;

        // PS: if ($InputObject.Credential -and (Test-Bound Credential -Not)) - the
        // [PSCredential]-typed assignment COERCES; a failed coercion (multi-input
        // Object[]) statement-faults and keeps the prior value.
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
            catch (Exception ex) { StatementFault.Surface(this, ex, "Get-DbaPfDataCollector"); }
        }

        // PS: refetch when no input OR input plus an explicitly bound -ComputerName.
        bool haveInput = _accumulated.Count > 0 && PsTruthyList(_accumulated);
        if (!haveInput || (haveInput && TestBound("ComputerName")))
        {
            foreach (DbaInstanceParameter? computer in ComputerName ?? new DbaInstanceParameter[0])
            {
                try
                {
                    foreach (PSObject? fetched in NestedCommand.InvokeScoped(this, GetCollectorSetScript, computer, effectiveCredential, CollectorSet, BoundVerbose()))
                        _accumulated.Add(fetched);
                }
                catch (PipelineStoppedException) { throw; }
                catch (RuntimeException ex) { StatementFault.Surface(this, ex, "Get-DbaPfDataCollector"); }
            }
        }

        if (PsTruthyList(_accumulated))
        {
            if (!LanguagePrimitives.IsTrue(MemberEnum(_accumulated, "DataCollectorSetObject")))
            {
                StopFunction("InputObject is not of the right type. Please use Get-DbaPfDataCollectorSet.");
                return;
            }
        }

        foreach (object? set in _accumulated)
        {
            // PS: $collectorxml is a process-local - a faulted [xml] cast keeps the
            // STALE value from the prior set and the loop re-projects it with the
            // CURRENT $set values.
            try
            {
                _collectorXml = PipelineValue(NestedCommand.InvokeScoped(this, CollectorXmlScript, set));
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (RuntimeException ex)
            {
                StatementFault.Surface(this, ex, "Get-DbaPfDataCollector");
            }
            try
            {
                foreach (PSObject? item in NestedCommand.InvokeScoped(this, SetProjectionScript, _collectorXml, set, Collector, effectiveCredential))
                    WriteObject(item);
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (RuntimeException ex)
            {
                StatementFault.Surface(this, ex, "Get-DbaPfDataCollector");
            }
        }
    }

    private object? _collectorXml;

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
                collected.Add(value);
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

    // PS: Get-DbaPfDataCollectorSet per computer (nested public, verbose carrier).
    private const string GetCollectorSetScript = """
param($__computer, $Credential, $CollectorSet, $__boundVerbose)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($__computer, $Credential, $CollectorSet, $__boundVerbose)
    if ($null -ne $__boundVerbose) { $VerbosePreference = $(if ($__boundVerbose) { "Continue" } else { "SilentlyContinue" }) }
    Get-DbaPfDataCollectorSet -ComputerName $__computer -Credential $Credential -CollectorSet $CollectorSet
} $__computer $Credential $CollectorSet $__boundVerbose 3>&1
""";

    // PS: the $collectorxml assignment (statement-conditional, stale-able).
    private const string CollectorXmlScript = """
param($set)
([xml]$set.Xml).DataCollectorSet.PerformanceCounterDataCollector
""";

    // PS: the per-set XML walk + emission VERBATIM (adapter reads, the Collector filter,
    // the UNC remote build, the PSCustomObject and its Select-DefaultView columns).
    private const string SetProjectionScript = """
param($collectorxml, $set, $Collector, $Credential)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($collectorxml, $set, $Collector, $Credential)
    $columns = 'ComputerName', 'DataCollectorSet', 'Name', 'DataCollectorType', 'DataSourceName', 'FileName', 'FileNameFormat', 'FileNameFormatPattern', 'LatestOutputLocation', 'LogAppend', 'LogCircular', 'LogFileFormat', 'LogOverwrite', 'SampleInterval', 'SegmentMaxRecords', 'Counters'
    foreach ($col in $collectorxml) {
        if ($Collector -and $Collector -notcontains $col.Name) {
            continue
        }

        $outputlocation = $col.LatestOutputLocation
        if ($outputlocation) {
            $dir = ($outputlocation).Replace(':', '$')
            $remote = "\\$($set.ComputerName)\$dir"
        } else {
            $remote = $null
        }

        [PSCustomObject]@{
            ComputerName               = $set.ComputerName
            DataCollectorSet           = $set.Name
            Name                       = $col.Name
            FileName                   = $col.FileName
            DataCollectorType          = $col.DataCollectorType
            FileNameFormat             = $col.FileNameFormat
            FileNameFormatPattern      = $col.FileNameFormatPattern
            LogAppend                  = $col.LogAppend
            LogCircular                = $col.LogCircular
            LogOverwrite               = $col.LogOverwrite
            LatestOutputLocation       = $col.LatestOutputLocation
            DataCollectorSetXml        = $set.Xml
            RemoteLatestOutputLocation = $remote
            DataSourceName             = $col.DataSourceName
            SampleInterval             = $col.SampleInterval
            SegmentMaxRecords          = $col.SegmentMaxRecords
            LogFileFormat              = $col.LogFileFormat
            Counters                   = $col.Counter
            CounterDisplayNames        = $col.CounterDisplayName
            CollectorXml               = $col
            DataCollectorObject        = $true
            Credential                 = $Credential
        } | Select-DefaultView -Property $columns
    }
} $collectorxml $set $Collector $Credential 3>&1
""";
}
