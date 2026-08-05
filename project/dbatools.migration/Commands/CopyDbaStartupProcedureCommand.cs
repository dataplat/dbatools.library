#nullable enable

using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Copies user-defined startup procedures in master between instances. Port of
/// public/Copy-DbaStartupProcedure.ps1. The workflow rides one module-scoped PowerShell hop: it
/// leans on Get-DbaModule, Invoke-DbaQuery and the private Get-ErrorMessage and Select-DefaultView,
/// and on SMO StoredProcedures.Item(), so the engine semantics stay where they were. The compiled
/// cmdlet supplies the real ShouldProcess runtime. Surface pinned by
/// migration/baselines/Copy-DbaStartupProcedure.json.
/// </summary>
[Cmdlet(VerbsCommon.Copy, "DbaStartupProcedure", DefaultParameterSetName = "Default",
    SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Medium)]
public sealed class CopyDbaStartupProcedureCommand : DbaBaseCmdlet
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

    /// <summary>Only copy the startup procedures with these names.</summary>
    [Parameter(Position = 4)]
    public string[]? Procedure { get; set; }

    /// <summary>Skip the startup procedures with these names.</summary>
    [Parameter(Position = 5)]
    public string[]? ExcludeProcedure { get; set; }

    /// <summary>Drop and recreate procedures that already exist on the destination.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // Begin and process stay in one hop, and there are no carried locals: no parameter takes
    // pipeline input, so ProcessRecord runs exactly once and nothing can survive between records.
    // Test-FunctionInterrupt still earns its line inside the script - the begin half's connect
    // failure has to stop the destination loop that follows in the same invocation.
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
            Procedure, ExcludeProcedure, Force.ToBool(),
            EnableException.ToBool(), this, NestedCommand.BoundCommonParameter(this, "Verbose"),
            NestedCommand.BoundCommonParameter(this, "Debug"));
    }

    private const string BodyScript = """
param($Source, $SourceSqlCredential, $Destination, $DestinationSqlCredential, $Procedure, $ExcludeProcedure, $Force, $EnableException, $__realCmdlet, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    # $Force is deliberately untyped: PowerShell excludes [switch] parameters from positional
    # binding, so one typed flag would shift every argument after it. It arrives as a real boolean,
    # which the -eq $false test below reads the way a switch reads.
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter]$Source, $SourceSqlCredential, [Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$Destination, $DestinationSqlCredential, [string[]]$Procedure, [string[]]$ExcludeProcedure, $Force, $EnableException, $__realCmdlet, $__boundVerbose, $__boundDebug)

    try {
        $sourceServer = Connect-DbaInstance -SqlInstance $Source -SqlCredential $SourceSqlCredential -MinimumVersion 9
    } catch {
        Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $Source -FunctionName Copy-DbaStartupProcedure
        return
    }
    # Includes properties: Name, Schema (both as strings)
    $startupProcs = Get-DbaModule -SqlInstance $sourceServer -Type StoredProcedure -Database master | Where-Object ExecIsStartup

    if ($Force) { $ConfirmPreference = 'none' }

    if (Test-FunctionInterrupt) { return }
    foreach ($destInstance in $Destination) {
        try {
            $destServer = Connect-DbaInstance -SqlInstance $destInstance -SqlCredential $DestinationSqlCredential -MinimumVersion 9
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $destinstance -Continue -FunctionName Copy-DbaStartupProcedure
        }

        $destStartupProcs = Get-DbaModule -SqlInstance $destServer -Type StoredProcedure -Database master

        foreach ($currentProc in $startupProcs) {
            $currentProcName = $currentProc.Name
            $currentProcSchema = $currentProc.SchemaName
            $currentProcFullName = "$currentProcSchema.$currentProcName"

            $copyStartupProcStatus = [PSCustomObject]@{
                SourceServer      = $sourceServer.Name
                DestinationServer = $destServer.Name
                Name              = $currentProcName
                Schema            = $currentProcSchema
                Status            = $null
                Notes             = $null
                Type              = "Startup Stored Procedure"
                DateTime          = [Dataplat.Dbatools.Utility.DbaDateTime](Get-Date)
            }

            if ($Procedure -and ($Procedure -notcontains $currentProcName)) {
                continue
            }

            if ($ExcludeProcedure -and ($ExcludeProcedure -contains $currentProcName)) {
                continue
            }

            if ($destStartupProcs.Name -contains $currentProcName) {
                if ($force -eq $false) {
                    if ($__realCmdlet.ShouldProcess($destInstance, "Startup procedure $currentProcFullName exists at destination. Use -Force to drop and migrate.")) {
                        $copyStartupProcStatus.Status = "Skipped"
                        $copyStartupProcStatus.Notes = "Already exists on destination"
                        $copyStartupProcStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject

                        Write-Message -Level Verbose -Message "Startup procedure $currentProcFullName exists at destination. Use -Force to drop and migrate." -FunctionName Copy-DbaStartupProcedure -ModuleName "dbatools"
                    }
                    continue
                } else {
                    if ($__realCmdlet.ShouldProcess($destInstance, "Dropping startup procedure $currentProcFullName and recreating")) {
                        try {
                            Write-Message -Level Verbose -Message "Dropping startup procedure $currentProcFullName" -FunctionName Copy-DbaStartupProcedure -ModuleName "dbatools"
                            $destServer.Query("DROP PROCEDURE [$($currentProcSchema)].[$($currentProcName)]")
                        } catch {
                            $copyStartupProcStatus.Status = "Failed"
                            $copyStartupProcStatus.Notes = (Get-ErrorMessage -Record $_)
                            $copyStartupProcStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                            Write-Message -Level Verbose -Message "Issue dropping startup procedure $currentProcFullName on $destinstance | $PSItem" -FunctionName Copy-DbaStartupProcedure -ModuleName "dbatools"
                            continue
                        }
                    }
                }
            }

            if ($__realCmdlet.ShouldProcess($destInstance, "Creating startup procedure $currentProcFullName")) {
                try {
                    Write-Message -Level Verbose -Message "Copying startup procedure $currentProcFullName" -FunctionName Copy-DbaStartupProcedure -ModuleName "dbatools"
                    $sp = $sourceServer.Databases['master'].StoredProcedures.Item($currentProcName, $currentProcSchema)
                    $header = $sp.TextHeader
                    $body = $sp.TextBody
                    $sql = $header + $body
                    Write-Message -Level Verbose -Message $sql -FunctionName Copy-DbaStartupProcedure -ModuleName "dbatools"
                    $null = Invoke-DbaQuery -SqlInstance $destServer -Query $sql -Database master -EnableException
                    $startupSql = "EXEC sp_procoption '$currentProcName', 'STARTUP', 'ON'"
                    Write-Message -Level Verbose -Message $startupSql -FunctionName Copy-DbaStartupProcedure -ModuleName "dbatools"
                    $null = Invoke-DbaQuery -SqlInstance $destServer -Query $startupSql -Database master -EnableException

                    $copyStartupProcStatus.Status = "Successful"
                    $copyStartupProcStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                } catch {
                    $copyStartupProcStatus.Status = "Failed"
                    $copyStartupProcStatus.Notes = (Get-ErrorMessage -Record $_)
                    $copyStartupProcStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                    Write-Message -Level Verbose -Message "Issue creating startup procedure $currentProcFullName on $destinstance | $PSItem" -FunctionName Copy-DbaStartupProcedure -ModuleName "dbatools"
                    continue
                }
            }
        }
    }
} $Source $SourceSqlCredential $Destination $DestinationSqlCredential $Procedure $ExcludeProcedure $Force $EnableException $__realCmdlet $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
