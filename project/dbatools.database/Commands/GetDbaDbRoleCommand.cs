#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves database role objects and metadata from databases. Port of public/Get-DbaDbRole.ps1;
/// the workflow remains a module-scoped PowerShell compatibility hop.
///
/// A process-only port with TWO ValueFromPipeline parameters (SqlInstance pos0 and InputObject pos6); the
/// -SqlInstance path gathers databases via Get-DbaDatabase and appends to $InputObject within the record.
/// The neither-piped guard (source 133) uses TRUTHINESS ($InputObject / $SqlInstance), NOT Test-Bound, so there
/// is NO carried-flag substitution; the guard's Stop-Function (no -Continue) + bare return exits the hop
/// scriptblock cleanly (the bare-return law - a bare return does NOT propagate out of the module scriptblock,
/// unlike a bare continue). The one continue (source 144) is inside foreach ($db) - loop-bound. No accumulator,
/// no interrupt, no ShouldProcess. The only edits are -FunctionName Get-DbaDbRole on the one Stop-Function and
/// one Write-Message. Surface pinned by migration/baselines/Get-DbaDbRole.json (positions 0-6 with
/// ExcludeFixedRole non-positional, two VFP, no ShouldProcess).
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbRole")]
public sealed class GetDbaDbRoleCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The database(s) to process.</summary>
    [Parameter(Position = 2)]
    public string[]? Database { get; set; }

    /// <summary>The database(s) to exclude.</summary>
    [Parameter(Position = 3)]
    public string[]? ExcludeDatabase { get; set; }

    /// <summary>Filter to the specified database role(s).</summary>
    [Parameter(Position = 4)]
    public string[]? Role { get; set; }

    /// <summary>The database role(s) to exclude.</summary>
    [Parameter(Position = 5)]
    public string[]? ExcludeRole { get; set; }

    /// <summary>Exclude the fixed database roles (and public).</summary>
    [Parameter]
    public SwitchParameter ExcludeFixedRole { get; set; }

    /// <summary>Database object(s) piped in from Get-DbaDatabase.</summary>
    [Parameter(ValueFromPipeline = true, Position = 6)]
    public Microsoft.SqlServer.Management.Smo.Database[]? InputObject { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, SqlCredential, Database, ExcludeDatabase, Role, ExcludeRole, ExcludeFixedRole.ToBool(),
            InputObject, EnableException.ToBool(), BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
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
    // PS: the process block VERBATIM. Edit: -FunctionName Get-DbaDbRole on the one Stop-Function and one
    // Write-Message. The neither-piped guard uses truthiness (no Test-Bound); its bare return exits cleanly;
    // the one continue is inside foreach ($db) - loop-bound.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $ExcludeDatabase, $Role, $ExcludeRole, $ExcludeFixedRole, $InputObject, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [string[]]$Database, [string[]]$ExcludeDatabase, [string[]]$Role, [string[]]$ExcludeRole, [switch]$ExcludeFixedRole, [Microsoft.SqlServer.Management.Smo.Database[]]$InputObject, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        if (-not $InputObject -and -not $SqlInstance) {
            Stop-Function -Message "You must pipe in a database or specify a SqlInstance" -FunctionName Get-DbaDbRole
            return
        }

        if ($SqlInstance) {
            $InputObject += Get-DbaDatabase -SqlInstance $SqlInstance -SqlCredential $SqlCredential -Database $Database -ExcludeDatabase $ExcludeDatabase -EnableException:$EnableException
        }

        foreach ($db in $InputObject) {
            if ($db.IsAccessible -eq $false) {
                continue
            }
            $server = $db.Parent
            Write-Message -Level 'Verbose' -Message "Getting Database Roles for $db on $server" -FunctionName Get-DbaDbRole

            $dbRoles = $db.Roles
            if ($Role) {
                $dbRoles = $dbRoles | Where-Object { $_.Name -in $Role }
            }
            if ($ExcludeRole) {
                $dbRoles = $dbRoles | Where-Object { $_.Name -notin $ExcludeRole }
            }
            if ($ExcludeFixedRole) {
                $dbRoles = $dbRoles | Where-Object { $_.IsFixedRole -eq $false -and $_.Name -ne 'public' }
            }

            foreach ($dbRole in $dbRoles) {
                Add-Member -Force -InputObject $dbRole -MemberType NoteProperty -Name ComputerName -Value $server.ComputerName
                Add-Member -Force -InputObject $dbRole -MemberType NoteProperty -Name InstanceName -Value $server.ServiceName
                Add-Member -Force -InputObject $dbRole -MemberType NoteProperty -Name SqlInstance -Value $server.DomainInstanceName
                Add-Member -Force -InputObject $dbRole -MemberType NoteProperty -Name Database -Value $db.Name
                Select-DefaultView -InputObject $dbRole -Property "ComputerName", "InstanceName", "Database", "Name", "IsFixedRole"
            }
        }
} $SqlInstance $SqlCredential $Database $ExcludeDatabase $Role $ExcludeRole $ExcludeFixedRole $InputObject $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}