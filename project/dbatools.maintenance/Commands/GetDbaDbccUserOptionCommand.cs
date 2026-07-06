#nullable enable

using System;
using System.Data;
using System.Management.Automation;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;
using Dataplat.Dbatools.Utility;
using Microsoft.SqlServer.Management.Smo;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves the session SET options reported by DBCC USEROPTIONS.
/// Port of public/Get-DbaDbccUserOption.ps1; surface pinned by migration/baselines/Get-DbaDbccUserOption.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbccUserOption")]
[OutputType(typeof(PSObject))]
public sealed class GetDbaDbccUserOptionCommand : DbaInstanceCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public override DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public override PSCredential? SqlCredential { get; set; }

    /// <summary>Returns only the named options.</summary>
    [Parameter(Position = 2)]
    [ValidateSet("ansi_null_dflt_on", "ansi_nulls", "ansi_padding", "ansi_warnings", "arithabort", "concat_null_yields_null", "datefirst", "dateformat", "isolation level", "language", "lock_timeout", "quoted_identifier", "textsize")]
    public string[]? Option { get; set; }

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        const string query = "DBCC USEROPTIONS WITH NO_INFOMSGS";

        foreach (DbaInstanceParameter instance in SqlInstance)
        {
            // PS: try { Connect-DbaInstance } catch { Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue }
            Server? server = ConnectInstance(instance, "Failure");
            if (server is null)
            {
                continue;
            }

            DataTable results;
            try
            {
                WriteMessage(MessageLevel.Verbose, string.Format("Query to run: {0}", query));
                results = server.ConnectionContext.ExecuteWithResults(query).Tables[0];
            }
            catch (Exception ex)
            {
                StopFunction(string.Format("Failure running {0} against {1}", query, instance), target: server, errorRecord: new ErrorRecord(ex, "dbatools_Get-DbaDbccUserOption", ErrorCategory.NotSpecified, server), continueLoop: true);
                continue;
            }

            foreach (DataRow row in results.Rows)
            {
                if (!TestBound(nameof(Option)) || ContainsName(Option, row[0]?.ToString()))
                {
                    PSObject result = new();
                    result.Properties.Add(new PSNoteProperty("ComputerName", SmoServerExtensions.GetComputerName(server)));
                    result.Properties.Add(new PSNoteProperty("InstanceName", server.ServiceName));
                    result.Properties.Add(new PSNoteProperty("SqlInstance", SmoServerExtensions.GetDomainInstanceName(server)));
                    result.Properties.Add(new PSNoteProperty("Option", row[0]));
                    result.Properties.Add(new PSNoteProperty("Value", row[1]));
                    WriteObject(result);
                }
            }
        }
    }

    private static bool ContainsName(string[]? values, string? name)
    {
        foreach (string value in values ?? Array.Empty<string>())
        {
            if (string.Equals(value, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
