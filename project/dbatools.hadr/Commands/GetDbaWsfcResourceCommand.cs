#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management.Automation;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;
using Dataplat.Dbatools.Utility;
using Microsoft.Management.Infrastructure;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves detailed information about cluster resources in a Windows Server Failover Cluster.
/// Port of public/Get-DbaWsfcResource.ps1; surface pinned by migration/baselines/Get-DbaWsfcResource.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaWsfcResource")]
[OutputType(typeof(CimInstance))]
public sealed class GetDbaWsfcResourceCommand : DbaBaseCmdlet
{
    /// <summary>The target cluster name or member node; defaults to the local computer.</summary>
    [Parameter(ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[]? ComputerName { get; set; } = DefaultComputerName();

    /// <summary>Allows you to login to the cluster using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? Credential { get; set; }

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        // PS: [DbaInstanceParameter[]]$ComputerName = $env:COMPUTERNAME - a machine without
        // the variable yields $null and the foreach body never runs.
        if (ComputerName is null)
        {
            return;
        }

        foreach (DbaInstanceParameter computer in ComputerName)
        {
            // PS: $cluster = Get-DbaWsfcCluster -ComputerName $computer -Credential $Credential
            // EnableException is NEVER forwarded to the inner cluster lookup, so a CIM failure
            // surfaces as a warning and the computer is skipped - even when the caller passed
            // -EnableException (pinned by the characterization tests).
            string? clusterName = null;
            string? clusterFqdn = null;
            try
            {
                CimService.CmObjectRequest clusterRequest = new()
                {
                    ComputerName = computer.ComputerName,
                    Credential = Credential,
                    ClassName = "MSCluster_Cluster",
                    Namespace = @"root\MSCluster"
                };
                CimService.CmObjectResult clusterResult = CimService.GetCmObject(clusterRequest);
                foreach (ErrorRecord passthrough in clusterResult.PassthroughErrors)
                {
                    WriteError(passthrough);
                }
                if (clusterResult.Instances.Count > 0)
                {
                    PSObject clusterObj = clusterResult.Instances[0];
                    clusterName = clusterObj.Properties["Name"]?.Value?.ToString();
                    clusterFqdn = clusterObj.Properties["Fqdn"]?.Value?.ToString();
                }
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                WriteMessage(MessageLevel.Warning, ex.Message, target: computer.ComputerName, exception: ex.InnerException ?? ex);
                continue;
            }

            // PS: $resources = Get-DbaCmObject -Computername $computer -Credential $Credential
            //     -Namespace root\MSCluster -ClassName MSCluster_Resource
            // EnableException is NEVER forwarded, so a CIM failure surfaces as the inner
            // command's own warning and the computer is skipped.
            List<PSObject> resources;
            try
            {
                CimService.CmObjectRequest resourceRequest = new()
                {
                    ComputerName = computer.ComputerName,
                    Credential = Credential,
                    ClassName = "MSCluster_Resource",
                    Namespace = @"root\MSCluster"
                };
                CimService.CmObjectResult resourceResult = CimService.GetCmObject(resourceRequest);
                foreach (ErrorRecord passthrough in resourceResult.PassthroughErrors)
                {
                    WriteError(passthrough);
                }
                resources = resourceResult.Instances;
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                WriteMessage(MessageLevel.Warning, ex.Message, target: computer.ComputerName, exception: ex.InnerException ?? ex);
                continue;
            }

            foreach (PSObject resource in resources)
            {
                // PS: $resource | Add-Member -Force -NotePropertyName State
                //     -NotePropertyValue (Get-ResourceState $resource.State)
                // Reads the native CIM uint16 State property and shadows it with a human-readable
                // string NoteProperty (e.g. 2 → "Online"). The characterization test pins that
                // State is a NoteProperty of TypeNameOfValue "System.String".
                object? rawStateObj = resource.Properties["State"]?.Value;
                int rawState = -1;
                if (rawStateObj != null)
                {
                    try { rawState = Convert.ToInt32(rawStateObj, CultureInfo.InvariantCulture); }
                    catch { rawState = -1; }
                }
                if (resource.Properties["State"] is PSNoteProperty)
                {
                    resource.Properties.Remove("State");
                }
                resource.Properties.Add(new PSNoteProperty("State", GetResourceState(rawState)));

                // PS: $resource | Add-Member -Force -NotePropertyName ClusterName -NotePropertyValue $cluster.Name
                if (resource.Properties["ClusterName"] is PSNoteProperty)
                {
                    resource.Properties.Remove("ClusterName");
                }
                resource.Properties.Add(new PSNoteProperty("ClusterName", clusterName));

                // PS: $resource | Add-Member -Force -NotePropertyName ClusterFqdn -NotePropertyValue $cluster.Fqdn
                if (resource.Properties["ClusterFqdn"] is PSNoteProperty)
                {
                    resource.Properties.Remove("ClusterFqdn");
                }
                resource.Properties.Add(new PSNoteProperty("ClusterFqdn", clusterFqdn));

                // PS: $resource | Select-DefaultView -Property ClusterName, ClusterFqdn, Name, State,
                //     Type, OwnerGroup, OwnerNode, PendingTimeout, PersistentState, QuorumCapable,
                //     RequiredDependencyClasses, RequiredDependencyTypes, RestartAction, RestartDelay,
                //     RestartPeriod, RestartThreshold, RetryPeriodOnFailure, SeparateMonitor
                OutputHelper.SetDefaultDisplayPropertySet(resource,
                    "ClusterName", "ClusterFqdn", "Name", "State", "Type", "OwnerGroup", "OwnerNode",
                    "PendingTimeout", "PersistentState", "QuorumCapable",
                    "RequiredDependencyClasses", "RequiredDependencyTypes",
                    "RestartAction", "RestartDelay", "RestartPeriod", "RestartThreshold",
                    "RetryPeriodOnFailure", "SeparateMonitor");
                WriteObject(resource);
            }
        }
    }

    /// <summary>Converts MSCluster_Resource.State integer to a human-readable string.</summary>
    /// <remarks>Faithfully ports private/functions/Get-ResourceState.ps1 switch table.</remarks>
    private static string GetResourceState(int state) => state switch
    {
        -1  => "Unknown",
        0   => "Inherited",
        1   => "Initializing",
        2   => "Online",
        3   => "Offline",
        4   => "Failed",
        128 => "Pending",
        129 => "Online Pending",
        130 => "Offline Pending",
        _   => state.ToString(CultureInfo.InvariantCulture)
    };

    private static DbaInstanceParameter[]? DefaultComputerName()
    {
        string? machine = Environment.GetEnvironmentVariable("COMPUTERNAME");
        if (string.IsNullOrEmpty(machine))
        {
            return null;
        }
        return new[] { new DbaInstanceParameter(machine) };
    }
}
