#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
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
/// Runs DBCC OPENTRAN per database. Port of public/Get-DbaDbDbccOpenTran.ps1 (W1-064).
/// Quirks preserved: the -Database filter matches Name OR ID against the STRING array with
/// PS -In semantics (an unconvertible element compares false, so a numeric string matches
/// by id); the per-db "Processing" verbose interpolates the SMO Database as "[name]"; an
/// inaccessible db warns through a record-less Stop-Function -Continue; the "Finshed"
/// verbose TYPO stays; an EMPTY DBCC result (the ETS Query pipeline collapses an empty
/// table to null) emits the single "No active open transactions." row while rows emit
/// "Oldest active transaction" with the two ordinal reads; the catch carries BOTH
/// -ErrorRecord and -Exception $_.Exception (the explicit exception wins). Surface pinned
/// by migration/baselines/Get-DbaDbDbccOpenTran.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbDbccOpenTran")]
public sealed class GetDbaDbDbccOpenTranCommand : DbaInstanceCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public override DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public override PSCredential? SqlCredential { get; set; }

    /// <summary>The databases to check (names or ids).</summary>
    [Parameter(Position = 2)]
    public string[]? Database { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // PS begin block: StringBuilder.Append(...) - constant template text.
    private const string Template = "DBCC OPENTRAN(#options#) WITH TABLERESULTS, NO_INFOMSGS";

    protected override void ProcessRecord()
    {
        foreach (DbaInstanceParameter instance in SqlInstance)
        {
            WriteMessage(MessageLevel.Verbose, "Attempting Connection to " + PsText(instance));
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

            // PS: $dbs = $server.Databases, optionally filtered Name-or-ID -In $Database.
            List<Microsoft.SqlServer.Management.Smo.Database> dbs = new List<Microsoft.SqlServer.Management.Smo.Database>();
            foreach (Microsoft.SqlServer.Management.Smo.Database candidate in server.Databases)
            {
                if (!TestBound("Database"))
                {
                    dbs.Add(candidate);
                    continue;
                }
                bool matched = false;
                foreach (string wanted in Database ?? new string[0])
                {
                    // PS -In: elementwise -eq with LHS-typed conversion; a failed
                    // conversion (id vs a name string) compares false, never faults.
                    if (PsOps.Eq(candidate.Name, wanted) || PsOps.Eq(candidate.ID, wanted))
                    {
                        matched = true;
                        break;
                    }
                }
                if (matched)
                    dbs.Add(candidate);
            }

            foreach (Microsoft.SqlServer.Management.Smo.Database db in dbs)
            {
                WriteMessage(MessageLevel.Verbose, "Processing " + PsText(db) + " on " + PsText(instance));

                if (!db.IsAccessible)
                {
                    StopFunction("The database " + PsText(db) + " is not accessible. Skipping.", continueLoop: true);
                    continue;
                }

                string query;
                Collection<PSObject> results;
                try
                {
                    query = Template;
                    query = query.Replace("#options#", "'" + db.Name + "'");

                    WriteMessage(MessageLevel.Verbose, "Query to run: " + query);
                    results = NestedCommand.InvokeScoped(this, ServerQueryScript, server, query);
                    WriteMessage(MessageLevel.Verbose, "Finshed");
                }
                catch (PipelineStoppedException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    ErrorRecord record = StatementFault.Record(ex, "Get-DbaDbDbccOpenTran");
                    StopFunction("Error capturing data on " + PsText(db), target: instance, errorRecord: record, exception: record.Exception, continueLoop: true);
                    continue;
                }

                // PS: $results = $server.Query(...) - an empty resultset comes back as a
                // REAL $null through the ETS scriptmethod (the W1-018 emission law); the
                // pipeline assignment collapses a lone $null to $null.
                if (results.Count == 0 || (results.Count == 1 && results[0] is null))
                {
                    PSObject result = new PSObject();
                    result.Properties.Add(new PSNoteProperty("ComputerName", SmoServerExtensions.GetComputerName(server)));
                    result.Properties.Add(new PSNoteProperty("InstanceName", server.ServiceName));
                    result.Properties.Add(new PSNoteProperty("SqlInstance", SmoServerExtensions.GetDomainInstanceName(server)));
                    result.Properties.Add(new PSNoteProperty("Database", db.Name));
                    result.Properties.Add(new PSNoteProperty("DatabaseId", db.ID));
                    result.Properties.Add(new PSNoteProperty("Cmd", query));
                    result.Properties.Add(new PSNoteProperty("Output", "No active open transactions."));
                    result.Properties.Add(new PSNoteProperty("Field", null));
                    result.Properties.Add(new PSNoteProperty("Data", null));
                    WriteObject(result);
                }
                else
                {
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
                        result.Properties.Add(new PSNoteProperty("Database", db.Name));
                        result.Properties.Add(new PSNoteProperty("DatabaseId", db.ID));
                        result.Properties.Add(new PSNoteProperty("Cmd", query));
                        result.Properties.Add(new PSNoteProperty("Output", "Oldest active transaction"));
                        result.Properties.Add(new PSNoteProperty("Field", columnCount > 0 ? row[0] : null));
                        result.Properties.Add(new PSNoteProperty("Data", columnCount > 1 ? row[1] : null));
                        WriteObject(result);
                    }
                }
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

    // PS: $server.Query($query) on the engine (the W1-046 seam).
    private const string ServerQueryScript = """
param($server, $query)
$server.Query($query)
""";
}
