#nullable enable

using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Configuration;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Adds a replica to an availability group, creating and starting the mirroring endpoint
/// when needed, applying the replica property matrix and granting cluster and endpoint
/// permissions. Port of public/Add-DbaAgReplica.ps1; surface pinned by
/// migration/baselines/Add-DbaAgReplica.json.
/// </summary>
[Cmdlet(VerbsCommon.Add, "DbaAgReplica", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low)]
public sealed partial class AddDbaAgReplicaCommand : DbaBaseCmdlet
{
    /// <summary>The SQL Server instances to add as replicas.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instances using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The replica name; defaults to the instance name.</summary>
    [Parameter(Position = 2)]
    public string? Name { get; set; }

    /// <summary>The cluster type backing the availability group.</summary>
    [Parameter(Position = 3)]
    [ValidateSet("Wsfc", "External", "None")]
    public string ClusterType { get; set; } = ConfigValueOrFallback("AvailabilityGroups.Default.ClusterType", "Wsfc");

    /// <summary>The availability mode of the replica.</summary>
    [Parameter(Position = 4)]
    [ValidateSet("AsynchronousCommit", "SynchronousCommit")]
    public string AvailabilityMode { get; set; } = "SynchronousCommit";

    /// <summary>The failover mode of the replica.</summary>
    [Parameter(Position = 5)]
    [ValidateSet("Automatic", "Manual", "External")]
    public string FailoverMode { get; set; } = "Automatic";

    /// <summary>The backup priority of the replica.</summary>
    [Parameter(Position = 6)]
    public int BackupPriority { get; set; } = 50;

    /// <summary>Connection mode when the replica is primary.</summary>
    [Parameter(Position = 7)]
    [ValidateSet("AllowAllConnections", "AllowReadWriteConnections")]
    public string ConnectionModeInPrimaryRole { get; set; } = "AllowAllConnections";

    /// <summary>Connection mode when the replica is secondary; friendly aliases are normalized in the body.</summary>
    [Parameter(Position = 8)]
    [ValidateSet("AllowNoConnections", "AllowReadIntentConnectionsOnly", "AllowAllConnections", "No", "Read-intent only", "Yes")]
    public string ConnectionModeInSecondaryRole { get; set; } = ConfigValueOrFallback("AvailabilityGroups.Default.ConnectionModeInSecondaryRole", "AllowNoConnections");

    /// <summary>The seeding mode of the replica.</summary>
    [Parameter(Position = 9)]
    [ValidateSet("Automatic", "Manual")]
    public string? SeedingMode { get; set; }

    /// <summary>The mirroring endpoint name to use or create.</summary>
    [Parameter(Position = 10)]
    public string? Endpoint { get; set; }

    /// <summary>Explicit endpoint URLs, one per instance.</summary>
    [Parameter(Position = 11)]
    public string[]? EndpointUrl { get; set; }

    /// <summary>Returns the uncreated replica object for further customization.</summary>
    [Parameter]
    public SwitchParameter Passthru { get; set; }

    /// <summary>The read-only routing list for the replica.</summary>
    [Parameter(Position = 12)]
    public string[]? ReadOnlyRoutingList { get; set; }

    /// <summary>The read-only routing connection URL for the replica.</summary>
    [Parameter(Position = 13)]
    public string? ReadonlyRoutingConnectionUrl { get; set; }

    /// <summary>Certificate securing the mirroring endpoint.</summary>
    [Parameter(Position = 14)]
    public string? Certificate { get; set; }

    /// <summary>Ensures the AlwaysOn_health extended events session is configured and running.</summary>
    [Parameter]
    public SwitchParameter ConfigureXESession { get; set; }

    /// <summary>The replica session timeout in seconds.</summary>
    [Parameter(Position = 15)]
    public int SessionTimeout { get; set; }

    /// <summary>The availability group object the replica is added to.</summary>
    [Parameter(Position = 16, ValueFromPipeline = true, Mandatory = true)]
    public Microsoft.SqlServer.Management.Smo.AvailabilityGroup? InputObject { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    private Hashtable? _state;

    // The source evaluates two parameter defaults through Get-DbatoolsConfigValue at bind
    // time; the property initializers read the same configuration store the compiled
    // config commands already use, with the source's fallbacks.
    private static string ConfigValueOrFallback(string fullName, string fallback)
    {
        if (ConfigurationHost.Configurations.TryGetValue(fullName, out Config? config) && config?.Value is not null)
        {
            return LanguagePrimitives.ConvertTo<string>(config.Value);
        }
        return fallback;
    }

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, SqlCredential, Name, ClusterType, AvailabilityMode, FailoverMode,
            BackupPriority, ConnectionModeInPrimaryRole, ConnectionModeInSecondaryRole,
            SeedingMode, Endpoint, EndpointUrl, Passthru.ToBool(), ReadOnlyRoutingList,
            ReadonlyRoutingConnectionUrl, Certificate, ConfigureXESession.ToBool(),
            SessionTimeout, InputObject, EnableException.ToBool(), _state, this,
            BoundFlag("Name"),
            BoundCommonParameter("WhatIf"), BoundCommonParameter("Confirm"),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            Hashtable? sentinel = item?.BaseObject as Hashtable;
            if (sentinel is not null && sentinel.ContainsKey("__w4003State"))
            {
                _state = sentinel["__w4003State"] as Hashtable;
                continue;
            }
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
                continue;
            }
            WriteObject(item);
        }
    }

    private bool BoundFlag(string name)
    {
        return MyInvocation.BoundParameters.ContainsKey(name);
    }

    private object? BoundCommonParameter(string name)
    {
        if (MyInvocation.BoundParameters.TryGetValue(name, out object? value))
        {
            return LanguagePrimitives.IsTrue(value);
        }
        return null;
    }

    private void RemoveHopErrorBookkeeping(ErrorRecord record)
    {
        try
        {
            if (SessionState.PSVariable.GetValue("Error") is not ArrayList errorList || errorList.Count == 0)
                return;
            if (errorList[0] is not ErrorRecord first)
                return;
            if (ReferenceEquals(first, record) || ReferenceEquals(first.Exception, record.Exception) ||
                string.Equals(first.Exception?.Message, record.Exception?.Message, System.StringComparison.Ordinal))
            {
                errorList.RemoveAt(0);
            }
        }
        catch
        {
            // Best-effort bookkeeping only.
        }
    }
}
