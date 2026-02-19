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
| 7 | New-DbatoolsSupportPackage | DONE | NewDbatoolsSupportPackageCommand.cs | OK | 100% | 1/1 | ShouldProcess, uses InvokeScript for PS data collection |
| 8 | Set-DbatoolsInsecureConnection | DONE | SetDbatoolsInsecureConnectionCommand.cs | OK | 100% | 4/4 pass | No ShouldProcess (matches PS1) |
| 9 | Set-DbatoolsPath | DONE | SetDbatoolsPathCommand.cs | OK | 100% | 1/1 | No ShouldProcess (matches PS1), depends on Get-DbatoolsPath |
| 10 | Export-DbatoolsConfig | DONE | ExportDbatoolsConfigCommand.cs | OK | 100% | 1/1 | Config file export, 4 param sets, scope path computation |
| 11 | Import-DbatoolsConfig | DONE | ImportDbatoolsConfigCommand.cs | OK | 100% | 1/1 | Config file import, 2 param sets, Peek mode, include/exclude filters, scope file reading |
| 12 | Invoke-DbatoolsFormatter | DONE | InvokeDbatoolsFormatterCommand.cs | OK | 100% | 3/3 | File formatter, uses InvokeScript for PSScriptAnalyzer, per-file EOL fix |
| 13 | Invoke-DbatoolsRenameHelper | DONE | InvokeDbatoolsRenameHelperCommand.cs | OK | 100% | 5/5 | File rename helper, ShouldProcess, no SQL |
| 14 | Measure-DbatoolsImport | PENDING | | | | |  |
| 15 | Register-DbatoolsConfig | PENDING | | | | |  |
| 16 | Reset-DbatoolsConfig | PENDING | | | | |  |
| 17 | Unregister-DbatoolsConfig | PENDING | | | | |  |
| 18 | Update-Dbatools | PENDING | | | | |  |
