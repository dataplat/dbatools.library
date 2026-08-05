#nullable enable

using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Copies user-defined server roles, their server-level permissions and their login memberships
/// between instances. Port of public/Copy-DbaServerRole.ps1. The whole workflow rides one
/// module-scoped PowerShell hop: it leans on SMO ServerRole scripting, EnumMemberNames/AddMember,
/// Get-DbaPermission for the grant statements and the private Select-DefaultView decoration, all of
/// which keep the retired function's engine semantics there. The compiled cmdlet supplies the real
/// ShouldProcess runtime. Surface pinned by migration/baselines/Copy-DbaServerRole.json.
/// </summary>
[Cmdlet(VerbsCommon.Copy, "DbaServerRole", DefaultParameterSetName = "Default",
    SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Medium)]
public sealed class CopyDbaServerRoleCommand : DbaBaseCmdlet
{
    /// <summary>The source SQL Server instance. Requires sysadmin and SQL Server 2012 or higher.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    public DbaInstanceParameter Source { get; set; } = null!;

    /// <summary>Alternative credential for the source instance.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SourceSqlCredential { get; set; }

    /// <summary>The destination SQL Server instances.</summary>
    [Parameter(Mandatory = true, Position = 2)]
    public DbaInstanceParameter[] Destination { get; set; } = null!;

    /// <summary>Alternative credential for destination instances.</summary>
    [Parameter(Position = 3)]
    public PSCredential? DestinationSqlCredential { get; set; }

    /// <summary>Only copy the server roles with these names.</summary>
    [Parameter(Position = 4)]
    public object[]? ServerRole { get; set; }

    /// <summary>Skip the server roles with these names.</summary>
    [Parameter(Position = 5)]
    public object[]? ExcludeServerRole { get; set; }

    /// <summary>Drop and recreate server roles that already exist on the destination.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // Begin and process merge into one hop, and there are no carried locals: no parameter takes
    // pipeline input, so ProcessRecord runs exactly once and nothing can survive between records.
    // The merge is what keeps the begin half's $ConfirmPreference = "none" visible to the
    // ShouldProcess calls in the destination loop, which is how -Force suppressed prompting before.
    protected override void ProcessRecord()
    {
        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                NestedCommand.RemoveDuplicateError(this, nestedError);
                WriteError(NestedCommand.PreserveErrorIdentity(nestedError));
            }
            else
            {
                WriteObject(item);
            }
        }, BodyScript,
            Source, SourceSqlCredential, Destination, DestinationSqlCredential,
            ServerRole, ExcludeServerRole, Force.ToBool(),
            EnableException.ToBool(), this, NestedCommand.BoundCommonParameter(this, "Verbose"),
            NestedCommand.BoundCommonParameter(this, "Debug"));
    }

    private const string BodyScript = """
param($Source, $SourceSqlCredential, $Destination, $DestinationSqlCredential, $ServerRole, $ExcludeServerRole, $Force, $EnableException, $__realCmdlet, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    # $Force is deliberately untyped: PowerShell excludes [switch] parameters from positional
    # binding, so one typed flag would shift every argument after it. It arrives as a real boolean,
    # which the -eq $false test below reads the way a switch reads.
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter]$Source, $SourceSqlCredential, [Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$Destination, $DestinationSqlCredential, [object[]]$ServerRole, [object[]]$ExcludeServerRole, $Force, $EnableException, $__realCmdlet, $__boundVerbose, $__boundDebug)

    try {
        $sourceServer = Connect-DbaInstance -SqlInstance $Source -SqlCredential $SourceSqlCredential -MinimumVersion 11
    } catch {
        Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $Source -FunctionName Copy-DbaServerRole
        return
    }

    $sourceRoles = $sourceServer.Roles | Where-Object { $PSItem.IsFixedRole -eq $false -and $PSItem.Name -ne "public" }

    if ($Force) { $ConfirmPreference = "none" }

    if (Test-FunctionInterrupt) { return }
    foreach ($destinstance in $Destination) {
        try {
            $destServer = Connect-DbaInstance -SqlInstance $destinstance -SqlCredential $DestinationSqlCredential -MinimumVersion 11
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $destinstance -Continue -FunctionName Copy-DbaServerRole
        }

        $destRoles = $destServer.Roles

        foreach ($currentRole in $sourceRoles) {
            $roleName = $currentRole.Name

            $copyRoleStatus = [PSCustomObject]@{
                SourceServer      = $sourceServer.Name
                DestinationServer = $destServer.Name
                Type              = "Server Role"
                Name              = $roleName
                Status            = $null
                Notes             = $null
                DateTime          = [Dataplat.Dbatools.Utility.DbaDateTime](Get-Date)
            }

            if ($ServerRole -and ($roleName -notin $ServerRole)) {
                continue
            }

            if ($ExcludeServerRole -and ($roleName -in $ExcludeServerRole)) {
                continue
            }

            if ($destRoles.Name -contains $roleName) {
                if ($force -eq $false) {
                    If ($__realCmdlet.ShouldProcess($destinstance, "Server role $roleName exists at destination. Use -Force to drop and migrate.")) {
                        $copyRoleStatus.Status = "Skipped"
                        $copyRoleStatus.Notes = "Already exists on destination"
                        $copyRoleStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject

                        Write-Message -Level Verbose -Message "Server role $roleName exists at destination. Use -Force to drop and migrate." -FunctionName Copy-DbaServerRole -ModuleName "dbatools"
                    }
                    continue
                } else {
                    If ($__realCmdlet.ShouldProcess($destinstance, "Dropping server role $roleName and recreating")) {
                        try {
                            Write-Message -Level Verbose -Message "Dropping server role $roleName" -FunctionName Copy-DbaServerRole -ModuleName "dbatools"
                            $destServer.Roles[$roleName].Drop()
                            $destServer.Roles.Refresh()
                        } catch {
                            $copyRoleStatus.Status = "Failed"
                            $copyRoleStatus.Notes = "$PSItem"
                            $copyRoleStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                            Write-Message -Level Verbose -Message "Issue dropping server role $roleName on $destinstance | $PSItem" -FunctionName Copy-DbaServerRole -ModuleName "dbatools"
                            continue
                        }
                    }
                }
            }

            if ($__realCmdlet.ShouldProcess($destinstance, "Creating server role $roleName")) {
                try {
                    Write-Message -Level Verbose -Message "Copying server role $roleName" -FunctionName Copy-DbaServerRole -ModuleName "dbatools"
                    $sql = $currentRole.Script() | Out-String
                    Write-Message -Level Debug -Message $sql -FunctionName Copy-DbaServerRole -ModuleName "dbatools"
                    $destServer.Query($sql)
                    $destServer.Roles.Refresh()

                    $splatPermissions = @{
                        SqlInstance        = $sourceServer
                        IncludeServerLevel = $true
                    }
                    $sourcePermissions = Get-DbaPermission @splatPermissions | Where-Object Grantee -eq $roleName
                    foreach ($perm in $sourcePermissions) {
                        try {
                            $permSql = $perm.GrantStatement
                            if ($permSql) {
                                Write-Message -Level Debug -Message "Granting permission: $permSql" -FunctionName Copy-DbaServerRole -ModuleName "dbatools"
                                $destServer.Query($permSql)
                            }
                        } catch {
                            Write-Message -Level Warning -Message "Could not grant permission for role $roleName on $destinstance | $PSItem" -FunctionName Copy-DbaServerRole -ModuleName "dbatools"
                        }
                    }

                    $members = $currentRole.EnumMemberNames()
                    foreach ($member in $members) {
                        if ($destServer.Logins.Name -contains $member) {
                            try {
                                Write-Message -Level Verbose -Message "Adding login $member to role $roleName" -FunctionName Copy-DbaServerRole -ModuleName "dbatools"
                                $destServer.Roles[$roleName].AddMember($member)
                            } catch {
                                Write-Message -Level Warning -Message "Could not add member $member to role $roleName on $destinstance | $PSItem" -FunctionName Copy-DbaServerRole -ModuleName "dbatools"
                            }
                        } else {
                            Write-Message -Level Verbose -Message "Login $member does not exist on destination, skipping membership" -FunctionName Copy-DbaServerRole -ModuleName "dbatools"
                        }
                    }

                    $copyRoleStatus.Status = "Successful"
                    $copyRoleStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                } catch {
                    $copyRoleStatus.Status = "Failed"
                    $copyRoleStatus.Notes = "$PSItem"
                    $copyRoleStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                    Write-Message -Level Verbose -Message "Issue creating server role $roleName on $destinstance | $PSItem" -FunctionName Copy-DbaServerRole -ModuleName "dbatools"
                    continue
                }
            }
        }
    }
} $Source $SourceSqlCredential $Destination $DestinationSqlCredential $ServerRole $ExcludeServerRole $Force $EnableException $__realCmdlet $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
