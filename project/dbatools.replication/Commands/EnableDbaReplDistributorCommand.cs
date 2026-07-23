#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Configures an instance as a replication distributor by creating the distribution database and
/// installing the distributor role. Port of public/Enable-DbaReplDistributor.ps1. The whole
/// process body rides ONE VERBATIM module hop per pipeline record: the foreach over SqlInstance,
/// the Get-DbaReplServer lookup (still module-scope PowerShell), and the ShouldProcess-gated
/// distributor install (SMO DistributionDatabase construction + ReplicationServer.InstallDistributor
/// + Refresh + emit) wrapped in the source's try / Stop-Function -Continue catch.
/// $PSCmdlet.ShouldProcess routes to the real cmdlet via $__realCmdlet so -WhatIf/-Confirm and
/// yes/no-to-all persist across records; in-hop Stop-Function/Write-Message carry -FunctionName and
/// read $EnableException from the hop param scope; merged-back 2&gt;&amp;1 records re-emit via
/// WriteError with the silent-$error compensation. No cross-record state (each SqlInstance record is
/// self-contained; the only Stop-Function is -Continue, which does not latch). Surface pinned by
/// migration/baselines/Enable-DbaReplDistributor.json.
/// </summary>
[Cmdlet(VerbsLifecycle.Enable, "DbaReplDistributor", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Medium)]
public sealed class EnableDbaReplDistributorCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The name of the distribution database to create. Defaults to 'distribution'.</summary>
    [Parameter(Position = 2)]
    public string DistributionDatabase { get; set; } = "distribution";

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                NestedCommand.RemoveDuplicateError(this, nestedError);
                WriteError(nestedError);
            }
            else
            {
                WriteObject(item);
            }
        }, BodyScript,
        SqlInstance, SqlCredential, DistributionDatabase, EnableException.ToBool(), this,
            NestedCommand.BoundCommonParameter(this, "Verbose"), NestedCommand.BoundCommonParameter(this, "Debug"));
    }

    // The whole process body VERBATIM in the dbatools module scope: the per-instance foreach, the
    // still-PS Get-DbaReplServer, and the ShouldProcess-gated distributor install. ShouldProcess
    // routes to the real cmdlet; Stop-Function/Write-Message carry -FunctionName.
    private const string BodyScript = """
param($SqlInstance, $SqlCredential, $DistributionDatabase, $EnableException, $__realCmdlet, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, $SqlCredential, $DistributionDatabase, $EnableException, $__realCmdlet)

    foreach ($instance in $SqlInstance) {

        $replServer = Get-DbaReplServer -SqlInstance $instance -SqlCredential $SqlCredential -EnableException:$EnableException

        Write-Message -Level Verbose -Message "Enabling replication distribution for $instance" -FunctionName Enable-DbaReplDistributor -ModuleName "dbatools"

        try {
            if ($__realCmdlet.ShouldProcess($instance, "Enabling distributor for $instance")) {
                $distributionDb = New-Object Microsoft.SqlServer.Replication.DistributionDatabase
                $distributionDb.ConnectionContext = $replServer.ConnectionContext
                $distributionDb.Name = $DistributionDatabase

                #TODO: lots more properties to add as params
                $replServer.InstallDistributor($null, $distributionDb)

                $replServer.Refresh()
                $replServer
            }
        } catch {
            Stop-Function -Message "Unable to enable replication distributor" -ErrorRecord $_ -Target $instance -Continue -FunctionName Enable-DbaReplDistributor
        }
    }
} $SqlInstance $SqlCredential $DistributionDatabase $EnableException $__realCmdlet @__commonParameters 3>&1 2>&1
""";
}
