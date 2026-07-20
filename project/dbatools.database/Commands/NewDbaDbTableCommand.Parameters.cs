#nullable enable

using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

// Parameter surface - split out per the repo 400-line file limit.
public sealed partial class NewDbaDbTableCommand
{
    /// <summary>SqlInstance.</summary>
    [Parameter(Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>SqlCredential.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Database.</summary>
    [Parameter(Position = 2)]
    [PsStringArrayCast]
    public string[]? Database { get; set; }

    /// <summary>Name.</summary>
    [Parameter(Position = 3)]
    [Alias("Table")]
    [PsStringCast]
    public string? Name { get; set; }

    /// <summary>Schema.</summary>
    [Parameter(Position = 4)]
    [PsStringCast]
    public string Schema { get; set; } = "dbo";

    /// <summary>ColumnMap.</summary>
    [Parameter(Position = 5)]
    public Hashtable[]? ColumnMap { get; set; }

    /// <summary>ColumnObject.</summary>
    [Parameter(Position = 6)]
    public Microsoft.SqlServer.Management.Smo.Column[]? ColumnObject { get; set; }

    /// <summary>AnsiNullsStatus.</summary>
    [Parameter]
    public SwitchParameter AnsiNullsStatus { get; set; }

    /// <summary>ChangeTrackingEnabled.</summary>
    [Parameter]
    public SwitchParameter ChangeTrackingEnabled { get; set; }

    /// <summary>DataSourceName.</summary>
    [Parameter(Position = 7)]
    [PsStringCast]
    public string? DataSourceName { get; set; }

    /// <summary>Durability.</summary>
    [Parameter(Position = 8)]
    public Microsoft.SqlServer.Management.Smo.DurabilityType Durability { get; set; }

    /// <summary>ExternalTableDistribution.</summary>
    [Parameter(Position = 9)]
    public Microsoft.SqlServer.Management.Smo.ExternalTableDistributionType ExternalTableDistribution { get; set; }

    /// <summary>FileFormatName.</summary>
    [Parameter(Position = 10)]
    [PsStringCast]
    public string? FileFormatName { get; set; }

    /// <summary>FileGroup.</summary>
    [Parameter(Position = 11)]
    [PsStringCast]
    public string? FileGroup { get; set; }

    /// <summary>FileStreamFileGroup.</summary>
    [Parameter(Position = 12)]
    [PsStringCast]
    public string? FileStreamFileGroup { get; set; }

    /// <summary>FileStreamPartitionScheme.</summary>
    [Parameter(Position = 13)]
    [PsStringCast]
    public string? FileStreamPartitionScheme { get; set; }

    /// <summary>FileTableDirectoryName.</summary>
    [Parameter(Position = 14)]
    [PsStringCast]
    public string? FileTableDirectoryName { get; set; }

    /// <summary>FileTableNameColumnCollation.</summary>
    [Parameter(Position = 15)]
    [PsStringCast]
    public string? FileTableNameColumnCollation { get; set; }

    /// <summary>FileTableNamespaceEnabled.</summary>
    [Parameter]
    public SwitchParameter FileTableNamespaceEnabled { get; set; }

    /// <summary>HistoryTableName.</summary>
    [Parameter(Position = 16)]
    [PsStringCast]
    public string? HistoryTableName { get; set; }

    /// <summary>HistoryTableSchema.</summary>
    [Parameter(Position = 17)]
    [PsStringCast]
    public string? HistoryTableSchema { get; set; }

    /// <summary>IsExternal.</summary>
    [Parameter]
    public SwitchParameter IsExternal { get; set; }

    /// <summary>IsFileTable.</summary>
    [Parameter]
    public SwitchParameter IsFileTable { get; set; }

    /// <summary>IsMemoryOptimized.</summary>
    [Parameter]
    public SwitchParameter IsMemoryOptimized { get; set; }

    /// <summary>IsSystemVersioned.</summary>
    [Parameter]
    public SwitchParameter IsSystemVersioned { get; set; }

    /// <summary>Location.</summary>
    [Parameter(Position = 18)]
    [PsStringCast]
    public string? Location { get; set; }

    /// <summary>LockEscalation.</summary>
    [Parameter(Position = 19)]
    public Microsoft.SqlServer.Management.Smo.LockEscalationType LockEscalation { get; set; }

    /// <summary>Owner.</summary>
    [Parameter(Position = 20)]
    [PsStringCast]
    public string? Owner { get; set; }

    /// <summary>PartitionScheme.</summary>
    [Parameter(Position = 21)]
    [PsStringCast]
    public string? PartitionScheme { get; set; }

    /// <summary>QuotedIdentifierStatus.</summary>
    [Parameter]
    public SwitchParameter QuotedIdentifierStatus { get; set; }

    /// <summary>RejectSampleValue.</summary>
    [Parameter(Position = 22)]
    public double RejectSampleValue { get; set; }

    /// <summary>RejectType.</summary>
    [Parameter(Position = 23)]
    public Microsoft.SqlServer.Management.Smo.ExternalTableRejectType RejectType { get; set; }

    /// <summary>RejectValue.</summary>
    [Parameter(Position = 24)]
    public double RejectValue { get; set; }

    /// <summary>RemoteDataArchiveDataMigrationState.</summary>
    [Parameter(Position = 25)]
    public Microsoft.SqlServer.Management.Smo.RemoteDataArchiveMigrationState RemoteDataArchiveDataMigrationState { get; set; }

    /// <summary>RemoteDataArchiveEnabled.</summary>
    [Parameter]
    public SwitchParameter RemoteDataArchiveEnabled { get; set; }

    /// <summary>RemoteDataArchiveFilterPredicate.</summary>
    [Parameter(Position = 26)]
    [PsStringCast]
    public string? RemoteDataArchiveFilterPredicate { get; set; }

    /// <summary>RemoteObjectName.</summary>
    [Parameter(Position = 27)]
    [PsStringCast]
    public string? RemoteObjectName { get; set; }

    /// <summary>RemoteSchemaName.</summary>
    [Parameter(Position = 28)]
    [PsStringCast]
    public string? RemoteSchemaName { get; set; }

    /// <summary>RemoteTableName.</summary>
    [Parameter(Position = 29)]
    [PsStringCast]
    public string? RemoteTableName { get; set; }

    /// <summary>RemoteTableProvisioned.</summary>
    [Parameter]
    public SwitchParameter RemoteTableProvisioned { get; set; }

    /// <summary>ShardingColumnName.</summary>
    [Parameter(Position = 30)]
    [PsStringCast]
    public string? ShardingColumnName { get; set; }

    /// <summary>TextFileGroup.</summary>
    [Parameter(Position = 31)]
    [PsStringCast]
    public string? TextFileGroup { get; set; }

    /// <summary>TrackColumnsUpdatedEnabled.</summary>
    [Parameter]
    public SwitchParameter TrackColumnsUpdatedEnabled { get; set; }

    /// <summary>HistoryRetentionPeriod.</summary>
    [Parameter(Position = 32)]
    public int HistoryRetentionPeriod { get; set; }

    /// <summary>HistoryRetentionPeriodUnit.</summary>
    [Parameter(Position = 33)]
    public Microsoft.SqlServer.Management.Smo.TemporalHistoryRetentionPeriodUnit HistoryRetentionPeriodUnit { get; set; }

    /// <summary>DwTableDistribution.</summary>
    [Parameter(Position = 34)]
    public Microsoft.SqlServer.Management.Smo.DwTableDistributionType DwTableDistribution { get; set; }

    /// <summary>RejectedRowLocation.</summary>
    [Parameter(Position = 35)]
    [PsStringCast]
    public string? RejectedRowLocation { get; set; }

    /// <summary>OnlineHeapOperation.</summary>
    [Parameter]
    public SwitchParameter OnlineHeapOperation { get; set; }

    /// <summary>LowPriorityMaxDuration.</summary>
    [Parameter(Position = 36)]
    public int LowPriorityMaxDuration { get; set; }

    /// <summary>DataConsistencyCheck.</summary>
    [Parameter]
    public SwitchParameter DataConsistencyCheck { get; set; }

    /// <summary>LowPriorityAbortAfterWait.</summary>
    [Parameter(Position = 37)]
    public Microsoft.SqlServer.Management.Smo.AbortAfterWait LowPriorityAbortAfterWait { get; set; }

    /// <summary>MaximumDegreeOfParallelism.</summary>
    [Parameter(Position = 38)]
    public int MaximumDegreeOfParallelism { get; set; }

    /// <summary>IsNode.</summary>
    [Parameter]
    public SwitchParameter IsNode { get; set; }

    /// <summary>IsEdge.</summary>
    [Parameter]
    public SwitchParameter IsEdge { get; set; }

    /// <summary>IsVarDecimalStorageFormatEnabled.</summary>
    [Parameter]
    public SwitchParameter IsVarDecimalStorageFormatEnabled { get; set; }

    /// <summary>Passthru.</summary>
    [Parameter]
    public SwitchParameter Passthru { get; set; }

    /// <summary>InputObject.</summary>
    [Parameter(ValueFromPipeline = true, Position = 39)]
    public Microsoft.SqlServer.Management.Smo.Database[]? InputObject { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // $object / $schemaObject carried across records for the catch-cleanup failure path (codex r1):
    // if the try throws before those assignments, the SOURCE's persistent process scope still holds
    // the PREVIOUS record's objects and the cleanup drops them. Opaque to C#.
    private Hashtable? _state;
}
