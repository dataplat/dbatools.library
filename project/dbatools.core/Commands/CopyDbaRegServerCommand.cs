#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Copies CMS groups and registered servers between instances. Port of
/// public/Copy-DbaRegServer.ps1 (W3-005). Neither parameter is pipeline-bound, so the
/// ENTIRE command (begin body + process body, in source order) rides ONE VERBATIM module
/// hop per the Copy-family convention (CopyDbaAgentServerCommand precedent): the
/// Force -&gt; $ConfirmPreference = 'none' prologue, the recursive Invoke-ParseServerGroup
/// helper (defined in hop scope; its own [cmdletbinding()] $Pscmdlet serves the
/// ShouldProcess gates and inherits the carried -WhatIf/-Confirm preferences exactly as
/// fn scope inheritance did), the begin-block source connect whose Stop-Function+return
/// short-circuits the concatenated process body just like the fn's
/// Test-FunctionInterrupt guard (the scope-1 magic-variable handshake rides verbatim
/// inside the single hop scope), and the per-destination loop. Only the two hop-level
/// Stop-Function calls carry explicit -FunctionName Copy-DbaRegServer (W1-090); the
/// helper's Write-Message callstack attribution (Invoke-ParseServerGroup) is identical
/// in both worlds. Surface pinned by migration/baselines/Copy-DbaRegServer.json
/// (implicit positions 0-4, Source/Destination mandatory, Group alias CMSGroup,
/// DefaultParameterSetName Default, ConfirmImpact Medium, no OutputType).
/// </summary>
[Cmdlet(VerbsCommon.Copy, "DbaRegServer", DefaultParameterSetName = "Default", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Medium)]
public sealed class CopyDbaRegServerCommand : DbaBaseCmdlet
{
    /// <summary>Source SQL Server instance containing the CMS to copy from.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    public DbaInstanceParameter? Source { get; set; }

    /// <summary>Credential for the source instance.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SourceSqlCredential { get; set; }

    /// <summary>Destination SQL Server instance(s) for the copied CMS content.</summary>
    [Parameter(Mandatory = true, Position = 2)]
    public DbaInstanceParameter[]? Destination { get; set; }

    /// <summary>Credential for the destination instances.</summary>
    [Parameter(Position = 3)]
    public PSCredential? DestinationSqlCredential { get; set; }

    /// <summary>Top-level CMS group name(s) to copy; all when omitted.</summary>
    [Parameter(Position = 4)]
    [Alias("CMSGroup")]
    public string[]? Group { get; set; }

    /// <summary>Replaces source server name references with the destination server name.</summary>
    [Parameter]
    public SwitchParameter SwitchServerName { get; set; }

    /// <summary>Drops and recreates existing groups and registrations at the destination.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BodyScript,
            Source, SourceSqlCredential, Destination, DestinationSqlCredential, Group,
            SwitchServerName.ToBool(), Force.ToBool(), EnableException.ToBool(),
            BoundCommonParameter("WhatIf"), BoundCommonParameter("Confirm"),
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

    /// <summary>Removes the silent $error copy the nested pipeline bagged for a merged-back
    /// non-terminating record (the W1-045 compensation).</summary>
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

    // PS: begin body then process body VERBATIM (single hop; no pipeline parameters means
    // process runs once). Substitutions only: explicit -FunctionName Copy-DbaRegServer on
    // the two hop-level Stop-Function calls (W1-090). The helper and its $Pscmdlet ride
    // untouched.
    private const string BodyScript = """
param($Source, $SourceSqlCredential, $Destination, $DestinationSqlCredential, $Group, $SwitchServerName, $Force, $EnableException, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = "Medium")]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter]$Source, [PSCredential]$SourceSqlCredential, [Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$Destination, [PSCredential]$DestinationSqlCredential, [string[]]$Group, $SwitchServerName, $Force, $EnableException, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    if ($Force) { $ConfirmPreference = 'none' }

    function Invoke-ParseServerGroup {
        [cmdletbinding()]
        param (
            $sourceGroup,
            $destinationGroup,
            $SwitchServerName
        )
        if ($destinationGroup.Name -eq "DatabaseEngineServerGroup" -and $sourceGroup.Name -ne "DatabaseEngineServerGroup") {
            $currentServerGroup = $destinationGroup
            $groupName = $sourceGroup.Name
            $destinationGroup = $destinationGroup.ServerGroups[$groupName]

            $copyDestinationGroupStatus = [PSCustomObject]@{
                SourceServer      = $sourceServer.Name
                DestinationServer = $destServer.Name
                Name              = $groupName
                Type              = "CMS Destination Group"
                Status            = $null
                Notes             = $null
                DateTime          = [Dataplat.Dbatools.Utility.DbaDateTime](Get-Date)
            }

            if ($null -ne $destinationGroup) {

                if ($force -eq $false) {
                    if ($Pscmdlet.ShouldProcess($destinstance, "Checking to see if $groupName exists")) {
                        $copyDestinationGroupStatus.Status = "Skipped"
                        $copyDestinationGroupStatus.Notes = "Already exists on destination"
                        $copyDestinationGroupStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                        Write-Message -Level Verbose -Message "Destination group $groupName exists at destination. Use -Force to drop and migrate."
                    }
                    continue
                }
                if ($Pscmdlet.ShouldProcess($destinstance, "Dropping group $groupName")) {
                    try {
                        Write-Message -Level Verbose -Message "Dropping group $groupName"
                        $destinationGroup.Drop()
                    } catch {
                        $copyDestinationGroupStatus.Status = "Failed"
                        $copyDestinationGroupStatus.Notes = $_.Exception.Message
                        $copyDestinationGroupStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                        Write-Message -Level Verbose -Message "Issue dropping group $groupName on $destinstance | $PSItem"
                        continue
                    }
                }
            }

            if ($Pscmdlet.ShouldProcess($destinstance, "Creating group $groupName")) {
                try {
                    Write-Message -Level Verbose -Message "Creating group $($sourceGroup.Name)"
                    $destinationGroup = New-Object Microsoft.SqlServer.Management.RegisteredServers.ServerGroup($currentServerGroup, $sourceGroup.Name)
                    $destinationGroup.Create()
                    $copyDestinationGroupStatus.Status = "Successful"
                    $copyDestinationGroupStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                } catch {
                    $copyDestinationGroupStatus.Status = "Failed"
                    $copyDestinationGroupStatus.Notes = $_.Exception.Message
                    $copyDestinationGroupStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                    Write-Message -Level Verbose -Message "Issue creating group $groupName on $destinstance | $PSItem"
                    continue
                }
            }
        }

        # Add Servers
        foreach ($instance in $sourceGroup.RegisteredServers) {
            $instanceName = $instance.Name
            $serverName = $instance.ServerName
            $destinstance = $destServer.Name
            $copyInstanceStatus = [PSCustomObject]@{
                SourceServer      = $sourceServer.Name
                DestinationServer = $destServer.Name
                Name              = $instanceName
                Type              = "CMS Instance"
                Status            = $null
                Notes             = $null
                DateTime          = [Dataplat.Dbatools.Utility.DbaDateTime](Get-Date)
            }

            if ($serverName.ToLowerInvariant() -eq $toCmStore.DomainInstanceName.ToLowerInvariant()) {
                if ($SwitchServerName) {
                    $serverName = $fromCmStore.DomainInstanceName
                    $instanceName = $fromCmStore.DomainInstanceName
                    Write-Message -Level Verbose -Message "SwitchServerName was used and new CMS equals current server name. $($toCmStore.DomainInstanceName.ToLowerInvariant()) changed to $serverName."
                } else {
                    if ($Pscmdlet.ShouldProcess($destinstance, "$serverName is Central Management Server. Add prohibited. Skipping.")) {
                        $copyInstanceStatus.Status = "Skipped"
                        $copyInstanceStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                        Write-Message -Level Verbose -Message "$serverName is Central Management Server. Add prohibited. Skipping."
                    }
                    continue
                }
            }

            if ($destinationGroup.RegisteredServers.Name -contains $instanceName) {

                if ($force -eq $false) {
                    if ($Pscmdlet.ShouldProcess($destinstance, "Checking to see if $instanceName in $groupName exists")) {
                        $copyInstanceStatus.Status = "Skipped"
                        $copyInstanceStatus.Notes = "Already exists on destination"
                        $copyInstanceStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                        Write-Message -Level Verbose -Message "Instance $instanceName exists in group $groupName at destination. Use -Force to drop and migrate."
                    }
                    continue
                }

                if ($Pscmdlet.ShouldProcess($destinstance, "Dropping instance $instanceName from $groupName and recreating")) {
                    try {
                        Write-Message -Level Verbose -Message "Dropping instance $instance from $groupName"
                        $destinationGroup.RegisteredServers[$instanceName].Drop()
                    } catch {
                        $copyInstanceStatus.Status = "Failed"
                        $copyInstanceStatus.Notes = $_.Exception.Message
                        $copyInstanceStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                        Write-Message -Level Verbose -Message "Issue dropping instance $instanceName from $groupName and recreating on $destinstance | $PSItem"
                        continue
                    }
                }
            }

            if ($Pscmdlet.ShouldProcess($destinstance, "Copying $instanceName")) {
                $newServer = New-Object Microsoft.SqlServer.Management.RegisteredServers.RegisteredServer($destinationGroup, $instanceName)
                $newServer.ServerName = $serverName
                $newServer.Description = $instance.Description

                if ($serverName -ne $fromCmStore.DomainInstanceName) {
                    $newServer.SecureConnectionString = $instance.SecureConnectionString
                    $newServer.ConnectionString = $instance.ConnectionString.ToString()
                }

                try {
                    $newServer.Create()
                    $copyInstanceStatus.Status = "Successful"
                    $copyInstanceStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                    Write-Message -Level Verbose -Message "Added Server $serverName as $instanceName to $($destinationGroup.Name)"
                } catch {
                    $copyInstanceStatus.Status = "Failed"
                    $copyInstanceStatus.Notes = $_.Exception.Message
                    $copyInstanceStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                    if ($_.Exception -match "same name") {
                        Write-Message -Level Verbose -Message "Could not add Switched Server instance name on $instanceName"
                    } else {
                        Write-Message -Level Verbose -Message "Failed to add $serverName on $instanceName"
                        continue
                    }
                }
            }
        }

        # Add Groups
        foreach ($fromSubGroup in $sourceGroup.ServerGroups) {
            $fromSubGroupName = $fromSubGroup.Name
            if ($Pscmdlet.ShouldProcess($destinstance, "Copying group $fromSubGroupName")) {
                $toSubGroup = $destinationGroup.ServerGroups[$fromSubGroupName]

                $copyGroupStatus = [PSCustomObject]@{
                    SourceServer      = $sourceServer.Name
                    DestinationServer = $destServer.Name
                    Name              = $fromSubGroupName
                    Type              = "CMS Group"
                    Status            = $null
                    Notes             = $null
                    DateTime          = [Dataplat.Dbatools.Utility.DbaDateTime](Get-Date)
                }
            }

            if ($null -ne $toSubGroup) {
                if ($force -eq $false) {
                    if ($Pscmdlet.ShouldProcess($destinstance, "Subgroup $fromSubGroupName exists at destination. Use -Force to drop and migrate.")) {
                        $copyGroupStatus.Status = "Skipped"
                        $copyGroupStatus.Notes = "Already exists on destination"
                        $copyGroupStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                        Write-Message -Level Verbose -Message "Subgroup $fromSubGroupName exists at destination. Use -Force to drop and migrate."
                    }
                    continue
                }

                if ($Pscmdlet.ShouldProcess($destinstance, "Dropping subgroup $fromSubGroupName recreating")) {
                    try {
                        Write-Message -Level Verbose -Message "Dropping subgroup $fromSubGroupName"
                        $toSubGroup.Drop()
                    } catch {
                        $copyGroupStatus.Status = "Failed"
                        $copyGroupStatus.Notes = $_.Exception.Message
                        $copyGroupStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                        Write-Message -Level Verbose -Message "Issue dropping group $fromSubGroupName on $destinstance | $PSItem"
                        continue
                    }
                }
            }

            if ($Pscmdlet.ShouldProcess($destinstance, "Creating group $($fromSubGroup.Name)")) {
                try {
                    Write-Message -Level Verbose -Message "Creating group $($fromSubGroup.Name)"
                    $toSubGroup = New-Object Microsoft.SqlServer.Management.RegisteredServers.ServerGroup($destinationGroup, $fromSubGroup.Name)
                    $toSubGroup.create()
                    $copyGroupStatus.Status = "Successful"
                    $copyGroupStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                } catch {
                    $copyGroupStatus.Status = "Failed"
                    $copyGroupStatus.Notes = $_.Exception.Message
                    $copyGroupStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                    Write-Message -Level Verbose -Message "Issue creating group $($fromSubGroup.Name) to $destinstance | $PSItem"
                    continue
                }
            }

            Invoke-ParseServerGroup -sourceGroup $fromSubGroup -destinationgroup $toSubGroup -SwitchServerName $SwitchServerName
        }
    }

    try {
        $sourceServer = Connect-DbaInstance -SqlInstance $Source -SqlCredential $SourceSqlCredential -MinimumVersion 10
        $fromCmStore = Get-DbaRegServerStore -SqlInstance $sourceServer
    } catch {
        Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $Source -FunctionName Copy-DbaRegServer
        return
    }

    if (Test-FunctionInterrupt) { return }
    foreach ($destinstance in $Destination) {
        try {
            $destServer = Connect-DbaInstance -SqlInstance $destinstance -SqlCredential $DestinationSqlCredential -MinimumVersion 10
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $destinstance -Continue -FunctionName Copy-DbaRegServer
        }
        $toCmStore = Get-DbaRegServerStore -SqlInstance $destServer

        $stores = $fromCmStore.DatabaseEngineServerGroup
        if ($Group) {
            $stores = @();
            foreach ($groupName in $Group) {
                $stores += $fromCmStore.DatabaseEngineServerGroup.ServerGroups[$groupName]
            }
        }

        foreach ($store in $stores) {
            Invoke-ParseServerGroup -sourceGroup $store -destinationgroup $toCmStore.DatabaseEngineServerGroup -SwitchServerName $SwitchServerName
        }
    }
} $Source $SourceSqlCredential $Destination $DestinationSqlCredential $Group $SwitchServerName $Force $EnableException $__boundWhatIf $__boundConfirm $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
