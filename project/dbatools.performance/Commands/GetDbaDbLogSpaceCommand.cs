#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Management.Automation;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;
using Microsoft.SqlServer.Management.Smo;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Reports transaction-log size and usage per database. Port of
/// public/Get-DbaDbLogSpace.ps1 (W1-067). The database walk filters IsAccessible
/// truthiness first, then the VALUE-truthy -Database/-ExcludeDatabase gates (W1-065 law)
/// and the -ExcludeSystemDatabase switch; 2012+ takes the per-db db-scoped DMV query
/// (single-row pipeline collapse; the [dbasize] casts ride LanguagePrimitives so a cast
/// fault abandons the whole object statement); older versions take one DBCC
/// SQLPERF(LOGSPACE) filtered by `$dbs.name -contains` (member-enumeration) whose catch
/// references the UNDEFINED $db - the module-SessionState dynamic read (lab-unreachable:
/// sql01 is 2019). Surface pinned by migration/baselines/Get-DbaDbLogSpace.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbLogSpace")]
public sealed class GetDbaDbLogSpaceCommand : DbaInstanceCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public override DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public override PSCredential? SqlCredential { get; set; }

    /// <summary>The databases to report on.</summary>
    [Parameter(Position = 2)]
    public string[]? Database { get; set; }

    /// <summary>The databases to skip.</summary>
    [Parameter(Position = 3)]
    public string[]? ExcludeDatabase { get; set; }

    /// <summary>Skip system databases.</summary>
    [Parameter]
    public SwitchParameter ExcludeSystemDatabase { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

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

            // PS: $dbs = $server.Databases | Where-Object IsAccessible, then the
            // VALUE-truthy include/exclude gates and the system-db switch.
            bool filterInclude = PsTruthy(Database);
            bool filterExclude = PsTruthy(ExcludeDatabase);
            List<Microsoft.SqlServer.Management.Smo.Database> dbs = new List<Microsoft.SqlServer.Management.Smo.Database>();
            foreach (Microsoft.SqlServer.Management.Smo.Database candidate in server.Databases)
            {
                if (!candidate.IsAccessible)
                    continue;
                if (filterInclude && !MatchesAny(candidate.Name, Database!))
                    continue;
                if (filterExclude && MatchesAny(candidate.Name, ExcludeDatabase!))
                    continue;
                if (ExcludeSystemDatabase.IsPresent && candidate.IsSystemObject)
                    continue;
                dbs.Add(candidate);
            }

            // PS: 2012+ use new DMV
            if (server.VersionMajor >= 11)
            {
                foreach (Microsoft.SqlServer.Management.Smo.Database db in dbs)
                {
                    object? logspace;
                    try
                    {
                        logspace = PipelineValue(NestedCommand.InvokeScoped(this, ServerQueryDbScript, server, "SELECT * FROM sys.dm_db_log_space_usage", db.Name));
                    }
                    catch (PipelineStoppedException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        StopFunction("Unable to collect log space data on " + PsText(instance) + ".", target: db, errorRecord: StatementFault.Record(ex, "Get-DbaDbLogSpace"), continueLoop: true);
                        continue;
                    }
                    // PS: the [dbasize] casts sit INSIDE the PSCustomObject statement - a
                    // cast fault abandons the whole emission.
                    object? logSize;
                    object? logSpaceUsed;
                    try
                    {
                        logSize = DbaSize(DotAccess(logspace, "total_log_size_in_bytes"));
                        logSpaceUsed = DbaSize(DotAccess(logspace, "used_log_space_in_bytes"));
                    }
                    catch (PipelineStoppedException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        StatementFault.Surface(this, ex, "Get-DbaDbLogSpace");
                        continue;
                    }
                    PSObject result = new PSObject();
                    result.Properties.Add(new PSNoteProperty("ComputerName", SmoServerExtensions.GetComputerName(server)));
                    result.Properties.Add(new PSNoteProperty("InstanceName", server.ServiceName));
                    result.Properties.Add(new PSNoteProperty("SqlInstance", SmoServerExtensions.GetDomainInstanceName(server)));
                    result.Properties.Add(new PSNoteProperty("Database", db.Name));
                    result.Properties.Add(new PSNoteProperty("LogSize", logSize));
                    result.Properties.Add(new PSNoteProperty("LogSpaceUsedPercent", DotAccess(logspace, "used_log_space_in_percent")));
                    result.Properties.Add(new PSNoteProperty("LogSpaceUsed", logSpaceUsed));
                    WriteObject(result);
                }
            }
            else
            {
                List<object?> logspaceRows = new List<object?>();
                try
                {
                    List<string> dbNames = new List<string>();
                    foreach (Microsoft.SqlServer.Management.Smo.Database db in dbs)
                        dbNames.Add(db.Name);
                    foreach (PSObject? row in NestedCommand.InvokeScoped(this, ServerQueryScript, server, "DBCC SQLPERF(LOGSPACE)"))
                    {
                        if (row is null)
                            continue;
                        object? rowName = DotAccess(row, "Database Name");
                        foreach (string name in dbNames)
                        {
                            if (PsOps.Eq(name, rowName))
                            {
                                logspaceRows.Add(row);
                                break;
                            }
                        }
                    }
                }
                catch (PipelineStoppedException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // PS: -Target $db references the UNDEFINED $db here - the dynamic
                    // scope read walks module -> global (the W1-051 law).
                    object? moduleDb = PipelineValue(NestedCommand.InvokeScoped(this, ModuleVariableScript, "db"));
                    StopFunction("Unable to collect log space data on " + PsText(instance) + ".", target: moduleDb, errorRecord: StatementFault.Record(ex, "Get-DbaDbLogSpace"), continueLoop: true);
                    continue;
                }

                foreach (object? ls in logspaceRows)
                {
                    object? logSize;
                    object? logSpaceUsed;
                    try
                    {
                        double sizeMb = ToDouble(DotAccess(ls, "Log Size (MB)"));
                        double usedPct = ToDouble(DotAccess(ls, "Log Space Used (%)"));
                        logSize = DbaSize(sizeMb * 1048576.0);
                        logSpaceUsed = DbaSize(sizeMb * (usedPct / 100.0) * 1048576.0);
                    }
                    catch (PipelineStoppedException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        StatementFault.Surface(this, ex, "Get-DbaDbLogSpace");
                        continue;
                    }
                    PSObject result = new PSObject();
                    result.Properties.Add(new PSNoteProperty("ComputerName", SmoServerExtensions.GetComputerName(server)));
                    result.Properties.Add(new PSNoteProperty("InstanceName", server.ServiceName));
                    result.Properties.Add(new PSNoteProperty("SqlInstance", SmoServerExtensions.GetDomainInstanceName(server)));
                    result.Properties.Add(new PSNoteProperty("Database", DotAccess(ls, "Database Name")));
                    result.Properties.Add(new PSNoteProperty("LogSize", logSize));
                    result.Properties.Add(new PSNoteProperty("LogSpaceUsedPercent", DotAccess(ls, "Log Space Used (%)")));
                    result.Properties.Add(new PSNoteProperty("LogSpaceUsed", logSpaceUsed));
                    WriteObject(result);
                }
            }
        }
    }

    /// <summary>PS [dbasize] cast via the engine's conversion machinery (null stays null).</summary>
    private static object? DbaSize(object? value)
    {
        object? unwrapped = value is PSObject pso ? pso.BaseObject : value;
        if (unwrapped is null)
            return LanguagePrimitives.ConvertTo(null, typeof(Dataplat.Dbatools.Utility.Size), CultureInfo.InvariantCulture);
        return LanguagePrimitives.ConvertTo(unwrapped, typeof(Dataplat.Dbatools.Utility.Size), CultureInfo.InvariantCulture);
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

    /// <summary>PS -In over the filter array (elementwise -eq).</summary>
    private static bool MatchesAny(string name, string[] values)
    {
        foreach (string value in values)
        {
            if (PsOps.Eq(name, value))
                return true;
        }
        return false;
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

    private static double ToDouble(object? value)
    {
        object? unwrapped = value is PSObject pso ? pso.BaseObject : value;
        return (double)LanguagePrimitives.ConvertTo(unwrapped, typeof(double), CultureInfo.InvariantCulture);
    }

    /// <summary>The PS dot operator with member-enumeration semantics (W1-060 shape).</summary>
    private static object? DotAccess(object? item, string name)
    {
        if (item is null)
            return null;
        PSObject wrapped = PSObject.AsPSObject(item);
        PSPropertyInfo? direct = wrapped.Properties[name];
        if (direct is not null)
        {
            object? value;
            try { value = direct.Value; }
            catch { return null; }
            return UnwrapTransit(value);
        }
        object? baseValue = wrapped.BaseObject;
        if (baseValue is not string && LanguagePrimitives.GetEnumerable(baseValue) is IEnumerable elements)
        {
            List<object?> collected = new List<object?>();
            foreach (object? element in elements)
            {
                if (element is null)
                    continue;
                PSObject wrappedElement = PSObject.AsPSObject(element);
                PSPropertyInfo? property = wrappedElement.Properties[name];
                if (property is not null)
                {
                    try { collected.Add(UnwrapTransit(property.Value)); }
                    catch { collected.Add(null); }
                }
                else if (wrappedElement.BaseObject is PSCustomObject)
                {
                    collected.Add(null);
                }
            }
            if (collected.Count == 0)
                return null;
            if (collected.Count == 1)
                return collected[0];
            return collected.ToArray();
        }
        return null;
    }

    private static object? UnwrapTransit(object? value)
    {
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

    // PS: $server.Query($query) on the engine (the W1-046 seam).
    private const string ServerQueryScript = """
param($server, $query)
$server.Query($query)
""";

    // PS: $server.query($query, $dbname) - the database-scoped ETS call.
    private const string ServerQueryDbScript = """
param($server, $query, $dbname)
$server.query($query, $dbname)
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
