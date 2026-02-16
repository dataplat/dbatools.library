# Migration Tracker: logshipping

## Dependencies
- Requires: foundation, database

## Commands

| # | Command | Status | C# File | Build | Parity | Notes |
|---|---------|--------|---------|-------|--------|-------|
| 1 | Get-DbaDbLogShipError | PENDING | | | | Read-only, no deps |
| 2 | Get-DbaDbMirror | PENDING | | | | Read-only, no deps |
| 3 | Get-DbaDbMirrorMonitor | PENDING | | | | Read-only, no deps |
| 4 | Test-DbaDbLogShipStatus | PENDING | | | |  |
| 5 | Set-DbaDbMirror | PENDING | | | | ShouldProcess required, depends on Get-DbaDbMirror |
| 6 | Add-DbaDbMirrorMonitor | PENDING | | | |  |
| 7 | Remove-DbaDbLogShipping | PENDING | | | | ShouldProcess required |
| 8 | Remove-DbaDbMirror | PENDING | | | | ShouldProcess required, depends on Get-DbaDbMirror |
| 9 | Remove-DbaDbMirrorMonitor | PENDING | | | | ShouldProcess required, depends on Get-DbaDbMirrorMonitor |
| 10 | Invoke-DbaDbLogShipping | PENDING | | | |  |
| 11 | Invoke-DbaDbLogShipRecovery | PENDING | | | |  |
| 12 | Invoke-DbaDbMirrorFailover | PENDING | | | |  |
| 13 | Invoke-DbaDbMirroring | PENDING | | | |  |
| 14 | Repair-DbaDbMirror | PENDING | | | |  |
