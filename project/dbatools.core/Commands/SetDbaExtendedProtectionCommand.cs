#nullable enable

using System;
using System.Collections;
using System.Globalization;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Sets the ExtendedProtection registry value for an instance's network library. Port of
/// public/Set-DbaExtendedProtection.ps1 (W3-092). WHOLE-ARRAY verbatim hop per record -
/// per-element is INELIGIBLE under the amended 25a09f3 rule: the loop carries
/// cross-element state. VFP-LOCAL CLASSIFICATION TABLE: $resolved/$sqlwmi/$regRoot/
/// $vsname/$serviceAccount/$scriptblock are assigned unconditionally (or on paths whose
/// failure -Continues past every read) at the top of each iteration = SAFE;
/// $instanceName is TRY-assigned with a swallowing catch ($null = 1), so a failing
/// DisplayName read leaves the PREVIOUS element's/record's value visible to the later
/// verbose line and the Invoke-Command2 arguments - the source leak rides the
/// whole-array hop within a record and the __w3092State sentinel across records. The
/// begin-block Value normalization (string->int switch) is a pure function of the bound
/// param and rides at hop top (W3-064 law; idempotent per record - an already-int Value
/// passes through). Gates: the private Test-ShouldProcess helper receives the REAL
/// cmdlet ($PSCmdlet -> $__realCmdlet; ConfirmImpact Low mirrored) - no
/// Force/ConfirmPreference convention, no transplant, no template-hold exposure.
/// SqlInstance carries the $env:COMPUTERNAME BIND-TIME default (W1-087/W3-083 class).
/// The WMI AdvancedProperties REGROOT/VSNAME fallback split, the Write-Message -Level
/// Critical success message, the remote Set-ItemProperty scriptblock and the
/// Select-Object -ExcludeProperty PSComputerName scrub ride verbatim. NO WarningAction
/// carrier (codex W3-005 r3). Surface pinned by
/// migration/baselines/Set-DbaExtendedProtection.json (implicit positions 0-2,
/// SqlInstance VFP with default, Value object ValidateSet 0/Off/1/Allowed/2/Required
/// default Off, DefaultParameterSetName Default with no member params).
/// </summary>
[Cmdlet(VerbsCommon.Set, "DbaExtendedProtection", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low, DefaultParameterSetName = "Default")]
public sealed class SetDbaExtendedProtectionCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances; defaults to this computer.</summary>
    [Parameter(ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[] SqlInstance { get; set; } =
        (DbaInstanceParameter[])LanguagePrimitives.ConvertTo(
            Environment.GetEnvironmentVariable("COMPUTERNAME"),
            typeof(DbaInstanceParameter[]), CultureInfo.InvariantCulture);

    /// <summary>Windows credential for the remote WMI/registry work.</summary>
    [Parameter(Position = 1)]
    public PSCredential? Credential { get; set; }

    /// <summary>The ExtendedProtection level (0/Off, 1/Allowed, 2/Required).</summary>
    [Parameter(Position = 2)]
    [ValidateSet("0", "Off", "1", "Allowed", "2", "Required")]
    public object Value { get; set; } = "Off";

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // Cross-record carry for the try-assigned/catch-swallowed $instanceName leak.
    private Hashtable? _state;

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            Hashtable? sentinel = item?.BaseObject as Hashtable;
            if (sentinel is not null && sentinel.ContainsKey("__w3092State"))
            {
                _state = sentinel["__w3092State"] as Hashtable;
                return;
            }
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
                return;
            }
            WriteObject(item);
        }, ProcessScript,
            SqlInstance, Credential, Value, EnableException.ToBool(), _state, this,
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

    // PS: the begin-block Value normalization + the ENTIRE process body VERBATIM per
    // record. Substitutions only: Test-ShouldProcess -Context $PSCmdlet -> $__realCmdlet,
    // explicit -FunctionName Set-DbaExtendedProtection on Stop-Function/Write-Message
    // (W1-090), and the $instanceName cross-record restore/carry through the sentinel.
    // The `$null = 1` catch-swallow, the REGROOT string-split fallback and the remote
    // registry scriptblock ride as-is.
    private const string ProcessScript = """
param($SqlInstance, $Credential, $Value, $EnableException, $__state, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = "Low")]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$Credential, [object]$Value, $EnableException, $__state, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    # cross-record restore: the try-assigned/catch-swallowed $instanceName leak
    if ($null -ne $__state) {
        $instanceName = $__state.instanceName
    }

    # ATTRIBUTION SHIM (the W3-084 Get-PSCallStack class): Test-ElevationRequirement
    # stamps its Stop-Function with (Get-PSCallStack)[1].Command - the CALLER frame.
    # Called bare from this hop that frame is the scriptblock => [<ScriptBlock>]
    # warning prefix + FQEID dbatools_<ScriptBlock>. The named wrapper restores the
    # source's own frame; the helper's dot-sourced Stop-Function -Continue flow control
    # unwinds through the wrapper to the foreach exactly as through the source frame.
    function Set-DbaExtendedProtection {
        param($__splat)
        Test-ElevationRequirement @__splat
    }

    # Check value and set the integer value
    if (($Value -notin 0, 1, 2) -and ($null -ne $Value)) {
        $Value = switch ($Value) { "Off" { 0 } "Allowed" { 1 } "Required" { 2 } }
    }

    foreach ($instance in $SqlInstance) {
        Write-Message -Level VeryVerbose -Message "Processing $instance." -Target $instance -FunctionName Set-DbaExtendedProtection -ModuleName "dbatools"
        if ($instance.IsLocalHost) {
            $null = Set-DbaExtendedProtection -__splat @{ ComputerName = $instance; Continue = $true }
        }

        try {
            $resolved = Resolve-DbaNetworkName -ComputerName $instance -Credential $Credential -EnableException
        } catch {
            try {
                $resolved = Resolve-DbaNetworkName -ComputerName $instance -Credential $Credential -Turbo -EnableException
            } catch {
                Stop-Function -Message "Issue resolving $instance" -Target $instance -Category InvalidArgument -Continue -FunctionName Set-DbaExtendedProtection
            }
        }

        try {
            $sqlwmi = Invoke-ManagedComputerCommand -ComputerName $resolved.FullComputerName -ScriptBlock { $wmi.Services } -Credential $Credential -EnableException | Where-Object DisplayName -eq "SQL Server ($($instance.InstanceName))"
        } catch {
            Stop-Function -Message "Failed to access $instance" -Target $instance -Continue -ErrorRecord $_ -FunctionName Set-DbaExtendedProtection
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
                Stop-Function -Message "Can't find instance $vsname on $instance." -Continue -Category ObjectNotFound -Target $instance -FunctionName Set-DbaExtendedProtection
            }
        }

        if ([System.String]::IsNullOrEmpty($vsname)) { $vsname = $instance }

        Write-Message -Level Verbose -Message "Regroot: $regRoot" -Target $instance -FunctionName Set-DbaExtendedProtection -ModuleName "dbatools"
        Write-Message -Level Verbose -Message "ServiceAcct: $serviceaccount" -Target $instance -FunctionName Set-DbaExtendedProtection -ModuleName "dbatools"
        Write-Message -Level Verbose -Message "InstanceName: $instancename" -Target $instance -FunctionName Set-DbaExtendedProtection -ModuleName "dbatools"
        Write-Message -Level Verbose -Message "VSNAME: $vsname" -Target $instance -FunctionName Set-DbaExtendedProtection -ModuleName "dbatools"
        Write-Message -Level Verbose -Message "Value: $Value" -Target $instance -FunctionName Set-DbaExtendedProtection -ModuleName "dbatools"

        $scriptblock = {
            $regPath = "Registry::HKEY_LOCAL_MACHINE\$($args[0])\MSSQLServer\SuperSocketNetLib"
            Set-ItemProperty -Path $regPath -Name ExtendedProtection -Value $args[3]
            $extendedProtection = (Get-ItemProperty -Path $regPath -Name ExtendedProtection).ExtendedProtection

            [PSCustomObject]@{
                ComputerName       = $env:COMPUTERNAME
                InstanceName       = $args[2]
                SqlInstance        = $args[1]
                ExtendedProtection = "$extendedProtection - $(switch ($extendedProtection) { 0 { "Off" } 1 { "Allowed" } 2 { "Required" } })"
            }
        }
        if (Test-ShouldProcess -Context $__realCmdlet -Target "local" -Action "Connecting to $instance to modify the ExtendedProtection value in $regRoot for $($instance.InstanceName)") {
            try {
                Invoke-Command2 -ComputerName $resolved.FullComputerName -Credential $Credential -ArgumentList $regRoot, $vsname, $instancename, $Value -ScriptBlock $scriptblock -ErrorAction Stop | Select-Object -Property * -ExcludeProperty PSComputerName, RunspaceId, PSShowComputerName
                Write-Message -Level Critical -Message "ExtendedProtection was successfully set on $($resolved.FullComputerName) for the $instancename instance. The change takes effect immediately for new connections." -Target $instance -FunctionName Set-DbaExtendedProtection -ModuleName "dbatools"
            } catch {
                Stop-Function -Message "Failed to connect to $($resolved.FullComputerName) using PowerShell remoting" -ErrorRecord $_ -Target $instance -Continue -FunctionName Set-DbaExtendedProtection
            }
        }
    }

    @{ __w3092State = @{ instanceName = $instanceName } }
} $SqlInstance $Credential $Value $EnableException $__state $__realCmdlet $__boundWhatIf $__boundConfirm $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
