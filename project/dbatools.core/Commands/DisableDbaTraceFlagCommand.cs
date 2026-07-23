#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Disables globally running SQL Server trace flags. Port of
/// public/Disable-DbaTraceFlag.ps1 (W3-009). Pure per-record process command with no begin/end
/// blocks, so the whole process body runs as one streaming hop. DEF-001 cond1+cond2: the process
/// foreach EMITS a $TraceFlagInfo object per trace flag (at the skipped / failed / successful
/// branches) AND has reachable Stop-Function -Continue at Connect-DbaInstance / the DBCC TRACEOFF
/// failure, so the hop STREAMS via InvokeScopedStreaming - a buffered hop would lose an earlier
/// flag's emit when a later one throws under -EnableException. Substitutions only:
/// $Pscmdlet.ShouldProcess -> $__realCmdlet.ShouldProcess (ConfirmImpact LOW mirrored) and
/// explicit -FunctionName Disable-DbaTraceFlag on every Stop-Function (W1-090); the body is
/// otherwise verbatim (including the in-loop `continue`, which is a normal PowerShell statement
/// inside the scriptblock). Surface pinned by migration/baselines/Disable-DbaTraceFlag.json.
/// </summary>
[Cmdlet(VerbsLifecycle.Disable, "DbaTraceFlag", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low)]
public sealed class DisableDbaTraceFlagCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The trace flag number(s) to disable.</summary>
    [Parameter(Mandatory = true, Position = 2)]
    public int[]? TraceFlag { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                NestedCommand.RemoveDuplicateError(this, nestedError);
                WriteError(nestedError);
                return;
            }
            WriteObject(item);
        }, ProcessScript,
            SqlInstance, SqlCredential, TraceFlag, EnableException.ToBool(), this,
            NestedCommand.BoundCommonParameter(this, "WhatIf"), NestedCommand.BoundCommonParameter(this, "Confirm"),
            NestedCommand.BoundCommonParameter(this, "Verbose"), NestedCommand.BoundCommonParameter(this, "Debug"));
    }

    // PS: the process body VERBATIM per record (no begin/end blocks). Substitutions only:
    // $Pscmdlet -> $__realCmdlet, explicit -FunctionName Disable-DbaTraceFlag on Stop-Function (W1-090).
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $TraceFlag, $EnableException, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = "Low")]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [int[]]$TraceFlag, $EnableException, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    foreach ($instance in $SqlInstance) {

        try {
            $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Disable-DbaTraceFlag
        }

        $current = Get-DbaTraceFlag -SqlInstance $server -EnableException

        foreach ($tf in $TraceFlag) {
            $TraceFlagInfo = [PSCustomObject]@{
                SourceServer = $server.ComputerName
                InstanceName = $server.ServiceName
                SqlInstance  = $server.DomainInstanceName
                TraceFlag    = $tf
                Status       = $null
                Notes        = $null
                DateTime     = [DbaDateTime](Get-Date)
            }
            if ($tf -notin $current.TraceFlag) {
                $TraceFlagInfo.Status = 'Skipped'
                $TraceFlagInfo.Notes = "Trace Flag is not running."
                $TraceFlagInfo
                Write-Message -Level Warning -Message "Trace Flag $tf is not currently running on $instance" -FunctionName Disable-DbaTraceFlag -ModuleName "dbatools"
                continue
            }
            if ($__realCmdlet.ShouldProcess($instance, "Disabling flag '$tf'")) {
                try {
                    $query = "DBCC TRACEOFF ($tf, -1)"
                    $server.Query($query)
                } catch {
                    $TraceFlagInfo.Status = "Failed"
                    $TraceFlagInfo.Notes = $_.Exception.Message
                    $TraceFlagInfo
                    Stop-Function -Message "Failure" -ErrorRecord $_ -Target $server -Continue -FunctionName Disable-DbaTraceFlag
                }
                $TraceFlagInfo.Status = "Successful"
                $TraceFlagInfo
            }
        }
    }
} $SqlInstance $SqlCredential $TraceFlag $EnableException $__realCmdlet $__boundWhatIf $__boundConfirm $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
