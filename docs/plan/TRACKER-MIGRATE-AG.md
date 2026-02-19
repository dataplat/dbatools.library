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
| 9 | Test-DbaAvailabilityGroup | DONE | TestDbaAvailabilityGroupCommand.cs | OK | 100% | 1/1 pass | Dual output mode (basic/AddDatabase), fixed replicaServerSMO scope bug from PS1 |
| 10 | New-DbaAvailabilityGroup | DONE | NewDbaAvailabilityGroupCommand.cs | OK | 100% | No Pester tests | ShouldProcess, ConfirmImpact.High, 35+ params, config-backed defaults, version-gated SMO, delegates to Add-DbaAgReplica/Join/Listener/Database/Permission |
| 11 | Set-DbaAgListener | DONE | SetDbaAgListenerCommand.cs | OK | 100% | 1/1 pass | ShouldProcess, ConfirmImpact.High, static ScriptBlocks, SqlCredential aliases preserved |
| 12 | Set-DbaAgReplica | DONE | SetDbaAgReplicaCommand.cs | OK | 100% | 1/1 param pass; integration pre-existing infra issue | ShouldProcess, ConfirmImpact.Medium, ReadOnlyRoutingList load-balanced detection, ConnectionModeInSecondaryRole alias normalization |
| 13 | Set-DbaAvailabilityGroup | DONE | SetDbaAvailabilityGroupCommand.cs | OK | 100% | 1/1 param pass; integration pre-existing infra issue | ShouldProcess, ConfirmImpact.Medium, ClusterConnectionOption version check (SQL 2025+), static ScriptBlocks |
| 14 | Add-DbaAgDatabase | DONE | AddDbaAgDatabaseCommand.cs | OK | 100% | 1/1 param (common param diff expected); integration pre-existing infra issue | ShouldProcess, ConfirmImpact.Low, 5-step workflow (seeding/backup/restore/add/sync), dual parameter sets, progress bars, SQL injection hardened, COLLATE-safe DMV queries |
| 15 | Add-DbaAgListener | DONE | AddDbaAgListenerCommand.cs | OK | 100% | 1/1 param (common param diff expected); integration pre-existing infra issue | ShouldProcess, ConfirmImpact.Low, static ScriptBlocks, subnet auto-calculation, DHCP support, Passthru, ValidateRange on Port |
| 16 | Add-DbaAgReplica | DONE | AddDbaAgReplicaCommand.cs | OK | 100% | 25/25 unit pass | ShouldProcess, ConfirmImpact.Low, 20+ params, config-backed defaults, endpoint auto-creation, WSFC permissions, XE session config, Passthru, anchored regex (ReDoS fix), ValidateRange on BackupPriority/SessionTimeout, fixed PS1 $second.Name bug |
| 17 | Disable-DbaAgHadr | DONE | DisableDbaAgHadrCommand.cs | OK | 100% | 1/1 param (common param diff expected); integration pre-existing infra issue | ShouldProcess, ConfirmImpact.High, WMI-based (DbaBaseCmdlet), static ScriptBlocks, Force restart, TestElevation |
| 18 | Enable-DbaAgHadr | DONE | EnableDbaAgHadrCommand.cs | OK | 100% | 1/1 param (common param diff expected); integration pre-existing infra issue | ShouldProcess, ConfirmImpact.High, WMI-based (DbaBaseCmdlet), static ScriptBlocks, Force restart, TestElevation |
| 19 | Remove-DbaAgDatabase | DONE | RemoveDbaAgDatabaseCommand.cs | OK | 100% | 1/1 param (common param diff expected); integration pre-existing infra issue | ShouldProcess, ConfirmImpact.High, InputObject (Database+AgDb), wired AvailabilityGroup filter (PS1 bug fix) |
| 20 | Remove-DbaAgListener | DONE | RemoveDbaAgListenerCommand.cs | OK | 100% | 1/1 param (common param diff expected); integration pre-existing infra issue | ShouldProcess, ConfirmImpact.High, wired AvailabilityGroup filter (PS1 bug fix) |
| 21 | Remove-DbaAgReplica | DONE | RemoveDbaAgReplicaCommand.cs | OK | 100% | 1/1 param (common param diff expected); integration pre-existing infra issue | ShouldProcess, ConfirmImpact.High, fixed PS1 null AvailabilityGroup output (Parent.AvailabilityGroup bug) |
| 22 | Remove-DbaAvailabilityGroup | DONE | RemoveDbaAvailabilityGroupCommand.cs | OK | 100% | 1/1 param (common param diff expected); integration pre-existing infra issue | ShouldProcess, ConfirmImpact.High, T-SQL DROP with bracket-quoted name (SQL injection fix), AllAvailabilityGroups switch |
| 23 | Compare-DbaAgReplicaAgentJob | DONE | CompareDbaAgReplicaAgentJobCommand.cs | OK | 93% | No Pester tests | ScriptBlock delegation, ExcludeSystemJob, IncludeModifiedDate, AgReplicaHelpers shared |
| 24 | Compare-DbaAgReplicaCredential | DONE | CompareDbaAgReplicaCredentialCommand.cs | OK | 93% | No Pester tests | Identity uniqueness comparison (case-sensitive), AgReplicaHelpers shared |
| 25 | Compare-DbaAgReplicaLogin | DONE | CompareDbaAgReplicaLoginCommand.cs | OK | 91% | No Pester tests | ExcludeSystemLogin, IncludeModifiedDate, sys.server_principals query, AgReplicaHelpers shared |
| 26 | Compare-DbaAgReplicaOperator | DONE | CompareDbaAgReplicaOperatorCommand.cs | OK | 93% | No Pester tests | Email uniqueness comparison (case-sensitive), AgReplicaHelpers shared |
| 27 | Compare-DbaAgReplicaSync | DONE | CompareDbaAgReplicaSyncCommand.cs | OK | 95% | No Pester tests | 8 object types, login property-level diffs, Exclude ValidateSet(13), AgReplicaHelpers shared |
| 28 | Compare-DbaAvailabilityGroup | DONE | CompareDbaAvailabilityGroupCommand.cs | OK | 94% | No Pester tests | Orchestrator, Type ValidateSet(5) default All, delegates to 4 sub-commands |
| 29 | Grant-DbaAgPermission | DONE | GrantDbaAgPermissionCommand.cs | OK | 95% | No Pester tests | ShouldProcess, ConfirmImpact.Low, endpoint+AG dual permissions, auto-creates logins, ObjectPermissionSet, CreateAnyDatabase special handling |
| 30 | Invoke-DbaAgFailover | DONE | InvokeDbaAgFailoverCommand.cs | OK | 95% | No Pester tests | ShouldProcess, ConfirmImpact.High, Force suppresses confirmation, graceful vs force-with-data-loss failover, Refresh after failover |
| 31 | Join-DbaAvailabilityGroup | DONE | JoinDbaAvailabilityGroupCommand.cs | OK | 95% | No Pester tests | ShouldProcess, ConfirmImpact.Low, ClusterType auto-detect from InputObject, SQL 2017+ T-SQL path vs SMO, SQL injection hardened (bracket escape) |
| 32 | Resume-DbaAgDbDataMovement | DONE | ResumeDbaAgDbDataMovementCommand.cs | OK | 95% | No Pester tests | ShouldProcess, ConfirmImpact.High, collect-in-ProcessRecord/execute-in-EndProcessing, AvailabilityGroup filter |
| 33 | Revoke-DbaAgPermission | DONE | RevokeDbaAgPermissionCommand.cs | OK | 95% | No Pester tests | ShouldProcess, ConfirmImpact.Low, mirrors Grant, fixed PS1 bugs (undefined $perm, GRANT instead of REVOKE for CreateAnyDatabase) |
| 34 | Suspend-DbaAgDbDataMovement | DONE | SuspendDbaAgDbDataMovementCommand.cs | OK | 95% | No Pester tests | ShouldProcess, ConfirmImpact.High, mirrors Resume, collect-in-ProcessRecord/execute-in-EndProcessing |
| 35 | Sync-DbaAvailabilityGroup | DONE | SyncDbaAvailabilityGroupCommand.cs | OK | 93% | No Pester tests | Most complex (407-line PS1), DAC connection, 15 Copy/Sync delegations, Write-Progress, SyncCombo deduplication, Force suppresses confirmation |
