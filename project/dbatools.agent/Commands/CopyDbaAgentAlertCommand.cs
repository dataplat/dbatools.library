#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Copies SQL Agent alerts, defaults, job associations, and notifications. Port of
/// public/Copy-DbaAgentAlert.ps1 (W2-001). The complete begin/process workflow rides one
/// module-scoped PowerShell hop so SMO scripting, collection refreshes, filtering, conflict
/// checks, notification flag coercion, output decoration, and flow-control quirks retain the
/// retired function's engine semantics. The compiled cmdlet supplies the real ShouldProcess
/// runtime. Surface pinned by migration/baselines/Copy-DbaAgentAlert.json.
/// </summary>
[Cmdlet(VerbsCommon.Copy, "DbaAgentAlert", DefaultParameterSetName = "Default",
    SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Medium)]
public sealed class CopyDbaAgentAlertCommand : DbaBaseCmdlet
{
    /// <summary>The source SQL Server instance.</summary>
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

    /// <summary>Only copy alerts with these names.</summary>
    [Parameter(Position = 4)]
    public object[]? Alert { get; set; }

    /// <summary>Exclude alerts with these names.</summary>
    [Parameter(Position = 5)]
    public object[]? ExcludeAlert { get; set; }

    /// <summary>Copy SQL Agent alert-system defaults.</summary>
    [Parameter]
    public SwitchParameter IncludeDefaults { get; set; }

    /// <summary>Drop and recreate alerts that already exist.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BodyScript,
            Source, SourceSqlCredential, Destination, DestinationSqlCredential,
            Alert, ExcludeAlert, IncludeDefaults.ToBool(), Force.ToBool(),
            EnableException.ToBool(), this, BoundCommonParameter("Verbose"),
            BoundCommonParameter("Debug")))
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
param($Source, $SourceSqlCredential, $Destination, $DestinationSqlCredential, $Alert, $ExcludeAlert, $IncludeDefaults, $Force, $EnableException, $__realCmdlet, $__boundVerbose, $__boundDebug)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter]$Source, $SourceSqlCredential, [Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$Destination, $DestinationSqlCredential, [object[]]$Alert, [object[]]$ExcludeAlert, $IncludeDefaults, $Force, $EnableException, $__realCmdlet, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundVerbose) { $VerbosePreference = $(if ($__boundVerbose) { "Continue" } else { "SilentlyContinue" }) }
    if ($null -ne $__boundDebug) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    try {
        $sourceServer = Connect-DbaInstance -SqlInstance $Source -SqlCredential $SourceSqlCredential
        $serverAlerts = $sourceServer.JobServer.Alerts
    } catch {
        Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $Source -FunctionName Copy-DbaAgentAlert
        return
    }
    if ($Force) { $ConfirmPreference = 'none' }

    if (Test-FunctionInterrupt) { return }
    foreach ($destinstance in $Destination) {
        try {
            $destServer = Connect-DbaInstance -SqlInstance $destinstance -SqlCredential $DestinationSqlCredential
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $destinstance -Continue -FunctionName Copy-DbaAgentAlert
        }
        $destAlerts = $destServer.JobServer.Alerts

        if ($IncludeDefaults -eq $true) {
            if ($__realCmdlet.ShouldProcess($destinstance, "Creating Alert Defaults")) {
                $copyAgentAlertStatus = [PSCustomObject]@{
                    SourceServer      = $sourceServer.Name
                    DestinationServer = $destServer.Name
                    Name              = "Alert Defaults"
                    Type              = "Alert Defaults"
                    Status            = $null
                    Notes             = $null
                    DateTime          = [Dataplat.Dbatools.Utility.DbaDateTime](Get-Date)
                }
                try {
                    Write-Message -Message "Creating Alert Defaults" -Level Verbose -FunctionName Copy-DbaAgentAlert
                    $sql = $sourceServer.JobServer.AlertSystem.Script() | Out-String
                    $sql = $sql -replace [Regex]::Escape("'$source'"), "'$destinstance'"

                    Write-Message -Message $sql -Level Debug -FunctionName Copy-DbaAgentAlert
                    $null = $destServer.Query($sql)

                    $copyAgentAlertStatus.Status = "Successful"
                } catch {
                    $copyAgentAlertStatus.Status = "Failed"
                    $copyAgentAlertStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                    Write-Message -Level Verbose -Message "Issue creating alert defaults on $destinstance | $PSItem" -FunctionName Copy-DbaAgentAlert
                }
            }
        }

        $destServerOperators = $destServer.JobServer.Operators

        foreach ($serverAlert in $serverAlerts) {
            $alertName = $serverAlert.name
            $copyAgentAlertStatus = [PSCustomObject]@{
                SourceServer      = $sourceServer.Name
                DestinationServer = $destServer.Name
                Name              = $alertName
                Type              = "Agent Alert"
                Notes             = $null
                Status            = $null
                DateTime          = [Dataplat.Dbatools.Utility.DbaDateTime](Get-Date)
            }
            if (($Alert -and $Alert -notcontains $alertName) -or ($ExcludeAlert -and $ExcludeAlert -contains $alertName)) {
                continue
            }

            if ($serverAlert.HasNotification) {
                $alertOperators = $serverAlert.EnumNotifications()
                if ($destServerOperators.Name -notin $alertOperators.OperatorName) {
                    $missingOperators = ($alertOperators | Where-Object OperatorName -NotIn $destServerOperators.Name).OperatorName
                    if ($missingOperators.Count -gt 0 -or $missingOperators.Length -gt 0) {
                        $operatorList = $missingOperators -join ','
                        if ($__realCmdlet.ShouldProcess($destinstance, "Missing operator(s) at destination.")) {
                            $copyAgentAlertStatus.Status = "Skipped"
                            $copyAgentAlertStatus.Notes = "Operator(s) [$operatorList] do not exist on destination"
                            $copyAgentAlertStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                            Write-Message -Message "One or more operators alerted by [$alertName] is not present at the destination. Alert will not be copied. Use Copy-DbaAgentOperator to copy the operator(s) to the destination. Missing operator(s): $operatorList" -Level Warning -FunctionName Copy-DbaAgentAlert
                        }
                        continue
                    }
                }
            }

            if ($destAlerts.name -contains $serverAlert.name) {
                if ($force -eq $false) {
                    if ($__realCmdlet.ShouldProcess($destinstance, "Alert [$alertName] exists at destination. Use -Force to drop and migrate.")) {
                        $copyAgentAlertStatus.Status = "Skipped"
                        $copyAgentAlertStatus.Notes = "Already exists on destination"
                        $copyAgentAlertStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                        Write-Message -Message "Alert [$alertName] exists at destination. Use -Force to drop and migrate." -Level Verbose -FunctionName Copy-DbaAgentAlert
                    }
                    continue
                }

                if ($__realCmdlet.ShouldProcess($destinstance, "Dropping alert $alertName and recreating")) {
                    try {
                        Write-Message -Message "Dropping Alert $alertName on $destServer." -Level Verbose -FunctionName Copy-DbaAgentAlert
                        $sql = "EXEC msdb.dbo.sp_delete_alert @name = N'$($alertname)';"
                        Write-Message -Message $sql -Level Debug -FunctionName Copy-DbaAgentAlert
                        $null = $destServer.Query($sql)
                        $destAlerts.Refresh()
                    } catch {
                        $copyAgentAlertStatus.Status = "Failed"
                        $copyAgentAlertStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                        Write-Message -Level Verbose -Message "Issue dropping/recreating alert $alertname on $destInstance | $PSItem" -FunctionName Copy-DbaAgentAlert
                        continue
                    }
                }
            }

            if ($destAlerts | Where-Object { $_.Severity -eq $serverAlert.Severity -and $_.MessageID -eq $serverAlert.MessageID -and $_.DatabaseName -eq $serverAlert.DatabaseName -and $_.EventDescriptionKeyword -eq $serverAlert.EventDescriptionKeyword }) {
                if ($__realCmdlet.ShouldProcess($destinstance, "Checking for conflicts")) {
                    $conflictMessage = "Alert [$alertName] has already been defined to use"
                    if ($serverAlert.Severity -gt 0) { $conflictMessage += " severity $($serverAlert.Severity)" }
                    if ($serverAlert.MessageID -gt 0) { $conflictMessage += " error number $($serverAlert.MessageID)" }
                    if ($serverAlert.DatabaseName) { $conflictMessage += " on database '$($serverAlert.DatabaseName)'" }
                    if ($serverAlert.EventDescriptionKeyword) { $conflictMessage += " with error text '$($serverAlert.Severity)'" }
                    $conflictMessage += ". Skipping."

                    Write-Message -Level Verbose -Message $conflictMessage -FunctionName Copy-DbaAgentAlert
                    $copyAgentAlertStatus.Status = "Skipped"
                    $copyAgentAlertStatus.Notes = $conflictMessage
                    $copyAgentAlertStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                }
                continue
            }
            if ($serverAlert.JobName -and $destServer.JobServer.Jobs.Name -NotContains $serverAlert.JobName) {
                Write-Message -Level Verbose -Message "Alert [$alertName] has job [$($serverAlert.JobName)] configured as response. The job does not exist on destination $destServer. Skipping." -FunctionName Copy-DbaAgentAlert
                if ($__realCmdlet.ShouldProcess($destinstance, "Checking for conflicts")) {
                    $copyAgentAlertStatus.Status = "Skipped"
                    $copyAgentAlertStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                }
                continue
            }

            if ($__realCmdlet.ShouldProcess($destinstance, "Creating Alert $alertName")) {
                try {
                    Write-Message -Message "Copying Alert $alertName" -Level Verbose -FunctionName Copy-DbaAgentAlert
                    $sql = $serverAlert.Script() | Out-String
                    $sql = $sql -replace "@job_id=N'........-....-....-....-............", "@job_id=N'00000000-0000-0000-0000-000000000000"

                    Write-Message -Message $sql -Level Debug -FunctionName Copy-DbaAgentAlert
                    $null = $destServer.Query($sql)

                    $copyAgentAlertStatus.Status = "Successful"
                    $copyAgentAlertStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                } catch {
                    $copyAgentAlertStatus.Status = "Failed"
                    $copyAgentAlertStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                    Write-Message -Level Verbose -Message "Issue creating alert $alertname on $destinstance | $PSItem" -FunctionName Copy-DbaAgentAlert
                }
            }

            $destServer.JobServer.Alerts.Refresh()
            $destServer.JobServer.Jobs.Refresh()

            $newAlert = $destServer.JobServer.Alerts[$alertName]
            $notifications = $serverAlert.EnumNotifications()
            $jobName = $serverAlert.JobName

            if ($serverAlert.JobId -ne '00000000-0000-0000-0000-000000000000') {
                $copyAgentAlertStatus = [PSCustomObject]@{
                    SourceServer      = $sourceServer.Name
                    DestinationServer = $destServer.Name
                    Name              = $alertName
                    Type              = "Agent Alert Job Association"
                    Notes             = "Associated with $jobName"
                    Status            = $null
                    DateTime          = [Dataplat.Dbatools.Utility.DbaDateTime](Get-Date)
                }
                if ($__realCmdlet.ShouldProcess($destinstance, "Adding $alertName to $jobName")) {
                    try {
                        Write-Message -Message "Adding $alertName to $jobName" -Level Verbose -FunctionName Copy-DbaAgentAlert
                        $newJob = $destServer.JobServer.Jobs[$jobName]
                        $newJobId = ($newJob.JobId) -replace " ", ""
                        $sql = $sql -replace '00000000-0000-0000-0000-000000000000', $newJobId
                        $sql = $sql -replace 'sp_add_alert', 'sp_update_alert'

                        Write-Message -Message $sql -Level Debug -FunctionName Copy-DbaAgentAlert
                        $null = $destServer.Query($sql)

                        $copyAgentAlertStatus.Status = "Successful"
                        $copyAgentAlertStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                    } catch {
                        $copyAgentAlertStatus.Status = "Failed"
                        $copyAgentAlertStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                        Write-Message -Level Verbose -Message "Issue adding alert to job to $destinstance | $PSItem" -FunctionName Copy-DbaAgentAlert
                        continue
                    }
                }
            }

            if ($__realCmdlet.ShouldProcess($destinstance, "Moving Notifications $alertName")) {
                try {
                    $copyAgentAlertStatus = [PSCustomObject]@{
                        SourceServer      = $sourceServer.Name
                        DestinationServer = $destServer.Name
                        Name              = $alertName
                        Type              = "Agent Alert Notification"
                        Notes             = $null
                        Status            = $null
                        DateTime          = [Dataplat.Dbatools.Utility.DbaDateTime](Get-Date)
                    }
                    foreach ($notify in $notifications) {
                        $notifyCollection = @()
                        if ($notify.UseNetSend -eq $true) {
                            Write-Message -Message "Adding net send" -Level Verbose -FunctionName Copy-DbaAgentAlert
                            $notifyCollection += "NetSend"
                        }

                        if ($notify.UseEmail -eq $true) {
                            Write-Message -Message "Adding email" -Level Verbose -FunctionName Copy-DbaAgentAlert
                            $notifyCollection += "NotifyEmail"
                        }

                        if ($notify.UsePager -eq $true) {
                            Write-Message -Message "Adding pager" -Level Verbose -FunctionName Copy-DbaAgentAlert
                            $notifyCollection += "Pager"
                        }

                        $notifyMethods = $notifyCollection -join ", "
                        $newAlert.AddNotification($notify.OperatorName, [Microsoft.SqlServer.Management.Smo.Agent.NotifyMethods]$notifyMethods)
                    }
                    $copyAgentAlertStatus.Status = "Successful"
                    $copyAgentAlertStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                } catch {
                    $copyAgentAlertStatus.Status = "Failed"
                    $copyAgentAlertStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                    Write-Message -Level Verbose -Message "Issue moving notifications to $destinstance for the alert $alertName | $PSItem" -FunctionName Copy-DbaAgentAlert
                }
            }
        }
    }
} $Source $SourceSqlCredential $Destination $DestinationSqlCredential $Alert $ExcludeAlert $IncludeDefaults $Force $EnableException $__realCmdlet $__boundVerbose $__boundDebug 3>&1 2>&1
""";
}
