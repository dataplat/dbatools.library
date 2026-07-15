#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves and decorates Database Mail configuration. Process-scope server state, helper
/// diagnostics, and SMO output shaping remain a module-scoped PowerShell compatibility hop.
/// Surface pinned by migration/baselines/Get-DbaDbMail.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbMail")]
public sealed class GetDbaDbMailCommand : DbaBaseCmdlet
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

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BodyScript,
            SqlInstance, SqlCredential, EnableException.ToBool(), _server,
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
            }
            else if (item is not null && LanguagePrimitives.IsTrue(
                item.Properties["__GetDbaDbMailProcessComplete"]?.Value))
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
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaDbMail
        }

        try {
            $mailserver = $server.Mail
            Add-Member -Force -InputObject $mailserver -MemberType NoteProperty -Name ComputerName -value $server.ComputerName
            Add-Member -Force -InputObject $mailserver -MemberType NoteProperty -Name InstanceName -value $server.ServiceName
            Add-Member -Force -InputObject $mailserver -MemberType NoteProperty -Name SqlInstance -value $server.DomainInstanceName
            $mailserver | Select-DefaultView -Property ComputerName, InstanceName, SqlInstance, Profiles, Accounts, ConfigurationValues, Properties
        } catch {
            Stop-Function -Message "Failure" -ErrorRecord $_ -Continue -FunctionName Get-DbaDbMail
        }
    }

    [pscustomobject]@{
        __GetDbaDbMailProcessComplete = $true
        Server = $server
    }
} $SqlInstance $SqlCredential $EnableException $Server @__commonParameters 3>&1 2>&1
""";
}
