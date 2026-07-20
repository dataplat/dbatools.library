#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Gets backup devices for SQL Server instances. Port of public/Get-DbaBackupDevice.ps1 (W3-021).
/// A read-only getter (Backup tag, but not a restore/backup operation). Pure per-record process
/// command with no begin/end blocks. DEF-001 cond1+cond2: the process foreach EMITS a decorated
/// backup device per item (Select-DefaultView) AND has a reachable Stop-Function -Continue at
/// Connect-DbaInstance, so the hop STREAMS via InvokeScopedStreaming. No ShouldProcess, no cross-record
/// state, no carriers beyond the parameters. Positions match the retired function (SqlInstance=0,
/// SqlCredential=1; EnableException=switch/null). Substitution only: explicit -FunctionName
/// Get-DbaBackupDevice on Stop-Function (W1-090); the body is otherwise verbatim. Surface pinned by
/// migration/baselines/Get-DbaBackupDevice.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaBackupDevice")]
public sealed class GetDbaBackupDeviceCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

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
            SqlInstance, SqlCredential, EnableException.ToBool(),
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

    // PS: the process body VERBATIM per record (no begin/end blocks). Substitution only: explicit
    // -FunctionName Get-DbaBackupDevice on Stop-Function (W1-090).
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    foreach ($instance in $SqlInstance) {
        try {
            $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaBackupDevice
        }

        foreach ($backupDevice in $server.BackupDevices) {
            Add-Member -Force -InputObject $backupDevice -MemberType NoteProperty -Name ComputerName -value $backupDevice.Parent.ComputerName
            Add-Member -Force -InputObject $backupDevice -MemberType NoteProperty -Name InstanceName -value $backupDevice.Parent.ServiceName
            Add-Member -Force -InputObject $backupDevice -MemberType NoteProperty -Name SqlInstance -value $backupDevice.Parent.DomainInstanceName

            Select-DefaultView -InputObject $backupDevice -Property ComputerName, InstanceName, SqlInstance, Name, BackupDeviceType, PhysicalLocation, SkipTapeLabel
        }
    }
} $SqlInstance $SqlCredential $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
