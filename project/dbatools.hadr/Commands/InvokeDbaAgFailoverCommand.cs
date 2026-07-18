#nullable enable

using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Fails over availability groups, gracefully or forcefully with potential data loss.
/// Port of public/Invoke-DbaAgFailover.ps1; surface pinned by
/// migration/baselines/Invoke-DbaAgFailover.json.
/// </summary>
[Cmdlet(VerbsLifecycle.Invoke, "DbaAgFailover", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.High)]
public sealed class InvokeDbaAgFailoverCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instances using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The availability groups to fail over.</summary>
    [Parameter(Position = 2)]
    public string[]? AvailabilityGroup { get; set; }

    /// <summary>Availability group objects piped from Get-DbaAvailabilityGroup.</summary>
    [Parameter(ValueFromPipeline = true, Position = 3)]
    public Microsoft.SqlServer.Management.Smo.AvailabilityGroup[]? InputObject { get; set; }

    /// <summary>Forces the failover, allowing potential data loss and suppressing prompts.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        // WHOLE-RECORD hop per the W3-005/W4-011 convention: the begin block's
        // Force -> ConfirmPreference suppression rides at the hop top and both
        // High-impact ShouldProcess gates run on the INNER scriptblock's own
        // $Pscmdlet, so [A]/[L] prompt state persists across a record's groups.
        // The two loop-less validation Stop-Function+return sites exit the record
        // via the dot-block frame; the catch site is -Continue (loop-local).
        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, SqlCredential, AvailabilityGroup, InputObject,
            Force.ToBool(), EnableException.ToBool(),
            TestBound(nameof(SqlInstance)), TestBound(nameof(InputObject)),
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

    // PS: the begin block's Force -> ConfirmPreference suppression rides verbatim at
    // the hop top, then the source process block VERBATIM, CRLF-preserved and
    // cmp-proven byte-exact after stripping three -FunctionName appends and the one
    // multi-name Test-Bound rewrite (SOURCE comment). ShouldProcess gates use the
    // inner block's own $Pscmdlet; the dot-block preserves the validation returns.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $AvailabilityGroup, $InputObject, $Force, $EnableException, $__boundSqlInstance, $__boundInputObject, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [string[]]$AvailabilityGroup, [Microsoft.SqlServer.Management.Smo.AvailabilityGroup[]]$InputObject, $Force, $EnableException, $__boundSqlInstance, $__boundInputObject, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    if ($Force) { $ConfirmPreference = 'none' }

    . {
        if (-not ($__boundSqlInstance -or $__boundInputObject)) { # SOURCE: if (Test-Bound -Not SqlInstance, InputObject) {
            Stop-Function -Message "You must supply either -SqlInstance or an Input Object" -FunctionName Invoke-DbaAgFailover
            return
        }

        if ($SqlInstance -and -not $AvailabilityGroup) {
            Stop-Function -Message "You must specify at least one availability group when using SqlInstance." -FunctionName Invoke-DbaAgFailover
            return
        }

        if ($SqlInstance) {
            $InputObject += Get-DbaAvailabilityGroup -SqlInstance $SqlInstance -SqlCredential $SqlCredential -AvailabilityGroup $AvailabilityGroup
        }

        foreach ($ag in $InputObject) {
            try {
                $server = $ag.Parent
                if ($Force) {
                    if ($Pscmdlet.ShouldProcess($server.Name, "Forcefully failing over $($ag.Name), allowing potential data loss")) {
                        $ag.FailoverWithPotentialDataLoss()
                        $ag.Refresh()
                        $ag
                    }
                } else {
                    if ($Pscmdlet.ShouldProcess($server.Name, "Gracefully failing over $($ag.Name)")) {
                        $ag.Failover()
                        $ag.Refresh()
                        $ag
                    }
                }
            } catch {
                Stop-Function -Continue -Message "Failure" -ErrorRecord $_ -FunctionName Invoke-DbaAgFailover
            }
        }
    }
} $SqlInstance $SqlCredential $AvailabilityGroup $InputObject $Force $EnableException $__boundSqlInstance $__boundInputObject $__boundWhatIf $__boundConfirm $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
