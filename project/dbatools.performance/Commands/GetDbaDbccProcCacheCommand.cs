#nullable enable

using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Data;
using System.Management.Automation;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;
using Microsoft.SqlServer.Management.Smo;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Runs DBCC PROCCACHE and projects the single stats row. Port of
/// public/Get-DbaDbccProcCache.ps1 (W1-062). The begin-block StringBuilder collapses to the
/// constant query text; the verbose line and the Server.Query ETS hop sit inside the
/// function's own try (fault -> Stop-Function 'Failure' with the SERVER as target,
/// -Continue); the row loop is OUTSIDE the try and its six ordinal reads null-tolerate
/// out-of-range like PS (the W1-061 engine law). Surface pinned by
/// migration/baselines/Get-DbaDbccProcCache.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbccProcCache")]
public sealed class GetDbaDbccProcCacheCommand : DbaInstanceCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public override DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public override PSCredential? SqlCredential { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // PS begin block: StringBuilder.Append(...) - constant text.
    private const string Query = "DBCC PROCCACHE WITH NO_INFOMSGS";

    protected override void ProcessRecord()
    {
        foreach (DbaInstanceParameter instance in SqlInstance)
        {
            Hashtable connectParams = new Hashtable();
            connectParams["SqlInstance"] = instance;
            connectParams["SqlCredential"] = SqlCredential;
            NestedConnect.Outcome connection = NestedConnect.Connect(this, connectParams);
            if (!connection.Ok)
            {
                StopFunction("Failure", target: instance, errorRecord: connection.Failure, category: ErrorCategory.ConnectionError, continueLoop: true);
                continue;
            }
            Server server = connection.Server!;

            Collection<PSObject> results;
            try
            {
                WriteMessage(MessageLevel.Verbose, "Query to run: " + Query);
                results = NestedCommand.InvokeScoped(this, ServerQueryScript, server, Query);
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // PS: catch { Stop-Function -Message "Failure" -ErrorRecord $_ -Target $server -Continue }
                StopFunction("Failure", target: server, errorRecord: StatementFault.Record(ex, "Get-DbaDbccProcCache"), continueLoop: true);
                continue;
            }

            foreach (PSObject? item in results)
            {
                if (item?.BaseObject is not DataRow row)
                    continue;
                // PS: out-of-range ordinal reads are NULL, no fault (the W1-061 law).
                int columnCount = row.Table.Columns.Count;
                PSObject result = new PSObject();
                result.Properties.Add(new PSNoteProperty("ComputerName", SmoServerExtensions.GetComputerName(server)));
                result.Properties.Add(new PSNoteProperty("InstanceName", server.ServiceName));
                result.Properties.Add(new PSNoteProperty("SqlInstance", SmoServerExtensions.GetDomainInstanceName(server)));
                result.Properties.Add(new PSNoteProperty("Count", columnCount > 0 ? row[0] : null));
                result.Properties.Add(new PSNoteProperty("Used", columnCount > 1 ? row[1] : null));
                result.Properties.Add(new PSNoteProperty("Active", columnCount > 2 ? row[2] : null));
                result.Properties.Add(new PSNoteProperty("CacheSize", columnCount > 3 ? row[3] : null));
                result.Properties.Add(new PSNoteProperty("CacheUsed", columnCount > 4 ? row[4] : null));
                result.Properties.Add(new PSNoteProperty("CacheActive", columnCount > 5 ? row[5] : null));
                WriteObject(result);
            }
        }
    }

    // PS: $server.Query($query) on the engine (the W1-046 seam).
    private const string ServerQueryScript = """
param($server, $query)
$server.Query($query)
""";
}
