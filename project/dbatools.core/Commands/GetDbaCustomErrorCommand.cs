#nullable enable

using System.Management.Automation;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Parameter;
using Dataplat.Dbatools.Utility;
using Microsoft.SqlServer.Management.Smo;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves user-defined error messages from SQL Server instances for auditing and documentation.
/// Port of public/Get-DbaCustomError.ps1; surface pinned by migration/baselines/Get-DbaCustomError.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaCustomError")]
[OutputType(typeof(UserDefinedMessage))]
public sealed class GetDbaCustomErrorCommand : DbaInstanceCmdlet
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
            // PS: try { Connect-DbaInstance } catch { Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue }
            Server? server = ConnectInstance(instance, "Failure");
            if (server is null)
            {
                continue;
            }

            foreach (UserDefinedMessage customError in server.UserDefinedMessages)
            {
                // PS: Add-Member -Force NoteProperty ComputerName/InstanceName/SqlInstance from $customError.Parent
                PSObject wrapped = PSObject.AsPSObject(customError);
                ReplaceNoteProperty(wrapped, "ComputerName", SmoServerExtensions.GetComputerName(server));
                ReplaceNoteProperty(wrapped, "InstanceName", server.ServiceName);
                ReplaceNoteProperty(wrapped, "SqlInstance", SmoServerExtensions.GetDomainInstanceName(server));

                // PS: Select-DefaultView -Property ComputerName, InstanceName, SqlInstance, ID, Text, LanguageID, Language
                OutputHelper.SetDefaultDisplayPropertySet(wrapped,
                    "ComputerName", "InstanceName", "SqlInstance", "ID", "Text", "LanguageID", "Language");

                WriteObject(wrapped);
            }
        }
    }

    private static void ReplaceNoteProperty(PSObject wrapped, string name, object? value)
    {
        PSPropertyInfo? existing = wrapped.Properties[name];
        if (existing is PSNoteProperty)
        {
            wrapped.Properties.Remove(name);
        }
        wrapped.Properties.Add(new PSNoteProperty(name, value));
    }
}
