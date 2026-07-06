#nullable enable

using System;
using System.Collections.Generic;
using System.Management.Automation;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;
using Dataplat.Dbatools.Utility;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves detailed information about clustered physical disks from Windows Server Failover Clusters.
/// Port of public/Get-DbaWsfcDisk.ps1; surface pinned by migration/baselines/Get-DbaWsfcDisk.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaWsfcDisk")]
[OutputType(typeof(PSObject))]
public sealed class GetDbaWsfcDiskCommand : DbaBaseCmdlet
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
            // surfaces as a warning and the computer is skipped.
            string? clusterName = null;
            string? clusterFqdn = null;
            try
            {
                CimService.CmObjectRequest clusterRequest = new CimService.CmObjectRequest()
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

            // PS: $resources = Get-DbaWsfcResource -ComputerName $computer -Credential $Credential
            //     | Where-Object Type -eq 'Physical Disk'
            // Get all MSCluster_Resource instances; filter by Type == 'Physical Disk'.
            List<PSObject> resources;
            try
            {
                CimService.CmObjectRequest resourceRequest = new CimService.CmObjectRequest()
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
                // PS: | Where-Object Type -eq 'Physical Disk'
                string? resourceType = resource.Properties["Type"]?.Value?.ToString();
                if (!string.Equals(resourceType, "Physical Disk", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string? resourceName = resource.Properties["Name"]?.Value?.ToString();
                string? ownerGroup = resource.Properties["OwnerGroup"]?.Value?.ToString();

                // PS: $resource | Add-Member -Force -NotePropertyName State -NotePropertyValue (Get-ResourceState $resource.State)
                // Get-ResourceState converts the numeric State value to a human-readable string.
                object? rawState = resource.Properties["State"]?.Value;
                string? stateName = GetResourceState(rawState);

                // PS: $disks = $resource | Get-CimAssociatedInstance -ResultClassName MSCluster_Disk
                List<PSObject> disks;
                try
                {
                    CimService.CmAssociationRequest diskAssocReq = new CimService.CmAssociationRequest()
                    {
                        ComputerName = computer.ComputerName,
                        Credential = Credential,
                        SourceObject = resource,
                        ResultClassName = "MSCluster_Disk",
                        Namespace = @"root\MSCluster"
                    };
                    CimService.CmObjectResult diskResult = CimService.GetAssociatedCmObjects(diskAssocReq);
                    foreach (ErrorRecord passthrough in diskResult.PassthroughErrors)
                    {
                        WriteError(passthrough);
                    }
                    disks = diskResult.Instances;
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

                foreach (PSObject disk in disks)
                {
                    // PS: $diskpart = $disk | Get-CimAssociatedInstance -ResultClassName MSCluster_DiskPartition
                    List<PSObject> partitions;
                    try
                    {
                        CimService.CmAssociationRequest partAssocReq = new CimService.CmAssociationRequest()
                        {
                            ComputerName = computer.ComputerName,
                            Credential = Credential,
                            SourceObject = disk,
                            ResultClassName = "MSCluster_DiskPartition",
                            Namespace = @"root\MSCluster"
                        };
                        CimService.CmObjectResult partResult = CimService.GetAssociatedCmObjects(partAssocReq);
                        foreach (ErrorRecord passthrough in partResult.PassthroughErrors)
                        {
                            WriteError(passthrough);
                        }
                        partitions = partResult.Instances;
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

                    foreach (PSObject diskpart in partitions)
                    {
                        // PS: [dbasize]($diskpart.TotalSize * 1MB)  — TotalSize is in MB, 1MB=1048576 bytes
                        object? rawTotalSize = diskpart.Properties["TotalSize"]?.Value;
                        object? rawFreeSpace = diskpart.Properties["FreeSpace"]?.Value;
                        long totalSizeMb = rawTotalSize != null ? Convert.ToInt64(rawTotalSize) : 0L;
                        long freeSpaceMb = rawFreeSpace != null ? Convert.ToInt64(rawFreeSpace) : 0L;
                        Size sizeObj = new Size(totalSizeMb * 1048576L);
                        Size freeObj = new Size(freeSpaceMb * 1048576L);

                        // PS: [PSCustomObject]@{ ... } | Select-DefaultView -ExcludeProperty ClusterDisk, ClusterDiskPart, ClusterResource
                        // new PSObject() TypeNames[0] = PSObject; insert PSCustomObject at 0 to match [PSCustomObject]@{} parity.
                        PSObject output = new PSObject();
                        output.TypeNames.Insert(0, "System.Management.Automation.PSCustomObject");
                        output.Properties.Add(new PSNoteProperty("ClusterName", clusterName));
                        output.Properties.Add(new PSNoteProperty("ClusterFqdn", clusterFqdn));
                        output.Properties.Add(new PSNoteProperty("ResourceGroup", ownerGroup));
                        output.Properties.Add(new PSNoteProperty("Disk", resourceName));
                        output.Properties.Add(new PSNoteProperty("State", stateName));
                        output.Properties.Add(new PSNoteProperty("FileSystem", diskpart.Properties["FileSystem"]?.Value));
                        output.Properties.Add(new PSNoteProperty("Path", diskpart.Properties["Path"]?.Value));
                        output.Properties.Add(new PSNoteProperty("Label", diskpart.Properties["VolumeLabel"]?.Value));
                        output.Properties.Add(new PSNoteProperty("Size", sizeObj));
                        output.Properties.Add(new PSNoteProperty("Free", freeObj));
                        output.Properties.Add(new PSNoteProperty("MountPoints", diskpart.Properties["MountPoints"]?.Value));
                        output.Properties.Add(new PSNoteProperty("SerialNumber", diskpart.Properties["SerialNumber"]?.Value));
                        output.Properties.Add(new PSNoteProperty("ClusterDisk", disk));
                        output.Properties.Add(new PSNoteProperty("ClusterDiskPart", diskpart));
                        output.Properties.Add(new PSNoteProperty("ClusterResource", resource));

                        // Select-DefaultView -ExcludeProperty ClusterDisk, ClusterDiskPart, ClusterResource
                        OutputHelper.SetDefaultDisplayPropertySetExcluding(output, new string[] { "ClusterDisk", "ClusterDiskPart", "ClusterResource" });

                        WriteObject(output);
                    }
                }
            }
        }
    }

    // Port of private/functions/Get-ResourceState.ps1: converts the numeric MSCluster_Resource
    // State property to the human-readable string the PS implementation attaches via Add-Member.
    private static string? GetResourceState(object? state)
    {
        if (state is null)
        {
            return null;
        }

        int stateValue;
        try
        {
            stateValue = Convert.ToInt32(state);
        }
        catch
        {
            return null;
        }

        switch (stateValue)
        {
            case -1: return "Unknown";
            case 0:  return "Inherited";
            case 1:  return "Initializing";
            case 2:  return "Online";
            case 3:  return "Offline";
            case 4:  return "Failed";
            case 128: return "Pending";
            case 129: return "Online Pending";
            case 130: return "Offline Pending";
            default: return null;
        }
    }

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
