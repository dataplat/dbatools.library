#nullable enable

using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Removes database mirroring monitoring from an instance.
/// Port of public/Remove-DbaDbMirrorMonitor.ps1; surface pinned by
/// migration/baselines/Remove-DbaDbMirrorMonitor.json.
/// </summary>
[Cmdlet(VerbsCommon.Remove, "DbaDbMirrorMonitor", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low)]
public sealed class RemoveDbaDbMirrorMonitorCommand : DbaBaseCmdlet
{
    /// <summary>The SQL Server instance or instances to remove mirror monitoring from.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instances using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    private Hashtable? _state;

    protected override void BeginProcessing()
    {
        base.BeginProcessing();

        // C1 transplant condition: loud fail before any record if the engine field is gone.
        PromptStateTransplant.AssertResolvable("Remove-DbaDbMirrorMonitor");
    }

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        // WHOLE-RECORD hop, and the simplest row in this family: no begin block, no
        // Test-FunctionInterrupt, no Test-Bound, and NO parameter mutations anywhere in the
        // process block. The AST detector reports zero parameter-target assignments, so there is
        // nothing sticky and nothing to carry - the sentinel carries prompt state ONLY. Stated
        // explicitly because the four preceding siblings each needed something different here,
        // and "nothing to carry" is a finding, not an omission.
        //
        // W3-082 PROMPT-STATE TRANSPLANT still applies: VFP SqlInstance + per-record
        // ProcessRecord + inner-$Pscmdlet gate. ConfirmImpact is Low rather than High, so the
        // prompt is less likely to be seen interactively - but Yes/No-to-All must still persist
        // across piped records when -Confirm is used explicitly, which is exactly what the
        // transplant preserves.
        //
        // Note the source's Connect-DbaInstance carries -MinimumVersion 9 (SQL 2005+), which
        // rides verbatim inside the body rather than being reimplemented in C#.
        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, SqlCredential,
            EnableException.ToBool(),
            _state,
            BoundCommonParameter("WhatIf"), BoundCommonParameter("Confirm"),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            Hashtable? sentinel = item?.BaseObject as Hashtable;
            if (sentinel is not null && sentinel.ContainsKey("__w4051State"))
            {
                _state = sentinel["__w4051State"] as Hashtable;
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

    // PS: the source process block VERBATIM, CRLF-preserved and byte-proven against source
    // lines 67-86 after appending two -FunctionName arguments. NO Test-Bound rewrites - this row
    // has none, so there are no SOURCE comments in the body at all. The ShouldProcess gate uses
    // the inner block's own $Pscmdlet; both Stop-Function sites are -Continue, so there is no
    // early return to preserve, but the dot-block frame is kept for consistency with the family
    // and so the harvest placement matches its siblings. Bracketing the body: only the W3-082
    // prompt-state transplant.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $EnableException, $__state, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Low')]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, $EnableException, $__state, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    # cross-record engine-state restore: the ShouldProcess Yes/No-to-All answer spans the
    # pipeline in the source (one CommandRuntime); the transplant field name is identical
    # on PS 5.1 and PS 7 (W3-082 mechanism, empirically verified). NO parameter carry on this
    # row - the AST detector reports zero parameter-target assignments in the process block.
    $__spField = $Pscmdlet.CommandRuntime.GetType().GetField("lastShouldProcessContinueStatus", [System.Reflection.BindingFlags]"NonPublic,Instance")
    if ($null -eq $__spField) {
        throw "Remove-DbaDbMirrorMonitor: prompt-state transplant field lastShouldProcessContinueStatus not resolvable on this engine (C1 assert)."
    }
    if ($null -ne $__state -and $null -ne $__state.shouldProcessContinueStatus) {
        $__spField.SetValue($Pscmdlet.CommandRuntime, [Enum]::Parse($__spField.FieldType, $__state.shouldProcessContinueStatus))
    }

    . {
        foreach ($instance in $SqlInstance) {
            try {
                $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential -MinimumVersion 9
            } catch {
                Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Remove-DbaDbMirrorMonitor
            }
            if ($Pscmdlet.ShouldProcess($instance, "Removing mirror monitoring")) {
                try {
                    $server.Query("EXEC msdb.dbo.sp_dbmmonitordropmonitoring")
                    [PSCustomObject]@{
                        ComputerName  = $server.ComputerName
                        InstanceName  = $server.ServiceName
                        SqlInstance   = $server.DomainInstanceName
                        MonitorStatus = "Removed"
                    }
                } catch {
                    Stop-Function -Message "Failure" -ErrorRecord $_ -Continue -FunctionName Remove-DbaDbMirrorMonitor
                }
            }
        }
    }

    @{ __w4051State = @{ shouldProcessContinueStatus = $(if ($null -ne $__spField) { "$($__spField.GetValue($Pscmdlet.CommandRuntime))" } else { $null }) } }
} $SqlInstance $SqlCredential $EnableException $__state $__boundWhatIf $__boundConfirm $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
