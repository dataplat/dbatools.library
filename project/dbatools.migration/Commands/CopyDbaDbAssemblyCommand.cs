#nullable enable

using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Copies user CLR assemblies from source databases to the matching databases on destination
/// instances. Port of public/Copy-DbaDbAssembly.ps1. The whole workflow rides one module-scoped
/// PowerShell hop because it leans on SMO assembly enumeration and Script(), Database.Query with
/// an explicit database argument, and Select-DefaultView decoration, all of which keep the retired
/// function's engine semantics there. The compiled cmdlet supplies the real ShouldProcess runtime.
/// Surface pinned by migration/baselines/Copy-DbaDbAssembly.json.
/// </summary>
[Cmdlet(VerbsCommon.Copy, "DbaDbAssembly", DefaultParameterSetName = "Default",
    SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Medium)]
public sealed class CopyDbaDbAssemblyCommand : DbaBaseCmdlet
{
    /// <summary>The source SQL Server instance. Requires sysadmin and SQL Server 2005 or higher.</summary>
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

    /// <summary>Only copy these assemblies, named DatabaseName.AssemblyName.</summary>
    [Parameter(Position = 4)]
    public object[]? Assembly { get; set; }

    /// <summary>Skip these assemblies, named DatabaseName.AssemblyName.</summary>
    [Parameter(Position = 5)]
    public object[]? ExcludeAssembly { get; set; }

    /// <summary>Drop and recreate assemblies that already exist on the destination.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

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
            Assembly, ExcludeAssembly, Force.ToBool(),
            MyInvocation.BoundParameters.ContainsKey("Assembly"),
            EnableException.ToBool(), this, NestedCommand.BoundCommonParameter(this, "Verbose"),
            NestedCommand.BoundCommonParameter(this, "Debug"));
    }

    // PS: begin and process collapse into one ProcessRecord because no parameter takes pipeline
    // input. The single Test-Bound site becomes the carried $__boundAssembly flag - the module
    // scope cannot see the caller's $PSBoundParameters, and the hop binds every parameter
    // positionally, so an uncarried Test-Bound would report every parameter bound.
    private const string BodyScript = """
param($Source, $SourceSqlCredential, $Destination, $DestinationSqlCredential, $Assembly, $ExcludeAssembly, $Force, $__boundAssembly, $EnableException, $__realCmdlet, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter]$Source, $SourceSqlCredential, [Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$Destination, $DestinationSqlCredential, [object[]]$Assembly, [object[]]$ExcludeAssembly, $Force, $__boundAssembly, $EnableException, $__realCmdlet, $__boundVerbose, $__boundDebug)

    try {
        $sourceServer = Connect-DbaInstance -SqlInstance $Source -SqlCredential $SourceSqlCredential -MinimumVersion 9
    } catch {
        Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $Source -FunctionName Copy-DbaDbAssembly
        return
    }
    $sourceAssemblies = @()
    foreach ($database in ($sourceServer.Databases | Where-Object IsAccessible)) {
        Write-Message -Level Verbose -Message "Processing $database on source" -FunctionName Copy-DbaDbAssembly -ModuleName "dbatools"

        try {
            # a bug here requires a try/catch
            $userAssemblies = $database.Assemblies | Where-Object IsSystemObject -eq $false
            foreach ($asmb in $userAssemblies) {
                $sourceAssemblies += $asmb
            }
        } catch {
            #here to avoid an empty catch
            $null = 1
        }
    }

    if ($Force) { $ConfirmPreference = "none" }

    if (Test-FunctionInterrupt) { return }
    foreach ($destinstance in $Destination) {
        try {
            $destServer = Connect-DbaInstance -SqlInstance $destinstance -SqlCredential $DestinationSqlCredential -MinimumVersion 9
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $destinstance -Continue -FunctionName Copy-DbaDbAssembly
        }

        $destAssemblies = @()
        foreach ($database in $destServer.Databases) {
            Write-Message -Level VeryVerbose -Message "Processing $database on destination" -FunctionName Copy-DbaDbAssembly -ModuleName "dbatools"
            try {
                # a bug here requires a try/catch
                $userAssemblies = $database.Assemblies | Where-Object IsSystemObject -eq $false
                foreach ($asmb in $userAssemblies) {
                    $destAssemblies += $asmb
                }
            } catch {
                #here to avoid an empty catch
                $null = 1
            }
        }
        foreach ($currentAssembly in $sourceAssemblies) {
            $assemblyName = $currentAssembly.Name
            $dbName = $currentAssembly.Parent.Name
            $destDb = $destServer.Databases[$dbName]
            Write-Message -Level VeryVerbose -Message "Processing $assemblyName on $dbName" -FunctionName Copy-DbaDbAssembly -ModuleName "dbatools"
            $copyDbAssemblyStatus = [PSCustomObject]@{
                SourceServer          = $sourceServer.Name
                SourceDatabase        = $dbName
                SourceDatabaseID      = $currentAssembly.Parent.ID
                DestinationServer     = $destServer.Name
                DestinationDatabase   = $destDb
                DestinationDatabaseID = $destDb.ID
                type                  = "Database Assembly"
                Name                  = $assemblyName
                Status                = $null
                Notes                 = $null
                DateTime              = [Dataplat.Dbatools.Utility.DbaDateTime](Get-Date)
            }


            if (!$destDb) {
                if ($__realCmdlet.ShouldProcess($destinstance, "Destination database $dbName does not exist. Skipping $assemblyName.")) {
                    Write-Message -Level Verbose -Message "Destination database $dbName does not exist. Skipping $assemblyName." -FunctionName Copy-DbaDbAssembly -ModuleName "dbatools"
                    $copyDbAssemblyStatus.Status = "Skipped"
                    $copyDbAssemblyStatus.Notes = "Destination database does not exist"
                    $copyDbAssemblyStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                }
                continue
            }

            if ($__boundAssembly -and $Assembly -notcontains "$dbName.$assemblyName" -or $ExcludeAssembly -contains "$dbName.$assemblyName") {
                continue
            }

            if ($currentAssembly.AssemblySecurityLevel -eq "External" -and -not $destDb.Trustworthy) {
                if ($__realCmdlet.ShouldProcess($destinstance, "Setting $dbName to External")) {
                    Write-Message -Level Verbose -Message "Setting $dbName Security Level to External on $destinstance." -FunctionName Copy-DbaDbAssembly -ModuleName "dbatools"
                    $sql = "ALTER DATABASE $dbName SET TRUSTWORTHY ON"
                    try {
                        Write-Message -Level Debug -Message $sql -FunctionName Copy-DbaDbAssembly -ModuleName "dbatools"
                        $destServer.Query($sql)
                    } catch {
                        $copyDbAssemblyStatus.Status = "Failed to set security level to external"
                        $copyDbAssemblyStatus.Notes = "$PSItem"
                        $copyDbAssemblyStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                        Write-Message -Level Verbose -Message "Failed to set security level to external for $dbName on $destinstance | $PSItem" -FunctionName Copy-DbaDbAssembly -ModuleName "dbatools"
                        continue
                    }
                }
            }

            if ($destDb.Query("SELECT name FROM sys.assemblies WHERE name = '$assemblyName'").name) {
                if ($force -eq $false) {
                    if ($__realCmdlet.ShouldProcess($destinstance, "Assembly $assemblyName exists at destination in the $dbName database. Use -Force to drop and migrate.")) {
                        Write-Message -Level Verbose -Message "Assembly $assemblyName exists at destination in the $dbName database. Use -Force to drop and migrate." -FunctionName Copy-DbaDbAssembly -ModuleName "dbatools"
                        $copyDbAssemblyStatus.Status = "Skipped"
                        $copyDbAssemblyStatus.Notes = "Already exists on destination"
                        $copyDbAssemblyStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                    }
                    continue
                } else {
                    if ($__realCmdlet.ShouldProcess($destinstance, "Dropping assembly $assemblyName on $($destDb.Name) on $($destServer.Name)")) {
                        try {
                            Write-Message -Level Verbose -Message "Dropping assembly $assemblyName." -FunctionName Copy-DbaDbAssembly -ModuleName "dbatools"

                            if ($destDb.Query("SELECT a.name FROM sys.assemblies a WHERE a.name = '$assemblyName' AND EXISTS (SELECT 1 FROM sys.assembly_references b WHERE b.assembly_id = a.assembly_id OR b.referenced_assembly_id = a.assembly_id)").name) {
                                Write-Message -Level Verbose -Message "This won't work if there are dependencies." -FunctionName Copy-DbaDbAssembly -ModuleName "dbatools"
                                throw "$assemblyName has dependencies but this command does not yet support dependent objects"
                            }

                            $destDb.Query("DROP ASSEMBLY $assemblyName")
                        } catch {
                            $copyDbAssemblyStatus.Status = "Failed"
                            $copyDbAssemblyStatus.Notes = "$PSItem"
                            $copyDbAssemblyStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                            Write-Message -Level Verbose -Message "Failed to drop assembly $assemblyName for $dbName on $destinstance | $PSItem" -FunctionName Copy-DbaDbAssembly -ModuleName "dbatools"
                            continue
                        }
                    }
                }
            }

            if ($__realCmdlet.ShouldProcess($destinstance, "Creating assembly $assemblyName")) {
                try {
                    Write-Message -Level Verbose -Message "Copying assembly $assemblyName from database." -FunctionName Copy-DbaDbAssembly -ModuleName "dbatools"
                    $sql = $currentAssembly.Script()
                    Write-Message -Level Debug -Message ($sql -join " ") -FunctionName Copy-DbaDbAssembly -ModuleName "dbatools"
                    $destDb.Query($sql, $dbName)

                    $copyDbAssemblyStatus.Status = "Successful"
                    $copyDbAssemblyStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject

                } catch {
                    $copyDbAssemblyStatus.Status = "Failed"
                    $copyDbAssemblyStatus.Notes = $PSItem
                    $copyDbAssemblyStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                    Write-Message -Level Verbose -Message "Failed to create assembly $assemblyName for $dbName on $destinstance | $PSItem" -FunctionName Copy-DbaDbAssembly -ModuleName "dbatools"
                    continue
                }
            }
        }
    }
} $Source $SourceSqlCredential $Destination $DestinationSqlCredential $Assembly $ExcludeAssembly $Force $__boundAssembly $EnableException $__realCmdlet $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
