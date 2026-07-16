#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Registers SQL Server instances to CMS or Local Server Groups. Port of
/// public/Add-DbaRegServer.ps1 (W3-002). The ENTIRE process body rides one VERBATIM
/// module hop per record inside a DOT-SOURCED inner block (W1-108: the early
/// Stop-Function return still lets the trailing state sentinel emit), with
/// $Pscmdlet.ShouldProcess routed to the REAL cmdlet (W1-085) and the
/// $PSBoundParameters value-truthiness reads carried as raw bound values. Bound
/// -WhatIf/-Confirm carry into the hop's own SupportsShouldProcess binding (the Copy-family
/// convention) so the nested Add-DbaRegServerGroup creation OUTSIDE the outer gate inherits
/// the caller's preference exactly as fn scope inherited it. Function-scope
/// mutations persist across records through the sentinel bag: $InputObject grows via +=
/// (typed re-coercion to ServerGroup[] included; a ReferenceEquals reset detects the
/// per-record pipeline rebind, W1-070), the $Name/$ServerName rewrites inside the
/// ServerObject loop stick for later records, and the stale-able locals
/// ($regServerGroup, $target, $newserver, loop variables) carry over exactly as PS
/// function scope carried them. The $Name parameter default (= $ServerName) is applied
/// once at first record like PS applies defaults once at binding (unbound [string]
/// reads ""). Nested Get-DbaRegServerGroup / Add-DbaRegServerGroup / Get-DbaRegServer
/// ride the hop verbatim. Surface pinned by migration/baselines/Add-DbaRegServer.json
/// (implicit positions 0-11 in declaration order, ConfirmImpact Medium, no OutputType).
/// </summary>
[Cmdlet(VerbsCommon.Add, "DbaRegServer", SupportsShouldProcess = true)]
public sealed class AddDbaRegServerCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance if a CMS is used.</summary>
    [Parameter(Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The actual instance name or network address used to connect.</summary>
    [Parameter(Position = 2)]
    public string? ServerName { get; set; }

    /// <summary>The display name shown in the Registered Servers tree; defaults to ServerName.</summary>
    [Parameter(Position = 3)]
    public string? Name { get; set; }

    /// <summary>Additional details shown in SSMS properties.</summary>
    [Parameter(Position = 4)]
    public string? Description { get; set; }

    /// <summary>The organizational folder (ServerGroup object or backslash-notation path).</summary>
    [Parameter(Position = 5)]
    public object? Group { get; set; }

    /// <summary>Azure Active Directory tenant ID.</summary>
    [Parameter(Position = 6)]
    public string? ActiveDirectoryTenant { get; set; }

    /// <summary>Azure Active Directory user principal name.</summary>
    [Parameter(Position = 7)]
    public string? ActiveDirectoryUserId { get; set; }

    /// <summary>A complete connection string for the registered server.</summary>
    [Parameter(Position = 8)]
    public string? ConnectionString { get; set; }

    /// <summary>Additional connection string parameters appended to the base connection.</summary>
    [Parameter(Position = 9)]
    public string? OtherParams { get; set; }

    /// <summary>Server group object(s) from Get-DbaRegServerGroup.</summary>
    [Parameter(ValueFromPipeline = true, Position = 10)]
    public Microsoft.SqlServer.Management.RegisteredServers.ServerGroup[]? InputObject { get; set; }

    /// <summary>SMO Server object(s) from Connect-DbaInstance to register.</summary>
    [Parameter(ValueFromPipeline = true, Position = 11)]
    public Microsoft.SqlServer.Management.Smo.Server[]? ServerObject { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // PS function-scope locals persisting across records (the state bag rides the hop
    // and comes back as the sentinel item).
    private Hashtable? _state;
    private object? _inputObjectState;
    private object? _lastBoundInputObject;
    private object? _nameState;
    private object? _serverNameState;
    private bool _bindInitialized;
    private bool _inputObjectNamedBound;

    protected override void BeginProcessing()
    {
        // Pipeline bindings are absent at begin time, so this pins whether InputObject was
        // NAMED-bound - the discriminator for the per-record rebind reset (codex W3-002 F1:
        // the same array INSTANCE piped twice defeats a pure ReferenceEquals check).
        _inputObjectNamedBound = TestBound("InputObject");
    }

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        // PS: named $InputObject keeps ONE array reference across records (the += growth
        // persists); a piped ServerGroup re-binds EVERY record it arrives in - even the
        // same instance again (W1-070 + codex W3-002 F1).
        if ((!_inputObjectNamedBound && TestBound("InputObject")) ||
            !ReferenceEquals(InputObject, _lastBoundInputObject) || !_bindInitialized)
        {
            _inputObjectState = InputObject;
            _lastBoundInputObject = InputObject;
        }

        if (!_bindInitialized)
        {
            // PS: parameter defaults apply once at binding; unbound [string] reads ""
            // (W1-087) and $Name defaults to $ServerName's post-binding value.
            _serverNameState = ServerName ?? "";
            _nameState = TestBound("Name") ? (object?)(Name ?? "") : _serverNameState;
            _bindInitialized = true;
        }

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, SqlCredential, _serverNameState, _nameState, Description ?? "", Group,
            ActiveDirectoryTenant ?? "", ActiveDirectoryUserId ?? "", ConnectionString ?? "",
            OtherParams ?? "", _inputObjectState, ServerObject, EnableException.ToBool(), _state,
            BoundRaw("ServerName"), BoundRaw("ServerObject"), BoundRaw("Name"), this,
            BoundCommonParameter("WhatIf"), BoundCommonParameter("Confirm"),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug"),
            BoundRaw("WarningAction")))
        {
            Hashtable? sentinel = item?.BaseObject as Hashtable;
            if (sentinel is not null && sentinel.ContainsKey("__w3002State"))
            {
                _state = sentinel["__w3002State"] as Hashtable;
                if (_state is not null)
                {
                    _inputObjectState = _state["InputObject"];
                    _nameState = _state["Name"];
                    _serverNameState = _state["ServerName"];
                }
                continue;
            }
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
                continue;
            }
            WriteObject(item);
        }
    }

    /// <summary>The raw bound value (or null when unbound) so the hop's
    /// $PSBoundParameters.X reads keep their value-truthiness semantics.</summary>
    private object? BoundRaw(string name)
    {
        if (MyInvocation.BoundParameters.TryGetValue(name, out object? value))
            return value;
        return null;
    }

    private object? BoundCommonParameter(string name)
    {
        if (MyInvocation.BoundParameters.TryGetValue(name, out object? value))
            return LanguagePrimitives.IsTrue(value);
        return null;
    }

    /// <summary>Removes the silent $error copy the nested pipeline bagged for a merged-back
    /// non-terminating record (the W1-045 compensation).</summary>
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

    // PS: the ENTIRE process body VERBATIM inside a dot-sourced inner block (the early
    // return after the parameterless-call Stop-Function exits the block; the trailing
    // sentinel still emits the mutated fn-scope state). Substitutions only:
    // $PSBoundParameters.X -> carried $__boundX raw values, $Pscmdlet -> $__realCmdlet,
    // and explicit -FunctionName Add-DbaRegServer on Stop-Function/Write-Message (W1-090).
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $ServerName, $Name, $Description, $Group, $ActiveDirectoryTenant, $ActiveDirectoryUserId, $ConnectionString, $OtherParams, $InputObject, $ServerObject, $EnableException, $__state, $__boundServerName, $__boundServerObject, $__boundName, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug, $__boundWarningAction)
$__commonParameters = @{}
if ($null -ne $__boundWarningAction) { $__commonParameters.WarningAction = $__boundWarningAction }
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = "Medium")]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [string]$ServerName, [string]$Name, [string]$Description, [object]$Group, [string]$ActiveDirectoryTenant, [string]$ActiveDirectoryUserId, [string]$ConnectionString, [string]$OtherParams, [Microsoft.SqlServer.Management.RegisteredServers.ServerGroup[]]$InputObject, [Microsoft.SqlServer.Management.Smo.Server[]]$ServerObject, $EnableException, $__state, $__boundServerName, $__boundServerObject, $__boundName, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug, $__boundWarningAction)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    # restore fn-scope locals mutated by earlier records
    if ($null -ne $__state) {
        $regServerGroup = $__state.regServerGroup
        $reggroup = $__state.reggroup
        $target = $__state.target
        $server = $__state.server
        $newserver = $__state.newserver
        $instance = $__state.instance
    }

    . {
        # double check in case a null name was bound
        if (-not $__boundServerName -and -not $__boundServerObject) {
            Stop-Function -Message "You must specify either ServerName or ServerObject" -FunctionName Add-DbaRegServer
            return
        }
        if (-not $Name) {
            if ($ServerObject) {
                $Name = $ServerObject.Name
            } else {
                $Name = $ServerName
            }
        }

        if ((-not $SqlInstance -and -not $InputObject) -or $ServerObject) {
            Write-Message -Level Verbose -Message "Parsing local" -FunctionName Add-DbaRegServer
            if (($Group)) {
                if ($Group -is [Microsoft.SqlServer.Management.RegisteredServers.ServerGroup]) {
                    $regServerGroup = Get-DbaRegServerGroup -Group $Group.Name
                } else {
                    Write-Message -Level Verbose -Message "String group provided" -FunctionName Add-DbaRegServer
                    $regServerGroup = Get-DbaRegServerGroup -Group $Group
                }
                if ($regServerGroup) {
                    $InputObject += $regServerGroup
                } else {
                    # Create the Group
                    if ($Group -is [Microsoft.SqlServer.Management.RegisteredServers.ServerGroup]) {
                        $InputObject += Add-DbaRegServerGroup -Name $Group.Name
                    } else {
                        Write-Message -Level Verbose -Message "String group provided" -FunctionName Add-DbaRegServer
                        $InputObject += Add-DbaRegServerGroup -Name $Group
                    }
                }
            } else {
                Write-Message -Level Verbose -Message "No group passed, getting root" -FunctionName Add-DbaRegServer
                $InputObject += Get-DbaRegServerGroup -Id 1
            }
        }

        foreach ($instance in $SqlInstance) {
            if (($Group)) {
                if ($Group -is [Microsoft.SqlServer.Management.RegisteredServers.ServerGroup]) {
                    $regServerGroup = Get-DbaRegServerGroup -SqlInstance $instance -SqlCredential $SqlCredential -Group $Group.Name
                } else {
                    $regServerGroup = Get-DbaRegServerGroup -SqlInstance $instance -SqlCredential $SqlCredential -Group $Group
                }

                if ($regServerGroup) {
                    $InputObject += $regServerGroup
                } else {
                    if ($Group -is [Microsoft.SqlServer.Management.RegisteredServers.ServerGroup]) {
                        $InputObject += Add-DbaRegServerGroup -SqlInstance $instance -SqlCredential $SqlCredential -Name $Group.Name
                    } else {
                        Write-Message -Level Verbose -Message "String group provided" -FunctionName Add-DbaRegServer
                        $InputObject += Add-DbaRegServerGroup -SqlInstance $instance -SqlCredential $SqlCredential -Name $Group
                    }
                }
            } else {
                $InputObject += Get-DbaRegServerGroup -SqlInstance $instance -SqlCredential $SqlCredential -Id 1
            }
        }

        foreach ($reggroup in $InputObject) {
            if ($reggroup.Source -eq "Azure Data Studio") {
                Stop-Function -Message "You cannot use dbatools to remove or add registered servers in Azure Data Studio" -Continue -FunctionName Add-DbaRegServer
            }
            Write-Message -Level Verbose -Message "ID: $($reggroup.ID)" -FunctionName Add-DbaRegServer
            if ($reggroup.ID) {
                $target = $reggroup.ParentServer.SqlInstance
            } else {
                $target = "Local Registered Servers"
            }
            if ($__realCmdlet.ShouldProcess($target, "Adding $name")) {

                if ($ServerObject) {
                    foreach ($server in $ServerObject) {
                        if (-not $__boundName) {
                            $Name = $server.Name
                        }
                        if (-not $__boundServerName) {
                            $ServerName = $server.Name
                        }
                        try {
                            $newserver = New-Object Microsoft.SqlServer.Management.RegisteredServers.RegisteredServer($reggroup, $Name)
                            $newserver.ServerName = $ServerName
                            $newserver.Description = $Description
                            $newserver.ConnectionString = $server.ConnectionContext.ConnectionString
                            $newserver.SecureConnectionString = $server.ConnectionContext.SecureConnectionString
                            $newserver.ActiveDirectoryTenant = $ActiveDirectoryTenant
                            $newserver.ActiveDirectoryUserId = $ActiveDirectoryUserId
                            $newserver.OtherParams = $OtherParams
                            $newserver.CredentialPersistenceType = "PersistLoginNameAndPassword"
                            $newserver.Create()

                            Get-DbaRegServer -SqlInstance $reggroup.ParentServer -Name $Name -ServerName $ServerName | Where-Object Source -ne 'Azure Data Studio'
                        } catch {
                            Stop-Function -Message "Failed to add $ServerName on $target" -ErrorRecord $_ -Continue -FunctionName Add-DbaRegServer
                        }
                    }
                } else {
                    try {
                        $newserver = New-Object Microsoft.SqlServer.Management.RegisteredServers.RegisteredServer($reggroup, $Name)
                        $newserver.ServerName = $ServerName
                        $newserver.Description = $Description
                        $newserver.ConnectionString = $ConnectionString
                        $newserver.ActiveDirectoryTenant = $ActiveDirectoryTenant
                        $newserver.ActiveDirectoryUserId = $ActiveDirectoryUserId
                        $newserver.OtherParams = $OtherParams
                        $newserver.Create()

                        Get-DbaRegServer -SqlInstance $reggroup.ParentServer -Name $Name -ServerName $ServerName | Where-Object Source -ne 'Azure Data Studio'
                    } catch {
                        Stop-Function -Message "Failed to add $ServerName on $target" -ErrorRecord $_ -Continue -FunctionName Add-DbaRegServer
                    }
                }
            }
        }
    }

    @{ __w3002State = @{ InputObject = $InputObject; Name = $Name; ServerName = $ServerName; regServerGroup = $regServerGroup; reggroup = $reggroup; target = $target; server = $server; newserver = $newserver; instance = $instance } }
} $SqlInstance $SqlCredential $ServerName $Name $Description $Group $ActiveDirectoryTenant $ActiveDirectoryUserId $ConnectionString $OtherParams $InputObject $ServerObject $EnableException $__state $__boundServerName $__boundServerObject $__boundName $__realCmdlet $__boundWhatIf $__boundConfirm $__boundVerbose $__boundDebug $__boundWarningAction @__commonParameters 3>&1 2>&1
""";
}
