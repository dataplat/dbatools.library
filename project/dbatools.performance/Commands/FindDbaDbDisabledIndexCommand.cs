#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;
using Microsoft.SqlServer.Management.Smo;
// the sibling namespace Dataplat.Dbatools.Database shadows Smo.Database inside
// Dataplat.Dbatools.Commands (the W1-020 namespace trap) - alias the SMO type
using SmoDatabase = Microsoft.SqlServer.Management.Smo.Database;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Finds disabled indexes across databases. Port of public/Find-DbaDbDisabledIndex.ps1
/// (W1-052). Connect rides NestedConnect (W1-046 seam); the per-database T-SQL runs on the
/// native SMO Database.ExecuteWithResults; output is the RAW DataRows (PS enumerates a
/// DataTable's rows in foreach). Quirks preserved: -NoClobber/-Append exist on the surface
/// but the body never reads them; the catch's Stop-Function has NO -Continue, so the db
/// loop simply proceeds (the interrupt flag is armed but the function never checks it);
/// -InnerErrorRecord is an alias of -ErrorRecord; the inner result-shape check
/// ($results.Count -gt 0 -or !IsNullOrEmpty) is tautological once Rows.Count passed and is
/// collapsed. Surface pinned by migration/baselines/Find-DbaDbDisabledIndex.json.
/// </summary>
[Cmdlet(VerbsCommon.Find, "DbaDbDisabledIndex", SupportsShouldProcess = true)]
public sealed class FindDbaDbDisabledIndexCommand : DbaInstanceCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public override DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public override PSCredential? SqlCredential { get; set; }

    /// <summary>The database(s) to process.</summary>
    [Parameter(Position = 2)]
    public object[]? Database { get; set; }

    /// <summary>The database(s) to exclude.</summary>
    [Parameter(Position = 3)]
    public object[]? ExcludeDatabase { get; set; }

    /// <summary>Surface-only in the function: the body never reads it.</summary>
    [Parameter]
    public SwitchParameter NoClobber { get; set; }

    /// <summary>Surface-only in the function: the body never reads it.</summary>
    [Parameter]
    public SwitchParameter Append { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // PS: the begin-block $sql literal VERBATIM.
    private const string DisabledIndexSql = @"
        SELECT DB_NAME() AS DatabaseName
        ,d.database_id AS DatabaseId
        ,s.name AS SchemaName
        ,t.name AS TableName
        ,i.object_id AS ObjectId
        ,i.name AS IndexName
        ,i.index_id AS IndexId
        ,i.type_desc AS TypeDesc
        FROM sys.tables t
        JOIN sys.schemas s
            ON t.schema_id = s.schema_id
        JOIN sys.indexes i
            ON i.object_id = t.object_id
        JOIN sys.databases d
            ON d.name = DB_NAME()
        WHERE i.is_disabled = 1";

    protected override void ProcessRecord()
    {
        foreach (DbaInstanceParameter instance in SqlInstance)
        {
            // PS: try { Connect-DbaInstance -MinimumVersion 9 } catch { Stop-Function
            //     -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue }
            Hashtable connectParams = new Hashtable();
            connectParams["SqlInstance"] = instance;
            connectParams["SqlCredential"] = SqlCredential;
            connectParams["MinimumVersion"] = 9;
            NestedConnect.Outcome connection = NestedConnect.Connect(this, connectParams);
            if (!connection.Ok)
            {
                StopFunction("Failure", target: instance, errorRecord: connection.Failure, category: ErrorCategory.ConnectionError, continueLoop: true);
                continue;
            }
            Server server = connection.Server!;

            // PS: $databases = $server.Databases | Where-Object Name -in $database   (bound)
            //     $databases = $server.Databases | Where-Object IsAccessible -eq $true
            List<SmoDatabase> databases = new List<SmoDatabase>();
            foreach (SmoDatabase candidate in server.Databases)
            {
                if (PsOps.IsTrue(Database))
                {
                    if (PsOps.In(candidate.Name, Database))
                        databases.Add(candidate);
                }
                else if (candidate.IsAccessible)
                {
                    databases.Add(candidate);
                }
            }

            if (databases.Count > 0)
            {
                // PS: foreach ($db in $databases.name) - member enumeration of the names.
                foreach (SmoDatabase databaseEntry in databases)
                {
                    string db = databaseEntry.Name;

                    // PS: if ($ExcludeDatabase -contains $db -or $null -eq $server.Databases[$db]) { continue }
                    if (PsOps.In(db, ExcludeDatabase) || server.Databases[db] is null)
                        continue;

                    try
                    {
                        if (ShouldProcess(db, "Getting disabled indexes"))
                        {
                            WriteMessage(MessageLevel.Verbose, "Getting indexes from database '" + db + "'.");
                            WriteMessage(MessageLevel.Debug, "SQL Statement: " + DisabledIndexSql);
                            DataSet disabledIndex = server.Databases[db].ExecuteWithResults(DisabledIndexSql);

                            if (disabledIndex.Tables[0].Rows.Count > 0)
                            {
                                // PS: foreach ($index in $results) { $index } - a DataTable
                                // enumerates its ROWS in the PS pipeline.
                                foreach (DataRow index in disabledIndex.Tables[0].Rows)
                                    WriteObject(index);
                            }
                            else
                            {
                                WriteMessage(MessageLevel.Verbose, "No Disabled indexes found");
                            }
                        }
                    }
                    catch (PipelineStoppedException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // PS: Stop-Function -Message "Issue gathering indexes" -Category
                        //     InvalidOperation -InnerErrorRecord $_ -Target $db (alias of
                        //     -ErrorRecord; NO -Continue: the loop proceeds unguarded).
                        StopFunction("Issue gathering indexes", target: db, errorRecord: ToCaughtRecord(ex), category: ErrorCategory.InvalidOperation, continueLoop: true);
                        continue;
                    }
                }
            }
            else
            {
                WriteMessage(MessageLevel.Verbose, "There are no databases to analyse.");
            }
        }
    }

    /// <summary>PS: catch { $_ } with the flattened-record rebuild (W1-009 class).</summary>
    private static ErrorRecord ToCaughtRecord(Exception ex)
    {
        ErrorRecord? inner = (ex as IContainsErrorRecord)?.ErrorRecord;
        if (inner is not null && inner.Exception is not ParentContainsErrorRecordException)
            return inner;
        if (inner is not null)
            return new ErrorRecord(ex, FirstErrorIdComponent(inner.FullyQualifiedErrorId), inner.CategoryInfo.Category, inner.TargetObject);
        return new ErrorRecord(ex, "Find-DbaDbDisabledIndex", ErrorCategory.NotSpecified, null);
    }

    private static string FirstErrorIdComponent(string? fullyQualifiedErrorId)
    {
        if (string.IsNullOrEmpty(fullyQualifiedErrorId))
            return "Find-DbaDbDisabledIndex";
        int comma = fullyQualifiedErrorId!.IndexOf(',');
        return comma < 0 ? fullyQualifiedErrorId : fullyQualifiedErrorId.Substring(0, comma);
    }
}
