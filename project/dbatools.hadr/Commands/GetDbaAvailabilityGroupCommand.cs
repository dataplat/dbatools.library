#nullable enable

using System;
using System.Management.Automation;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Parameter;
using Dataplat.Dbatools.Utility;
using Microsoft.SqlServer.Management.Smo;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves Availability Groups from SQL Server instances with HADR enabled.
/// Port of public/Get-DbaAvailabilityGroup.ps1; surface pinned by migration/baselines/Get-DbaAvailabilityGroup.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaAvailabilityGroup")]
[OutputType(typeof(AvailabilityGroup))]
public sealed class GetDbaAvailabilityGroupCommand : DbaInstanceCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public override DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public override PSCredential? SqlCredential { get; set; }

    /// <summary>Returns only the named availability groups.</summary>
    [Parameter(Position = 2)]
    public string[]? AvailabilityGroup { get; set; }

    /// <summary>Returns a slim object indicating whether this replica is primary.</summary>
    [Parameter]
    public SwitchParameter IsPrimary { get; set; }

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        foreach (DbaInstanceParameter instance in SqlInstance)
        {
            // PS: try { Connect-DbaInstance -MinimumVersion 11 } catch { Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue }
            Server? server = ConnectInstance(instance, "Failure", minimumVersion: 11);
            if (server is null)
            {
                continue;
            }

            if (!server.IsHadrEnabled)
            {
                StopFunction(string.Format("Availability Group (HADR) is not configured for the instance: {0}.", instance), target: instance, continueLoop: true);
                continue;
            }

            foreach (AvailabilityGroup ag in server.AvailabilityGroups)
            {
                if (FilterHelper.IsActive(AvailabilityGroup) && !ContainsName(AvailabilityGroup!, ag.Name))
                {
                    continue;
                }

                // Refresh list of databases to fix #9094
                ag.AvailabilityDatabases.Refresh();

                PSObject wrapped = PSObject.AsPSObject(ag);
                ReplaceNoteProperty(wrapped, "ComputerName", SmoServerExtensions.GetComputerName(server));
                ReplaceNoteProperty(wrapped, "InstanceName", server.ServiceName);
                ReplaceNoteProperty(wrapped, "SqlInstance", SmoServerExtensions.GetDomainInstanceName(server));

                if (IsPrimary.ToBool())
                {
                    ReplaceNoteProperty(wrapped, "IsPrimary", ag.LocalReplicaRole == AvailabilityReplicaRole.Primary);
                    // PS: 'Name as AvailabilityGroup'
                    AddAliasIfMissing(wrapped, "AvailabilityGroup", "Name");
                    OutputHelper.SetDefaultDisplayPropertySet(wrapped,
                        "ComputerName", "InstanceName", "SqlInstance", "AvailabilityGroup", "IsPrimary");
                }
                else
                {
                    // PS: 'Name as AvailabilityGroup', 'PrimaryReplicaServerName as PrimaryReplica'
                    AddAliasIfMissing(wrapped, "AvailabilityGroup", "Name");
                    AddAliasIfMissing(wrapped, "PrimaryReplica", "PrimaryReplicaServerName");
                    OutputHelper.SetDefaultDisplayPropertySet(wrapped,
                        "ComputerName", "InstanceName", "SqlInstance", "LocalReplicaRole", "AvailabilityGroup",
                        "PrimaryReplica", "ClusterType", "DtcSupportEnabled", "AutomatedBackupPreference",
                        "AvailabilityReplicas", "AvailabilityDatabases", "AvailabilityGroupListeners");
                }

                WriteObject(wrapped);
            }
        }
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

    private static void AddAliasIfMissing(PSObject wrapped, string aliasName, string referencedName)
    {
        if (wrapped.Properties[aliasName] is null)
        {
            OutputHelper.AddAliasProperty(wrapped, aliasName, referencedName);
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
