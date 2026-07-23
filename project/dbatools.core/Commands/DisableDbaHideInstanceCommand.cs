#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Disables the HideInstance flag for SQL Server instances. Port of
/// public/Disable-DbaHideInstance.ps1 (W3-007). Pure per-record process command with no
/// begin/end blocks, so the whole process body runs as one streaming hop. DEF-001 cond1+cond2:
/// the process foreach EMITS a [PSCustomObject] per instance (returned by the Invoke-Command2
/// remote scriptblock) AND has reachable Stop-Function -Continue at Resolve-DbaNetworkName /
/// Invoke-ManagedComputerCommand / the REGROOT lookup / Invoke-Command2, so the hop STREAMS via
/// InvokeScopedStreaming - a buffered hop would lose an earlier instance's emit when a later
/// instance throws under -EnableException. The source's SqlInstance default ($env:COMPUTERNAME)
/// is applied in ProcessRecord when the parameter is unbound (the GetDbaClientAlias pattern),
/// since a compiled parameter cannot default to a runtime expression. Substitutions only:
/// $PScmdlet.ShouldProcess -> $__realCmdlet.ShouldProcess (ConfirmImpact LOW mirrored) and
/// explicit -FunctionName Disable-DbaHideInstance on every Stop-Function (W1-090); the body is
/// otherwise verbatim. Surface pinned by migration/baselines/Disable-DbaHideInstance.json.
/// </summary>
[Cmdlet(VerbsLifecycle.Disable, "DbaHideInstance", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low, DefaultParameterSetName = "Default")]
public sealed class DisableDbaHideInstanceCommand : DbaBaseCmdlet
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

        // PS: [DbaInstanceParameter[]]$SqlInstance = $env:COMPUTERNAME - apply the default ONLY
        // when the parameter is genuinely ABSENT (a compiled parameter cannot default to a
        // runtime expression). An EXPLICITLY bound $null/@() must NOT fall back to localhost:
        // the function world's foreach over the bound empty value is a no-op, and defaulting
        // here would touch the local registry the caller never named (codex).
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
                NestedCommand.RemoveDuplicateError(this, nestedError);
                WriteError(nestedError);
                return;
            }
            if (item?.Properties["__dbatoolsDhiNameCarrier"] is not null &&
                LanguagePrimitives.IsTrue(item.Properties["__dbatoolsDhiNameCarrier"].Value))
            {
                // DEF-011/012: $instanceName's cross-record persistence (see ProcessScript note).
                // Mirrors the Enable-DbaHideInstance sibling (codex return-sweep parity catch).
                _carriedInstanceName = item.Properties["InstanceName"]?.Value;
                return;
            }
            WriteObject(item);
        }, ProcessScript,
            instances, Credential, EnableException.ToBool(), this,
            NestedCommand.BoundCommonParameter(this, "WhatIf"), NestedCommand.BoundCommonParameter(this, "Confirm"),
            NestedCommand.BoundCommonParameter(this, "Verbose"), NestedCommand.BoundCommonParameter(this, "Debug"),
            _carriedInstanceName);
    }

    private object? _carriedInstanceName;

    // PS: the process body VERBATIM per record (no begin/end blocks). Substitutions only:
    // $PScmdlet -> $__realCmdlet, explicit -FunctionName Disable-DbaHideInstance on Stop-Function
    // (W1-090). SqlInstance arrives already defaulted to the computer name by ProcessRecord.
    private const string ProcessScript = """
param($SqlInstance, $Credential, $EnableException, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug, $__carriedInstanceName)
$__commonParameters = @{}
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = "Low")]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$Credential, $EnableException, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug, $__carriedInstanceName)
    # DEF-011/012 (codex return-sweep): the source's $instanceName persists across pipeline records -
    # if a later record's .Replace() throws, the function world retains the PRIOR record's value while
    # a fresh hop scope would use $null. Seed from the carrier; updated post-assignment (Enable sibling parity).
    if ($null -ne $__carriedInstanceName) { $instanceName = $__carriedInstanceName }
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    foreach ($instance in $SqlInstance) {
        Write-Message -Level VeryVerbose -Message "Processing $instance." -Target $instance -FunctionName Disable-DbaHideInstance -ModuleName "dbatools"
        if ($instance.IsLocalHost) {
            $null = Test-ElevationRequirement -ComputerName $instance -Continue
        }

        try {
            $resolved = Resolve-DbaNetworkName -ComputerName $instance -Credential $Credential -EnableException
        } catch {
            try {
                $resolved = Resolve-DbaNetworkName -ComputerName $instance -Credential $Credential -Turbo -EnableException
            } catch {
                Stop-Function -Message "Issue resolving $instance" -Target $instance -Category InvalidArgument -Continue -FunctionName Disable-DbaHideInstance
            }
        }

        try {
            $sqlwmi = Invoke-ManagedComputerCommand -ComputerName $resolved.FullComputerName -ScriptBlock { $wmi.Services } -Credential $Credential -EnableException | Where-Object DisplayName -eq "SQL Server ($($instance.InstanceName))"
        } catch {
            Stop-Function -Message "Failed to access $instance" -Target $instance -Continue -ErrorRecord $_ -FunctionName Disable-DbaHideInstance
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
                Stop-Function -Message "Can't find instance $vsname on $instance." -Continue -Category ObjectNotFound -Target $instance -FunctionName Disable-DbaHideInstance
            }
        }

        if ([System.String]::IsNullOrEmpty($vsname)) { $vsname = $instance }

        Write-Message -Level Verbose -Message "Regroot: $regRoot" -Target $instance -FunctionName Disable-DbaHideInstance -ModuleName "dbatools"
        Write-Message -Level Verbose -Message "ServiceAcct: $serviceAccount" -Target $instance -FunctionName Disable-DbaHideInstance -ModuleName "dbatools"
        Write-Message -Level Verbose -Message "InstanceName: $instanceName" -Target $instance -FunctionName Disable-DbaHideInstance -ModuleName "dbatools"
        Write-Message -Level Verbose -Message "VSNAME: $vsname" -Target $instance -FunctionName Disable-DbaHideInstance -ModuleName "dbatools"

        $scriptBlock = {
            $regPath = "Registry::HKEY_LOCAL_MACHINE\$($args[0])\MSSQLServer\SuperSocketNetLib"
            Set-ItemProperty -Path $regPath -Name HideInstance -Value $false
            $HideInstance = (Get-ItemProperty -Path $regPath -Name HideInstance).HideInstance

            [PSCustomObject]@{
                ComputerName = $env:COMPUTERNAME
                InstanceName = $args[2]
                SqlInstance  = $args[1]
                HideInstance = ($HideInstance -eq $true)
            }
        }

        if ($__realCmdlet.ShouldProcess("local", "Connecting to $instance to modify the HideInstance value in $regRoot for $($instance.InstanceName)")) {
            try {
                Invoke-Command2 -ComputerName $resolved.FullComputerName -Credential $Credential -ArgumentList $regRoot, $vsname, $instanceName -ScriptBlock $scriptBlock -ErrorAction Stop
                Write-Message -Level Critical -Message "HideInstance was successfully disabled on $($resolved.FullComputerName) for the $instanceName instance. The change takes effect immediately for new connections." -Target $instance -FunctionName Disable-DbaHideInstance -ModuleName "dbatools"
            } catch {
                Stop-Function -Message "Failed to connect to $($resolved.FullComputerName) using PowerShell remoting" -ErrorRecord $_ -Target $instance -Continue -FunctionName Disable-DbaHideInstance
            }
        }
    }

    # DEF-011/012: emit the final $instanceName so the C# host carries it into the next record's hop
    # (Enable-DbaHideInstance sibling parity). The sentinel is consumed by the streaming handler, never emitted.
    [pscustomobject]@{ __dbatoolsDhiNameCarrier = $true; InstanceName = $instanceName }
} $SqlInstance $Credential $EnableException $__realCmdlet $__boundWhatIf $__boundConfirm $__boundVerbose $__boundDebug $__carriedInstanceName @__commonParameters 3>&1 2>&1
""";
}
