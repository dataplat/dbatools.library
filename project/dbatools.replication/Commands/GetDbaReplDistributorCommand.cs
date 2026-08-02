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
/// Retrieves replication distributor configuration and status from SQL Server instances.
/// Port of public/Get-DbaReplDistributor.ps1 with public/Get-DbaReplServer.ps1 inlined
/// (its only role here is constructing the decorated ReplicationServer);
/// surface pinned by migration/baselines/Get-DbaReplDistributor.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaReplDistributor")]
[OutputType(typeof(ReplicationServer))]
public sealed class GetDbaReplDistributorCommand : DbaInstanceCmdlet
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
            WriteMessage(MessageLevel.Verbose, $"Attempting to retrieve distributor information from {instance}");

            // PS: $distributor = Get-DbaReplServer ... -EnableException:$EnableException
            // inlined: Connect-DbaInstance + ReplicationServer over the same connection
            // context + the three identity NoteProperties.
            PSObject wrapped;
            try
            {
                SmoConnectionRequest request = new();
                request.Instance = instance;
                request.SqlCredential = SqlCredential;
                Server server = ConnectionService.GetServer(request);
                SetActiveConnection(server.ConnectionContext);

                ReplicationServer replicationServer = new();
                replicationServer.ConnectionContext = server.ConnectionContext;

                wrapped = PSObject.AsPSObject(replicationServer);
                ReplaceNoteProperty(wrapped, "ComputerName", SmoServerExtensions.GetComputerName(server));
                ReplaceNoteProperty(wrapped, "InstanceName", server.ServiceName);
                ReplaceNoteProperty(wrapped, "SqlInstance", SmoServerExtensions.GetDomainInstanceName(server));
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Failure-shape parity: in default mode the inlined Get-DbaReplServer's own
                // Stop-Function surfaces first ("Failure" warning, no output, next instance);
                // under EnableException that throw is re-wrapped by THIS command's catch, so
                // the terminating message is the ConnectionError wrap.
                string message = EnableException.ToBool()
                    ? $"Error occurred getting information about {instance}"
                    : "Failure";
                StopFunction(message, target: instance, errorRecord: new ErrorRecord(ex, "dbatools_Get-DbaReplDistributor", ErrorCategory.ConnectionError, instance), category: ErrorCategory.ConnectionError, continueLoop: true);
                // PS default mode still reaches the verbose line below after the inlined
                // failure (Select-DefaultView then emits nothing for the null distributor);
                // keep the message-stream parity (cross-model review 2026-07-06 finding 5).
                WriteMessage(MessageLevel.Verbose, "Getting publisher for ");
                continue;
            }

            // PS: Write-Message "Getting publisher for $server" - $server is not defined in
            // the outer function's scope, so the rendered message is verbatim empty-suffixed.
            WriteMessage(MessageLevel.Verbose, "Getting publisher for ");

            // The outer Select-DefaultView wins over the inlined function's narrower set.
            OutputHelper.SetDefaultDisplayPropertySet(wrapped, "ComputerName", "InstanceName", "SqlInstance", "IsPublisher", "IsDistributor", "DistributionServer", "DistributionDatabase", "DistributorInstalled", "DistributorAvailable", "HasRemotePublisher");
            WriteObject(wrapped);
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
