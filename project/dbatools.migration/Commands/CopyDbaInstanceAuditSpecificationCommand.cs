#nullable enable

using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Copies server audit specifications between instances. Port of
/// public/Copy-DbaInstanceAuditSpecification.ps1. The workflow rides one module-scoped PowerShell
/// hop: it leans on the private Get-ErrorMessage, Test-SqlSa and Select-DefaultView, and on SMO
/// ServerAuditSpecification.Script() plus Server.Query, so the engine semantics stay where they
/// were. The compiled cmdlet supplies the real ShouldProcess runtime. Surface pinned by
/// migration/baselines/Copy-DbaInstanceAuditSpecification.json.
/// </summary>
[Cmdlet(VerbsCommon.Copy, "DbaInstanceAuditSpecification", DefaultParameterSetName = "Default",
    SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Medium)]
public sealed class CopyDbaInstanceAuditSpecificationCommand : DbaBaseCmdlet
{
    /// <summary>The source SQL Server instance. Requires sysadmin and SQL Server 2008 or higher.</summary>
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

    /// <summary>Only copy the server audit specifications with these names.</summary>
    [Parameter(Position = 4)]
    public object[]? AuditSpecification { get; set; }

    /// <summary>Skip the server audit specifications with these names.</summary>
    [Parameter(Position = 5)]
    public object[]? ExcludeAuditSpecification { get; set; }

    /// <summary>Drop and recreate audit specifications that already exist on the destination.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // Begin and process stay in one hop, and there are no carried locals: no parameter takes
    // pipeline input, so ProcessRecord runs exactly once and nothing can survive between records.
    // Test-FunctionInterrupt still earns its line inside the script - the begin half's source
    // connect and sysadmin checks have to stop the destination loop that follows in the same
    // invocation.
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
            AuditSpecification, ExcludeAuditSpecification, Force.ToBool(),
            EnableException.ToBool(), this, NestedCommand.BoundCommonParameter(this, "Verbose"),
            NestedCommand.BoundCommonParameter(this, "Debug"));
    }

    private const string BodyScript = """
param($Source, $SourceSqlCredential, $Destination, $DestinationSqlCredential, $AuditSpecification, $ExcludeAuditSpecification, $Force, $EnableException, $__realCmdlet, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    # $Force is deliberately untyped: PowerShell excludes [switch] parameters from positional
    # binding, so one typed flag would shift every argument after it. It arrives as a real boolean,
    # which the -eq $false tests below read the way a switch reads.
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter]$Source, $SourceSqlCredential, [Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$Destination, $DestinationSqlCredential, [object[]]$AuditSpecification, [object[]]$ExcludeAuditSpecification, $Force, $EnableException, $__realCmdlet, $__boundVerbose, $__boundDebug)

    try {
        $sourceServer = Connect-DbaInstance -SqlInstance $Source -SqlCredential $SourceSqlCredential -MinimumVersion 10
    } catch {
        Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $Source -FunctionName Copy-DbaInstanceAuditSpecification
        return
    }

    if (!(Test-SqlSa -SqlInstance $sourceServer -SqlCredential $SourceSqlCredential)) {
        Stop-Function -Message "Not a sysadmin on $source. Quitting." -FunctionName Copy-DbaInstanceAuditSpecification
        return
    }

    # Plural, and one letter away from the $AuditSpecification parameter that filters it below.
    $AuditSpecifications = $sourceServer.ServerAuditSpecifications

    if ($Force) { $ConfirmPreference = 'none' }

    if (Test-FunctionInterrupt) { return }

    foreach ($destinstance in $Destination) {
        try {
            $destServer = Connect-DbaInstance -SqlInstance $destinstance -SqlCredential $DestinationSqlCredential -MinimumVersion 10
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $destinstance -Continue -FunctionName Copy-DbaInstanceAuditSpecification
        }

        if (!(Test-SqlSa -SqlInstance $destServer -SqlCredential $DestinationSqlCredential)) {
            Stop-Function -Message "Not a sysadmin on $destinstance. Quitting." -FunctionName Copy-DbaInstanceAuditSpecification
            return
        }

        if ($destServer.VersionMajor -lt $sourceServer.VersionMajor) {
            Stop-Function -Message "Migration from version $($destServer.VersionMajor) to version $($sourceServer.VersionMajor) is not supported." -FunctionName Copy-DbaInstanceAuditSpecification
            return
        }
        $destAudits = $destServer.ServerAuditSpecifications
        foreach ($auditSpec in $AuditSpecifications) {
            $auditSpecName = $auditSpec.Name

            $copyAuditSpecStatus = [PSCustomObject]@{
                SourceServer      = $sourceServer.Name
                DestinationServer = $destServer.Name
                Type              = "Server Audit Specification"
                Name              = $auditSpecName
                Status            = $null
                Notes             = $null
                DateTime          = [Dataplat.Dbatools.Utility.DbaDateTime](Get-Date)
            }

            if ($AuditSpecification -and $auditSpecName -notin $AuditSpecification -or $auditSpecName -in $ExcludeAuditSpecification) {
                continue
            }

            $destServer.Audits.Refresh()
            if ($destServer.Audits.Name -notcontains $auditSpec.AuditName) {
                if ($__realCmdlet.ShouldProcess($destinstance, "Audit $($auditSpec.AuditName) does not exist on $destinstance. Skipping $auditSpecName.")) {
                    $copyAuditSpecStatus.Status = "Skipped"
                    $copyAuditSpecStatus.Notes = "Audit $($auditSpec.AuditName) does not exist on $destinstance. Skipping $auditSpecName."
                    Write-Message -Level Warning -Message "Audit $($auditSpec.AuditName) does not exist on $destinstance. Skipping $auditSpecName." -FunctionName Copy-DbaInstanceAuditSpecification -ModuleName "dbatools"
                    $copyAuditSpecStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                }
                continue
            }

            if ($destAudits.name -contains $auditSpecName) {
                if ($force -eq $false) {
                    if ($__realCmdlet.ShouldProcess($destinstance, "Server audit $auditSpecName exists at destination. Use -Force to drop and migrate.")) {
                        Write-Message -Level Verbose -Message "Server audit $auditSpecName exists at destination. Use -Force to drop and migrate." -FunctionName Copy-DbaInstanceAuditSpecification -ModuleName "dbatools"
                        $copyAuditSpecStatus.Status = "Skipped"
                        $copyAuditSpecStatus.Notes = "Already exists on destination"
                        $copyAuditSpecStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                    }
                    continue
                } else {
                    if ($__realCmdlet.ShouldProcess($destinstance, "Dropping server audit $auditSpecName and recreating")) {
                        try {
                            Write-Message -Level Verbose -Message "Dropping server audit $auditSpecName" -FunctionName Copy-DbaInstanceAuditSpecification -ModuleName "dbatools"
                            $destServer.ServerAuditSpecifications[$auditSpecName].Drop()
                        } catch {
                            $copyAuditSpecStatus.Status = "Failed"
                            $copyAuditSpecStatus.Notes = (Get-ErrorMessage -Record $_)
                            $copyAuditSpecStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                            Write-Message -Level Verbose -Message "Issue dropping audit specification $auditSpecName on $destinstance | $PSItem" -FunctionName Copy-DbaInstanceAuditSpecification -ModuleName "dbatools"
                            continue
                        }
                    }
                }
            }
            if ($__realCmdlet.ShouldProcess($destinstance, "Creating server audit $auditSpecName")) {
                try {
                    Write-Message -Level Verbose -Message "Copying server audit $auditSpecName" -FunctionName Copy-DbaInstanceAuditSpecification -ModuleName "dbatools"
                    $sql = $auditSpec.Script() | Out-String
                    Write-Message -Level Debug -Message $sql -FunctionName Copy-DbaInstanceAuditSpecification -ModuleName "dbatools"
                    $destServer.Query($sql)
                    $copyAuditSpecStatus.Status = "Successful"
                    $copyAuditSpecStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                } catch {
                    $copyAuditSpecStatus.Status = "Failed"
                    $copyAuditSpecStatus.Notes = (Get-ErrorMessage -Record $_)
                    $copyAuditSpecStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                    Write-Message -Level Verbose -Message "Issue creating audit specification $auditSpecName on $destinstance | $PSItem" -FunctionName Copy-DbaInstanceAuditSpecification -ModuleName "dbatools"
                    continue
                }
            }
        }
    }
} $Source $SourceSqlCredential $Destination $DestinationSqlCredential $AuditSpecification $ExcludeAuditSpecification $Force $EnableException $__realCmdlet $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
