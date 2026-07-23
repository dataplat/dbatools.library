#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Finds orphaned database users (users whose login no longer exists on the instance). Port of
/// public/Get-DbaDbOrphanUser.ps1; the workflow remains a module-scoped PowerShell compatibility hop.
///
/// A process-only port (SqlInstance is ValueFromPipeline, so process fires per record); the simplest kind -
/// no begin/end, no accumulator, no interrupt (both Stop-Function calls are -Continue and there is no
/// Test-FunctionInterrupt or early return), no Test-Bound. Database/ExcludeDatabase are truthiness checks. The
/// only edits are -FunctionName Get-DbaDbOrphanUser on the two Stop-Function and five Write-Message calls.
/// Surface pinned by migration/baselines/Get-DbaDbOrphanUser.json (positions 0-3, no sets, no ShouldProcess).
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbOrphanUser")]
public sealed class GetDbaDbOrphanUserCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The database(s) to check.</summary>
    [Parameter(Position = 2)]
    public object[]? Database { get; set; }

    /// <summary>The database(s) to exclude.</summary>
    [Parameter(Position = 3)]
    public object[]? ExcludeDatabase { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                NestedCommand.RemoveDuplicateError(this, nestedError);
                WriteError(nestedError);
                return;
            }
            WriteObject(item);
        }, ProcessScript,
            SqlInstance, SqlCredential, Database, ExcludeDatabase, EnableException.ToBool(),
            NestedCommand.BoundCommonParameter(this, "Verbose"), NestedCommand.BoundCommonParameter(this, "Debug"));
    }
    // PS: the process block VERBATIM. Edit: -FunctionName Get-DbaDbOrphanUser on the two Stop-Function and
    // five Write-Message calls. Database/ExcludeDatabase are truthiness checks (no Test-Bound).
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $ExcludeDatabase, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [object[]]$Database, [object[]]$ExcludeDatabase, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        foreach ($instance in $SqlInstance) {
            try {
                $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
            } catch {
                Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaDbOrphanUser
            }
            $DatabaseCollection = @($server.Databases | Where-Object IsAccessible)

            if ($Database) {
                $DatabaseCollection = @($DatabaseCollection | Where-Object Name -In $Database)
            }
            if ($ExcludeDatabase) {
                $DatabaseCollection = @($DatabaseCollection | Where-Object Name -NotIn $ExcludeDatabase)
            }

            if ($DatabaseCollection.Count -gt 0) {
                foreach ($db in $DatabaseCollection) {
                    try {
                        Write-Message -Level Verbose -Message "Validating users on database '$db'." -FunctionName Get-DbaDbOrphanUser -ModuleName "dbatools"
                        $UsersToWork = @()
                        # ContainmentType is SQL Server 2012+ only, so keep the legacy path for older versions.
                        $isContainedDatabase = (
                            $server.versionMajor -gt 10 -and
                            $null -ne $db.ContainmentType -and
                            $db.ContainmentType -ne [Microsoft.SqlServer.Management.Smo.ContainmentType]::None
                        )
                        if (-not $isContainedDatabase) {
                            $UsersToWork += $db.Users | Where-Object { ($_.Login -eq "") -and ($_.ID -gt 4) -and ($_.Sid.Length -eq 16) -and ($_.LoginType -in "SqlLogin", "Certificate") }
                        } else {
                            Write-Message -Level Verbose -Message "Skipping SQL login orphan check on contained database '$db' (ContainmentType: $($db.ContainmentType))." -FunctionName Get-DbaDbOrphanUser -ModuleName "dbatools"
                        }
                        $UsersToWork += $db.Users | Where-Object { ($_.Login -notin $server.Logins.Name) -and ($_.ID -gt 4) -and ($_.Sid.Length -gt 16 -and $_.LoginType -in "WindowsUser", "WindowsGroup") }
                        if ($UsersToWork.Count -gt 0) {
                            Write-Message -Level Verbose -Message "Orphan users found" -FunctionName Get-DbaDbOrphanUser -ModuleName "dbatools"
                            foreach ($user in $UsersToWork) {
                                [PSCustomObject]@{
                                    ComputerName = $server.ComputerName
                                    InstanceName = $server.ServiceName
                                    SqlInstance  = $server.DomainInstanceName
                                    DatabaseName = $db.Name
                                    User         = $user.Name
                                    SmoUser      = $user
                                } | Select-DefaultView -Property ComputerName, InstanceName, SqlInstance, DatabaseName, User
                            }
                        } else {
                            Write-Message -Level Verbose -Message "No orphan users found on database '$db'." -FunctionName Get-DbaDbOrphanUser -ModuleName "dbatools"
                        }
                        #reset collection
                        $UsersToWork = $null
                    } catch {
                        Stop-Function -Message $_ -Continue -FunctionName Get-DbaDbOrphanUser
                    }
                }
            } else {
                Write-Message -Level VeryVerbose -Message "There are no databases to analyse." -FunctionName Get-DbaDbOrphanUser -ModuleName "dbatools"
            }
        }
} $SqlInstance $SqlCredential $Database $ExcludeDatabase $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
