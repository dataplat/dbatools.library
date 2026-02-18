# Migration Tracker: computer

## Dependencies
- Requires: foundation

## Commands

| # | Command | Status | C# File | Build | Parity | Pester | Notes |
|---|---------|--------|---------|-------|--------|--------|-------|
| 1 | Get-DbaClientAlias | DONE | GetDbaClientAliasCommand.cs | OK | 100% | 7/7 | Read-only, no deps |
| 2 | Get-DbaClientProtocol | DONE | GetDbaClientProtocolCommand.cs | OK | 100% | 1/1 (1 skipped) | Read-only, no deps |
| 3 | Get-DbaCmConnection | DONE | GetDbaCmConnectionCommand.cs | OK | 100% | 3/3 | Read-only, no deps |
| 4 | Get-DbaCmObject | DONE | GetDbaCmObjectCommand.cs | OK | 100% | 2/2 (2 test-adapt) | Read-only, no deps |
| 5 | Get-DbaComputerCertificate | PENDING | | | | | Read-only, no deps |
| 6 | Get-DbaComputerSystem | PENDING | | | | | Read-only, no deps |
| 7 | Get-DbaDiskSpace | PENDING | | | | | Read-only, no deps |
| 8 | Get-DbaLocaleSetting | PENDING | | | | | Read-only, no deps |
| 9 | Get-DbaMemoryCondition | PENDING | | | | | Read-only, no deps |
| 10 | Get-DbaMemoryUsage | PENDING | | | | | Read-only, no deps |
| 11 | Get-DbaMsdtc | PENDING | | | | | Read-only, no deps |
| 12 | Get-DbaOperatingSystem | PENDING | | | | | Read-only, no deps |
| 13 | Get-DbaPageFileSetting | PENDING | | | | | Read-only, no deps |
| 14 | Get-DbaPowerPlan | PENDING | | | | | Read-only, no deps |
| 15 | Get-DbaPrivilege | PENDING | | | | | Read-only, no deps |
| 16 | Get-DbaService | PENDING | | | | | Read-only, no deps |
| 17 | Get-DbaWindowsLog | PENDING | | | | | Read-only, no deps |
| 18 | Get-DbaWsfcAvailableDisk | PENDING | | | | | Read-only, no deps |
| 19 | Get-DbaWsfcCluster | PENDING | | | | | Read-only, no deps |
| 20 | Get-DbaWsfcDisk | PENDING | | | | | Read-only, no deps |
| 21 | Get-DbaWsfcNetwork | PENDING | | | | | Read-only, no deps |
| 22 | Get-DbaWsfcNetworkInterface | PENDING | | | | | Read-only, no deps |
| 23 | Get-DbaWsfcNode | PENDING | | | | | Read-only, no deps |
| 24 | Get-DbaWsfcResource | PENDING | | | | | Read-only, no deps |
| 25 | Get-DbaWsfcResourceGroup | PENDING | | | | | Read-only, no deps |
| 26 | Get-DbaWsfcResourceType | PENDING | | | | | Read-only, no deps |
| 27 | Get-DbaWsfcRole | PENDING | | | | | Read-only, no deps |
| 28 | Get-DbaWsfcSharedVolume | PENDING | | | | | Read-only, no deps |
| 29 | Test-DbaCmConnection | PENDING | | | | |  |
| 30 | Test-DbaComputerCertificateExpiration | PENDING | | | | |  |
| 31 | Test-DbaDiskAlignment | PENDING | | | | |  |
| 32 | Test-DbaDiskAllocation | PENDING | | | | |  |
| 33 | Test-DbaDiskSpeed | PENDING | | | | |  |
| 34 | Test-DbaPowerPlan | PENDING | | | | |  |
| 35 | New-DbaClientAlias | PENDING | | | | | ShouldProcess required |
| 36 | New-DbaCmConnection | PENDING | | | | | ShouldProcess required |
| 37 | New-DbaComputerCertificate | PENDING | | | | | ShouldProcess required |
| 38 | New-DbaComputerCertificateSigningRequest | PENDING | | | | | ShouldProcess required |
| 39 | Set-DbaCmConnection | PENDING | | | | | ShouldProcess required, depends on Get-DbaCmConnection |
| 40 | Set-DbaPowerPlan | PENDING | | | | | ShouldProcess required, depends on Get-DbaPowerPlan |
| 41 | Set-DbaPrivilege | PENDING | | | | | ShouldProcess required, depends on Get-DbaPrivilege |
| 42 | Add-DbaComputerCertificate | PENDING | | | | |  |
| 43 | Remove-DbaClientAlias | PENDING | | | | | ShouldProcess required, depends on Get-DbaClientAlias |
| 44 | Remove-DbaCmConnection | PENDING | | | | | ShouldProcess required, depends on Get-DbaCmConnection |
| 45 | Remove-DbaComputerCertificate | PENDING | | | | | ShouldProcess required, depends on Get-DbaComputerCertificate |
| 46 | Backup-DbaComputerCertificate | PENDING | | | | |  |
| 47 | Measure-DbaDiskSpaceRequirement | PENDING | | | | |  |
| 48 | Restart-DbaService | PENDING | | | | |  |
| 49 | Start-DbaService | PENDING | | | | |  |
| 50 | Stop-DbaService | PENDING | | | | |  |
