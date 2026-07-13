#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.Management.Automation;
using System.Text.RegularExpressions;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;
using Microsoft.SqlServer.Management.Smo;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Computes the extent-diff percentage since the last full backup. Port of
/// public/Get-DbaDbExtentDiff.ps1 (W1-065). The connect rides NestedConnect with
/// -NonPooledConnection and its own failure message; the -Database/-ExcludeDatabase
/// filters gate on VALUE truthiness (an empty array skips the filter, not Test-Bound);
/// inaccessible databases log a verbose skip; 2016SP2+ (13.0.5026) takes the
/// per-database DMV query (single-row property reads with the PS pipeline collapse and
/// a [math]::Round binder emulation - decimal stays decimal, otherwise double, an
/// unconvertible value faults the WHOLE object statement); older versions walk
/// master_files DBCC PAGE diff maps with PS-numeric division/addition typing (int
/// preserved when exact, double otherwise) and the begin-block Get-DbaExtent regex
/// helper (out-of-range match indexing null-walks to zero per the W1-061 law); each
/// instance ends with the piped Disconnect-DbaInstance, output discarded. Surface
/// pinned by migration/baselines/Get-DbaDbExtentDiff.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbExtentDiff")]
public sealed class GetDbaDbExtentDiffCommand : DbaInstanceCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public override DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public override PSCredential? SqlCredential { get; set; }

    /// <summary>The databases to analyze.</summary>
    [Parameter(Position = 2)]
    public object[]? Database { get; set; }

    /// <summary>The databases to skip.</summary>
    [Parameter(Position = 3)]
    public object[]? ExcludeDatabase { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // PS begin block: $rex = [regex]':(?<extent>[\d]+)\)'
    private static readonly Regex ExtentRegex = new Regex(@":(?<extent>[\d]+)\)");

    // PS: process-block locals persist and the un-tried Query statements keep their
    // STALE value on a fault - the emission statement still runs with it.
    private object? _dbccPageResults;
    private Collection<PSObject> _masterFiles = new Collection<PSObject>();

    protected override void ProcessRecord()
    {
        foreach (DbaInstanceParameter instance in SqlInstance)
        {
            Hashtable connectParams = new Hashtable();
            connectParams["SqlInstance"] = instance;
            connectParams["SqlCredential"] = SqlCredential;
            connectParams["NonPooledConnection"] = true;
            NestedConnect.Outcome connection = NestedConnect.Connect(this, connectParams);
            if (!connection.Ok)
            {
                StopFunction("Error occurred while establishing connection to " + PsText(instance), target: instance, errorRecord: connection.Failure, category: ErrorCategory.ConnectionError, continueLoop: true);
                continue;
            }
            Server server = connection.Server!;

            // PS: $dbs = $server.Databases with VALUE-truthy Name -In/-NotIn filters.
            List<Microsoft.SqlServer.Management.Smo.Database> dbs = new List<Microsoft.SqlServer.Management.Smo.Database>();
            // PS: if ($Database) is VALUE truthiness - a 1-element array takes its
            // element's truthiness, so @($false) or @("") skips the filter entirely.
            bool filterInclude = PsTruthy(Database);
            bool filterExclude = PsTruthy(ExcludeDatabase);
            foreach (Microsoft.SqlServer.Management.Smo.Database candidate in server.Databases)
            {
                if (filterInclude && !MatchesAny(candidate.Name, Database!))
                    continue;
                if (filterExclude && MatchesAny(candidate.Name, ExcludeDatabase!))
                    continue;
                dbs.Add(candidate);
            }

            List<Microsoft.SqlServer.Management.Smo.Database> sourceDbs = new List<Microsoft.SqlServer.Management.Smo.Database>();
            foreach (Microsoft.SqlServer.Management.Smo.Database db in dbs)
            {
                if (!db.IsAccessible)
                    WriteMessage(MessageLevel.Verbose, PsText(db) + " is not accessible on " + PsText(instance) + ", skipping");
                else
                    sourceDbs.Add(db);
            }

            // PS: Available from 2016 SP2
            if (server.Version is not null && server.Version >= new Version(13, 0, 5026))
            {
                foreach (Microsoft.SqlServer.Management.Smo.Database db in sourceDbs)
                {
                    const string dmvQuery = @"
                        SELECT
                        SUM(total_page_count) / 8 AS [ExtentsTotal],
                        SUM(modified_extent_page_count) / 8 AS [ExtentsChanged],
                        100.0 * SUM(modified_extent_page_count)/SUM(total_page_count) AS [ChangedPerc]
                        FROM sys.dm_db_file_space_usage
                    ";
                    try
                    {
                        _dbccPageResults = PipelineValue(NestedCommand.InvokeScoped(this, ServerQueryDbScript, server, dmvQuery, db.Name));
                    }
                    catch (PipelineStoppedException) { throw; }
                    catch (RuntimeException ex) { StatementFault.Surface(this, ex, "Get-DbaDbExtentDiff"); }

                    // PS: the [math]::Round sits INSIDE the PSCustomObject statement - a
                    // binder fault abandons the whole emission.
                    object? changedPerc;
                    try
                    {
                        changedPerc = PsMathRound(DotAccess(_dbccPageResults, "ChangedPerc"));
                    }
                    catch (PipelineStoppedException) { throw; }
                    catch (Exception ex)
                    {
                        StatementFault.Surface(this, ex is IContainsErrorRecord rec && rec.ErrorRecord is not null ? rec.ErrorRecord : new ErrorRecord(ex, "MethodCountCouldNotFindBest", ErrorCategory.NotSpecified, null));
                        continue;
                    }
                    PSObject result = new PSObject();
                    result.Properties.Add(new PSNoteProperty("ComputerName", SmoServerExtensions.GetComputerName(server)));
                    result.Properties.Add(new PSNoteProperty("InstanceName", server.ServiceName));
                    result.Properties.Add(new PSNoteProperty("SqlInstance", SmoServerExtensions.GetDomainInstanceName(server)));
                    result.Properties.Add(new PSNoteProperty("DatabaseName", db.Name));
                    result.Properties.Add(new PSNoteProperty("ExtentsTotal", DotAccess(_dbccPageResults, "ExtentsTotal")));
                    result.Properties.Add(new PSNoteProperty("ExtentsChanged", DotAccess(_dbccPageResults, "ExtentsChanged")));
                    result.Properties.Add(new PSNoteProperty("ChangedPerc", changedPerc));
                    WriteObject(result);
                }
            }
            else
            {
                const string masterFilesQuery = @"
                        SELECT [file_id], [size], database_id, DB_NAME(database_id) AS dbname FROM master.sys.master_files
                        WHERE [type_desc] = N'ROWS'
                    ";
                try
                {
                    _masterFiles = NestedCommand.InvokeScoped(this, ServerQueryScript, server, masterFilesQuery);
                }
                catch (PipelineStoppedException) { throw; }
                catch (RuntimeException ex) { StatementFault.Surface(this, ex, "Get-DbaDbExtentDiff"); }

                // PS: Where-Object dbname -In $sourcedbs.Name then Group-Object dbname.
                List<string> sourceNames = new List<string>();
                foreach (Microsoft.SqlServer.Management.Smo.Database db in sourceDbs)
                    sourceNames.Add(db.Name);
                List<string> groupOrder = new List<string>();
                Dictionary<string, List<PSObject>> groups = new Dictionary<string, List<PSObject>>(StringComparer.CurrentCultureIgnoreCase);
                foreach (PSObject? item in _masterFiles)
                {
                    if (item is null)
                        continue;
                    object? dbname = DotAccess(item, "dbname");
                    bool wanted = false;
                    foreach (string name in sourceNames)
                    {
                        if (PsOps.Eq(dbname, name))
                        {
                            wanted = true;
                            break;
                        }
                    }
                    if (!wanted)
                        continue;
                    string key = PsText(dbname);
                    List<PSObject>? group;
                    if (!groups.TryGetValue(key, out group))
                    {
                        group = new List<PSObject>();
                        groups[key] = group;
                        groupOrder.Add(key);
                    }
                    group.Add(item);
                }

                foreach (string groupName in groupOrder)
                {
                    object sizeTotal = 0;
                    List<object?> dbExtents = new List<object?>();
                    foreach (PSObject fileRow in groups[groupName])
                    {
                        // PS: int arithmetic overflow-promotes to double and the loop
                        // exits - long carries the same values and exit condition.
                        long extentId = 0;
                        object? size = DotAccess(fileRow, "size");
                        int sizeValue = ToInt(size);
                        sizeTotal = PsAdd(sizeTotal, PsDivide(sizeValue, 8));
                        while (extentId < sizeValue)
                        {
                            long pageId = extentId + 6;
                            string pageQuery = "DBCC PAGE ('" + PsText(DotAccess(fileRow, "dbname")) + "', " + PsText(DotAccess(fileRow, "file_id")) + ", " + pageId.ToString(CultureInfo.InvariantCulture) + ", 3)  WITH TABLERESULTS, NO_INFOMSGS";
                            try
                            {
                                _dbccPageResults = PipelineValue(NestedCommand.InvokeScoped(this, ServerQueryScript, server, pageQuery));
                            }
                            catch (PipelineStoppedException) { throw; }
                            catch (RuntimeException ex) { StatementFault.Surface(this, ex, "Get-DbaDbExtentDiff"); }
                            // PS: the filter pipe walks the STALE $DBCCPageResults when the
                            // Query statement faulted; a lone real $null matches nothing.
                            foreach (object? pageItem in EnumerateValue(_dbccPageResults))
                            {
                                if (pageItem is null)
                                    continue;
                                if (PsOps.Eq(DotAccess(pageItem, "VALUE"), "    CHANGED") && LikeDiffMap(DotAccess(pageItem, "ParentObject")))
                                    dbExtents.Add(pageItem);
                            }
                            extentId = extentId + 511232;
                        }
                    }
                    object extents = GetDbaExtent(dbExtents);
                    object? changedPerc;
                    try
                    {
                        changedPerc = PsMathRound(ToDouble(extents) / ToDouble(sizeTotal) * 100.0);
                    }
                    catch (PipelineStoppedException) { throw; }
                    catch (Exception ex)
                    {
                        StatementFault.Surface(this, new ErrorRecord(ex, "MethodCountCouldNotFindBest", ErrorCategory.NotSpecified, null));
                        continue;
                    }
                    PSObject result = new PSObject();
                    result.Properties.Add(new PSNoteProperty("ComputerName", SmoServerExtensions.GetComputerName(server)));
                    result.Properties.Add(new PSNoteProperty("InstanceName", server.ServiceName));
                    result.Properties.Add(new PSNoteProperty("SqlInstance", SmoServerExtensions.GetDomainInstanceName(server)));
                    result.Properties.Add(new PSNoteProperty("DatabaseName", groupName));
                    result.Properties.Add(new PSNoteProperty("ExtentsTotal", sizeTotal));
                    result.Properties.Add(new PSNoteProperty("ExtentsChanged", extents));
                    result.Properties.Add(new PSNoteProperty("ChangedPerc", changedPerc));
                    WriteObject(result);
                }
            }

            // PS: $null = $server | Disconnect-DbaInstance
            try
            {
                _ = NestedCommand.InvokeScoped(this, DisconnectScript, server, BoundVerbose());
            }
            catch (PipelineStoppedException) { throw; }
            catch (RuntimeException ex) { StatementFault.Surface(this, ex, "Get-DbaDbExtentDiff"); }
        }
    }

    /// <summary>PS begin-block Get-DbaExtent: counts extents from the regex-matched
    /// Field texts with PS-numeric accumulation.</summary>
    private static object GetDbaExtent(List<object?> dbExtents)
    {
        object res = 0;
        foreach (object? row in dbExtents)
        {
            // PS: [string[]]$field = $dbExtents.Field - each element casts to string.
            string f = PsText(DotAccess(row, "Field"));
            MatchCollection extents = ExtentRegex.Matches(f);
            if (extents.Count == 1)
            {
                res = PsAdd(res, 1);
            }
            else
            {
                // PS: out-of-range MatchCollection indexing null-walks; [int]$null is 0.
                int second = extents.Count > 1 ? ToInt(extents[1].Groups["extent"].Value) : 0;
                int first = extents.Count > 0 ? ToInt(extents[0].Groups["extent"].Value) : 0;
                int pages = second - first;
                res = PsAdd(res, PsAdd(PsDivide(pages, 8), 1));
            }
        }
        return res;
    }

    /// <summary>PS -like 'DIFF_MAP*' (case-insensitive prefix here).</summary>
    private static bool LikeDiffMap(object? value)
    {
        if (value is null)
            return false;
        return PsText(value).StartsWith("DIFF_MAP", StringComparison.OrdinalIgnoreCase);
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

    /// <summary>PS -In over the raw filter array (elementwise -eq).</summary>
    private static bool MatchesAny(string name, object[] values)
    {
        foreach (object? value in values)
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

    /// <summary>PS [math]::Round(x, 2) binder: decimal keeps the decimal overload,
    /// everything else converts to double; an unconvertible value throws (the caller
    /// statement-faults the whole emission).</summary>
    private static object PsMathRound(object? value)
    {
        object? unwrapped = value is PSObject pso ? pso.BaseObject : value;
        if (unwrapped is decimal dec)
            return Math.Round(dec, 2);
        double dbl = (double)LanguagePrimitives.ConvertTo(unwrapped, typeof(double), CultureInfo.InvariantCulture);
        return Math.Round(dbl, 2);
    }

    /// <summary>PS division: an exact int result stays int, otherwise double.</summary>
    private static object PsDivide(int a, int b)
    {
        if (b != 0 && a % b == 0)
            return a / b;
        return (double)a / b;
    }

    /// <summary>PS addition over the int-or-double values this command produces; an
    /// int overflow promotes to double like the engine.</summary>
    private static object PsAdd(object a, object b)
    {
        if (a is int ia && b is int ib)
        {
            long sum = (long)ia + ib;
            if (sum >= int.MinValue && sum <= int.MaxValue)
                return (int)sum;
            return (double)sum;
        }
        return Convert.ToDouble(a, CultureInfo.InvariantCulture) + Convert.ToDouble(b, CultureInfo.InvariantCulture);
    }

    /// <summary>Enumerates a PS pipeline value: null yields nothing through a filter
    /// pipe, an array yields elements, a scalar yields itself.</summary>
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

    private static int ToInt(object? value)
    {
        if (value is null)
            return 0;
        object? unwrapped = value is PSObject pso ? pso.BaseObject : value;
        if (unwrapped is null || unwrapped is DBNull)
            return 0;
        if (unwrapped is string text && text.Length == 0)
            return 0;
        return (int)LanguagePrimitives.ConvertTo(unwrapped, typeof(int), CultureInfo.InvariantCulture);
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

    /// <summary>A bound -Verbose carrier for the hop scopes (W1-044 convention).</summary>
    private object? BoundVerbose()
    {
        object? verbose;
        if (MyInvocation.BoundParameters.TryGetValue("Verbose", out verbose))
            return LanguagePrimitives.IsTrue(verbose);
        return null;
    }

    // PS: $server.Query($query) on the engine (the W1-046 seam).
    private const string ServerQueryScript = """
param($server, $query)
$server.Query($query)
""";

    // PS: $server.Query($query, $dbname) - the database-scoped ETS call.
    private const string ServerQueryDbScript = """
param($server, $query, $dbname)
$server.Query($query, $dbname)
""";

    private const string DisconnectScript = """
param($server, $__boundVerbose)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($server, $__boundVerbose)
    if ($null -ne $__boundVerbose) { $VerbosePreference = $(if ($__boundVerbose) { "Continue" } else { "SilentlyContinue" }) }
    $null = $server | Disconnect-DbaInstance
} $server $__boundVerbose 3>&1
""";
}
