#nullable enable

using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Copies Resource Governor pools, workload groups and the classifier function between instances.
/// Port of public/Copy-DbaResourceGovernor.ps1. The workflow rides one module-scoped PowerShell hop:
/// it leans on Get-DbaRgClassifierFunction, the private Get-ErrorMessage and Select-DefaultView, and
/// on SMO ResourcePool/WorkloadGroup Script() plus Server.Query, so the engine semantics stay where
/// they were. The compiled cmdlet supplies the real ShouldProcess runtime. Surface pinned by
/// migration/baselines/Copy-DbaResourceGovernor.json.
/// </summary>
[Cmdlet(VerbsCommon.Copy, "DbaResourceGovernor", DefaultParameterSetName = "Default",
    SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Medium)]
public sealed class CopyDbaResourceGovernorCommand : DbaBaseCmdlet
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

    /// <summary>Only copy the resource pools with these names.</summary>
    [Parameter(Position = 4)]
    public object[]? ResourcePool { get; set; }

    /// <summary>Skip the resource pools with these names.</summary>
    [Parameter(Position = 5)]
    public object[]? ExcludeResourcePool { get; set; }

    /// <summary>Drop and recreate pools, workload groups and the classifier function that already exist on the destination.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // Begin and process merge into one hop, and there are no carried locals: no parameter takes
    // pipeline input, so ProcessRecord runs exactly once and nothing can survive between records.
    // The merge is what keeps the begin half's $ConfirmPreference = 'none' visible to the
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
            ResourcePool, ExcludeResourcePool, Force.ToBool(),
            EnableException.ToBool(), this, NestedCommand.BoundCommonParameter(this, "Verbose"),
            NestedCommand.BoundCommonParameter(this, "Debug"));
    }

    private const string BodyScript = """
param($Source, $SourceSqlCredential, $Destination, $DestinationSqlCredential, $ResourcePool, $ExcludeResourcePool, $Force, $EnableException, $__realCmdlet, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    # $Force is deliberately untyped: PowerShell excludes [switch] parameters from positional
    # binding, so one typed flag would shift every argument after it. It arrives as a real boolean,
    # which the -eq $false tests below read the way a switch reads.
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter]$Source, $SourceSqlCredential, [Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$Destination, $DestinationSqlCredential, [object[]]$ResourcePool, [object[]]$ExcludeResourcePool, $Force, $EnableException, $__realCmdlet, $__boundVerbose, $__boundDebug)

    if ($Force) { $ConfirmPreference = 'none' }

    try {
        $sourceServer = Connect-DbaInstance -SqlInstance $Source -SqlCredential $SourceSqlCredential -MinimumVersion 10
    } catch {
        Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $Source -FunctionName Copy-DbaResourceGovernor
        return
    }
    $sourceClassifierFunction = Get-DbaRgClassifierFunction -SqlInstance $sourceServer

    foreach ($destinstance in $Destination) {
        try {
            $destServer = Connect-DbaInstance -SqlInstance $destinstance -SqlCredential $DestinationSqlCredential -MinimumVersion 10
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $destinstance -Continue -FunctionName Copy-DbaResourceGovernor
        }
        $destClassifierFunction = Get-DbaRgClassifierFunction -SqlInstance $destServer

        $copyResourceGovSetting = [PSCustomObject]@{
            SourceServer      = $sourceServer.Name
            DestinationServer = $destServer.Name
            Type              = "Resource Governor Settings"
            Name              = "All Settings"
            Status            = $null
            Notes             = $null
            DateTime          = [Dataplat.Dbatools.Utility.DbaDateTime](Get-Date)
        }

        $copyResourceGovClassifierFunc = [PSCustomObject]@{
            SourceServer      = $sourceServer.Name
            DestinationServer = $destServer.Name
            Type              = "Resource Governor Settings"
            Name              = "Classifier Function"
            Status            = $null
            Notes             = $null
            DateTime          = [Dataplat.Dbatools.Utility.DbaDateTime](Get-Date)
        }

        if ($__realCmdlet.ShouldProcess($destinstance, "Updating Resource Governor settings")) {
            if ($destServer.Edition -notmatch 'Enterprise' -and $destServer.Edition -notmatch 'Datacenter' -and $destServer.Edition -notmatch 'Developer') {
                Write-Message -Level Verbose -Message "The resource governor is not available in this edition of SQL Server. You can manipulate resource governor metadata but you will not be able to apply resource governor configuration. Only Enterprise edition of SQL Server supports resource governor." -FunctionName Copy-DbaResourceGovernor -ModuleName "dbatools"
            } else {
                try {
                    Write-Message -Level Verbose -Message "Managing classifier function." -FunctionName Copy-DbaResourceGovernor -ModuleName "dbatools"
                    # ALL IN ONE, NO CONTINUES
                    if (!$sourceClassifierFunction) {
                        $copyResourceGovClassifierFunc.Status = "Skipped"
                        $copyResourceGovClassifierFunc.Notes = $null
                        $copyResourceGovClassifierFunc | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                    } else {
                        $fullyQualifiedFunctionName = $sourceClassifierFunction.Schema + "." + $sourceClassifierFunction.Name

                        if (!$destClassifierFunction) {
                            $destServer = Connect-DbaInstance -SqlInstance $destinstance -SqlCredential $DestinationSqlCredential
                            $destFunction = $destServer.Databases["master"].UserDefinedFunctions[$sourceClassifierFunction.Name]
                            if ($destFunction) {
                                Write-Message -Level Verbose -Message "Dropping the function with the source classifier function name." -FunctionName Copy-DbaResourceGovernor -ModuleName "dbatools"
                                $destFunction.Drop()
                            }

                            Write-Message -Level Verbose -Message "Creating function." -FunctionName Copy-DbaResourceGovernor -ModuleName "dbatools"
                            $script = $sourceClassifierFunction.Script() | Where-Object { $_ -notmatch '^SET QUOTED_IDENTIFIER' -and $_ -notmatch '^SET ANSI_NULLS' }
                            $destServer.Query($script)

                            $sql = "ALTER RESOURCE GOVERNOR WITH (CLASSIFIER_FUNCTION = $fullyQualifiedFunctionName);"
                            Write-Message -Level Debug -Message $sql -FunctionName Copy-DbaResourceGovernor -ModuleName "dbatools"
                            Write-Message -Level Verbose -Message "Mapping Resource Governor classifier function." -FunctionName Copy-DbaResourceGovernor -ModuleName "dbatools"
                            $destServer.Query($sql)

                            $copyResourceGovClassifierFunc.Status = "Successful"
                            $copyResourceGovClassifierFunc.Notes = "The new classifier function has been created"
                            $copyResourceGovClassifierFunc | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject

                            $sql = "ALTER RESOURCE GOVERNOR RECONFIGURE;"
                            Write-Message -Level Debug -Message $sql -FunctionName Copy-DbaResourceGovernor -ModuleName "dbatools"
                            Write-Message -Level Verbose -Message "Reconfiguring Resource Governor." -FunctionName Copy-DbaResourceGovernor -ModuleName "dbatools"
                            $destServer.Query($sql)
                        } else {
                            if ($Force -eq $false) {
                                $copyResourceGovClassifierFunc.Status = "Skipped"
                                $copyResourceGovClassifierFunc.Notes = "Already exists on destination"
                                $copyResourceGovClassifierFunc | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                            } else {

                                $sql = "ALTER RESOURCE GOVERNOR WITH (CLASSIFIER_FUNCTION = NULL);"
                                Write-Message -Level Debug -Message $sql -FunctionName Copy-DbaResourceGovernor -ModuleName "dbatools"
                                Write-Message -Level Verbose -Message "Disabling the Resource Governor." -FunctionName Copy-DbaResourceGovernor -ModuleName "dbatools"
                                $destServer.Query($sql)

                                $sql = "ALTER RESOURCE GOVERNOR RECONFIGURE;"
                                Write-Message -Level Debug -Message $sql -FunctionName Copy-DbaResourceGovernor -ModuleName "dbatools"
                                Write-Message -Level Verbose -Message "Reconfiguring Resource Governor." -FunctionName Copy-DbaResourceGovernor -ModuleName "dbatools"
                                $destServer.Query($sql)

                                Write-Message -Level Verbose -Message "Dropping the destination classifier function." -FunctionName Copy-DbaResourceGovernor -ModuleName "dbatools"
                                $destServer = Connect-DbaInstance -SqlInstance $destinstance -SqlCredential $DestinationSqlCredential
                                $destFunction = $destServer.Databases["master"].UserDefinedFunctions[$sourceClassifierFunction.Name]
                                $destClassifierFunction.Drop()

                                Write-Message -Level Verbose -Message "Re-creating the Resource Governor classifier function." -FunctionName Copy-DbaResourceGovernor -ModuleName "dbatools"
                                $script = $sourceClassifierFunction.Script() | Where-Object { $_ -notmatch '^SET QUOTED_IDENTIFIER' -and $_ -notmatch '^SET ANSI_NULLS' }
                                $destServer.Query($script)

                                $sql = "ALTER RESOURCE GOVERNOR WITH (CLASSIFIER_FUNCTION = $fullyQualifiedFunctionName);"
                                Write-Message -Level Debug -Message $sql -FunctionName Copy-DbaResourceGovernor -ModuleName "dbatools"
                                Write-Message -Level Verbose -Message "Mapping Resource Governor classifier function." -FunctionName Copy-DbaResourceGovernor -ModuleName "dbatools"
                                $destServer.Query($sql)

                                $sql = "ALTER RESOURCE GOVERNOR RECONFIGURE;"
                                Write-Message -Level Debug -Message $sql -FunctionName Copy-DbaResourceGovernor -ModuleName "dbatools"
                                Write-Message -Level Verbose -Message "Reconfiguring Resource Governor." -FunctionName Copy-DbaResourceGovernor -ModuleName "dbatools"
                                $destServer.Query($sql)

                                $copyResourceGovClassifierFunc.Status = "Successful"
                                $copyResourceGovClassifierFunc.Notes = "The old classifier function has been overwritten."
                                $copyResourceGovClassifierFunc | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                            }
                        }
                    }
                } catch {
                    $copyResourceGovSetting.Status = "Failed"
                    $copyResourceGovSetting.Notes = (Get-ErrorMessage -Record $_)
                    $copyResourceGovSetting | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                    $sql = "ALTER RESOURCE GOVERNOR RECONFIGURE;"
                    Write-Message -Level Debug -Message $sql -FunctionName Copy-DbaResourceGovernor -ModuleName "dbatools"
                    Write-Message -Level Verbose -Message "Reconfiguring Resource Governor." -FunctionName Copy-DbaResourceGovernor -ModuleName "dbatools"
                    $destServer.Query($sql)
                    Write-Message -Level Verbose -Message "Issue reconfiguring Resource Governor on $destinstance | $PSItem" -FunctionName Copy-DbaResourceGovernor -ModuleName "dbatools"
                }
            }
        }

        # Pools
        if ($ResourcePool) {
            $pools = $sourceServer.ResourceGovernor.ResourcePools | Where-Object Name -In $ResourcePool
        } elseif ($ExcludeResourcePool) {
            # Assigns $pool, not $pools - so -ExcludeResourcePool leaves $pools unset and the loop
            # below copies nothing. Carried verbatim from the script implementation rather than
            # corrected, because fixing it here would change output the port is meant to preserve.
            $pool = $sourceServer.ResourceGovernor.ResourcePools | Where-Object Name -NotIn $ExcludeResourcePool
        } else {
            $pools = $sourceServer.ResourceGovernor.ResourcePools | Where-Object { $_.Name -notin "internal", "default" }
        }

        Write-Message -Level Verbose -Message "Migrating pools." -FunctionName Copy-DbaResourceGovernor -ModuleName "dbatools"
        foreach ($pool in $pools) {
            $poolName = $pool.Name

            $copyResourceGovPool = [PSCustomObject]@{
                SourceServer      = $sourceServer.Name
                DestinationServer = $destServer.Name
                Type              = "Resource Governor Pool"
                Name              = $poolName
                Status            = $null
                Notes             = $null
                DateTime          = [Dataplat.Dbatools.Utility.DbaDateTime](Get-Date)
            }

            if ($null -ne $destServer.ResourceGovernor.ResourcePools[$poolName]) {
                if ($force -eq $false) {
                    if ($__realCmdlet.ShouldProcess($destinstance, "Pool '$poolName' was skipped because it already exists on $destinstance. Use -Force to drop and recreate.")) {
                        Write-Message -Level Verbose -Message "Pool '$poolName' was skipped because it already exists on $destinstance. Use -Force to drop and recreate." -FunctionName Copy-DbaResourceGovernor -ModuleName "dbatools"
                        $copyResourceGovPool.Status = "Skipped"
                        $copyResourceGovPool.Notes = "Already exists on destination"
                        $copyResourceGovPool | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                    }
                } else {
                    if ($__realCmdlet.ShouldProcess($destinstance, "Attempting to drop $poolName")) {
                        Write-Message -Level Verbose -Message "Pool '$poolName' exists on $destinstance." -FunctionName Copy-DbaResourceGovernor -ModuleName "dbatools"
                        Write-Message -Level Verbose -Message "Force specified. Dropping $poolName." -FunctionName Copy-DbaResourceGovernor -ModuleName "dbatools"

                        try {
                            $destServer = Connect-DbaInstance -SqlInstance $destinstance -SqlCredential $DestinationSqlCredential
                            $destPool = $destServer.ResourceGovernor.ResourcePools[$poolName]
                            $workloadGroups = $destPool.WorkloadGroups
                            foreach ($workloadGroup in $workloadGroups) {
                                $workloadGroup.Drop()
                            }
                            $destPool.Drop()
                            $destServer.ResourceGovernor.Alter()
                        } catch {
                            $copyResourceGovPool.Status = "Failed"
                            $copyResourceGovPool.Notes = (Get-ErrorMessage -Record $_)
                            $copyResourceGovPool | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject

                            Write-Message -Level Verbose -Message "Issue dropping pool $poolName on $destinstance | $PSItem" -FunctionName Copy-DbaResourceGovernor -ModuleName "dbatools"

                            $sql = "ALTER RESOURCE GOVERNOR RECONFIGURE;"
                            Write-Message -Level Debug -Message $sql -FunctionName Copy-DbaResourceGovernor -ModuleName "dbatools"
                            Write-Message -Level Verbose -Message "Reconfiguring Resource Governor." -FunctionName Copy-DbaResourceGovernor -ModuleName "dbatools"
                            $destServer.Query($sql)
                            continue
                        }
                    }
                }
            }

            if ($__realCmdlet.ShouldProcess($destinstance, "Migrating pool $poolName")) {
                try {
                    $sql = $pool.Script() | Out-String
                    Write-Message -Level Debug -Message $sql -FunctionName Copy-DbaResourceGovernor -ModuleName "dbatools"
                    Write-Message -Level Verbose -Message "Copying pool $poolName." -FunctionName Copy-DbaResourceGovernor -ModuleName "dbatools"
                    $destServer.Query($sql)

                    $copyResourceGovPool.Status = "Successful"
                    $copyResourceGovPool | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject

                    $workloadGroups = $pool.WorkloadGroups
                    foreach ($workloadGroup in $workloadGroups) {
                        $workgroupName = $workloadGroup.Name

                        $copyResourceGovWorkGroup = [PSCustomObject]@{
                            SourceServer      = $sourceServer.Name
                            DestinationServer = $destServer.Name
                            Type              = "Resource Governor Pool Workgroup"
                            Name              = $workgroupName
                            Status            = $null
                            Notes             = $null
                            DateTime          = [Dataplat.Dbatools.Utility.DbaDateTime](Get-Date)
                        }

                        $sql = $workloadGroup.Script() | Out-String
                        Write-Message -Level Debug -Message $sql -FunctionName Copy-DbaResourceGovernor -ModuleName "dbatools"
                        Write-Message -Level Verbose -Message "Copying $workgroupName." -FunctionName Copy-DbaResourceGovernor -ModuleName "dbatools"
                        $destServer.Query($sql)

                        $copyResourceGovWorkGroup.Status = "Successful"
                        $copyResourceGovWorkGroup | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject

                        $sql = "ALTER RESOURCE GOVERNOR RECONFIGURE;"
                        Write-Message -Level Debug -Message $sql -FunctionName Copy-DbaResourceGovernor -ModuleName "dbatools"
                        Write-Message -Level Verbose -Message "Reconfiguring Resource Governor." -FunctionName Copy-DbaResourceGovernor -ModuleName "dbatools"
                        $destServer.Query($sql)
                    }
                } catch {
                    # $copyResourceGovWorkGroup and $workgroupName both survive from an earlier
                    # iteration, so a pool that fails before its first workload group reports the
                    # previous group's name. Carried verbatim.
                    if ($copyResourceGovWorkGroup) {
                        $copyResourceGovWorkGroup.Status = "Failed"
                        $copyResourceGovWorkGroup.Notes = (Get-ErrorMessage -Record $_)
                        $copyResourceGovWorkGroup | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                    }
                    Write-Message -Level Verbose -Message "Issue creating $workgroupName on $destinstance | $PSItem" -FunctionName Copy-DbaResourceGovernor -ModuleName "dbatools"
                    continue
                }
            }
        }

        if ($__realCmdlet.ShouldProcess($destinstance, "Finalizing migration by reconfiguring Resource Governor.")) {
            if ($destServer.Edition -notmatch 'Enterprise' -and $destServer.Edition -notmatch 'Datacenter' -and $destServer.Edition -notmatch 'Developer') {
                Write-Message -Level Verbose -Message "The resource governor is not available in this edition of SQL Server. You can manipulate resource governor metadata but you will not be able to apply resource governor configuration. Only Enterprise edition of SQL Server supports resource governor." -FunctionName Copy-DbaResourceGovernor -ModuleName "dbatools"
            } else {

                Write-Message -Level Verbose -Message "Reconfiguring Resource Governor." -FunctionName Copy-DbaResourceGovernor -ModuleName "dbatools"
                try {
                    if (!$sourceServer.ResourceGovernor.Enabled) {
                        $sql = "ALTER RESOURCE GOVERNOR DISABLE"
                        $destServer.Query($sql)

                        $sql = "ALTER RESOURCE GOVERNOR RECONFIGURE;"
                        Write-Message -Level Debug -Message $sql -FunctionName Copy-DbaResourceGovernor -ModuleName "dbatools"
                        Write-Message -Level Verbose -Message "Reconfiguring Resource Governor." -FunctionName Copy-DbaResourceGovernor -ModuleName "dbatools"
                        $destServer.Query($sql)
                    } else {
                        $sql = "ALTER RESOURCE GOVERNOR RECONFIGURE"
                        $destServer.Query($sql)
                    }
                } catch {
                    $altermsg = $_.Exception
                }


                $copyResourceGovReconfig = [PSCustomObject]@{
                    SourceServer      = $sourceServer.Name
                    DestinationServer = $destServer.Name
                    Type              = "Reconfigure Resource Governor"
                    Name              = "Reconfigure Resource Governor"
                    Status            = "Successful"
                    Notes             = $altermsg
                    DateTime          = [Dataplat.Dbatools.Utility.DbaDateTime](Get-Date)
                }
                $copyResourceGovReconfig | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
            }
        }
    }
} $Source $SourceSqlCredential $Destination $DestinationSqlCredential $ResourcePool $ExcludeResourcePool $Force $EnableException $__realCmdlet $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
