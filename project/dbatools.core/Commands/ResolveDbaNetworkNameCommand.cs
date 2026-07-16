#nullable enable

using System;
using System.Collections;
using System.Globalization;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Resolves computer names to network identity details (DNS + CIM). Port of
/// public/Resolve-DbaNetworkName.ps1 (W3-083). NO ShouldProcess (plain CmdletBinding) -
/// no WhatIf/Confirm carriers, no gate routing. PER-ELEMENT process hops (25a09f3
/// ruling): the source foreaches $ComputerName with no cross-element state, so each
/// element rides its own hop; the pre-loop bypass-config check and the non-Windows Turbo
/// coercion are idempotent per element and ride at the top of every hop (verbatim body,
/// 1-element array). The begin-scope Get-ComputerDomainName helper is re-declared at hop
/// top (function definitions cannot cross hop scopes). DOT-SOURCED inner block (the
/// bypass path ends in an early `return`). The $env:COMPUTERNAME bind-time default
/// applies at construction exactly like PS bind-time defaults (W1-087/W3-063 class).
/// Private Test-Windows/Invoke-Command2 and public Get-DbaCmObject/config reads ride the
/// hop verbatim, including the hardcoded -EnableException on Get-DbaCmObject inside its
/// own try/catch. NO WarningAction carrier (codex W3-005 r3). Surface pinned by
/// migration/baselines/Resolve-DbaNetworkName.json (implicit positions 0-1, ComputerName
/// pos0 VFP with default, Turbo Alias FastParrot, no ShouldProcess).
/// </summary>
[Cmdlet(VerbsDiagnostic.Resolve, "DbaNetworkName")]
public sealed class ResolveDbaNetworkNameCommand : DbaBaseCmdlet
{
    /// <summary>The computer(s) to resolve; defaults to this computer.</summary>
    [Parameter(ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[] ComputerName { get; set; } =
        (DbaInstanceParameter[])LanguagePrimitives.ConvertTo(
            Environment.GetEnvironmentVariable("COMPUTERNAME"),
            typeof(DbaInstanceParameter[]), CultureInfo.InvariantCulture);

    /// <summary>Credential for the remote CIM/WMI queries.</summary>
    [Parameter(Position = 1)]
    public PSCredential? Credential { get; set; }

    /// <summary>DNS-resolution-only fast path.</summary>
    [Parameter]
    [Alias("FastParrot")]
    public SwitchParameter Turbo { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        // Stream one hop PER COMPUTER: a whole-array hop batches every element's live
        // Debug/Verbose ahead of all buffered output, where the source's foreach
        // interleaves them per element (W2-010 P2A; coordinator 25a09f3 ruling). The
        // source loop body has no cross-element state; the pre-loop bypass/Turbo lines
        // are idempotent per element.
        foreach (DbaInstanceParameter computer in ComputerName ?? Array.Empty<DbaInstanceParameter>())
        {
            if (Interrupted)
                return;

            foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
                new[] { computer }, Credential, Turbo.ToBool(), EnableException.ToBool(),
                BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
            {
                if (item?.BaseObject is ErrorRecord nestedError)
                {
                    RemoveHopErrorBookkeeping(nestedError);
                    WriteError(nestedError);
                    continue;
                }
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

    // PS: the begin-scope helper + the ENTIRE process body VERBATIM per element inside a
    // dot-sourced block (the bypass path early-returns). Substitutions only: explicit
    // -FunctionName Resolve-DbaNetworkName on Stop-Function/Write-Message (W1-090). The
    // $Turbo in-hop coercion on non-Windows, the commented-out DNSDomain lines, and the
    // hardcoded -EnableException on Get-DbaCmObject ride as-is.
    private const string ProcessScript = """
param($ComputerName, $Credential, $Turbo, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$ComputerName, [PSCredential]$Credential, $Turbo, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    Function Get-ComputerDomainName {
        Param (
            $FQDN,
            $ComputerName
        )
        # deduce the domain name based on resolved name + original request
        if ($fqdn -notmatch "\.") {
            if ($ComputerName -match "\.") {
                return $ComputerName.Substring($ComputerName.IndexOf(".") + 1)
            } else {
                return "$env:USERDNSDOMAIN".ToLowerInvariant()
            }
        } else {
            return $fqdn.Substring($fqdn.IndexOf(".") + 1)
        }
    }

    . {
        if ((Get-DbatoolsConfigValue -FullName commands.resolve-dbanetworkname.bypass)) {
            foreach ($computer in $ComputerName) {
                [PSCustomObject]@{
                    InputName        = $computer
                    ComputerName     = $computer
                    IPAddress        = $computer
                    DNSHostname      = $computer
                    DNSDomain        = $computer # (Get-ComputerDomainName -ComputerName $computer)
                    Domain           = $computer # (Get-ComputerDomainName -ComputerName $computer)
                    DNSHostEntry     = $computer
                    FQDN             = $computer
                    FullComputerName = $computer
                }
                continue
            }
            return
        }

        if (-not (Test-Windows -NoWarn)) {
            Write-Message -Level Verbose -Message "Non-Windows client detected. Turbo (DNS resolution only) set to $true" -FunctionName Resolve-DbaNetworkName
            $Turbo = $true
        }

        foreach ($computer in $ComputerName) {
            if ($computer.IsLocalhost) {
                $cName = $env:COMPUTERNAME
            } else {
                $cName = $computer.ComputerName
            }

            # resolve IP address
            try {
                Write-Message -Level VeryVerbose -Message "Resolving $cName using .NET.Dns GetHostEntry" -FunctionName Resolve-DbaNetworkName
                $resolved = [System.Net.Dns]::GetHostEntry($cName)
                $ipaddresses = $resolved.AddressList | Sort-Object -Property AddressFamily # prioritize IPv4
                $ipaddress = $ipaddresses[0].IPAddressToString
            } catch {
                Stop-Function -Message "DNS name $cName not found" -Continue -ErrorRecord $_ -FunctionName Resolve-DbaNetworkName
            }

            # try to resolve IP into a hostname
            try {
                Write-Message -Level VeryVerbose -Message "Resolving $ipaddress using .NET.Dns GetHostByAddress" -FunctionName Resolve-DbaNetworkName
                $fqdn = [System.Net.Dns]::GetHostByAddress($ipaddress).HostName
            } catch {
                Write-Message -Level Debug -Message "Failed to resolve $ipaddress using .NET.Dns GetHostByAddress" -FunctionName Resolve-DbaNetworkName
                $fqdn = $resolved.HostName
            }

            $dnsDomain = Get-ComputerDomainName -FQDN $fqdn -ComputerName $cName
            # augment fqdn if needed
            if ($fqdn -notmatch "\." -and $dnsDomain) {
                $fqdn = "$fqdn.$dnsdomain"
            }
            $hostname = $fqdn.Split(".")[0]

            # create an output object with some preliminary data gathered so far
            $result = [PSCustomObject]@{
                InputName        = $computer
                ComputerName     = $hostname.ToUpper()
                IPAddress        = $ipaddress
                DNSHostname      = $hostname
                DNSDomain        = $dnsdomain
                Domain           = $dnsdomain
                DNSHostEntry     = $fqdn
                FQDN             = $fqdn
                FullComputerName = $cName
            }
            if ($Turbo) {
                # that's a finish line for a Turbo mode
                $result
                continue
            }

            # finding out which IP to use by pinging all of them. The first to respond is the one.
            $ping = New-Object System.Net.NetworkInformation.Ping
            $timeout = 1000 #milliseconds
            foreach ($ip in $ipaddresses) {
                $reply = $ping.Send($ip, $timeout)
                if ($reply.Status -eq 'Success') {
                    $ipaddress = $ip.IPAddressToString
                    break
                }
            }
            $result.IPAddress = $ipaddress

            # re-try DNS reverse zone lookup if the IP to use is not the first one
            if ($ipaddresses[0].IPAddressToString -ne $ipaddress) {
                try {
                    Write-Message -Level VeryVerbose -Message "Resolving $ipaddress using .NET.Dns GetHostByAddress" -FunctionName Resolve-DbaNetworkName
                    $fqdn = [System.Net.Dns]::GetHostByAddress($ipaddress).HostName
                    # re-adjust DNS domain again
                    $dnsDomain = Get-ComputerDomainName -FQDN $fqdn -ComputerName $cName
                    # augment fqdn if needed
                    if ($fqdn -notmatch "\." -and $dnsDomain) {
                        $fqdn = "$fqdn.$dnsdomain"
                    }
                    $hostname = $fqdn.Split(".")[0]

                    # update result fields accordingly
                    $result.ComputerName = $hostname.ToUpper()
                    $result.DNSHostname = $hostname
                    $result.DNSDomain = $dnsdomain
                    $result.Domain = $dnsdomain
                    $result.DNSHostEntry = $fqdn
                    $result.FQDN = $fqdn
                } catch {
                    Write-Message -Level VeryVerbose -Message "Failed to obtain a new name from $ipaddress, re-using $fqdn" -FunctionName Resolve-DbaNetworkName
                }
            }


            Write-Message -Level Debug -Message "Getting domain name from the remote host $fqdn" -FunctionName Resolve-DbaNetworkName
            try {
                $ScBlock = {
                    return [System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().DomainName
                }
                $cParams = @{
                    ComputerName = $cName
                }
                if ($Credential) { $cParams.Credential = $Credential }

                $conn = Get-DbaCmObject @cParams -ClassName win32_ComputerSystem -EnableException
                if ($conn) {
                    # update results accordingly
                    $result.ComputerName = $conn.Name
                    $dnsHostname = $conn.DNSHostname
                    $dnsDomain = $conn.Domain
                    $result.FQDN = "$dnsHostname.$dnsDomain".TrimEnd('.')
                    $result.DNSHostName = $dnsHostname
                    $result.Domain = $dnsDomain
                }
                try {
                    Write-Message -Level Debug -Message "Getting DNS domain from the remote host $($cParams.ComputerName)" -FunctionName Resolve-DbaNetworkName
                    $dnsSuffix = Invoke-Command2 @cParams -ScriptBlock $ScBlock -ErrorAction Stop -Raw
                    $result.DNSDomain = $dnsSuffix
                    if ($dnsSuffix) {
                        $fullComputerName = $result.DNSHostName + "." + $dnsSuffix
                    } else {
                        $fullComputerName = $result.DNSHostName
                    }
                    $result.FullComputerName = $fullComputerName
                } catch {
                    Write-Message -Level Verbose -Message "Unable to get DNS domain information from $($cParams.ComputerName)" -FunctionName Resolve-DbaNetworkName
                }
            } catch {
                Write-Message -Level Verbose -Message "Unable to get domain name from $($cParams.ComputerName)" -FunctionName Resolve-DbaNetworkName
            }

            # getting a DNS host entry for the full name
            try {
                Write-Message -Level VeryVerbose -Message "Resolving $($result.FullComputerName) using .NET.Dns GetHostEntry" -FunctionName Resolve-DbaNetworkName
                $result.DNSHostEntry = ([System.Net.Dns]::GetHostEntry($result.FullComputerName)).HostName
            } catch {
                Write-Message -Level Verbose -Message ".NET.Dns GetHostEntry failed for $($result.FullComputerName)" -FunctionName Resolve-DbaNetworkName
            }

            # returning the final result
            $result
        }
    }
} $ComputerName $Credential $Turbo $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
