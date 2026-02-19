# Migration Tracker: database

## Dependencies
- Requires: foundation

## Commands

| # | Command | Status | C# File | Build | Parity | Pester | Notes |
|---|---------|--------|---------|-------|--------|--------|-------|
| 1 | Get-DbaAvailableCollation | DONE | GetDbaAvailableCollationCommand.cs | OK | OK | 2/2 pass | Read-only, no deps |
| 2 | Get-DbaBinaryFileTable | PENDING | | | | | Read-only, no deps |
| 3 | Get-DbaDatabase | PENDING | | | | | Read-only, no deps |
| 4 | Get-DbaDbccHelp | PENDING | | | | | Read-only, no deps |
| 5 | Get-DbaDbccMemoryStatus | PENDING | | | | | Read-only, no deps |
| 6 | Get-DbaDbccProcCache | PENDING | | | | | Read-only, no deps |
| 7 | Get-DbaDbccSessionBuffer | PENDING | | | | | Read-only, no deps |
| 8 | Get-DbaDbccStatistic | PENDING | | | | | Read-only, no deps |
| 9 | Get-DbaDbccUserOption | PENDING | | | | | Read-only, no deps |
| 10 | Get-DbaDbCompatibility | PENDING | | | | | Read-only, no deps |
| 11 | Get-DbaDbCompression | PENDING | | | | | Read-only, no deps |
| 12 | Get-DbaDbDbccOpenTran | PENDING | | | | | Read-only, no deps |
| 13 | Get-DbaDbDetachedFileInfo | PENDING | | | | | Read-only, no deps |
| 14 | Get-DbaDbExtentDiff | PENDING | | | | | Read-only, no deps |
| 15 | Get-DbaDbFeatureUsage | PENDING | | | | | Read-only, no deps |
| 16 | Get-DbaDbFile | PENDING | | | | | Read-only, no deps |
| 17 | Get-DbaDbFileGroup | PENDING | | | | | Read-only, no deps |
| 18 | Get-DbaDbFileGrowth | PENDING | | | | | Read-only, no deps |
| 19 | Get-DbaDbFileMapping | PENDING | | | | | Read-only, no deps |
| 20 | Get-DbaDbIdentity | PENDING | | | | | Read-only, no deps |
| 21 | Get-DbaDbLogSpace | PENDING | | | | | Read-only, no deps |
| 22 | Get-DbaDbMemoryUsage | PENDING | | | | | Read-only, no deps |
| 23 | Get-DbaDbPageInfo | PENDING | | | | | Read-only, no deps |
| 24 | Get-DbaDbQueryStoreOption | PENDING | | | | | Read-only, no deps |
| 25 | Get-DbaDbRecoveryModel | PENDING | | | | | Read-only, no deps |
| 26 | Get-DbaDbSharePoint | PENDING | | | | | Read-only, no deps |
| 27 | Get-DbaDbSnapshot | PENDING | | | | | Read-only, no deps |
| 28 | Get-DbaDbSpace | PENDING | | | | | Read-only, no deps |
| 29 | Get-DbaDbState | PENDING | | | | | Read-only, no deps |
| 30 | Get-DbaDbVirtualLogFile | PENDING | | | | | Read-only, no deps |
| 31 | Get-DbaDefaultPath | PENDING | | | | | Read-only, no deps |
| 32 | Get-DbaDependency | PENDING | | | | | Read-only, no deps |
| 33 | Get-DbaHelpIndex | PENDING | | | | | Read-only, no deps |
| 34 | Get-DbaLastGoodCheckDb | PENDING | | | | | Read-only, no deps |
| 35 | Get-DbaRandomizedDataset | PENDING | | | | | Read-only, no deps |
| 36 | Get-DbaRandomizedDatasetTemplate | PENDING | | | | | Read-only, no deps |
| 37 | Get-DbaRandomizedType | PENDING | | | | | Read-only, no deps |
| 38 | Get-DbaRandomizedValue | PENDING | | | | | Read-only, no deps |
| 39 | Get-DbaSchemaChangeHistory | PENDING | | | | | Read-only, no deps |
| 40 | Get-DbaSuspectPage | PENDING | | | | | Read-only, no deps |
| 41 | Test-DbaDbCollation | PENDING | | | | |  |
| 42 | Test-DbaDbCompatibility | PENDING | | | | |  |
| 43 | Test-DbaDbCompression | PENDING | | | | |  |
| 44 | Test-DbaDbDataGeneratorConfig | PENDING | | | | |  |
| 45 | Test-DbaDbDataMaskingConfig | PENDING | | | | |  |
| 46 | Test-DbaDbOwner | PENDING | | | | |  |
| 47 | Test-DbaDbQueryStore | PENDING | | | | |  |
| 48 | Test-DbaDbRecoveryModel | PENDING | | | | |  |
| 49 | Test-DbaIdentityUsage | PENDING | | | | |  |
| 50 | Find-DbaDatabase | PENDING | | | | |  |
| 51 | Find-DbaDbDisabledIndex | PENDING | | | | |  |
| 52 | Find-DbaDbDuplicateIndex | PENDING | | | | |  |
| 53 | Find-DbaDbGrowthEvent | PENDING | | | | |  |
| 54 | Find-DbaDbUnusedIndex | PENDING | | | | |  |
| 55 | Find-DbaOrphanedFile | PENDING | | | | |  |
| 56 | Find-DbaSimilarTable | PENDING | | | | |  |
| 57 | Find-DbaUserObject | PENDING | | | | |  |
| 58 | New-DbaDatabase | PENDING | | | | | ShouldProcess required |
| 59 | New-DbaDbDataGeneratorConfig | PENDING | | | | | ShouldProcess required |
| 60 | New-DbaDbFileGroup | PENDING | | | | | ShouldProcess required |
| 61 | New-DbaDbMaskingConfig | PENDING | | | | | ShouldProcess required |
| 62 | New-DbaDbSnapshot | PENDING | | | | | ShouldProcess required |
| 63 | New-DbaDbTransfer | PENDING | | | | | ShouldProcess required |
| 64 | Set-DbaDbCompatibility | PENDING | | | | | ShouldProcess required, depends on Get-DbaDbCompatibility |
| 65 | Set-DbaDbCompression | PENDING | | | | | ShouldProcess required, depends on Get-DbaDbCompression |
| 66 | Set-DbaDbFileGroup | PENDING | | | | | ShouldProcess required, depends on Get-DbaDbFileGroup |
| 67 | Set-DbaDbFileGrowth | PENDING | | | | | ShouldProcess required, depends on Get-DbaDbFileGrowth |
| 68 | Set-DbaDbIdentity | PENDING | | | | | ShouldProcess required, depends on Get-DbaDbIdentity |
| 69 | Set-DbaDbOwner | PENDING | | | | | ShouldProcess required |
| 70 | Set-DbaDbQueryStoreOption | PENDING | | | | | ShouldProcess required, depends on Get-DbaDbQueryStoreOption |
| 71 | Set-DbaDbRecoveryModel | PENDING | | | | | ShouldProcess required, depends on Get-DbaDbRecoveryModel |
| 72 | Set-DbaDbState | PENDING | | | | | ShouldProcess required, depends on Get-DbaDbState |
| 73 | Set-DbaDefaultPath | PENDING | | | | | ShouldProcess required, depends on Get-DbaDefaultPath |
| 74 | Add-DbaDbFile | PENDING | | | | |  |
| 75 | Remove-DbaDatabase | PENDING | | | | | ShouldProcess required, depends on Get-DbaDatabase |
| 76 | Remove-DbaDatabaseSafely | PENDING | | | | | ShouldProcess required |
| 77 | Remove-DbaDbData | PENDING | | | | | ShouldProcess required |
| 78 | Remove-DbaDbFileGroup | PENDING | | | | | ShouldProcess required, depends on Get-DbaDbFileGroup |
| 79 | Remove-DbaDbSnapshot | PENDING | | | | | ShouldProcess required, depends on Get-DbaDbSnapshot |
| 80 | Remove-DbaDbTableData | PENDING | | | | | ShouldProcess required |
| 81 | Copy-DbaDbQueryStoreOption | PENDING | | | | |  |
| 82 | Copy-DbaDbTableData | PENDING | | | | |  |
| 83 | Copy-DbaDbViewData | PENDING | | | | |  |
| 84 | Copy-DbaSystemDbUserObject | PENDING | | | | |  |
| 85 | ConvertTo-DbaDataTable | PENDING | | | | |  |
| 86 | ConvertTo-DbaTimeline | PENDING | | | | |  |
| 87 | Dismount-DbaDatabase | PENDING | | | | |  |
| 88 | Expand-DbaDbLogFile | PENDING | | | | |  |
| 89 | Export-DbaBinaryFile | PENDING | | | | |  |
| 90 | Export-DbaCsv | PENDING | | | | |  |
| 91 | Export-DbaDbTableData | PENDING | | | | |  |
| 92 | Export-DbaSysDbUserObject | PENDING | | | | |  |
| 93 | Import-DbaBinaryFile | PENDING | | | | |  |
| 94 | Import-DbaCsv | PENDING | | | | |  |
| 95 | Invoke-DbaBalanceDataFiles | PENDING | | | | |  |
| 96 | Invoke-DbaDbAzSqlTip | PENDING | | | | |  |
| 97 | Invoke-DbaDbccDropCleanBuffer | PENDING | | | | |  |
| 98 | Invoke-DbaDbccFreeCache | PENDING | | | | |  |
| 99 | Invoke-DbaDbClone | PENDING | | | | |  |
| 100 | Invoke-DbaDbDataGenerator | PENDING | | | | |  |
| 101 | Invoke-DbaDbDataMasking | PENDING | | | | |  |
| 102 | Invoke-DbaDbDbccCheckConstraint | PENDING | | | | |  |
| 103 | Invoke-DbaDbDbccCleanTable | PENDING | | | | |  |
| 104 | Invoke-DbaDbDbccUpdateUsage | PENDING | | | | |  |
| 105 | Invoke-DbaDbDecryptObject | PENDING | | | | |  |
| 106 | Invoke-DbaDbPiiScan | PENDING | | | | |  |
| 107 | Invoke-DbaDbShrink | PENDING | | | | |  |
| 108 | Invoke-DbaDbTransfer | PENDING | | | | |  |
| 109 | Invoke-DbaDbUpgrade | PENDING | | | | |  |
| 110 | Measure-DbaDbVirtualLogFile | PENDING | | | | |  |
| 111 | Mount-DbaDatabase | PENDING | | | | |  |
| 112 | Move-DbaDbFile | PENDING | | | | |  |
| 113 | Rename-DbaDatabase | PENDING | | | | |  |
| 114 | Show-DbaDbList | PENDING | | | | |  |
| 115 | Watch-DbaDbLogin | PENDING | | | | |  |
| 116 | Write-DbaDbTableData | PENDING | | | | |  |
