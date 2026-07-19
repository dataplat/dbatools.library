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

        // PS: [DbaInstanceParameter[]]$SqlInstance = $env:COMPUTERNAME - apply the default when
        // the parameter is unbound (a compiled parameter cannot default to a runtime expression).
        DbaInstanceParameter[]? instances = SqlInstance;
        if (instances is null || instances.Length == 0)
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

    // PS: the process body VERBATIM per record (no begin/end blocks). Substitutions only:
    // $PScmdlet -> $__realCmdlet, explicit -FunctionName Disable-DbaHideInstance on Stop-Function
    // (W1-090). SqlInstance arrives already defaulted to the computer name by ProcessRecord.
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

    foreach ($instance in $SqlInstance) {
        Write-Message -Level VeryVerbose -Message "Processing $instance." -Target $instance
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

        Write-Message -Level Verbose -Message "Regroot: $regRoot" -Target $instance
        Write-Message -Level Verbose -Message "ServiceAcct: $serviceAccount" -Target $instance
        Write-Message -Level Verbose -Message "InstanceName: $instanceName" -Target $instance
        Write-Message -Level Verbose -Message "VSNAME: $vsname" -Target $instance

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
                Write-Message -Level Critical -Message "HideInstance was successfully disabled on $($resolved.FullComputerName) for the $instanceName instance. The change takes effect immediately for new connections." -Target $instance
            } catch {
                Stop-Function -Message "Failed to connect to $($resolved.FullComputerName) using PowerShell remoting" -ErrorRecord $_ -Target $instance -Continue -FunctionName Disable-DbaHideInstance
            }
        }
    }
} $SqlInstance $Credential $EnableException $__realCmdlet $__boundWhatIf $__boundConfirm $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
