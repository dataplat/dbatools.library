#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Applies row/page compression to tables, indexes and indexed views, either from Test-DbaDbCompression
/// recommendations or from an explicit -CompressionType. Port of public/Set-DbaDbCompression.ps1; the
/// workflow remains a module-scoped PowerShell compatibility hop.
///
/// A process-only port, and the largest body in this descent (~260 lines of process). It carries a
/// single Test-Bound - Test-Bound "InputObject" -> $__boundInputObject - which gates whether
/// compression suggestions come from the caller or from a live Test-DbaDbCompression call. That
/// distinction cannot be rewritten as "if ($InputObject)": the source deliberately asks whether the
/// parameter was SUPPLIED, so an explicitly-passed empty/null InputObject must still take the
/// passed-in branch rather than silently re-testing the database.
///
/// ShouldProcess is real (baseline: supportsShouldProcess true, confirmImpact Medium), so all THREE
/// $Pscmdlet.ShouldProcess(...) gates become $__realCmdlet.ShouldProcess(...) with target and action
/// strings byte-for-byte.
///
/// NO continue-guard wrapper is needed. The two bare `continue` statements (status-not-Normal and
/// compatibility-level-too-low) both sit inside the genuine `foreach ($db in $dbs)` loop, and every
/// Stop-Function -Continue site sits inside either that loop or the enclosing
/// `foreach ($instance in $SqlInstance)`. There are no `return` statements at all.
///
/// The body calls the module-private helper Get-ObjectNameParts. That resolves only because the hop
/// executes inside the dbatools module scope via `& $__dbatoolsModule` - it would be CommandNotFound
/// from a plain scriptblock, which is precisely what the module-scoped hop exists to provide.
///
/// PRESERVED SOURCE BUG - do not "fix" this in the port. At source line 195 the online-rebuild
/// capability scan iterates `$tables`, but `$tables` is not assigned until line 265, inside the
/// OTHER (explicit -CompressionType) branch. So on the "Recommended" path `$tables` is $null on the
/// first database and the scan body never executes, leaving $isOnlineRebuildSupported $false and
/// forcing offline rebuilds; across a multi-database run it is worse than $null, because it can hold
/// the PREVIOUS database's table collection and decide this database's online-rebuild capability
/// from another database's indexes. Reproduced verbatim per the verbatim-source-bugs law and logged
/// upstream; the hop reproduces it for free precisely because the body is unmodified.
///
/// -InputObject is deliberately `object` and NOT an array: the source declares it untyped
/// (`$InputObject`, no type constraint) and the baseline records System.Object at position 8.
/// Narrowing it to a typed array would change binding for the Test-DbaDbCompression pipeline.
///
/// The two switches ForceOfflineRebuilds and SortInTempDB are passed as carried BOOL VALUES, not as
/// bound flags, because the body only ever reads their VALUE (`!$ForceOfflineRebuilds`,
/// `if ($SortInTempDB)`, and the SMO assignment `$underlyingObj.SortInTempdb = $SortInTempDB`) and
/// never asks whether they were bound.
///
/// Other body edits are -FunctionName Set-DbaDbCompression attribution stamping on the direct
/// Stop-Function and Write-Message sites.
///
/// Surface pinned by migration/baselines/Set-DbaDbCompression.json
/// (sourceSha256 ba6601c5bcf9fde9cce8d5cc4e876c8af0985ac60aee79d6490ab7ebdedc5a2c):
/// DefaultParameterSetName "Default"; SqlInstance 0 MANDATORY + ValueFromPipeline, SqlCredential 1,
/// Database 2, ExcludeDatabase 3, Table 4, CompressionType 5, MaxRunTime 6, PercentCompression 7,
/// InputObject 8; ForceOfflineRebuilds / SortInTempDB non-positional switches; outputType empty.
/// Positions are declared EXPLICITLY per the positional-binding-loss class ruling: an advanced
/// function gets implicit positional binding, a compiled cmdlet does not.
/// </summary>
[Cmdlet(VerbsCommon.Set, "DbaDbCompression", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Medium, DefaultParameterSetName = "Default")]
public sealed class SetDbaDbCompressionCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The database(s) to process.</summary>
    [Parameter(Position = 2)]
    public string[]? Database { get; set; }

    /// <summary>The database(s) to exclude.</summary>
    [Parameter(Position = 3)]
    public string[]? ExcludeDatabase { get; set; }

    /// <summary>The table(s) to process.</summary>
    [Parameter(Position = 4)]
    public string[]? Table { get; set; }

    /// <summary>Compression type to apply, or Recommended to use Test-DbaDbCompression results.</summary>
    [Parameter(Position = 5)]
    [ValidateSet("Recommended", "Page", "Row", "None")]
    public string CompressionType { get; set; } = "Recommended";

    /// <summary>Maximum run time in minutes; 0 means no limit.</summary>
    [Parameter(Position = 6)]
    public int MaxRunTime { get; set; } = 0;

    /// <summary>Minimum percent compression required before an object is processed.</summary>
    [Parameter(Position = 7)]
    public int PercentCompression { get; set; } = 0;

    /// <summary>Compression suggestions piped in from Test-DbaDbCompression. Untyped, per the source.</summary>
    [Parameter(Position = 8)]
    public object? InputObject { get; set; }

    /// <summary>Force offline rebuilds even where online rebuilds are supported.</summary>
    [Parameter]
    public SwitchParameter ForceOfflineRebuilds { get; set; }

    /// <summary>Sort in tempdb during index rebuilds.</summary>
    [Parameter]
    public SwitchParameter SortInTempDB { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, SqlCredential, Database, ExcludeDatabase, Table,
            CompressionType, MaxRunTime, PercentCompression,
            ForceOfflineRebuilds.ToBool(), SortInTempDB.ToBool(),
            InputObject, EnableException.ToBool(),
            TestBound(nameof(InputObject)),
            this, BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
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

    // PS: the process block verbatim. Edits: Test-Bound "InputObject" -> $__boundInputObject,
    // the three $Pscmdlet -> $__realCmdlet, and -FunctionName Set-DbaDbCompression on the direct
    // Stop-Function and Write-Message sites. The $tables-before-assignment bug at the online-rebuild
    // scan is PRESERVED deliberately (see the class remarks).
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $ExcludeDatabase, $Table, $CompressionType, $MaxRunTime, $PercentCompression, $ForceOfflineRebuilds, $SortInTempDB, $InputObject, $EnableException, $__boundInputObject, $__realCmdlet, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [string[]]$Database, [string[]]$ExcludeDatabase, [string[]]$Table, [string]$CompressionType, [int]$MaxRunTime, [int]$PercentCompression, $ForceOfflineRebuilds, $SortInTempDB, $InputObject, $EnableException, $__boundInputObject, $__realCmdlet, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    $starttime = Get-Date
    foreach ($instance in $SqlInstance) {
        try {
            $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential -MinimumVersion 10
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Set-DbaDbCompression
        }

        #The reason why we do this is because of SQL 2016 and they now allow for compression on standard edition.
        if ($server.EngineEdition -notmatch 'Enterprise' -and $server.VersionMajor -lt '13') {
            Stop-Function -Message "Only SQL Server Enterprise Edition supports compression on $server" -Target $server -Continue -FunctionName Set-DbaDbCompression
        }
        $dbs = $server.Databases | Where-Object { $_.IsAccessible -and $_.IsSystemObject -eq 0 }
        if ($Database) {
            $dbs = $dbs | Where-Object { $_.Name -in $Database }
        }
        if ($ExcludeDatabase) {
            $dbs = $dbs | Where-Object { $_.Name -NotIn $ExcludeDatabase }
        }
        foreach ($db in $dbs) {
            Write-Message -Level Verbose -Message "Querying $instance - $db" -FunctionName Set-DbaDbCompression
            if ($db.Status -ne 'Normal') {
                Write-Message -Level Warning -Message "$db has status $($db.Status) and will be skipped." -Target $db -FunctionName Set-DbaDbCompression
                continue
            }
            if ($db.CompatibilityLevel -lt 'Version100') {
                Write-Message -Level Warning -Message "$db has a compatibility level lower than Version100 and will be skipped." -FunctionName Set-DbaDbCompression
                continue
            }
            $isOnlineRebuildSupported = $false
            # Loop on indexes to see if Rebuild Online is supported
            $onlineRebuildScanCompleted = $false
            foreach ($obj in $tables | Where-Object { !$_.IsMemoryOptimized -and !$_.HasSparseColumn }) {
                if ($onlineRebuildScanCompleted) {
                    break
                }
                foreach ($index in $($obj.Indexes | Where-Object { !$_.IsMemoryOptimized -and $_.IndexType -notmatch 'Columnstore' })) {
                    if ($index.IsOnlineRebuildSupported -or $server.isAzure) {
                        $isOnlineRebuildSupported = $true
                        $onlineRebuildScanCompleted = $true
                        break
                    }
                }
            }
            Write-Message -Level Verbose -Message "Are Online Rebuilds supported ? $isOnlineRebuildSupported" -FunctionName Set-DbaDbCompression
            $CanDoOnlineOperation = $false
            if ($IsOnlineRebuildSupported -and !$ForceOfflineRebuilds) {
                $CanDoOnlineOperation = $true
                Write-Message -Level Verbose -Message "Using Online Rebuilds where possible" -FunctionName Set-DbaDbCompression
            }

            if ($CompressionType -eq "Recommended") {
                if ($__boundInputObject) {
                    Write-Message -Level Verbose -Message "Using passed in compression suggestions" -FunctionName Set-DbaDbCompression
                    $compressionSuggestion = $InputObject | Where-Object { $_.Database -eq $db.Name }
                } else {
                    if ($__realCmdlet.ShouldProcess($db, "Testing database for compression suggestions on $instance")) {
                        try {
                            $compressionSuggestion = Test-DbaDbCompression -SqlInstance $server -Database $db.Name -Table $Table -EnableException
                        } catch {
                            Stop-Function -Message "Unable to test database compression suggestions for $instance - $db" -Target $db -ErrorRecord $_ -Continue -FunctionName Set-DbaDbCompression
                        }
                    }
                }

                if ($__realCmdlet.ShouldProcess($db, "Applying suggested compression using results from Test-DbaDbCompression")) {
                    $objects = $compressionSuggestion | Select-Object *, @{l = 'AlreadyProcessed'; e = { "False" } }
                    foreach ($obj in ($objects | Where-Object { $_.CompressionTypeRecommendation -notin @('NO_GAIN', '?') -and $_.PercentCompression -ge $PercentCompression } | Sort-Object PercentCompression -Descending)) {
                        if ($MaxRunTime -ne 0 -and ($(Get-Date) - $starttime).TotalMinutes -ge $MaxRunTime) {
                            Write-Message -Level Warning -Message "Reached max run time of $MaxRunTime" -FunctionName Set-DbaDbCompression
                            break
                        }
                        if ($obj.indexId -le 1) {
                            ##heaps and clustered indexes
                            Write-Message -Level Verbose -Message "Applying $($obj.CompressionTypeRecommendation) compression to $($obj.Database).$($obj.Schema).$($obj.TableName)" -FunctionName Set-DbaDbCompression
                            try {
                                ($server.Databases[$obj.Database].Tables[$obj.TableName, $obj.Schema].PhysicalPartitions | Where-Object { $_.PartitionNumber -eq $obj.Partition }).DataCompression = $obj.CompressionTypeRecommendation
                                $server.Databases[$obj.Database].Tables[$obj.TableName, $obj.Schema].OnlineHeapOperation = $CanDoOnlineOperation
                                $server.Databases[$obj.Database].Tables[$obj.TableName, $obj.Schema].Rebuild()
                            } catch {
                                Stop-Function -Message "Compression failed for $instance - $db - table $($obj.Schema).$($obj.TableName) - partition $($obj.Partition)" -Target $db -ErrorRecord $_ -Continue -FunctionName Set-DbaDbCompression
                            }
                        } else {
                            ##nonclustered indexes
                            Write-Message -Level Verbose -Message "Applying $($obj.CompressionTypeRecommendation) compression to $($obj.Database).$($obj.Schema).$($obj.TableName).$($obj.IndexName)" -FunctionName Set-DbaDbCompression
                            try {
                                $underlyingObj = $server.Databases[$obj.Database].Tables[$obj.TableName, $obj.Schema].Indexes[$obj.IndexName]
                                ($underlyingObj.PhysicalPartitions | Where-Object { $_.PartitionNumber -eq $obj.Partition }).DataCompression = $obj.CompressionTypeRecommendation
                                $underlyingObj.OnlineIndexOperation = $CanDoOnlineOperation
                                $underlyingObj.SortInTempdb = $SortInTempDB
                                $underlyingObj.Rebuild()
                            } catch {
                                Stop-Function -Message "Compression failed for $instance - $db - table $($obj.Schema).$($obj.TableName) - index $($obj.IndexName) - partition $($obj.Partition)" -Target $db -ErrorRecord $_ -Continue -FunctionName Set-DbaDbCompression
                            }
                        }
                        $obj.AlreadyProcessed = "True"
                        $obj
                    }
                }
            } else {
                if ($__realCmdlet.ShouldProcess($db, "Applying $CompressionType compression")) {
                    $tables = $server.Databases[$($db.name)].Tables
                    if ($Table) {
                        $tableParts = $Table | ForEach-Object { Get-ObjectNameParts -ObjectName $_ }
                        $tables = foreach ($tablePart in $tableParts) {
                            $server.Databases[$($db.name)].Tables | Where-Object {
                                $_.Name -eq $tablePart.Name -and
                                $tablePart.Schema -in ($_.Schema, $null) -and
                                $tablePart.Database -in ($db.Name, $null)
                            }
                        }
                    }

                    foreach ($obj in $tables | Where-Object { !$_.IsMemoryOptimized -and !$_.HasSparseColumn }) {
                        if ($MaxRunTime -ne 0 -and ($(Get-Date) - $starttime).TotalMinutes -ge $MaxRunTime) {
                            Write-Message -Level Warning -Message "Reached max run time of $MaxRunTime" -FunctionName Set-DbaDbCompression
                            break
                        }
                        foreach ($p in $($obj.PhysicalPartitions | Where-Object { $_.DataCompression -notin ($CompressionType, 'ColumnStore', 'ColumnStoreArchive') })) {
                            Write-Message -Level Verbose -Message "Compressing table $($obj.Schema).$($obj.Name)" -FunctionName Set-DbaDbCompression
                            try {
                                $($obj.PhysicalPartitions | Where-Object { $_.PartitionNumber -eq $p.PartitionNumber }).DataCompression = $CompressionType
                                $obj.OnlineHeapOperation = $CanDoOnlineOperation
                                $obj.Rebuild()
                            } catch {
                                Stop-Function -Message "Compression failed for $instance - $db - table $($obj.Schema).$($obj.Name) - partition $($p.PartitionNumber)" -Target $db -ErrorRecord $_ -Continue -FunctionName Set-DbaDbCompression
                            }
                            [PSCustomObject]@{
                                ComputerName                  = $server.ComputerName
                                InstanceName                  = $server.ServiceName
                                SqlInstance                   = $server.DomainInstanceName
                                Database                      = $db.Name
                                Schema                        = $obj.Schema
                                TableName                     = $obj.Name
                                IndexName                     = $null
                                Partition                     = $p.PartitionNumber
                                IndexID                       = 0
                                IndexType                     = Switch ($obj.HasHeapIndex) { $false { "ClusteredIndex" } $true { "Heap" } }
                                PercentScan                   = $null
                                PercentUpdate                 = $null
                                RowEstimatePercentOriginal    = $null
                                PageEstimatePercentOriginal   = $null
                                CompressionTypeRecommendation = $CompressionType.ToUpper()
                                SizeCurrent                   = $null
                                SizeRequested                 = $null
                                PercentCompression            = $null
                                AlreadyProcessed              = "True"
                            }
                        }

                        foreach ($index in $($obj.Indexes | Where-Object { !$_.IsMemoryOptimized -and $_.IndexType -notmatch 'Columnstore' })) {
                            if ($MaxRunTime -ne 0 -and ($(Get-Date) - $starttime).TotalMinutes -ge $MaxRunTime) {
                                Write-Message -Level Warning -Message "Reached max run time of $MaxRunTime" -FunctionName Set-DbaDbCompression
                                break
                            }
                            foreach ($p in $($index.PhysicalPartitions | Where-Object { $_.DataCompression -ne $CompressionType })) {
                                Write-Message -Level Verbose -Message "Compressing $($Index.IndexType) $($Index.Name) Partition $($p.PartitionNumber)" -FunctionName Set-DbaDbCompression
                                try {
                                    ## There is a bug in SMO where setting compression to None at the index level doesn't work
                                    ## Once this UserVoice item is fixed the workaround can be removed
                                    ## https://feedback.azure.com/forums/908035-sql-server/suggestions/34080112-data-compression-smo-bug
                                    if ($CompressionType -eq "None") {
                                        $withOptions = @("DATA_COMPRESSION = $CompressionType")
                                        if ($CanDoOnlineOperation) {
                                            $withOptions += "ONLINE = ON"
                                        }
                                        if ($SortInTempDB) {
                                            $withOptions += "SORT_IN_TEMPDB = ON"
                                        }
                                        $query = "ALTER INDEX [$($index.Name)] ON $($index.Parent) REBUILD PARTITION = ALL WITH ($($withOptions -join ", "))"
                                        $Server.Query($query, $db.Name)
                                    } else {
                                        $($index.PhysicalPartitions | Where-Object { $_.PartitionNumber -eq $P.PartitionNumber }).DataCompression = $CompressionType
                                        $index.OnlineIndexOperation = $CanDoOnlineOperation
                                        $index.SortInTempdb = $SortInTempDB
                                        $index.Rebuild()
                                    }
                                } catch {
                                    Stop-Function -Message "Compression failed for $instance - $db - table $($obj.Schema).$($obj.Name) - index $($index.Name) - partition $($p.PartitionNumber)" -Target $db -ErrorRecord $_ -Continue -FunctionName Set-DbaDbCompression
                                }
                                [PSCustomObject]@{
                                    ComputerName                  = $server.ComputerName
                                    InstanceName                  = $server.ServiceName
                                    SqlInstance                   = $server.DomainInstanceName
                                    Database                      = $db.Name
                                    Schema                        = $obj.Schema
                                    TableName                     = $obj.Name
                                    IndexName                     = $index.Name
                                    Partition                     = $p.PartitionNumber
                                    IndexID                       = $index.Id
                                    IndexType                     = $index.IndexType
                                    PercentScan                   = $null
                                    PercentUpdate                 = $null
                                    RowEstimatePercentOriginal    = $null
                                    PageEstimatePercentOriginal   = $null
                                    CompressionTypeRecommendation = $CompressionType.ToUpper()
                                    SizeCurrent                   = $null
                                    SizeRequested                 = $null
                                    PercentCompression            = $null
                                    AlreadyProcessed              = "True"
                                }
                            }
                        }
                    }
                    foreach ($index in $($server.Databases[$($db.name)].Views | Where-Object { $_.Indexes }).Indexes) {
                        $parentView = $index.Parent
                        foreach ($p in $($index.PhysicalPartitions | Where-Object { $_.DataCompression -ne $CompressionType })) {
                            Write-Message -Level Verbose -Message "Compressing $($index.IndexType) $($index.Name) Partition $($p.PartitionNumber)" -FunctionName Set-DbaDbCompression
                            try {
                                ## There is a bug in SMO where setting compression to None at the index level doesn't work
                                ## Once this UserVoice item is fixed the workaround can be removed
                                ## https://feedback.azure.com/forums/908035-sql-server/suggestions/34080112-data-compression-smo-bug
                                if ($CompressionType -eq "None") {
                                    $withOptions = @("DATA_COMPRESSION = $CompressionType")
                                    if ($CanDoOnlineOperation) {
                                        $withOptions += "ONLINE = ON"
                                    }
                                    if ($SortInTempDB) {
                                        $withOptions += "SORT_IN_TEMPDB = ON"
                                    }
                                    $query = "ALTER INDEX [$($index.Name)] ON $($index.Parent) REBUILD PARTITION = ALL WITH ($($withOptions -join ", "))"
                                    $Server.Query($query, $db.Name)
                                } else {
                                    $($index.PhysicalPartitions | Where-Object { $_.PartitionNumber -eq $P.PartitionNumber }).DataCompression = $CompressionType
                                    $index.OnlineIndexOperation = $CanDoOnlineOperation
                                    $index.SortInTempdb = $SortInTempDB
                                    $index.Rebuild()
                                }
                            } catch {
                                Stop-Function -Message "Compression failed for $instance - $db - view $($parentView.Schema).$($parentView.Name) - index $($index.Name) - partition $($p.PartitionNumber)" -Target $db -ErrorRecord $_ -Continue -FunctionName Set-DbaDbCompression
                            }
                            [PSCustomObject]@{
                                ComputerName                  = $server.ComputerName
                                InstanceName                  = $server.ServiceName
                                SqlInstance                   = $server.DomainInstanceName
                                Database                      = $db.Name
                                Schema                        = $parentView.Schema
                                TableName                     = $parentView.Name
                                IndexName                     = $index.Name
                                Partition                     = $p.PartitionNumber
                                IndexID                       = $index.Id
                                IndexType                     = $index.IndexType
                                PercentScan                   = $null
                                PercentUpdate                 = $null
                                RowEstimatePercentOriginal    = $null
                                PageEstimatePercentOriginal   = $null
                                CompressionTypeRecommendation = $CompressionType.ToUpper()
                                SizeCurrent                   = $null
                                SizeRequested                 = $null
                                PercentCompression            = $null
                                AlreadyProcessed              = "True"
                            }
                        }
                    }
                }
            }
        }
    }
} $SqlInstance $SqlCredential $Database $ExcludeDatabase $Table $CompressionType $MaxRunTime $PercentCompression $ForceOfflineRebuilds $SortInTempDB $InputObject $EnableException $__boundInputObject $__realCmdlet $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
