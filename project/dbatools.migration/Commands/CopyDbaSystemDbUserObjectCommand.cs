#nullable enable

using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Copies user-created objects - schemas, tables, views, procedures, functions, rules and triggers -
/// out of the master, model and msdb system databases between instances. Port of
/// public/Copy-DbaSystemDbUserObject.ps1. The workflow rides one module-scoped PowerShell hop: the
/// body leans on Get-DbaModule, Test-SqlSa and the private Get-ErrorMessage and Select-DefaultView,
/// and on the SMO Transfer scripting engine, so those semantics stay where they were. The compiled
/// cmdlet supplies the real ShouldProcess runtime. Surface pinned by
/// migration/baselines/Copy-DbaSystemDbUserObject.json - the source declares no
/// DefaultParameterSetName, so this cmdlet must not invent one.
/// </summary>
[Cmdlet(VerbsCommon.Copy, "DbaSystemDbUserObject", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Medium)]
public sealed partial class CopyDbaSystemDbUserObjectCommand : DbaBaseCmdlet
{
    /// <summary>The source SQL Server instance holding the user objects. Requires sysadmin.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    public DbaInstanceParameter Source { get; set; } = null!;

    /// <summary>Alternative credential for the source instance.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SourceSqlCredential { get; set; }

    /// <summary>The destination SQL Server instances. Requires sysadmin on each.</summary>
    [Parameter(Mandatory = true, Position = 2)]
    public DbaInstanceParameter[] Destination { get; set; } = null!;

    /// <summary>Alternative credential for the destination instances.</summary>
    [Parameter(Position = 3)]
    public PSCredential? DestinationSqlCredential { get; set; }

    /// <summary>Drop objects that already exist on the destination before recreating them. Ignored in Classic mode.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    /// <summary>Use the legacy bulk SMO Transfer path instead of the per-object default. Emits no output.</summary>
    [Parameter]
    public SwitchParameter Classic { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.
}
