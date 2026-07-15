#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;
using SmoAgentJob = Microsoft.SqlServer.Management.Smo.Agent.Job;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Copies SQL Agent jobs and validates their databases, owners, proxies, operators, alerts,
/// schedules, and modification dates. Port of public/Copy-DbaAgentJob.ps1 (W2-002).
/// The complete job workflow remains a module-scoped PowerShell compatibility hop; the compiled
/// cmdlet preserves the advanced function's begin/process pipeline lifetime and supplies the real
/// ShouldProcess runtime. Surface pinned by migration/baselines/Copy-DbaAgentJob.json.
/// </summary>
[Cmdlet(VerbsCommon.Copy, "DbaAgentJob", DefaultParameterSetName = "Default",
    SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Medium)]
public sealed class CopyDbaAgentJobCommand : DbaBaseCmdlet
{
    /// <summary>Source SQL Server instance.</summary>
    [Parameter(Position = 0)]
    public DbaInstanceParameter? Source { get; set; }

    /// <summary>Alternative credential for the source instance.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SourceSqlCredential { get; set; }

    /// <summary>Destination SQL Server instances.</summary>
    [Parameter(Mandatory = true, Position = 2)]
    public DbaInstanceParameter[] Destination { get; set; } = null!;

    /// <summary>Alternative credential for destination instances.</summary>
    [Parameter(Position = 3)]
    public PSCredential? DestinationSqlCredential { get; set; }

    /// <summary>Only copy jobs with these names.</summary>
    [Parameter(Position = 4)]
    public object[]? Job { get; set; }

    /// <summary>Exclude jobs with these names.</summary>
    [Parameter(Position = 5)]
    public object[]? ExcludeJob { get; set; }

    /// <summary>Disable successfully copied jobs on the source.</summary>
    [Parameter]
    public SwitchParameter DisableOnSource { get; set; }

    /// <summary>Disable successfully copied jobs on the destination.</summary>
    [Parameter]
    public SwitchParameter DisableOnDestination { get; set; }

    /// <summary>Overwrite existing jobs and substitute sa for missing owners.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    /// <summary>Rename the copied job.</summary>
    [Parameter(Position = 6)]
    public string? NewName { get; set; }

    /// <summary>Synchronize only when the source modification date is newer.</summary>
    [Parameter]
    public SwitchParameter UseLastModified { get; set; }

    /// <summary>SQL Agent jobs supplied directly or through the pipeline.</summary>
    [Parameter(ValueFromPipeline = true, Position = 7)]
    public SmoAgentJob[]? InputObject { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    private readonly List<SmoAgentJob> _beginJobs = new();
    private bool _beginInterrupted;
    private bool _inputObjectBoundAtBegin;
    private bool _boundJob;
    private bool _boundExcludeJob;
    private bool _boundNewName;

    protected override void BeginProcessing()
    {
        _inputObjectBoundAtBegin = TestBound("InputObject");
        _boundJob = TestBound("Job");
        _boundExcludeJob = TestBound("ExcludeJob");
        _boundNewName = TestBound("NewName");

        bool completed = false;
        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BeginScript,
            Source, SourceSqlCredential, Job, ExcludeJob, NewName, InputObject,
            EnableException.ToBool(), _boundJob, _boundExcludeJob, _boundNewName,
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
            }
            else if (item?.BaseObject is SmoAgentJob job)
            {
                _beginJobs.Add(job);
            }
            else if (item is not null && LanguagePrimitives.IsTrue(
                item.Properties["__CopyDbaAgentJobBeginComplete"]?.Value))
            {
                completed = true;
            }
            else if (item is not null)
            {
                WriteObject(item);
            }
        }
        _beginInterrupted = !completed;
    }

    protected override void ProcessRecord()
    {
        if (_beginInterrupted)
            return;

        // Command-line InputObject is visible in begin and is therefore subject to Source's
        // overwrite. Pipeline binding happens after begin and overwrites the fetched value for
        // that process record, exactly like the advanced function parameter variable.
        SmoAgentJob[] jobs = !_inputObjectBoundAtBegin && InputObject is not null
            ? InputObject
            : _beginJobs.ToArray();

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
            Source, Destination, DestinationSqlCredential, Job, ExcludeJob,
            DisableOnSource.ToBool(), DisableOnDestination.ToBool(), Force.ToBool(), NewName,
            UseLastModified.ToBool(), jobs, EnableException.ToBool(), this,
            _boundJob, _boundExcludeJob, _boundNewName,
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

    private const string BeginScript = """
param($Source, $SourceSqlCredential, $Job, $ExcludeJob, $NewName, $InputObject, $EnableException, $__boundJob, $__boundExcludeJob, $__boundNewName, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter]$Source, $SourceSqlCredential, [object[]]$Job, [object[]]$ExcludeJob, $NewName, [Microsoft.SqlServer.Management.Smo.Agent.Job[]]$InputObject, $EnableException, $__boundJob, $__boundExcludeJob, $__boundNewName, $__boundVerbose, $__boundDebug)

    if ($Source) {
        try {
            $splatGetJob = @{
                SqlInstance   = $Source
                SqlCredential = $SourceSqlCredential
            }
            if ($__boundJob) {
                $splatGetJob['Job'] = $Job
            }
            if ($__boundExcludeJob) {
                $splatGetJob['ExcludeJob'] = $ExcludeJob
            }
            $InputObject = Get-DbaAgentJob @splatGetJob
        } catch {
            Stop-Function -Message "Error occurred while establishing connection to $Source" -Category ConnectionError -ErrorRecord $_ -Target $Source -FunctionName Copy-DbaAgentJob
            return
        }
    }
    if ($__boundNewName -and $InputObject.Count -gt 1) {
        Stop-Function -Message "Cannot use -NewName when copying multiple jobs" -FunctionName Copy-DbaAgentJob
        return
    }
    $InputObject
    [pscustomobject]@{ __CopyDbaAgentJobBeginComplete = $true }
} $Source $SourceSqlCredential $Job $ExcludeJob $NewName $InputObject $EnableException $__boundJob $__boundExcludeJob $__boundNewName $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";

    private const string ProcessScript = """
param($Source, $Destination, $DestinationSqlCredential, $Job, $ExcludeJob, $DisableOnSource, $DisableOnDestination, $Force, $NewName, $UseLastModified, $InputObject, $EnableException, $__realCmdlet, $__boundJob, $__boundExcludeJob, $__boundNewName, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter]$Source, [Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$Destination, $DestinationSqlCredential, [object[]]$Job, [object[]]$ExcludeJob, $DisableOnSource, $DisableOnDestination, $Force, $NewName, $UseLastModified, [Microsoft.SqlServer.Management.Smo.Agent.Job[]]$InputObject, $EnableException, $__realCmdlet, $__boundJob, $__boundExcludeJob, $__boundNewName, $__boundVerbose, $__boundDebug)
    if ($Force) { $ConfirmPreference = 'none' }

    if (Test-FunctionInterrupt) { return }
    foreach ($destinstance in $Destination) {
        try {
            $destServer = Connect-DbaInstance -SqlInstance $destinstance -SqlCredential $DestinationSqlCredential
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $destinstance -Continue -FunctionName Copy-DbaAgentJob
        }
        $destJobs = $destServer.JobServer.Jobs

        foreach ($serverJob in $InputObject) {
            $jobName = $serverJob.Name
            $jobId = $serverJob.JobId
            $sourceserver = $serverJob.Parent.Parent
            $alertsReferencingJob = @()
            $destJobName = if ($__boundNewName) { $NewName } else { $jobName }

            if ($sourceserver.Name -eq $destServer.Name -and -not $__boundNewName) {
                Stop-Function -Message "Source and destination are the same server ($($destServer.Name)). Use -NewName to copy job [$jobName] with a different name on the same server." -Continue -FunctionName Copy-DbaAgentJob
            }

            $copyJobStatus = [PSCustomObject]@{
                SourceServer      = $sourceserver.Name
                DestinationServer = $destServer.Name
                Name              = $destJobName
                Type              = "Agent Job"
                Status            = $null
                Notes             = $null
                DateTime          = [DbaDateTime](Get-Date)
            }

            if ($__boundJob -and $jobName -notin $Job) {
                Write-Message -Level Verbose -Message "Job [$jobName] filtered. Skipping." -FunctionName Copy-DbaAgentJob
                continue
            }
            if ($__boundExcludeJob -and $jobName -in $ExcludeJob) {
                Write-Message -Level Verbose -Message "Job [$jobName] excluded. Skipping." -FunctionName Copy-DbaAgentJob
                continue
            }
            Write-Message -Message "Working on job: $jobName" -Level Verbose -FunctionName Copy-DbaAgentJob
            $sql = "`r`n                SELECT sp.[name] AS MaintenancePlanName`r`n                FROM msdb.dbo.sysmaintplan_plans AS sp`r`n                INNER JOIN msdb.dbo.sysmaintplan_subplans AS sps`r`n                    ON sps.plan_id = sp.id`r`n                WHERE job_id = '$($jobId)'"
            Write-Message -Message $sql -Level Debug -FunctionName Copy-DbaAgentJob

            $MaintenancePlanName = $sourceServer.Query($sql).MaintenancePlanName

            if ($MaintenancePlanName) {
                if ($__realCmdlet.ShouldProcess($destinstance, "Job [$jobName] is associated with Maintenance Plan: $MaintenancePlanNam")) {
                    $copyJobStatus.Status = "Skipped"
                    $copyJobStatus.Notes = "Job is associated with maintenance plan"
                    $copyJobStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                    Write-Message -Level Verbose -Message "Job [$jobName] is associated with Maintenance Plan: $MaintenancePlanName" -FunctionName Copy-DbaAgentJob
                }
                continue
            }

            $dbNames = ($serverJob.JobSteps | Where-Object { $_.SubSystem -notin 'ActiveScripting', 'AnalysisQuery', 'AnalysisCommand' }).DatabaseName | Where-Object { $_.Length -gt 0 }
            $missingDb = $dbNames | Where-Object { $destServer.Databases.Name -notcontains $_ }

            if ($missingDb.Count -gt 0 -and $dbNames.Count -gt 0) {
                if ($__realCmdlet.ShouldProcess($destinstance, "Database(s) $missingDb doesn't exist on destination. Skipping job [$jobName].")) {
                    $missingDb = ($missingDb | Sort-Object | Get-Unique) -join ", "
                    $copyJobStatus.Status = "Skipped"
                    $copyJobStatus.Notes = "Job is dependent on database: $missingDb"
                    $copyJobStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                    Write-Message -Level Verbose -Message "Database(s) $missingDb doesn't exist on destination. Skipping job [$jobName]." -FunctionName Copy-DbaAgentJob
                }
                continue
            }

            $missingLogin = $serverJob.OwnerLoginName | Where-Object { $destServer.Logins.Name -notcontains $_ }

            if ($missingLogin.Count -gt 0) {
                # Secondary check: verify if the owner has access via AD group membership
                $missingLogin = $missingLogin | Where-Object {
                    $ownerName = $_
                    try {
                        $adInfo = $destServer.EnumWindowsUserInfo($ownerName)
                        if ($adInfo.Rows.Count -gt 0) {
                            Write-Message -Level Verbose -Message "Login $ownerName not found as a direct login but has access via AD group membership on destination. Proceeding." -FunctionName Copy-DbaAgentJob
                            $false
                        } else {
                            $true
                        }
                    } catch {
                        Write-Message -Level Verbose -Message "Could not verify AD group membership for $ownerName on destination: $PSItem" -FunctionName Copy-DbaAgentJob
                        $true
                    }
                }
            }

            if ($missingLogin.Count -gt 0) {
                if ($force -eq $false) {
                    if ($__realCmdlet.ShouldProcess($destinstance, "Login(s) $missingLogin doesn't exist on destination. Use -Force to set owner to [sa]. Skipping job [$jobName].")) {
                        $missingLogin = ($missingLogin | Sort-Object | Get-Unique) -join ", "
                        $copyJobStatus.Status = "Skipped"
                        $copyJobStatus.Notes = "Job is dependent on login $missingLogin"
                        $copyJobStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                        Write-Message -Level Verbose -Message "Login(s) $missingLogin doesn't exist on destination. Use -Force to set owner to [sa]. Skipping job [$jobName]." -FunctionName Copy-DbaAgentJob
                    }
                    continue
                }
            }

            $proxyNames = ($serverJob.JobSteps | Where-Object ProxyName).ProxyName
            $missingProxy = $proxyNames | Where-Object { $destServer.JobServer.ProxyAccounts.Name -notcontains $_ }

            if ($missingProxy -and $proxyNames) {
                if ($__realCmdlet.ShouldProcess($destinstance, "Proxy Account(s) $missingProxy doesn't exist on destination. Skipping job [$jobName].")) {
                    $missingProxy = ($missingProxy | Sort-Object | Get-Unique) -join ", "
                    $copyJobStatus.Status = "Skipped"
                    $copyJobStatus.Notes = "Job is dependent on proxy $missingProxy"
                    $copyJobStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                    Write-Message -Level Verbose -Message "Proxy Account(s) $missingProxy doesn't exist on destination. Skipping job [$jobName]." -FunctionName Copy-DbaAgentJob
                }
                continue
            }

            $operators = $serverJob.OperatorToEmail, $serverJob.OperatorToNetSend, $serverJob.OperatorToPage | Where-Object { $_.Length -gt 0 }
            $missingOperators = $operators | Where-Object { $destServer.JobServer.Operators.Name -notcontains $_ }

            if ($missingOperators.Count -gt 0 -and $operators.Count -gt 0) {
                $missingOperator = ($missingOperators | Sort-Object | Get-Unique) -join ", "
                if ($__realCmdlet.ShouldProcess($destinstance, "Operator(s) $($missingOperator) doesn't exist on destination. Skipping job [$jobName]")) {
                    $copyJobStatus.Status = "Skipped"
                    $copyJobStatus.Notes = "Job is dependent on operator $missingOperator"
                    $copyJobStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                    Write-Message -Level Verbose -Message "Operator(s) $($missingOperator) doesn't exist on destination. Skipping job [$jobName]" -FunctionName Copy-DbaAgentJob
                }
                continue
            }

            if ($destJobs.name -contains $destJobName) {
                if ($UseLastModified) {
                    try {
                        # Query date_modified from both source and destination using parameterized queries
                        $splatSourceDate = @{
                            SqlInstance  = $sourceserver
                            Database     = "msdb"
                            Query        = "SELECT date_modified FROM dbo.sysjobs WHERE name = @jobName"
                            SqlParameter = @{ jobName = $jobName }
                        }
                        $sourceDate = (Invoke-DbaQuery @splatSourceDate).date_modified

                        $splatDestDate = @{
                            SqlInstance  = $destServer
                            Database     = "msdb"
                            Query        = "SELECT date_modified FROM dbo.sysjobs WHERE name = @jobName"
                            SqlParameter = @{ jobName = $destJobName }
                        }
                        $destDate = (Invoke-DbaQuery @splatDestDate).date_modified

                        if ($null -eq $sourceDate -or $null -eq $destDate) {
                            Write-Message -Level Warning -Message "Could not retrieve date_modified for job $jobName. Skipping date comparison." -FunctionName Copy-DbaAgentJob
                            if ($force -eq $false) {
                                if ($__realCmdlet.ShouldProcess($destinstance, "Job $jobName exists at destination. Use -Force to drop and migrate.")) {
                                    $copyJobStatus.Status = "Skipped"
                                    $copyJobStatus.Notes = "Already exists on destination"
                                    $copyJobStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                                    Write-Message -Level Verbose -Message "Job $jobName exists at destination. Use -Force to drop and migrate." -FunctionName Copy-DbaAgentJob
                                }
                                continue
                            }
                        } elseif ($sourceDate -gt $destDate) {
                            # Source is newer, proceed with drop and recreate
                            if ($__realCmdlet.ShouldProcess($destinstance, "Source job is newer (modified $sourceDate). Dropping and recreating job $destJobName")) {
                                try {
                                    Write-Message -Message "Source job $jobName is newer. Dropping and recreating $destJobName." -Level Verbose -FunctionName Copy-DbaAgentJob
                                    # Before dropping, save which alerts reference this job
                                    $splatAlertsForJob = @{
                                        SqlInstance  = $destServer
                                        Database     = "msdb"
                                        Query        = "SELECT name FROM dbo.sysalerts WHERE job_id = (SELECT job_id FROM dbo.sysjobs WHERE name = @jobName)"
                                        SqlParameter = @{ jobName = $destJobName }
                                    }
                                    $alertsReferencingJob = (Invoke-DbaQuery @splatAlertsForJob).name
                                    Write-Message -Message "Found $($alertsReferencingJob.Count) alert(s) referencing job $destJobName" -Level Verbose -FunctionName Copy-DbaAgentJob
                                    $destServer.JobServer.Jobs[$destJobName].Drop()
                                } catch {
                                    $copyJobStatus.Status = "Failed"
                                    $copyJobStatus.Notes = (Get-ErrorMessage -Record $_).Message
                                    $copyJobStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                                    Write-Message -Level Verbose -Message "Issue dropping job $jobName on $destinstance | $PSItem" -FunctionName Copy-DbaAgentJob
                                    continue
                                }
                            }
                        } elseif ($sourceDate -eq $destDate) {
                            # Dates are equal, skip
                            if ($__realCmdlet.ShouldProcess($destinstance, "Job $jobName has same modification date. Skipping.")) {
                                $copyJobStatus.Status = "Skipped"
                                $copyJobStatus.Notes = "Job has same modification date on source and destination"
                                $copyJobStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                                Write-Message -Level Verbose -Message "Job $jobName has same modification date ($sourceDate). Skipping." -FunctionName Copy-DbaAgentJob
                            }
                            continue
                        } else {
                            # Destination is newer, skip with warning
                            if ($__realCmdlet.ShouldProcess($destinstance, "Job $jobName is newer on destination. Skipping.")) {
                                $copyJobStatus.Status = "Skipped"
                                $copyJobStatus.Notes = "Destination job is newer than source (dest: $destDate, source: $sourceDate)"
                                $copyJobStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                                Write-Message -Level Warning -Message "Job $jobName is newer on destination ($destDate) than source ($sourceDate). Skipping." -FunctionName Copy-DbaAgentJob
                            }
                            continue
                        }
                    } catch {
                        Write-Message -Level Warning -Message "Error comparing dates for job $jobName | $PSItem" -FunctionName Copy-DbaAgentJob
                        if ($force -eq $false) {
                            if ($__realCmdlet.ShouldProcess($destinstance, "Job $jobName exists at destination. Use -Force to drop and migrate.")) {
                                $copyJobStatus.Status = "Skipped"
                                $copyJobStatus.Notes = "Already exists on destination"
                                $copyJobStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                                Write-Message -Level Verbose -Message "Job $jobName exists at destination. Use -Force to drop and migrate." -FunctionName Copy-DbaAgentJob
                            }
                            continue
                        }
                    }
                } elseif ($force -eq $false) {
                    if ($__realCmdlet.ShouldProcess($destinstance, "Job $jobName exists at destination. Use -Force to drop and migrate.")) {
                        $copyJobStatus.Status = "Skipped"
                        $copyJobStatus.Notes = "Already exists on destination"
                        $copyJobStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                        Write-Message -Level Verbose -Message "Job $jobName exists at destination. Use -Force to drop and migrate." -FunctionName Copy-DbaAgentJob
                    }
                    continue
                } else {
                    if ($__realCmdlet.ShouldProcess($destinstance, "Dropping job $destJobName and recreating")) {
                        try {
                            Write-Message -Message "Dropping Job $destJobName" -Level Verbose -FunctionName Copy-DbaAgentJob
                            # Before dropping, save which alerts reference this job
                            $splatAlertsForJob = @{
                                SqlInstance  = $destServer
                                Database     = "msdb"
                                Query        = "SELECT name FROM dbo.sysalerts WHERE job_id = (SELECT job_id FROM dbo.sysjobs WHERE name = @jobName)"
                                SqlParameter = @{ jobName = $destJobName }
                            }
                            $alertsReferencingJob = (Invoke-DbaQuery @splatAlertsForJob).name
                            Write-Message -Message "Found $($alertsReferencingJob.Count) alert(s) referencing job $destJobName" -Level Verbose -FunctionName Copy-DbaAgentJob
                            $destServer.JobServer.Jobs[$destJobName].Drop()
                        } catch {
                            $copyJobStatus.Status = "Failed"
                            $copyJobStatus.Notes = (Get-ErrorMessage -Record $_).Message
                            $copyJobStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                            Write-Message -Level Verbose -Message "Issue dropping job $jobName on $destinstance | $PSItem" -FunctionName Copy-DbaAgentJob
                            continue
                        }
                    }
                }
            }

            if ($__realCmdlet.ShouldProcess($destinstance, "Creating Job $destJobName")) {
                try {
                    Write-Message -Message "Copying Job $jobName as $destJobName" -Level Verbose -FunctionName Copy-DbaAgentJob
                    $sql = $serverJob.Script() | Out-String

                    if ($missingLogin.Count -gt 0 -and $force) {
                        $saLogin = Get-SqlSaLogin -SqlInstance $destServer
                        $sql = $sql -replace [Regex]::Escape("@owner_login_name=N'$missingLogin'"), "@owner_login_name=N'$saLogin'"
                    }

                    $sql = $sql -replace [Regex]::Escape("@server=N'$($sourceserver.DomainInstanceName)'"), "@server=N'$($destServer.DomainInstanceName)'"

                    if ($__boundNewName) {
                        $sql = $sql -replace [Regex]::Escape("@job_name=N'$jobName'"), "@job_name=N'$NewName'"
                    }

                    Write-Message -Message $sql -Level Debug -FunctionName Copy-DbaAgentJob
                    $destServer.Query($sql)

                    $destServer.JobServer.Jobs.Refresh()
                    $destServer.JobServer.Jobs[$destJobName].IsEnabled = $sourceServer.JobServer.Jobs[$serverJob.name].IsEnabled
                    $destServer.JobServer.Jobs[$destJobName].Alter()

                    # Restore alert-to-job links if job was dropped and recreated
                    if ($alertsReferencingJob -and $alertsReferencingJob.Count -gt 0) {
                        Write-Message -Message "Restoring alert-to-job links for $jobName" -Level Verbose -FunctionName Copy-DbaAgentJob
                        foreach ($alertName in $alertsReferencingJob) {
                            try {
                                $splatUpdateAlert = @{
                                    SqlInstance  = $destServer
                                    Database     = "msdb"
                                    Query        = "EXEC dbo.sp_update_alert @name = @alertName, @job_name = @jobName"
                                    SqlParameter = @{
                                        alertName = $alertName
                                        jobName   = $jobName
                                    }
                                }
                                $null = Invoke-DbaQuery @splatUpdateAlert
                                Write-Message -Message "Restored link between alert [$alertName] and job [$jobName]" -Level Verbose -FunctionName Copy-DbaAgentJob
                            } catch {
                                Write-Message -Level Warning -Message "Failed to restore alert link for [$alertName] to job [$jobName] | $PSItem" -FunctionName Copy-DbaAgentJob
                            }
                        }
                    }

                    $copyJobStatus.Status = "Successful"
                    $copyJobStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                } catch {
                    $copyJobStatus.Status = "Failed"
                    $copyJobStatus.Notes = (Get-ErrorMessage -Record $_)
                    $copyJobStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                    Write-Message -Level Verbose -Message "Issue copying job $jobName on $destinstance | $PSItem" -FunctionName Copy-DbaAgentJob
                    continue
                }
            }

            if ($DisableOnDestination) {
                if ($__realCmdlet.ShouldProcess($destinstance, "Disabling $destJobName")) {
                    Write-Message -Message "Disabling $destJobName on $destinstance" -Level Verbose -FunctionName Copy-DbaAgentJob
                    $destServer.JobServer.Jobs[$destJobName].IsEnabled = $False
                    $destServer.JobServer.Jobs[$destJobName].Alter()
                }
            }

            if ($DisableOnSource) {
                if ($__realCmdlet.ShouldProcess($source, "Disabling $jobName")) {
                    Write-Message -Message "Disabling $jobName on $source" -Level Verbose -FunctionName Copy-DbaAgentJob
                    $serverJob.IsEnabled = $false
                    $serverJob.Alter()
                }
            }
        }
    }
} $Source $Destination $DestinationSqlCredential $Job $ExcludeJob $DisableOnSource $DisableOnDestination $Force $NewName $UseLastModified $InputObject $EnableException $__realCmdlet $__boundJob $__boundExcludeJob $__boundNewName $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
