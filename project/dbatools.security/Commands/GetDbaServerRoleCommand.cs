#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Gets the server-level roles defined on a SQL Server instance, with their members.
/// </summary>
/// <remarks>
/// <para>
/// The workflow remains a module-scoped PowerShell compatibility hop so that the role enumeration, the
/// member lookup, the added note properties, the default view, and dbatools stream and error handling stay
/// observable-identical to the script implementation.
/// </para>
/// <para>
/// The command is process-only and carries no cross-record state, so it ships as a single hop per record.
/// It streams through InvokeScopedStreaming rather than buffering: SqlInstance is an array and the body
/// emits per role, so one record can emit roles for an early instance and then hit the connection
/// Stop-Function on a later one - which THROWS under -EnableException rather than taking its continue
/// branch - and a buffered call would discard the roles already produced (DEF-001). The source gates its
/// filters on plain variable truthiness rather than Test-Bound, so no boundness flags are carried, and it
/// has no Write-Message and no ShouldProcess: the single -FunctionName stamp is the only body edit.
/// EnableException is carried as a plain (untyped) value, because a switch in the inner CmdletBinding
/// scriptblock is excluded from positional binding.
/// </para>
/// </remarks>
[Cmdlet(VerbsCommon.Get, "DbaServerRole")]
[OutputType(typeof(Microsoft.SqlServer.Management.Smo.ServerRole))]
public sealed class GetDbaServerRoleCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The server roles to return.</summary>
    [Parameter(Position = 2)]
    [PsStringArrayCast]
    public string[]? ServerRole { get; set; }

    /// <summary>Server roles to exclude from the results.</summary>
    [Parameter(Position = 3)]
    [PsStringArrayCast]
    public string[]? ExcludeServerRole { get; set; }

    /// <summary>Excludes the fixed server roles.</summary>
    [Parameter]
    public SwitchParameter ExcludeFixedRole { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    /// <summary>Returns the server roles for the instances bound to the current record.</summary>
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
            else
            {
                WriteObject(item);
            }
        }, ProcessScript,
            SqlInstance, SqlCredential, ServerRole, ExcludeServerRole, ExcludeFixedRole.ToBool(),
            EnableException.ToBool(), BoundCommonParameter("Verbose"), BoundCommonParameter("Debug"));
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

    // PS: the process body VERBATIM. The only edit is -FunctionName on the single DIRECT Stop-Function
    // call. EnableException and the switch are received untyped.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $ServerRole, $ExcludeServerRole, $ExcludeFixedRole, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [System.Management.Automation.PSCredential]$SqlCredential, [string[]]$ServerRole, [string[]]$ExcludeServerRole, $ExcludeFixedRole, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        foreach ($instance in $SqlInstance) {
            try {
                $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential -AzureUnsupported
            } catch {
                Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaServerRole
            }

            $serverroles = $server.Roles

            if ($ServerRole) {
                $serverRoles = $serverRoles | Where-Object Name -In $ServerRole
            }
            if ($ExcludeServerRole) {
                $serverRoles = $serverRoles | Where-Object Name -NotIn $ExcludeServerRole
            }
            if ($ExcludeFixedRole) {
                $serverRoles = $serverRoles | Where-Object IsFixedRole -eq $false
            }

            foreach ($role in $serverRoles) {
                $members = $role.EnumMemberNames()

                Add-Member -Force -InputObject $role -MemberType NoteProperty -Name Login -Value $members
                Add-Member -Force -InputObject $role -MemberType NoteProperty -Name ComputerName -value $server.ComputerName
                Add-Member -Force -InputObject $role -MemberType NoteProperty -Name InstanceName -value $server.ServiceName
                Add-Member -Force -InputObject $role -MemberType NoteProperty -Name SqlInstance -value $server.DomainInstanceName
                Add-Member -Force -InputObject $role -MemberType NoteProperty -Name Role -Value $role.Name
                Add-Member -Force -InputObject $role -MemberType NoteProperty -Name ServerRole -Value $role.Name

                $default = 'ComputerName', 'InstanceName', 'SqlInstance', 'Role', 'Login', 'Owner', 'IsFixedRole', 'DateCreated', 'DateModified'
                Select-DefaultView -InputObject $role -Property $default
            }
        }
} $SqlInstance $SqlCredential $ServerRole $ExcludeServerRole $ExcludeFixedRole $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1

""";
}
