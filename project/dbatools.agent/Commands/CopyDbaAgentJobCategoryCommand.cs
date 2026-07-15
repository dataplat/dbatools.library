#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Copies custom SQL Agent job, alert, and operator categories. Port of
/// public/Copy-DbaAgentJobCategory.ps1 (W2-003). The complete workflow remains a
/// module-scoped PowerShell compatibility hop so SMO scripting, filtering, result decoration,
/// and the legacy AgentCategory/AlertCategory naming quirk retain the retired function's engine
/// semantics. The compiled cmdlet supplies the real ShouldProcess runtime. Surface pinned by
/// migration/baselines/Copy-DbaAgentJobCategory.json.
/// </summary>
[Cmdlet(VerbsCommon.Copy, "DbaAgentJobCategory", DefaultParameterSetName = "Default",
    SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Medium)]
public sealed class CopyDbaAgentJobCategoryCommand : DbaBaseCmdlet
{
    /// <summary>The source SQL Server instance.</summary>
    [Parameter(Mandatory = true)]
    public DbaInstanceParameter Source { get; set; } = null!;

    /// <summary>Alternative credential for the source instance.</summary>
    [Parameter]
    public PSCredential? SourceSqlCredential { get; set; }

    /// <summary>The destination SQL Server instances.</summary>
    [Parameter(Mandatory = true)]
    public DbaInstanceParameter[] Destination { get; set; } = null!;

    /// <summary>Alternative credential for destination instances.</summary>
    [Parameter]
    public PSCredential? DestinationSqlCredential { get; set; }

    /// <summary>Category types to copy.</summary>
    [Parameter(ParameterSetName = "SpecificAlerts")]
    [ValidateSet("Job", "Alert", "Operator")]
    public string[]? CategoryType { get; set; }

    /// <summary>Job category names to copy.</summary>
    [Parameter]
    public string[]? JobCategory { get; set; }

    /// <summary>Alert category names exposed by the legacy surface.</summary>
    [Parameter]
    public string[]? AgentCategory { get; set; }

    /// <summary>Operator category names to copy.</summary>
    [Parameter]
    public string[]? OperatorCategory { get; set; }

    /// <summary>Drop and recreate categories that already exist.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BodyScript,
            Source, SourceSqlCredential, Destination, DestinationSqlCredential,
            CategoryType, JobCategory, AgentCategory, OperatorCategory,
            Force.ToBool(), EnableException.ToBool(), this,
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
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
        }
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

    private const string BodyScript = """
param($Source, $SourceSqlCredential, $Destination, $DestinationSqlCredential, $CategoryType, $JobCategory, $AgentCategory, $OperatorCategory, $Force, $EnableException, $__realCmdlet, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter]$Source, $SourceSqlCredential, [Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$Destination, $DestinationSqlCredential, [string[]]$CategoryType, [string[]]$JobCategory, [string[]]$AgentCategory, [string[]]$OperatorCategory, $Force, $EnableException, $__realCmdlet, $__boundVerbose, $__boundDebug)

    function Copy-JobCategory {
        param ([string[]]$jobCategories)
        process {
            $serverJobCategories = $sourceServer.JobServer.JobCategories | Where-Object ID -ge 100
            $destJobCategories = $destServer.JobServer.JobCategories | Where-Object ID -ge 100

            foreach ($jobCategory in $serverJobCategories) {
                $categoryName = $jobCategory.Name
                $copyJobCategoryStatus = [PSCustomObject]@{
                    SourceServer      = $sourceServer.Name
                    DestinationServer = $destServer.Name
                    Name              = $categoryName
                    Type              = "Agent Job Category"
                    Status            = $null
                    Notes             = $null
                    DateTime          = [Dataplat.Dbatools.Utility.DbaDateTime](Get-Date)
                }

                if ($jobCategories.Count -gt 0 -and $jobCategories -notcontains $categoryName) { continue }

                if ($destJobCategories.Name -contains $jobCategory.name) {
                    if ($force -eq $false) {
                        if ($__realCmdlet.ShouldProcess($destinstance, "Job category $categoryName exists at destination. Use -Force to drop and migrate.")) {
                            $copyJobCategoryStatus.Status = "Skipped"
                            $copyJobCategoryStatus.Notes = "Already exists on destination"
                            $copyJobCategoryStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                            Write-Message -Level Verbose -Message "Job category $categoryName exists at destination. Use -Force to drop and migrate." -FunctionName Copy-DbaAgentJobCategory
                        }
                        continue
                    } else {
                        if ($__realCmdlet.ShouldProcess($destinstance, "Dropping job category $categoryName")) {
                            try {
                                Write-Message -Level Verbose -Message "Dropping Job category $categoryName" -FunctionName Copy-DbaAgentJobCategory
                                $destServer.JobServer.JobCategories[$categoryName].Drop()
                            } catch {
                                $copyJobCategoryStatus.Status = "Failed"
                                $copyJobCategoryStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                                Write-Message -Level Verbose -Message "Issue dropping job category $categoryName on $destinstance | $PSItem" -FunctionName Copy-DbaAgentJobCategory
                                continue
                            }
                        }
                    }
                }

                if ($__realCmdlet.ShouldProcess($destinstance, "Creating Job category $categoryName")) {
                    try {
                        Write-Message -Level Verbose -Message "Copying Job category $categoryName" -FunctionName Copy-DbaAgentJobCategory
                        $sql = $jobCategory.Script() | Out-String
                        Write-Message -Level Debug -Message "SQL Statement: $sql" -FunctionName Copy-DbaAgentJobCategory
                        $destServer.Query($sql)
                        $copyJobCategoryStatus.Status = "Successful"
                        $copyJobCategoryStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                    } catch {
                        $copyJobCategoryStatus.Status = "Failed"
                        $copyJobCategoryStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                        Write-Message -Level Verbose -Message "Issue copying job category $categoryName on $destinstance | $PSItem" -FunctionName Copy-DbaAgentJobCategory
                        continue
                    }
                }
            }
        }
    }

    function Copy-OperatorCategory {
        [CmdletBinding(DefaultParameterSetName = "Default", SupportsShouldProcess)]
        param ([string[]]$operatorCategories)
        process {
            $serverOperatorCategories = $sourceServer.JobServer.OperatorCategories | Where-Object ID -ge 100
            $destOperatorCategories = $destServer.JobServer.OperatorCategories | Where-Object ID -ge 100

            foreach ($operatorCategory in $serverOperatorCategories) {
                $categoryName = $operatorCategory.Name
                $copyOperatorCategoryStatus = [PSCustomObject]@{
                    SourceServer      = $sourceServer.Name
                    DestinationServer = $destServer.Name
                    Type              = "Agent Operator Category"
                    Name              = $categoryName
                    Status            = $null
                    Notes             = $null
                    DateTime          = [Dataplat.Dbatools.Utility.DbaDateTime](Get-Date)
                }

                if ($operatorCategories.Count -gt 0 -and $operatorCategories -notcontains $categoryName) { continue }

                if ($destOperatorCategories.Name -contains $operatorCategory.Name) {
                    if ($force -eq $false) {
                        if ($__realCmdlet.ShouldProcess($destinstance, "Operator category $categoryName exists at destination. Use -Force to drop and migrate.")) {
                            $copyOperatorCategoryStatus.Status = "Skipped"
                            $copyOperatorCategoryStatus.Notes = "Already exists on destination"
                            $copyOperatorCategoryStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                            Write-Message -Level Verbose -Message "Operator category $categoryName exists at destination. Use -Force to drop and migrate." -FunctionName Copy-DbaAgentJobCategory
                        }
                        continue
                    } else {
                        if ($__realCmdlet.ShouldProcess($destinstance, "Dropping operator category $categoryName and recreating")) {
                            try {
                                Write-Message -Level Verbose -Message "Dropping Operator category $categoryName" -FunctionName Copy-DbaAgentJobCategory
                                $destServer.JobServer.OperatorCategories[$categoryName].Drop()
                                Write-Message -Level Verbose -Message "Copying Operator category $categoryName" -FunctionName Copy-DbaAgentJobCategory
                                $sql = $operatorCategory.Script() | Out-String
                                Write-Message -Level Debug -Message $sql -FunctionName Copy-DbaAgentJobCategory
                                $destServer.Query($sql)
                            } catch {
                                $copyOperatorCategoryStatus.Status = "Failed"
                                $copyOperatorCategoryStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                                Write-Message -Level Verbose -Message "Issue dropping operator category $categoryName on $destinstance | $PSItem" -FunctionName Copy-DbaAgentJobCategory
                                continue
                            }
                        }
                    }
                } else {
                    if ($__realCmdlet.ShouldProcess($destinstance, "Creating Operator category $categoryName")) {
                        try {
                            Write-Message -Level Verbose -Message "Copying Operator category $categoryName" -FunctionName Copy-DbaAgentJobCategory
                            $sql = $operatorCategory.Script() | Out-String
                            Write-Message -Level Debug -Message $sql -FunctionName Copy-DbaAgentJobCategory
                            $destServer.Query($sql)
                            $copyOperatorCategoryStatus.Status = "Successful"
                            $copyOperatorCategoryStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                        } catch {
                            $copyOperatorCategoryStatus.Status = "Failed"
                            $copyOperatorCategoryStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                            Write-Message -Level Verbose -Message "Issue copying operator category $categoryName on $destinstance | $PSItem" -FunctionName Copy-DbaAgentJobCategory
                            continue
                        }
                    }
                }
            }
        }
    }

    function Copy-AlertCategory {
        [CmdletBinding(DefaultParameterSetName = "Default", SupportsShouldProcess)]
        param ([string[]]$AlertCategories)
        process {
            if ($sourceServer.VersionMajor -lt 9 -or $destServer.VersionMajor -lt 9) {
                throw "Server AlertCategories are only supported in SQL Server 2005 and above. Quitting."
            }

            $serverAlertCategories = $sourceServer.JobServer.AlertCategories | Where-Object ID -ge 100
            $destAlertCategories = $destServer.JobServer.AlertCategories | Where-Object ID -ge 100

            foreach ($alertCategory in $serverAlertCategories) {
                $categoryName = $alertCategory.Name
                $copyAlertCategoryStatus = [PSCustomObject]@{
                    SourceServer      = $sourceServer.Name
                    DestinationServer = $destServer.Name
                    Type              = "Agent Alert Category"
                    Name              = $categoryName
                    Status            = $null
                    Notes             = $null
                    DateTime          = [Dataplat.Dbatools.Utility.DbaDateTime](Get-Date)
                }

                if ($alertCategories.Length -gt 0 -and $alertCategories -notcontains $categoryName) { continue }

                if ($destAlertCategories.Name -contains $alertCategory.name) {
                    if ($force -eq $false) {
                        if ($__realCmdlet.ShouldProcess($destinstance, "Alert category $categoryName exists at destination. Use -Force to drop and migrate.")) {
                            $copyAlertCategoryStatus.Status = "Skipped"
                            $copyAlertCategoryStatus.Notes = "Already exists on destination"
                            $copyAlertCategoryStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                            Write-Message -Level Verbose -Message "Alert category $categoryName exists at destination. Use -Force to drop and migrate." -FunctionName Copy-DbaAgentJobCategory
                        }
                        continue
                    } else {
                        if ($__realCmdlet.ShouldProcess($destinstance, "Dropping alert category $categoryName and recreating")) {
                            try {
                                Write-Message -Level Verbose -Message "Dropping Alert category $categoryName" -FunctionName Copy-DbaAgentJobCategory
                                $destServer.JobServer.AlertCategories[$categoryName].Drop()
                                Write-Message -Level Verbose -Message "Copying Alert category $categoryName" -FunctionName Copy-DbaAgentJobCategory
                                $sql = $alertcategory.Script() | Out-String
                                Write-Message -Level Debug -Message "SQL Statement: $sql" -FunctionName Copy-DbaAgentJobCategory
                                $destServer.Query($sql)
                            } catch {
                                $copyAlertCategoryStatus.Status = "Failed"
                                $copyAlertCategoryStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                                Write-Message -Level Verbose -Message "Issue dropping alert category $categoryName on $destinstance | $PSItem" -FunctionName Copy-DbaAgentJobCategory
                                continue
                            }
                        }
                    }
                } else {
                    if ($__realCmdlet.ShouldProcess($destinstance, "Creating Alert category $categoryName")) {
                        try {
                            Write-Message -Level Verbose -Message "Copying Alert category $categoryName" -FunctionName Copy-DbaAgentJobCategory
                            $sql = $alertCategory.Script() | Out-String
                            Write-Message -Level Debug -Message $sql -FunctionName Copy-DbaAgentJobCategory
                            $destServer.Query($sql)
                            $copyAlertCategoryStatus.Status = "Successful"
                            $copyAlertCategoryStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                        } catch {
                            $copyAlertCategoryStatus.Status = "Failed"
                            $copyAlertCategoryStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                            Write-Message -Level Verbose -Message "Issue creating alert category $categoryName on $destinstance | $PSItem" -FunctionName Copy-DbaAgentJobCategory
                            continue
                        }
                    }
                }
            }
        }
    }

    try {
        $sourceServer = Connect-DbaInstance -SqlInstance $Source -SqlCredential $SourceSqlCredential
    } catch {
        Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $Source -FunctionName Copy-DbaAgentJobCategory
        return
    }

    if ($Force) { $ConfirmPreference = 'none' }
    if (Test-FunctionInterrupt) { return }

    foreach ($destinstance in $Destination) {
        try {
            $destServer = Connect-DbaInstance -SqlInstance $destinstance -SqlCredential $DestinationSqlCredential
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $destinstance -Continue -FunctionName Copy-DbaAgentJobCategory
        }

        if ($CategoryType.count -gt 0) {
            switch ($CategoryType) {
                "Job" { Copy-JobCategory }
                "Alert" { Copy-AlertCategory }
                "Operator" { Copy-OperatorCategory }
            }
            continue
        }

        # The original public function exposes AgentCategory but reads AlertCategory here.
        # Preserve that legacy no-op surface exactly.
        if (($OperatorCategory.Count + $AlertCategory.Count + $jobCategory.Count) -gt 0) {
            if ($OperatorCategory.Count -gt 0) { Copy-OperatorCategory -OperatorCategories $OperatorCategory }
            if ($AlertCategory.Count -gt 0) { Copy-AlertCategory -AlertCategories $AlertCategory }
            if ($jobCategory.Count -gt 0) { Copy-JobCategory -JobCategories $jobCategory }
            continue
        }

        Copy-OperatorCategory
        Copy-AlertCategory
        Copy-JobCategory
    }
} $Source $SourceSqlCredential $Destination $DestinationSqlCredential $CategoryType $JobCategory $AgentCategory $OperatorCategory $Force $EnableException $__realCmdlet $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
