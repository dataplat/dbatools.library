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
using Dataplat.Dbatools.Utility;
using Microsoft.SqlServer.Management.Smo;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Reports buffer-pool consumption per database and page type. Port of
/// public/Get-DbaDbMemoryUsage.ps1 (W1-068). Quirks preserved: the query catch's
/// Stop-Function has NO -Continue and the function never returns - non-EE execution
/// FALLS THROUGH into the row loop with the STALE $results from a prior iteration
/// (field-persisted; EE throws); the include/exclude/system gates are Test-BOUND
/// checks (not truthiness); $percentUsed is its own statement (DBNull maps to int 0,
/// otherwise [Math]::Round(double) - a fault keeps the STALE value and the emission
/// still runs); the [int] PageCount cast and the [DbaSize]$SizeMb * 1024 operator
/// (Size op_Multiply(Size, double)) sit INSIDE the PSCustomObject statement - a fault
/// abandons that row's emission; each row pipes through Select-DefaultView
/// -ExcludeProperty PageCount. Surface pinned by
/// migration/baselines/Get-DbaDbMemoryUsage.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbMemoryUsage")]
public sealed class GetDbaDbMemoryUsageCommand : DbaInstanceCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public override DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public override PSCredential? SqlCredential { get; set; }

    /// <summary>The databases to include.</summary>
    [Parameter(Position = 2, ValueFromPipelineByPropertyName = true)]
    public object[]? Database { get; set; }

    /// <summary>The databases to skip.</summary>
    [Parameter(Position = 3)]
    public object[]? ExcludeDatabase { get; set; }

    /// <summary>Include the system databases (excluded by default).</summary>
    [Parameter]
    public SwitchParameter IncludeSystemDb { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // PS begin block: the buffer-descriptor query, verbatim.
    private const string Sql = @"DECLARE @total_buffer INT;
            SELECT @total_buffer = cntr_value
            FROM sys.dm_os_performance_counters
            WHERE RTRIM([object_name]) LIKE '%Buffer Manager'
                AND counter_name = 'Database Pages';

            ;WITH src AS (
                SELECT database_id, page_type, db_buffer_pages = COUNT_BIG(*)
                FROM sys.dm_os_buffer_descriptors
                GROUP BY database_id, page_type
            )
            SELECT [DatabaseName] = CASE [database_id] WHEN 32767 THEN 'ResourceDb' ELSE DB_NAME([database_id]) END,
                page_type AS 'PageType',
                db_buffer_pages AS 'PageCount',
                (db_buffer_pages * 8) / 1024 AS 'SizeMb',
                CAST(db_buffer_pages * 100.0 / @total_buffer AS FLOAT) AS 'PercentUsed'
            FROM src
            ORDER BY [DatabaseName];";

    private static readonly string[] SystemDbNames = new string[] { "master", "model", "msdb", "tempdb", "ResourceDb" };

    // PS: process-block locals persist; the no-Continue catch falls through into the
    // row loop with the STALE $results, and a faulted $percentUsed statement keeps its
    // previous value while the emission still runs.
    private Collection<PSObject> _results = new Collection<PSObject>();
    // PS: $percentUsed starts UNDEFINED (null) - a first-row fault leaves it null.
    private object? _percentUsed;

    protected override void ProcessRecord()
    {
        foreach (DbaInstanceParameter instance in SqlInstance)
        {
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

            try
            {
                _results = NestedCommand.InvokeScoped(this, ServerQueryScript, server, Sql);
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // PS: Stop-Function WITHOUT -Continue and no return - non-EE falls
                // through to the row loop with the stale $results (EE throws).
                StopFunction("Issue collecting data", target: instance, errorRecord: StatementFault.Record(ex, "Get-DbaDbMemoryUsage"));
            }

            foreach (PSObject? item in _results)
            {
                if (item is null)
                    continue;
                object? databaseName = DotAccess(item, "DatabaseName");

                if (TestBound("Database"))
                {
                    if (!MatchesAny(databaseName, Database ?? new object[0]))
                        continue;
                }
                if (TestBound("ExcludeDatabase"))
                {
                    if (MatchesAny(databaseName, ExcludeDatabase ?? new object[0]))
                        continue;
                }
                if (!TestBound("IncludeSystemDb"))
                {
                    if (MatchesAny(databaseName, SystemDbNames))
                        continue;
                }

                // PS: the $percentUsed statement - DBNull maps to int 0, otherwise
                // [Math]::Round(double); a fault keeps the stale value.
                object? rawPercent = DotAccess(item, "PercentUsed");
                if (rawPercent is DBNull)
                {
                    _percentUsed = 0;
                }
                else
                {
                    try
                    {
                        _percentUsed = Math.Round((double)LanguagePrimitives.ConvertTo(rawPercent, typeof(double), CultureInfo.InvariantCulture));
                    }
                    catch (PipelineStoppedException) { throw; }
                    catch (Exception ex)
                    {
                        // PS: the [Math]::Round binder fault - MethodException with the
                        // argument-conversion id and NotSpecified category.
                        string faultMessage = "Cannot convert argument \"d\", with value: \"" + PsText(rawPercent) + "\", for \"Round\" to type \"System.Double\": \"" + ex.Message + "\"";
                        StatementFault.Surface(this, new ErrorRecord(new MethodException(faultMessage, ex), "MethodArgumentConversionInvalidCastArgument", ErrorCategory.NotSpecified, null));
                    }
                }

                // PS: the [int] cast and the [DbaSize]*1024 operator sit inside the
                // PSCustomObject statement - a fault abandons this row's emission.
                object? pageCount;
                object? size;
                try
                {
                    pageCount = LanguagePrimitives.ConvertTo(DotAccess(item, "PageCount"), typeof(int), CultureInfo.InvariantCulture);
                    // PS: [DbaSize]$null * 1024 null-propagates without invoking the
                    // operator; only a real Size multiplies.
                    object? sizeMbValue = DotAccess(item, "SizeMb");
                    if (sizeMbValue is null)
                    {
                        size = null;
                    }
                    else
                    {
                        Size sizeMb = (Size)LanguagePrimitives.ConvertTo(sizeMbValue, typeof(Size), CultureInfo.InvariantCulture);
                        size = sizeMb * 1024.0;
                    }
                }
                catch (PipelineStoppedException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    StatementFault.Surface(this, ex, "Get-DbaDbMemoryUsage");
                    continue;
                }

                PSObject result = new PSObject();
                result.Properties.Add(new PSNoteProperty("ComputerName", SmoServerExtensions.GetComputerName(server)));
                result.Properties.Add(new PSNoteProperty("InstanceName", server.ServiceName));
                result.Properties.Add(new PSNoteProperty("SqlInstance", SmoServerExtensions.GetDomainInstanceName(server)));
                result.Properties.Add(new PSNoteProperty("Database", databaseName));
                result.Properties.Add(new PSNoteProperty("PageType", DotAccess(item, "PageType")));
                result.Properties.Add(new PSNoteProperty("PageCount", pageCount));
                result.Properties.Add(new PSNoteProperty("Size", size));
                result.Properties.Add(new PSNoteProperty("PercentUsed", _percentUsed));

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
                    StatementFault.Surface(this, ex, "Get-DbaDbMemoryUsage");
                }
            }
        }
    }

    /// <summary>PS -in/-notin over the filter array (elementwise -eq).</summary>
    private static bool MatchesAny(object? value, object[] candidates)
    {
        foreach (object? candidate in candidates)
        {
            if (PsOps.Eq(value, candidate))
                return true;
        }
        return false;
    }

    /// <summary>The PS dot operator (single objects here; DataRow column reads).</summary>
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

    // PS: $server.Query($query) on the engine (the W1-046 seam).
    private const string ServerQueryScript = """
param($server, $query)
$server.Query($query)
""";

    private const string SelectDefaultViewScript = """
param($__row)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($__row)
    Select-DefaultView -InputObject $__row -ExcludeProperty "PageCount"
} $__row 3>&1
""";
}
