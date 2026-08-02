#nullable enable

using System;
using System.Collections.Generic;
using System.Data;
using System.Management.Automation;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Parameter;
using Dataplat.Dbatools.Utility;
using Microsoft.SqlServer.Management.Smo;
using Microsoft.SqlServer.Management.Smo.Mail;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves Database Mail profiles from SQL Server instances.
/// Port of public/Get-DbaDbMailProfile.ps1; surface pinned by migration/baselines/Get-DbaDbMailProfile.json.
/// The Get-DbaDbMail resolution runs as an inline SMO read (server.Mail with the same
/// instance decorations the PS helper applies).
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbMailProfile")]
[OutputType(typeof(MailProfile))]
public sealed class GetDbaDbMailProfileCommand : DbaInstanceCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Position = 0)]
    public override DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public override PSCredential? SqlCredential { get; set; }

    /// <summary>Returns only the named profiles.</summary>
    [Parameter(Position = 2)]
    public string[]? Profile { get; set; }

    /// <summary>Excludes the named profiles.</summary>
    [Parameter(Position = 3)]
    public string[]? ExcludeProfile { get; set; }

    /// <summary>SqlMail objects piped in from Get-DbaDbMail.</summary>
    [Parameter(ValueFromPipeline = true, Position = 4)]
    public SqlMail[]? InputObject { get; set; }

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        List<SqlMail> mailServers = new();
        if (InputObject is { Length: > 0 })
        {
            mailServers.AddRange(InputObject);
        }

        foreach (DbaInstanceParameter instance in SqlInstance ?? Array.Empty<DbaInstanceParameter>())
        {
            // PS: $InputObject += Get-DbaDbMail -SqlInstance $instance -SqlCredential $SqlCredential
            Server? server = ConnectInstance(instance, "Failure");
            if (server is null)
            {
                continue;
            }
            SqlMail mail = server.Mail;
            PSObject wrappedMail = PSObject.AsPSObject(mail);
            ReplaceNoteProperty(wrappedMail, "ComputerName", SmoServerExtensions.GetComputerName(server));
            ReplaceNoteProperty(wrappedMail, "InstanceName", server.ServiceName);
            ReplaceNoteProperty(wrappedMail, "SqlInstance", SmoServerExtensions.GetDomainInstanceName(server));
            mailServers.Add(mail);
        }

        if (mailServers.Count == 0)
        {
            StopFunction("No servers to process");
            return;
        }

        foreach (SqlMail mailserver in mailServers)
        {
            try
            {
                foreach (MailProfile prof in mailserver.Profiles)
                {
                    if (FilterHelper.IsActive(Profile) && !ContainsName(Profile!, prof.Name))
                    {
                        continue;
                    }
                    if (FilterHelper.IsActive(ExcludeProfile) && ContainsName(ExcludeProfile!, prof.Name))
                    {
                        continue;
                    }

                    PSObject wrapped = PSObject.AsPSObject(prof);
                    ReplaceNoteProperty(wrapped, "ComputerName", SmoServerExtensions.GetPSProperty(mailserver, "ComputerName"));
                    ReplaceNoteProperty(wrapped, "InstanceName", SmoServerExtensions.GetPSProperty(mailserver, "InstanceName"));
                    ReplaceNoteProperty(wrapped, "SqlInstance", SmoServerExtensions.GetPSProperty(mailserver, "SqlInstance"));
                    ReplaceNoteProperty(wrapped, "MailAccount", EnumAccountNames(prof));

                    OutputHelper.SetDefaultDisplayPropertySet(wrapped,
                        "ComputerName", "InstanceName", "SqlInstance", "ID", "Name", "Description",
                        "ForceDeleteForActiveProfiles", "IsBusyProfile", "MailAccount");

                    WriteObject(wrapped);
                }
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                StopFunction("Failure", errorRecord: new ErrorRecord(ex, "dbatools_Get-DbaDbMailProfile", ErrorCategory.NotSpecified, mailserver), continueLoop: true);
                continue;
            }
        }
    }

    private static object? EnumAccountNames(MailProfile prof)
    {
        // PS: $prof.EnumAccounts().AccountName - member enumeration over the DataTable rows;
        // a single account surfaces as the bare value, several as an array.
        DataTable accounts = prof.EnumAccounts();
        List<object> names = new();
        foreach (DataRow row in accounts.Rows)
        {
            names.Add(row["AccountName"]);
        }
        if (names.Count == 0)
        {
            return null;
        }
        if (names.Count == 1)
        {
            return names[0];
        }
        return names.ToArray();
    }

    private static bool ContainsName(string[] values, string? name)
    {
        foreach (string value in values)
        {
            if (string.Equals(value, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
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
