#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Detaches user databases. Port of public/Dismount-DbaDatabase.ps1; the workflow remains a
/// module-scoped PowerShell compatibility hop.
///
/// The process body rides one VERBATIM module hop per record. Every record is SELF-CONTAINED
/// because the parameter sets are mutually exclusive: in the "SqlInstance" set process fires once
/// and $InputObject accumulates only within that invocation's SqlInstance loop; in the "Pipeline"
/// set the piped $InputObject rebinds every record - so no sentinel and no C# state field are
/// needed (the Remove-DbaDatabase / W3-063 shape). $server is reassigned unconditionally at the top
/// of each loop, so it never carries a stale value.
///
/// Every Stop-Function here is -Continue (skip the current database and keep looping), and the one
/// bare Continue is a plain loop continue, so a failure never halts later records and there is no
/// interrupt carry to thread. The five $Pscmdlet.ShouldProcess gates route to the real cmdlet via
/// $__realCmdlet (ConfirmImpact Medium mirrored); -WhatIf and -Confirm ride the carriers.
///
/// The body references $ExcludeDatabase, which the source never declares as a parameter - it is an
/// unset variable whose "if ($ExcludeDatabase)" branch never fires. It ships verbatim; an unset read
/// walks the scope chain exactly as the function's did. In-hop Stop-Function/Write-Message carry
/// -FunctionName; the nested Get-DbaProcess/Get-DbaDbSnapshot/Stop-DbaProcess resolve through the
/// module scope. Surface pinned by migration/baselines/Dismount-DbaDatabase.json (three parameter
/// sets, no positions).
/// </summary>
[Cmdlet(VerbsData.Dismount, "DbaDatabase", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Medium, DefaultParameterSetName = "Default")]
public sealed class DismountDbaDatabaseCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ParameterSetName = "SqlInstance")]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The database(s) to detach.</summary>
    [Parameter(Mandatory = true, ParameterSetName = "SqlInstance")]
    [PsStringArrayCast]
    public string[]? Database { get; set; }

    /// <summary>SMO Database object(s), typically from Get-DbaDatabase.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, ParameterSetName = "Pipeline")]
    public Microsoft.SqlServer.Management.Smo.Database[]? InputObject { get; set; }

    /// <summary>Update distribution statistics before detaching.</summary>
    [Parameter]
    public SwitchParameter UpdateStatistics { get; set; }

    /// <summary>Kill connections, break mirrors, and drop from availability groups to force the
    /// detach.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, SqlCredential, Database, InputObject, UpdateStatistics.ToBool(),
            Force.ToBool(), EnableException.ToBool(), this,
            BoundCommonParameter("WhatIf"), BoundCommonParameter("Confirm"),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
                continue;
            }
            WriteObject(item);
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

    // PS: the ENTIRE process body VERBATIM per record. Substitutions only: $Pscmdlet ->
    // $__realCmdlet and -FunctionName Dismount-DbaDatabase on every Stop-Function/Write-Message. The
    // undeclared $ExcludeDatabase reference, the bare Connect-DbaInstance, the mirror/AG/session
    // handling, the interpolated messages, and the DetachDatabase call all ride as-is.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $InputObject, $UpdateStatistics, $Force, $EnableException, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = "Medium")]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [string[]]$Database, [Microsoft.SqlServer.Management.Smo.Database[]]$InputObject, $UpdateStatistics, $Force, $EnableException, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        foreach ($instance in $SqlInstance) {
            try {
                $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
            } catch {
                Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Dismount-DbaDatabase
            }

            if ($Database) {
                $InputObject += $server.Databases | Where-Object Name -in $Database
            } else {
                $InputObject += $server.Databases
            }

            if ($ExcludeDatabase) {
                $InputObject = $InputObject | Where-Object Name -NotIn $ExcludeDatabase
            }
        }

        foreach ($db in $InputObject) {
            $db.Refresh()
            $server = $db.Parent

            if ($db.IsSystemObject) {
                Stop-Function -Message "$db is a system database and cannot be detached using this method." -Target $db -Continue -FunctionName Dismount-DbaDatabase
            }

            Write-Message -Level Verbose -Message "Checking replication status." -FunctionName Dismount-DbaDatabase
            if ($db.ReplicationOptions -ne "None") {
                Stop-Function -Message "Skipping $db  on $server because it is replicated." -Target $db -Continue -FunctionName Dismount-DbaDatabase
            }

            # repeat because different servers could be piped in
            $snapshots = (Get-DbaDbSnapshot -SqlInstance $server).SnapshotOf
            Write-Message -Level Verbose -Message "Checking for snaps" -FunctionName Dismount-DbaDatabase
            if ($db.Name -in $snapshots) {
                Write-Message -Level Warning -Message "Database $db has snapshots, you need to drop them before detaching. Skipping $db on $server." -FunctionName Dismount-DbaDatabase
                Continue
            }

            Write-Message -Level Verbose -Message "Checking mirror status" -FunctionName Dismount-DbaDatabase
            if ($db.IsMirroringEnabled -and !$Force) {
                Stop-Function -Message "$db on $server is being mirrored. Use -Force to break mirror or use the safer backup/restore method." -Target $db -Continue -FunctionName Dismount-DbaDatabase
            }

            Write-Message -Level Verbose -Message "Checking Availability Group status" -FunctionName Dismount-DbaDatabase

            if ($db.AvailabilityGroupName -and !$Force) {
                $ag = $db.AvailabilityGroupName
                Stop-Function -Message "$db on $server is part of an Availability Group ($ag). Use -Force to drop from $ag availability group to detach. Alternatively, you can use the safer backup/restore method." -Target $db -Continue -FunctionName Dismount-DbaDatabase
            }

            $sessions = Get-DbaProcess -SqlInstance $db.Parent -Database $db.Name

            if ($sessions -and !$Force) {
                Stop-Function -Message "$db on $server currently has connected users and cannot be dropped. Use -Force to kill all connections and detach the database." -Target $db -Continue -FunctionName Dismount-DbaDatabase
            }

            if ($force) {

                if ($sessions) {
                    If ($__realCmdlet.ShouldProcess($server, "Killing $($sessions.count) sessions which are connected to $db")) {
                        $null = $sessions | Stop-DbaProcess -ErrorAction SilentlyContinue -WarningAction SilentlyContinue
                    }
                }

                if ($db.IsMirroringEnabled) {
                    If ($__realCmdlet.ShouldProcess($server, "Breaking mirror for $db on $server")) {
                        try {
                            Write-Message -Level Warning -Message "Breaking mirror for $db on $server." -FunctionName Dismount-DbaDatabase
                            $db.ChangeMirroringState([Microsoft.SqlServer.Management.Smo.MirroringOption]::Off)
                            $db.Alter()
                            $db.Refresh()
                        } catch {
                            Stop-Function -Message "Could not break mirror for $db on $server - not detaching." -Target $db -ErrorRecord $_ -Continue -FunctionName Dismount-DbaDatabase
                        }
                    }
                }

                if ($db.AvailabilityGroupName) {
                    $ag = $db.AvailabilityGroupName
                    If ($__realCmdlet.ShouldProcess($server, "Attempting remove $db on $server from Availability Group $ag")) {
                        try {
                            $server.AvailabilityGroups[$ag].AvailabilityDatabases[$db.name].Drop()
                            Write-Message -Level Verbose -Message "Successfully removed $db from  detach from $ag on $server." -FunctionName Dismount-DbaDatabase
                        } catch {
                            if ($_.Exception.InnerException) {
                                $exception = $_.Exception.InnerException.ToString() -Split "Microsoft.Data.SqlClient.SqlException: "
                                $exception = " | $(($exception[1] -Split "at Microsoft.SqlServer.Management.Common.ConnectionManager")[0])".TrimEnd()
                            }

                            Stop-Function -Message "Could not remove $db from $ag on $server $exception." -Target $db -ErrorRecord $_ -Continue -FunctionName Dismount-DbaDatabase
                        }
                    }
                }

                $sessions = Get-DbaProcess -SqlInstance $db.Parent -Database $db.Name

                if ($sessions) {
                    If ($__realCmdlet.ShouldProcess($server, "Killing $($sessions.count) sessions which are still connected to $db")) {
                        $null = $sessions | Stop-DbaProcess -ErrorAction SilentlyContinue -WarningAction SilentlyContinue
                    }
                }
            }

            If ($__realCmdlet.ShouldProcess($server, "Detaching $db on $server")) {
                try {
                    $dbID = $db.ID
                    $server.DetachDatabase($db.Name, $UpdateStatistics)

                    [PSCustomObject]@{
                        ComputerName = $server.ComputerName
                        InstanceName = $server.ServiceName
                        SqlInstance  = $server.DomainInstanceName
                        Database     = $db.name
                        DatabaseID   = $dbID
                        DetachResult = "Success"
                    }
                } catch {
                    Stop-Function -Message "Failure" -Target $db -ErrorRecord $_ -Continue -FunctionName Dismount-DbaDatabase
                }
            }
        }
} $SqlInstance $SqlCredential $Database $InputObject $UpdateStatistics $Force $EnableException $__realCmdlet $__boundWhatIf $__boundConfirm $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
