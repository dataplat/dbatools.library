#nullable enable

using System;
using System.Collections.Generic;
using System.Management.Automation;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Parameter;
using Dataplat.Dbatools.Utility;
using Microsoft.SqlServer.Management.Smo;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves Resource Governor resource pools (internal or external).
/// Port of public/Get-DbaRgResourcePool.ps1; surface pinned by migration/baselines/Get-DbaRgResourcePool.json.
/// The Get-DbaResourceGovernor resolution runs as an inline SMO read (server.ResourceGovernor
/// with the same instance decorations the PS helper applies).
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaRgResourcePool")]
[OutputType(typeof(ResourcePool), typeof(ExternalResourcePool))]
public sealed class GetDbaRgResourcePoolCommand : DbaInstanceCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(ValueFromPipeline = true, Position = 0)]
    public override DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public override PSCredential? SqlCredential { get; set; }

    /// <summary>Internal (default) or External resource pools.</summary>
    [Parameter(Position = 2)]
    [ValidateSet("Internal", "External")]
    public string Type { get; set; } = "Internal";

    /// <summary>ResourceGovernor objects piped in from Get-DbaResourceGovernor.</summary>
    [Parameter(ValueFromPipeline = true, Position = 3)]
    public ResourceGovernor[]? InputObject { get; set; }

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        List<ResourceGovernor> governors = new();
        if (InputObject is { Length: > 0 })
        {
            governors.AddRange(InputObject);
        }

        // PS quirk preserved: the source loops $SqlInstance but passes the WHOLE array to
        // Get-DbaResourceGovernor on every iteration, so N instances yield N x N governors.
        foreach (DbaInstanceParameter _ in SqlInstance ?? Array.Empty<DbaInstanceParameter>())
        {
            foreach (DbaInstanceParameter instance in SqlInstance!)
            {
                // Inline Get-DbaResourceGovernor: Connect-DbaInstance -MinimumVersion 10
                Server? server = ConnectInstance(instance, "Failure", minimumVersion: 10);
                if (server is null)
                {
                    continue;
                }
                ResourceGovernor resourcegov = server.ResourceGovernor;
                if (resourcegov is not null)
                {
                    PSObject wrappedGov = PSObject.AsPSObject(resourcegov);
                    ReplaceNoteProperty(wrappedGov, "ComputerName", SmoServerExtensions.GetComputerName(server));
                    ReplaceNoteProperty(wrappedGov, "InstanceName", server.ServiceName);
                    ReplaceNoteProperty(wrappedGov, "SqlInstance", SmoServerExtensions.GetDomainInstanceName(server));
                    governors.Add(resourcegov);
                }
            }
        }

        foreach (ResourceGovernor resourcegov in governors)
        {
            if (string.Equals(Type, "External", StringComparison.OrdinalIgnoreCase))
            {
                ExternalResourcePoolCollection? pools = resourcegov.ExternalResourcePools;
                if (pools is { Count: > 0 })
                {
                    foreach (ExternalResourcePool pool in pools)
                    {
                        EmitPool(pool, resourcegov);
                    }
                }
            }
            else
            {
                ResourcePoolCollection? pools = resourcegov.ResourcePools;
                if (pools is { Count: > 0 })
                {
                    foreach (ResourcePool pool in pools)
                    {
                        EmitPool(pool, resourcegov);
                    }
                }
            }
        }
    }

    private void EmitPool(object pool, ResourceGovernor resourcegov)
    {
        PSObject wrapped = PSObject.AsPSObject(pool);
        ReplaceNoteProperty(wrapped, "ComputerName", SmoServerExtensions.GetPSProperty(resourcegov, "ComputerName"));
        ReplaceNoteProperty(wrapped, "InstanceName", SmoServerExtensions.GetPSProperty(resourcegov, "InstanceName"));
        ReplaceNoteProperty(wrapped, "SqlInstance", SmoServerExtensions.GetPSProperty(resourcegov, "SqlInstance"));

        OutputHelper.SetDefaultDisplayPropertySet(wrapped,
            "ComputerName", "InstanceName", "SqlInstance", "Id", "Name", "CapCpuPercentage", "IsSystemObject",
            "MaximumCpuPercentage", "MaximumIopsPerVolume", "MaximumMemoryPercentage", "MinimumCpuPercentage",
            "MinimumIopsPerVolume", "MinimumMemoryPercentage", "WorkloadGroups");

        WriteObject(wrapped);
    }

    private static void ReplaceNoteProperty(PSObject wrapped, string name, object? value)
    {
        PSPropertyInfo? existing = wrapped.Properties[name];
        if (existing is PSNoteProperty)
        {
            wrapped.Properties.Remove(name);
        }
        wrapped.Properties.Add(new PSNoteProperty(name, value));
    }
}
