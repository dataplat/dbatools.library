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
/// Lists SQL Server memory dumps. Port of public/Get-DbaDump.ps1 (W1-073). The version
/// gate keeps the 2008R2 exception (versionMajor 10 + versionMinor 50) with a
/// record-less Stop-Function -Continue; the dump query and its row loop sit INSIDE the
/// function's own try (an empty resultset's real $null enumerates to nothing; a
/// [dbasize] cast fault - DBNull size - lands in the catch's Stop-Function -Continue
/// targeting the SERVER). Surface pinned by migration/baselines/Get-DbaDump.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDump")]
public sealed class GetDbaDumpCommand : DbaInstanceCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public override DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public override PSCredential? SqlCredential { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // PS begin block: the dump query, verbatim.
    private const string Sql = "SELECT filename, creation_time, size_in_bytes FROM sys.dm_server_memory_dumps";

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

            if (server.VersionMajor < 11 && !(server.VersionMajor == 10 && server.VersionMinor == 50))
            {
                StopFunction("This function does not support versions lower than SQL Server 2008 R2 (v10.50). Skipping server '" + PsText(instance) + "'", continueLoop: true);
                continue;
            }

            try
            {
                object? results = PipelineValue(NestedCommand.InvokeScoped(this, ServerQueryScript, server, Sql));
                foreach (object? item in EnumerateValue(results))
                {
                    PSObject result = new PSObject();
                    result.Properties.Add(new PSNoteProperty("ComputerName", SmoServerExtensions.GetComputerName(server)));
                    result.Properties.Add(new PSNoteProperty("InstanceName", server.ServiceName));
                    result.Properties.Add(new PSNoteProperty("SqlInstance", SmoServerExtensions.GetDomainInstanceName(server)));
                    result.Properties.Add(new PSNoteProperty("FileName", DotAccess(item, "filename")));
                    result.Properties.Add(new PSNoteProperty("CreationTime", DotAccess(item, "creation_time")));
                    result.Properties.Add(new PSNoteProperty("Size", DbaSize(DotAccess(item, "size_in_bytes"))));
                    WriteObject(result);
                }
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                StopFunction("Issue collecting data on " + PsText(server), target: server, errorRecord: StatementFault.Record(ex, "Get-DbaDump"), continueLoop: true);
                continue;
            }
        }
    }

    /// <summary>PS [dbasize] cast via the engine's conversion machinery (null stays
    /// null; a DBNull faults to the function's own catch).</summary>
    private static object? DbaSize(object? value)
    {
        object? unwrapped = value is PSObject pso ? pso.BaseObject : value;
        if (unwrapped is null)
            return null;
        return LanguagePrimitives.ConvertTo(unwrapped, typeof(Size), CultureInfo.InvariantCulture);
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
}
