#nullable enable

using System;
using System.Management.Automation;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;
using Dataplat.Dbatools.Utility;
using Microsoft.SqlServer.Management.Smo;
using Microsoft.SqlServer.Replication;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves replication configuration and server role information from SQL Server instances (W4-103).
/// Port of public/Get-DbaReplServer.ps1. Native C# reimplementation (not a scriptblock hop): the source
/// process block Connect-DbaInstance's the instance, constructs a Microsoft.SqlServer.Replication.ReplicationServer
/// over the same ConnectionContext, decorates it with the three identity NoteProperties, and Select-DefaultView's
/// the ReplicationServer role/distribution properties. The source's begin-block Add-ReplicationLibrary is subsumed
/// by the compile-time reference to Microsoft.SqlServer.Replication here (the RMO types are already loaded); the
/// sibling GetDbaReplServer inline in GetDbaReplDistributorCommand.cs (W4-100) confirms this shape. The process
/// emits directly via WriteObject and its ConnectionError Stop-Function is a per-instance loop-continue, so no
/// InvokeScopedStreaming buffering is involved (DEF-001 concerns scriptblock hops, not native ports). Surface
/// pinned by migration/baselines/Get-DbaReplServer.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaReplServer")]
[OutputType(typeof(ReplicationServer))]
public sealed class GetDbaReplServerCommand : DbaInstanceCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public override DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public override PSCredential? SqlCredential { get; set; }

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        foreach (DbaInstanceParameter instance in SqlInstance)
        {
            try
            {
                // PS: $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
                SmoConnectionRequest request = new();
                request.Instance = instance;
                request.SqlCredential = SqlCredential;
                Server server = ConnectionService.GetServer(request);
                SetActiveConnection(server.ConnectionContext);

                // PS: $replServer = New-Object Microsoft.SqlServer.Replication.ReplicationServer
                //     $replServer.ConnectionContext = $Server.ConnectionContext
                ReplicationServer replicationServer = new();
                replicationServer.ConnectionContext = server.ConnectionContext;

                // PS: Add-Member -Force ComputerName / InstanceName / SqlInstance
                PSObject wrapped = PSObject.AsPSObject(replicationServer);
                ReplaceNoteProperty(wrapped, "ComputerName", SmoServerExtensions.GetComputerName(server));
                ReplaceNoteProperty(wrapped, "InstanceName", server.ServiceName);
                ReplaceNoteProperty(wrapped, "SqlInstance", SmoServerExtensions.GetDomainInstanceName(server));

                // PS: Select-DefaultView -Property ComputerName, InstanceName, SqlInstance, IsDistributor, IsPublisher, DistributionServer, DistributionDatabase
                OutputHelper.SetDefaultDisplayPropertySet(wrapped, "ComputerName", "InstanceName", "SqlInstance", "IsDistributor", "IsPublisher", "DistributionServer", "DistributionDatabase");
                WriteObject(wrapped);
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // PS: Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue
                StopFunction("Failure", target: instance, errorRecord: new ErrorRecord(ex, "dbatools_Get-DbaReplServer", ErrorCategory.ConnectionError, instance), category: ErrorCategory.ConnectionError, continueLoop: true);
                continue;
            }
        }
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
