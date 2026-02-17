# Migration Tracker: ag

## Dependencies
- Requires: foundation

## Commands

| # | Command | Status | C# File | Build | Parity | Pester | Notes |
|---|---------|--------|---------|-------|--------|--------|-------|
| 1 | Get-DbaAgBackupHistory | DONE | GetDbaAgBackupHistoryCommand.cs | OK | 100% | 1/1 | Read-only, no deps |
| 2 | Get-DbaAgDatabase | DONE | GetDbaAgDatabaseCommand.cs | OK | 100% | 2/2 pass | Read-only, no deps |
| 3 | Get-DbaAgDatabaseReplicaState | DONE | GetDbaAgDatabaseReplicaStateCommand.cs | OK | 100% | Pre-existing infra issue | Read-only, no deps |
| 4 | Get-DbaAgHadr | DONE | GetDbaAgHadrCommand.cs | OK | 100% | 2/2 pass | Read-only, no deps |
| 5 | Get-DbaAgListener | DONE | GetDbaAgListenerCommand.cs | OK | 100% | 1/1 unit pass; integration pre-existing infra issue | Read-only, no deps |
| 6 | Get-DbaAgReplica | DONE | GetDbaAgReplicaCommand.cs | OK | 100% | 1/1 unit pass; integration pre-existing infra issue | Read-only, no deps |
| 7 | Get-DbaAvailabilityGroup | DONE | GetDbaAvailabilityGroupCommand.cs | OK | 100% | 1/1 unit pass; integration pre-existing infra issue | Read-only, no deps |
| 8 | Test-DbaAgSpn | DONE | TestDbaAgSpnCommand.cs | OK | 100% | 1/1 pass | Fixed PS1 $resolved bug, MSA pattern |
| 9 | Test-DbaAvailabilityGroup | PENDING | | | | |  |
| 10 | New-DbaAvailabilityGroup | PENDING | | | | | ShouldProcess required |
| 11 | Set-DbaAgListener | PENDING | | | | | ShouldProcess required, depends on Get-DbaAgListener |
| 12 | Set-DbaAgReplica | PENDING | | | | | ShouldProcess required, depends on Get-DbaAgReplica |
| 13 | Set-DbaAvailabilityGroup | PENDING | | | | | ShouldProcess required, depends on Get-DbaAvailabilityGroup |
| 14 | Add-DbaAgDatabase | PENDING | | | | |  |
| 15 | Add-DbaAgListener | PENDING | | | | |  |
| 16 | Add-DbaAgReplica | PENDING | | | | |  |
| 17 | Disable-DbaAgHadr | PENDING | | | | | ShouldProcess required |
| 18 | Enable-DbaAgHadr | PENDING | | | | | ShouldProcess required |
| 19 | Remove-DbaAgDatabase | PENDING | | | | | ShouldProcess required, depends on Get-DbaAgDatabase |
| 20 | Remove-DbaAgListener | PENDING | | | | | ShouldProcess required, depends on Get-DbaAgListener |
| 21 | Remove-DbaAgReplica | PENDING | | | | | ShouldProcess required, depends on Get-DbaAgReplica |
| 22 | Remove-DbaAvailabilityGroup | PENDING | | | | | ShouldProcess required, depends on Get-DbaAvailabilityGroup |
| 23 | Compare-DbaAgReplicaAgentJob | PENDING | | | | |  |
| 24 | Compare-DbaAgReplicaCredential | PENDING | | | | |  |
| 25 | Compare-DbaAgReplicaLogin | PENDING | | | | |  |
| 26 | Compare-DbaAgReplicaOperator | PENDING | | | | |  |
| 27 | Compare-DbaAgReplicaSync | PENDING | | | | |  |
| 28 | Compare-DbaAvailabilityGroup | PENDING | | | | |  |
| 29 | Grant-DbaAgPermission | PENDING | | | | |  |
| 30 | Invoke-DbaAgFailover | PENDING | | | | |  |
| 31 | Join-DbaAvailabilityGroup | PENDING | | | | |  |
| 32 | Resume-DbaAgDbDataMovement | PENDING | | | | |  |
| 33 | Revoke-DbaAgPermission | PENDING | | | | |  |
| 34 | Suspend-DbaAgDbDataMovement | PENDING | | | | |  |
| 35 | Sync-DbaAvailabilityGroup | PENDING | | | | |  |
