#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Management.Automation;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Parameter;
using Microsoft.SqlServer.Management.Smo;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Reports single-use plan-cache usage. Port of public/Get-DbaPlanCache.ps1 (W1-093).
/// The process body is three statement-conditional steps whose locals persist across
/// instances AND records (function scope): the un-tried $server.Query($sql) (a fault
/// surfaces the record and the NEXT statements run with the STALE $results), the
/// $size = [dbasize]($results.MB * 1024 * 1024) cast statement (same stale-keep), and
/// the Add-Member -Force + Select-DefaultView emission hop (a null $results feeds the
/// binder errors through 2>&1 re-emission, the W1-045/W1-092 law). Surface pinned by
/// migration/baselines/Get-DbaPlanCache.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaPlanCache")]
public sealed class GetDbaPlanCacheCommand : DbaInstanceCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public override DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public override PSCredential? SqlCredential { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    private const string Sql = @"SELECT SERVERPROPERTY('MachineName') AS ComputerName,
    ISNULL(SERVERPROPERTY('InstanceName'), 'MSSQLSERVER') AS InstanceName,
    SERVERPROPERTY('ServerName') AS SqlInstance, MB = SUM(CAST((CASE WHEN usecounts = 1 AND objtype IN ('Adhoc', 'Prepared') THEN size_in_bytes ELSE 0 END) AS DECIMAL(12, 2))) / 1024 / 1024,
    UseCount = SUM(CASE WHEN usecounts = 1 AND objtype IN ('Adhoc', 'Prepared') THEN 1 ELSE 0 END)
    FROM sys.dm_exec_cached_plans;";

    // PS: $results and $size are function-scope locals - a statement fault keeps the
    // STALE value from the prior instance/record.
    private object? _results;
    private object? _size;

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

            // PS: $results = $server.Query($sql) - NO try; a fault surfaces and the
            // next statements run with the stale value.
            try
            {
                _results = MethodReturnValue(NestedCommand.InvokeScoped(this, ServerQueryScript, server, Sql));
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (RuntimeException ex)
            {
                StatementFault.Surface(this, ex, "Get-DbaPlanCache");
            }

            // PS: $size = [dbasize]($results.MB * 1024 * 1024) - statement-conditional.
            try
            {
                _size = MethodReturnValue(NestedCommand.InvokeScoped(this, SizeScript, _results));
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (RuntimeException ex)
            {
                StatementFault.Surface(this, ex, "Get-DbaPlanCache");
            }

            // PS: Add-Member -Force ... ; Select-DefaultView -InputObject $results - the
            // binder errors of a null $results merge back 2>&1 and re-emit (W1-092 law).
            try
            {
                foreach (PSObject? item in NestedCommand.InvokeScoped(this, EmitScript, _results, _size))
                {
                    if (item?.BaseObject is ErrorRecord nestedError)
                    {
                        NestedCommand.RemoveDuplicateError(this, nestedError);
                        WriteError(nestedError);
                    }
                    else
                    {
                        WriteObject(item);
                    }
                }
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (RuntimeException ex)
            {
                StatementFault.Surface(this, ex, "Get-DbaPlanCache");
            }
        }
    }

    /// <summary>PS method-return shaping through the hop: none = null, the ETS real-null
    /// single element = null, one = the item, many = array.</summary>
    private static object? MethodReturnValue(Collection<PSObject> results)
    {
        if (results.Count == 0)
            return null;
        if (results.Count == 1)
        {
            if (results[0] is null)
                return null;
            return results[0];
        }
        object?[] array = new object?[results.Count];
        for (int n = 0; n < results.Count; n++)
            array[n] = results[n];
        return array;
    }

    // PS: $server.Query($query) on the engine (the W1-046 seam).
    private const string ServerQueryScript = """
param($server, $query)
$server.Query($query)
""";

    // PS: the [dbasize] size statement VERBATIM (member read, decimal math, cast).
    private const string SizeScript = """
param($results)
[dbasize]($results.MB * 1024 * 1024)
""";

    // PS: the Add-Member -Force note append + the Select-DefaultView emission VERBATIM.
    private const string EmitScript = """
param($results, $size)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($results, $size)
    Add-Member -Force -InputObject $results -MemberType NoteProperty -Name Size -Value $size

    Select-DefaultView -InputObject $results -Property ComputerName, InstanceName, SqlInstance, Size, UseCount
} $results $size 3>&1 2>&1
""";
}
