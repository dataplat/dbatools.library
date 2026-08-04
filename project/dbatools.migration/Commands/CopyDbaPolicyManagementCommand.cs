#nullable enable

using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Copies Policy-Based Management categories, conditions, object sets and policies between
/// instances. Port of public/Copy-DbaPolicyManagement.ps1. The whole workflow rides one
/// module-scoped PowerShell hop: it needs the private Add-PbmLibrary to Add-Type the DMF
/// assemblies before any PolicyStore type can resolve, the module-scope $script:isWindows
/// platform flag, the private Get-ErrorMessage helper and Select-DefaultView decoration. The
/// compiled cmdlet supplies the real ShouldProcess runtime. Surface pinned by
/// migration/baselines/Copy-DbaPolicyManagement.json.
/// </summary>
[Cmdlet(VerbsCommon.Copy, "DbaPolicyManagement", DefaultParameterSetName = "Default",
    SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Medium)]
public sealed class CopyDbaPolicyManagementCommand : DbaBaseCmdlet
{
    /// <summary>Source SQL Server instance. Requires sysadmin and SQL Server 2008 or higher.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    public DbaInstanceParameter Source { get; set; } = null!;

    /// <summary>Alternative credential for the source instance.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SourceSqlCredential { get; set; }

    /// <summary>Destination SQL Server instances. Requires sysadmin and SQL Server 2008 or higher.</summary>
    [Parameter(Mandatory = true, Position = 2)]
    public DbaInstanceParameter[] Destination { get; set; } = null!;

    /// <summary>Alternative credential for destination instances.</summary>
    [Parameter(Position = 3)]
    public PSCredential? DestinationSqlCredential { get; set; }

    /// <summary>Only copy the policies with these names.</summary>
    [Parameter(Position = 4)]
    public object[]? Policy { get; set; }

    /// <summary>Skip the policies with these names.</summary>
    [Parameter(Position = 5)]
    public object[]? ExcludePolicy { get; set; }

    /// <summary>Only copy the conditions with these names.</summary>
    [Parameter(Position = 6)]
    public object[]? Condition { get; set; }

    /// <summary>Skip the conditions with these names.</summary>
    [Parameter(Position = 7)]
    public object[]? ExcludeCondition { get; set; }

    /// <summary>Drop and recreate conditions and policies that already exist on the destination.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // Begin and process merge into one hop and no local survives between records: no parameter
    // takes pipeline input, so ProcessRecord runs exactly once. The source connection, the
    // PolicyStore reads and $ConfirmPreference are all re-created inside the same invocation that
    // consumes them. Test-FunctionInterrupt still earns its line, because the begin half's
    // platform guard and source connect have to stop the destination loop that follows them.
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
            Policy, ExcludePolicy, Condition, ExcludeCondition, Force.ToBool(),
            EnableException.ToBool(), this, NestedCommand.BoundCommonParameter(this, "Verbose"),
            NestedCommand.BoundCommonParameter(this, "Debug"));
    }

    private const string BodyScript = """
param($Source, $SourceSqlCredential, $Destination, $DestinationSqlCredential, $Policy, $ExcludePolicy, $Condition, $ExcludeCondition, $Force, $EnableException, $__realCmdlet, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    # $Force is deliberately untyped: PowerShell excludes [switch] parameters from positional
    # binding, so one typed flag would shift every argument after it. It arrives as a real boolean,
    # which the -eq $false tests below read the way a switch reads.
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter]$Source, $SourceSqlCredential, [Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$Destination, $DestinationSqlCredential, [object[]]$Policy, [object[]]$ExcludePolicy, [object[]]$Condition, [object[]]$ExcludeCondition, $Force, $EnableException, $__realCmdlet, $__boundVerbose, $__boundDebug)

    if (-not $script:isWindows) {
        Stop-Function -Message "Copy-DbaPolicyManagement does not support Linux - we're still waiting for the Core SMOs from Microsoft" -FunctionName Copy-DbaPolicyManagement
        return
    }

    Add-PbmLibrary
    try {
        $sourceServer = Connect-DbaInstance -SqlInstance $Source -SqlCredential $SourceSqlCredential -MinimumVersion 10
    } catch {
        Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $Source -FunctionName Copy-DbaPolicyManagement
        return
    }
    $sourceSqlConn = $sourceServer.ConnectionContext.SqlConnectionObject
    $sourceSqlStoreConnection = New-Object Microsoft.SqlServer.Management.Sdk.Sfc.SqlStoreConnection $sourceSqlConn
    $sourceStore = New-Object  Microsoft.SqlServer.Management.DMF.PolicyStore $sourceSqlStoreConnection
    $storePolicies = $sourceStore.Policies | Where-Object { $_.IsSystemObject -eq $false }
    $storeConditions = $sourceStore.Conditions | Where-Object { $_.IsSystemObject -eq $false }
    $storeObjectSets = $sourceStore.ObjectSets | Where-Object { $_.IsSystemObject -eq $false }

    if ($Force) { $ConfirmPreference = 'none' }

    if (Test-FunctionInterrupt) { return }
    foreach ($destinstance in $Destination) {
        try {
            $destServer = Connect-DbaInstance -SqlInstance $destinstance -SqlCredential $DestinationSqlCredential -MinimumVersion 10
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $destinstance -Continue -FunctionName Copy-DbaPolicyManagement
        }
        $destSqlConn = $destServer.ConnectionContext.SqlConnectionObject
        $destSqlStoreConnection = New-Object Microsoft.SqlServer.Management.Sdk.Sfc.SqlStoreConnection $destSqlConn
        $destStore = New-Object  Microsoft.SqlServer.Management.DMF.PolicyStore $destSqlStoreConnection

        $storePoliciesToCopy = $storePolicies
        $storeConditionsToCopy = $storeConditions
        $storeObjectSetsToCopy = $storeObjectSets

        if ($Policy) {
            $storePoliciesToCopy = $storePoliciesToCopy | Where-Object Name -In $Policy
        }
        if ($ExcludePolicy) {
            $storePoliciesToCopy = $storePoliciesToCopy | Where-Object Name -NotIn $ExcludePolicy
        }
        if ($Condition) {
            $storeConditionsToCopy = $storeConditionsToCopy | Where-Object Name -In $Condition
        }
        if ($ExcludeCondition) {
            $storeConditionsToCopy = $storeConditionsToCopy | Where-Object Name -NotIn $ExcludeCondition
        }

        if ($Policy -and $Condition) {
            $storeConditionsToCopy = $null
            $storePoliciesToCopy = $null
        }

        if ($Policy -or $ExcludePolicy) {
            $requiredObjectSets = $storePoliciesToCopy |
                Select-Object -ExpandProperty ObjectSet -Unique |
                Where-Object { $PSItem }
            $storeObjectSetsToCopy = $storeObjectSetsToCopy | Where-Object Name -In $requiredObjectSets
        }

        <#
                        Categories
        #>

        Write-Message -Level Verbose -Message "Migrating categories" -FunctionName Copy-DbaPolicyManagement -ModuleName "dbatools"
        $uniquePolicyCategories = $storePoliciesToCopy | Select-Object -ExpandProperty PolicyCategory -Unique
        $storeCategories = $sourceStore.PolicyCategories | Where-Object { $_.Name -in $uniquePolicyCategories }
        foreach ($category in $storeCategories) {
            $categoryName = $category.Name

            $copyCategoryStatus = [PSCustomObject]@{
                SourceServer      = $sourceServer.Name
                DestinationServer = $destServer.Name
                Name              = $categoryName
                Type              = "Policy Category"
                Status            = $null
                Notes             = $null
                DateTime          = [Dataplat.Dbatools.Utility.DbaDateTime](Get-Date)
            }

            if ($null -ne $destStore.PolicyCategories['Database']) {
                if ($__realCmdlet.ShouldProcess($destinstance, "Policy category '$categoryName' was skipped because it already exists on $destination")) {
                    Write-Message -Level Verbose -Message "Policy category '$categoryName' was skipped because it already exists on $destination." -FunctionName Copy-DbaPolicyManagement -ModuleName "dbatools"
                    $copyCategoryStatus.Status = "Skipped"
                    $copyCategoryStatus.Notes = "Already exists on destination"
                    $copyCategoryStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                }
                continue
            }

            if ($__realCmdlet.ShouldProcess($destination, "Migrating policy category $categoryName") -and $copyCategoryStatus.Status -ne 'Skipped') {
                try {
                    $sql = $category.ScriptCreate().GetScript() | Out-String
                    Write-Message -Level Debug -Message $sql -FunctionName Copy-DbaPolicyManagement -ModuleName "dbatools"
                    Write-Message -Level Verbose -Message "Copying policy category $categoryName" -FunctionName Copy-DbaPolicyManagement -ModuleName "dbatools"
                    $null = $destServer.Query($sql)
                    $destStore.PolicyCategories.Refresh()
                    $copyCategoryStatus.Status = "Successful"
                    $copyCategoryStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                } catch {
                    $copyCategoryStatus.Status = "Failed"
                    $copyCategoryStatus.Notes = $_.Exception.Message
                    $copyCategoryStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                    Write-Message -Level Verbose -Message "Issue copying policy category $categoryName on $destinstance | $PSItem" -FunctionName Copy-DbaPolicyManagement -ModuleName "dbatools"
                    continue
                }
            }
        }

        <#
                    Conditions
        #>

        Write-Message -Level Verbose -Message "Migrating conditions" -FunctionName Copy-DbaPolicyManagement -ModuleName "dbatools"
        foreach ($condition in $storeConditionsToCopy) {
            $conditionName = $condition.Name

            $copyConditionStatus = [PSCustomObject]@{
                SourceServer      = $sourceServer.Name
                DestinationServer = $destServer.Name
                Name              = $conditionName
                Type              = "Policy Condition"
                Status            = $null
                Notes             = $null
                DateTime          = [Dataplat.Dbatools.Utility.DbaDateTime](Get-Date)
            }

            if ($null -ne $destStore.Conditions[$conditionName]) {
                if ($force -eq $false) {
                    if ($__realCmdlet.ShouldProcess($destinstance, "Condition '$conditionName' was skipped because it already exists on $destinstance. Use -Force to drop and recreate")) {
                        Write-Message -Level Verbose -Message "condition '$conditionName' was skipped because it already exists on $destinstance. Use -Force to drop and recreate" -FunctionName Copy-DbaPolicyManagement -ModuleName "dbatools"
                        $copyConditionStatus.Status = "Skipped"
                        $copyConditionStatus.Notes = "Already exists on destination"
                        $copyConditionStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                    }
                    continue
                } else {
                    if ($__realCmdlet.ShouldProcess($destinstance, "Attempting to drop policy condition $conditionName")) {
                        Write-Message -Level Verbose -Message "Condition '$conditionName' exists on $destinstance. Force specified. Dropping $conditionName." -FunctionName Copy-DbaPolicyManagement -ModuleName "dbatools"

                        try {
                            $dependentPolicies = $destStore.Conditions[$conditionName].EnumDependentPolicies()
                            foreach ($dependent in $dependentPolicies) {
                                $dependent.Drop()
                                $destStore.Conditions.Refresh()
                            }
                            $destStore.Conditions[$conditionName].Drop()
                        } catch {
                            $copyConditionStatus.Status = "Failed"
                            $copyConditionStatus.Notes = (Get-ErrorMessage -Record $_).Message
                            $copyConditionStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                            Write-Message -Level Verbose -Message "Issue dropping policy condition $conditionName on $destinstance | $PSItem" -FunctionName Copy-DbaPolicyManagement -ModuleName "dbatools"
                            continue
                        }
                    }
                }
            }

            if ($__realCmdlet.ShouldProcess($destinstance, "Migrating condition $conditionName")) {
                try {
                    $sql = $condition.ScriptCreate().GetScript() | Out-String
                    Write-Message -Level Debug -Message $sql -FunctionName Copy-DbaPolicyManagement -ModuleName "dbatools"
                    Write-Message -Level Verbose -Message "Copying condition $conditionName" -FunctionName Copy-DbaPolicyManagement -ModuleName "dbatools"
                    $null = $destServer.Query($sql)
                    $destStore.Conditions.Refresh()

                    $copyConditionStatus.Status = "Successful"
                    $copyConditionStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                } catch {
                    $copyConditionStatus.Status = "Failed"
                    $copyConditionStatus.Notes = (Get-ErrorMessage -Record $_).Message
                    $copyConditionStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                    Write-Message -Level Verbose -Message "Issue creating policy condition $conditionName on $destinstance | $PSItem" -FunctionName Copy-DbaPolicyManagement -ModuleName "dbatools"
                    continue
                }
            }
        }

        <#
                    ObjectSets
        #>

        Write-Message -Level Verbose -Message "Migrating object sets" -FunctionName Copy-DbaPolicyManagement -ModuleName "dbatools"
        foreach ($objectSet in $storeObjectSetsToCopy) {
            $objectSetName = $objectSet.Name

            $copyObjectSetStatus = [PSCustomObject]@{
                SourceServer      = $sourceServer.Name
                DestinationServer = $destServer.Name
                Name              = $objectSetName
                Type              = "Policy ObjectSet"
                Status            = $null
                Notes             = $null
                DateTime          = [Dataplat.Dbatools.Utility.DbaDateTime](Get-Date)
            }

            if ($null -ne $destStore.ObjectSets[$objectSetName]) {
                if ($__realCmdlet.ShouldProcess($destinstance, "ObjectSet '$objectSetName' was skipped because it already exists on $destinstance")) {
                    Write-Message -Level Verbose -Message "ObjectSet '$objectSetName' was skipped because it already exists on $destinstance." -FunctionName Copy-DbaPolicyManagement -ModuleName "dbatools"
                    $copyObjectSetStatus.Status = "Skipped"
                    $copyObjectSetStatus.Notes = "Already exists on destination"
                    $copyObjectSetStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                }
                continue
            }

            if ($__realCmdlet.ShouldProcess($destinstance, "Migrating object set $objectSetName")) {
                try {
                    $sql = $objectSet.ScriptCreate().GetScript() | Out-String
                    Write-Message -Level Debug -Message $sql -FunctionName Copy-DbaPolicyManagement -ModuleName "dbatools"
                    Write-Message -Level Verbose -Message "Copying object set $objectSetName" -FunctionName Copy-DbaPolicyManagement -ModuleName "dbatools"
                    $null = $destServer.Query($sql)
                    $destStore.ObjectSets.Refresh()

                    $copyObjectSetStatus.Status = "Successful"
                    $copyObjectSetStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                } catch {
                    $copyObjectSetStatus.Status = "Failed"
                    $copyObjectSetStatus.Notes = (Get-ErrorMessage -Record $_).Message
                    $copyObjectSetStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                    Write-Message -Level Verbose -Message "Issue creating object set $objectSetName on $destinstance | $PSItem" -FunctionName Copy-DbaPolicyManagement -ModuleName "dbatools"
                    continue
                }
            }
        }

        <#
                    Policies
        #>

        Write-Message -Level Verbose -Message "Migrating policies" -FunctionName Copy-DbaPolicyManagement -ModuleName "dbatools"
        foreach ($policy in $storePoliciesToCopy) {
            $policyName = $policy.Name

            $copyPolicyStatus = [PSCustomObject]@{
                SourceServer      = $sourceServer.Name
                DestinationServer = $destServer.Name
                Name              = $policyName
                Type              = "Policy"
                Status            = $null
                Notes             = $null
                DateTime          = [Dataplat.Dbatools.Utility.DbaDateTime](Get-Date)
            }

            if ($null -ne $destStore.Policies[$policyName]) {
                if ($force -eq $false) {
                    if ($__realCmdlet.ShouldProcess($destinstance, "Policy '$policyName' was skipped because it already exists on $destinstance. Use -Force to drop and recreate")) {
                        Write-Message -Level Verbose -Message "Policy '$policyName' was skipped because it already exists on $destinstance. Use -Force to drop and recreate" -FunctionName Copy-DbaPolicyManagement -ModuleName "dbatools"

                        $copyPolicyStatus.Status = "Skipped"
                        $copyPolicyStatus.Notes = "Already exists on destination"
                        $copyPolicyStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                    }
                    continue
                } else {
                    if ($__realCmdlet.ShouldProcess($destinstance, "Attempting to drop $policyName")) {
                        Write-Message -Level Verbose -Message "Policy '$policyName' exists on $destinstance. Force specified. Dropping $policyName." -FunctionName Copy-DbaPolicyManagement -ModuleName "dbatools"

                        try {
                            $destStore.Policies[$policyName].Drop()
                            $destStore.Policies.refresh()
                        } catch {
                            $copyPolicyStatus.Status = "Failed"
                            $copyPolicyStatus.Notes = (Get-ErrorMessage -Record $_).Message
                            $copyPolicyStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                            Write-Message -Level Verbose -Message "Issue creating policy $policyName on $destinstance | $PSItem" -FunctionName Copy-DbaPolicyManagement -ModuleName "dbatools"
                            continue
                        }
                    }
                }
            }

            if ($__realCmdlet.ShouldProcess($destinstance, "Migrating policy $policyName")) {
                try {
                    $destStore.Conditions.Refresh()
                    $destStore.Policies.Refresh()
                    $sql = $policy.ScriptCreate().GetScript() | Out-String
                    Write-Message -Level Debug -Message $sql -FunctionName Copy-DbaPolicyManagement -ModuleName "dbatools"
                    Write-Message -Level Verbose -Message "Copying policy $policyName" -FunctionName Copy-DbaPolicyManagement -ModuleName "dbatools"
                    $null = $destServer.Query($sql)

                    $copyPolicyStatus.Status = "Successful"
                    $copyPolicyStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                } catch {
                    $copyPolicyStatus.Status = "Failed"
                    $copyPolicyStatus.Notes = (Get-ErrorMessage -Record $_).Message
                    $copyPolicyStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject

                    # This is usually because of a duplicate dependent from above. Just skip for now.
                    # (no idea what the above means)
                    Write-Message -Level Verbose -Message "Issue creating policy $policyName on $destinstance | $PSItem" -FunctionName Copy-DbaPolicyManagement -ModuleName "dbatools"
                    continue
                }
            }
        }
    }
} $Source $SourceSqlCredential $Destination $DestinationSqlCredential $Policy $ExcludePolicy $Condition $ExcludeCondition $Force $EnableException $__realCmdlet $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
