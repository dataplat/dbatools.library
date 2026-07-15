#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves and decorates SQL Agent JobServer configuration. Process-scope server state,
/// ScriptProperty creation, and SMO output shaping remain a module-scoped PowerShell compatibility
/// hop. Surface pinned by migration/baselines/Get-DbaAgentServer.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaAgentServer")]
public sealed class GetDbaAgentServerCommand : DbaBaseCmdlet
{
    /// <summary>Target SQL Server instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    [PsDbaInstanceArrayCast]
    public DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    private object? _server;

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        foreach (DbaInstanceParameter instance in SqlInstance)
        {
            if (Interrupted)
                return;

            foreach (PSObject? item in NestedCommand.InvokeScoped(this, BodyScript,
                new[] { instance }, SqlCredential, EnableException.ToBool(), _server,
                BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
            {
                if (item?.BaseObject is ErrorRecord nestedError)
                {
                    RemoveHopErrorBookkeeping(nestedError);
                    WriteError(nestedError);
                }
                else if (item is not null && LanguagePrimitives.IsTrue(
                    item.Properties["__GetDbaAgentServerProcessComplete"]?.Value))
                {
                    object? serverState = item.Properties["Server"]?.Value;
                    _server = serverState is PSObject wrapper ? wrapper.BaseObject : serverState;
                }
                else
                {
                    WriteObject(item);
                }
            }
        }
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

    private const string BodyScript = """
param($SqlInstance, $SqlCredential, $EnableException, $Server, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, $SqlCredential,
        $EnableException, $Server)

    $server = $Server
    foreach ($instance in $SqlInstance) {
        try {
            $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaAgentServer
        }

        $jobServer = $server.JobServer
        $defaultView = 'ComputerName', 'InstanceName', 'SqlInstance', 'AgentDomainGroup', 'AgentLogLevel', 'AgentMailType', 'AgentShutdownWaitTime', 'ErrorLogFile', 'IdleCpuDuration', 'IdleCpuPercentage', 'IsCpuPollingEnabled', 'JobServerType', 'LoginTimeout', 'JobHistoryIsEnabled', 'MaximumHistoryRows', 'MaximumJobHistoryRows', 'MsxAccountCredentialName', 'MsxAccountName', 'MsxServerName', 'Name', 'NetSendRecipient', 'ServiceAccount', 'ServiceStartMode', 'SqlAgentAutoStart', 'SqlAgentMailProfile', 'SqlAgentRestart', 'SqlServerRestart', 'State', 'SysAdminOnly'

        Add-Member -Force -InputObject $jobServer -MemberType NoteProperty -Name ComputerName -Value $jobServer.Parent.ComputerName
        Add-Member -Force -InputObject $jobServer -MemberType NoteProperty -Name InstanceName -value $jobServer.Parent.ServiceName
        Add-Member -Force -InputObject $jobServer -MemberType NoteProperty -Name SqlInstance -Value $jobServer.Parent.DomainInstanceName
        Add-Member -Force -InputObject $jobServer -MemberType ScriptProperty -Name JobHistoryIsEnabled -Value { switch ( $this.MaximumHistoryRows ) { -1 { $false } default { $true } } }

        Select-DefaultView -InputObject $jobServer -Property $defaultView
    }

    [pscustomobject]@{
        __GetDbaAgentServerProcessComplete = $true
        Server = $server
    }
} $SqlInstance $SqlCredential $EnableException $Server @__commonParameters 3>&1 2>&1
""";
}
