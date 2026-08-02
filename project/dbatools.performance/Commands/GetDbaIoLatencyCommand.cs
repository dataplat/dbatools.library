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
/// Reports IO latency statistics per database file. Port of
/// public/Get-DbaIoLatency.ps1 (W1-078). The begin block logs the query at Debug level
/// ONCE; each instance connects with -MinimumVersion 9 and the Query rides the hop in
/// the foreach expression (statement-conditional); rows project the 23 raw reads and
/// pipe through Select-DefaultView with the seven-name exclude list. Surface pinned by
/// migration/baselines/Get-DbaIoLatency.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaIoLatency")]
public sealed class GetDbaIoLatencyCommand : DbaInstanceCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public override DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public override PSCredential? SqlCredential { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    private const string Sql = @"SELECT
            [vfs].[database_id],
            DB_NAME ([vfs].[database_id]) AS [DatabaseName],
            [vfs].[file_id],
            [mf].[physical_name],
            [num_of_reads],
            [io_stall_read_ms],
            [num_of_writes],
            [io_stall_write_ms],
            [io_stall],
            [num_of_bytes_read],
            [num_of_bytes_written],
            [sample_ms],
            [size_on_disk_bytes],
            [file_handle],
            [ReadLatency] =
            CASE WHEN [num_of_reads] = 0
                THEN 0
                ELSE ([io_stall_read_ms] / [num_of_reads])
            END,
            [WriteLatency] =
                CASE WHEN [num_of_writes] = 0
                    THEN 0
                    ELSE ([io_stall_write_ms] / [num_of_writes])
                END,
            [Latency] =
                CASE WHEN ([num_of_reads] = 0 AND [num_of_writes] = 0)
                    THEN 0
                    ELSE ([io_stall] / ([num_of_reads] + [num_of_writes]))
                END,
            [AvgBPerRead] =
                CASE WHEN [num_of_reads] = 0
                    THEN 0
                    ELSE ([num_of_bytes_read] / [num_of_reads])
                END,
            [AvgBPerWrite] =
                CASE WHEN [num_of_writes] = 0
                    THEN 0
                    ELSE ([num_of_bytes_written] / [num_of_writes])
                END,
            [AvgBPerTransfer] =
                CASE WHEN ([num_of_reads] = 0 AND [num_of_writes] = 0)
                    THEN 0
                    ELSE
                        (([num_of_bytes_read] + [num_of_bytes_written]) /
                        ([num_of_reads] + [num_of_writes]))
                    END
        FROM sys.dm_io_virtual_file_stats (NULL,NULL) AS [vfs]
        INNER JOIN sys.master_files AS [mf]
            ON [vfs].[database_id] = [mf].[database_id]
            AND [vfs].[file_id] = [mf].[file_id];";

    protected override void BeginProcessing()
    {
        WriteMessage(MessageLevel.Debug, Sql);
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
                results = PipelineValue(NestedCommand.InvokeScoped(this, ServerQueryScript, server, Sql));
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (RuntimeException ex)
            {
                StatementFault.Surface(this, ex, "Get-DbaIoLatency");
                continue;
            }
            foreach (object? row in EnumerateValue(results))
            {
                PSObject result = new PSObject();
                result.Properties.Add(new PSNoteProperty("ComputerName", SmoServerExtensions.GetComputerName(server)));
                result.Properties.Add(new PSNoteProperty("InstanceName", server.ServiceName));
                result.Properties.Add(new PSNoteProperty("SqlInstance", SmoServerExtensions.GetDomainInstanceName(server)));
                result.Properties.Add(new PSNoteProperty("DatabaseId", DotAccess(row, "database_id")));
                result.Properties.Add(new PSNoteProperty("DatabaseName", DotAccess(row, "DatabaseName")));
                result.Properties.Add(new PSNoteProperty("FileId", DotAccess(row, "file_id")));
                result.Properties.Add(new PSNoteProperty("PhysicalName", DotAccess(row, "physical_name")));
                result.Properties.Add(new PSNoteProperty("NumberOfReads", DotAccess(row, "num_of_reads")));
                result.Properties.Add(new PSNoteProperty("IoStallRead", DotAccess(row, "io_stall_read_ms")));
                result.Properties.Add(new PSNoteProperty("NumberOfwrites", DotAccess(row, "num_of_writes")));
                result.Properties.Add(new PSNoteProperty("IoStallWrite", DotAccess(row, "io_stall_write_ms")));
                result.Properties.Add(new PSNoteProperty("IoStall", DotAccess(row, "io_stall")));
                result.Properties.Add(new PSNoteProperty("NumberOfBytesRead", DotAccess(row, "num_of_bytes_read")));
                result.Properties.Add(new PSNoteProperty("NumberOfBytesWritten", DotAccess(row, "num_of_bytes_written")));
                result.Properties.Add(new PSNoteProperty("SampleMilliseconds", DotAccess(row, "sample_ms")));
                result.Properties.Add(new PSNoteProperty("SizeOnDiskBytes", DotAccess(row, "size_on_disk_bytes")));
                result.Properties.Add(new PSNoteProperty("FileHandle", DotAccess(row, "file_handle")));
                result.Properties.Add(new PSNoteProperty("ReadLatency", DotAccess(row, "ReadLatency")));
                result.Properties.Add(new PSNoteProperty("WriteLatency", DotAccess(row, "WriteLatency")));
                result.Properties.Add(new PSNoteProperty("Latency", DotAccess(row, "Latency")));
                result.Properties.Add(new PSNoteProperty("AvgBPerRead", DotAccess(row, "AvgBPerRead")));
                result.Properties.Add(new PSNoteProperty("AvgBPerWrite", DotAccess(row, "AvgBPerWrite")));
                result.Properties.Add(new PSNoteProperty("AvgBPerTransfer", DotAccess(row, "AvgBPerTransfer")));

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
                    StatementFault.Surface(this, ex, "Get-DbaIoLatency");
                }
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

    // PS: Select-DefaultView -ExcludeProperty $excludeColumns (the begin-block list).
    private const string SelectDefaultViewScript = """
param($__row)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($__row)
    Select-DefaultView -InputObject $__row -ExcludeProperty FileHandle, ReadLatency, WriteLatency, Latency, AvgBPerRead, AvgBPerWrite, AvgBPerTransfer
} $__row 3>&1
""";
}
