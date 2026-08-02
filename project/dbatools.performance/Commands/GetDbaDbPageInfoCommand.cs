#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Management.Automation;
using System.Text;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;
using Microsoft.SqlServer.Management.Smo;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Reports page allocation details per table. Port of public/Get-DbaDbPageInfo.ps1
/// (W1-069). The begin block assembles the SQL once: the VALUE-truthy -Schema filter
/// joins names as N'a','b' (only the FIRST gets the N prefix - quirk kept), the -Table
/// filter parses each name through the PRIVATE Get-ObjectNameParts hop and builds the
/// OR-joined clause ( [string]$null casts to "" ); each -SqlInstance appends its
/// (optionally Name -in filtered) Databases to InputObject; the db loop revalidates
/// Parent.VersionMajor INSIDE the try (a null db null-walks to the "Unsupported"
/// branch), emits the RAW Database.Query rows, and the catch targets $instance - the
/// LAST loop value, or the module-scope dynamic read when pipeline-only input never
/// defined it (W1-051 law). Surface pinned by migration/baselines/Get-DbaDbPageInfo.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbPageInfo")]
public sealed class GetDbaDbPageInfoCommand : DbaInstanceCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Position = 0)]
    public override DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public override PSCredential? SqlCredential { get; set; }

    /// <summary>The databases to scan.</summary>
    [Parameter(Position = 2)]
    public string[]? Database { get; set; }

    /// <summary>Schema filter for the page query.</summary>
    [Parameter(Position = 3)]
    public string[]? Schema { get; set; }

    /// <summary>Table filter for the page query (1-3 part names).</summary>
    [Parameter(Position = 4)]
    public string[]? Table { get; set; }

    /// <summary>Database objects piped in, typically from Get-DbaDatabase.</summary>
    [Parameter(ValueFromPipeline = true, Position = 5)]
    public Microsoft.SqlServer.Management.Smo.Database[]? InputObject { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    private const string BaseSql = @"SELECT SERVERPROPERTY('MachineName') AS ComputerName,
        ISNULL(SERVERPROPERTY('InstanceName'), 'MSSQLSERVER') AS InstanceName,
        SERVERPROPERTY('ServerName') AS SqlInstance, [Database] = DB_NAME(DB_ID()),
        ss.name AS [Schema], st.name AS [Table], dbpa.page_type_desc AS PageType,
                        dbpa.page_free_space_percent AS PageFreePercent,
                        IsAllocated =
                          CASE dbpa.is_allocated
                             WHEN 0 THEN 'False'
                             WHEN 1 THEN 'True'
                          END,
                        IsMixedPage =
                          CASE dbpa.is_mixed_page_allocation
                             WHEN 0 THEN 'False'
                             WHEN 1 THEN 'True'
                          END
                        FROM sys.dm_db_database_page_allocations(DB_ID(), NULL, NULL, NULL, 'DETAILED') AS dbpa
                        INNER JOIN sys.tables AS st ON st.object_id = dbpa.object_id
                        INNER JOIN sys.schemas AS ss ON ss.schema_id = st.schema_id";

    private string _sql = "";

    // PS: $instance persists after the instance loop; a pipeline-only invocation never
    // defines it and the catch's -Target reads through the module scope (W1-051 law).
    private object? _lastInstance;
    private bool _instanceSeen;

    protected override void BeginProcessing()
    {
        _sql = BaseSql;

        // PS: if ($Schema) - VALUE truthiness (W1-065 law).
        if (PsTruthy(Schema))
        {
            List<string> schemaNames = new List<string>();
            foreach (string? name in Schema!)
            {
                if (name is null)
                    schemaNames.Add("");
                else
                    schemaNames.Add(name.Replace("'", "''"));
            }
            _sql = _sql + " WHERE ss.name IN (N'" + string.Join("','", schemaNames) + "')";
        }

        if (PsTruthy(Table))
        {
            List<string> tableWhereClauses = new List<string>();
            foreach (string? tableName in Table!)
            {
                object? tablePart = PipelineValue(NestedCommand.InvokeScoped(this, GetObjectNamePartsScript, tableName));
                string namePart = PsString(DotAccess(tablePart, "Name")).Replace("'", "''");
                List<string> clauseParts = new List<string>();
                clauseParts.Add("st.name = N'" + namePart + "'");

                object? schemaPart = DotAccess(tablePart, "Schema");
                if (LanguagePrimitives.IsTrue(schemaPart))
                    clauseParts.Add("ss.name = N'" + PsString(schemaPart).Replace("'", "''") + "'");

                object? databasePart = DotAccess(tablePart, "Database");
                if (LanguagePrimitives.IsTrue(databasePart))
                    clauseParts.Add("DB_NAME() = N'" + PsString(databasePart).Replace("'", "''") + "'");

                tableWhereClauses.Add("(" + string.Join(" AND ", clauseParts) + ")");
            }

            string tableWhereClause = string.Join(" OR ", tableWhereClauses);
            if (PsTruthy(Schema))
                _sql = _sql + " AND (" + tableWhereClause + ")";
            else
                _sql = _sql + " WHERE " + tableWhereClause;
        }
    }

    protected override void ProcessRecord()
    {
        // PS: $InputObject += ... per instance.
        List<object?> inputObjects = new List<object?>();
        if (InputObject is not null)
        {
            foreach (Microsoft.SqlServer.Management.Smo.Database db in InputObject)
                inputObjects.Add(db);
        }

        foreach (DbaInstanceParameter instance in SqlInstance ?? new DbaInstanceParameter[0])
        {
            _lastInstance = instance;
            _instanceSeen = true;
            Hashtable connectParams = new Hashtable();
            connectParams["SqlInstance"] = instance;
            connectParams["SqlCredential"] = SqlCredential;
            connectParams["MinimumVersion"] = 11;
            NestedConnect.Outcome connection = NestedConnect.Connect(this, connectParams);
            if (!connection.Ok)
            {
                StopFunction("Failure", target: instance, errorRecord: connection.Failure, category: ErrorCategory.ConnectionError, continueLoop: true);
                continue;
            }
            // PS reads are ETS-FIRST: $server.Databases and the per-db properties
            // resolve instance/note members before native ones (the mock-driven
            // characterization test decorates real objects this way).
            object? serverValue = connection.ServerValue;

            bool filterInclude = PsTruthy(Database);
            foreach (object? candidate in EnumerateValue(DotAccess(serverValue, "Databases")))
            {
                // PS: null elements survive the append (and later null-walk to the
                // "Unsupported" branch); the Name filter drops them when bound.
                if (filterInclude && !MatchesAny(DotAccess(candidate, "Name"), Database!))
                    continue;
                inputObjects.Add(candidate);
            }
        }

        foreach (object? item in inputObjects)
        {
            try
            {
                // PS: $db.Parent.VersionMajor reads ETS-first; a null db null-walks to
                // null, and null -ge 11 is FALSE - the "Unsupported" branch. A
                // non-comparable value faults into the catch like the PS -ge.
                object? versionMajor = DotAccess(DotAccess(item, "Parent"), "VersionMajor");
                bool supported = versionMajor is not null && LanguagePrimitives.Compare(versionMajor, 11, false, CultureInfo.InvariantCulture) >= 0;
                if (supported)
                {
                    foreach (PSObject? row in NestedCommand.InvokeScoped(this, DatabaseQueryScript, item!, _sql))
                        WriteObject(row);
                }
                else
                {
                    StopFunction("Unsupported SQL Server version", target: item, continueLoop: true);
                    continue;
                }
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                object? instanceTarget = _instanceSeen ? _lastInstance : PipelineValue(NestedCommand.InvokeScoped(this, ModuleVariableScript, "instance"));
                StopFunction("Something went wrong executing the query", target: instanceTarget, errorRecord: StatementFault.Record(ex, "Get-DbaDbPageInfo"), continueLoop: true);
                continue;
            }
        }
    }

    /// <summary>PS array truthiness: empty = false, one element = its truthiness,
    /// two or more = true.</summary>
    private static bool PsTruthy(string[]? values)
    {
        if (values is null || values.Length == 0)
            return false;
        if (values.Length == 1)
            return LanguagePrimitives.IsTrue(values[0]);
        return true;
    }

    /// <summary>PS -in over the filter array (elementwise -eq).</summary>
    private static bool MatchesAny(object? name, string[] values)
    {
        foreach (string value in values)
        {
            if (PsOps.Eq(name, value))
                return true;
        }
        return false;
    }

    /// <summary>Enumerates a PS value: null yields nothing, a collection yields
    /// elements, a scalar yields itself.</summary>
    private static IEnumerable<object?> EnumerateValue(object? value)
    {
        if (value is null)
            yield break;
        object? unwrapped = value is PSObject pso ? pso.BaseObject : value;
        if (unwrapped is not string && LanguagePrimitives.GetEnumerable(unwrapped) is IEnumerable elements)
        {
            foreach (object? element in elements)
                yield return element;
            yield break;
        }
        yield return value;
    }

    /// <summary>PS pipeline-assignment collapse: none = null, one = the item, many = array.</summary>
    private static object? PipelineValue(Collection<PSObject> results)
    {
        if (results.Count == 0)
            return null;
        if (results.Count == 1)
            return results[0];
        object?[] array = new object?[results.Count];
        for (int n = 0; n < results.Count; n++)
            array[n] = results[n];
        return array;
    }

    /// <summary>PS [string] cast: null becomes "".</summary>
    private static string PsString(object? value)
    {
        if (value is null)
            return "";
        return (string)LanguagePrimitives.ConvertTo(value, typeof(string), CultureInfo.InvariantCulture);
    }

    /// <summary>The PS dot operator (single-object property reads).</summary>
    private static object? DotAccess(object? item, string name)
    {
        if (item is null)
            return null;
        PSObject wrapped = PSObject.AsPSObject(item);
        PSPropertyInfo? direct = wrapped.Properties[name];
        if (direct is null)
            return null;
        object? value;
        try { value = direct.Value; }
        catch { return null; }
        if (value is PSObject psValue && psValue.BaseObject is not PSCustomObject)
            return psValue.BaseObject;
        return value;
    }

    // PS: $db.Query($sql) - the Database-scoped ETS call (the W1-052 seam).
    private const string DatabaseQueryScript = """
param($db, $query)
$db.Query($query)
""";

    // PS: the PRIVATE Get-ObjectNameParts helper rides the module hop.
    private const string GetObjectNamePartsScript = """
param($__name)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($__name)
    Get-ObjectNameParts -ObjectName $__name
} $__name 3>&1
""";

    // PS: the undefined fn-variable read resolves module -> global (the W1-051 law).
    private const string ModuleVariableScript = """
param($__name)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($__name)
    $ExecutionContext.SessionState.PSVariable.GetValue($__name)
} $__name 3>&1
""";
}
