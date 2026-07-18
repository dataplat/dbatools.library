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
[Cmdlet(VerbsCommon.Get, "DbaPermission")]
[OutputType(typeof(System.Data.DataRow))]
public sealed class GetDbaPermissionCommand : DbaBaseCmdlet
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

    /// <summary>Includes server-level permissions in the results.</summary>
    [Parameter]
    public SwitchParameter IncludeServerLevel { get; set; }

    /// <summary>Excludes permissions on system objects.</summary>
    [Parameter]
    public SwitchParameter ExcludeSystemObjects { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    /// <summary>The T-SQL templates the begin block builds, captured ONCE before any record.</summary>
    private string? _servPermSql;
    private string? _dbPermSql;

    /// <summary>
    /// Builds the begin block''s T-SQL templates once, in module scope, before any pipeline record.
    /// </summary>
    /// <remarks>
    /// When -ExcludeSystemObjects is absent the script leaves $ExcludeSystemObjectssql UNASSIGNED, so the
    /// interpolation that builds the server template scope-walks to whatever the caller has in scope. The
    /// script resolves that ONCE in its begin block; rebuilding the templates per record would instead
    /// re-resolve it per record and pick up any mutation the caller made between records.
    /// </remarks>
    protected override void BeginProcessing()
    {
        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BeginScript,
            ExcludeSystemObjects.ToBool(), BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
            }
            else if (item is not null && LanguagePrimitives.IsTrue(
                item.Properties["__GetDbaPermissionBeginComplete"]?.Value))
            {
                _servPermSql = item.Properties["ServPermsql"]?.Value as string;
                _dbPermSql = item.Properties["DBPermsql"]?.Value as string;
            }
            else if (item is not null)
            {
                WriteObject(item);
            }
        }
    }

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
            SqlInstance, SqlCredential, Database, ExcludeDatabase, IncludeServerLevel.ToBool(),
            _servPermSql, _dbPermSql, EnableException.ToBool(),
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

    // PS: the begin block VERBATIM, run once in module scope; it hands the two templates the process
    // body consumes back through a sentinel.
    private const string BeginScript = """
param($ExcludeSystemObjects, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param($ExcludeSystemObjects, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        if ($ExcludeSystemObjects) {
            $ExcludeSystemObjectssql = "WHERE major_id > 0 "
        }

        $ServPermsql = "SELECT SERVERPROPERTY('MachineName') AS ComputerName,
                       ISNULL(SERVERPROPERTY('InstanceName'), 'MSSQLSERVER') AS InstanceName,
                       SERVERPROPERTY('ServerName') AS SqlInstance
                        , [Database] = ''
                        , [PermState] = state_desc
                        , [PermissionName] = permission_name
                        , [SecurableType] = COALESCE(o.type_desc,sp.class_desc)
                        , [Securable] = CASE
                            WHEN class = 100 THEN @@SERVERNAME
                            WHEN class = 101 THEN SUSER_NAME(major_id)
                            WHEN class = 105 THEN (SELECT TOP (1) name FROM sys.endpoints WHERE endpoint_id = major_id)
                            WHEN class = 108 THEN (SELECT TOP (1) ag.name FROM sys.availability_replicas ar JOIN sys.availability_groups ag ON ar.group_id = ag.group_id WHERE ar.replica_metadata_id = major_id)
                            ELSE CONVERT(NVARCHAR, major_id)
                            END
                        , [Grantee] = SUSER_NAME(grantee_principal_id)
                        , [GranteeType] = pr.type_desc
                        , [revokeStatement] = 'REVOKE ' + permission_name + ' ' + COALESCE(OBJECT_NAME(major_id),'') + ' FROM [' + SUSER_NAME(grantee_principal_id) + ']'
                        , [grantStatement] = 'GRANT ' + permission_name + ' ' + COALESCE(OBJECT_NAME(major_id),'') + ' TO [' + SUSER_NAME(grantee_principal_id) + ']'
                            + CASE WHEN sp.state_desc = 'GRANT_WITH_GRANT_OPTION' THEN ' WITH GRANT OPTION' ELSE '' END
                    FROM sys.server_permissions sp
                        JOIN sys.server_principals pr ON pr.principal_id = sp.grantee_principal_id
                        LEFT OUTER JOIN sys.all_objects o ON o.object_id = sp.major_id

                    $ExcludeSystemObjectssql

                    UNION ALL
                    SELECT    SERVERPROPERTY('MachineName') AS ComputerName
                            , ISNULL(SERVERPROPERTY('InstanceName'), 'MSSQLSERVER') AS InstanceName
                            , SERVERPROPERTY('ServerName') AS SqlInstance
                            , [database] = ''
                            , [PermState] = 'GRANT'
                            , [PermissionName] = pb.[permission_name]
                            , [SecurableType] = pb.class_desc
                            , [Securable] = @@SERVERNAME
                            , [Grantee] = spr.name
                            , [GranteeType] = spr.type_desc
                            , [revokestatement] = ''
                            , [grantstatement] = ''
                    FROM sys.server_principals AS spr
                    INNER JOIN sys.fn_builtin_permissions('SERVER') AS pb ON
                        spr.[name]='bulkadmin' AND pb.[permission_name]='ADMINISTER BULK OPERATIONS'
                        OR
                        spr.[name]='dbcreator' AND pb.[permission_name]='CREATE ANY DATABASE'
                        OR
                        spr.[name]='diskadmin' AND pb.[permission_name]='ALTER RESOURCES'
                        OR
                        spr.[name]='processadmin' AND pb.[permission_name] IN ('ALTER ANY CONNECTION', 'ALTER SERVER STATE')
                        OR
                        spr.[name]='sysadmin' AND pb.[permission_name]='CONTROL SERVER'
                        OR
                        spr.[name]='securityadmin' AND pb.[permission_name]='ALTER ANY LOGIN'
                        OR
                        spr.[name]='serveradmin'  AND pb.[permission_name] IN ('ALTER ANY ENDPOINT', 'ALTER RESOURCES','ALTER SERVER STATE', 'ALTER SETTINGS','SHUTDOWN', 'VIEW SERVER STATE')
                        OR
                        spr.[name]='setupadmin' AND pb.[permission_name]='ALTER ANY LINKED SERVER'
                    WHERE spr.[type]='R'
                    ;"

        $DBPermsql = "SELECT SERVERPROPERTY('MachineName') AS ComputerName,
                    ISNULL(SERVERPROPERTY('InstanceName'), 'MSSQLSERVER') AS InstanceName,
                    SERVERPROPERTY('ServerName') AS SqlInstance
                    , [Database] = DB_NAME()
                    , [PermState] = state_desc
                    , [PermissionName] = permission_name
                    , [SecurableType] = COALESCE(o.type_desc,dp.class_desc)
                    , [Securable] = CASE    WHEN class = 0 THEN DB_NAME()
                                            WHEN class = 1 THEN ISNULL(s.name + '.','')+OBJECT_NAME(major_id)
                                            WHEN class = 3 THEN SCHEMA_NAME(major_id)
                                            WHEN class = 6 THEN SCHEMA_NAME(t.schema_id)+'.' + t.name
                                            END
                    , [Grantee] = USER_NAME(grantee_principal_id)
                    , [GranteeType] = pr.type_desc
                    , [RevokeStatement] = CASE WHEN class = 3 THEN 'REVOKE ' + permission_name + ' ON Schema::[' + ISNULL(SCHEMA_NAME(dp.major_id) COLLATE DATABASE_DEFAULT,'') + '] FROM [' + USER_NAME(grantee_principal_id) +']'
                                            ELSE 'REVOKE ' + permission_name + ' ON [' + ISNULL(SCHEMA_NAME(o.schema_id) COLLATE DATABASE_DEFAULT+'].[','')+OBJECT_NAME(major_id)+ '] FROM [' + USER_NAME(grantee_principal_id) +']'
                                            END
                    , [GrantStatement] = CASE WHEN class = 3 THEN state_desc + ' ' + permission_name + ' ON Schema::[' + ISNULL(SCHEMA_NAME(dp.major_id) COLLATE DATABASE_DEFAULT,'') + '] TO [' + USER_NAME(grantee_principal_id) + ']'
                                            ELSE state_desc + ' ' + permission_name + ' ON [' + ISNULL(SCHEMA_NAME(o.schema_id) COLLATE DATABASE_DEFAULT+'].[','')+OBJECT_NAME(major_id)+ '] TO [' + USER_NAME(grantee_principal_id) + ']'
                                            END
                        + CASE WHEN dp.state_desc = 'GRANT_WITH_GRANT_OPTION' THEN ' WITH GRANT OPTION' ELSE '' END
                    FROM sys.database_permissions dp
                    JOIN sys.database_principals pr ON pr.principal_id = dp.grantee_principal_id
                    LEFT OUTER JOIN sys.all_objects o ON (o.object_id = dp.major_id AND dp.class NOT IN (0, 3))
                    LEFT OUTER JOIN sys.schemas s ON s.schema_id = o.schema_id
                    LEFT OUTER JOIN sys.types t on t.user_type_id = dp.major_id

                $ExcludeSystemObjectssql

                UNION ALL
                SELECT    SERVERPROPERTY('MachineName') AS ComputerName
                        , ISNULL(SERVERPROPERTY('InstanceName'), 'MSSQLSERVER') AS InstanceName
                        , SERVERPROPERTY('ServerName') AS SqlInstance
                        , [database] = DB_NAME()
                        , [PermState] = ''
                        , [PermissionName] = p.[permission_name]
                        , [SecurableType] = p.class_desc
                        , [Securable] = DB_NAME()
                        , [Grantee] = dp.name
                        , [GranteeType] = dp.type_desc
                        , [revokestatement] = ''
                        , [grantstatement] = ''
                FROM sys.database_principals AS dp
                INNER JOIN sys.fn_builtin_permissions('DATABASE') AS p ON
                    dp.[name]='db_accessadmin' AND p.[permission_name] IN ('ALTER ANY USER', 'CREATE SCHEMA')
                    OR
                    dp.[name]='db_backupoperator' AND p.[permission_name] IN ('BACKUP DATABASE', 'BACKUP LOG', 'CHECKPOINT')
                    OR
                    dp.[name] IN ('db_datareader', 'db_denydatareader') AND p.[permission_name]='SELECT'
                    OR
                    dp.[name] IN ('db_datawriter', 'db_denydatawriter') AND p.[permission_name] IN ('INSERT', 'DELETE', 'UPDATE')
                    OR
                    dp.[name]='db_ddladmin' AND
                    p.[permission_name] IN ('ALTER ANY ASSEMBLY', 'ALTER ANY ASYMMETRIC KEY',
                                            'ALTER ANY CERTIFICATE', 'ALTER ANY CONTRACT',
                                            'ALTER ANY DATABASE DDL TRIGGER', 'ALTER ANY DATABASE EVENT',
                                            'NOTIFICATION', 'ALTER ANY DATASPACE', 'ALTER ANY FULLTEXT CATALOG',
                                            'ALTER ANY MESSAGE TYPE', 'ALTER ANY REMOTE SERVICE BINDING',
                                            'ALTER ANY ROUTE', 'ALTER ANY SCHEMA', 'ALTER ANY SERVICE',
                                            'ALTER ANY SYMMETRIC KEY', 'CHECKPOINT', 'CREATE AGGREGATE',
                                            'CREATE DEFAULT', 'CREATE FUNCTION', 'CREATE PROCEDURE',
                                            'CREATE QUEUE', 'CREATE RULE', 'CREATE SYNONYM', 'CREATE TABLE',
                                            'CREATE TYPE', 'CREATE VIEW', 'CREATE XML SCHEMA COLLECTION',
                                            'REFERENCES')
                    OR
                    dp.[name]='db_owner' AND p.[permission_name]='CONTROL'
                    OR
                    dp.[name]='db_securityadmin' AND p.[permission_name] IN ('ALTER ANY APPLICATION ROLE', 'ALTER ANY ROLE', 'CREATE SCHEMA', 'VIEW DEFINITION')

                WHERE dp.[type]='R'
                    AND dp.is_fixed_role=1
                UNION ALL -- include the dbo user
                SELECT
                    [ComputerName]        = SERVERPROPERTY('MachineName')
                ,    [InstanceName]        = ISNULL(SERVERPROPERTY('InstanceName'), 'MSSQLSERVER')
                ,    [SqlInstance]        = SERVERPROPERTY('ServerName')
                ,    [database]            = DB_NAME()
                ,    [PermState]            = ''
                ,    [PermissionName]    = 'CONTROL'
                ,    [SecurableType]        = 'DATABASE'
                ,    [Securable]            = DB_NAME()
                ,    [Grantee]            = SUSER_SNAME(owner_sid)
                ,    [GranteeType]        = 'DATABASE OWNER (dbo user)'
                ,    [revokestatement]    = ''
                ,    [grantstatement]    = ''
                FROM
                    sys.databases
                WHERE
                    name = DB_NAME()
                UNION ALL -- include the users with the db_owner role
                SELECT
                    [ComputerName]        = SERVERPROPERTY('MachineName')
                ,    [InstanceName]        = ISNULL(SERVERPROPERTY('InstanceName'), 'MSSQLSERVER')
                ,    [SqlInstance]        = SERVERPROPERTY('ServerName')
                ,    [database]            = DB_NAME()
                ,    [PermState]            = ''
                ,    [PermissionName]    = 'CONTROL'
                ,    [SecurableType]        = 'DATABASE'
                ,    [Securable]            = DB_NAME()
                ,    [Grantee]            = databaseUser.name
                ,    [GranteeType]        = 'DATABASE OWNER (db_owner role)'
                ,    [revokestatement]    = ''
                ,    [grantstatement]    = ''
                FROM
                (
                    SELECT
                        member_principal_id
                    FROM
                        sys.database_role_members AS roleMembers
                    INNER JOIN
                        sys.database_principals AS roleFilter
                            ON roleMembers.role_principal_id = roleFilter.principal_id
                            AND roleFilter.name = 'db_owner'
                ) dbOwner
                INNER JOIN
                    sys.database_principals AS databaseUser
                        ON dbOwner.member_principal_id = databaseUser.principal_id
                WHERE
                    databaseUser.name <> 'dbo'
                UNION ALL -- include the schema owners
                SELECT
                    [ComputerName]        = SERVERPROPERTY('MachineName')
                ,    [InstanceName]        = ISNULL(SERVERPROPERTY('InstanceName'), 'MSSQLSERVER')
                ,    [SqlInstance]        = SERVERPROPERTY('ServerName')
                ,    [database]            = DB_NAME()
                ,    [PermState]            = ''
                ,    [PermissionName]    = 'CONTROL'
                ,    [SecurableType]        = 'SCHEMA'
                ,    [Securable]            = name
                ,    [Grantee]            = USER_NAME(principal_id)
                ,    [GranteeType]        = 'SCHEMA OWNER'
                ,    [revokestatement]    = ''
                ,    [grantstatement]    = ''
                FROM
                    sys.schemas
                WHERE
                    name NOT IN (SELECT name FROM sys.database_principals WHERE type = 'R')
                AND name NOT IN ('dbo', 'guest', 'INFORMATION_SCHEMA', 'sys')
                ;"

    [pscustomobject]@{ __GetDbaPermissionBeginComplete = $true; ServPermsql = $ServPermsql; DBPermsql = $DBPermsql }
} $ExcludeSystemObjects $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1

""";

    // PS: the process body VERBATIM, with the templates supplied by the begin hop. Substitutions only:
    // -FunctionName on the two DIRECT Stop-Function calls; -FunctionName + -ModuleName "dbatools" on the
    // five DIRECT Write-Message calls. Switches and EnableException are received untyped.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $ExcludeDatabase, $IncludeServerLevel, $ServPermsql, $DBPermsql, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [System.Management.Automation.PSCredential]$SqlCredential, [object[]]$Database, [object[]]$ExcludeDatabase, $IncludeServerLevel, [string]$ServPermsql, [string]$DBPermsql, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }
        foreach ($instance in $SqlInstance) {
            try {
                $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential -MinimumVersion 9
            } catch {
                Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaPermission
            }

            if ($IncludeServerLevel) {
                if ($server.IsAzure) {
                    Write-Message -Level Warning -Message "Server-level permissions are not supported on Azure SQL Database. Skipping -IncludeServerLevel." -FunctionName Get-DbaPermission -ModuleName "dbatools"
                } else {
                    Write-Message -Level Debug -Message "T-SQL: $ServPermsql" -FunctionName Get-DbaPermission -ModuleName "dbatools"
                    $server.Query($ServPermsql)
                }
            }

            $currentDBPermsql = if ($server.IsAzure) {
                $DBPermsql.Replace("SUSER_SNAME(owner_sid)", "(SELECT TOP 1 name FROM sys.database_principals WHERE sid = owner_sid)")
            } else {
                $DBPermsql
            }

            $dbs = $server.Databases

            if ($Database) {
                $dbs = $dbs | Where-Object Name -In $Database
            }

            if ($ExcludeDatabase) {
                $dbs = $dbs | Where-Object Name -NotIn $ExcludeDatabase
            }

            foreach ($db in $dbs) {
                Write-Message -Level Verbose -Message "Processing $db on $instance." -FunctionName Get-DbaPermission -ModuleName "dbatools"

                if ($db.IsAccessible -eq $false) {
                    Write-Message -Level Warning -Message "The database $db is not accessible. Skipping database." -FunctionName Get-DbaPermission -ModuleName "dbatools"
                    Continue
                }

                Write-Message -Level Debug -Message "T-SQL: $currentDBPermsql" -FunctionName Get-DbaPermission -ModuleName "dbatools"
                try {
                    $db.ExecuteWithResults($currentDBPermsql).Tables.Rows
                } catch {
                    Stop-Function -Message "Failure executing against $($db.Name) on $instance" -ErrorRecord $_ -Continue -FunctionName Get-DbaPermission
                }
            }
        }
} $SqlInstance $SqlCredential $Database $ExcludeDatabase $IncludeServerLevel $ServPermsql $DBPermsql $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1

""";
}

