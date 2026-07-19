#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Gets server-level (DDL) triggers for SQL Server instances. Port of
/// public/Get-DbaInstanceTrigger.ps1 (W3-039). Pure per-record process command with no begin/end
/// blocks (nothing to carry). DEF-001 cond1+cond2: the process foreach EMITS a decorated trigger per
/// item (Select-DefaultView) AND has reachable Stop-Function -Continue at Connect-DbaInstance and the
/// per-trigger catch, so the hop STREAMS via InvokeScopedStreaming. No ShouldProcess, no cross-record
/// state, no carriers beyond the parameters. Positions match the retired function (SqlInstance=0,
/// SqlCredential=1; EnableException=switch/null). Substitution only: explicit -FunctionName
/// Get-DbaInstanceTrigger on Stop-Function (W1-090); the body is otherwise verbatim (including the
/// source's mixed $Instance/$instance casing, which PowerShell treats as one variable). Surface pinned
/// by migration/baselines/Get-DbaInstanceTrigger.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaInstanceTrigger")]
public sealed class GetDbaInstanceTriggerCommand : DbaBaseCmdlet
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
    // -FunctionName Get-DbaInstanceTrigger on Stop-Function (W1-090).
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

    foreach ($Instance in $SqlInstance) {
        try {
            $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaInstanceTrigger
        }

        foreach ($trigger in $server.Triggers) {
            try {
                Add-Member -Force -InputObject $trigger -MemberType NoteProperty -Name ComputerName -value $server.ComputerName
                Add-Member -Force -InputObject $trigger -MemberType NoteProperty -Name InstanceName -value $server.ServiceName
                Add-Member -Force -InputObject $trigger -MemberType NoteProperty -Name SqlInstance -value $server.DomainInstanceName
                Select-DefaultView -InputObject $trigger -Property ComputerName, InstanceName, SqlInstance, ID, Name, AnsiNullsStatus, AssemblyName, BodyStartIndex, ClassName, CreateDate, DateLastModified, DdlTriggerEvents, ExecutionContext, ExecutionContextLogin, ImplementationType, IsDesignMode, IsEnabled, IsEncrypted, IsSystemObject, MethodName, QuotedIdentifierStatus, State, TextHeader, TextMode
            } catch {
                Stop-Function -Message "Failure" -ErrorRecord $_ -Continue -FunctionName Get-DbaInstanceTrigger
            }
        }
    }
} $SqlInstance $SqlCredential $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
