#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Removes internal or external Resource Governor pools. Port of
/// public/Remove-DbaRgResourcePool.ps1 (W1-116). The complete process body rides one
/// module-scoped PowerShell hop so its no-input guards, source type-inference quirk,
/// per-instance connection/append loop, nested Stop-Function continues, pool Drop and
/// reconfigure ShouldProcess calls, SkipReconfigure warning, outer catch behavior, and
/// EnableException flow retain their original PowerShell semantics. The compiled cmdlet
/// supplies the real ShouldProcess implementation and carries whether -Type was explicitly
/// bound because the nested module scope cannot inspect the caller's PSBoundParameters.
/// Surface pinned by migration/baselines/Remove-DbaRgResourcePool.json.
/// </summary>
[Cmdlet(VerbsCommon.Remove, "DbaRgResourcePool", SupportsShouldProcess = true,
    ConfirmImpact = ConfirmImpact.Low, DefaultParameterSetName = "Default")]
public sealed class RemoveDbaRgResourcePoolCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(ValueFromPipeline = true, ValueFromPipelineByPropertyName = true, Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(ValueFromPipelineByPropertyName = true, Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The resource-pool names to remove.</summary>
    [Parameter(Position = 2)]
    public string[]? ResourcePool { get; set; }

    /// <summary>Internal or External resource pool.</summary>
    [Parameter(Position = 3)]
    [ValidateSet("Internal", "External")]
    public string Type { get; set; } = "Internal";

    /// <summary>Skip Resource Governor reconfiguration after removal.</summary>
    [Parameter]
    public SwitchParameter SkipReconfigure { get; set; }

    /// <summary>Resource-pool objects piped from Get-DbaRgResourcePool.</summary>
    [Parameter(ValueFromPipeline = true, Position = 4)]
    public object[]? InputObject { get; set; }

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
            SqlInstance, SqlCredential, ResourcePool, Type, SkipReconfigure.ToBool(),
            InputObject, TestBound("Type"), EnableException.ToBool(), this, BoundVerbose());
    }

    private object? BoundVerbose()
    {
        if (MyInvocation.BoundParameters.TryGetValue("Verbose", out object? verbose))
            return LanguagePrimitives.IsTrue(verbose);
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
                string.Equals(first.Exception?.Message, record.Exception?.Message, StringComparison.Ordinal))
            {
                errorList.RemoveAt(0);
            }
        }
        catch
        {
            // Best-effort bookkeeping only.
        }
    }

    private const string BodyScript = """
param($SqlInstance, $SqlCredential, $ResourcePool, $Type, $SkipReconfigure, $InputObject, $__typeBound, $EnableException, $__realCmdlet, $__boundVerbose)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($SqlInstance, $SqlCredential, $ResourcePool, $Type, $SkipReconfigure, $InputObject, $__typeBound, $EnableException, $__realCmdlet, $__boundVerbose)
    if ($null -ne $__boundVerbose) { $VerbosePreference = $(if ($__boundVerbose) { "Continue" } else { "SilentlyContinue" }) }

    if (-not $InputObject -and -not $ResourcePool) {
        Stop-Function -Message "You must pipe in a resource pool or specify a ResourcePool." -FunctionName Remove-DbaRgResourcePool
        return
    }
    if (-not $InputObject -and -not $SqlInstance) {
        Stop-Function -Message "You must pipe in a resource pool or specify a SqlInstance." -FunctionName Remove-DbaRgResourcePool
        return
    }

    if (($InputObject) -and (-not $__typeBound)) {
        if ($InputObject -is [Microsoft.SqlServer.Management.Smo.ResourcePool]) {
            $Type = "Internal"
        } elseif ($InputObject -is [Microsoft.SqlServer.Management.Smo.ExternalResourcePool]) {
            $Type = "External"
        }
    }

    foreach ($instance in $SqlInstance) {
        try {
            $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Remove-DbaRgResourcePool
        }
        if ($Type -eq "Internal") {
            $InputObject += $server.ResourceGovernor.ResourcePools | Where-Object Name -in $ResourcePool
        } elseif ($Type -eq "External") {
            $InputObject += $server.ResourceGovernor.ExternalResourcePools | Where-Object Name -in $ResourcePool
        }
    }

    foreach ($resPool in $InputObject) {
        try {
            $server = $resPool.Parent.Parent
            if ($__realCmdlet.ShouldProcess($resPool, "Dropping existing resource pool")) {
                try {
                    $resPool.Drop()
                } catch {
                    Stop-Function -Message "Could not remove existing resource pool $resPool on $server." -Target $resPool -Continue -FunctionName Remove-DbaRgResourcePool
                }
            }

            # Reconfigure Resource Governor
            if ($SkipReconfigure) {
                Write-Message -Level Warning -Message "Resource pool changes will not take effect in Resource Governor until it is reconfigured." -FunctionName Remove-DbaRgResourcePool
            } elseif ($__realCmdlet.ShouldProcess($server, "Reconfiguring the Resource Governor")) {
                $server.ResourceGovernor.Alter()
            }
        } catch {
            Stop-Function -Message "Failure" -ErrorRecord $_ -Target $resPool -Continue -FunctionName Remove-DbaRgResourcePool
        }
    }
} $SqlInstance $SqlCredential $ResourcePool $Type $SkipReconfigure $InputObject $__typeBound $EnableException $__realCmdlet $__boundVerbose 3>&1 2>&1
""";
}
