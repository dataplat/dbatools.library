#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;
using Microsoft.SqlServer.Management.Smo;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Gets the Resource Governor classifier function. Port of
/// public/Get-DbaRgClassifierFunction.ps1 (W1-096). Quirks preserved: the refetch loop
/// iterates $SqlInstance but passes the WHOLE ARRAY to every nested
/// Get-DbaResourceGovernor call (N instances append N x N governors); $InputObject is
/// re-bound fresh per pipeline record and grows via += within it (W1-089 law); the
/// match compares each [schema].[name] against the member-enumerated
/// $InputObject.ClassifierFunction of the WHOLE accumulated array (multi-input reads an
/// ARRAY and the scalar -eq coerces it to its $OFS join - never matches); the
/// truthiness-gated Add-Member quadruplet decorates the live SMO function; the
/// unconditional Select-DefaultView -InputObject feeds a null function's binder error
/// through the W1-045 2>&1 re-emission. No Stop-Function anywhere - the nested fetch
/// warns through the hop merge even under -EnableException. Surface pinned by
/// migration/baselines/Get-DbaRgClassifierFunction.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaRgClassifierFunction")]
public sealed class GetDbaRgClassifierFunctionCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Resource Governor objects piped in from Get-DbaResourceGovernor.</summary>
    [Parameter(ValueFromPipeline = true, Position = 2)]
    public ResourceGovernor[]? InputObject { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // PS: $InputObject is re-bound FRESH on every pipeline record; the += growth never
    // survives into the next record (W1-089 law).
    private List<object?> _accumulated = new List<object?>();

    protected override void ProcessRecord()
    {
        _accumulated = new List<object?>();
        if (InputObject is not null)
        {
            foreach (object? item in InputObject)
                _accumulated.Add(item);
        }

        // PS: foreach ($instance in $SqlInstance) { $InputObject += Get-DbaResourceGovernor
        // -SqlInstance $SqlInstance ... } - the WHOLE array rides every nested call.
        foreach (DbaInstanceParameter? instance in SqlInstance ?? new DbaInstanceParameter[0])
        {
            try
            {
                foreach (PSObject? fetched in NestedCommand.InvokeScoped(this, GetGovernorScript, SqlInstance, SqlCredential, BoundVerbose()))
                    _accumulated.Add(fetched);
            }
            catch (PipelineStoppedException) { throw; }
            catch (RuntimeException ex) { StatementFault.Surface(this, ex, "Get-DbaRgClassifierFunction"); }
        }

        object?[] accumulatedArray = _accumulated.ToArray();
        foreach (object? resourceGovernor in _accumulated)
        {
            try
            {
                foreach (PSObject? item in NestedCommand.InvokeScoped(this, ClassifierProjectionScript, resourceGovernor, accumulatedArray))
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
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (RuntimeException ex)
            {
                StatementFault.Surface(this, ex, "Get-DbaRgClassifierFunction");
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

    /// <summary>A bound -Verbose carrier for the hop scopes (W1-044 convention).</summary>
    private object? BoundVerbose()
    {
        object? verbose;
        if (MyInvocation.BoundParameters.TryGetValue("Verbose", out verbose))
            return LanguagePrimitives.IsTrue(verbose);
        return null;
    }

    // PS: Get-DbaResourceGovernor with the WHOLE $SqlInstance array (nested public,
    // verbose carrier).
    private const string GetGovernorScript = """
param($SqlInstance, $SqlCredential, $__boundVerbose)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($SqlInstance, $SqlCredential, $__boundVerbose)
    if ($null -ne $__boundVerbose) { $VerbosePreference = $(if ($__boundVerbose) { "Continue" } else { "SilentlyContinue" }) }
    Get-DbaResourceGovernor -SqlInstance $SqlInstance -SqlCredential $SqlCredential
} $SqlInstance $SqlCredential $__boundVerbose 3>&1
""";

    // PS: the per-governor body VERBATIM (the master UDF walk, the buggy
    // whole-array $InputObject.ClassifierFunction compare, the Add-Member quadruplet,
    // the unconditional SDV emission with binder-error re-emission).
    private const string ClassifierProjectionScript = """
param($resourcegov, $InputObject)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($resourcegov, $InputObject)
    $server = $resourcegov.Parent
    $classifierFunction = $null

    foreach ($currentFunction in $server.Databases["master"].UserDefinedFunctions) {
        $fullyQualifiedFunctionName = [string]::Format("[{0}].[{1}]", $currentFunction.Schema, $currentFunction.Name)
        if ($fullyQualifiedFunctionName -eq $InputObject.ClassifierFunction) {
            $classifierFunction = $currentFunction
        }
    }

    if ($classifierFunction) {
        Add-Member -Force -InputObject $classifierFunction -MemberType NoteProperty -Name ComputerName -value $resourcegov.ComputerName
        Add-Member -Force -InputObject $classifierFunction -MemberType NoteProperty -Name InstanceName -value $resourcegov.InstanceName
        Add-Member -Force -InputObject $classifierFunction -MemberType NoteProperty -Name SqlInstance -value $resourcegov.SqlInstance
        Add-Member -Force -InputObject $classifierFunction -MemberType NoteProperty -Name Database -value 'master'
    }

    Select-DefaultView -InputObject $classifierFunction -Property ComputerName, InstanceName, SqlInstance, Database, Schema, CreateDate, DateLastModified, Name, DataType
} $resourcegov $InputObject 3>&1 2>&1
""";
}
