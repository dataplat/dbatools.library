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
/// Reports latch wait statistics. Port of public/Get-DbaLatchStatistic.ps1 (W1-079).
/// The begin block interpolates -Threshold (invariant int) into the HAVING clause and
/// logs the query at Debug level ONCE; each instance connects with -MinimumVersion 9
/// and the Query rides the hop in the foreach expression (statement-conditional); rows
/// project the raw reads as plain PSCustomObjects (no default view). Surface pinned by
/// migration/baselines/Get-DbaLatchStatistic.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaLatchStatistic")]
public sealed class GetDbaLatchStatisticCommand : DbaInstanceCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public override DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public override PSCredential? SqlCredential { get; set; }

    /// <summary>Cumulative percentage threshold for the HAVING clause.</summary>
    [Parameter(Position = 2)]
    public int Threshold { get; set; } = 95;

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    private string _sql = "";

    protected override void BeginProcessing()
    {
        _sql = @"WITH [Latches] AS
               (
                   SELECT
                       [latch_class],
                       [wait_time_ms] / 1000.0 AS [WaitS],
                       [waiting_requests_count] AS [WaitCount],
                       CASE WHEN SUM([wait_time_ms]) OVER() > 0 THEN 100.0 * [wait_time_ms] / SUM([wait_time_ms]) OVER() END AS [Percentage],
                       ROW_NUMBER() OVER(ORDER BY [wait_time_ms] DESC) AS [RowNum]
                   FROM sys.dm_os_latch_stats
                   WHERE [latch_class] NOT IN (N'BUFFER')
               )
               SELECT
                   MAX ([W1].[latch_class]) AS [LatchClass],
                   CAST (MAX ([W1].[WaitS]) AS DECIMAL(14, 2)) AS [WaitSeconds],
                   MAX ([W1].[WaitCount]) AS [WaitCount],
                   CAST (MAX ([W1].[Percentage]) AS DECIMAL(14, 2)) AS [Percentage],
                   CAST (CASE WHEN MAX([W1].[WaitCount]) > 0 THEN MAX([W1].[WaitS]) / MAX([W1].[WaitCount]) END AS DECIMAL (14, 4)) AS [AvgWaitSeconds],
                   CAST ('https://www.sqlskills.com/help/latches/' + MAX ([W1].[latch_class]) AS XML) AS [URL]
               FROM [Latches] AS [W1]
               INNER JOIN [Latches] AS [W2]
                   ON [W2].[RowNum] <= [W1].[RowNum]
               GROUP BY [W1].[RowNum]
               HAVING SUM ([W2].[Percentage]) - MAX ([W1].[Percentage]) < " + Threshold.ToString(CultureInfo.InvariantCulture) + ";";

        WriteMessage(MessageLevel.Debug, _sql);
    }

    protected override void ProcessRecord()
    {
        // PS: if (Test-FunctionInterrupt) { return }
        if (Interrupted)
            return;

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

            object? results = null;
            try
            {
                results = PipelineValue(NestedCommand.InvokeScoped(this, ServerQueryScript, server, _sql));
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (RuntimeException ex)
            {
                StatementFault.Surface(this, ex, "Get-DbaLatchStatistic");
                continue;
            }
            foreach (object? row in EnumerateValue(results))
            {
                PSObject result = new PSObject();
                result.Properties.Add(new PSNoteProperty("ComputerName", SmoServerExtensions.GetComputerName(server)));
                result.Properties.Add(new PSNoteProperty("InstanceName", server.ServiceName));
                result.Properties.Add(new PSNoteProperty("SqlInstance", SmoServerExtensions.GetDomainInstanceName(server)));
                result.Properties.Add(new PSNoteProperty("WaitType", DotAccess(row, "LatchClass")));
                result.Properties.Add(new PSNoteProperty("WaitSeconds", DotAccess(row, "WaitSeconds")));
                result.Properties.Add(new PSNoteProperty("WaitCount", DotAccess(row, "WaitCount")));
                result.Properties.Add(new PSNoteProperty("Percentage", DotAccess(row, "Percentage")));
                result.Properties.Add(new PSNoteProperty("AverageWaitSeconds", DotAccess(row, "AvgWaitSeconds")));
                result.Properties.Add(new PSNoteProperty("URL", DotAccess(row, "URL")));
                WriteObject(result);
            }
        }
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

    /// <summary>The PS dot operator (raw DataRow column reads).</summary>
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

    // PS: $server.Query($query) on the engine (the W1-046 seam).
    private const string ServerQueryScript = """
param($server, $query)
$server.Query($query)
""";
}
