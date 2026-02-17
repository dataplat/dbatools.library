# Migration Tracker: config

## Dependencies
- Requires: foundation

## Commands

| # | Command | Status | C# File | Build | Parity | Pester | Notes |
|---|---------|--------|---------|-------|--------|--------|-------|
| 1 | Get-DbatoolsChangeLog | DONE | GetDbatoolsChangeLogCommand.cs | OK | 100% | 1/1 | Read-only, no deps |
| 2 | Get-DbatoolsConfig | DONE | GetDbatoolsConfigCommand.cs | OK | 100% | 7/7 | Read-only, no deps |
| 3 | Get-DbatoolsConfigValue | DONE | GetDbatoolsConfigValueCommand.cs | OK | 100% | 1/1 | Read-only, no deps |
| 4 | Get-DbatoolsError | DONE | GetDbatoolsErrorCommand.cs | OK | 100% | 5/5 | Read-only, no deps |
| 5 | Get-DbatoolsLog | DONE | GetDbatoolsLogCommand.cs | OK | 100% | 16/16 | Read-only, no deps |
| 6 | Get-DbatoolsPath | DONE | GetDbatoolsPathCommand.cs | OK | 100% | 1/1 | Read-only, no deps |
| 7 | New-DbatoolsSupportPackage | PENDING | | | | | ShouldProcess required |
| 8 | Set-DbatoolsInsecureConnection | PENDING | | | | | ShouldProcess required |
| 9 | Set-DbatoolsPath | PENDING | | | | | ShouldProcess required, depends on Get-DbatoolsPath |
| 10 | Export-DbatoolsConfig | PENDING | | | | |  |
| 11 | Import-DbatoolsConfig | PENDING | | | | |  |
| 12 | Invoke-DbatoolsFormatter | PENDING | | | | |  |
| 13 | Invoke-DbatoolsRenameHelper | PENDING | | | | |  |
| 14 | Measure-DbatoolsImport | PENDING | | | | |  |
| 15 | Register-DbatoolsConfig | PENDING | | | | |  |
| 16 | Reset-DbatoolsConfig | PENDING | | | | |  |
| 17 | Unregister-DbatoolsConfig | PENDING | | | | |  |
| 18 | Update-Dbatools | PENDING | | | | |  |
