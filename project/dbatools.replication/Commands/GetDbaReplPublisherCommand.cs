#nullable enable

using System;
using System.Collections.Generic;
using System.Management.Automation;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;
using Dataplat.Dbatools.Utility;
using Microsoft.SqlServer.Management.Smo;
using Microsoft.SqlServer.Replication;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves replication publisher information from distribution server instances.
/// Port of public/Get-DbaReplPublisher.ps1 with public/Get-DbaReplServer.ps1 inlined;
/// surface pinned by migration/baselines/Get-DbaReplPublisher.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaReplPublisher")]
[OutputType(typeof(DistributionPublisher))]
public sealed class GetDbaReplPublisherCommand : DbaInstanceCmdlet
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
            // PS: $server = Connect-DbaInstance; $replServer = Get-DbaReplServer $server
            // (inlined: ReplicationServer over the same connection context).
            Server server;
            ReplicationServer replicationServer;
            try
            {
                SmoConnectionRequest request = new();
                request.Instance = instance;
                request.SqlCredential = SqlCredential;
                server = ConnectionService.GetServer(request);
                SetActiveConnection(server.ConnectionContext);

                replicationServer = new ReplicationServer();
                replicationServer.ConnectionContext = server.ConnectionContext;
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // PS: Stop-Function -Message "Failure" -Category ConnectionError, in BOTH
                // modes - unlike the Distributor port, this command's own catch wraps the
                // inlined Get-DbaReplServer failure before EnableException ever rethrows
                // (cross-model review 2026-07-06 pm2 finding 9). ACCEPTED DEVIATION: the
                // record targets the instance, not the PS $server variable that is null or
                // stale from a previous iteration at this point.
                StopFunction("Failure", target: instance, errorRecord: new ErrorRecord(ex, "dbatools_Get-DbaReplPublisher", ErrorCategory.ConnectionError, instance), category: ErrorCategory.ConnectionError, continueLoop: true);
                continue;
            }

            // PS: Write-Message -Level Verbose "Getting publisher for $server" - the SMO
            // server interpolates to its bracketed name.
            WriteMessage(MessageLevel.Verbose, $"Getting publisher for {server}");

            List<DistributionPublisher> publishers = new();
            try
            {
                foreach (DistributionPublisher publisher in replicationServer.DistributionPublishers)
                {
                    publishers.Add(publisher);
                }
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // message truncated mid-sentence in the PS source; preserved verbatim
                StopFunction("Unable to get publisher for", target: server, errorRecord: new ErrorRecord(ex, "dbatools_Get-DbaReplPublisher", ErrorCategory.NotSpecified, server), continueLoop: true);
                continue;
            }

            foreach (DistributionPublisher publisher in publishers)
            {
                PSObject wrapped = PSObject.AsPSObject(publisher);
                ReplaceNoteProperty(wrapped, "ComputerName", SmoServerExtensions.GetComputerName(server));
                ReplaceNoteProperty(wrapped, "InstanceName", server.ServiceName);
                ReplaceNoteProperty(wrapped, "SqlInstance", SmoServerExtensions.GetDomainInstanceName(server));
                OutputHelper.SetDefaultDisplayPropertySet(wrapped,
                    "ComputerName", "InstanceName", "SqlInstance", "Status", "WorkingDirectory",
                    "DistributionDatabase", "DistributionPublications", "PublisherType", "Name");
                WriteObject(wrapped);
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
