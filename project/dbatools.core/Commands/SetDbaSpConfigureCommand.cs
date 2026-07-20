#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Sets sp_configure server configuration values. Port of public/Set-DbaSpConfigure.ps1
/// (W3-096). WHOLE-RECORD verbatim hop. VFP-LOCAL CLASSIFICATION TABLE (with the
/// promoted where-does-the-port-keep-this-state question answered per entry):
/// $InputObject (+= mutated param) - source keeps it in the fn scope with per-record
/// pipeline restore; the port keeps it in the HOP SCOPE PER RECORD, which is
/// range-equivalent because InputObject is the SOLE VFP (piped records rebind+restore;
/// SqlInstance is not VFP, so no record sequence can observe the mutation across
/// records - the W3-094 shape); every loop local ($server/$currentRunValue/
/// $currentConfigValue/$minValue/$maxValue/$isDynamic/$configuration) - source keeps
/// them in the fn scope but re-assigns unconditionally at iteration top, port keeps
/// them hop-scope per record: no reachable read of stale state either side; NO
/// sentinel. Engine state: gates on the REAL cmdlet whose runtime spans the pipeline
/// (exactly like the source's single $Pscmdlet) - no transplant. The single Test-Bound
/// call rides as a carried flag
/// (scope-walk law, W3-093). Gates route to the REAL cmdlet ($Pscmdlet ->
/// $__realCmdlet; no Force convention - no transplant, no hold exposure); the
/// ShouldProcess TARGET is the raw $SqlInstance ARRAY (stringifies; empty on the piped
/// path - the leaked-null-target cousin of W3-089). QUIRKS preserved verbatim:
/// -Target $Instance on Write-Message/Stop-Function reads an UNDECLARED variable
/// (W3-079 class - null target); the catch's Stop-Function -Continue -ContinueLabel
/// main names a label that EXISTS NOWHERE - in the source an unmatched `continue main`
/// unwinds past the loop and ends the process block (silently swallowing remaining
/// records); the lowercase $value/$Pscmdlet spellings; the OUTPUT emission sits INSIDE
/// the gate (-WhatIf emits no rows) while the same-value/out-of-range Stop-Function
/// -Continue warnings fire BEFORE it. [PsIntCast] on Value (W1-043; unbound reads 0,
/// which the range checks then compare). NO WarningAction carrier (codex W3-005 r3).
/// Surface pinned by migration/baselines/Set-DbaSpConfigure.json (implicit positions
/// 0-4, Value Alias NewValue/NewConfig, Name Alias Config/ConfigName, InputObject
/// object[] pos4 sole VFP, ConfirmImpact default Medium).
/// </summary>
[Cmdlet(VerbsCommon.Set, "DbaSpConfigure", SupportsShouldProcess = true)]
public sealed class SetDbaSpConfigureCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The new configuration value.</summary>
    [Parameter(Position = 2)]
    [Alias("NewValue", "NewConfig")]
    [PsIntCast]
    public int Value { get; set; }

    /// <summary>The configuration name(s) to change.</summary>
    [Parameter(Position = 3)]
    [Alias("Config", "ConfigName")]
    public string[]? Name { get; set; }

    /// <summary>Get-DbaSpConfigure output to act on.</summary>
    [Parameter(ValueFromPipeline = true, Position = 4)]
    public object[]? InputObject { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

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
            SqlInstance, SqlCredential, Value, Name, InputObject, EnableException.ToBool(),
            TestBound(nameof(SqlInstance)), this,
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

    // PS: the ENTIRE process body VERBATIM per record. Substitutions only: $Pscmdlet ->
    // $__realCmdlet on the gate, Test-Bound -ParameterName SqlInstance -> the carried
    // flag, and explicit -FunctionName Set-DbaSpConfigure on Stop-Function/Write-Message
    // (W1-090). The undeclared $Instance targets, the unmatched -ContinueLabel main and
    // the source's escaped-quote warning text ride as-is.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Value, $Name, $InputObject, $EnableException, $__boundSqlInstance, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess)]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [System.Management.Automation.PSCredential]$SqlCredential, [int]$Value, [string[]]$Name, [object[]]$InputObject, $EnableException, $__boundSqlInstance, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    # (Test-Bound -ParameterName SqlInstance)
    if ($__boundSqlInstance) {
        $InputObject += Get-DbaSpConfigure -SqlInstance $SqlInstance -SqlCredential $SqlCredential -Name $Name
    }

    foreach ($configobject in $InputObject) {
        $server = $configobject.Parent
        $currentRunValue = $configobject.RunningValue
        $currentConfigValue = $configobject.ConfiguredValue
        $minValue = $configobject.MinValue
        $maxValue = $configobject.MaxValue
        $isDynamic = $configobject.IsDynamic
        $configuration = $configobject.Name

        #Let us not waste energy setting the value to itself
        if ($currentConfigValue -eq $value) {
            Stop-Function -Message "Value to set is the same as the existing value. No work being performed." -Continue -Target $server -Category InvalidData -FunctionName Set-DbaSpConfigure
        }

        #Going outside the min/max boundary can be done, but it can break SQL, so I don't think allowing that is wise at this juncture
        if ($value -lt $minValue -or $value -gt $maxValue) {
            Stop-Function -Message "Value out of range for $configuration ($minValue <-> $maxValue)" -Continue -Category InvalidArgument -FunctionName Set-DbaSpConfigure
        }

        If ($__realCmdlet.ShouldProcess($SqlInstance, "Adjusting server configuration $configuration from $currentConfigValue to $value.")) {
            try {
                $configobject.Property.ConfigValue = $value
                $server.Configuration.Alter()

                [PSCustomObject]@{
                    ComputerName  = $server.ComputerName
                    InstanceName  = $server.ServiceName
                    SqlInstance   = $server.DomainInstanceName
                    ConfigName    = $configuration
                    PreviousValue = $currentConfigValue
                    NewValue      = $value
                }

                #If it's a dynamic setting we're all clear, otherwise let the user know that SQL needs to be restarted for the change to take
                if ($isDynamic -eq $false) {
                    Write-Message -Level Warning -Message "Configuration setting $configuration has been set, but restart of SQL Server is required for the new value `"$value`" to be used (old value: `"$currentRunValue`")" -Target $Instance -FunctionName Set-DbaSpConfigure -ModuleName "dbatools"
                }
            } catch {
                Stop-Function -Message "Unable to change config setting" -Target $Instance -ErrorRecord $_ -Continue -ContinueLabel main -FunctionName Set-DbaSpConfigure
            }
        }
    }
} $SqlInstance $SqlCredential $Value $Name $InputObject $EnableException $__boundSqlInstance $__realCmdlet $__boundWhatIf $__boundConfirm $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
