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
/// Reads resource-monitor memory notifications from the ring buffer. Port of
/// public/Get-DbaMemoryCondition.ps1 (W1-081). The query catch's Stop-Function has NO
/// -Continue and the function never returns - non-EE execution FALLS THROUGH into the
/// row loop with the STALE $results (field-persisted, the W1-068 class; EE throws); each
/// row's eight [dbasize] * 1024 computations sit INSIDE the PSCustomObject statement -
/// a cast fault (DBNull) abandons that row statement-conditionally while a null
/// propagates past the multiply (the W1-068 null-propagation law). Surface pinned by
/// migration/baselines/Get-DbaMemoryCondition.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaMemoryCondition")]
public sealed class GetDbaMemoryConditionCommand : DbaInstanceCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public override DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public override PSCredential? SqlCredential { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    private const string Sql = @"
    SELECT
        CONVERT(VARCHAR(30), GETDATE(), 121) AS Runtime,
        DATEADD(MILLISECOND, -1 * CONVERT(BIGINT, (sys.ms_ticks - sys.s_ticks*1000) - (a.[RecordTime] - a.[RecordTime_S]*1000)), DATEADD(SECOND, -1 * (sys.s_ticks - a.[RecordTime_S]), GETDATE())) AS NotificationTime,
        [NotificationType],
        [MemoryUtilizationPercent],
        [TotalPhysicalMemoryKB],
        [AvailablePhysicalMemoryKB],
        [TotalPageFileKB],
        [AvailablePageFileKB],
        [TotalVirtualAddressSpaceKB],
        [AvailableVirtualAddressSpaceKB],
        [NodeId],
        [SQLReservedMemoryKB],
        [SQLCommittedMemoryKB],
        [RecordId],
        [Type],
        [Indicators],
        [RecordTime],
        sys.ms_ticks AS [CurrentTime]
    FROM
    (
        SELECT
            x.value('(//Record/ResourceMonitor/Notification)[1]', 'VARCHAR(30)') AS [NotificationType],
            x.value('(//Record/MemoryRecord/MemoryUtilization)[1]', 'BIGINT') AS [MemoryUtilizationPercent],
            x.value('(//Record/MemoryRecord/TotalPhysicalMemory)[1]', 'BIGINT') AS [TotalPhysicalMemoryKB],
            x.value('(//Record/MemoryRecord/AvailablePhysicalMemory)[1]', 'BIGINT') AS [AvailablePhysicalMemoryKB],
            x.value('(//Record/MemoryRecord/TotalPageFile)[1]', 'BIGINT') AS [TotalPageFileKB],
            x.value('(//Record/MemoryRecord/AvailablePageFile)[1]', 'BIGINT') AS [AvailablePageFileKB],
            x.value('(//Record/MemoryRecord/TotalVirtualAddressSpace)[1]', 'BIGINT') AS [TotalVirtualAddressSpaceKB],
            x.value('(//Record/MemoryRecord/AvailableVirtualAddressSpace)[1]', 'BIGINT') AS [AvailableVirtualAddressSpaceKB],
            x.value('(//Record/MemoryNode/@id)[1]', 'BIGINT') AS [NodeId],
            x.value('(//Record/MemoryNode/ReservedMemory)[1]', 'BIGINT') AS [SQLReservedMemoryKB],
            x.value('(//Record/MemoryNode/CommittedMemory)[1]', 'BIGINT') AS [SQLCommittedMemoryKB],
            x.value('(//Record/@id)[1]', 'BIGINT') AS [RecordId],
            x.value('(//Record/@type)[1]', 'VARCHAR(30)') AS [Type],
            x.value('(//Record/ResourceMonitor/Indicators)[1]', 'BIGINT') AS [Indicators],
            x.value('(//Record/@time)[1]', 'BIGINT') AS [RecordTime],
            CONVERT(BIGINT, x.value('(//Record/@time)[1]', 'BIGINT')/1000) AS [RecordTime_S]
        FROM
        (
            SELECT CAST(record AS XML) FROM sys.dm_os_ring_buffers
            WHERE ring_buffer_type = 'RING_BUFFER_RESOURCE_MONITOR'
        ) AS R(x)
    ) a
    CROSS JOIN
    (
        SELECT
            ms_ticks,
            CONVERT(BIGINT, ms_ticks/1000) AS s_ticks
        FROM sys.dm_os_sys_info
    ) sys
    ORDER BY a.[RecordTime] ASC";

    // PS: process-block locals persist; the no-Continue catch falls through into the
    // row loop with the STALE $results.
    private object? _results;

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
                _results = PipelineValue(NestedCommand.InvokeScoped(this, ServerQueryScript, server, Sql));
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // PS: Stop-Function WITHOUT -Continue and no return - non-EE falls
                // through to the row loop with the stale $results (EE throws).
                StopFunction("Issue collecting data", target: instance, errorRecord: StatementFault.Record(ex, "Get-DbaMemoryCondition"));
            }

            foreach (object? row in EnumerateValue(_results))
            {
                // PS: the [dbasize] casts sit INSIDE the PSCustomObject statement - a
                // cast fault abandons this row's emission statement-conditionally.
                object? totalPhysical;
                object? availablePhysical;
                object? totalPageFile;
                object? availablePageFile;
                object? totalVas;
                object? availableVas;
                object? reserved;
                object? committed;
                try
                {
                    totalPhysical = DbaSizeTimes1024(DotAccess(row, "TotalPhysicalMemoryKB"));
                    availablePhysical = DbaSizeTimes1024(DotAccess(row, "AvailablePhysicalMemoryKB"));
                    totalPageFile = DbaSizeTimes1024(DotAccess(row, "TotalPageFileKB"));
                    availablePageFile = DbaSizeTimes1024(DotAccess(row, "AvailablePageFileKB"));
                    totalVas = DbaSizeTimes1024(DotAccess(row, "TotalVirtualAddressSpaceKB"));
                    availableVas = DbaSizeTimes1024(DotAccess(row, "AvailableVirtualAddressSpaceKB"));
                    reserved = DbaSizeTimes1024(DotAccess(row, "SQLReservedMemoryKB"));
                    committed = DbaSizeTimes1024(DotAccess(row, "SQLCommittedMemoryKB"));
                }
                catch (PipelineStoppedException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    StatementFault.Surface(this, ex, "Get-DbaMemoryCondition");
                    continue;
                }

                PSObject result = new PSObject();
                result.Properties.Add(new PSNoteProperty("ComputerName", SmoServerExtensions.GetComputerName(server)));
                result.Properties.Add(new PSNoteProperty("InstanceName", server.ServiceName));
                result.Properties.Add(new PSNoteProperty("SqlInstance", SmoServerExtensions.GetDomainInstanceName(server)));
                result.Properties.Add(new PSNoteProperty("Runtime", DotAccess(row, "runtime")));
                result.Properties.Add(new PSNoteProperty("NotificationTime", DotAccess(row, "NotificationTime")));
                result.Properties.Add(new PSNoteProperty("NotificationType", DotAccess(row, "NotificationType")));
                result.Properties.Add(new PSNoteProperty("MemoryUtilizationPercent", DotAccess(row, "MemoryUtilizationPercent")));
                result.Properties.Add(new PSNoteProperty("TotalPhysicalMemory", totalPhysical));
                result.Properties.Add(new PSNoteProperty("AvailablePhysicalMemory", availablePhysical));
                result.Properties.Add(new PSNoteProperty("TotalPageFile", totalPageFile));
                result.Properties.Add(new PSNoteProperty("AvailablePageFile", availablePageFile));
                result.Properties.Add(new PSNoteProperty("TotalVirtualAddressSpace", totalVas));
                result.Properties.Add(new PSNoteProperty("AvailableVirtualAddressSpace", availableVas));
                result.Properties.Add(new PSNoteProperty("NodeId", DotAccess(row, "NodeId")));
                result.Properties.Add(new PSNoteProperty("SQLReservedMemory", reserved));
                result.Properties.Add(new PSNoteProperty("SQLCommittedMemory", committed));
                result.Properties.Add(new PSNoteProperty("RecordId", DotAccess(row, "RecordId")));
                result.Properties.Add(new PSNoteProperty("Type", DotAccess(row, "Type")));
                result.Properties.Add(new PSNoteProperty("Indicators", DotAccess(row, "Indicators")));
                result.Properties.Add(new PSNoteProperty("RecordTime", DotAccess(row, "RecordTime")));
                result.Properties.Add(new PSNoteProperty("CurrentTime", DotAccess(row, "CurrentTime")));
                WriteObject(result);
            }
        }
    }

    /// <summary>PS: [dbasize]$x * 1024 - null propagates past the operator; a real value
    /// casts via LanguagePrimitives then multiplies (Size op_Multiply); DBNull faults.</summary>
    private static object? DbaSizeTimes1024(object? value)
    {
        object? unwrapped = value is PSObject pso ? pso.BaseObject : value;
        if (unwrapped is null)
            return null;
        Size sized = (Size)LanguagePrimitives.ConvertTo(unwrapped, typeof(Size), CultureInfo.InvariantCulture);
        return sized * 1024.0;
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
