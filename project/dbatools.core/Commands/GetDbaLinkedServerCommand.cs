#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Gets linked servers for SQL Server instances. Port of public/Get-DbaLinkedServer.ps1 (W3-042).
/// Pure per-record process command with no begin/end blocks. DEF-001 cond1+cond2: the process foreach
/// EMITS a decorated linked server per match (Select-DefaultView) AND has a reachable
/// Stop-Function -Continue at Connect-DbaInstance, so the hop STREAMS via InvokeScopedStreaming. No
/// ShouldProcess, no cross-record state, no carriers beyond the parameters. Positions match the
/// retired function (SqlInstance=0, SqlCredential=1, LinkedServer=2, ExcludeLinkedServer=3;
/// EnableException=switch/null) and DefaultParameterSetName "Default" is preserved via the [Cmdlet]
/// attribute. Substitution only: explicit -FunctionName Get-DbaLinkedServer on Stop-Function (W1-090);
/// the body is otherwise verbatim. Surface pinned by migration/baselines/Get-DbaLinkedServer.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaLinkedServer", DefaultParameterSetName = "Default")]
public sealed class GetDbaLinkedServerCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Linked server name(s) to include.</summary>
    [Parameter(Position = 2)]
    public object[]? LinkedServer { get; set; }

    /// <summary>Linked server name(s) to exclude.</summary>
    [Parameter(Position = 3)]
    public object[]? ExcludeLinkedServer { get; set; }

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
            SqlInstance, SqlCredential, LinkedServer, ExcludeLinkedServer, EnableException.ToBool(),
            NestedCommand.BoundCommonParameter(this, "Verbose"), NestedCommand.BoundCommonParameter(this, "Debug"));
    }

    // PS: the process body VERBATIM per record (no begin/end blocks). Substitution only: explicit
    // -FunctionName Get-DbaLinkedServer on Stop-Function (W1-090).
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $LinkedServer, $ExcludeLinkedServer, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(DefaultParameterSetName = "Default")]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [object[]]$LinkedServer, [object[]]$ExcludeLinkedServer, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    foreach ($instance in $SqlInstance) {
        try {
            $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaLinkedServer
        }

        $lservers = $server.LinkedServers

        if ($LinkedServer) {
            $lservers = $lservers | Where-Object { $_.Name -in $LinkedServer }
        }
        if ($ExcludeLinkedServer) {
            $lservers = $lservers | Where-Object { $_.Name -notin $ExcludeLinkedServer }
        }

        foreach ($ls in $lservers) {
            Add-Member -Force -InputObject $ls -MemberType NoteProperty -Name ComputerName -value $server.ComputerName
            Add-Member -Force -InputObject $ls -MemberType NoteProperty -Name InstanceName -value $server.ServiceName
            Add-Member -Force -InputObject $ls -MemberType NoteProperty -Name SqlInstance -value $server.DomainInstanceName
            Add-Member -Force -InputObject $ls -MemberType NoteProperty -Name Impersonate -value $ls.LinkedServerLogins.Impersonate
            Add-Member -Force -InputObject $ls -MemberType NoteProperty -Name RemoteUser -value $ls.LinkedServerLogins.RemoteUser

            Select-DefaultView -InputObject $ls -Property ComputerName, InstanceName, SqlInstance, Name, 'DataSource as RemoteServer', ProductName, Impersonate, RemoteUser, 'DistPublisher as Publisher', Distributor, DateLastModified
        }
    }
} $SqlInstance $SqlCredential $LinkedServer $ExcludeLinkedServer $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
