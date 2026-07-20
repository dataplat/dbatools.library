#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Gets the SQL Server error log configuration (count, size, path). Port of
/// public/Get-DbaErrorLogConfig.ps1 (W3-031). Pure per-record process command with no begin/end
/// blocks. DEF-001 cond1+cond2: the process foreach EMITS a [PSCustomObject] per instance AND has a
/// reachable Stop-Function -Continue at Connect-DbaInstance, so the hop STREAMS via
/// InvokeScopedStreaming - a buffered hop would lose an earlier instance's emit when a later instance
/// throws under -EnableException. No ShouldProcess, no cross-record state, no carriers beyond the
/// parameters. Positions match the retired function (SqlInstance=0, SqlCredential=1;
/// EnableException=switch/null). Substitution only: explicit -FunctionName Get-DbaErrorLogConfig on
/// Stop-Function (W1-090); the body is otherwise verbatim. Surface pinned by
/// migration/baselines/Get-DbaErrorLogConfig.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaErrorLogConfig")]
public sealed class GetDbaErrorLogConfigCommand : DbaBaseCmdlet
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
    // -FunctionName Get-DbaErrorLogConfig on Stop-Function (W1-090).
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
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaErrorLogConfig
        }

        $numLogs = $server.NumberOfLogFiles
        $logSize =
        if ($server.VersionMajor -ge 11) {
            [dbasize]($server.ErrorLogSizeKb * 1024)
        } else {
            $null
        }

        [PSCustomObject]@{
            ComputerName = $server.ComputerName
            InstanceName = $server.ServiceName
            SqlInstance  = $server.DomainInstanceName
            LogCount     = $numLogs
            LogSize      = $logSize
            LogPath      = $server.ErrorLogPath
        }
    }
} $SqlInstance $SqlCredential $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
