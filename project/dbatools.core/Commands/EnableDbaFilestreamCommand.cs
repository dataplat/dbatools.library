#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Enables SQL Server FileStream at the service and instance levels. Port of
/// public/Enable-DbaFilestream.ps1 (W3-010), the sibling of W3-006 Disable-DbaFilestream.
/// Pure per-record process command: the begin block only sets local constants (the
/// $FileStreamLevel name->int normalization, $level, $OutputLookup, the -Force
/// ConfirmPreference tweak) consumed inside the same process body, so there is NO cross-record
/// accumulator and NO end hop - the begin constants inline into the process script. DEF-001
/// cond1+cond2 (the process foreach EMITS Get-DbaFilestream per instance AND has reachable
/// Stop-Function -Continue at Connect-DbaInstance / Set-DbaSpConfigure), so the hop STREAMS via
/// InvokeScopedStreaming - a buffered hop would lose an earlier instance's emit when a later
/// instance throws under -EnableException. The pre-loop "ShareName requires level >= 2" guard is
/// a terminating Stop-Function BEFORE any emit, so it is DEF-001-neutral. Substitutions only:
/// $PSCmdlet.ShouldProcess -> $__realCmdlet.ShouldProcess (ConfirmImpact MEDIUM mirrored) and
/// explicit -FunctionName Enable-DbaFilestream on every Stop-Function (W1-090); the body is
/// otherwise verbatim. Surface pinned by migration/baselines/Enable-DbaFilestream.json.
/// </summary>
[Cmdlet(VerbsLifecycle.Enable, "DbaFilestream", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Medium)]
public sealed class EnableDbaFilestreamCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipelineByPropertyName = true, Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instance.</summary>
    [Parameter(ValueFromPipelineByPropertyName = true, Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Alternative Windows credential for the target server.</summary>
    [Parameter(ValueFromPipelineByPropertyName = true, Position = 2)]
    public PSCredential? Credential { get; set; }

    /// <summary>The FileStream access level to enable (name or numeric 1/2/3).</summary>
    [Parameter(Position = 3)]
    [ValidateSet("TSql", "TSqlIoStreaming", "TSqlIoStreamingRemoteClient", "1", "2", "3")]
    public string FileStreamLevel { get; set; } = "1";

    /// <summary>The Windows file share name for FileStream data (requires level >= 2).</summary>
    [Parameter(Position = 4)]
    public string? ShareName { get; set; }

    /// <summary>Bypasses confirmation and restarts the SQL Server service to apply changes immediately.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

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
            SqlInstance, SqlCredential, Credential, FileStreamLevel, ShareName, Force.ToBool(), EnableException.ToBool(), this,
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

    // PS: the begin constants (the $FileStreamLevel name->int normalization, $level,
    // $OutputLookup and the -Force ConfirmPreference tweak) inline ahead of the process body,
    // which is VERBATIM per record. Substitutions only: $PSCmdlet -> $__realCmdlet, explicit
    // -FunctionName Enable-DbaFilestream on Stop-Function (W1-090).
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Credential, $FileStreamLevel, $ShareName, $Force, $EnableException, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = "Medium")]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [PSCredential]$Credential, [string]$FileStreamLevel, [string]$ShareName, $Force, $EnableException, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    if ($FileStreamLevel -notin (1, 2, 3)) {
        $FileStreamLevel = switch ($FileStreamLevel) {
            "TSql" {
                1
            }
            "TSqlIoStreaming" {
                2
            }
            "TSqlIoStreamingRemoteClient" {
                3
            }
        }
    }
    # = $finallevel removed as it was identified as a unused variable
    $level = [int]$FileStreamLevel
    $OutputLookup = @{
        0 = 'Disabled'
        1 = 'FileStream enabled for T-Sql access'
        2 = 'FileStream enabled for T-Sql and IO streaming access'
        3 = 'FileStream enabled for T-Sql, IO streaming, and remote clients'
    }

    if ($Force) { $ConfirmPreference = 'none' }

    if ($ShareName -and $level -lt 2) {
        Stop-Function -Message "Filestream must be at least level 2 when using ShareName" -FunctionName Enable-DbaFilestream
        return
    }

    foreach ($instance in $SqlInstance) {
        try {
            $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Enable-DbaFilestream
        }

        $filestreamstate = [int]$server.Configuration.FilestreamAccessLevel.ConfigValue

        if ($Force -or $__realCmdlet.ShouldProcess($instance, "Changing from '$($OutputLookup[$filestreamstate])' to '$($OutputLookup[$level])' at the instance level")) {
            # Server level
            if ($server.IsClustered) {
                $nodes = Get-DbaWsfcNode -ComputerName $instance
                foreach ($node in $nodes.Name) {
                    $result = Set-FileSystemSetting -Instance $node -Credential $Credential -ShareName $ShareName -FilestreamLevel $level
                }
            } else {
                $result = Set-FileSystemSetting -Instance $instance -Credential $Credential -ShareName $ShareName -FilestreamLevel $level
            }

            # Instance level
            if ($level -eq 3) {
                $level = 2
            }

            try {
                $null = Set-DbaSpConfigure -SqlInstance $server -Name FilestreamAccessLevel -Value $level -EnableException
            } catch {
                Stop-Function -Message "Failure" -ErrorRecord $_ -Continue -FunctionName Enable-DbaFilestream
            }

            if ($Force) {
                #$restart replaced with $null as it was identified as a unused variable
                $null = Restart-DbaService -ComputerName $server.ComputerName -InstanceName $server.ServiceName -Type Engine -Force
            }

            Get-DbaFilestream -SqlInstance $instance -SqlCredential $SqlCredential -Credential $Credential
            if ($filestreamstate -ne $level -and -not $Force) {
                Write-Message -Level Warning -Message "[$instance] $result"
            }
        }
    }
} $SqlInstance $SqlCredential $Credential $FileStreamLevel $ShareName $Force $EnableException $__realCmdlet $__boundWhatIf $__boundConfirm $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
