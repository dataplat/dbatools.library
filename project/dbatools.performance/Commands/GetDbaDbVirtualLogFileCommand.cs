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
/// Lists virtual log file entries per database via DBCC LOGINFO. Port of
/// public/Get-DbaDbVirtualLogFile.ps1 (W1-071). The database walk filters IsAccessible
/// truthiness, the VALUE-truthy -Database/-ExcludeDatabase gates (W1-065 law) and the
/// !$IncludeSystemDBs system filter; per db the Database.Query ETS hop runs INSIDE the
/// function's own try with the row loop (DotAccess raw column reads incl. the CreateLSN
/// case difference) - any fault lands in the catch's Stop-Function -Continue. Surface
/// pinned by migration/baselines/Get-DbaDbVirtualLogFile.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbVirtualLogFile")]
[OutputType(typeof(System.Collections.ArrayList))]
public sealed class GetDbaDbVirtualLogFileCommand : DbaInstanceCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public override DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public override PSCredential? SqlCredential { get; set; }

    /// <summary>The databases to report on.</summary>
    [Parameter(Position = 2)]
    public object[]? Database { get; set; }

    /// <summary>The databases to skip.</summary>
    [Parameter(Position = 3)]
    public object[]? ExcludeDatabase { get; set; }

    /// <summary>Include the system databases (excluded by default).</summary>
    [Parameter]
    public SwitchParameter IncludeSystemDBs { get; set; }

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
                if (!IncludeSystemDBs.IsPresent && candidate.IsSystemObject)
                    continue;
                dbs.Add(candidate);
            }

            foreach (Microsoft.SqlServer.Management.Smo.Database db in dbs)
            {
                try
                {
                    Collection<PSObject> data = NestedCommand.InvokeScoped(this, DatabaseQueryScript, db, "DBCC LOGINFO");

                    foreach (PSObject? d in data)
                    {
                        if (d is null)
                            continue;
                        PSObject result = new PSObject();
                        result.Properties.Add(new PSNoteProperty("ComputerName", SmoServerExtensions.GetComputerName(server)));
                        result.Properties.Add(new PSNoteProperty("InstanceName", server.ServiceName));
                        result.Properties.Add(new PSNoteProperty("SqlInstance", SmoServerExtensions.GetDomainInstanceName(server)));
                        result.Properties.Add(new PSNoteProperty("Database", db.Name));
                        result.Properties.Add(new PSNoteProperty("RecoveryUnitId", DotAccess(d, "RecoveryUnitId")));
                        result.Properties.Add(new PSNoteProperty("FileId", DotAccess(d, "FileId")));
                        result.Properties.Add(new PSNoteProperty("FileSize", DotAccess(d, "FileSize")));
                        result.Properties.Add(new PSNoteProperty("StartOffset", DotAccess(d, "StartOffset")));
                        result.Properties.Add(new PSNoteProperty("FSeqNo", DotAccess(d, "FSeqNo")));
                        result.Properties.Add(new PSNoteProperty("Status", DotAccess(d, "Status")));
                        result.Properties.Add(new PSNoteProperty("Parity", DotAccess(d, "Parity")));
                        result.Properties.Add(new PSNoteProperty("CreateLsn", DotAccess(d, "CreateLSN")));
                        WriteObject(result);
                    }
                }
                catch (PipelineStoppedException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    StopFunction("Unable to query " + db.Name + " on " + PsText(instance) + ".", target: db, errorRecord: StatementFault.Record(ex, "Get-DbaDbVirtualLogFile"), continueLoop: true);
                    continue;
                }
            }
        }
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

    /// <summary>PS -in over the filter array (elementwise -eq with the ELEMENT as the
    /// left operand, so a non-string filter value drives the coercion - codex W1-110 r1
    /// class fix, retrofitted: -Database 1 matches a database named "01" exactly like
    /// the function did).</summary>
    private static bool MatchesAny(string name, object[] values)
    {
        return PsOps.In(name, values);
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

    // PS: $db.Query($query) - the Database-scoped ETS call (the W1-052 seam).
    private const string DatabaseQueryScript = """
param($db, $query)
$db.Query($query)
""";
}
