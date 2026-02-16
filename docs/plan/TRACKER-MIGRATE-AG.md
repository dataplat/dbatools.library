# Migration Tracker: ag

## Dependencies
- Requires: foundation

## Commands

| # | Command | Status | C# File | Build | Parity | Notes |
|---|---------|--------|---------|-------|--------|-------|
| 1 | Get-DbaAgBackupHistory | PENDING | | | | Read-only, no deps |
| 2 | Get-DbaAgDatabase | PENDING | | | | Read-only, no deps |
| 3 | Get-DbaAgDatabaseReplicaState | PENDING | | | | Read-only, no deps |
| 4 | Get-DbaAgHadr | PENDING | | | | Read-only, no deps |
| 5 | Get-DbaAgListener | PENDING | | | | Read-only, no deps |
| 6 | Get-DbaAgReplica | PENDING | | | | Read-only, no deps |
| 7 | Get-DbaAvailabilityGroup | PENDING | | | | Read-only, no deps |
| 8 | Test-DbaAgSpn | PENDING | | | |  |
| 9 | Test-DbaAvailabilityGroup | PENDING | | | |  |
| 10 | New-DbaAvailabilityGroup | PENDING | | | | ShouldProcess required |
| 11 | Set-DbaAgListener | PENDING | | | | ShouldProcess required, depends on Get-DbaAgListener |
| 12 | Set-DbaAgReplica | PENDING | | | | ShouldProcess required, depends on Get-DbaAgReplica |
| 13 | Set-DbaAvailabilityGroup | PENDING | | | | ShouldProcess required, depends on Get-DbaAvailabilityGroup |
| 14 | Add-DbaAgDatabase | PENDING | | | |  |
| 15 | Add-DbaAgListener | PENDING | | | |  |
| 16 | Add-DbaAgReplica | PENDING | | | |  |
| 17 | Disable-DbaAgHadr | PENDING | | | | ShouldProcess required |
| 18 | Enable-DbaAgHadr | PENDING | | | | ShouldProcess required |
| 19 | Remove-DbaAgDatabase | PENDING | | | | ShouldProcess required, depends on Get-DbaAgDatabase |
| 20 | Remove-DbaAgListener | PENDING | | | | ShouldProcess required, depends on Get-DbaAgListener |
| 21 | Remove-DbaAgReplica | PENDING | | | | ShouldProcess required, depends on Get-DbaAgReplica |
| 22 | Remove-DbaAvailabilityGroup | PENDING | | | | ShouldProcess required, depends on Get-DbaAvailabilityGroup |
| 23 | Compare-DbaAgReplicaAgentJob | PENDING | | | |  |
| 24 | Compare-DbaAgReplicaCredential | PENDING | | | |  |
| 25 | Compare-DbaAgReplicaLogin | PENDING | | | |  |
| 26 | Compare-DbaAgReplicaOperator | PENDING | | | |  |
| 27 | Compare-DbaAgReplicaSync | PENDING | | | |  |
| 28 | Compare-DbaAvailabilityGroup | PENDING | | | |  |
| 29 | Grant-DbaAgPermission | PENDING | | | |  |
| 30 | Invoke-DbaAgFailover | PENDING | | | |  |
| 31 | Join-DbaAvailabilityGroup | PENDING | | | |  |
| 32 | Resume-DbaAgDbDataMovement | PENDING | | | |  |
| 33 | Revoke-DbaAgPermission | PENDING | | | |  |
| 34 | Suspend-DbaAgDbDataMovement | PENDING | | | |  |
| 35 | Sync-DbaAvailabilityGroup | PENDING | | | |  |
