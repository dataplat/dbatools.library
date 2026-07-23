#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Removes the publisher role from an instance currently configured for replication publishing.
/// Port of public/Disable-DbaReplPublishing.ps1. The whole process body rides ONE VERBATIM
/// module hop per pipeline record: the foreach over SqlInstance, the Get-DbaReplServer lookup
/// (still module-scope PowerShell), the IsPublisher branch that removes the instance from the
/// distributor (SMO ReplicationServer.DistributionPublishers.Remove(Force)) then Refreshes and
/// emits, and the non-publisher Stop-Function -Continue guard (with the source's -ContinueLabel
/// main and $instance target preserved bug-for-bug). $PSCmdlet.ShouldProcess routes to the real
/// cmdlet via $__realCmdlet so -WhatIf/-Confirm and yes/no-to-all persist across records; in-hop
/// Stop-Function/Write-Message carry -FunctionName and read $EnableException from the hop param
/// scope; merged-back 2&gt;&amp;1 records re-emit via WriteError with the silent-$error
/// compensation. No cross-record state (each SqlInstance record is self-contained; every
/// Stop-Function is -Continue, which does not latch). Surface pinned by
/// migration/baselines/Disable-DbaReplPublishing.json.
/// </summary>
[Cmdlet(VerbsLifecycle.Disable, "DbaReplPublishing", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.High)]
public sealed class DisableDbaReplPublishingCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Forces removal of the publisher configuration without verifying the distributor connection status.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
            }
            else
            {
                WriteObject(item);
            }
        }, BodyScript,
        SqlInstance, SqlCredential, Force.ToBool(), EnableException.ToBool(), this,
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug"));
    }

    /// <summary>A bound common-parameter carrier for the hop scopes (Verbose+Debug forwarding).</summary>
    private object? BoundCommonParameter(string name)
    {
        if (MyInvocation.BoundParameters.TryGetValue(name, out object? value))
            return LanguagePrimitives.IsTrue(value);
        return null;
    }

    /// <summary>Removes the silent $error copy the nested pipeline bagged for a merged-back
    /// non-terminating record.</summary>
    private void RemoveHopErrorBookkeeping(ErrorRecord record)
    {
        try
        {
            if (SessionState.PSVariable.GetValue("Error") is not ArrayList errorList || errorList.Count == 0)
                return;
            if (errorList[0] is not ErrorRecord first)
                return;
            if (ReferenceEquals(first, record) || ReferenceEquals(first.Exception, record.Exception) ||
                string.Equals(first.Exception?.Message, record.Exception?.Message, StringComparison.Ordinal))
            {
                errorList.RemoveAt(0);
            }
        }
        catch
        {
            // best-effort bookkeeping
        }
    }

    // The whole process body VERBATIM in the dbatools module scope: the per-instance foreach,
    // the still-PS Get-DbaReplServer, the IsPublisher removal branch, and the non-publisher
    // guard. ShouldProcess routes to the real cmdlet; Stop-Function/Write-Message carry
    // -FunctionName.
    private const string BodyScript = """
param($SqlInstance, $SqlCredential, $Force, $EnableException, $__realCmdlet, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, $SqlCredential, [bool]$Force, $EnableException, $__realCmdlet)

    foreach ($instance in $SqlInstance) {

        $replServer = Get-DbaReplServer -SqlInstance $instance -SqlCredential $SqlCredential -EnableException:$EnableException

        Write-Message -Level Verbose -Message "Disabling and removing publishing for $instance" -FunctionName Disable-DbaReplPublishing -ModuleName "dbatools"

        if ($replServer.IsPublisher) {
            try {
                if ($__realCmdlet.ShouldProcess($instance, "Disabling and removing publishing on $instance")) {
                    $replServer.DistributionPublishers.Remove($Force)
                }

                $replServer.Refresh()
                $replServer

            } catch {
                Stop-Function -Message "Unable to disable replication publishing" -ErrorRecord $_ -Target $instance -Continue -FunctionName Disable-DbaReplPublishing
            }
        } else {
            Stop-Function -Message "$instance isn't currently enabled for publishing." -Continue -ContinueLabel main -Target $instance -Category ObjectNotFound -FunctionName Disable-DbaReplPublishing
        }
    }
} $SqlInstance $SqlCredential $Force $EnableException $__realCmdlet @__commonParameters 3>&1 2>&1
""";
}
