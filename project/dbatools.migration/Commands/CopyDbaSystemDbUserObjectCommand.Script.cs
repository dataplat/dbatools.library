#nullable enable

using System.Management.Automation;

namespace Dataplat.Dbatools.Commands;

// The hop lives in its own partial so neither half runs past the 400-line file rule, and the
// C# call site is kept HERE with the body rather than with the parameter surface: the DEF-005
// arg-order detector name-checks the marshal against the body's param() only when it can see
// both in one file, and splitting them apart would silently drop that half of the check.
// The body itself is the source function's process block verbatim apart from the sanctioned
// mechanical edits, so it is kept whole and unreflowed - a diff against the .ps1 is how this
// port is audited.
public sealed partial class CopyDbaSystemDbUserObjectCommand
{
    // Begin and process fold into one hop and carry no state between records: nothing takes
    // pipeline input, so ProcessRecord runs exactly once. Test-FunctionInterrupt still earns its
    // line inside the script, because the sysadmin check has to stop the destination loop that
    // follows it in the same invocation.
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
            Source, SourceSqlCredential, Destination, DestinationSqlCredential, Force.ToBool(),
            Classic.ToBool(), EnableException.ToBool(), this,
            NestedCommand.BoundCommonParameter(this, "Verbose"),
            NestedCommand.BoundCommonParameter(this, "Debug"));
    }

    private const string BodyScript = """
param($Source, $SourceSqlCredential, $Destination, $DestinationSqlCredential, $Force, $Classic, $EnableException, $__realCmdlet, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    # $Force and $Classic are deliberately untyped: PowerShell excludes [switch] parameters from
    # positional binding, so one typed flag would shift every argument after it. They arrive as
    # real booleans, which the -not tests below read the way a switch reads.
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter]$Source, $SourceSqlCredential, [Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$Destination, $DestinationSqlCredential, $Force, $Classic, $EnableException, $__realCmdlet)

    if ($Force) { $ConfirmPreference = 'none' }

    function get-sqltypename ($type) {
        switch ($type) {
            "VIEW" { "view" }
            "SQL_TABLE_VALUED_FUNCTION" { "User table valued function" }
            "DEFAULT_CONSTRAINT" { "User default constraint" }
            "SQL_STORED_PROCEDURE" { "User stored procedure" }
            "RULE" { "User rule" }
            "SQL_INLINE_TABLE_VALUED_FUNCTION" { "User inline table valued function" }
            "SQL_TRIGGER" { "User server trigger" }
            "SQL_SCALAR_FUNCTION" { "User scalar function" }
            default { $type }
        }
    }

    try {
        $sourceServer = Connect-DbaInstance -SqlInstance $Source -SqlCredential $SourceSqlCredential
    } catch {
        Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $Source -FunctionName Copy-DbaSystemDbUserObject
        return
    }

    if (!(Test-SqlSa -SqlInstance $sourceServer -SqlCredential $SourceSqlCredential)) {
        Stop-Function -Message "Not a sysadmin on $source. Quitting." -FunctionName Copy-DbaSystemDbUserObject
        return
    }

    if (Test-FunctionInterrupt) { return }
    foreach ($destinstance in $Destination) {
        try {
            $destServer = Connect-DbaInstance -SqlInstance $destinstance -SqlCredential $DestinationSqlCredential
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $destinstance -Continue -FunctionName Copy-DbaSystemDbUserObject
        }

        if (!(Test-SqlSa -SqlInstance $destServer -SqlCredential $DestinationSqlCredential)) {
            Stop-Function -Message "Not a sysadmin on $destinstance" -Continue -FunctionName Copy-DbaSystemDbUserObject
        }

        $systemDbs = "master", "model", "msdb"

        if (-not $Classic) {
            foreach ($systemDb in $systemDbs) {
                $smodb = $sourceServer.databases[$systemDb]
                $destdb = $destserver.databases[$systemDb]

                $tables = $smodb.Tables | Where-Object IsSystemObject -ne $true
                $schemas = $smodb.Schemas | Where-Object IsSystemObject -ne $true

                foreach ($schema in $schemas) {
                    $copyobject = [PSCustomObject]@{
                        SourceServer      = $sourceServer.Name
                        DestinationServer = $destServer.Name
                        Name              = $schema
                        Type              = "User schema in $systemDb"
                        Status            = $null
                        Notes             = $null
                        DateTime          = [Dataplat.Dbatools.Utility.DbaDateTime](Get-Date)
                    }

                    $destschema = $destdb.Schemas | Where-Object Name -eq $schema.Name

                    if ($destschema) {
                        if (-not $force) {
                            if ($__realCmdlet.ShouldProcess($destInstance, "Skipping schema $schema because it already exists on the destination instance. Use -Force to drop and recreate")) {
                                $copyobject.Status = "Skipped"
                                $copyobject.Notes = "Already exists on destination"
                                $copyobject | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                            }
                            continue
                        } else {
                            if ($__realCmdlet.ShouldProcess($destInstance, "Dropping schema $schema in $systemDb")) {
                                try {
                                    Write-Message -Level Verbose -Message "Force specified. Dropping $schema in $destdb on $destinstance" -FunctionName Copy-DbaSystemDbUserObject -ModuleName "dbatools"
                                    $destschema.Drop()
                                } catch {
                                    $copyobject.Status = "Failed"
                                    $copyobject.Notes = $_.Exception.InnerException.InnerException.InnerException.Message
                                    $copyobject | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                                    continue
                                }
                            }
                        }
                    }

                    $transfer = New-Object Microsoft.SqlServer.Management.Smo.Transfer $smodb
                    $null = $transfer.CopyAllObjects = $false
                    $null = $transfer.Options.WithDependencies = $true
                    $null = $transfer.Options.ScriptOwner = $true
                    $null = $transfer.ObjectList.Add($schema)
                    if ($__realCmdlet.ShouldProcess($destInstance, "Attempting to add schema $($schema.Name) to $systemDb")) {
                        try {
                            $sql = $transfer.ScriptTransfer()
                            Write-Message -Level Debug -Message "$sql" -FunctionName Copy-DbaSystemDbUserObject -ModuleName "dbatools"
                            $null = $destServer.Query($sql, $systemDb)
                            $copyobject.Status = "Successful"
                            $copyobject.Notes = "May have also created dependencies"
                            $copyobject | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                        } catch {
                            $copyobject.Status = "Failed"
                            $copyobject.Notes = (Get-ErrorMessage -Record $_)
                            $copyobject | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                            continue
                        }
                    }
                }

                foreach ($table in $tables) {
                    $copyobject = [PSCustomObject]@{
                        SourceServer      = $sourceServer.Name
                        DestinationServer = $destServer.Name
                        Name              = $table
                        Type              = "User table in $systemDb"
                        Status            = $null
                        Notes             = $null
                        DateTime          = [Dataplat.Dbatools.Utility.DbaDateTime](Get-Date)
                    }

                    $desttable = $destdb.Tables.Item($table.Name, $table.Schema)

                    if ($desttable) {
                        if (-not $force) {
                            if ($__realCmdlet.ShouldProcess($destInstance, "Skipping table $desttable because it already exists on the destination instance. Use -Force to drop and recreate")) {
                                $copyobject.Status = "Skipped"
                                $copyobject.Notes = "Already exists on destination"
                                $copyobject | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                            }
                            continue
                        } else {
                            if ($__realCmdlet.ShouldProcess($destInstance, "Dropping table $table in $systemDb")) {
                                try {
                                    Write-Message -Level Verbose -Message "Force specified. Dropping $table in $destdb on $destinstance" -FunctionName Copy-DbaSystemDbUserObject -ModuleName "dbatools"
                                    $desttable.Drop()
                                } catch {
                                    $copyobject.Status = "Failed"
                                    $copyobject.Notes = $_.Exception.InnerException.InnerException.InnerException.Message
                                    $copyobject | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                                    continue
                                }
                            }
                        }
                    }

                    $transfer = New-Object Microsoft.SqlServer.Management.Smo.Transfer $smodb
                    $null = $transfer.CopyAllObjects = $false
                    $null = $transfer.Options.WithDependencies = $true
                    $null = $transfer.Options.Indexes = $true
                    if ($__realCmdlet.ShouldProcess($destInstance, "Attempting to add table $table to $systemDb")) {
                        try {
                            $null = $transfer.ObjectList.Add($table)
                            $sql = $transfer.ScriptTransfer()
                            Write-Message -Level Debug -Message "$sql" -FunctionName Copy-DbaSystemDbUserObject -ModuleName "dbatools"
                            $null = $destServer.Query($sql, $systemDb)
                            $copyobject.Status = "Successful"
                            $copyobject.Notes = "May have also created dependencies"
                            $copyobject | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                        } catch {
                            $copyobject.Status = "Failed"
                            $copyobject.Notes = (Get-ErrorMessage -Record $_)
                            $copyobject | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                            continue
                        }
                    }
                }

                $userobjects = Get-DbaModule -SqlInstance $sourceserver -Database $systemDb -ExcludeSystemObjects | Sort-Object Type
                Write-Message -Level Verbose -Message "Copying from $systemDb" -FunctionName Copy-DbaSystemDbUserObject -ModuleName "dbatools"
                foreach ($userobject in $userobjects) {

                    $name = "[$($userobject.SchemaName)].[$($userobject.Name)]"
                    $db = $userobject.Database
                    $type = get-sqltypename $userobject.Type
                    $sql = $userobject.Definition
                    $schema = $userobject.SchemaName

                    $copyobject = [PSCustomObject]@{
                        SourceServer      = $sourceServer.Name
                        DestinationServer = $destServer.Name
                        Name              = $name
                        Type              = "$type in $systemDb"
                        Status            = $null
                        Notes             = $null
                        DateTime          = [Dataplat.Dbatools.Utility.DbaDateTime](Get-Date)
                    }
                    Write-Message -Level Debug -Message $sql -FunctionName Copy-DbaSystemDbUserObject -ModuleName "dbatools"
                    try {
                        Write-Message -Level Verbose -Message "Searching for $name in $db on $destinstance" -FunctionName Copy-DbaSystemDbUserObject -ModuleName "dbatools"
                        $result = Get-DbaModule -SqlInstance $destServer -ExcludeSystemObjects -Database $db |
                            Where-Object { $PSItem.Name -eq $userobject.Name -and $PSItem.Type -eq $userobject.Type }
                        if ($result) {
                            Write-Message -Level Verbose -Message "Found $name in $db on $destinstance" -FunctionName Copy-DbaSystemDbUserObject -ModuleName "dbatools"
                            if (-not $Force) {
                                if ($__realCmdlet.ShouldProcess($destInstance, "Object $name already exists, skipping")) {
                                    $copyobject.Status = "Skipped"
                                    $copyobject.Notes = "Already exists on destination"
                                    $copyobject | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                                }
                                continue
                            } else {
                                $smobject = switch ($userobject.Type) {
                                    "VIEW" { $smodb.Views.Item($userobject.Name, $userobject.SchemaName) }
                                    "SQL_STORED_PROCEDURE" { $smodb.StoredProcedures.Item($userobject.Name, $userobject.SchemaName) }
                                    "RULE" { $smodb.Rules.Item($userobject.Name, $userobject.SchemaName) }
                                    "SQL_TRIGGER" { $smodb.Triggers.Item($userobject.Name, $userobject.SchemaName) }
                                    "SQL_TABLE_VALUED_FUNCTION" { $smodb.UserDefinedFunctions.Item($name) }
                                    "SQL_INLINE_TABLE_VALUED_FUNCTION" { $smodb.UserDefinedFunctions.Item($name) }
                                    "SQL_SCALAR_FUNCTION" { $smodb.UserDefinedFunctions.Item($name) }
                                }

                                if ($smobject) {
                                    Write-Message -Level Verbose -Message "Force specified. Dropping $smobject on $destdb on $destinstance using SMO" -FunctionName Copy-DbaSystemDbUserObject -ModuleName "dbatools"
                                    $transfer = New-Object Microsoft.SqlServer.Management.Smo.Transfer $smodb
                                    $null = $transfer.CopyAllObjects = $false
                                    $null = $transfer.Options.WithDependencies = $true
                                    $null = $transfer.ObjectList.Add($smobject)
                                    $null = $transfer.Options.ScriptDrops = $true
                                    $dropsql = $transfer.ScriptTransfer()
                                    Write-Message -Level Debug -Message "$dropsql" -FunctionName Copy-DbaSystemDbUserObject -ModuleName "dbatools"
                                    if ($__realCmdlet.ShouldProcess($destInstance, "Attempting to drop $type $name from $systemDb")) {
                                        $null = $destdb.Query("$dropsql")
                                    }
                                } else {
                                    if ($__realCmdlet.ShouldProcess($destInstance, "Attempting to drop $type $name from $systemDb using T-SQL")) {
                                        $null = $destdb.Query("DROP FUNCTION $($userobject.name)")
                                    }
                                }
                                if ($__realCmdlet.ShouldProcess($destInstance, "Attempting to add $type $name to $systemDb")) {
                                    $null = $destdb.Query("$sql")
                                    $copyobject.Status = "Successful"
                                    $copyobject | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                                }
                            }
                        } else {
                            if ($__realCmdlet.ShouldProcess($destInstance, "Attempting to add $type $name to $systemDb")) {
                                $null = $destdb.Query("$sql")
                                $copyobject.Status = "Successful"
                                $copyobject | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                            }
                        }
                    } catch {
                        try {
                            $smobject = switch ($userobject.Type) {
                                "VIEW" { $smodb.Views.Item($userobject.Name, $userobject.SchemaName) }
                                "SQL_STORED_PROCEDURE" { $smodb.StoredProcedures.Item($userobject.Name, $userobject.SchemaName) }
                                "RULE" { $smodb.Rules.Item($userobject.Name, $userobject.SchemaName) }
                                "SQL_TRIGGER" { $smodb.Triggers.Item($userobject.Name, $userobject.SchemaName) }
                            }
                            if ($smobject) {
                                $transfer = New-Object Microsoft.SqlServer.Management.Smo.Transfer $smodb
                                $null = $transfer.CopyAllObjects = $false
                                $null = $transfer.Options.WithDependencies = $true
                                $null = $transfer.ObjectList.Add($smobject)
                                $sql = $transfer.ScriptTransfer()
                                Write-Message -Level Debug -Message "$sql" -FunctionName Copy-DbaSystemDbUserObject -ModuleName "dbatools"
                                Write-Message -Level Verbose -Message "Adding $smoobject on $destdb on $destinstance" -FunctionName Copy-DbaSystemDbUserObject -ModuleName "dbatools"
                                if ($__realCmdlet.ShouldProcess($destInstance, "Attempting to add $type $name to $systemDb")) {
                                    $null = $destdb.Query("$sql")
                                }
                                $copyobject.Status = "Successful"
                                $copyobject.Notes = "May have also installed dependencies"
                                $copyobject | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                            } else {
                                $copyobject.Status = "Failed"
                                $copyobject.Notes = (Get-ErrorMessage -Record $_)
                                $copyobject | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                                continue
                            }
                        } catch {
                            $copyobject.Status = "Failed"
                            $copyobject.Notes = (Get-ErrorMessage -Record $_)
                            $copyobject | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                            continue
                        }
                    }
                }
            }
        } else {
            foreach ($systemDb in $systemDbs) {
                $sysdb = $sourceServer.databases[$systemDb]
                $transfer = New-Object Microsoft.SqlServer.Management.Smo.Transfer $sysdb
                $transfer.CopyAllObjects = $false
                $transfer.CopyAllDatabaseTriggers = $true
                $transfer.CopyAllDefaults = $true
                $transfer.CopyAllRoles = $true
                $transfer.CopyAllRules = $true
                $transfer.CopyAllSchemas = $true
                $transfer.CopyAllSequences = $true
                $transfer.CopyAllSqlAssemblies = $true
                $transfer.CopyAllSynonyms = $true
                $transfer.CopyAllTables = $true
                $transfer.CopyAllViews = $true
                $transfer.CopyAllStoredProcedures = $true
                $transfer.CopyAllUserDefinedAggregates = $true
                $transfer.CopyAllUserDefinedDataTypes = $true
                $transfer.CopyAllUserDefinedTableTypes = $true
                $transfer.CopyAllUserDefinedTypes = $true
                $transfer.CopyAllUserDefinedFunctions = $true
                $transfer.CopyAllUsers = $true
                $transfer.PreserveDbo = $true
                $transfer.Options.AllowSystemObjects = $false
                $transfer.Options.ContinueScriptingOnError = $true
                $transfer.Options.IncludeDatabaseRoleMemberships = $true
                $transfer.Options.Indexes = $true
                $transfer.Options.Permissions = $true
                $transfer.Options.WithDependencies = $false

                Write-Message -Level Output -Message "Copying from $systemDb." -FunctionName Copy-DbaSystemDbUserObject -ModuleName "dbatools"
                try {
                    $sqlQueries = $transfer.ScriptTransfer()

                    foreach ($sql in $sqlQueries) {
                        Write-Message -Level Debug -Message "$sql" -FunctionName Copy-DbaSystemDbUserObject -ModuleName "dbatools"
                        if ($__realCmdlet.ShouldProcess($destInstance, $sql)) {
                            try {
                                $destServer.Query($sql, $systemDb)
                            } catch {
                                # Don't care - long story having to do with duplicate stuff
                                # here to avoid an empty catch
                                $null = 1
                            }
                        }
                    }
                } catch {
                    # Don't care - long story having to do with duplicate stuff
                    # here to avoid an empty catch
                    $null = 1
                }
            }
        }
    }
} $Source $SourceSqlCredential $Destination $DestinationSqlCredential $Force $Classic $EnableException $__realCmdlet @__commonParameters 3>&1 2>&1
""";
}
