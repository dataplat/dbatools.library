#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Gets registered-server groups from Central Management Servers. Port of
/// public/Get-DbaRegServerGroup.ps1 (W3-048, WAVE-3 remnant); the workflow remains a module-scoped
/// PowerShell compatibility hop. READ-ONLY (no mutating verbs, no SupportsShouldProcess).
///
/// BEGIN+PROCESS. -SqlInstance is ValueFromPipeline, so process fires per piped instance. Emission is
/// process-only (no end block): the body iterates the accumulated stores and emits each group.
///
/// TWO begin-INITIALIZED CROSS-RECORD ACCUMULATORS - $serverstores AND $groups. Found by running the
/// carry detectors BEFORE coding (Find-AccumulatorCarry flagged $serverstores). begin :102 does
/// "$serverstores = $groups = @()", initializing BOTH, and process appends to them (:107/:114 for
/// $serverstores, :141/:148 for $groups) with NO per-record reset. So each rides begin -> process AND
/// record -> record, exactly the New-DbaDbMaskingConfig $databases class:
///   $serverstores accumulates every piped instance's store, and the "foreach ($serverstore in
///     $serverstores)" loop therefore RE-ITERATES earlier records' stores on later records,
///     re-emitting their groups. Bug-for-bug source behaviour.
///   $groups is rebuilt inside the serverstore loop - "+=" in the -Group path (:141/:148), wholesale
///     "=" in the else / DatabaseEngineServerGroup / -ExcludeGroup / -Id paths (:158/:162/:168/:174/
///     :176) - and is never reset between serverstores, so it too persists.
/// Both ride a state sentinel restored from the process carry (or begin's @() seed on the first
/// record). The sentinel uses PLAIN ASSIGNMENT, never a $() subexpression - a subexpression
/// enumerates a collection away (the W2-152 defect), and these are arrays.
///
/// NO INTERRUPT BRIDGE (no Test-FunctionInterrupt; the :109 Stop-Function carries -Continue). NO
/// Test-Bound, no $PSBoundParameters reads, no .IsPresent - the body tests -Group/-ExcludeGroup/-Id/
/// -SqlInstance directly by value, so there are no boundness carriers. The inherited EnableException
/// crosses as a SwitchParameter OBJECT received untyped.
///
/// In-hop Stop-Function/Write-Message calls carry -FunctionName. Implicit positions 0-4 are made
/// explicit per the W2-071 law and confirmed against the exported baseline; SqlInstance is position 0
/// and ValueFromPipeline. Streaming (DEF-001): each group is emitted via Select-DefaultView as it is
/// found. Surface pinned by migration/baselines/Get-DbaRegServerGroup.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaRegServerGroup")]
public sealed class GetDbaRegServerGroupCommand : DbaBaseCmdlet
{
    /// <summary>The CMS instance(s).</summary>
    [Parameter(ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Limit to these groups.</summary>
    [Parameter(Position = 2)]
    public object[]? Group { get; set; }

    /// <summary>Exclude these groups.</summary>
    [Parameter(Position = 3)]
    public object[]? ExcludeGroup { get; set; }

    /// <summary>Limit to these group ids (1 = default).</summary>
    [Parameter(Position = 4)]
    public int[]? Id { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // begin's empty @() seeds for $serverstores/$groups; opaque to C#.
    private Hashtable? _beginState;
    // the $serverstores/$groups accumulators carried record-to-record; opaque to C#.
    private Hashtable? _state;

    protected override void BeginProcessing()
    {
        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BeginScript,
            EnableException, NestedCommand.BoundCommonParameter(this, "Verbose"), NestedCommand.BoundCommonParameter(this, "Debug")))
        {
            if (item?.BaseObject is Hashtable sentinel && sentinel.ContainsKey("__getDbaRegServerGroupBegin"))
            {
                _beginState = sentinel["__getDbaRegServerGroupBegin"] as Hashtable;
                continue;
            }
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                NestedCommand.RemoveDuplicateError(this, nestedError);
                WriteError(nestedError);
                continue;
            }
            WriteObject(item);
        }
    }

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        // Streaming, not buffered (DEF-001): each group is emitted as it is found, so a buffered hop
        // would discard results already produced when a later instance's failure terminated the hop.
        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is Hashtable sentinel && sentinel.ContainsKey("__getDbaRegServerGroupProcess"))
            {
                _state = sentinel["__getDbaRegServerGroupProcess"] as Hashtable;
                return;
            }
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                NestedCommand.RemoveDuplicateError(this, nestedError);
                WriteError(nestedError);
                return;
            }
            WriteObject(item);
        }, ProcessScript,
            SqlInstance, SqlCredential, Group, ExcludeGroup, Id, EnableException,
            _beginState, _state,
            NestedCommand.BoundCommonParameter(this, "Verbose"), NestedCommand.BoundCommonParameter(this, "Debug"));
    }

    // PS: the begin block VERBATIM ("$serverstores = $groups = @()"), dot-sourced. The sentinel
    // carries both empty seeds via PLAIN assignment (a $() subexpression would enumerate the arrays).
    private const string BeginScript = """
param($EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param($EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    . {
        $serverstores = $groups = @()
    }

    $__ss = Get-Variable -Name serverstores -Scope 0 -ErrorAction Ignore
    $__gr = Get-Variable -Name groups -Scope 0 -ErrorAction Ignore
    $__ssv = $null; if ($__ss) { $__ssv = $__ss.Value }
    $__grv = $null; if ($__gr) { $__grv = $__gr.Value }
    @{ __getDbaRegServerGroupBegin = @{ Serverstores = $__ssv; Groups = $__grv } }
} $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
    // PS: the process block VERBATIM, dot-sourced. Only edit is -FunctionName on message calls. Both
    // accumulators restore from the process carry (or begin's @() seed on the first record) BEFORE
    // the body, so they persist begin->process AND record->record as the source's shared scope does.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Group, $ExcludeGroup, $Id, $EnableException, $__beginState, $__state, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [object[]]$Group, [object[]]$ExcludeGroup, [int[]]$Id, $EnableException, $__beginState, $__state, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    # the shared accumulators - the process carry across records, else begin's @() seed on record 1
    if ($null -ne $__state) { $serverstores = $__state.Serverstores; $groups = $__state.Groups }
    else { $serverstores = $__beginState.Serverstores; $groups = $__beginState.Groups }

    . {
        foreach ($instance in $SqlInstance) {
            try {
                $serverstores += Get-DbaRegServerStore -SqlInstance $instance -SqlCredential $SqlCredential -EnableException
            } catch {
                Stop-Function -Message "Cannot access Central Management Server '$instance'" -ErrorRecord $_ -Continue -FunctionName Get-DbaRegServerGroup
            }
        }

        if (-not $SqlInstance) {
            $serverstores += Get-DbaRegServerStore
        }

        foreach ($serverstore in $serverstores) {
            if ($Group) {
                foreach ($currentgroup in $Group) {
                    Write-Message -Level Verbose -Message "Processing $currentgroup" -FunctionName Get-DbaRegServerGroup -ModuleName "dbatools"
                    if ($currentgroup -is [Microsoft.SqlServer.Management.RegisteredServers.ServerGroup]) {
                        $currentgroup = Get-RegServerGroupReverseParse -object $currentgroup
                    }

                    if ($currentgroup -match 'DatabaseEngineServerGroup\\') {
                        $currentgroup = $currentgroup.Replace('DatabaseEngineServerGroup\', '')
                    }

                    if ($currentgroup -match '\\') {
                        $split = $currentgroup.Split('\')
                        $i = 0
                        $groupobject = $serverstore.DatabaseEngineServerGroup
                        do {
                            if ($groupobject) {
                                $groupobject = $groupobject.ServerGroups[$split[$i]]
                                Write-Message -Level Verbose -Message "Parsed $($groupobject.Name)" -FunctionName Get-DbaRegServerGroup -ModuleName "dbatools"
                            }
                        }
                        until ($i++ -eq $split.GetUpperBound(0))
                        if ($groupobject) {
                            $groups += $groupobject
                        }
                    } else {
                        try {
                            $thisgroup = $serverstore.DatabaseEngineServerGroup.ServerGroups[$currentgroup]
                            if ($thisgroup) {
                                Write-Message -Level Verbose -Message "Added $($thisgroup.Name)" -FunctionName Get-DbaRegServerGroup -ModuleName "dbatools"
                                $groups += $thisgroup
                            }
                        } catch {
                            # here to avoid an empty catch
                            $null = 1
                        }
                    }
                }
            } else {
                Write-Message -Level Verbose -Message "Added all root server groups" -FunctionName Get-DbaRegServerGroup -ModuleName "dbatools"
                $groups = $serverstore.DatabaseEngineServerGroup.ServerGroups
            }

            if ($Group -eq 'DatabaseEngineServerGroup') {
                Write-Message -Level Verbose -Message "Added root group" -FunctionName Get-DbaRegServerGroup -ModuleName "dbatools"
                $groups = $serverstore.DatabaseEngineServerGroup
            }

            if ($ExcludeGroup) {
                $excluded = Get-DbaRegServerGroup -SqlInstance $serverstore.ParentServer -SqlCredential $SqlCredential -Group $ExcludeGroup
                Write-Message -Level Verbose -Message "Excluding $ExcludeGroup" -FunctionName Get-DbaRegServerGroup -ModuleName "dbatools"
                $groups = $groups | Where-Object { $_.Urn.Value -notin $excluded.Urn.Value }
            }

            if ($Id) {
                Write-Message -Level Verbose -Message "Filtering for id $Id. Id 1 = default." -FunctionName Get-DbaRegServerGroup -ModuleName "dbatools"
                if ($Id -eq 1) {
                    $groups = $serverstore.DatabaseEngineServerGroup
                } else {
                    $groups = $serverstore.DatabaseEngineServerGroup.GetDescendantRegisteredServers().Parent | Where-Object Id -In $Id
                }
            }
            if ($serverstore.ServerConnection) {
                $serverstore.ServerConnection.Disconnect()
            }

            foreach ($groupobject in $groups) {
                Add-Member -Force -InputObject $groupobject -MemberType NoteProperty -Name ComputerName -Value $serverstore.ComputerName
                Add-Member -Force -InputObject $groupobject -MemberType NoteProperty -Name InstanceName -Value $serverstore.InstanceName
                Add-Member -Force -InputObject $groupobject -MemberType NoteProperty -Name SqlInstance -Value $serverstore.SqlInstance
                Add-Member -Force -InputObject $groupobject -MemberType NoteProperty -Name ParentServer -Value $serverstore.ParentServer

                if ($groupobject.ComputerName) {
                    Select-DefaultView -InputObject $groupobject -Property ComputerName, InstanceName, SqlInstance, Name, DisplayName, Description, ServerGroups, RegisteredServers
                } else {
                    Select-DefaultView -InputObject $groupobject -Property Name, DisplayName, Description, ServerGroups, RegisteredServers
                }
            }
        }
    }

    $__ss = Get-Variable -Name serverstores -Scope 0 -ErrorAction Ignore
    $__gr = Get-Variable -Name groups -Scope 0 -ErrorAction Ignore
    $__ssv = $null; if ($__ss) { $__ssv = $__ss.Value }
    $__grv = $null; if ($__gr) { $__grv = $__gr.Value }
    @{ __getDbaRegServerGroupProcess = @{ Serverstores = $__ssv; Groups = $__grv } }
} $SqlInstance $SqlCredential $Group $ExcludeGroup $Id $EnableException $__beginState $__state $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}