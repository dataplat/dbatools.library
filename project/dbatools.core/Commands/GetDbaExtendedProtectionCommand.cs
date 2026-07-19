#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Gets the SQL Server Extended Protection setting. Port of
/// public/Get-DbaExtendedProtection.ps1 (W3-032). Shares the resolve / WMI / registry boilerplate of
/// the HideInstance family (W3-007/011/034) but reads ExtendedProtection. Pure per-record process
/// command with no begin/end blocks. DEF-001 cond1+cond2: the process foreach EMITS a [PSCustomObject]
/// per instance (from the Invoke-Command2 remote read) AND has reachable Stop-Function -Continue at
/// Resolve-DbaNetworkName / Invoke-ManagedComputerCommand / the REGROOT lookup / Invoke-Command2, so
/// the hop STREAMS via InvokeScopedStreaming. Cross-record-state check: $instanceName is conditionally
/// set (the DisplayName.Replace try), but its stale value is UNREACHABLE - the stale case (null
/// DisplayName) coincides with an empty $regRoot, which Stop-Function -Continues before the emit - so
/// no carrier is needed (unlike W3-006/010's reachable $result). The SqlInstance default
/// ($env:COMPUTERNAME) is applied in ProcessRecord ONLY when the parameter was not explicitly bound.
/// Intentional rewrites (the body is otherwise verbatim): $PScmdlet.ShouldProcess ->
/// $__realCmdlet.ShouldProcess (ConfirmImpact LOW mirrored); explicit -FunctionName
/// Get-DbaExtendedProtection on Stop-Function (W1-090); -FunctionName/-ModuleName on the five
/// direct Write-Message calls (DEF-006); and the W3-084 named-wrapper shim around
/// Test-ElevationRequirement (Get-PSCallStack caller-frame attribution, matching the Set
/// sibling). Surface pinned by migration/baselines/Get-DbaExtendedProtection.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaExtendedProtection", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low, DefaultParameterSetName = "Default")]
public sealed class GetDbaExtendedProtectionCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances. Defaults to the local computer.</summary>
    [Parameter(Position = 0, ValueFromPipeline = true)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative Windows credential for the target server.</summary>
    [Parameter(Position = 1)]
    public PSCredential? Credential { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        // PS: [DbaInstanceParameter[]]$SqlInstance = $env:COMPUTERNAME - apply the default ONLY when
        // the parameter was not explicitly bound (an explicit $null/@() must NOT fall back to localhost).
        DbaInstanceParameter[]? instances = SqlInstance;
        if (!MyInvocation.BoundParameters.ContainsKey("SqlInstance") && (instances is null || instances.Length == 0))
        {
            string? machine = Environment.GetEnvironmentVariable("COMPUTERNAME");
            if (!string.IsNullOrEmpty(machine))
                instances = new[] { new DbaInstanceParameter(machine) };
        }

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
            instances, Credential, EnableException.ToBool(), this,
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

    // PS: the process body per record (no begin/end blocks). Intentional rewrites: $PScmdlet ->
    // $__realCmdlet; -FunctionName on Stop-Function (W1-090); DEF-006 attribution on the five
    // Write-Message calls; the W3-084 elevation-shim wrapper. SqlInstance arrives already
    // defaulted to the computer name by ProcessRecord.
    private const string ProcessScript = """
param($SqlInstance, $Credential, $EnableException, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = "Low")]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$Credential, $EnableException, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }
    # ATTRIBUTION SHIM (the W3-084 Get-PSCallStack class): Test-ElevationRequirement stamps
    # its Stop-Function with (Get-PSCallStack)[1].Command - the CALLER frame. Called bare
    # from this hop that frame is the scriptblock => [<ScriptBlock>] attribution. The named
    # wrapper restores the source's own frame (same shim as the Set sibling); -Continue flow
    # control unwinds through the wrapper to the foreach exactly as through the source frame.
    function Get-DbaExtendedProtection {
        param($__splat)
        Test-ElevationRequirement @__splat
    }


    foreach ($instance in $SqlInstance) {
        Write-Message -Level VeryVerbose -Message "Processing $instance." -FunctionName Get-DbaExtendedProtection -ModuleName "dbatools" -Target $instance
        if ($instance.IsLocalHost) {
            $null = Get-DbaExtendedProtection @{ ComputerName = $instance; Continue = $true }
        }

        try {
            $resolved = Resolve-DbaNetworkName -ComputerName $instance -Credential $Credential -EnableException
        } catch {
            try {
                $resolved = Resolve-DbaNetworkName -ComputerName $instance -Credential $Credential -Turbo -EnableException
            } catch {
                Stop-Function -Message "Issue resolving $instance" -Target $instance -Category InvalidArgument -Continue -FunctionName Get-DbaExtendedProtection
            }
        }

        try {
            $sqlwmi = Invoke-ManagedComputerCommand -ComputerName $resolved.FullComputerName -ScriptBlock { $wmi.Services } -Credential $Credential -EnableException | Where-Object DisplayName -eq "SQL Server ($($instance.InstanceName))"
        } catch {
            Stop-Function -Message "Failed to access $instance" -Target $instance -Continue -ErrorRecord $_ -FunctionName Get-DbaExtendedProtection
        }

        $regRoot = ($sqlwmi.AdvancedProperties | Where-Object Name -eq REGROOT).Value
        $vsname = ($sqlwmi.AdvancedProperties | Where-Object Name -eq VSNAME).Value
        try {
            $instanceName = $sqlwmi.DisplayName.Replace('SQL Server (', '').Replace(')', '')
        } catch {
            $null = 1
        }
        $serviceAccount = $sqlwmi.ServiceAccount

        if ([System.String]::IsNullOrEmpty($regRoot)) {
            $regRoot = $sqlwmi.AdvancedProperties | Where-Object { $_ -match 'REGROOT' }
            $vsname = $sqlwmi.AdvancedProperties | Where-Object { $_ -match 'VSNAME' }

            if (![System.String]::IsNullOrEmpty($regRoot)) {
                $regRoot = ($regRoot -Split 'Value\=')[1]
                $vsname = ($vsname -Split 'Value\=')[1]
            } else {
                Stop-Function -Message "Can't find instance $vsname on $instance." -Continue -Category ObjectNotFound -Target $instance -FunctionName Get-DbaExtendedProtection
            }
        }

        if ([System.String]::IsNullOrEmpty($vsname)) { $vsname = $instance }

        Write-Message -Level Verbose -Message "Regroot: $regRoot" -FunctionName Get-DbaExtendedProtection -ModuleName "dbatools" -Target $instance
        Write-Message -Level Verbose -Message "ServiceAcct: $serviceAccount" -FunctionName Get-DbaExtendedProtection -ModuleName "dbatools" -Target $instance
        Write-Message -Level Verbose -Message "InstanceName: $instanceName" -FunctionName Get-DbaExtendedProtection -ModuleName "dbatools" -Target $instance
        Write-Message -Level Verbose -Message "VSNAME: $vsname" -FunctionName Get-DbaExtendedProtection -ModuleName "dbatools" -Target $instance

        $scriptblock = {
            $regPath = "Registry::HKEY_LOCAL_MACHINE\$($args[0])\MSSQLServer\SuperSocketNetLib"
            $extendedProtection = (Get-ItemProperty -Path $regPath -Name ExtendedProtection).ExtendedProtection

            [PSCustomObject]@{
                ComputerName       = $env:COMPUTERNAME
                InstanceName       = $args[2]
                SqlInstance        = $args[1]
                ExtendedProtection = "$extendedProtection - $(switch ($extendedProtection) { 0 { "Off" } 1 { "Allowed" } 2 { "Required" } })"
            }
        }

        if ($__realCmdlet.ShouldProcess("local", "Connecting to $instance to modify the ExtendedProtection value in $regRoot for $($instance.InstanceName)")) {
            try {
                Invoke-Command2 -ComputerName $resolved.FullComputerName -Credential $Credential -ArgumentList $regRoot, $vsname, $instanceName -ScriptBlock $scriptblock -ErrorAction Stop | Select-Object -Property * -ExcludeProperty PSComputerName, RunspaceId, PSShowComputerName
            } catch {
                Stop-Function -Message "Failed to connect to $($resolved.FullComputerName) using PowerShell remoting" -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaExtendedProtection
            }
        }
    }
} $SqlInstance $Credential $EnableException $__realCmdlet $__boundWhatIf $__boundConfirm $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
