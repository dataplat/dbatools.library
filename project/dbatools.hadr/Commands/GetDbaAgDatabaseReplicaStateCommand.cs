#nullable enable

using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Returns per-replica database state detail for availability groups (SSMS dashboard
/// style), from instances or piped availability groups. Port of public/Get-DbaAgDatabaseReplicaState.ps1; surface pinned by
/// migration/baselines/Get-DbaAgDatabaseReplicaState.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaAgDatabaseReplicaState")]
public sealed class GetDbaAgDatabaseReplicaStateCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instances using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Restricts results to these availability groups.</summary>
    [Parameter(Position = 2)]
    public string[]? AvailabilityGroup { get; set; }

    /// <summary>Restricts results to these databases.</summary>
    [Parameter(Position = 3)]
    public string[]? Database { get; set; }

    /// <summary>Availability group objects piped from Get-DbaAvailabilityGroup.</summary>
    [Parameter(ValueFromPipeline = true, Position = 4)]
    public Microsoft.SqlServer.Management.Smo.AvailabilityGroup[]? InputObject { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        // Whole-record hop: the source process has no per-instance foreach at top level
        // (the $InputObject += accumulation and database loop share record scope), and
        // the loop-less Stop-Function + return exits the record in both worlds. The
        // Test-Bound flags are computed per record - pipeline binding adds InputObject
        // to BoundParameters only on records that actually bound it.
        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, SqlCredential, AvailabilityGroup, Database,
            InputObject, EnableException.ToBool(),
            TestBound(nameof(SqlInstance)), TestBound(nameof(InputObject)),
            NestedCommand.BoundCommonParameter(this, "Verbose"), NestedCommand.BoundCommonParameter(this, "Debug")))
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                NestedCommand.RemoveDuplicateError(this, nestedError);
                WriteError(nestedError);
                continue;
            }
            WriteObject(item);
        }
    }

    // PS: the source process block VERBATIM (EOL-normalized like every sliced body).
    // Substitutions: one -FunctionName append on the loop-less Stop-Function and the
    // multi-name Test-Bound -> carried bound flags (SOURCE comment); the source's
    // `return` after it exits the record identically in both worlds. The dot-block
    // preserves that early return without skipping the hop frame.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $AvailabilityGroup, $Database, $InputObject, $EnableException, $__boundSqlInstance, $__boundInputObject, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [string[]]$AvailabilityGroup, [string[]]$Database, [Microsoft.SqlServer.Management.Smo.AvailabilityGroup[]]$InputObject, $EnableException, $__boundSqlInstance, $__boundInputObject, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    . {
        if (-not ($__boundSqlInstance -or $__boundInputObject)) { # SOURCE: if (Test-Bound -Not SqlInstance, InputObject) {
            Stop-Function -Message "You must supply either -SqlInstance or an Input Object" -FunctionName Get-DbaAgDatabaseReplicaState
            return
        }

        if ($SqlInstance) {
            $InputObject += Get-DbaAvailabilityGroup -SqlInstance $SqlInstance -SqlCredential $SqlCredential -AvailabilityGroup $AvailabilityGroup
        }

        foreach ($ag in $InputObject) {
            # Comprehensive health monitoring similar to SSMS AG Dashboard
            # Returns detailed database replica state information for all replicas
            foreach ($replica in $ag.AvailabilityReplicas) {
                $replicaId = $replica.UniqueId
                $replicaStates = $ag.DatabaseReplicaStates | Where-Object AvailabilityReplicaId -eq $replicaId

                foreach ($db in $ag.AvailabilityDatabases) {
                    if ($Database) {
                        if ($db.Name -notin $Database) { continue }
                    }

                    # AvailabilityDateabaseId is a typo in SMO but we have to use it as-is
                    # See https://learn.microsoft.com/en-us/dotnet/api/microsoft.sqlserver.management.smo.databasereplicastate.availabilitydateabaseid
                    $databaseReplicaState = $replicaStates | Where-Object AvailabilityDateabaseId -eq $db.UniqueId
                    if ($null -eq $databaseReplicaState) {
                        continue
                    }

                    [PSCustomObject]@{
                        ComputerName                = $ag.ComputerName
                        InstanceName                = $ag.InstanceName
                        SqlInstance                 = $ag.SqlInstance
                        AvailabilityGroup           = $ag.Name
                        PrimaryReplica              = $ag.PrimaryReplica
                        ReplicaServerName           = $databaseReplicaState.AvailabilityReplicaServerName
                        ReplicaRole                 = $databaseReplicaState.ReplicaRole
                        ReplicaAvailabilityMode     = $replica.AvailabilityMode
                        ReplicaFailoverMode         = $replica.FailoverMode
                        ReplicaConnectionState      = $replica.ConnectionState
                        ReplicaJoinState            = $replica.JoinState
                        ReplicaSynchronizationState = $replica.RollupSynchronizationState
                        DatabaseName                = $databaseReplicaState.AvailabilityDatabaseName
                        SynchronizationState        = $databaseReplicaState.SynchronizationState
                        IsFailoverReady             = $databaseReplicaState.IsFailoverReady
                        IsJoined                    = $databaseReplicaState.IsJoined
                        IsSuspended                 = $databaseReplicaState.IsSuspended
                        SuspendReason               = $databaseReplicaState.SuspendReason
                        EstimatedRecoveryTime       = $databaseReplicaState.EstimatedRecoveryTime
                        EstimatedDataLoss           = $databaseReplicaState.EstimatedDataLoss
                        SynchronizationPerformance  = $databaseReplicaState.SynchronizationPerformance
                        LogSendQueueSize            = $databaseReplicaState.LogSendQueueSize
                        LogSendRate                 = $databaseReplicaState.LogSendRate
                        RedoQueueSize               = $databaseReplicaState.RedoQueueSize
                        RedoRate                    = $databaseReplicaState.RedoRate
                        FileStreamSendRate          = $databaseReplicaState.FileStreamSendRate
                        EndOfLogLSN                 = $databaseReplicaState.EndOfLogLSN
                        RecoveryLSN                 = $databaseReplicaState.RecoveryLSN
                        TruncationLSN               = $databaseReplicaState.TruncationLSN
                        LastCommitLSN               = $databaseReplicaState.LastCommitLSN
                        LastCommitTime              = $databaseReplicaState.LastCommitTime
                        LastHardenedLSN             = $databaseReplicaState.LastHardenedLSN
                        LastHardenedTime            = $databaseReplicaState.LastHardenedTime
                        LastReceivedLSN             = $databaseReplicaState.LastReceivedLSN
                        LastReceivedTime            = $databaseReplicaState.LastReceivedTime
                        LastRedoneLSN               = $databaseReplicaState.LastRedoneLSN
                        LastRedoneTime              = $databaseReplicaState.LastRedoneTime
                        LastSentLSN                 = $databaseReplicaState.LastSentLSN
                        LastSentTime                = $databaseReplicaState.LastSentTime
                    }
                }
            }
        }
    }
} $SqlInstance $SqlCredential $AvailabilityGroup $Database $InputObject $EnableException $__boundSqlInstance $__boundInputObject $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
