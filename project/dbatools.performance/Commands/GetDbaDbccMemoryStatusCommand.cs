#nullable enable

using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.Management.Automation;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;
using Microsoft.SqlServer.Management.Smo;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Runs DBCC MEMORYSTATUS and parses every recordset into flat metric objects. Port of
/// public/Get-DbaDbccMemoryStatus.ps1 (W1-061). The all-tables $server.query($query,
/// "master", $true) call rides the Server ETS hop VERBATIM (lowercase .query kept); the
/// parse loop runs natively INSIDE the function's own try - any fault there (the query
/// included) lands in the catch's Stop-Function -Continue, so no statement-conditional
/// machinery applies. Counters preserved exactly: RecordSet increments per table, RowId is
/// CUMULATIVE across tables, RecordSetId resets to 0 after each table; Name/Value are the
/// raw ordinal DataRow reads. Surface pinned by migration/baselines/Get-DbaDbccMemoryStatus.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbccMemoryStatus")]
public sealed class GetDbaDbccMemoryStatusCommand : DbaInstanceCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public override DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public override PSCredential? SqlCredential { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // PS begin block: $query = 'DBCC MEMORYSTATUS'
    private const string Query = "DBCC MEMORYSTATUS";

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

            WriteMessage(MessageLevel.Verbose, "Collecting " + Query + " data from server: " + PsText(instance));
            try
            {
                Collection<PSObject> datatable = NestedCommand.InvokeScoped(this, QueryAllTablesScript, server, Query);

                int recordset = 0;
                int rowId = 0;
                int recordsetId = 0;

                foreach (PSObject? item in datatable)
                {
                    if (item?.BaseObject is not DataTable dataset)
                        continue;
                    string dataSection = dataset.Columns[0].ColumnName;
                    string dataType = dataset.Columns[1].ColumnName;
                    recordset = recordset + 1;
                    foreach (DataRow row in dataset.Rows)
                    {
                        rowId = rowId + 1;
                        recordsetId = recordsetId + 1;
                        PSObject result = new PSObject();
                        result.Properties.Add(new PSNoteProperty("ComputerName", SmoServerExtensions.GetComputerName(server)));
                        result.Properties.Add(new PSNoteProperty("InstanceName", server.ServiceName));
                        result.Properties.Add(new PSNoteProperty("SqlInstance", SmoServerExtensions.GetDomainInstanceName(server)));
                        result.Properties.Add(new PSNoteProperty("RecordSet", recordset));
                        result.Properties.Add(new PSNoteProperty("RowId", rowId));
                        result.Properties.Add(new PSNoteProperty("RecordSetId", recordsetId));
                        result.Properties.Add(new PSNoteProperty("Type", dataSection));
                        result.Properties.Add(new PSNoteProperty("Name", row[0]));
                        result.Properties.Add(new PSNoteProperty("Value", row[1]));
                        result.Properties.Add(new PSNoteProperty("ValueType", dataType));
                        WriteObject(result);
                    }
                    recordsetId = 0;
                }
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // PS: the function's own try encloses the query AND the parse loop; ANY
                // fault lands in its catch's Stop-Function -Continue.
                StopFunction("Failure Executing " + Query, target: instance, errorRecord: StatementFault.Record(ex, "Get-DbaDbccMemoryStatus"), continueLoop: true);
                continue;
            }
        }
    }

    /// <summary>PS string interpolation via LanguagePrimitives (invariant).</summary>
    private static string PsText(object? value)
    {
        if (value is null)
            return "";
        return (string)LanguagePrimitives.ConvertTo(value, typeof(string), CultureInfo.InvariantCulture);
    }

    // PS: $server.query($query, 'master', $true) - the all-tables ETS call, verbatim.
    private const string QueryAllTablesScript = """
param($server, $query)
$server.query($query, "master", $true)
""";
}
