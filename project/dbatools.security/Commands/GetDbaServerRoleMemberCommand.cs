#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Gets the logins that are members of the server-level roles on a SQL Server instance.
/// </summary>
/// <remarks>
/// <para>
/// The workflow remains a module-scoped PowerShell compatibility hop so that the role and member
/// enumeration, the login filtering, the emitted object shape, and dbatools stream and error handling stay
/// observable-identical to the script implementation.
/// </para>
/// <para>
/// The command is process-only and carries no cross-record state, so it ships as a single hop per record.
/// The login-lookup Stop-Function has NO -Continue and NO following return, so in normal mode it warns,
/// latches the dbatools interrupt, and FALLS THROUGH into the filtering below with whatever $logins holds
/// - that is the source's behaviour and it is reproduced verbatim, not guarded. Nothing in this process
/// block reads Test-FunctionInterrupt, so the latch has no further effect and needs no carry. It streams
/// through InvokeScopedStreaming rather than buffering: SqlInstance is an array and the body emits per
/// member, so one record can emit members for an early instance and then terminate on a later one under
/// -EnableException, where a buffered call would discard what was already produced (DEF-001).
/// </para>
/// <para>
/// The five Test-Bound reads map to carried by-name flags, since Test-Bound scope-walks the caller and
/// cannot ride a hop; the two that gate on Login share one flag, as the source's two reads of the same
/// parameter do. All switches and EnableException are carried as plain (untyped) values, because a switch
/// in the inner CmdletBinding scriptblock is excluded from positional binding. Every direct Stop-Function
/// takes -FunctionName and every direct Write-Message takes -FunctionName plus -ModuleName "dbatools".
/// </para>
/// </remarks>
[Cmdlet(VerbsCommon.Get, "DbaServerRoleMember")]
[OutputType(typeof(PSCustomObject))]
public sealed class GetDbaServerRoleMemberCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    [Alias("Credential")]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The server roles to report members for.</summary>
    [Parameter(Position = 2)]
    [PsStringArrayCast]
    public string[]? ServerRole { get; set; }

    /// <summary>Server roles to exclude from the report.</summary>
    [Parameter(Position = 3)]
    [PsStringArrayCast]
    public string[]? ExcludeServerRole { get; set; }

    /// <summary>Restricts the report to the roles these logins belong to.</summary>
    [Parameter(Position = 4)]
    public object[]? Login { get; set; }

    /// <summary>Excludes the fixed server roles.</summary>
    [Parameter]
    public SwitchParameter ExcludeFixedRole { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    /// <summary>
    /// The process block's cross-record state.
    /// </summary>
    /// <remarks>
    /// The script's process block is ONE function scope for the whole pipeline. If a later record's
    /// Get-DbaLogin throws, its assignment to $logins is aborted and the variable RETAINS the previous
    /// record's value, which the body then keeps filtering against - so the script still returns results
    /// for that record. A per-record hop starts with $logins null and filters everything out. Measured
    /// by codex against mocked servers: source returned two results, the port one. Every variable the
    /// process block assigns is carried, so the body's own assignments still land where the source's do.
    /// </remarks>
    private Hashtable? _processState;

    /// <summary>Returns the role members for the instances bound to the current record.</summary>
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
            }
            else if (item is not null && item.BaseObject is PSCustomObject && LanguagePrimitives.IsTrue(
                item.Properties["__GetDbaServerRoleMemberProcessComplete"]?.Value))
            {
                _processState = UnwrapHopValue(item.Properties["State"]?.Value) as Hashtable;
            }
            else if (item is not null)
            {
                WriteObject(item);
            }
        }, ProcessScript,
            SqlInstance, SqlCredential, ServerRole, ExcludeServerRole, Login, ExcludeFixedRole.ToBool(),
            EnableException.ToBool(),
            TestBound(nameof(Login)), TestBound(nameof(ServerRole)), TestBound(nameof(ExcludeServerRole)),
            TestBound(nameof(ExcludeFixedRole)), _processState,
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug"));
    }

    /// <summary>
    /// Unwraps a value the hop carried out through its sentinel.
    /// </summary>
    /// <remarks>
    /// A value the script left unset arrives as AutomationNull, which behaves as $null in PowerShell but
    /// unwraps to a truthy, property-less object - so it comes back as null instead. Otherwise the value is
    /// unwrapped ONLY when the wrapper adds nothing: note properties live on the PSObject wrapper rather
    /// than the BaseObject, so unwrapping such a value silently discards them.
    /// </remarks>
    private static object? UnwrapHopValue(object? value)
    {
        if (value is null || ReferenceEquals(value, System.Management.Automation.Internal.AutomationNull.Value))
            return null;
        if (value is not PSObject wrapper)
            return value;
        foreach (PSMemberInfo member in wrapper.Members)
        {
            if (member is PSNoteProperty)
                return wrapper;
        }
        return wrapper.BaseObject;
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

    // PS: the process body VERBATIM. Substitutions only: the five Test-Bound reads -> the carried by-name
    // flags (the two Login reads share one flag, as the source's two reads of that parameter do);
    // -FunctionName on the two DIRECT Stop-Function calls; -FunctionName + -ModuleName "dbatools" on the
    // five DIRECT Write-Message calls. Switches and EnableException are received untyped.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $ServerRole, $ExcludeServerRole, $Login, $ExcludeFixedRole, $EnableException, $__loginBound, $__serverRoleBound, $__excludeServerRoleBound, $__excludeFixedRoleBound, $__carryState, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [System.Management.Automation.PSCredential]$SqlCredential, [string[]]$ServerRole, [string[]]$ExcludeServerRole, [object[]]$Login, $ExcludeFixedRole, $EnableException, $__loginBound, $__serverRoleBound, $__excludeServerRoleBound, $__excludeFixedRoleBound, $__carryState, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    # Process-scope state as the previous record left it - the script keeps ONE function scope for the
    # whole pipeline. try/finally because the body can return early; finally always hands the state back.
    if ($__carryState) { foreach ($__k in $__carryState.Keys) { Set-Variable -Name $__k -Value $__carryState[$__k] } }
    try {

        foreach ($instance in $SqlInstance) {
            try {
                $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
            } catch {
                Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaServerRoleMember
            }

            $roles = $server.Roles

            if ($__loginBound) {
                try {
                    $logins = Get-DbaLogin -SqlInstance $server -Login $Login -EnableException
                } catch {
                    Stop-Function -Message "Issue gathering login details" -ErrorRecord $_ -Target $instance -FunctionName Get-DbaServerRoleMember
                }
                Write-Message -Level 'Verbose' -Message "Filtering by logins: $($logins -join ', ')" -FunctionName Get-DbaServerRoleMember -ModuleName "dbatools"
                $loginRoles = @()
                foreach ($l in $logins) {
                    $loginRoles += $l.ListMembers()
                }

                $loginRoles = $loginRoles | Select-Object -Unique
                Write-Message -Level 'Verbose' -Message "Filtering by roles: $($loginRoles -join ', ')" -FunctionName Get-DbaServerRoleMember -ModuleName "dbatools"

                $roles = $roles | Where-Object { $_.Name -in $loginRoles }
            }

            if ($__serverRoleBound) {
                $roles = $roles | Where-Object { $_.Name -in $ServerRole }
            }

            if ($__excludeServerRoleBound) {
                $roles = $roles | Where-Object { $_.Name -notin $ExcludeServerRole }
            }

            if ($__excludeFixedRoleBound) {
                $roles = $roles | Where-Object { $_.IsFixedRole -eq $false }
            }

            foreach ($role in $roles) {
                Write-Message -Level 'Verbose' -Message "Getting Server Role Members for $role on $instance" -FunctionName Get-DbaServerRoleMember -ModuleName "dbatools"

                $members = $role.EnumMemberNames()
                Write-Message -Level 'Verbose' -Message "$role members: $($members -join ', ')" -FunctionName Get-DbaServerRoleMember -ModuleName "dbatools"

                if ($__loginBound) {
                    Write-Message -Level 'Verbose' -Message "Only returning results for $($logins.Name -join ', ')" -FunctionName Get-DbaServerRoleMember -ModuleName "dbatools"
                    $members = $members | Where-Object { $_ -in $logins.Name }
                }

                foreach ($member in $members) {
                    $loginList = $server.Logins | Where-Object { $_.Name -eq $member }

                    if ($loginList) {
                        [PSCustomObject]@{
                            ComputerName = $server.ComputerName
                            InstanceName = $server.ServiceName
                            SqlInstance  = $server.DomainInstanceName
                            Role         = $role.Name
                            Name         = $loginList.Name
                            SmoRole      = $role
                            SmoLogin     = $loginList
                        }
                    }
                }
            }
        }
    } finally {
    [pscustomobject]@{ __GetDbaServerRoleMemberProcessComplete = $true; State = @{ server = $server; roles = $roles; logins = $logins; loginRoles = $loginRoles; members = $members; loginList = $loginList } }
    }
} $SqlInstance $SqlCredential $ServerRole $ExcludeServerRole $Login $ExcludeFixedRole $EnableException $__loginBound $__serverRoleBound $__excludeServerRoleBound $__excludeFixedRoleBound $__carryState $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1

""";
}
