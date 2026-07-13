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
/// Gets Resource Governor workload groups. Port of public/Get-DbaRgWorkloadGroup.ps1
/// (W1-098). The W1-096 sibling one level down: BOTH SqlInstance and InputObject are
/// ValueFromPipeline (the binder routes by type); the refetch loop iterates
/// $SqlInstance but passes the WHOLE ARRAY to every nested Get-DbaRgResourcePool call;
/// $InputObject re-binds fresh per record with += growth inside it (W1-089 law); each
/// pool rides one VERBATIM hop (the WorkloadGroups read, the truthiness gate, the PIPED
/// Add-Member triplet decorating every group element with the POOL's note values, and
/// the piped 13-col Select-DefaultView). No Stop-Function in the process body - the
/// nested fetch warns through the hop merge even under -EnableException. Surface pinned
/// by migration/baselines/Get-DbaRgWorkloadGroup.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaRgWorkloadGroup")]
public sealed class GetDbaRgWorkloadGroupCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Resource pool objects piped in from Get-DbaRgResourcePool.</summary>
    [Parameter(ValueFromPipeline = true, Position = 2)]
    public ResourcePool[]? InputObject { get; set; }

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

        // PS: foreach ($instance in $SqlInstance) { $InputObject += Get-DbaRgResourcePool
        // -SqlInstance $SqlInstance ... } - the WHOLE array rides every nested call.
        foreach (DbaInstanceParameter? instance in SqlInstance ?? new DbaInstanceParameter[0])
        {
            try
            {
                foreach (PSObject? fetched in NestedCommand.InvokeScoped(this, GetPoolScript, SqlInstance, SqlCredential, BoundVerbose()))
                    _accumulated.Add(fetched);
            }
            catch (PipelineStoppedException) { throw; }
            catch (RuntimeException ex) { StatementFault.Surface(this, ex, "Get-DbaRgWorkloadGroup"); }
        }

        foreach (object? pool in _accumulated)
        {
            try
            {
                foreach (PSObject? item in NestedCommand.InvokeScoped(this, GroupProjectionScript, pool))
                    WriteObject(item);
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (RuntimeException ex)
            {
                StatementFault.Surface(this, ex, "Get-DbaRgWorkloadGroup");
            }
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

    // PS: Get-DbaRgResourcePool with the WHOLE $SqlInstance array (nested public,
    // verbose carrier).
    private const string GetPoolScript = """
param($SqlInstance, $SqlCredential, $__boundVerbose)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($SqlInstance, $SqlCredential, $__boundVerbose)
    if ($null -ne $__boundVerbose) { $VerbosePreference = $(if ($__boundVerbose) { "Continue" } else { "SilentlyContinue" }) }
    Get-DbaRgResourcePool -SqlInstance $SqlInstance -SqlCredential $SqlCredential
} $SqlInstance $SqlCredential $__boundVerbose 3>&1
""";

    // PS: the per-pool body VERBATIM (WorkloadGroups read, truthiness gate, the PIPED
    // Add-Member triplet with the pool's note values, the piped Select-DefaultView).
    private const string GroupProjectionScript = """
param($pool)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($pool)
    $group = $pool.WorkloadGroups
    if ($group) {
        $group | Add-Member -Force -MemberType NoteProperty -Name ComputerName -value $pool.ComputerName
        $group | Add-Member -Force -MemberType NoteProperty -Name InstanceName -value $pool.InstanceName
        $group | Add-Member -Force -MemberType NoteProperty -Name SqlInstance -value $pool.SqlInstance
        $group | Select-DefaultView -Property ComputerName, InstanceName, SqlInstance, Id, Name, ExternalResourcePoolName, GroupMaximumRequests, Importance, IsSystemObject, MaximumDegreeOfParallelism, RequestMaximumCpuTimeInSeconds, RequestMaximumMemoryGrantPercentage, RequestMemoryGrantTimeoutInSeconds
    }
} $pool 3>&1
""";
}
