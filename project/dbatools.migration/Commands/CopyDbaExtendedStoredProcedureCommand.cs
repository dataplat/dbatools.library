#nullable enable

using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Copies custom Extended Stored Procedures and their DLL files between instances. Port of
/// public/Copy-DbaExtendedStoredProcedure.ps1. The workflow rides one module-scoped PowerShell hop:
/// it leans on the private Get-ErrorMessage and Select-DefaultView, and on Server.Query, so the
/// engine semantics stay where they were. The compiled cmdlet supplies the real ShouldProcess
/// runtime. Surface pinned by migration/baselines/Copy-DbaExtendedStoredProcedure.json.
/// </summary>
[Cmdlet(VerbsCommon.Copy, "DbaExtendedStoredProcedure", DefaultParameterSetName = "Default",
    SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Medium)]
public sealed class CopyDbaExtendedStoredProcedureCommand : DbaBaseCmdlet
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

    /// <summary>Only copy the Extended Stored Procedures with these names.</summary>
    [Parameter(Position = 4)]
    public string[]? ExtendedProcedure { get; set; }

    /// <summary>Skip the Extended Stored Procedures with these names.</summary>
    [Parameter(Position = 5)]
    public string[]? ExcludeExtendedProcedure { get; set; }

    /// <summary>Where the DLL files are copied to; the destination Binn directory by default.</summary>
    [Parameter(Position = 6)]
    public string? DestinationPath { get; set; }

    /// <summary>Drop and recreate procedures that already exist on the destination.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // Begin and process stay in one hop, and there are no carried locals: no parameter takes
    // pipeline input, so ProcessRecord runs exactly once and nothing can survive between records.
    // Test-FunctionInterrupt still earns its line inside the script - the begin half's connect and
    // query failures have to stop the destination loop that follows in the same invocation.
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
            ExtendedProcedure, ExcludeExtendedProcedure, DestinationPath, Force.ToBool(),
            EnableException.ToBool(), this, NestedCommand.BoundCommonParameter(this, "Verbose"),
            NestedCommand.BoundCommonParameter(this, "Debug"));
    }

    private const string BodyScript = """
param($Source, $SourceSqlCredential, $Destination, $DestinationSqlCredential, $ExtendedProcedure, $ExcludeExtendedProcedure, $DestinationPath, $Force, $EnableException, $__realCmdlet, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    # $Force is deliberately untyped: PowerShell excludes [switch] parameters from positional
    # binding, so one typed flag would shift every argument after it. It arrives as a real boolean,
    # which the -eq $false test below reads the way a switch reads.
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter]$Source, $SourceSqlCredential, [Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$Destination, $DestinationSqlCredential, [string[]]$ExtendedProcedure, [string[]]$ExcludeExtendedProcedure, [string]$DestinationPath, $Force, $EnableException, $__realCmdlet, $__boundVerbose, $__boundDebug)

    try {
        $sourceServer = Connect-DbaInstance -SqlInstance $Source -SqlCredential $SourceSqlCredential -MinimumVersion 9
    } catch {
        Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $Source -FunctionName Copy-DbaExtendedStoredProcedure
        return
    }

    # Query to get custom Extended Stored Procedures
    # System XPs typically start with xp_ and are in resource database or have DLL in system paths
    # Custom XPs are user-created and we'll identify them
    $sql = @"
SELECT
    p.name AS ProcedureName,
    SCHEMA_NAME(p.schema_id) AS SchemaName,
    p.object_id,
    m.definition AS DllPath
FROM sys.procedures p
INNER JOIN sys.all_objects o ON p.object_id = o.object_id
LEFT JOIN sys.sql_modules m ON p.object_id = m.object_id
WHERE p.type = 'X'
    AND p.is_ms_shipped = 0
ORDER BY p.name
"@

    try {
        $sourceXPs = $sourceServer.Query($sql)
    } catch {
        Stop-Function -Message "Failed to query Extended Stored Procedures from source: $PSItem" -Target $Source -ErrorRecord $_ -FunctionName Copy-DbaExtendedStoredProcedure
        return
    }

    if (-not $sourceXPs) {
        Write-Message -Level Verbose -Message "No custom Extended Stored Procedures found on source server" -FunctionName Copy-DbaExtendedStoredProcedure -ModuleName "dbatools"
    }

    if ($Force) { $ConfirmPreference = 'none' }

    if (Test-FunctionInterrupt) { return }

    foreach ($destInstance in $Destination) {
        try {
            $destServer = Connect-DbaInstance -SqlInstance $destInstance -SqlCredential $DestinationSqlCredential -MinimumVersion 9
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $destInstance -Continue -FunctionName Copy-DbaExtendedStoredProcedure
        }

        # Get destination XPs
        try {
            $destXPs = $destServer.Query($sql)
        } catch {
            Stop-Function -Message "Failed to query Extended Stored Procedures from destination: $PSItem" -Target $destInstance -ErrorRecord $_ -Continue -FunctionName Copy-DbaExtendedStoredProcedure
        }

        # Get destination Binn path if not specified
        if (-not $DestinationPath) {
            try {
                $destBinnPath = $destServer.RootDirectory + "\Binn"
            } catch {
                Write-Message -Level Warning -Message "Could not determine destination Binn directory. DLL files will not be copied." -FunctionName Copy-DbaExtendedStoredProcedure -ModuleName "dbatools"
                $destBinnPath = $null
            }
        } else {
            $destBinnPath = $DestinationPath
        }

        # Get source Binn path
        try {
            $sourceBinnPath = $sourceServer.RootDirectory + "\Binn"
        } catch {
            Write-Message -Level Warning -Message "Could not determine source Binn directory. DLL files will not be copied." -FunctionName Copy-DbaExtendedStoredProcedure -ModuleName "dbatools"
            $sourceBinnPath = $null
        }

        foreach ($currentXP in $sourceXPs) {
            $xpName = $currentXP.ProcedureName
            $xpSchema = $currentXP.SchemaName
            $xpFullName = "$xpSchema.$xpName"

            $copyXPStatus = [PSCustomObject]@{
                SourceServer      = $sourceServer.Name
                DestinationServer = $destServer.Name
                Name              = $xpName
                Schema            = $xpSchema
                Type              = "Extended Stored Procedure"
                Status            = $null
                Notes             = $null
                DateTime          = [Dataplat.Dbatools.Utility.DbaDateTime](Get-Date)
            }

            # Filter by include/exclude
            if ($ExtendedProcedure -and ($ExtendedProcedure -notcontains $xpName)) {
                continue
            }

            if ($ExcludeExtendedProcedure -and ($ExcludeExtendedProcedure -contains $xpName)) {
                continue
            }

            # Check if exists on destination
            $existsOnDest = $destXPs | Where-Object ProcedureName -eq $xpName

            if ($existsOnDest) {
                if ($force -eq $false) {
                    if ($__realCmdlet.ShouldProcess($destInstance, "Extended Stored Procedure $xpFullName exists at destination. Use -Force to drop and migrate.")) {
                        $copyXPStatus.Status = "Skipped"
                        $copyXPStatus.Notes = "Already exists on destination"
                        $copyXPStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject

                        Write-Message -Level Verbose -Message "Extended Stored Procedure $xpFullName exists at destination. Use -Force to drop and migrate." -FunctionName Copy-DbaExtendedStoredProcedure -ModuleName "dbatools"
                    }
                    continue
                } else {
                    if ($__realCmdlet.ShouldProcess($destInstance, "Dropping Extended Stored Procedure $xpFullName and recreating")) {
                        try {
                            Write-Message -Level Verbose -Message "Dropping Extended Stored Procedure $xpFullName" -FunctionName Copy-DbaExtendedStoredProcedure -ModuleName "dbatools"
                            # Get DLL name before dropping
                            $dropXP = $destXPs | Where-Object ProcedureName -eq $xpName
                            $dropDllName = $null
                            if ($dropXP.DllPath) {
                                $dropDllName = Split-Path $dropXP.DllPath -Leaf
                            }
                            $dropSql = "EXEC dbo.sp_dropextendedproc @functname = N'$xpFullName'"
                            $null = $destServer.Query($dropSql)
                        } catch {
                            $copyXPStatus.Status = "Failed"
                            $copyXPStatus.Notes = (Get-ErrorMessage -Record $_)
                            $copyXPStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                            Write-Message -Level Verbose -Message "Issue dropping Extended Stored Procedure $xpFullName on $destInstance | $PSItem" -FunctionName Copy-DbaExtendedStoredProcedure -ModuleName "dbatools"
                            continue
                        }
                    }
                }
            }

            if ($__realCmdlet.ShouldProcess($destInstance, "Creating Extended Stored Procedure $xpFullName")) {
                try {
                    # Get DLL information from source
                    $sourceDllPath = $currentXP.DllPath
                    if (-not $sourceDllPath) {
                        # Try to get from sys.extended_procedures or sp_helpextendedproc
                        $dllQuery = "EXEC dbo.sp_helpextendedproc @funcname = N'$xpFullName'"
                        $dllInfo = $sourceServer.Query($dllQuery)
                        if ($dllInfo) {
                            $sourceDllPath = $dllInfo[0].DLL
                        }
                    }

                    if (-not $sourceDllPath) {
                        $copyXPStatus.Status = "Failed"
                        $copyXPStatus.Notes = "Could not determine DLL path for Extended Stored Procedure"
                        $copyXPStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                        Write-Message -Level Warning -Message "Could not determine DLL path for Extended Stored Procedure $xpFullName. Manual intervention required." -FunctionName Copy-DbaExtendedStoredProcedure -ModuleName "dbatools"
                        continue
                    }

                    $dllFileName = Split-Path $sourceDllPath -Leaf
                    $dllCopied = $false
                    $dllCopyNotes = $null

                    # Attempt to copy DLL file
                    if ($sourceBinnPath -and $destBinnPath) {
                        $sourceDllFullPath = Join-Path $sourceBinnPath $dllFileName
                        $destDllFullPath = Join-Path $destBinnPath $dllFileName

                        # Check if source DLL exists
                        $sourceComputerName = $sourceServer.ComputerName
                        $destComputerName = $destServer.ComputerName

                        try {
                            # Use UNC paths for remote copying
                            $sourceUncPath = "\\$sourceComputerName\$($sourceDllFullPath -replace ':', '$')"
                            $destUncPath = "\\$destComputerName\$($destDllFullPath -replace ':', '$')"

                            if (Test-Path $sourceUncPath) {
                                Write-Message -Level Verbose -Message "Copying DLL from $sourceUncPath to $destUncPath" -FunctionName Copy-DbaExtendedStoredProcedure -ModuleName "dbatools"
                                Copy-Item -Path $sourceUncPath -Destination $destUncPath -Force -ErrorAction Stop
                                $dllCopied = $true
                                Write-Message -Level Verbose -Message "Successfully copied DLL file" -FunctionName Copy-DbaExtendedStoredProcedure -ModuleName "dbatools"
                            } else {
                                $dllCopyNotes = "Source DLL not found at expected path: $sourceDllFullPath"
                                Write-Message -Level Warning -Message $dllCopyNotes -FunctionName Copy-DbaExtendedStoredProcedure -ModuleName "dbatools"
                            }
                        } catch {
                            $dllCopyNotes = "Failed to copy DLL file: $PSItem. DLL may need to be copied manually or recompiled for OS/SQL version compatibility."
                            Write-Message -Level Warning -Message $dllCopyNotes -FunctionName Copy-DbaExtendedStoredProcedure -ModuleName "dbatools"
                        }
                    } else {
                        $dllCopyNotes = "Could not determine source or destination Binn paths. DLL must be copied manually."
                        Write-Message -Level Warning -Message $dllCopyNotes -FunctionName Copy-DbaExtendedStoredProcedure -ModuleName "dbatools"
                    }

                    # Create the Extended Stored Procedure
                    $destDllPath = if ($dllCopied) { $destDllFullPath } else { $sourceDllPath }
                    $createSql = "EXEC dbo.sp_addextendedproc @functname = N'$xpFullName', @dllname = N'$destDllPath'"

                    Write-Message -Level Verbose -Message "Creating Extended Stored Procedure $xpFullName" -FunctionName Copy-DbaExtendedStoredProcedure -ModuleName "dbatools"
                    Write-Message -Level Debug -Message $createSql -FunctionName Copy-DbaExtendedStoredProcedure -ModuleName "dbatools"

                    $null = $destServer.Query($createSql)

                    $copyXPStatus.Status = if ($dllCopied) { "Successful" } else { "Successful (DLL not copied)" }
                    $copyXPStatus.Notes = $dllCopyNotes
                    $copyXPStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject

                    if (-not $dllCopied) {
                        Write-Message -Level Warning -Message "Extended Stored Procedure $xpFullName created but DLL was not copied. You may need to manually copy the DLL file and ensure it's compatible with the destination OS/SQL version." -FunctionName Copy-DbaExtendedStoredProcedure -ModuleName "dbatools"
                    }
                } catch {
                    $copyXPStatus.Status = "Failed"
                    $copyXPStatus.Notes = (Get-ErrorMessage -Record $_)
                    $copyXPStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                    Write-Message -Level Verbose -Message "Issue creating Extended Stored Procedure $xpFullName on $destInstance | $PSItem" -FunctionName Copy-DbaExtendedStoredProcedure -ModuleName "dbatools"
                    continue
                }
            }
        }
    }
} $Source $SourceSqlCredential $Destination $DestinationSqlCredential $ExtendedProcedure $ExcludeExtendedProcedure $DestinationPath $Force $EnableException $__realCmdlet $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
