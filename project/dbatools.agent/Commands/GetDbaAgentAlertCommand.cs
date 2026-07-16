#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves and decorates SQL Agent alerts. Port of public/Get-DbaAgentAlert.ps1 (W2-010).
/// The workflow remains a module-scoped PowerShell compatibility hop so wildcard expansion,
/// duplicate results, SMO notification tables, DbaDateTime coercion, and stream/error behavior
/// retain the retired function's engine semantics. Surface pinned by
/// migration/baselines/Get-DbaAgentAlert.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaAgentAlert")]
public sealed class GetDbaAgentAlertCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Alert-name filters, including wildcard patterns.</summary>
    [Parameter(Position = 2)]
    public string[]? Alert { get; set; }

    /// <summary>Alert-name exclusion patterns.</summary>
    [Parameter(Position = 3)]
    public string[]? ExcludeAlert { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        // Stream one hop PER INSTANCE: a whole-array hop batches every instance's Debug
        // records ahead of all alert output, where the source's foreach interleaves
        // Debug-then-alerts per instance (opus W2-010 P2A; same class and fix as the
        // bdced66 DbMail retrofit). The hop body has no cross-instance state.
        foreach (DbaInstanceParameter instance in SqlInstance)
        {
            if (Interrupted)
                return;

            foreach (PSObject? item in NestedCommand.InvokeScoped(this, BodyScript,
                new[] { instance }, SqlCredential, Alert, ExcludeAlert, EnableException.ToBool(),
                TestBound(nameof(Alert)), TestBound(nameof(ExcludeAlert)),
                BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
            {
                if (item?.BaseObject is ErrorRecord nestedError)
                {
                    RemoveHopErrorBookkeeping(nestedError);
                    WriteError(nestedError);
                }
                else
                {
                    WriteObject(item);
                }
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
param($SqlInstance, $SqlCredential, $Alert, $ExcludeAlert, $EnableException, $__boundAlert, $__boundExcludeAlert, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, $SqlCredential, [string[]]$Alert, [string[]]$ExcludeAlert, $EnableException, $__boundAlert, $__boundExcludeAlert, $__boundVerbose, $__boundDebug)

    foreach ($instance in $SqlInstance) {
        try {
            $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaAgentAlert
        }

        Write-Message -Level Debug -Message "Getting Edition from $server" -FunctionName Get-DbaAgentAlert
        Write-Message -Level Debug -Message "$server is a $($server.Edition)" -FunctionName Get-DbaAgentAlert

        if ($server.Edition -like 'Express*') {
            Stop-Function -Message "There is no SQL Agent on $server, it's a $($server.Edition)" -Continue -FunctionName Get-DbaAgentAlert
        }

        $defaults = "ComputerName", "SqlInstance", "InstanceName", "Name", "ID", "JobName", "AlertType", "CategoryName", "Severity", "MessageId", "IsEnabled", "DelayBetweenResponses", "LastRaised", "OccurrenceCount"

        $alerts = $server.Jobserver.Alerts

        if ($__boundAlert) {
            $tempAlerts = @()

            foreach ($a in $Alert) {
                $tempAlerts += $alerts | Where-Object Name -like $a
            }

            $alerts = $tempAlerts
        }

        if ($__boundExcludeAlert) {
            foreach ($e in $ExcludeAlert) {
                $alerts = $alerts | Where-Object Name -notlike $e
            }
        }

        foreach ($alrt in $alerts) {
            $lastraised = [dbadatetime]$alrt.LastOccurrenceDate

            Add-Member -Force -InputObject $alrt -MemberType NoteProperty -Name ComputerName -value $server.ComputerName
            Add-Member -Force -InputObject $alrt -MemberType NoteProperty -Name InstanceName -value $server.ServiceName
            Add-Member -Force -InputObject $alrt -MemberType NoteProperty -Name SqlInstance -value $server.DomainInstanceName
            Add-Member -Force -InputObject $alrt -MemberType NoteProperty Notifications -value $alrt.EnumNotifications()
            Add-Member -Force -InputObject $alrt -MemberType NoteProperty LastRaised -value $lastraised

            Select-DefaultView -InputObject $alrt -Property $defaults
        }
    }
} $SqlInstance $SqlCredential $Alert $ExcludeAlert $EnableException $__boundAlert $__boundExcludeAlert $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
