#nullable enable

using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Disables the HADR service setting on SQL Server instances via WMI, optionally
/// restarting the Engine and Agent services to apply it. Port of
/// public/Disable-DbaAgHadr.ps1; surface pinned by
/// migration/baselines/Disable-DbaAgHadr.json.
/// </summary>
[Cmdlet(VerbsLifecycle.Disable, "DbaAgHadr", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.High)]
public sealed class DisableDbaAgHadrCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Connect to the Windows server as a different user.</summary>
    [Parameter(Position = 1)]
    public PSCredential? Credential { get; set; }

    /// <summary>Restarts Engine and Agent services to apply the change immediately.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

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

        // WHOLE-ARRAY hop (the W3-005 Copy-family convention, coordinator-ruled): the
        // source's begin block sets fn-scope $ConfirmPreference = 'none' under -Force
        // and both ShouldProcess gates run on the INNER hop scriptblock's $Pscmdlet,
        // so [A]/[L] prompt state must persist across the record's instances - one
        // scriptblock invocation per record reproduces that; per-element would reset
        // the CommandRuntime per instance. The source's process-scope `return` on the
        // elevation check likewise exits the whole record in both worlds.
        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, Credential, Force.ToBool(), TestBound(nameof(Force)),
            EnableException.ToBool(),
            BoundCommonParameter("WhatIf"), BoundCommonParameter("Confirm"),
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

    // PS: the begin-block Force -> ConfirmPreference suppression rides verbatim at the
    // hop top (pure function of bound params, idempotent per record), then the source
    // process foreach VERBATIM. ShouldProcess gates use the INNER block's own $Pscmdlet
    // (W3-005 Copy-family convention). Substitutions: six -FunctionName appends on the
    // uncommented Stop-Function/Write-Message sites (the Write-Message inside the
    // source's commented-out block is untouched) and two Test-Bound -> $__boundForce
    // flag rewrites with SOURCE comments (Test-Bound scope-walks and cannot ride a
    // hop); stripping both reproduces the source bytes md5-exact.
    private const string ProcessScript = """
param($SqlInstance, $Credential, $Force, $__boundForce, $EnableException, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = "High")]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$Credential, $Force, $__boundForce, $EnableException, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    if ($Force) { $ConfirmPreference = 'none' }

        foreach ($instance in $SqlInstance) {
            $computer = $computerFullName = $instance.ComputerName
            $instanceName = $instance.InstanceName
            if (-not (Test-ElevationRequirement -ComputerName $instance)) {
                return
            }
            $noChange = $false

            <#
            #Variable marked as unused by PSScriptAnalyzer
            switch ($instance.InstanceName) {
                'MSSQLSERVER' { $agentName = 'SQLSERVERAGENT' }
                default { $agentName = "SQLAgent`$$instanceName" }
            }
            #>

            try {
                Write-Message -Level Verbose -Message "Checking current Hadr setting for $computer" -FunctionName Disable-DbaAgHadr
                $currentState = Get-WmiHadr -SqlInstance $instance -Credential $Credential
            } catch {
                Stop-Function -Message "Failure to pull current state of Hadr setting on $computer" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Disable-DbaAgHadr
            }

            $isHadrEnabled = $currentState.IsHadrEnabled
            Write-Message -Level InternalComment -Message "$instance Hadr current value: $isHadrEnabled" -FunctionName Disable-DbaAgHadr

            # hadr results from sql wmi can be iffy, skip the check
            <#
            if (-not $isHadrEnabled) {
                Write-Message -Level Warning -Message "Hadr is already disabled for instance: $($instance.FullName)"
                $noChange = $true
                continue
            }
            #>

            $scriptBlock = {
                $instance = $args[0]
                $sqlService = $wmi.Services | Where-Object DisplayName -eq "SQL Server ($instance)"
                $sqlService.ChangeHadrServiceSetting(0)
            }

            if ($noChange -eq $false) {
                if ($PSCmdlet.ShouldProcess($instance, "Changing Hadr from $isHadrEnabled to 0 for $instance")) {
                    try {
                        Invoke-ManagedComputerCommand -ComputerName $computerFullName -Credential $Credential -ScriptBlock $scriptBlock -ArgumentList $instanceName
                    } catch {
                        Stop-Function -Continue -Message "Failure on $($instance.FullName) | This may be because AlwaysOn Availability Groups feature requires the x86(non-WOW) or x64 Enterprise Edition of SQL Server 2012 (or later version) running on Windows Server 2008 (or later version) with WSFC hotfix KB 2494036 installed." -FunctionName Disable-DbaAgHadr
                    }
                }
                if ($__boundForce) { # SOURCE: if (Test-Bound 'Force') {
                    if ($PSCmdlet.ShouldProcess($instance, "Force provided, restarting Engine and Agent service for $instance on $computerFullName")) {
                        try {
                            $null = Stop-DbaService -ComputerName $computerFullName -InstanceName $instanceName -Type Agent, Engine
                            $null = Start-DbaService -ComputerName $computerFullName -InstanceName $instanceName -Type Agent, Engine
                        } catch {
                            Stop-Function -Message "Issue restarting $instance" -Target $instance -Continue -FunctionName Disable-DbaAgHadr
                        }
                    }
                }
                $newState = Get-WmiHadr -SqlInstance $instance -Credential $Credential

                if (-not $__boundForce) { # SOURCE: if (Test-Bound -Not -ParameterName Force) {
                    Write-Message -Level Warning -Message "You must restart the SQL Server for it to take effect." -FunctionName Disable-DbaAgHadr
                }

                [PSCustomObject]@{
                    ComputerName  = $newState.ComputerName
                    InstanceName  = $newState.InstanceName
                    SqlInstance   = $newState.SqlInstance
                    IsHadrEnabled = $false
                }
            }
        }
} $SqlInstance $Credential $Force $__boundForce $EnableException $__boundWhatIf $__boundConfirm $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
