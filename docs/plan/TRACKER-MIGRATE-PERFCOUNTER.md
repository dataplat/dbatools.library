# Migration Tracker: perfcounter

## Dependencies
- Requires: foundation

## Commands

| # | Command | Status | C# File | Build | Parity | Pester | Notes |
|---|---------|--------|---------|-------|--------|--------|-------|
| 1 | Get-DbaPfAvailableCounter | PENDING | | | | | Read-only, no deps |
| 2 | Get-DbaPfDataCollector | PENDING | | | | | Read-only, no deps |
| 3 | Get-DbaPfDataCollectorCounter | PENDING | | | | | Read-only, no deps |
| 4 | Get-DbaPfDataCollectorCounterSample | PENDING | | | | | Read-only, no deps |
| 5 | Get-DbaPfDataCollectorSet | PENDING | | | | | Read-only, no deps |
| 6 | Get-DbaPfDataCollectorSetTemplate | PENDING | | | | | Read-only, no deps |
| 7 | Add-DbaPfDataCollectorCounter | PENDING | | | | |  |
| 8 | Remove-DbaPfDataCollectorCounter | PENDING | | | | | ShouldProcess required, depends on Get-DbaPfDataCollectorCounter |
| 9 | Remove-DbaPfDataCollectorSet | PENDING | | | | | ShouldProcess required, depends on Get-DbaPfDataCollectorSet |
| 10 | Copy-DbaDataCollector | PENDING | | | | |  |
| 11 | Export-DbaPfDataCollectorSetTemplate | PENDING | | | | |  |
| 12 | Import-DbaPfDataCollectorSetTemplate | PENDING | | | | |  |
| 13 | Invoke-DbaPfRelog | PENDING | | | | |  |
| 14 | Start-DbaPfDataCollectorSet | PENDING | | | | |  |
| 15 | Stop-DbaPfDataCollectorSet | PENDING | | | | |  |
