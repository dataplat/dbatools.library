#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;
using Microsoft.SqlServer.Management.Smo;
// the sibling namespace Dataplat.Dbatools.Database shadows Smo.Database inside
// Dataplat.Dbatools.Commands (the W1-020 namespace trap) - alias the SMO type
using SmoDatabase = Microsoft.SqlServer.Management.Smo.Database;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Finds exact and overlapping duplicate indexes. Port of
/// public/Find-DbaDbDuplicateIndex.ps1 (W1-053). Connect rides NestedConnect (W1-046
/// seam); the version-split query (versionMajor 9 vs later, exact vs -IncludeOverlapping)
/// runs through the REAL Database.Query ETS ScriptMethod in an engine hop under
/// EngineTryScope (the function's try{}), its output streaming as the command's output.
/// The catch's Stop-Function has no -ErrorRecord and no -Continue: the plain "Query
/// failure" warning arms the interrupt (later pipeline items return through the
/// Test-FunctionInterrupt guard) while the db loop proceeds. The verbose line interpolates
/// the SMO Database object ("[name]" brackets). The four SQL literals are extracted
/// byte-verbatim from the function source.
/// Surface pinned by migration/baselines/Find-DbaDbDuplicateIndex.json.
/// </summary>
[Cmdlet(VerbsCommon.Find, "DbaDbDuplicateIndex")]
public sealed class FindDbaDbDuplicateIndexCommand : DbaInstanceCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public override DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public override PSCredential? SqlCredential { get; set; }

    /// <summary>The database(s) to process.</summary>
    [Parameter(Position = 2)]
    public object[]? Database { get; set; }

    /// <summary>Includes overlapping (partial duplicate) indexes.</summary>
    [Parameter]
    public SwitchParameter IncludeOverlapping { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        // PS: if (Test-FunctionInterrupt) { return }
        if (Interrupted)
            return;

        foreach (DbaInstanceParameter instance in SqlInstance)
        {
            Hashtable connectParams = new Hashtable();
            connectParams["SqlInstance"] = instance;
            connectParams["SqlCredential"] = SqlCredential;
            connectParams["MinimumVersion"] = 9;
            NestedConnect.Outcome connection = NestedConnect.Connect(this, connectParams);
            if (!connection.Ok)
            {
                StopFunction("Failure", target: instance, errorRecord: connection.Failure, category: ErrorCategory.ConnectionError, continueLoop: true);
                continue;
            }
            Server server = connection.Server!;

            List<SmoDatabase> databases = new List<SmoDatabase>();
            foreach (SmoDatabase candidate in server.Databases)
            {
                if (PsOps.IsTrue(Database))
                {
                    if (PsOps.In(candidate.Name, Database))
                        databases.Add(candidate);
                }
                else if (candidate.IsAccessible)
                {
                    databases.Add(candidate);
                }
            }

            foreach (SmoDatabase db in databases)
            {
                try
                {
                    // PS: "$db" interpolates the SMO Database object ("[name]").
                    WriteMessage(MessageLevel.Verbose, "Getting indexes from database '" + PsText(db) + "'.");

                    string query = server.VersionMajor == 9
                        ? (IncludeOverlapping.ToBool() ? OverlappingQuery2005 : ExactDuplicateQuery2005)
                        : (IncludeOverlapping.ToBool() ? OverlappingQuery : ExactDuplicateQuery);

                    // PS: $db.Query($query) - the ETS ScriptMethod rides the engine under
                    // the function try{}'s flag; output streams as the command's output.
                    using EngineTryScope tryScope = EngineTryScope.Enter(this);
                    foreach (PSObject? item in NestedCommand.InvokeScoped(this, DatabaseQueryScript, db, query))
                        WriteObject(item);
                }
                catch (PipelineStoppedException)
                {
                    throw;
                }
                catch (Exception)
                {
                    // PS: Stop-Function -Message "Query failure" -Target $db - no record,
                    // no -Continue: plain warning, interrupt armed, loop proceeds.
                    StopFunction("Query failure", target: db);
                }
            }
        }
    }

    /// <summary>PS string interpolation of a value.</summary>
    private static string PsText(object? value)
    {
        if (value is null)
            return "";
        return PSObject.AsPSObject(value).ToString();
    }

    // PS: $db.Query($query) - the statement runs on the engine (ETS dispatch, real-$null
    // emission, silent inner bookkeeping - the W1-046 seam).
    private const string DatabaseQueryScript = """
param($db, $query)
$db.Query($query)
""";

    private const string ExactDuplicateQuery2005 = """

            WITH CTE_IndexCols
            AS (
                SELECT i.[object_id]
                    ,i.index_id
                    ,OBJECT_SCHEMA_NAME(i.[object_id]) AS SchemaName
                    ,OBJECT_NAME(i.[object_id]) AS TableName
                    ,NAME AS IndexName
                    ,ISNULL(STUFF((
                                SELECT ', ' + col.NAME + ' ' + CASE
                                        WHEN idxCol.is_descending_key = 1
                                            THEN 'DESC'
                                        ELSE 'ASC'
                                        END -- Include column order (ASC / DESC)
                                FROM sys.index_columns idxCol
                                INNER JOIN sys.columns col ON idxCol.[object_id] = col.[object_id]
                                    AND idxCol.column_id = col.column_id
                                WHERE i.[object_id] = idxCol.[object_id]
                                    AND i.index_id = idxCol.index_id
                                    AND idxCol.is_included_column = 0
                                ORDER BY idxCol.key_ordinal
                                FOR XML PATH('')
                                ), 1, 2, ''), '') AS KeyColumns
                    ,ISNULL(STUFF((
                                SELECT ', ' + col.NAME + ' ' + CASE
                                        WHEN idxCol.is_descending_key = 1
                                            THEN 'DESC'
                                        ELSE 'ASC'
                                        END -- Include column order (ASC / DESC)
                                FROM sys.index_columns idxCol
                                INNER JOIN sys.columns col ON idxCol.[object_id] = col.[object_id]
                                    AND idxCol.column_id = col.column_id
                                WHERE i.[object_id] = idxCol.[object_id]
                                    AND i.index_id = idxCol.index_id
                                    AND idxCol.is_included_column = 1
                                ORDER BY idxCol.key_ordinal
                                FOR XML PATH('')
                                ), 1, 2, ''), '') AS IncludedColumns
                    ,i.[type_desc] AS IndexType
                    ,i.is_disabled AS IsDisabled
                    ,i.is_unique AS IsUnique
                FROM sys.indexes AS i
                WHERE i.index_id > 0 -- Exclude HEAPS
                    AND i.[type_desc] IN (
                        'CLUSTERED'
                        ,'NONCLUSTERED'
                        )
                    AND OBJECT_SCHEMA_NAME(i.[object_id]) <> 'sys'
                )
                ,CTE_IndexSpace
            AS (
                SELECT s.[object_id]
                    ,s.index_id
                    ,SUM(s.[used_page_count]) * 8 / 1024.0 AS IndexSizeMB
                    ,SUM(p.[rows]) AS [RowCount]
                FROM sys.dm_db_partition_stats AS s
                INNER JOIN sys.partitions p WITH (NOLOCK) ON s.[partition_id] = p.[partition_id]
                    AND s.[object_id] = p.[object_id]
                    AND s.index_id = p.index_id
                WHERE s.index_id > 0 -- Exclude HEAPS
                    AND OBJECT_SCHEMA_NAME(s.[object_id]) <> 'sys'
                GROUP BY s.[object_id]
                    ,s.index_id
                )
            SELECT DB_NAME() AS DatabaseName
                ,CI1.SchemaName + '.' + CI1.TableName AS 'TableName'
                ,CI1.IndexName
                ,CI1.KeyColumns
                ,CI1.IncludedColumns
                ,CI1.IndexType
                ,COALESCE(CSPC.IndexSizeMB,0) AS 'IndexSizeMB'
                ,COALESCE(CSPC.[RowCount],0) AS 'RowCount'
                ,CI1.IsDisabled
                ,CI1.IsUnique
            FROM CTE_IndexCols AS CI1
            LEFT JOIN CTE_IndexSpace AS CSPC ON CI1.[object_id] = CSPC.[object_id]
                AND CI1.index_id = CSPC.index_id
            WHERE EXISTS (
                    SELECT 1
                    FROM CTE_IndexCols CI2
                    WHERE CI1.SchemaName = CI2.SchemaName
                        AND CI1.TableName = CI2.TableName
                        AND CI1.KeyColumns = CI2.KeyColumns
                        AND CI1.IncludedColumns = CI2.IncludedColumns
                        AND CI1.IndexName <> CI2.IndexName
                    )
""";

    private const string OverlappingQuery2005 = """

            WITH CTE_IndexCols
            AS (
                SELECT i.[object_id]
                    ,i.index_id
                    ,OBJECT_SCHEMA_NAME(i.[object_id]) AS SchemaName
                    ,OBJECT_NAME(i.[object_id]) AS TableName
                    ,NAME AS IndexName
                    ,ISNULL(STUFF((
                                SELECT ', ' + col.NAME + ' ' + CASE
                                        WHEN idxCol.is_descending_key = 1
                                            THEN 'DESC'
                                        ELSE 'ASC'
                                        END -- Include column order (ASC / DESC)
                                FROM sys.index_columns idxCol
                                INNER JOIN sys.columns col ON idxCol.[object_id] = col.[object_id]
                                    AND idxCol.column_id = col.column_id
                                WHERE i.[object_id] = idxCol.[object_id]
                                    AND i.index_id = idxCol.index_id
                                    AND idxCol.is_included_column = 0
                                ORDER BY idxCol.key_ordinal
                                FOR XML PATH('')
                                ), 1, 2, ''), '') AS KeyColumns
                    ,ISNULL(STUFF((
                                SELECT ', ' + col.NAME + ' ' + CASE
                                        WHEN idxCol.is_descending_key = 1
                                            THEN 'DESC'
                                        ELSE 'ASC'
                                        END -- Include column order (ASC / DESC)
                                FROM sys.index_columns idxCol
                                INNER JOIN sys.columns col ON idxCol.[object_id] = col.[object_id]
                                    AND idxCol.column_id = col.column_id
                                WHERE i.[object_id] = idxCol.[object_id]
                                    AND i.index_id = idxCol.index_id
                                    AND idxCol.is_included_column = 1
                                ORDER BY idxCol.key_ordinal
                                FOR XML PATH('')
                                ), 1, 2, ''), '') AS IncludedColumns
                    ,i.[type_desc] AS IndexType
                    ,i.is_disabled AS IsDisabled
                    ,i.is_unique AS IsUnique
                FROM sys.indexes AS i
                WHERE i.index_id > 0 -- Exclude HEAPS
                    AND i.[type_desc] IN (
                        'CLUSTERED'
                        ,'NONCLUSTERED'
                        )
                    AND OBJECT_SCHEMA_NAME(i.[object_id]) <> 'sys'
                )
                ,CTE_IndexSpace
            AS (
                SELECT s.[object_id]
                    ,s.index_id
                    ,SUM(s.[used_page_count]) * 8 / 1024.0 AS IndexSizeMB
                    ,SUM(p.[rows]) AS [RowCount]
                FROM sys.dm_db_partition_stats AS s
                INNER JOIN sys.partitions p WITH (NOLOCK) ON s.[partition_id] = p.[partition_id]
                    AND s.[object_id] = p.[object_id]
                    AND s.index_id = p.index_id
                WHERE s.index_id > 0 -- Exclude HEAPS
                    AND OBJECT_SCHEMA_NAME(s.[object_id]) <> 'sys'
                GROUP BY s.[object_id]
                    ,s.index_id
                )
            SELECT DB_NAME() AS DatabaseName
                ,CI1.SchemaName + '.' + CI1.TableName AS 'TableName'
                ,CI1.IndexName
                ,CI1.KeyColumns
                ,CI1.IncludedColumns
                ,CI1.IndexType
                ,COALESCE(CSPC.IndexSizeMB,0) AS 'IndexSizeMB'
                ,COALESCE(CSPC.[RowCount],0) AS 'RowCount'
                ,CI1.IsDisabled
                ,CI1.IsUnique
            FROM CTE_IndexCols AS CI1
            LEFT JOIN CTE_IndexSpace AS CSPC ON CI1.[object_id] = CSPC.[object_id]
                AND CI1.index_id = CSPC.index_id
            WHERE EXISTS (
                    SELECT 1
                    FROM CTE_IndexCols CI2
                    WHERE CI1.SchemaName = CI2.SchemaName
                        AND CI1.TableName = CI2.TableName
                        AND (
                            (
                                CI1.KeyColumns LIKE CI2.KeyColumns + '%'
                                AND SUBSTRING(CI1.KeyColumns, LEN(CI2.KeyColumns) + 1, 1) = ' '
                                )
                            OR (
                                CI2.KeyColumns LIKE CI1.KeyColumns + '%'
                                AND SUBSTRING(CI2.KeyColumns, LEN(CI1.KeyColumns) + 1, 1) = ' '
                                )
                            )
                        AND CI1.IndexName <> CI2.IndexName
                    )
""";

    private const string ExactDuplicateQuery = """

            WITH CTE_IndexCols
            AS (
                SELECT i.[object_id]
                    ,i.index_id
                    ,OBJECT_SCHEMA_NAME(i.[object_id]) AS SchemaName
                    ,OBJECT_NAME(i.[object_id]) AS TableName
                    ,NAME AS IndexName
                    ,ISNULL(STUFF((
                                SELECT ', ' + col.NAME + ' ' + CASE
                                        WHEN idxCol.is_descending_key = 1
                                            THEN 'DESC'
                                        ELSE 'ASC'
                                        END -- Include column order (ASC / DESC)
                                FROM sys.index_columns idxCol
                                INNER JOIN sys.columns col ON idxCol.[object_id] = col.[object_id]
                                    AND idxCol.column_id = col.column_id
                                WHERE i.[object_id] = idxCol.[object_id]
                                    AND i.index_id = idxCol.index_id
                                    AND idxCol.is_included_column = 0
                                ORDER BY idxCol.key_ordinal
                                FOR XML PATH('')
                                ), 1, 2, ''), '') AS KeyColumns
                    ,ISNULL(STUFF((
                                SELECT ', ' + col.NAME + ' ' + CASE
                                        WHEN idxCol.is_descending_key = 1
                                            THEN 'DESC'
                                        ELSE 'ASC'
                                        END -- Include column order (ASC / DESC)
                                FROM sys.index_columns idxCol
                                INNER JOIN sys.columns col ON idxCol.[object_id] = col.[object_id]
                                    AND idxCol.column_id = col.column_id
                                WHERE i.[object_id] = idxCol.[object_id]
                                    AND i.index_id = idxCol.index_id
                                    AND idxCol.is_included_column = 1
                                ORDER BY idxCol.key_ordinal
                                FOR XML PATH('')
                                ), 1, 2, ''), '') AS IncludedColumns
                    ,i.[type_desc] AS IndexType
                    ,i.is_disabled AS IsDisabled
                    ,i.has_filter AS IsFiltered
                    ,i.is_unique AS IsUnique
                FROM sys.indexes AS i
                WHERE i.index_id > 0 -- Exclude HEAPS
                    AND i.[type_desc] IN (
                        'CLUSTERED'
                        ,'NONCLUSTERED'
                        )
                    AND OBJECT_SCHEMA_NAME(i.[object_id]) <> 'sys'
                )
                ,CTE_IndexSpace
            AS (
                SELECT s.[object_id]
                    ,s.index_id
                    ,SUM(s.[used_page_count]) * 8 / 1024.0 AS IndexSizeMB
                    ,SUM(p.[rows]) AS [RowCount]
                    ,p.data_compression_desc AS CompressionDescription
                FROM sys.dm_db_partition_stats AS s
                INNER JOIN sys.partitions p WITH (NOLOCK) ON s.[partition_id] = p.[partition_id]
                    AND s.[object_id] = p.[object_id]
                    AND s.index_id = p.index_id
                WHERE s.index_id > 0 -- Exclude HEAPS
                    AND OBJECT_SCHEMA_NAME(s.[object_id]) <> 'sys'
                GROUP BY s.[object_id]
                    ,s.index_id
                    ,p.data_compression_desc
                )
            SELECT DB_NAME() AS DatabaseName
                ,CI1.SchemaName + '.' + CI1.TableName AS 'TableName'
                ,CI1.IndexName
                ,CI1.KeyColumns
                ,CI1.IncludedColumns
                ,CI1.IndexType
                ,COALESCE(CSPC.IndexSizeMB,0) AS 'IndexSizeMB'
                ,COALESCE(CSPC.CompressionDescription, 'NONE') AS 'CompressionDescription'
                ,COALESCE(CSPC.[RowCount],0) AS 'RowCount'
                ,CI1.IsDisabled
                ,CI1.IsFiltered
                ,CI1.IsUnique
            FROM CTE_IndexCols AS CI1
            LEFT JOIN CTE_IndexSpace AS CSPC ON CI1.[object_id] = CSPC.[object_id]
                AND CI1.index_id = CSPC.index_id
            WHERE EXISTS (
                    SELECT 1
                    FROM CTE_IndexCols CI2
                    WHERE CI1.SchemaName = CI2.SchemaName
                        AND CI1.TableName = CI2.TableName
                        AND CI1.KeyColumns = CI2.KeyColumns
                        AND CI1.IncludedColumns = CI2.IncludedColumns
                        AND CI1.IsFiltered = CI2.IsFiltered
                        AND CI1.IndexName <> CI2.IndexName
                    )
""";

    private const string OverlappingQuery = """

            WITH CTE_IndexCols AS
            (
                SELECT
                        i.[object_id]
                        ,i.index_id
                        ,OBJECT_SCHEMA_NAME(i.[object_id]) AS SchemaName
                        ,OBJECT_NAME(i.[object_id]) AS TableName
                        ,Name AS IndexName
                        ,ISNULL(STUFF((SELECT ', ' + col.NAME + ' ' + CASE
                                                                    WHEN idxCol.is_descending_key = 1 THEN 'DESC'
                                                                    ELSE 'ASC'
                                                                END -- Include column order (ASC / DESC)
                                FROM sys.index_columns idxCol
                                    INNER JOIN sys.columns col
                                    ON idxCol.[object_id] = col.[object_id]
                                    AND idxCol.column_id = col.column_id
                                WHERE i.[object_id] = idxCol.[object_id]
                                AND i.index_id = idxCol.index_id
                                AND idxCol.is_included_column = 0
                                ORDER BY idxCol.key_ordinal
                        FOR XML PATH('')), 1, 2, ''), '') AS KeyColumns
                        ,ISNULL(STUFF((SELECT ', ' + col.NAME + ' ' + CASE
                                                                    WHEN idxCol.is_descending_key = 1 THEN 'DESC'
                                                                    ELSE 'ASC'
                                                                END -- Include column order (ASC / DESC)
                                FROM sys.index_columns idxCol
                                    INNER JOIN sys.columns col
                                    ON idxCol.[object_id] = col.[object_id]
                                    AND idxCol.column_id = col.column_id
                                WHERE i.[object_id] = idxCol.[object_id]
                                AND i.index_id = idxCol.index_id
                                AND idxCol.is_included_column = 1
                                ORDER BY idxCol.key_ordinal
                        FOR XML PATH('')), 1, 2, ''), '') AS IncludedColumns
                        ,i.[type_desc] AS IndexType
                        ,i.is_disabled AS IsDisabled
                        ,i.has_filter AS IsFiltered
                        ,i.is_unique AS IsUnique
                FROM sys.indexes AS i
                WHERE i.index_id > 0 -- Exclude HEAPS
                AND i.[type_desc] IN ('CLUSTERED', 'NONCLUSTERED')
                AND OBJECT_SCHEMA_NAME(i.[object_id]) <> 'sys'
            ),
            CTE_IndexSpace AS
            (
            SELECT
                        s.[object_id]
                        ,s.index_id
                        ,SUM(s.[used_page_count]) * 8 / 1024.0 AS IndexSizeMB
                        ,SUM(p.[rows]) AS [RowCount]
                        ,p.data_compression_desc AS CompressionDescription
                FROM sys.dm_db_partition_stats AS s
                    INNER JOIN sys.partitions p WITH (NOLOCK)
                    ON s.[partition_id] = p.[partition_id]
                    AND s.[object_id] = p.[object_id]
                    AND s.index_id = p.index_id
                WHERE s.index_id > 0 -- Exclude HEAPS
                    AND OBJECT_SCHEMA_NAME(s.[object_id]) <> 'sys'
                GROUP BY s.[object_id], s.index_id, p.data_compression_desc
            )
            SELECT
                    DB_NAME() AS DatabaseName
                    ,CI1.SchemaName + '.' + CI1.TableName AS 'TableName'
                    ,CI1.IndexName
                    ,CI1.KeyColumns
                    ,CI1.IncludedColumns
                    ,CI1.IndexType
                    ,COALESCE(CSPC.IndexSizeMB,0) AS 'IndexSizeMB'
                    ,COALESCE(CSPC.CompressionDescription, 'NONE') AS 'CompressionDescription'
                    ,COALESCE(CSPC.[RowCount],0) AS 'RowCount'
                    ,CI1.IsDisabled
                    ,CI1.IsFiltered
                    ,CI1.IsUnique
            FROM CTE_IndexCols AS CI1
                LEFT JOIN CTE_IndexSpace AS CSPC
                ON CI1.[object_id] = CSPC.[object_id]
                AND CI1.index_id = CSPC.index_id
            WHERE EXISTS (SELECT 1
                            FROM CTE_IndexCols CI2
                        WHERE CI1.SchemaName = CI2.SchemaName
                            AND CI1.TableName = CI2.TableName
                            AND (
                                        (CI1.KeyColumns LIKE CI2.KeyColumns + '%' AND SUBSTRING(CI1.KeyColumns,LEN(CI2.KeyColumns)+1,1) = ' ')
                                    OR (CI2.KeyColumns LIKE CI1.KeyColumns + '%' AND SUBSTRING(CI2.KeyColumns,LEN(CI1.KeyColumns)+1,1) = ' ')
                                )
                            AND CI1.IsFiltered = CI2.IsFiltered
                            AND CI1.IndexName <> CI2.IndexName
                        )
""";

}
