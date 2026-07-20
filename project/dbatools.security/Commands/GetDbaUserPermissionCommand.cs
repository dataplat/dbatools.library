#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Gets the server-level and database-level permissions defined on a SQL Server instance.
/// </summary>
/// <remarks>
/// <para>
/// The workflow remains a module-scoped PowerShell compatibility hop so that the permission queries, the
/// Azure variants, the database filtering, and dbatools stream and error handling stay
/// observable-identical to the script implementation.
/// </para>
/// <para>
/// The script's begin block only builds constant T-SQL templates - the one conditional piece, the
/// system-object filter, is derived from a switch that cannot change between records - and the process
/// block never mutates them or carries anything else across records. Those assignments therefore ride at
/// the top of the per-record hop: recomputing identical templates per record is behaviourally
/// indistinguishable from the script's begin-once, and it avoids a sentinel that would carry no state.
/// </para>
/// <para>
/// The command streams through InvokeScopedStreaming rather than buffering, because SqlInstance is an
/// array and the body emits per database: one record can emit rows for an early database and then hit a
/// Stop-Function on a later one, which terminates under -EnableException, and a buffered call would
/// discard the rows already produced (DEF-001). The source gates every filter on plain variable
/// truthiness rather than Test-Bound, so no boundness flags are carried, and it has no ShouldProcess.
/// The only body edits are message attribution: -FunctionName on the two direct Stop-Function calls and
/// -FunctionName plus -ModuleName "dbatools" on the five direct Write-Message calls.
/// </para>
/// </remarks>
[Cmdlet(VerbsCommon.Get, "DbaUserPermission")]
[OutputType(typeof(PSCustomObject))]
public sealed class GetDbaUserPermissionCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The databases to report permissions for.</summary>
    [Parameter(Position = 2)]
    public object[]? Database { get; set; }

    /// <summary>Databases to exclude from the report.</summary>
    [Parameter(Position = 3)]
    public object[]? ExcludeDatabase { get; set; }

    /// <summary>Excludes the system databases from the report.</summary>
    [Parameter]
    public SwitchParameter ExcludeSystemDatabase { get; set; }

    /// <summary>Includes the public and guest principals.</summary>
    [Parameter]
    public SwitchParameter IncludePublicGuest { get; set; }

    /// <summary>Includes permissions on system objects.</summary>
    [Parameter]
    public SwitchParameter IncludeSystemObjects { get; set; }

    /// <summary>Excludes securable-level detail from the report.</summary>
    [Parameter]
    public SwitchParameter ExcludeSecurables { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    /// <summary>Returns the permissions for the instances bound to the current record.</summary>
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
            SqlInstance, SqlCredential, Database, ExcludeDatabase, ExcludeSystemDatabase.ToBool(),
            IncludePublicGuest.ToBool(), IncludeSystemObjects.ToBool(), ExcludeSecurables.ToBool(), EnableException.ToBool(),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug"));
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

    // PS: the begin block's T-SQL templates, then the process body VERBATIM. Substitutions only:
    // -FunctionName on the two DIRECT Stop-Function calls; -FunctionName + -ModuleName "dbatools" on the
    // five DIRECT Write-Message calls. Switches and EnableException are received untyped.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $ExcludeDatabase, $ExcludeSystemDatabase, $IncludePublicGuest, $IncludeSystemObjects, $ExcludeSecurables, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [System.Management.Automation.PSCredential]$SqlCredential, [object[]]$Database, [object[]]$ExcludeDatabase, $ExcludeSystemDatabase, $IncludePublicGuest, $IncludeSystemObjects, $ExcludeSecurables, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        $removeStigSQL = "       BEGIN TRY DROP FUNCTION STIG.server_effective_permissions END TRY BEGIN CATCH END CATCH;
                       GO
                       BEGIN TRY DROP VIEW STIG.server_permissions END TRY BEGIN CATCH END CATCH;
                       GO
                       BEGIN TRY DROP FUNCTION STIG.members_of_server_role END TRY BEGIN CATCH END CATCH;
                       GO
                       BEGIN TRY DROP FUNCTION STIG.server_roles_of END TRY BEGIN CATCH END CATCH;
                       GO
                       BEGIN TRY DROP VIEW STIG.server_role_members END TRY BEGIN CATCH END CATCH;
                       GO
                       BEGIN TRY DROP FUNCTION STIG.database_effective_permissions END TRY BEGIN CATCH END CATCH;
                       GO
                       BEGIN TRY DROP VIEW STIG.database_permissions END TRY BEGIN CATCH END CATCH;
                       GO
                       BEGIN TRY DROP FUNCTION STIG.members_of_db_role END TRY BEGIN CATCH END CATCH;
                       GO
                       BEGIN TRY DROP FUNCTION STIG.database_roles_of END TRY BEGIN CATCH END CATCH;
                       GO
                       BEGIN TRY DROP VIEW STIG.database_role_members END TRY BEGIN CATCH END CATCH;
                       GO
                       BEGIN TRY DROP SCHEMA STIG END TRY BEGIN CATCH END CATCH;
                       GO"


        $serverSQL = "SELECT  'SERVER LOGINS' AS Type ,
                                    sl.name AS Member ,
                                    ISNULL(srm.Role, 'None') AS [Role/Securable/Class] ,
                                    ' ' AS [Schema/Owner] ,
                                    ' ' AS [Securable] ,
                                    ' ' AS [Grantee Type] ,
                                    ' ' AS [Grantee] ,
                                    ' ' AS [Permission] ,
                                    ' ' AS [State] ,
                                    ' ' AS [Grantor] ,
                                    ' ' AS [Grantor Type] ,
                                    ' ' AS [Source View]
                            FROM    master.sys.syslogins sl
                                    LEFT JOIN tempdb.[STIG].[server_role_members] srm ON sl.name = srm.Member
                            WHERE   sl.name NOT LIKE 'NT %'
                                    AND sl.name NOT LIKE '##%'"

        $dbSQL = "SELECT  'DB ROLE MEMBERS' AS Type ,
                                Member ,
                                ISNULL(Role, 'None') AS [Role/Securable/Class],
                                ' ' AS [Schema/Owner] ,
                                ' ' AS [Securable] ,
                                ' ' AS [Grantee Type] ,
                                ' ' AS [Grantee] ,
                                ' ' AS [Permission] ,
                                ' ' AS [State] ,
                                ' ' AS [Grantor] ,
                                ' ' AS [Grantor Type] ,
                                ' ' AS [Source View]
                        FROM    tempdb.[STIG].[database_role_members]"

        # append unions to get securables if not excluded:
        if (-not $ExcludeSecurables) {

            $serverSQL = $serverSQL + "
                            UNION
                            SELECT  'SERVER SECURABLES' AS Type ,
                                    sl.name ,
                                    sp.[Securable Class] COLLATE SQL_Latin1_General_CP1_CI_AS ,
                                    ' ' ,
                                    sp.[Securable] ,
                                    sp.[Grantee Type] COLLATE SQL_Latin1_General_CP1_CI_AS ,
                                    sp.Grantee ,
                                    sp.Permission COLLATE SQL_Latin1_General_CP1_CI_AS ,
                                    sp.State COLLATE SQL_Latin1_General_CP1_CI_AS ,
                                    sp.Grantor ,
                                    sp.[Grantor Type] COLLATE SQL_Latin1_General_CP1_CI_AS ,
                                    sp.[Source View]
                            FROM    master.sys.syslogins sl
                                    LEFT JOIN tempdb.[STIG].[server_permissions] sp ON sl.name = sp.Grantee
                            WHERE   sl.name NOT LIKE 'NT %'
                                    AND sl.name NOT LIKE '##%';"

            $dbSQL = $dbSQL + "
                        UNION
                        SELECT DISTINCT
                                'DB SECURABLES' AS Type ,
                                ISNULL(drm.Member, 'None') AS [Role/Securable/Class] ,
                                dp.[Securable Type or Class] COLLATE SQL_Latin1_General_CP1_CI_AS ,
                                dp.[Schema/Owner] ,
                                dp.Securable ,
                                dp.[Grantee Type] COLLATE SQL_Latin1_General_CP1_CI_AS ,
                                dp.Grantee ,
                                dp.Permission COLLATE SQL_Latin1_General_CP1_CI_AS ,
                                dp.State COLLATE SQL_Latin1_General_CP1_CI_AS ,
                                dp.Grantor ,
                                dp.[Grantor Type] COLLATE SQL_Latin1_General_CP1_CI_AS ,
                                dp.[Source View]
                        FROM    tempdb.[STIG].[database_role_members] drm
                                FULL JOIN tempdb.[STIG].[database_permissions] dp ON ( drm.Member = dp.Grantee
                                                                                      OR drm.Role = dp.Grantee
                                                                                     )
                        WHERE    dp.Grantor IS NOT NULL
                                AND dp.Grantee NOT IN ('public', 'guest')
                                AND [Schema/Owner] <> 'sys'"
        }

        if ($IncludePublicGuest) { $dbSQL = $dbSQL.Replace("AND dp.Grantee NOT IN ('public', 'guest')", "") }
        if ($IncludeSystemObjects) { $dbSQL = $dbSQL.Replace("AND [Schema/Owner] <> 'sys'", "") }


        foreach ($instance in $SqlInstance) {
            try {
                $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential -MinimumVersion 10 -AzureUnsupported
            } catch {
                Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaUserPermission
            }

            $dbs = $server.Databases
            $tempdb = $server.Databases['tempdb']

            if ($Database) {
                $dbs = $dbs | Where-Object { $Database -contains $_.Name }
            }

            if ($ExcludeDatabase) {
                $dbs = $dbs | Where-Object Name -NotIn $ExcludeDatabase
            }

            if ($ExcludeSystemDatabase) {
                $dbs = $dbs | Where-Object IsSystemObject -eq $false
            }

            Write-Message -Level Verbose -Message "Reading stig.sql" -FunctionName Get-DbaUserPermission -ModuleName "dbatools"
            $sqlFile = Join-DbaPath -Path $script:PSModuleRoot -ChildPath "bin", "stig.sql"
            $sql = [System.IO.File]::ReadAllText("$sqlFile")

            try {
                Write-Message -Level Verbose -Message "Removing STIG schema if it still exists from previous run" -FunctionName Get-DbaUserPermission -ModuleName "dbatools"
                # We use Invoke-DbaQuery (here and later in the code) because using ExecuteNonQuery with long batches causes problems on AppVeyor.
                $null = Invoke-DbaQuery -SqlInstance $server -Database tempdb -Query $removeStigSQL -EnableException
                Write-Message -Level Verbose -Message "Creating STIG schema customized for master database" -FunctionName Get-DbaUserPermission -ModuleName "dbatools"
                $createStigSQL = $sql.Replace("<TARGETDB>", 'master')
                $null = Invoke-DbaQuery -SqlInstance $server -Database tempdb -Query $createStigSQL -EnableException
                Write-Message -Level Verbose -Message "Building data table for server objects" -FunctionName Get-DbaUserPermission -ModuleName "dbatools"
                $serverDT = Invoke-DbaQuery -SqlInstance $server -Database tempdb -Query $serverSQL -EnableException
                foreach ($row in $serverDT) {
                    [PSCustomObject]@{
                        ComputerName       = $server.ComputerName
                        InstanceName       = $server.ServiceName
                        SqlInstance        = $server.DomainInstanceName
                        Object             = 'SERVER'
                        Type               = $row.Type
                        Member             = $row.Member
                        RoleSecurableClass = $row.'Role/Securable/Class'
                        SchemaOwner        = $row.'Schema/Owner'
                        Securable          = $row.Securable
                        GranteeType        = $row.'Grantee Type'
                        Grantee            = $row.Grantee
                        Permission         = $row.Permission
                        State              = $row.State
                        Grantor            = $row.Grantor
                        GrantorType        = $row.'Grantor Type'
                        SourceView         = $row.'Source View'
                    }
                }
            } catch {
                Stop-Function -Message "Failed to create or use STIG schema on $instance" -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaUserPermission
            }

            foreach ($db in $dbs) {
                Write-Message -Level Verbose -Message "Processing $db on $instance" -FunctionName Get-DbaUserPermission -ModuleName "dbatools"

                if ($db.IsAccessible -eq $false) {
                    Write-Message -Level Warning -Message "The database $db on $instance is not accessible. Skipping." -FunctionName Get-DbaUserPermission -ModuleName "dbatools"
                    continue
                }

                try {
                    Write-Message -Level Verbose -Message "Removing STIG schema if it still exists from previous run" -FunctionName Get-DbaUserPermission -ModuleName "dbatools"
                    $null = Invoke-DbaQuery -SqlInstance $server -Database tempdb -Query $removeStigSQL -EnableException
                    Write-Message -Level Verbose -Message "Creating STIG schema customized for current database" -FunctionName Get-DbaUserPermission -ModuleName "dbatools"
                    $createStigSQL = $sql.Replace("<TARGETDB>", $db.Name)
                    Write-Message -Level Verbose -Message "Length of createStigSQL: $($createStigSQL.Length)" -FunctionName Get-DbaUserPermission -ModuleName "dbatools"
                    $null = Invoke-DbaQuery -SqlInstance $server -Database tempdb -Query $createStigSQL -EnableException
                    Write-Message -Level Verbose -Message "Building data table for database objects" -FunctionName Get-DbaUserPermission -ModuleName "dbatools"
                    $dbDT = Invoke-DbaQuery -SqlInstance $server -Database $db.Name -Query $dbSQL -EnableException
                    foreach ($row in $dbDT) {
                        [PSCustomObject]@{
                            ComputerName       = $server.ComputerName
                            InstanceName       = $server.ServiceName
                            SqlInstance        = $server.DomainInstanceName
                            Object             = $db.Name
                            Type               = $row.Type
                            Member             = $row.Member
                            RoleSecurableClass = $row.'Role/Securable/Class'
                            SchemaOwner        = $row.'Schema/Owner'
                            Securable          = $row.Securable
                            GranteeType        = $row.'Grantee Type'
                            Grantee            = $row.Grantee
                            Permission         = $row.Permission
                            State              = $row.State
                            Grantor            = $row.Grantor
                            GrantorType        = $row.'Grantor Type'
                            SourceView         = $row.'Source View'
                        }
                    }
                } catch {
                    Stop-Function -Message "Failed to create or use STIG schema for database $db on $instance" -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaUserPermission
                }
            }

            try {
                Write-Message -Level Verbose -Message "Removing STIG schema from tempdb" -FunctionName Get-DbaUserPermission -ModuleName "dbatools"
                $null = Invoke-DbaQuery -SqlInstance $server -Database tempdb -Query $removeStigSQL -EnableException
            } catch {
                Stop-Function -Message "Failed to remove STIG schema from tempdb on $instance" -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaUserPermission
            }
        }
} $SqlInstance $SqlCredential $Database $ExcludeDatabase $ExcludeSystemDatabase $IncludePublicGuest $IncludeSystemObjects $ExcludeSecurables $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1

""";
}

