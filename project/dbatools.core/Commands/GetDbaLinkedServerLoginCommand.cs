#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Gets linked server logins (remote login mappings) for SQL Server instances. Port of
/// public/Get-DbaLinkedServerLogin.ps1 (W3-043). Pure per-record process command with no begin/end
/// blocks. The $InputObject accumulation ($InputObject += Connect | Get-DbaLinkedServer) and its
/// consumption both happen within the SAME process invocation (both foreach loops are in process, no
/// end block), so it is invocation-local - NO cross-record sentinel is needed (unlike
/// Remove-DbaLinkedServer, whose drop was in the end block). DEF-001 cond1+cond2: the process foreach
/// EMITS a decorated login per match (Select-DefaultView) AND has reachable Stop-Function -Continue at
/// the LinkedServer-required checks, so the hop STREAMS via InvokeScopedStreaming. The source's
/// Test-Bound -ParameterName LinkedServer guards (used twice) are carried as a bound flag - the
/// scriptblock runs in module scope and cannot see the real cmdlet's $PSBoundParameters. The command
/// declares SupportsShouldProcess but its body never calls ShouldProcess, so the attribute is mirrored
/// for surface parity with no $__realCmdlet carrier. Positions match the retired function
/// (SqlInstance=0, SqlCredential=1, LinkedServer=2, LocalLogin=3, ExcludeLocalLogin=4, InputObject=5;
/// EnableException=switch/null). Substitutions only: Test-Bound -> the carried $__boundLinkedServer
/// flag, explicit -FunctionName Get-DbaLinkedServerLogin on Stop-Function (W1-090); the body is
/// otherwise verbatim. Surface pinned by migration/baselines/Get-DbaLinkedServerLogin.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaLinkedServerLogin", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low)]
public sealed class GetDbaLinkedServerLoginCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The linked server(s) to read logins from.</summary>
    [Parameter(Position = 2)]
    public string[]? LinkedServer { get; set; }

    /// <summary>Local login name(s) to include.</summary>
    [Parameter(Position = 3)]
    public string[]? LocalLogin { get; set; }

    /// <summary>Local login name(s) to exclude.</summary>
    [Parameter(Position = 4)]
    public string[]? ExcludeLocalLogin { get; set; }

    /// <summary>Server or LinkedServer object(s) piped in.</summary>
    [Parameter(ValueFromPipeline = true, Position = 5)]
    public object[]? InputObject { get; set; }

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
            SqlInstance, SqlCredential, LinkedServer, LocalLogin, ExcludeLocalLogin, InputObject, EnableException.ToBool(),
            TestBound(nameof(LinkedServer)),
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
    // Test-Bound -Not -ParameterName LinkedServer -> the negated carried $__boundLinkedServer flag,
    // explicit -FunctionName Get-DbaLinkedServerLogin on Stop-Function (W1-090).
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $LinkedServer, $LocalLogin, $ExcludeLocalLogin, $InputObject, $EnableException, $__boundLinkedServer, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = "Low")]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [string[]]$LinkedServer, [string[]]$LocalLogin, [string[]]$ExcludeLocalLogin, [object[]]$InputObject, $EnableException, $__boundLinkedServer, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    foreach ($instance in $SqlInstance) {

        if (-not $__boundLinkedServer) {
            Stop-Function -Message "LinkedServer is required" -Continue -FunctionName Get-DbaLinkedServerLogin
        }

        $InputObject += Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential | Get-DbaLinkedServer -LinkedServer $LinkedServer
    }

    foreach ($obj in $InputObject) {

        if ($obj -is [Microsoft.SqlServer.Management.Smo.Server]) {

            if (-not $__boundLinkedServer) {
                Stop-Function -Message "LinkedServer is required" -Continue -FunctionName Get-DbaLinkedServerLogin
            }

            $ls = Get-DbaLinkedServer -SqlInstance $obj -LinkedServer $LinkedServer

        } elseif ($obj -is [Microsoft.SqlServer.Management.Smo.LinkedServer]) {
            $ls = $obj
        }

        $linkedServerLogins = $ls.LinkedServerLogins

        if ($LocalLogin) {
            $linkedServerLogins = $linkedServerLogins | Where-Object { $_.Name -in $LocalLogin }
        }

        if ($ExcludeLocalLogin) {
            $linkedServerLogins = $linkedServerLogins | Where-Object { $_.Name -notin $ExcludeLocalLogin }
        }

        foreach ($lsLogin in $linkedServerLogins) {
            Add-Member -Force -InputObject $lsLogin -MemberType NoteProperty -Name ComputerName -value $ls.parent.ComputerName
            Add-Member -Force -InputObject $lsLogin -MemberType NoteProperty -Name InstanceName -value $ls.parent.ServiceName
            Add-Member -Force -InputObject $lsLogin -MemberType NoteProperty -Name SqlInstance -value $ls.parent.DomainInstanceName

            Select-DefaultView -InputObject $lsLogin -Property ComputerName, InstanceName, SqlInstance, Name, RemoteUser, Impersonate
        }
    }
} $SqlInstance $SqlCredential $LinkedServer $LocalLogin $ExcludeLocalLogin $InputObject $EnableException $__boundLinkedServer $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
