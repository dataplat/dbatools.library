#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Creates a SQL Server Agent alert category on the target instances.
/// </summary>
/// <remarks>
/// The SMO alert-category construction and the output run the original dbatools PowerShell body
/// inside the dbatools module scope rather than being reimplemented in C#, so the engine decides the
/// observable details.
///
/// The function's begin block only lowers the confirm preference under -Force; that is folded into the
/// top of the process hop because -Force is not pipeline-bound, so running it once per record is
/// equivalent to running it once, and the gate reads the confirm preference from the hop scope.
///
/// Output streams as it is produced. A single record can create categories across several instances,
/// and each is emitted before a later one may fail under -EnableException; buffering them and losing
/// them to a later terminating failure would hide categories that were actually created.
///
/// This cmdlet supplies the real ShouldProcess runtime to the hop. Surface pinned by
/// migration/baselines/New-DbaAgentAlertCategory.json.
/// </remarks>
[Cmdlet(VerbsCommon.New, "DbaAgentAlertCategory", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low)]
public sealed class NewDbaAgentAlertCategoryCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    [PsDbaInstanceArrayCast]
    public DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>One or more alert category names to create.</summary>
    [Parameter(Mandatory = true, Position = 2)]
    [ValidateNotNullOrEmpty]
    public string[] Category { get; set; } = null!;

    /// <summary>Suppress the confirmation prompt.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

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
            SqlInstance, SqlCredential, Category, Force.ToBool(), EnableException.ToBool(), this,
            BoundCommonParameter("WhatIf"), BoundCommonParameter("Confirm"),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug"));
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
            {
                return;
            }
            if (errorList[0] is not ErrorRecord first)
            {
                return;
            }
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
param($SqlInstance, $SqlCredential, $Category, $Force, $EnableException, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = "Low")]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, $SqlCredential, [string[]]$Category, [switch]$Force, $EnableException, $__realCmdlet)

    if ($Force) { $ConfirmPreference = 'none' }
    $__gate = if ($Force) { $PSCmdlet } else { $__realCmdlet }
    foreach ($instance in $SqlInstance) {
        try {
            $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName New-DbaAgentAlertCategory
        }

        foreach ($cat in $Category) {
            if ($cat -in $server.JobServer.AlertCategories.Name) {
                Stop-Function -Message "Alert category $cat already exists on $instance" -Target $instance -Continue -FunctionName New-DbaAgentAlertCategory
            } else {
                if ($__gate.ShouldProcess($instance, "Adding the alert category $cat")) {
                    try {
                        try {
                            $alertCategory = New-Object Microsoft.SqlServer.Management.Smo.Agent.AlertCategory($server.JobServer, $cat)
                        } catch {
                            if ($_.Exception.Message -match "newParent") {
                                Stop-Function -Message "Cannot create agent alert category through a contained availability group listener. SQL Server Agent objects are instance-level and must be managed on the instance directly. Please connect to the primary replica instead of the listener. Use Get-DbaAvailabilityGroup to find the current primary replica." -ErrorRecord $_ -Target $cat -Continue -FunctionName New-DbaAgentAlertCategory
                                return
                            } else {
                                throw
                            }
                        }

                        $alertCategory.Create()

                        $server.JobServer.Refresh()
                    } catch {
                        Stop-Function -Message "Something went wrong creating the alert category $cat on $instance" -Target $cat -Continue -ErrorRecord $_ -FunctionName New-DbaAgentAlertCategory
                    }
                }
            }
            Get-DbaAgentAlertCategory -SqlInstance $server -Category $cat
        }
    }
} $SqlInstance $SqlCredential $Category $Force $EnableException $__realCmdlet @__commonParameters 3>&1 2>&1
""";
}
