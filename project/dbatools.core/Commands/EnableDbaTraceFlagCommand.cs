#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Enables globally running SQL Server trace flags. Port of
/// public/Enable-DbaTraceFlag.ps1 (W3-013), the sibling of W3-009 Disable-DbaTraceFlag (same
/// shape; DBCC TRACEON + $server.Refresh() instead of TRACEOFF, skips when the flag is ALREADY
/// running). Pure per-record process command with no begin/end blocks, so the whole process body
/// runs as one streaming hop. DEF-001 cond1+cond2: the process foreach EMITS a $TraceFlagInfo
/// object per trace flag (skipped / failed / successful) AND has reachable Stop-Function -Continue
/// at Connect-DbaInstance / the DBCC TRACEON failure, so the hop STREAMS via InvokeScopedStreaming
/// - a buffered hop would lose an earlier flag's emit when a later one throws under
/// -EnableException. Substitutions only: $Pscmdlet.ShouldProcess -> $__realCmdlet.ShouldProcess
/// (ConfirmImpact LOW mirrored) and explicit -FunctionName Enable-DbaTraceFlag on every
/// Stop-Function (W1-090); the body is otherwise verbatim (including the in-loop `continue`).
/// Surface pinned by migration/baselines/Enable-DbaTraceFlag.json.
/// </summary>
[Cmdlet(VerbsLifecycle.Enable, "DbaTraceFlag", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low)]
public sealed class EnableDbaTraceFlagCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The trace flag number(s) to enable.</summary>
    [Parameter(Mandatory = true)]
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
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
                return;
            }
            WriteObject(item);
        }, ProcessScript,
            SqlInstance, SqlCredential, TraceFlag, EnableException.ToBool(), this,
            BoundCommonParameter("WhatIf"), BoundCommonParameter("Confirm"),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug"));
    }

    private object? BoundCommonParameter(string name)
    {
        if (MyInvocation.BoundParameters.TryGetValue(name, out object? value))
            return LanguagePrimitives.IsTrue(value);
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

    // PS: the process body VERBATIM per record (no begin/end blocks). Substitutions only:
    // $Pscmdlet -> $__realCmdlet, explicit -FunctionName Enable-DbaTraceFlag on Stop-Function (W1-090).
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
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Enable-DbaTraceFlag
        }

        $CurrentRunningTraceFlags = Get-DbaTraceFlag -SqlInstance $server -EnableException

        # We could combine all trace flags but the granularity is worth it
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
            if ($CurrentRunningTraceFlags.TraceFlag -contains $tf) {
                $TraceFlagInfo.Status = 'Skipped'
                $TraceFlagInfo.Notes = "The Trace flag is already running."
                $TraceFlagInfo
                Write-Message -Level Warning -Message "The Trace flag [$tf] is already running globally."
                continue
            }
            if ($__realCmdlet.ShouldProcess($instance, "Enabling flag '$tf'")) {
                try {
                    $query = "DBCC TRACEON($tf, -1)"
                    $server.Query($query)
                    $server.Refresh()
                } catch {
                    $TraceFlagInfo.Status = "Failed"
                    $TraceFlagInfo.Notes = $_.Exception.Message
                    $TraceFlagInfo
                    Stop-Function -Message "Failure" -ErrorRecord $_ -Target $server -Continue -FunctionName Enable-DbaTraceFlag
                }
                $TraceFlagInfo.Status = "Successful"
                $TraceFlagInfo
            }
        }
    }
} $SqlInstance $SqlCredential $TraceFlag $EnableException $__realCmdlet $__boundWhatIf $__boundConfirm $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
