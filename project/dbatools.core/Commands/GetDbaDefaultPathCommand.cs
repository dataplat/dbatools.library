#nullable enable

using System;
using System.Data;
using System.IO;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;
using Dataplat.Dbatools.Utility;
using Microsoft.SqlServer.Management.Smo;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves the default data, log, backup and error log paths of SQL Server instances.
/// Port of public/Get-DbaDefaultPath.ps1; surface pinned by migration/baselines/Get-DbaDefaultPath.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDefaultPath")]
[OutputType(typeof(PSObject))]
public sealed class GetDbaDefaultPathCommand : DbaInstanceCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public override DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public override PSCredential? SqlCredential { get; set; }

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        foreach (DbaInstanceParameter instance in SqlInstance)
        {
            // PS: try { Connect-DbaInstance -AzureUnsupported } catch { Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue }
            Server? server = ConnectInstance(instance, "Failure", azureUnsupported: true);
            if (server is null)
            {
                continue;
            }

            object? dataPath = server.DefaultFile;
            if (IsEmptyString(dataPath))
            {
                // PS: $server.Query("SELECT SERVERPROPERTY('InstanceDefaultDataPath') AS Data").Data
                dataPath = QueryScalar(server, "SELECT SERVERPROPERTY('InstanceDefaultDataPath') AS Data", "Data");
            }

            if (dataPath == DBNull.Value || dataPath is null || IsEmptyString(dataPath))
            {
                // PS: Split-Path (Get-DbaDatabase -SqlInstance $server -Database model).FileGroups[0].Files[0].FileName
                // Inline SMO lookup replaces the Get-DbaDatabase call (modules.md section 4.3 pattern).
                dataPath = Path.GetDirectoryName(server.Databases["model"].FileGroups[0].Files[0].FileName);
            }

            if (IsEmptyString(dataPath))
            {
                dataPath = server.Information.MasterDBPath;
            }

            object? logPath = server.DefaultLog;

            if (IsEmptyString(logPath))
            {
                // PS: $server.Query("SELECT SERVERPROPERTY('InstanceDefaultLogPath') AS Log").Log
                logPath = QueryScalar(server, "SELECT SERVERPROPERTY('InstanceDefaultLogPath') AS Log", "Log");
            }

            if (logPath == DBNull.Value || logPath is null || IsEmptyString(logPath))
            {
                // PS: Split-Path (Get-DbaDatabase -SqlInstance $server -Database model).LogFiles.FileName
                logPath = Path.GetDirectoryName(server.Databases["model"].LogFiles[0].FileName);
            }

            if (IsEmptyString(logPath))
            {
                logPath = server.Information.MasterDBLogPath;
            }

            string dataPathText = Convert.ToString(dataPath)!.Trim().TrimEnd('\\');
            string logPathText = Convert.ToString(logPath)!.Trim().TrimEnd('\\');

            PSObject result = new();
            OutputHelper.AddInstanceProperties(result, server);
            result.Properties.Add(new PSNoteProperty("Data", dataPathText));
            result.Properties.Add(new PSNoteProperty("Log", logPathText));
            result.Properties.Add(new PSNoteProperty("Backup", server.BackupDirectory));
            result.Properties.Add(new PSNoteProperty("ErrorLog", server.ErrorLogPath));

            // The PS source emits the bare PSCustomObject with no Select-DefaultView call,
            // so no display set is attached here either.
            WriteObject(result);
        }
    }

    private static bool IsEmptyString(object? value)
    {
        // PS: $value.Length -eq 0 - null deliberately does NOT count as empty (no strict
        // mode), so only an actual zero-length string takes the fallback branch.
        return value is string { Length: 0 };
    }

    private object? QueryScalar(Server server, string sql, string columnName)
    {
        // PS: $server.Query(...) is the dbatools ETS script method over
        // ConnectionContext.ExecuteWithResults; same T-SQL, same first-row column read.
        DataSet resultSet = server.ConnectionContext.ExecuteWithResults(sql);
        if (resultSet.Tables.Count == 0 || resultSet.Tables[0].Rows.Count == 0)
        {
            return null;
        }
        return resultSet.Tables[0].Rows[0][columnName];
    }
}
