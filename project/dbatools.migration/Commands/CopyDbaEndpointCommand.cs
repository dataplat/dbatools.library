#nullable enable

using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Copies user-defined endpoints between instances. Port of public/Copy-DbaEndpoint.ps1. The whole
/// workflow rides one module-scoped PowerShell hop because it leans on SMO endpoint scripting, the
/// private Get-ErrorMessage, and Select-DefaultView decoration, all of which keep the retired
/// function's engine semantics there. The compiled cmdlet supplies the real ShouldProcess runtime.
/// Surface pinned by migration/baselines/Copy-DbaEndpoint.json.
/// </summary>
[Cmdlet(VerbsCommon.Copy, "DbaEndpoint", DefaultParameterSetName = "Default",
    SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Medium)]
public sealed class CopyDbaEndpointCommand : DbaBaseCmdlet
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

    /// <summary>Only copy the endpoints with these names.</summary>
    [Parameter(Position = 4)]
    public object[]? Endpoint { get; set; }

    /// <summary>Skip the endpoints with these names.</summary>
    [Parameter(Position = 5)]
    public object[]? ExcludeEndpoint { get; set; }

    /// <summary>Drop and recreate endpoints that already exist on the destination.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // Begin and process stay in one hop, and there are no carried locals: no parameter takes
    // pipeline input, so ProcessRecord runs exactly once and nothing can survive between records.
    // Test-FunctionInterrupt still earns its line inside the script - the begin half's connect
    // failure has to stop the destination loop that follows it in the same invocation.
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
            Endpoint, ExcludeEndpoint, Force.ToBool(),
            EnableException.ToBool(), this, NestedCommand.BoundCommonParameter(this, "Verbose"),
            NestedCommand.BoundCommonParameter(this, "Debug"));
    }

    private const string BodyScript = """
param($Source, $SourceSqlCredential, $Destination, $DestinationSqlCredential, $Endpoint, $ExcludeEndpoint, $Force, $EnableException, $__realCmdlet, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    # $Force is deliberately untyped: PowerShell excludes [switch] parameters from positional
    # binding, so one typed flag would shift every argument after it. It arrives as a real boolean,
    # which the -eq $false test below reads the way a switch reads.
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter]$Source, $SourceSqlCredential, [Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$Destination, $DestinationSqlCredential, [object[]]$Endpoint, [object[]]$ExcludeEndpoint, $Force, $EnableException, $__realCmdlet, $__boundVerbose, $__boundDebug)

    try {
        $sourceServer = Connect-DbaInstance -SqlInstance $Source -SqlCredential $SourceSqlCredential -MinimumVersion 9
    } catch {
        Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $Source -FunctionName Copy-DbaEndpoint
        return
    }
    $serverEndpoints = $sourceServer.Endpoints | Where-Object IsSystemObject -eq $false

    if ($Force) { $ConfirmPreference = 'none' }

    if (Test-FunctionInterrupt) { return }
    foreach ($destinstance in $Destination) {
        try {
            $destServer = Connect-DbaInstance -SqlInstance $destinstance -SqlCredential $DestinationSqlCredential -MinimumVersion 9
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $destinstance -Continue -FunctionName Copy-DbaEndpoint
        }
        $destEndpoints = $destServer.Endpoints

        foreach ($currentEndpoint in $serverEndpoints) {
            $endpointName = $currentEndpoint.Name

            $copyEndpointStatus = [PSCustomObject]@{
                SourceServer      = $sourceServer.Name
                DestinationServer = $destServer.Name
                Name              = $endpointName
                Type              = "Endpoint"
                Status            = $null
                Notes             = $null
                DateTime          = [Dataplat.Dbatools.Utility.DbaDateTime](Get-Date)
            }

            if ($Endpoint -and $Endpoint -notcontains $endpointName -or $ExcludeEndpoint -contains $endpointName) {
                continue
            }

            if ($destEndpoints.Name -contains $endpointName) {
                if ($force -eq $false) {
                    if ($__realCmdlet.ShouldProcess($destinstance, "Server endpoint $endpointName exists at destination. Use -Force to drop and migrate.")) {
                        $copyEndpointStatus.Status = "Skipped"
                        $copyEndpointStatus.Notes = "Already exists on destination"
                        $copyEndpointStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                        Write-Message -Level Verbose -Message "Server endpoint $endpointName exists at destination. Use -Force to drop and migrate." -FunctionName Copy-DbaEndpoint -ModuleName "dbatools"
                    }
                    continue
                } else {
                    if ($__realCmdlet.ShouldProcess($destinstance, "Dropping server endpoint $endpointName and recreating.")) {
                        try {
                            Write-Message -Level Verbose -Message "Dropping server endpoint $endpointName." -FunctionName Copy-DbaEndpoint -ModuleName "dbatools"
                            $destServer.Endpoints[$endpointName].Drop()
                        } catch {
                            $copyEndpointStatus.Status = "Failed"
                            $copyEndpointStatus.Notes = (Get-ErrorMessage -Record $_)
                            $copyEndpointStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                            Write-Message -Level Verbose -Message "Issue dropping server endpoint $endpointName on $destinstance | $PSItem" -FunctionName Copy-DbaEndpoint -ModuleName "dbatools"
                            continue
                        }
                    }
                }
            }

            if ($__realCmdlet.ShouldProcess($destinstance, "Creating server endpoint $endpointName.")) {
                try {
                    Write-Message -Level Verbose -Message "Copying server endpoint $endpointName." -FunctionName Copy-DbaEndpoint -ModuleName "dbatools"
                    $destServer.Query($currentEndpoint.Script()) | Out-Null
                    $copyEndpointStatus.Status = "Successful"
                    $copyEndpointStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                } catch {
                    $copyEndpointStatus.Status = "Failed"
                    $copyEndpointStatus.Notes = (Get-ErrorMessage -Record $_)
                    $copyEndpointStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                    Write-Message -Level Verbose -Message "Issue creating server endpoint $endpointName on $destinstance | $PSItem" -FunctionName Copy-DbaEndpoint -ModuleName "dbatools"
                    continue
                }
            }
        }
    }
} $Source $SourceSqlCredential $Destination $DestinationSqlCredential $Endpoint $ExcludeEndpoint $Force $EnableException $__realCmdlet $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
