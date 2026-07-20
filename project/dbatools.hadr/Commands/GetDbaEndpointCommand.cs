#nullable enable

using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Returns server endpoints with TCP listener and FQDN context. Port of
/// public/Get-DbaEndpoint.ps1; surface pinned by
/// migration/baselines/Get-DbaEndpoint.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaEndpoint")]
public sealed class GetDbaEndpointCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instances using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Restricts results to these endpoint names.</summary>
    [Parameter(Position = 2)]
    public string[]? Endpoint { get; set; }

    /// <summary>Restricts results to these endpoint types.</summary>
    [Parameter(Position = 3)]
    [ValidateSet("DatabaseMirroring", "ServiceBroker", "Soap", "TSql")]
    public string[]? Type { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        if (SqlInstance is null)
        {
            return;
        }

        foreach (DbaInstanceParameter instance in SqlInstance)
        {
            // [DEF-001] closed via InvokeScopedStreaming (ab7492c). Streaming changes -WhatIf transcript
            // capture (documented observability change, not behaviour); the parity runner strips the
            // transcript gate-message. Fleet-confirmed non-blocker (C's streamed ShouldProcess wave, MSTest 487/487).
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
                new DbaInstanceParameter[] { instance }, SqlCredential, Endpoint, Type,
                EnableException.ToBool(),
                BoundCommonParameter("Verbose"), BoundCommonParameter("Debug"));
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

    // PS: the source process foreach VERBATIM, one element per hop invocation (the
    // source loop line doubles as the guard loop for the Stop-Function -Continue
    // site), CRLF-preserved and cmp-proven byte-exact after stripping the two
    // -FunctionName appends (one Stop-Function, one Write-Message). No begin block,
    // no gates, no Test-Bound.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Endpoint, $Type, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [string[]]$Endpoint, [string[]]$Type, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        foreach ($instance in $SqlInstance) {
            try {
                $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential -MinimumVersion 9
            } catch {
                Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaEndpoint
            }

            $endpoints = $server.Endpoints

            if ($Endpoint) {
                $endpoints = $endpoints | Where-Object Name -In $Endpoint
            }
            if ($Type) {
                $endpoints = $endpoints | Where-Object EndpointType -In $Type
            }

            foreach ($end in $endpoints) {
                Write-Message -Level Verbose -Message "Getting endpoint $($end.Name) on $($server.Name)" -FunctionName Get-DbaEndpoint -ModuleName "dbatools"
                if ($end.Protocol.Tcp.ListenerPort) {
                    if ($end.Protocol.Tcp.ListenerIPAddress -ne [System.Net.IPAddress]'0.0.0.0') {
                        $dns = $end.Protocol.Tcp.ListenerIPAddress
                    } elseif ($server.HostPlatform -eq "Linux" -and $server.NetName) {
                        $dns = $server.NetName
                    } elseif ($server.ComputerName -match '\.') {
                        $dns = $server.ComputerName
                    } else {
                        try {
                            $dns = [System.Net.Dns]::GetHostEntry($server.ComputerName).HostName
                        } catch {
                            try {
                                $dns = [System.Net.Dns]::GetHostAddresses($server.ComputerName)
                            } catch {
                                $dns = $server.ComputerName
                            }
                        }
                    }

                    $fqdn = "TCP://" + $dns + ":" + $end.Protocol.Tcp.ListenerPort
                } else {
                    $fqdn = $null
                }

                Add-Member -Force -InputObject $end -MemberType NoteProperty -Name ComputerName -Value $server.ComputerName
                Add-Member -Force -InputObject $end -MemberType NoteProperty -Name InstanceName -Value $server.ServiceName
                Add-Member -Force -InputObject $end -MemberType NoteProperty -Name SqlInstance -Value $server.DomainInstanceName
                Add-Member -Force -InputObject $end -MemberType NoteProperty -Name Fqdn -Value $fqdn
                Add-Member -Force -InputObject $end -MemberType NoteProperty -Name IPAddress -Value $end.Protocol.Tcp.ListenerIPAddress
                Add-Member -Force -InputObject $end -MemberType NoteProperty -Name Port -Value $end.Protocol.Tcp.ListenerPort
                if ($end.Protocol.Tcp.ListenerPort) {
                    Select-DefaultView -InputObject $end -Property ComputerName, InstanceName, SqlInstance, ID, Name, IPAddress, Port, EndpointState, EndpointType, Owner, IsAdminEndpoint, Fqdn, IsSystemObject
                } else {
                    Select-DefaultView -InputObject $end -Property ComputerName, InstanceName, SqlInstance, ID, Name, EndpointState, EndpointType, Owner, IsAdminEndpoint, Fqdn, IsSystemObject
                }
            }
        }
} $SqlInstance $SqlCredential $Endpoint $Type $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
