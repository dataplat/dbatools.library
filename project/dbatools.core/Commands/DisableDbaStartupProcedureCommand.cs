#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Disables SQL Server startup stored procedures. Port of
/// public/Disable-DbaStartupProcedure.ps1 (W3-008). The begin block only sets the $action
/// ('Disable') and $startup ($false) constants consumed inside the same process body, so there
/// is NO end hop - the begin constants inline into the process script. DEF-012: the emit block
/// (Add-Member + Select-DefaultView) sits OUTSIDE the inner foreach, so $sp/$db/$server/$status/
/// $note DO persist across records in the function world and are carried (trailer packet
/// __dbatoolsDspCarrier, keyed on a per-invocation token, -> _carrierState). DEF-001 cond1+cond2: the process body EMITS (Select-DefaultView) per record AND has
/// reachable Stop-Function -Continue at Connect-DbaInstance / the procedure-parse checks, so the
/// hop STREAMS via InvokeScopedStreaming - a buffered hop would lose an earlier record's emit
/// when a later record throws under -EnableException. The source's Test-Bound guards
/// (InputObject / SqlInstance / StartupProcedure) are carried as bound flags - the scriptblock
/// runs in module scope and cannot see the real cmdlet's $PSBoundParameters. Substitutions only:
/// Test-Bound -> the carried $__bound* flags, $Pscmdlet.ShouldProcess -> $__realCmdlet.ShouldProcess
/// (ConfirmImpact HIGH mirrored), and explicit -FunctionName Disable-DbaStartupProcedure on every
/// Stop-Function (W1-090); the body is otherwise verbatim (including the source's emit-outside-the
/// -inner-foreach shape). Surface pinned by migration/baselines/Disable-DbaStartupProcedure.json.
/// </summary>
[Cmdlet(VerbsLifecycle.Disable, "DbaStartupProcedure", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.High, DefaultParameterSetName = "Default")]
public sealed class DisableDbaStartupProcedureCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The startup procedure(s) to disable (schema.name).</summary>
    [Parameter(Position = 2)]
    public string[]? StartupProcedure { get; set; }

    /// <summary>Startup procedure object(s) piped in.</summary>
    [Parameter(ValueFromPipeline = true, Position = 3)]
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
            // DEF-012: the trailer packet parks the source's cross-record variables. It is
            // identified by a PER-INVOCATION token, never by property name alone (codex r1): a
            // caller can pipe an SMO StoredProcedure that has been Add-Member'd with any property
            // name they like, and Select-DefaultView passes extended properties straight through -
            // name-plus-truthiness would let a decorated input object be swallowed as output AND
            // corrupt the carrier state. The token is unguessable from outside this invocation.
            if (item?.Properties["__dbatoolsDspCarrier"]?.Value is string token &&
                string.Equals(token, _carrierToken, StringComparison.Ordinal))
            {
                _carrierState = item;
                return;
            }
            WriteObject(item);
        }, ProcessScript,
            SqlInstance, SqlCredential, StartupProcedure, InputObject, EnableException.ToBool(), this,
            TestBound(nameof(InputObject)), TestBound(nameof(SqlInstance)), TestBound(nameof(StartupProcedure)),
            BoundCommonParameter("WhatIf"), BoundCommonParameter("Confirm"),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug"),
            _carrierState, _carrierToken);
    }

    // DEF-012 cross-record carrier state - the trailer PSObject from the previous record, carrying
    // both each variable's value AND whether it was ASSIGNED in that record's scope (codex r1):
    // PowerShell distinguishes "assigned $null" from "never assigned", and only the latter falls
    // through to a module-scope variable of the same name. Seeding unconditionally would make
    // record 1 (nothing carried yet, legacy still unassigned) diverge in the opposite direction.
    private object? _carrierState;
    private readonly string _carrierToken = Guid.NewGuid().ToString("N");

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

    // PS: the begin constants ($action / $startup) inline ahead of the process body, which is
    // VERBATIM per record. Substitutions only: Test-Bound -> carried $__bound* flags, $Pscmdlet
    // -> $__realCmdlet, explicit -FunctionName Disable-DbaStartupProcedure on Stop-Function (W1-090).
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $StartupProcedure, $InputObject, $EnableException, $__realCmdlet, $__boundInputObject, $__boundSqlInstance, $__boundStartupProcedure, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug, $__carrier, $__carrierToken)
$__commonParameters = @{}
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = "High")]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [string[]]$StartupProcedure, [object[]]$InputObject, $EnableException, $__realCmdlet, $__boundInputObject, $__boundSqlInstance, $__boundStartupProcedure, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug, $__carrier, $__carrierToken)
    # DEF-012 (codex, return-sweep): the emit block below sits OUTSIDE the inner foreach, so in the
    # function world it runs once per RECORD with the LAST iteration's $sp/$db/$server/$status/$note
    # - and on a record whose $InputObject is empty (pipe $null, or -StartupProcedure resolves
    # nothing so every Stop-Function -Continue fires) those variables RETAIN the prior record's
    # values and the function re-emits the stale $sp. A fresh hop scope would see $null. Seed all
    # five from the carrier; the trailer packet at the bottom parks their post-record values.
    # Seed on the ASSIGNED flag, not on non-nullness: PowerShell treats "assigned `$null" and
    # "never assigned" differently (only the latter falls through to a module-scope variable of
    # the same name), so an assigned `$null must be replayed as an assignment (codex r1).
    if ($null -ne $__carrier) {
        if ($__carrier.HasSp) { $sp = $__carrier.Sp }
        if ($__carrier.HasDb) { $db = $__carrier.Db }
        if ($__carrier.HasServer) { $server = $__carrier.Server }
        if ($__carrier.HasStatus) { $status = $__carrier.Status }
        if ($__carrier.HasNote) { $note = $__carrier.Note }
    }
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    $action = 'Disable'
    $startup = $false

    if (-not $__boundInputObject) {
        if ($__boundSqlInstance) {
            if (-not $__boundStartupProcedure) {
                Stop-Function -Message "You must specify one or more Startup Procedures when using the SqlInstance parameter." -FunctionName Disable-DbaStartupProcedure
                return
            }
        } else {
            Stop-Function -Message "You must supply either a SqlInstance or an InputObject ." -FunctionName Disable-DbaStartupProcedure
            return
        }

        foreach ($instance in $SqlInstance) {
            Write-Message -Level Verbose -Message "Getting startup procedures for $instance" -FunctionName Disable-DbaStartupProcedure -ModuleName "dbatools"
            try {
                $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
            } catch {
                Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Disable-DbaStartupProcedure
            }
            $db = $server.Databases['master']

            foreach ($proc in $StartupProcedure) {
                $procParts = Get-ObjectNameParts $proc
                if ($procParts.Parsed) {
                    $sp = $db.StoredProcedures.Item($procParts.Name, $procParts.Schema)
                    if ($null -eq $sp) {
                        Stop-Function -Message "Requested procedure $proc does not exist." -Continue -Target $server -Category InvalidData -FunctionName Disable-DbaStartupProcedure
                    } else {
                        Write-Message -Level Verbose -Message "Adding $($procParts.Name) $($procParts.Schema) for $instance" -FunctionName Disable-DbaStartupProcedure -ModuleName "dbatools"
                        $InputObject += $sp
                    }
                } else {
                    Stop-Function -Message "Requested procedure $proc could not be parsed." -Continue -Target $server -Category InvalidData -FunctionName Disable-DbaStartupProcedure
                }
            }
        }
    }

    foreach ($sp in $InputObject) {
        $db = $sp.Parent
        $server = $db.Parent

        try {
            if ($sp.Startup -eq $startup) {
                Write-Message -Level Verbose -Message "No work being performed. Startup property already $startup" -FunctionName Disable-DbaStartupProcedure -ModuleName "dbatools"
                $status = $false
                $note = "Action $action already performed"
            } else {
                if ($__realCmdlet.ShouldProcess("$instance", "Setting Startup status of $proc to $startup")) {
                    $sp.Startup = $startup
                    $sp.Alter()
                    $status = $true
                    $note = "$action succeded"
                } else {
                    $status = $false
                    $note = "$action skipped"
                }
            }

        } catch {
            $status = $false
            $note = "$action failed"
        }
    }

    Add-Member -Force -InputObject $sp -MemberType NoteProperty -Name ComputerName -value $server.ComputerName
    Add-Member -Force -InputObject $sp -MemberType NoteProperty -Name InstanceName -value $server.ServiceName
    Add-Member -Force -InputObject $sp -MemberType NoteProperty -Name SqlInstance -value $server.DomainInstanceName
    Add-Member -Force -InputObject $sp -MemberType NoteProperty -Name Database -value $db.Name
    Add-Member -Force -InputObject $sp -MemberType NoteProperty -Name Action -value $action
    Add-Member -Force -InputObject $sp -MemberType NoteProperty -Name Status -value $status
    Add-Member -Force -InputObject $sp -MemberType NoteProperty -Name Note -value $note

    $defaults = 'ComputerName', 'InstanceName', 'SqlInstance', 'Database', 'Schema', 'Name', 'Startup', 'Action', 'Status', 'Note'
    Select-DefaultView -InputObject $sp -Property $defaults
    [pscustomobject]@{
        __dbatoolsDspCarrier = $__carrierToken
        HasSp                = $null -ne (Get-Variable -Name sp -Scope 0 -ErrorAction Ignore)
        Sp                   = $sp
        HasDb                = $null -ne (Get-Variable -Name db -Scope 0 -ErrorAction Ignore)
        Db                   = $db
        HasServer            = $null -ne (Get-Variable -Name server -Scope 0 -ErrorAction Ignore)
        Server               = $server
        HasStatus            = $null -ne (Get-Variable -Name status -Scope 0 -ErrorAction Ignore)
        Status               = $status
        HasNote              = $null -ne (Get-Variable -Name note -Scope 0 -ErrorAction Ignore)
        Note                 = $note
    }
} $SqlInstance $SqlCredential $StartupProcedure $InputObject $EnableException $__realCmdlet $__boundInputObject $__boundSqlInstance $__boundStartupProcedure $__boundWhatIf $__boundConfirm $__boundVerbose $__boundDebug $__carrier $__carrierToken @__commonParameters 3>&1 2>&1
""";
}
