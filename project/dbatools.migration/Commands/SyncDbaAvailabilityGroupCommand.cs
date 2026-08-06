#nullable enable

using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Copies the server-level objects an availability group does not replicate - logins, agent jobs,
/// linked servers, credentials and the rest - from the primary replica to every secondary. Port of
/// public/Sync-DbaAvailabilityGroup.ps1; the workflow stays a module-scoped PowerShell compatibility
/// hop because every step is a call to another dbatools command plus SMO refreshes on the JobServer.
/// Surface pinned by migration/baselines/Sync-DbaAvailabilityGroup.json.
///
/// PROCESS+END lifecycle split. InputObject is ValueFromPipeline, so process fires once per piped
/// availability group and collects primary/secondary pairs; the end block does the whole sync once,
/// after every record has been seen. Folding end into process would sync per record, which is the
/// exact duplication the source's $dupe check exists to prevent.
///
/// NO BEGIN HOP, and the two things begin does are reproduced where they are actually read.
/// "$allcombos = @()" is the accumulator seeded in C# below - an empty array is not worth a runspace
/// round trip, and a begin hop that produced nothing observable would be evidence-free. The other
/// statement, "if ($Force) { $ConfirmPreference = 'none' }", is a preference variable: begin, process
/// and end share ONE function scope in the source, so the assignment reaches the end block's
/// ShouldProcess call and the confirmation gates of the nested Copy-Dba* commands. The hop scopes are
/// separate, so it is repeated at the top of the end hop, which is the scope that reads it.
///
/// THREE VALUES CROSS RECORDS, and only one of them is an accumulator the source names as such.
///   $allcombos   - the primary/secondary pairs. Grown per record, consumed by end.
///   $Secondary   - a PARAMETER variable the process body reassigns: "$Secondary += (replica names)".
///                  It is not pipeline-bound, so the engine never rebinds it, and record two starts
///                  from record one's accumulated list. Its param-block type constraint survives the
///                  carry because the inner param() redeclares it, so the value coerces exactly as
///                  the function-world assignment would.
///   $secondaries - only assigned INSIDE "if ($Secondary)", so a record that resolves no secondary
///                  keeps the previous record's connected servers and files them into its own combo.
/// All three ride one process sentinel. One Seeded flag covers $Secondary and $secondaries together:
/// after any record has run, the carried value is the correct one for the next: only the
/// no-record-has-run case may fall back to the bound argument.
///
/// $InputObject IS NOT CARRIED, deliberately. The body does "$InputObject += Get-DbaAvailabilityGroup"
/// too, but the engine rebinds a pipeline-bound parameter on every record, which discards the
/// previous record's append. Carrying it would invent an accumulation the source does not have.
///
/// THE INTERRUPT LATCH IS SEEDED, NOT GUARDED. There is no Interrupted prologue on ProcessRecord:
/// the source's process block reads Test-FunctionInterrupt nowhere, and adding the guard is the #723
/// over-application that silences every record behind a mid-pipe failure. The end block DOES read it,
/// so the flag is carried out of each process hop (sticky, because the seed goes back in on the next
/// record) and seeded into the end hop, where the source's own "if (Test-FunctionInterrupt) { return }"
/// performs the stop.
///
/// TEST-BOUND BECOMES CARRIED FLAGS. The hop passes every argument positionally, so an uncarried
/// Test-Bound would report the whole parameter list bound and neither guard would ever fire; the
/// dot-sourced body compounds it with a fresh empty $PSBoundParameters. The three sites - the
/// "-Primary or an Input Object" guard and the two -Job/-ExcludeJob splat keys - read booleans
/// computed from the cmdlet's own MyInvocation.BoundParameters instead.
///
/// -WhatIf AND -Confirm ARE CARRIED INTO THE END HOP. Only the DatabaseOwner step consults
/// ShouldProcess directly (routed through $__realCmdlet); the other fourteen steps are bare calls to
/// Copy-Dba* commands that declare their own SupportsShouldProcess and, in the function world,
/// inherited $WhatIfPreference from the caller's scope. A hop scope does not inherit it, so without
/// this carry a -WhatIf run would perform a real sync - the same defect W6-026 measured on
/// Start-DbaMigration. Splatting the bound values into the hop's [CmdletBinding(SupportsShouldProcess)]
/// block reproduces the inheritance.
///
/// NAMED-WRAPPER SHIM ON THE END BODY. It calls Write-ProgressHelper fifteen times and that helper
/// reads (Get-PSCallStack)[1].Command; run bare the frame reads as a scriptblock marker. The body
/// therefore runs inside a function carrying the command's name, dot-invoked so its two early returns
/// leave the body rather than the hop and its locals stay visible to the scope the latch lives in.
///
/// Switches marshal as real booleans and the inner param leaves them untyped: PowerShell excludes
/// [switch] parameters from positional binding, so one typed flag would shift every argument after it.
/// The body reads them as booleans and splices them with -Switch:$Value, which a boolean serves.
/// </summary>
[Cmdlet(VerbsData.Sync, "DbaAvailabilityGroup", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Medium)]
public sealed class SyncDbaAvailabilityGroupCommand : DbaBaseCmdlet
{
    /// <summary>The primary replica the server-level objects are copied from.</summary>
    [Parameter(Position = 0)]
    public DbaInstanceParameter? Primary { get; set; }

    /// <summary>Login to the primary replica using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? PrimarySqlCredential { get; set; }

    /// <summary>The secondary replicas the objects are copied to. Discovered from the availability group when omitted.</summary>
    [Parameter(Position = 2)]
    public DbaInstanceParameter[]? Secondary { get; set; }

    /// <summary>Login to the secondary replicas using alternative credentials.</summary>
    [Parameter(Position = 3)]
    public PSCredential? SecondarySqlCredential { get; set; }

    /// <summary>Windows credential used to decrypt passwords over PowerShell remoting.</summary>
    [Parameter(Position = 4)]
    public PSCredential? Credential { get; set; }

    /// <summary>The availability group whose replicas are synchronized.</summary>
    [Parameter(Position = 5)]
    public string? AvailabilityGroup { get; set; }

    /// <summary>Object types to leave out of the synchronization.</summary>
    [Parameter(Position = 6)]
    [Alias("ExcludeType")]
    [ValidateSet("AgentCategory", "AgentOperator", "AgentAlert", "AgentProxy", "AgentSchedule", "AgentJob",
        "Credentials", "CustomErrors", "DatabaseMail", "DatabaseOwner", "LinkedServers", "Logins",
        "LoginPermissions", "SpConfigure", "SystemTriggers")]
    public string[]? Exclude { get; set; }

    /// <summary>Only synchronize these logins.</summary>
    [Parameter(Position = 7)]
    public string[]? Login { get; set; }

    /// <summary>Skip these logins.</summary>
    [Parameter(Position = 8)]
    public string[]? ExcludeLogin { get; set; }

    /// <summary>Only synchronize these SQL Agent jobs.</summary>
    [Parameter(Position = 9)]
    public string[]? Job { get; set; }

    /// <summary>Skip these SQL Agent jobs.</summary>
    [Parameter(Position = 10)]
    public string[]? ExcludeJob { get; set; }

    /// <summary>Disable the synchronized jobs on the secondary replicas.</summary>
    [Parameter]
    public SwitchParameter DisableJobOnDestination { get; set; }

    /// <summary>Availability groups from Get-DbaAvailabilityGroup.</summary>
    [Parameter(Position = 11, ValueFromPipeline = true)]
    public Microsoft.SqlServer.Management.Smo.AvailabilityGroup[]? InputObject { get; set; }

    /// <summary>Copy credentials, mail accounts and linked servers without their passwords.</summary>
    [Parameter]
    public SwitchParameter ExcludePassword { get; set; }

    /// <summary>Drop and recreate objects that already exist on the secondaries.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // $allcombos, the reassigned $Secondary, $secondaries and the interrupt latch. Seeded here rather
    // than by a begin hop; AllCombos starts as begin's "$allcombos = @()".
    private Hashtable _processState = new Hashtable
    {
        ["AllCombos"] = new object[0],
        ["Secondary"] = null,
        ["Secondaries"] = null,
        ["Seeded"] = false,
        ["Interrupted"] = false
    };

    // NO Interrupted PROLOGUE - see the type comment. The source's process block never reads the flag.
    protected override void ProcessRecord()
    {
        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            Primary, PrimarySqlCredential, Secondary, SecondarySqlCredential, AvailabilityGroup,
            InputObject, EnableException.ToBool(),
            MyInvocation.BoundParameters.ContainsKey("Primary"),
            MyInvocation.BoundParameters.ContainsKey("InputObject"),
            _processState,
            NestedCommand.BoundCommonParameter(this, "Verbose"), NestedCommand.BoundCommonParameter(this, "Debug")))
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                NestedCommand.RemoveDuplicateError(this, nestedError);
                WriteError(NestedCommand.PreserveErrorIdentity(nestedError));
                continue;
            }
            WriteObject(item);
        }
    }

    protected override void EndProcessing()
    {
        // Streaming, not buffered: a sync walks fifteen object types across every secondary and emits
        // a result object per copied object, so a buffered hop would withhold the whole run until the
        // last agent job finished.
        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                NestedCommand.RemoveDuplicateError(this, nestedError);
                WriteError(NestedCommand.PreserveErrorIdentity(nestedError));
                return;
            }
            WriteObject(item);
        }, EndScript,
            AvailabilityGroup, Exclude, Login, ExcludeLogin, Job, ExcludeJob,
            DisableJobOnDestination.ToBool(), Credential, ExcludePassword.ToBool(), Force.ToBool(),
            EnableException.ToBool(),
            MyInvocation.BoundParameters.ContainsKey("Job"),
            MyInvocation.BoundParameters.ContainsKey("ExcludeJob"),
            _processState, this,
            NestedCommand.BoundCommonParameter(this, "WhatIf"), NestedCommand.BoundCommonParameter(this, "Confirm"),
            NestedCommand.BoundCommonParameter(this, "Verbose"), NestedCommand.BoundCommonParameter(this, "Debug"));
    }

    // PS: the process block VERBATIM inside a dot-sourced block, so its three early returns leave the
    // block and the write-back below still runs - which is what makes the carry match the function's
    // single scope. Edits: the two Test-Bound reads become the carried flags, and every Stop-Function
    // carries -FunctionName Sync-DbaAvailabilityGroup so the message names the command, not the hop.
    private const string ProcessScript = """
param($Primary, $PrimarySqlCredential, $Secondary, $SecondarySqlCredential, $AvailabilityGroup, $InputObject, $EnableException, $__boundPrimary, $__boundInputObject, $__processState, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter]$Primary, [PSCredential]$PrimarySqlCredential, [Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$Secondary, [PSCredential]$SecondarySqlCredential, [string]$AvailabilityGroup, [Microsoft.SqlServer.Management.Smo.AvailabilityGroup[]]$InputObject, $EnableException, $__boundPrimary, $__boundInputObject, $__processState, $__boundVerbose, $__boundDebug)

    $allcombos = $__processState.AllCombos
    if ($__processState.Seeded) {
        $Secondary = $__processState.Secondary
        $secondaries = $__processState.Secondaries
    }

    # -WhatIf:$false for the same reason Stop-Function carries it on this variable: the latch is
    # internal plumbing, so a -WhatIf run must neither announce it nor skip writing it.
    Set-Variable -Name __dbatools_interrupt_function_78Q9VPrM6999g6zo24Qn83m09XF56InEn4hFrA8Fwhu5xJrs6r -Value ([bool]$__processState.Interrupted) -Scope 0 -WhatIf:$false -Confirm:$false

    . {
        if (-not $__boundPrimary -and -not $__boundInputObject) {
            Stop-Function -Message "You must supply either -Primary or an Input Object" -FunctionName Sync-DbaAvailabilityGroup
            return
        }

        if (-not $AvailabilityGroup -and -not $Secondary -and -not $InputObject) {
            Stop-Function -Message "You must specify a secondary or an availability group." -FunctionName Sync-DbaAvailabilityGroup
            return
        }

        if ($InputObject) {
            $server = $InputObject.Parent
        } else {
            try {
                $server = Connect-DbaInstance -SqlInstance $Primary -SqlCredential $PrimarySqlCredential
            } catch {
                Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $Primary -FunctionName Sync-DbaAvailabilityGroup
                return
            }
        }

        if ($AvailabilityGroup) {
            $InputObject += Get-DbaAvailabilityGroup -SqlInstance $server -AvailabilityGroup $AvailabilityGroup
        }

        if ($InputObject) {
            $Secondary += (($InputObject.AvailabilityReplicas | Where-Object Name -ne $server.DomainInstanceName).Name | Select-Object -Unique)
        }

        if ($Secondary) {
            $Secondary = $Secondary | Sort-Object
            $secondaries = @()
            foreach ($computer in $Secondary) {
                try {
                    $secondaries += Connect-DbaInstance -SqlInstance $computer -SqlCredential $SecondarySqlCredential
                } catch {
                    Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $computer -Continue -FunctionName Sync-DbaAvailabilityGroup
                }
            }
        }

        $thiscombo = [PSCustomObject]@{
            PrimaryServer   = $server
            SecondaryServer = $secondaries
        }

        # In the event that someone pipes in an availability group, this will keep the sync from running a bunch of times
        $dupe = $false

        foreach ($ag in $allcombos) {
            if ($ag.PrimaryServer.Name -eq $thiscombo.PrimaryServer.Name -and
                $ag.SecondaryServer.Name.ToString() -eq $thiscombo.SecondaryServer.Name.ToString()) {
                $dupe = $true
            }
        }

        if ($dupe -eq $false) {
            $allcombos += $thiscombo
        }
    }

    $__iv = Get-Variable -Name __dbatools_interrupt_function_78Q9VPrM6999g6zo24Qn83m09XF56InEn4hFrA8Fwhu5xJrs6r -Scope 0 -ErrorAction Ignore
    $__processState.Interrupted = [bool]($__iv -and $__iv.Value)
    $__processState.AllCombos = $allcombos
    $__processState.Secondary = $Secondary
    $__processState.Secondaries = $secondaries
    $__processState.Seeded = $true
} $Primary $PrimarySqlCredential $Secondary $SecondarySqlCredential $AvailabilityGroup $InputObject $EnableException $__boundPrimary $__boundInputObject $__processState $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";

    // PS: the end block VERBATIM inside the named-wrapper shim, with process's $allcombos and latch
    // restored ahead of it. Edits: the two Test-Bound reads become the carried flags, $PSCmdlet
    // becomes $__realCmdlet so the gate reaches the compiled cmdlet's own ShouldProcess, and
    // Stop-Function carries -FunctionName Sync-DbaAvailabilityGroup.
    private const string EndScript = """
param($AvailabilityGroup, $Exclude, $Login, $ExcludeLogin, $Job, $ExcludeJob, $DisableJobOnDestination, $Credential, $ExcludePassword, $Force, $EnableException, $__boundJob, $__boundExcludeJob, $__processState, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = "Medium")]
    param([string]$AvailabilityGroup, [string[]]$Exclude, [string[]]$Login, [string[]]$ExcludeLogin, [string[]]$Job, [string[]]$ExcludeJob, $DisableJobOnDestination, [PSCredential]$Credential, $ExcludePassword, $Force, $EnableException, $__boundJob, $__boundExcludeJob, $__processState, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)

    $allcombos = $__processState.AllCombos

    # The source's begin block set this once for the whole invocation; each hop scope is separate,
    # so it has to be re-set in the one the ShouldProcess call below actually reads.
    if ($Force) { $ConfirmPreference = 'none' }

    # -WhatIf:$false for the same reason Stop-Function carries it on this variable: the latch is
    # internal plumbing, so a -WhatIf run must neither announce it nor skip writing it.
    Set-Variable -Name __dbatools_interrupt_function_78Q9VPrM6999g6zo24Qn83m09XF56InEn4hFrA8Fwhu5xJrs6r -Value ([bool]$__processState.Interrupted) -Scope 0 -WhatIf:$false -Confirm:$false

    function Sync-DbaAvailabilityGroup {
        if (Test-FunctionInterrupt) { return }

        # now that all combinations have been figured out, begin sync without duplicating work
        foreach ($ag in $allcombos) {
            $server = $ag.PrimaryServer
            $secondaries = $ag.SecondaryServer

            $stepCounter = 0
            $activity = "Syncing availability group $AvailabilityGroup"

            if (-not $secondaries) {
                Stop-Function -Message "No secondaries found." -FunctionName Sync-DbaAvailabilityGroup
                return
            }

            $primaryserver = $server.Name
            $secondaryservers = $secondaries.Name -join ", "

            if ($Exclude -notcontains "SpConfigure") {
                Write-ProgressHelper -Activity $activity -StepNumber ($stepCounter++) -Message "Syncing SQL Server Configuration"
                Copy-DbaSpConfigure -Source $server -Destination $secondaries
            }

            if ($Exclude -notcontains "Logins") {
                Write-ProgressHelper -Activity $activity -StepNumber ($stepCounter++) -Message "Syncing logins"
                Copy-DbaLogin -Source $server -Destination $secondaries -Login $Login -ExcludeLogin $ExcludeLogin -Force:$Force
            }

            if ($Exclude -notcontains "DatabaseOwner") {
                if ($__realCmdlet.ShouldProcess("Updating database owners to match newly migrated logins from $primaryserver to $secondaryservers")) {
                    Write-ProgressHelper -Activity $activity -StepNumber ($stepCounter++) -Message "Updating database owners to match newly migrated logins"
                    foreach ($sec in $secondaries) {
                        $null = Update-SqlDbOwner -Source $server -Destination $sec
                    }
                }
            }

            if ($Exclude -notcontains "CustomErrors") {
                Write-ProgressHelper -Activity $activity -StepNumber ($stepCounter++) -Message "Syncing custom errors (user defined messages)"
                Copy-DbaCustomError -Source $server -Destination $secondaries -Force:$Force
            }

            if ($Exclude -notcontains "Credentials") {
                Write-ProgressHelper -Activity $activity -StepNumber ($stepCounter++) -Message "Syncing SQL credentials"
                Copy-DbaCredential -Source $server -Destination $secondaries -Credential $Credential -ExcludePassword:$ExcludePassword -Force:$Force
            }

            if ($Exclude -notcontains "DatabaseMail") {
                Write-ProgressHelper -Activity $activity -StepNumber ($stepCounter++) -Message "Syncing database mail"
                Copy-DbaDbMail -Source $server -Destination $secondaries -Credential $Credential -ExcludePassword:$ExcludePassword -Force:$Force
            }

            if ($Exclude -notcontains "LinkedServers") {
                Write-ProgressHelper -Activity $activity -StepNumber ($stepCounter++) -Message "Syncing linked servers"
                Copy-DbaLinkedServer -Source $server -Destination $secondaries -Credential $Credential -ExcludePassword:$ExcludePassword -Force:$Force
            }

            if ($Exclude -notcontains "SystemTriggers") {
                Write-ProgressHelper -Activity $activity -StepNumber ($stepCounter++) -Message "Syncing System Triggers"
                Copy-DbaInstanceTrigger -Source $server -Destination $secondaries -Force:$Force
            }

            if ($Exclude -notcontains "AgentCategory") {
                Write-ProgressHelper -Activity $activity -StepNumber ($stepCounter++) -Message "Syncing Agent Categories"
                Copy-DbaAgentJobCategory -Source $server -Destination $secondaries -Force:$force
                foreach ($sec in $secondaries) {
                    if ($sec.JobServer) {
                        $sec.JobServer.JobCategories.Refresh()
                        $sec.JobServer.OperatorCategories.Refresh()
                        $sec.JobServer.AlertCategories.Refresh()
                    }
                }
            }

            if ($Exclude -notcontains "AgentOperator") {
                Write-ProgressHelper -Activity $activity -StepNumber ($stepCounter++) -Message "Syncing Agent Operators"
                Copy-DbaAgentOperator -Source $server -Destination $secondaries -Force:$force
                foreach ($sec in $secondaries) {
                    if ($sec.JobServer) {
                        $sec.JobServer.Operators.Refresh()
                    }
                }
            }

            if ($Exclude -notcontains "AgentAlert") {
                Write-ProgressHelper -Activity $activity -StepNumber ($stepCounter++) -Message "Syncing Agent Alerts"
                Copy-DbaAgentAlert -Source $server -Destination $secondaries -Force:$force -IncludeDefaults
            }

            if ($Exclude -notcontains "AgentProxy") {
                Write-ProgressHelper -Activity $activity -StepNumber ($stepCounter++) -Message "Syncing Agent Proxy Accounts"
                Copy-DbaAgentProxy -Source $server -Destination $secondaries -Force:$force
                foreach ($sec in $secondaries) {
                    if ($sec.JobServer) {
                        $sec.JobServer.ProxyAccounts.Refresh()
                    }
                }
            }

            if ($Exclude -notcontains "AgentSchedule") {
                Write-ProgressHelper -Activity $activity -StepNumber ($stepCounter++) -Message "Syncing Agent Schedules"
                Copy-DbaAgentSchedule -Source $server -Destination $secondaries -Force:$force
                foreach ($sec in $secondaries) {
                    if ($sec.JobServer) {
                        $sec.JobServer.SharedSchedules.Refresh()
                        $sec.JobServer.Refresh()
                    }
                    $sec.Refresh()
                }
            }

            if ($Exclude -notcontains "AgentJob") {
                Write-ProgressHelper -Activity $activity -StepNumber ($stepCounter++) -Message "Syncing Agent Jobs"
                $splatGetJob = @{
                    SqlInstance = $server
                    Type        = "Local"
                }
                if ($__boundJob) {
                    $splatGetJob['Job'] = $Job
                }
                if ($__boundExcludeJob) {
                    $splatGetJob['ExcludeJob'] = $ExcludeJob
                }
                $jobsToSync = Get-DbaAgentJob @splatGetJob

                $splatCopyJob = @{
                    Destination          = $secondaries
                    Force                = $force
                    DisableOnDestination = $DisableJobOnDestination
                    InputObject          = $jobsToSync
                }
                Copy-DbaAgentJob @splatCopyJob
            }

            if ($Exclude -notcontains "LoginPermissions") {
                Write-ProgressHelper -Activity $activity -StepNumber ($stepCounter++) -Message "Syncing login permissions"
                Sync-DbaLoginPermission -Source $server -Destination $secondaries -Login $Login -ExcludeLogin $ExcludeLogin
            }
        }
    }
    . Sync-DbaAvailabilityGroup
} $AvailabilityGroup $Exclude $Login $ExcludeLogin $Job $ExcludeJob $DisableJobOnDestination $Credential $ExcludePassword $Force $EnableException $__boundJob $__boundExcludeJob $__processState $__realCmdlet $__boundWhatIf $__boundConfirm $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
