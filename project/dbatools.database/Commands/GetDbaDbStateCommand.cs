#nullable enable

using System;
using System.Collections.Generic;
using System.Data;
using System.Management.Automation;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Parameter;
using Dataplat.Dbatools.Utility;
using Microsoft.SqlServer.Management.Smo;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves the read-write, status and access state of databases.
/// Port of public/Get-DbaDbState.ps1; surface pinned by migration/baselines/Get-DbaDbState.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbState")]
[OutputType(typeof(PSObject))]
public sealed class GetDbaDbStateCommand : DbaInstanceCmdlet
{
    private const string DbStatesQuery = @"SELECT
Name   = name,
Access = user_access_desc,
Status = state_desc,
RW     = CASE WHEN is_read_only = 0 THEN 'READ_WRITE' ELSE 'READ_ONLY' END
FROM sys.databases";

    private const string DbStatesQuery2000 = @"SELECT
Name   = name,
Access = DATABASEPROPERTYEX(name, 'UserAccess'),
Status = DATABASEPROPERTYEX(name, 'Status'),
RW     = DATABASEPROPERTYEX(name, 'Updateability')
FROM sys.databases";

    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public override DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public override PSCredential? SqlCredential { get; set; }

    /// <summary>Returns state for only the named databases.</summary>
    [Parameter(Position = 2)]
    public object[]? Database { get; set; }

    /// <summary>Excludes the named databases.</summary>
    [Parameter(Position = 3)]
    public object[]? ExcludeDatabase { get; set; }

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        foreach (DbaInstanceParameter instance in SqlInstance)
        {
            // PS: try { Connect-DbaInstance } catch { Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue }
            Server? server = ConnectInstance(instance, "Failure");
            if (server is null)
            {
                continue;
            }

            DataTable dbStates = server.ConnectionContext.ExecuteWithResults(
                server.VersionMajor == 8 ? DbStatesQuery2000 : DbStatesQuery).Tables[0];

            // PS: "normal" hashtable doesn't account for case sensitivity
            Dictionary<string, DataRow> dbStatesHash = new(StringComparer.Ordinal);
            foreach (DataRow stateRow in dbStates.Rows)
            {
                dbStatesHash[(string)stateRow["Name"]] = stateRow;
            }

            string[] systemDatabases = { "master", "model", "msdb", "tempdb", "distribution" };

            foreach (DataRow db in dbStates.Rows)
            {
                string name = (string)db["Name"];
                bool isSystem = false;
                foreach (string sysDb in systemDatabases)
                {
                    if (string.Equals(sysDb, name, StringComparison.OrdinalIgnoreCase))
                    {
                        isSystem = true;
                        break;
                    }
                }
                if (isSystem)
                {
                    continue;
                }
                if (FilterHelper.IsActive(Database) && !ContainsValue(Database!, name))
                {
                    continue;
                }
                if (FilterHelper.IsActive(ExcludeDatabase) && ContainsValue(ExcludeDatabase!, name))
                {
                    continue;
                }

                DataRow dbStatus = dbStatesHash[name];
                PSObject result = new();
                // PS emission order preserved: SqlInstance ($server.Name), InstanceName, ComputerName first
                result.Properties.Add(new PSNoteProperty("SqlInstance", server.Name));
                result.Properties.Add(new PSNoteProperty("InstanceName", server.ServiceName));
                result.Properties.Add(new PSNoteProperty("ComputerName", SmoServerExtensions.GetComputerName(server)));
                result.Properties.Add(new PSNoteProperty("DatabaseName", name));
                result.Properties.Add(new PSNoteProperty("RW", dbStatus["RW"]));
                result.Properties.Add(new PSNoteProperty("Status", dbStatus["Status"]));
                result.Properties.Add(new PSNoteProperty("Access", dbStatus["Access"]));
                result.Properties.Add(new PSNoteProperty("Database", server.Databases[name]));

                // PS: Select-DefaultView -ExcludeProperty Database
                OutputHelper.SetDefaultDisplayPropertySetExcluding(result, new[] { "Database" });

                WriteObject(result);
            }
        }
    }

    private static bool ContainsValue(object[] values, string? name)
    {
        foreach (object value in values)
        {
            if (value is not null && string.Equals(value.ToString(), name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
