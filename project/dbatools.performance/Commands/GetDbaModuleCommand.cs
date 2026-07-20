#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Lists SQL modules (views, procedures, functions, triggers...). Port of
/// public/Get-DbaModule.ps1 (W1-083). The begin block maps -Type names to type_desc
/// literals and builds the SQL once (the -ModifiedSince datetime INTERPOLATES invariant;
/// -ExcludeSystemObjects appends its clause after a NEWLINE); the process block keeps the
/// quirks: the no-input record-less Stop-Function gate, the "$SqlInstance" array
/// interpolation in the verbose, the $PSBoundParameters-driven nested Get-DbaDatabase
/// call (unbound keys read null; ExcludeSystem binds the colon-switch), and the
/// cross-record $InputObject accumulation (the W1-070 reference-reset law); each
/// database reads ETS-first, warns-and-skips when inaccessible, and the db-scoped Query
/// rows project through Select-DefaultView -ExcludeProperty Definition. Surface pinned
/// by migration/baselines/Get-DbaModule.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaModule")]
public sealed class GetDbaModuleCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The databases to include.</summary>
    [Parameter(Position = 2)]
    public object[]? Database { get; set; }

    /// <summary>The databases to skip.</summary>
    [Parameter(Position = 3)]
    public object[]? ExcludeDatabase { get; set; }

    /// <summary>Only modules modified at or after this time.</summary>
    [Parameter(Position = 4)]
    [PsDateTimeCast]
    public DateTime ModifiedSince { get; set; } = (DateTime)LanguagePrimitives.ConvertTo("1900-01-01", typeof(DateTime), CultureInfo.InvariantCulture);

    /// <summary>The module types to include.</summary>
    [Parameter(Position = 5)]
    [ValidateSet("View", "TableValuedFunction", "DefaultConstraint", "StoredProcedure", "Rule", "InlineTableValuedFunction", "Trigger", "ScalarFunction")]
    public string[]? Type { get; set; }

    /// <summary>Skip system databases in the nested Get-DbaDatabase call.</summary>
    [Parameter]
    public SwitchParameter ExcludeSystemDatabases { get; set; }

    /// <summary>Filter out ms-shipped modules.</summary>
    [Parameter]
    public SwitchParameter ExcludeSystemObjects { get; set; }

    /// <summary>Database objects piped in, typically from Get-DbaDatabase.</summary>
    [Parameter(ValueFromPipeline = true, Position = 6)]
    public Microsoft.SqlServer.Management.Smo.Database[]? InputObject { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    private string _sql = "";

    // PS: $InputObject += ... persists across process blocks unless the binder
    // re-binds it (the W1-070 reference-reset law).
    private List<object?> _accumulated = new List<object?>();
    private object? _lastBoundInputObject;

    protected override void BeginProcessing()
    {
        List<string> types = new List<string>();
        foreach (string? t in Type ?? new string[0])
        {
            if (PsOps.Eq(t, "View")) types.Add("VIEW");
            if (PsOps.Eq(t, "TableValuedFunction")) types.Add("SQL_TABLE_VALUED_FUNCTION");
            if (PsOps.Eq(t, "DefaultConstraint")) types.Add("DEFAULT_CONSTRAINT");
            if (PsOps.Eq(t, "StoredProcedure")) types.Add("SQL_STORED_PROCEDURE");
            if (PsOps.Eq(t, "Rule")) types.Add("RULE");
            if (PsOps.Eq(t, "InlineTableValuedFunction")) types.Add("SQL_INLINE_TABLE_VALUED_FUNCTION");
            if (PsOps.Eq(t, "Trigger")) types.Add("SQL_TRIGGER");
            if (PsOps.Eq(t, "ScalarFunction")) types.Add("SQL_SCALAR_FUNCTION");
        }

        _sql = @"SELECT  DB_NAME() AS DatabaseName,
        so.name AS ModuleName,
        so.object_id ,
        SCHEMA_NAME(so.schema_id) AS SchemaName ,
        so.parent_object_id ,
        so.type ,
        so.type_desc ,
        so.create_date ,
        so.modify_date ,
        so.is_ms_shipped ,
        sm.definition,
         OBJECTPROPERTY(so.object_id, 'ExecIsStartUp') AS startup
        FROM sys.sql_modules sm
        LEFT JOIN sys.objects so ON sm.object_id = so.object_id
        WHERE so.modify_date >= '" + PsText(ModifiedSince) + "'";
        if (ExcludeSystemObjects.IsPresent)
        {
            _sql += "\n AND so.is_ms_shipped = 0";
        }
        if (PsTruthy(Type))
        {
            string sqlTypes = string.Join("','", types);
            _sql += " AND type_desc IN ('" + sqlTypes + "')";
        }
        _sql += "\n ORDER BY so.modify_date";
    }

    protected override void ProcessRecord()
    {
        if (!PsTruthy(InputObject) && !PsTruthy(SqlInstance))
        {
            StopFunction("You must pipe in a database or specify a SqlInstance");
            return;
        }

        if (!ReferenceEquals(InputObject, _lastBoundInputObject))
        {
            _accumulated = new List<object?>();
            if (InputObject is not null)
            {
                foreach (Microsoft.SqlServer.Management.Smo.Database db in InputObject)
                    _accumulated.Add(db);
            }
            _lastBoundInputObject = InputObject;
        }

        if (PsTruthy(SqlInstance))
        {
            WriteMessage(MessageLevel.Verbose, "Creating InputObject from " + PsJoinSpace(SqlInstance!));
            try
            {
                foreach (PSObject? fetched in NestedCommand.InvokeScoped(this, GetDatabaseScript, BoundOrNull("SqlInstance"), BoundOrNull("SqlCredential"), BoundOrNull("Database"), BoundOrNull("ExcludeDatabase"), LanguagePrimitives.IsTrue(BoundOrNull("ExcludeSystemDatabases")), BoundVerbose(), BoundDebug()))
                    _accumulated.Add(fetched);
            }
            catch (PipelineStoppedException) { throw; }
            catch (RuntimeException ex) { StatementFault.Surface(this, ex, "Get-DbaModule"); }
        }

        foreach (object? item in _accumulated)
        {
            if (!LanguagePrimitives.IsTrue(DotAccess(item, "IsAccessible")))
            {
                WriteMessage(MessageLevel.Warning, "Database " + PsText(item) + " is not accessible. Skipping.");
                continue;
            }

            object? server = DotAccess(item, "Parent");
            WriteMessage(MessageLevel.Verbose, "Processing " + PsText(item) + " on " + PsText(DotAccess(server, "DomainInstanceName")));

            object? results = null;
            try
            {
                results = PipelineValue(NestedCommand.InvokeScoped(this, ServerQueryDbScript, server, _sql, DotAccess(item, "name")));
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (RuntimeException ex)
            {
                StatementFault.Surface(this, ex, "Get-DbaModule");
                continue;
            }
            foreach (object? row in EnumerateValue(results))
            {
                PSObject result = new PSObject();
                result.Properties.Add(new PSNoteProperty("ComputerName", DotAccess(server, "ComputerName")));
                result.Properties.Add(new PSNoteProperty("InstanceName", DotAccess(server, "ServiceName")));
                result.Properties.Add(new PSNoteProperty("SqlInstance", DotAccess(server, "DomainInstanceName")));
                result.Properties.Add(new PSNoteProperty("Database", DotAccess(row, "DatabaseName")));
                result.Properties.Add(new PSNoteProperty("Name", DotAccess(row, "ModuleName")));
                result.Properties.Add(new PSNoteProperty("ObjectID", DotAccess(row, "object_id")));
                result.Properties.Add(new PSNoteProperty("SchemaName", DotAccess(row, "SchemaName")));
                result.Properties.Add(new PSNoteProperty("Type", DotAccess(row, "type_desc")));
                result.Properties.Add(new PSNoteProperty("CreateDate", DotAccess(row, "create_date")));
                result.Properties.Add(new PSNoteProperty("ModifyDate", DotAccess(row, "modify_date")));
                result.Properties.Add(new PSNoteProperty("IsMsShipped", DotAccess(row, "is_ms_shipped")));
                result.Properties.Add(new PSNoteProperty("ExecIsStartUp", DotAccess(row, "startup")));
                result.Properties.Add(new PSNoteProperty("Definition", DotAccess(row, "definition")));

                try
                {
                    foreach (PSObject? shaped in NestedCommand.InvokeScoped(this, SelectDefaultViewScript, result))
                        WriteObject(shaped);
                }
                catch (PipelineStoppedException)
                {
                    throw;
                }
                catch (RuntimeException ex)
                {
                    StatementFault.Surface(this, ex, "Get-DbaModule");
                }
            }
        }
    }

    /// <summary>PS: $PSBoundParameters.Key - the bound value or null.</summary>
    private object? BoundOrNull(string name)
    {
        object? value;
        if (MyInvocation.BoundParameters.TryGetValue(name, out value))
            return value;
        return null;
    }

    /// <summary>PS "$array" interpolation: space-joined elements.</summary>
    private static string PsJoinSpace(DbaInstanceParameter[] values)
    {
        List<string> parts = new List<string>();
        foreach (DbaInstanceParameter? value in values)
            parts.Add(PsText(value));
        return string.Join(" ", parts);
    }

    /// <summary>PS array truthiness: empty = false, one element = its truthiness,
    /// two or more = true.</summary>
    private static bool PsTruthy(object[]? values)
    {
        if (values is null || values.Length == 0)
            return false;
        if (values.Length == 1)
            return LanguagePrimitives.IsTrue(values[0]);
        return true;
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

    /// <summary>PS foreach over a value: null iterates zero times, an array yields
    /// elements (nulls included), a scalar yields itself.</summary>
    private static IEnumerable<object?> EnumerateValue(object? value)
    {
        if (value is null)
            yield break;
        if (value is object?[] array)
        {
            foreach (object? element in array)
                yield return element;
            yield break;
        }
        yield return value;
    }

    /// <summary>The PS dot operator (ETS-first single-object reads).</summary>
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

    /// <summary>PS string interpolation via LanguagePrimitives (invariant).</summary>
    private static string PsText(object? value)
    {
        if (value is null)
            return "";
        return (string)LanguagePrimitives.ConvertTo(value, typeof(string), CultureInfo.InvariantCulture);
    }

    /// <summary>A bound -Verbose carrier for the hop scopes (W1-044 convention).</summary>
    private object? BoundDebug()
    {
        object? debug;
        if (MyInvocation.BoundParameters.TryGetValue("Debug", out debug))
            return LanguagePrimitives.IsTrue(debug);
        return null;
    }

    private object? BoundVerbose()
    {
        object? verbose;
        if (MyInvocation.BoundParameters.TryGetValue("Verbose", out verbose))
            return LanguagePrimitives.IsTrue(verbose);
        return null;
    }

    // PS: Get-DbaDatabase driven by $PSBoundParameters reads (null when unbound).
    private const string GetDatabaseScript = """
param($__instances, $SqlCredential, $Database, $ExcludeDatabase, $__excludeSystem, $__boundVerbose, $__boundDebug)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($__instances, $SqlCredential, $Database, $ExcludeDatabase, $__excludeSystem, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundVerbose) { $VerbosePreference = $(if ($__boundVerbose) { "Continue" } else { "SilentlyContinue" }) }
    if ($null -ne $__boundDebug) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }
    Get-DbaDatabase -SqlInstance $__instances -SqlCredential $SqlCredential -Database $Database -ExcludeDatabase $ExcludeDatabase -ExcludeSystem:$__excludeSystem
} $__instances $SqlCredential $Database $ExcludeDatabase $__excludeSystem $__boundVerbose $__boundDebug 3>&1
""";

    // PS: $server.Query($sql, $db.name) - the database-scoped ETS call.
    private const string ServerQueryDbScript = """
param($server, $query, $dbname)
$server.Query($query, $dbname)
""";

    private const string SelectDefaultViewScript = """
param($__row)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($__row)
    Select-DefaultView -InputObject $__row -ExcludeProperty Definition
} $__row 3>&1
""";
}
