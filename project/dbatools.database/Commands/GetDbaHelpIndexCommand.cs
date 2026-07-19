#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Returns index usage, size, and statistics metadata for database objects. Port of public/Get-DbaHelpIndex.ps1;
/// the workflow remains a module-scoped PowerShell compatibility hop.
///
/// A process-only port. The source begin block (~400 lines) builds the large SQL query state
/// ($TablePredicate, $FragSelectColumn/$FragJoin, $OutputProperties, and the assembled $SizesQuery) from the
/// parameters (ObjectName, IncludeStats, IncludeDataTypes, IncludeFragmentation). All of it is param-derived with
/// NO cross-record accumulation, so the WHOLE begin block is PREPENDED (verbatim) into the process hop and re-runs
/// per pipeline record - behaviorally identical (the prepend pattern). Four switches (IncludeStats, IncludeDataTypes,
/// Raw, IncludeFragmentation) are consumed as VALUES (if ($IncludeFragmentation), if ($Raw), ...), so they are passed
/// as marshaled bools (.ToBool()) into UNTYPED inner params - typing them [switch] would shift positional binding
/// (switch-in-hop-param law; binding-probed BOUND-OK). No Test-Bound. Edits: -FunctionName Get-DbaHelpIndex on the
/// process's two Write-Message and three Stop-Function (all -Continue). No ShouldProcess. Surface pinned by
/// migration/baselines/Get-DbaHelpIndex.json (positions 0-5, InputObject VFP pos4, four non-positional switches, no ShouldProcess).
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaHelpIndex")]
public sealed class GetDbaHelpIndexCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances (SQL 2008+).</summary>
    [Parameter(Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The database(s) to process.</summary>
    [Parameter(Position = 2)]
    public object[]? Database { get; set; }

    /// <summary>The database(s) to exclude.</summary>
    [Parameter(Position = 3)]
    public object[]? ExcludeDatabase { get; set; }

    /// <summary>Database object(s) piped in from Get-DbaDatabase.</summary>
    [Parameter(ValueFromPipeline = true, Position = 4)]
    public Microsoft.SqlServer.Management.Smo.Database[]? InputObject { get; set; }

    /// <summary>Limit results to the specified object (table) name.</summary>
    [Parameter(Position = 5)]
    public string? ObjectName { get; set; }

    /// <summary>Include statistics rows in the results.</summary>
    [Parameter]
    public SwitchParameter IncludeStats { get; set; }

    /// <summary>Include index key/include column data types.</summary>
    [Parameter]
    public SwitchParameter IncludeDataTypes { get; set; }

    /// <summary>Return raw (unformatted) numeric values.</summary>
    [Parameter]
    public SwitchParameter Raw { get; set; }

    /// <summary>Include index fragmentation percentage (expensive).</summary>
    [Parameter]
    public SwitchParameter IncludeFragmentation { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, SqlCredential, Database, ExcludeDatabase, InputObject, ObjectName,
            IncludeStats.ToBool(), IncludeDataTypes.ToBool(), Raw.ToBool(), IncludeFragmentation.ToBool(),
            EnableException.ToBool(), BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
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
    // PS: the source begin block (the ~400-line SQL query build, VERBATIM) PREPENDED to the process block. Process
    // edits: -FunctionName Get-DbaHelpIndex on the two Write-Message and three Stop-Function (all -Continue). The four
    // switches arrive as marshaled bools; their inner params are UNTYPED (if ($IncludeFragmentation)/if ($Raw)/... work).
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $ExcludeDatabase, $InputObject, $ObjectName, $IncludeStats, $IncludeDataTypes, $Raw, $IncludeFragmentation, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [object[]]$Database, [object[]]$ExcludeDatabase, [Microsoft.SqlServer.Management.Smo.Database[]]$InputObject, [string]$ObjectName, $IncludeStats, $IncludeDataTypes, $Raw, $IncludeFragmentation, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }


        #Add the table predicate to the query
        if (!$ObjectName) {
            $TablePredicate = "DECLARE @TableName NVARCHAR(256);"
        } else {
            $TablePredicate = "DECLARE @TableName NVARCHAR(256); SET @TableName = '$($ObjectName -replace "'", "''")';";
        }

        #Add Fragmentation info if requested
        $FragSelectColumn = ", NULL as avg_fragmentation_in_percent"
        $FragJoin = ''
        $OutputProperties = 'Database,Object,Index,IndexType,KeyColumns,IncludeColumns,FilterDefinition,DataCompression,IndexReads,IndexUpdates,SizeKB,IndexRows,IndexLookups,MostRecentlyUsed,StatsSampleRows,StatsRowMods,HistogramSteps,StatsLastUpdated'
        if ($IncludeFragmentation) {
            $FragSelectColumn = ', pstat.avg_fragmentation_in_percent'
            $FragJoin = "LEFT JOIN sys.dm_db_index_physical_stats(DB_ID(), NULL, NULL, NULL , 'DETAILED') pstat
             ON pstat.database_id = ustat.database_id
             AND pstat.object_id = ustat.object_id
             AND pstat.index_id = ustat.index_id"
            $OutputProperties = 'Database,Object,Index,IndexType,KeyColumns,IncludeColumns,FilterDefinition,DataCompression,IndexReads,IndexUpdates,SizeKB,IndexRows,IndexLookups,MostRecentlyUsed,StatsSampleRows,StatsRowMods,HistogramSteps,StatsLastUpdated,IndexFragInPercent'
        }
        $OutputProperties = $OutputProperties.Split(',')
        #Figure out if we are including stats in the results
        if ($IncludeStats) {
            $IncludeStatsPredicate = "";
        } else {
            $IncludeStatsPredicate = "WHERE StatisticsName IS NULL";
        }

        #Data types being returns with the results?
        if ($IncludeDataTypes) {
            $IncludeDataTypesPredicate = 'DECLARE @IncludeDataTypes BIT; SET @IncludeDataTypes = 1';
        } else {
            $IncludeDataTypesPredicate = 'DECLARE @IncludeDataTypes BIT; SET @IncludeDataTypes = 0';
        }

        #region SizesQuery
        $SizesQuery = "
            SET NOCOUNT ON;
            SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

            $TablePredicate
            $IncludeDataTypesPredicate
            ;

        DECLARE @IndexUsageStats TABLE
            (
            object_id INT ,
            index_id INT ,
            user_scans BIGINT ,
            user_seeks BIGINT ,
            user_updates BIGINT ,
            user_lookups BIGINT ,
            last_user_lookup DATETIME2(0) ,
            last_user_scan DATETIME2(0) ,
            last_user_seek DATETIME2(0) ,
            avg_fragmentation_in_percent FLOAT
            );

        DECLARE @StatsInfo TABLE
            (
            object_id INT ,
            stats_id INT ,
            stats_column_name NVARCHAR(128) ,
            stats_column_id INT ,
            stats_name NVARCHAR(128) ,
            stats_last_updated DATETIME2(0) ,
            stats_sampled_rows BIGINT ,
            rowmods BIGINT ,
            histogramsteps INT ,
            StatsRows BIGINT ,
            FullObjectName NVARCHAR(256)
            );

        INSERT  INTO @IndexUsageStats
                ( object_id ,
                index_id ,
                user_scans ,
                user_seeks ,
                user_updates ,
                user_lookups ,
                last_user_lookup ,
                last_user_scan ,
                last_user_seek ,
                avg_fragmentation_in_percent
                )
                SELECT  ustat.object_id ,
                        ustat.index_id ,
                        ustat.user_scans ,
                        ustat.user_seeks ,
                        ustat.user_updates ,
                        ustat.user_lookups ,
                        ustat.last_user_lookup ,
                        ustat.last_user_scan ,
                        ustat.last_user_seek
                        $FragSelectColumn
                FROM    sys.dm_db_index_usage_stats ustat
                $FragJoin
                WHERE   ustat.database_id = DB_ID();

        INSERT  INTO @StatsInfo
                ( object_id ,
                stats_id ,
                stats_column_name ,
                stats_column_id ,
                stats_name ,
                stats_last_updated ,
                stats_sampled_rows ,
                rowmods ,
                histogramsteps ,
                StatsRows ,
                FullObjectName
                )
                SELECT  s.object_id ,
                        s.stats_id ,
                        c.name ,
                        sc.stats_column_id ,
                        s.name ,
                        sp.last_updated ,
                        sp.rows_sampled ,
                        sp.modification_counter ,
                        sp.steps ,
                        sp.rows ,
                        QUOTENAME(sch.name) + '.' + QUOTENAME(t.name) AS FullObjectName
                FROM    sys.stats AS s
                        INNER JOIN sys.stats_columns sc ON s.stats_id = sc.stats_id
                                                        AND s.object_id = sc.object_id
                        INNER JOIN sys.columns c ON c.object_id = sc.object_id
                                                    AND c.column_id = sc.column_id
                        INNER JOIN sys.tables t ON c.object_id = t.object_id
                        INNER JOIN sys.schemas sch ON sch.schema_id = t.schema_id
                        OUTER APPLY sys.dm_db_stats_properties(s.object_id,
                                                            s.stats_id) AS sp
                WHERE   s.object_id = CASE WHEN @TableName IS NULL THEN s.object_id
                                        ELSE OBJECT_ID(@TableName)
                                    END;


        ;
        WITH    cteStatsInfo
                AS ( SELECT   object_id ,
                                si.stats_id ,
                                si.stats_name ,
                                STUFF((SELECT   N', ' + stats_column_name
                                    FROM     @StatsInfo si2
                                    WHERE    si2.object_id = si.object_id
                                                AND si2.stats_id = si.stats_id
                                    ORDER BY si2.stats_column_id
                                FOR   XML PATH(N'') ,
                                        TYPE).value(N'.[1]', N'nvarchar(1000)'), 1,
                                    2, N'') AS StatsColumns ,
                                MAX(si.stats_sampled_rows) AS SampleRows ,
                                MAX(si.rowmods) AS RowMods ,
                                MAX(si.histogramsteps) AS HistogramSteps ,
                                MAX(si.stats_last_updated) AS StatsLastUpdated ,
                                MAX(si.StatsRows) AS StatsRows,
                                FullObjectName
                    FROM     @StatsInfo si
                    GROUP BY si.object_id ,
                                si.stats_id ,
                                si.stats_name ,
                                si.FullObjectName
                    ),
                cteIndexSizes
                AS ( SELECT   object_id ,
                                index_id ,
                                CASE WHEN index_id < 2
                                    THEN ( ( SUM(in_row_data_page_count
                                                + lob_used_page_count
                                                + row_overflow_used_page_count)
                                            * 8192 ) / 1024 )
                                    else ( ( SUM(used_page_count) * 8192 ) / 1024 )
                                END AS SizeKB
                    FROM     sys.dm_db_partition_stats
                    GROUP BY object_id ,
                                index_id
                    ),
                cteRows
                AS ( SELECT   object_id ,
                                index_id ,
                                SUM(rows) AS IndexRows
                    FROM     sys.partitions
                    GROUP BY object_id ,
                                index_id
                    ),
                cteIndex
                AS ( SELECT   OBJECT_NAME(c.object_id) AS ObjectName ,
                                c.object_id ,
                                c.index_id ,
                                i.name COLLATE SQL_Latin1_General_CP1_CI_AS AS name ,
                                c.index_column_id ,
                                c.column_id ,
                                c.is_included_column ,
                                CASE WHEN @IncludeDataTypes = 0
                                        AND c.is_descending_key = 1
                                    THEN sc.name + ' DESC'
                                    WHEN @IncludeDataTypes = 0
                                        AND c.is_descending_key = 0 THEN sc.name
                                    WHEN @IncludeDataTypes = 1
                                        AND c.is_descending_key = 1
                                        AND c.is_included_column = 0
                                    THEN sc.name + ' DESC (' + t.name + ') '
                                    WHEN @IncludeDataTypes = 1
                                        AND c.is_descending_key = 0
                                        AND c.is_included_column = 0
                                    THEN sc.name + ' (' + t.name + ')'
                                    ELSE sc.name
                                END AS ColumnName ,
                                i.filter_definition ,
                                ISNULL(dd.user_scans, 0) AS user_scans ,
                                ISNULL(dd.user_seeks, 0) AS user_seeks ,
                                ISNULL(dd.user_updates, 0) AS user_updates ,
                                ISNULL(dd.user_lookups, 0) AS user_lookups ,
                                CONVERT(DATETIME2(0), ISNULL(dd.last_user_lookup,
                                                            '1901-01-01')) AS LastLookup ,
                                CONVERT(DATETIME2(0), ISNULL(dd.last_user_scan,
                                                            '1901-01-01')) AS LastScan ,
                                CONVERT(DATETIME2(0), ISNULL(dd.last_user_seek,
                                                            '1901-01-01')) AS LastSeek ,
                                i.fill_factor ,
                                c.is_descending_key ,
                                p.data_compression_desc ,
                                i.type_desc ,
                                i.is_unique ,
                                i.is_unique_constraint ,
                                i.is_primary_key ,
                                ci.SizeKB ,
                                cr.IndexRows ,
                                QUOTENAME(sch.name) + '.' + QUOTENAME(tbl.name) AS FullObjectName ,
                                ISNULL(dd.avg_fragmentation_in_percent, 0) AS avg_fragmentation_in_percent
                    FROM     sys.indexes i
                                JOIN sys.index_columns c ON i.object_id = c.object_id
                                                            AND i.index_id = c.index_id
                                JOIN sys.columns sc ON c.object_id = sc.object_id
                                                    AND c.column_id = sc.column_id
                                INNER JOIN sys.tables tbl ON c.object_id = tbl.object_id
                                INNER JOIN sys.schemas sch ON sch.schema_id = tbl.schema_id
                                LEFT JOIN sys.types t ON sc.user_type_id = t.user_type_id
                                LEFT JOIN @IndexUsageStats dd ON i.object_id = dd.object_id
                                                                AND i.index_id = dd.index_id --AND dd.database_id = DB_ID()
                                JOIN sys.partitions p ON i.object_id = p.object_id
                                                        AND i.index_id = p.index_id
                                JOIN cteIndexSizes ci ON i.object_id = ci.object_id
                                                        AND i.index_id = ci.index_id
                                JOIN cteRows cr ON i.object_id = cr.object_id
                                                AND i.index_id = cr.index_id
                    WHERE    i.object_id = CASE WHEN @TableName IS NULL
                                                THEN i.object_id
                                                ELSE OBJECT_ID(@TableName)
                                            END
                    ),
                cteResults
                AS ( SELECT   ci.FullObjectName ,
                                ci.object_id ,
                                MAX(index_id) AS Index_Id ,
                                ci.type_desc
                                + CASE WHEN ci.is_primary_key = 1
                                    THEN ' (PRIMARY KEY)'
                                    WHEN ci.is_unique_constraint = 1
                                    THEN ' (UNIQUE CONSTRAINT)'
                                    WHEN ci.is_unique = 1 THEN ' (UNIQUE)'
                                    ELSE ''
                                END AS IndexType ,
                                name AS IndexName ,
                                STUFF((SELECT   N', ' + ColumnName
                                    FROM     cteIndex ci2
                                    WHERE    ci2.name = ci.name AND ci2.object_id=ci.object_id
                                                AND ci2.is_included_column = 0
                                    GROUP BY ci2.index_column_id ,
                                                ci2.ColumnName
                                    ORDER BY ci2.index_column_id
                                FOR   XML PATH(N'') ,
                                        TYPE).value(N'.[1]', N'nvarchar(1000)'), 1,
                                    2, N'') AS KeyColumns ,
                                ISNULL(STUFF((SELECT    N', ' + ColumnName
                                            FROM      cteIndex ci3
                                            WHERE     ci3.name = ci.name AND ci3.object_id=ci.object_id
                                                        AND ci3.is_included_column = 1
                                            GROUP BY  ci3.index_column_id ,
                                                        ci3.ColumnName
                                            ORDER BY  ci3.index_column_id
                                    FOR   XML PATH(N'') ,
                                                TYPE).value(N'.[1]',
                                                            N'nvarchar(1000)'), 1, 2,
                                            N''), '') AS IncludeColumns ,
                                ISNULL(filter_definition, '') AS FilterDefinition ,
                                ci.fill_factor ,
                                CASE WHEN ci.data_compression_desc = 'NONE' THEN ''
                                    ELSE ci.data_compression_desc
                                END AS DataCompression ,
                                MAX(ci.user_seeks) + MAX(ci.user_scans)
                                + MAX(ci.user_lookups) AS IndexReads ,
                                MAX(ci.user_lookups) AS IndexLookups ,
                                ci.user_updates AS IndexUpdates ,
                                ci.SizeKB AS SizeKB ,
                                ci.IndexRows AS IndexRows ,
                                CASE WHEN LastScan > LastSeek
                                        AND LastScan > LastLookup THEN LastScan
                                    WHEN LastSeek > LastScan
                                        AND LastSeek > LastLookup THEN LastSeek
                                    WHEN LastLookup > LastScan
                                        AND LastLookup > LastSeek THEN LastLookup
                                    ELSE ''
                                END AS MostRecentlyUsed ,
                                AVG(ci.avg_fragmentation_in_percent) AS avg_fragmentation_in_percent
                    FROM     cteIndex ci
                    GROUP BY ci.ObjectName ,
                                ci.name ,
                                ci.filter_definition ,
                                ci.object_id ,
                                ci.LastLookup ,
                                ci.LastSeek ,
                                ci.LastScan ,
                                ci.user_updates ,
                                ci.fill_factor ,
                                ci.data_compression_desc ,
                                ci.type_desc ,
                                ci.is_primary_key ,
                                ci.is_unique ,
                                ci.is_unique_constraint ,
                                ci.SizeKB ,
                                ci.IndexRows ,
                                ci.FullObjectName
                    ),
                AllResults
                AS ( SELECT   c.FullObjectName ,
                                IndexType ,
                                ISNULL(IndexName, si.stats_name) AS IndexName ,
                                NULL as StatisticsName ,
                                ISNULL(KeyColumns, si.StatsColumns) AS KeyColumns ,
                                ISNULL(IncludeColumns, '') AS IncludeColumns ,
                                FilterDefinition ,
                                fill_factor AS [FillFactor] ,
                                DataCompression ,
                                IndexReads ,
                                IndexUpdates ,
                                SizeKB ,
                                IndexRows ,
                                IndexLookups ,
                                MostRecentlyUsed ,
                                SampleRows AS StatsSampleRows ,
                                RowMods AS StatsRowMods ,
                                si.HistogramSteps ,
                                si.StatsLastUpdated ,
                                avg_fragmentation_in_percent AS IndexFragInPercent,
                                1 AS Ordering
                    FROM     cteResults c
                                INNER JOIN cteStatsInfo si ON si.object_id = c.object_id
                                                            AND si.stats_id = c.Index_Id
                    UNION
                    SELECT   QUOTENAME(sch.name) + '.' + QUOTENAME(tbl.name) AS FullObjectName ,
                                '' ,
                                '' ,
                                stats_name ,
                                StatsColumns ,
                                '' ,
                                '' AS FilterDefinition ,
                                '' AS Fill_Factor ,
                                '' AS DataCompression ,
                                '' AS IndexReads ,
                                '' AS IndexUpdates ,
                                '' AS SizeKB ,
                                StatsRows AS IndexRows ,
                                '' AS IndexLookups ,
                                '' AS MostRecentlyUsed ,
                                SampleRows AS StatsSampleRows ,
                                RowMods AS StatsRowMods ,
                                csi.HistogramSteps ,
                                csi.StatsLastUpdated ,
                                '' AS IndexFragInPercent ,
                                2
                    FROM     cteStatsInfo csi
                    INNER JOIN sys.tables tbl ON csi.object_id = tbl.object_id
                                INNER JOIN sys.schemas sch ON sch.schema_id = tbl.schema_id
                    WHERE    stats_id NOT IN (
                                SELECT  stats_id
                                FROM    cteResults c
                                        INNER JOIN cteStatsInfo si ON si.object_id = c.object_id
                                                                    AND si.stats_id = c.Index_Id )
                    )
            SELECT  FullObjectName ,
                    IndexType ,
                    IndexName ,
                    StatisticsName ,
                    KeyColumns ,
                    ISNULL(IncludeColumns, '') AS IncludeColumns ,
                    FilterDefinition ,
                    [FillFactor] AS [FillFactor] ,
                    DataCompression ,
                    IndexReads ,
                    IndexUpdates ,
                    SizeKB ,
                    IndexRows ,
                    IndexLookups ,
                    MostRecentlyUsed ,
                    StatsSampleRows ,
                    StatsRowMods ,
                    HistogramSteps ,
                    StatsLastUpdated ,
                    IndexFragInPercent
            FROM    AllResults
                    $IncludeStatsPredicate
        OPTION  ( RECOMPILE );
        "
        #endRegion SizesQuery

        Write-Message -Level Debug -Message $SizesQuery -FunctionName Get-DbaHelpIndex -ModuleName "dbatools"

        foreach ($instance in $SqlInstance) {
            try {
                $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential -MinimumVersion 10
            } catch {
                Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaHelpIndex
            }

            $InputObject += Get-DbaDatabase -SqlInstance $server -Database $Database -ExcludeDatabase $ExcludeDatabase
        }

        foreach ($db in $InputObject) {
            $server = $db.Parent

            if (!$db.IsAccessible) {
                Stop-Function -Message "$db is not accessible. Skipping." -Continue -FunctionName Get-DbaHelpIndex
            }

            Write-Message -Level Debug -Message "$SizesQuery" -FunctionName Get-DbaHelpIndex -ModuleName "dbatools"
            try {
                $IndexDetails = $db.Query($SizesQuery)

                foreach ($detail in $IndexDetails) {
                    $recentlyused = [datetime]$detail.MostRecentlyUsed

                    if ($recentlyused.year -eq 1900) {
                        $recentlyused = $null
                    }

                    # Map query column names to output property names
                    $propertyMapping = @{
                        FullObjectName   = "Object"
                        IndexName        = "Index"
                        StatisticsName   = "Statistics"
                        SizeKB           = "Size"
                        MostRecentlyUsed = "MostRecentlyUsed"
                    }

                    # Properties that need numeric formatting when not in Raw mode
                    $numericProperties = @("IndexReads", "IndexUpdates", "SizeKB", "IndexRows", "IndexLookups", "StatsSampleRows", "StatsRowMods")
                    $decimalProperties = @("IndexFragInPercent")

                    # Build hashtable with all properties
                    $properties = @{
                        ComputerName = $server.ComputerName
                        InstanceName = $server.ServiceName
                        SqlInstance  = $server.DomainInstanceName
                        Database     = $db.Name
                    }

                    # Dynamically add all properties from the query result
                    foreach ($property in $detail.PSObject.Properties) {
                        $propertyName = $property.Name
                        $propertyValue = $property.Value

                        # Use mapped name if one exists
                        $outputPropertyName = if ($propertyMapping.ContainsKey($propertyName)) {
                            $propertyMapping[$propertyName]
                        } else {
                            $propertyName
                        }

                        # Apply special handling for specific properties
                        if ($propertyName -eq "MostRecentlyUsed") {
                            $propertyValue = $recentlyused
                        } elseif ($propertyName -eq "SizeKB") {
                            if ($Raw) {
                                $propertyValue = [dbasize]($propertyValue * 1024)
                            } else {
                                $propertyValue = "{0:N0}" -f $propertyValue
                            }
                        } elseif (!$Raw -and $numericProperties -contains $propertyName) {
                            $propertyValue = "{0:N0}" -f $propertyValue
                        } elseif (!$Raw -and $decimalProperties -contains $propertyName) {
                            $propertyValue = "{0:F2}" -f $propertyValue
                        }

                        $properties[$outputPropertyName] = $propertyValue
                    }

                    [pscustomobject]$properties
                }
            } catch {
                Stop-Function -Continue -ErrorRecord $_ -Message "Cannot process $db on $server" -FunctionName Get-DbaHelpIndex
            }
        }
} $SqlInstance $SqlCredential $Database $ExcludeDatabase $InputObject $ObjectName $IncludeStats $IncludeDataTypes $Raw $IncludeFragmentation $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
