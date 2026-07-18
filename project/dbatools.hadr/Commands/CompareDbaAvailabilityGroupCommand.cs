#nullable enable

using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Orchestrates the four Compare-DbaAgReplica* commands (agent jobs, logins, credentials,
/// operators) across the replicas of availability groups, forwarding type-specific switches
/// to each. Port of public/Compare-DbaAvailabilityGroup.ps1; surface pinned by
/// migration/baselines/Compare-DbaAvailabilityGroup.json.
/// </summary>
[Cmdlet(VerbsData.Compare, "DbaAvailabilityGroup")]
public sealed class CompareDbaAvailabilityGroupCommand : DbaBaseCmdlet
{
    /// <summary>The SQL Server instance or instances hosting the availability groups.</summary>
    [Parameter(ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instances using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Restricts the comparison to these availability groups.</summary>
    [Parameter(Position = 2)]
    public string[]? AvailabilityGroup { get; set; }

    /// <summary>Object types to compare; All expands to the full set.</summary>
    [Parameter(Position = 3)]
    [ValidateSet("AgentJob", "Login", "Credential", "Operator", "All")]
    public string[] Type { get; set; } = new[] { "All" };

    /// <summary>Excludes system jobs from the agent-job comparison.</summary>
    [Parameter]
    public SwitchParameter ExcludeSystemJob { get; set; }

    /// <summary>Excludes system logins from the login comparison.</summary>
    [Parameter]
    public SwitchParameter ExcludeSystemLogin { get; set; }

    /// <summary>Also reports modification-date drift for jobs and logins.</summary>
    [Parameter]
    public SwitchParameter IncludeModifiedDate { get; set; }

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
            foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
                new DbaInstanceParameter[] { instance }, SqlCredential, AvailabilityGroup,
                Type, ExcludeSystemJob.ToBool(), ExcludeSystemLogin.ToBool(),
                IncludeModifiedDate.ToBool(), EnableException.ToBool(),
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

    // PS: the source process block VERBATIM (the pre-loop Type expansion plus the
    // foreach; per-element hop, so the expansion re-runs per element - idempotent by
    // construction, matching the function world where the expanded value persists).
    // ZERO substitutions: the source has no Stop-Function, no gates, no Test-Bound -
    // the body is the source bytes unmodified.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $AvailabilityGroup, $Type, $ExcludeSystemJob, $ExcludeSystemLogin, $IncludeModifiedDate, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [string[]]$AvailabilityGroup, [string[]]$Type, $ExcludeSystemJob, $ExcludeSystemLogin, $IncludeModifiedDate, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        if ("All" -in $Type) {
            $Type = @("AgentJob", "Login", "Credential", "Operator")
        }

        foreach ($instance in $SqlInstance) {
            if ("AgentJob" -in $Type) {
                $splatAgentJob = @{
                    SqlInstance     = $instance
                    SqlCredential   = $SqlCredential
                    EnableException = $EnableException
                }

                if ($AvailabilityGroup) {
                    $splatAgentJob["AvailabilityGroup"] = $AvailabilityGroup
                }

                if ($ExcludeSystemJob) {
                    $splatAgentJob["ExcludeSystemJob"] = $true
                }

                if ($IncludeModifiedDate) {
                    $splatAgentJob["IncludeModifiedDate"] = $true
                }

                Compare-DbaAgReplicaAgentJob @splatAgentJob
            }

            if ("Login" -in $Type) {
                $splatLogin = @{
                    SqlInstance     = $instance
                    SqlCredential   = $SqlCredential
                    EnableException = $EnableException
                }

                if ($AvailabilityGroup) {
                    $splatLogin["AvailabilityGroup"] = $AvailabilityGroup
                }

                if ($ExcludeSystemLogin) {
                    $splatLogin["ExcludeSystemLogin"] = $true
                }

                if ($IncludeModifiedDate) {
                    $splatLogin["IncludeModifiedDate"] = $true
                }

                Compare-DbaAgReplicaLogin @splatLogin
            }

            if ("Credential" -in $Type) {
                $splatCredential = @{
                    SqlInstance     = $instance
                    SqlCredential   = $SqlCredential
                    EnableException = $EnableException
                }

                if ($AvailabilityGroup) {
                    $splatCredential["AvailabilityGroup"] = $AvailabilityGroup
                }

                Compare-DbaAgReplicaCredential @splatCredential
            }

            if ("Operator" -in $Type) {
                $splatOperator = @{
                    SqlInstance     = $instance
                    SqlCredential   = $SqlCredential
                    EnableException = $EnableException
                }

                if ($AvailabilityGroup) {
                    $splatOperator["AvailabilityGroup"] = $AvailabilityGroup
                }

                Compare-DbaAgReplicaOperator @splatOperator
            }
        }
} $SqlInstance $SqlCredential $AvailabilityGroup $Type $ExcludeSystemJob $ExcludeSystemLogin $IncludeModifiedDate $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
