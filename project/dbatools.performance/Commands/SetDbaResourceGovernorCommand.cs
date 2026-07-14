#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Configures SQL Server Resource Governor. Port of public/Set-DbaResourceGovernor.ps1
/// (W1-119). The per-instance body rides one module-scoped PowerShell hop so connection
/// failure continues, state snapshots, Enabled-over-Disabled precedence, three distinct
/// ShouldProcess actions, classifier UDF lookup/NULL handling, nested Stop-Function
/// continues, unguarded Alter/Refresh faults, and final Get-DbaResourceGovernor output keep
/// their source semantics. Surface pinned by
/// migration/baselines/Set-DbaResourceGovernor.json.
/// </summary>
[Cmdlet(VerbsCommon.Set, "DbaResourceGovernor", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low)]
public sealed class SetDbaResourceGovernorCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    [Alias("Credential")]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Enable Resource Governor.</summary>
    [Parameter]
    public SwitchParameter Enabled { get; set; }

    /// <summary>Disable Resource Governor.</summary>
    [Parameter]
    public SwitchParameter Disabled { get; set; }

    /// <summary>The classifier UDF name, or NULL to clear it.</summary>
    [Parameter(Position = 2)]
    public string? ClassifierFunction { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BodyScript,
            SqlInstance, SqlCredential, Enabled.ToBool(), Disabled.ToBool(),
            ClassifierFunction, EnableException.ToBool(), this, BoundVerbose()))
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
        }
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
param($SqlInstance, $SqlCredential, $Enabled, $Disabled, $ClassifierFunction, $EnableException, $__realCmdlet, $__boundVerbose)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($SqlInstance, $SqlCredential, $Enabled, $Disabled, $ClassifierFunction, $EnableException, $__realCmdlet, $__boundVerbose)
    if ($null -ne $__boundVerbose) { $VerbosePreference = $(if ($__boundVerbose) { "Continue" } else { "SilentlyContinue" }) }

    foreach ($instance in $SqlInstance) {
        try {
            $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Set-DbaResourceGovernor
        }

        $resourceGovernorState = [bool]$server.ResourceGovernor.Enabled
        $resourceGovernorClassifierFunction = [string]$server.ResourceGovernor.ClassifierFunction

        # Set Enabled status
        if ($Enabled) {
            if ($__realCmdlet.ShouldProcess($instance, "Changing Resource Governor enabled from '$resourceGovernorState' to 'True' at the instance level")) {
                try {
                    $server.ResourceGovernor.Enabled = $true
                } catch {
                    Stop-Function -Message "Couldn't enable Resource Governor" -ErrorRecord $_ -Continue -FunctionName Set-DbaResourceGovernor
                }
            }
        } elseif ($Disabled) {
            if ($__realCmdlet.ShouldProcess($instance, "Changing Resource Governor enabled from '$resourceGovernorState' to 'False' at the instance level")) {
                try {
                    $server.ResourceGovernor.Enabled = $false
                } catch {
                    Stop-Function -Message "Couldn't disable Resource Governor" -ErrorRecord $_ -Continue -FunctionName Set-DbaResourceGovernor
                }
            }
        }

        # Set Classifier Function
        if ($ClassifierFunction) {
            if ($__realCmdlet.ShouldProcess($instance, "Changing Resource Governor Classifier Function from '$resourceGovernorClassifierFunction' to '$ClassifierFunction'")) {
                if ($ClassifierFunction -eq "NULL") {
                    $server.ResourceGovernor.ClassifierFunction = $ClassifierFunction
                } else {
                    $objClassifierFunction = Get-DbaDbUdf -SqlInstance $instance -SqlCredential $SqlCredential -Database "master" -Name $ClassifierFunction
                    if ($objClassifierFunction) {
                        $server.ResourceGovernor.ClassifierFunction = $objClassifierFunction
                    } else {
                        Stop-Function -Message "Classifier function '$ClassifierFunction' does not exist." -Category ObjectNotFound -Continue -FunctionName Set-DbaResourceGovernor
                    }
                }
            }
        }

        # Execute
        if ($__realCmdlet.ShouldProcess($instance, "Changing Resource Governor")) {
            $server.ResourceGovernor.Alter()
            $server.ResourceGovernor.Refresh()
        }

        Get-DbaResourceGovernor -SqlInstance $server
    }
} $SqlInstance $SqlCredential $Enabled $Disabled $ClassifierFunction $EnableException $__realCmdlet $__boundVerbose 3>&1 2>&1
""";
}
