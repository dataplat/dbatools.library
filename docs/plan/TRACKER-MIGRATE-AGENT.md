# Migration Tracker: agent

## Dependencies
- Requires: foundation

## Commands

| # | Command | Status | C# File | Build | Parity | Pester | Notes |
|---|---------|--------|---------|-------|--------|--------|-------|
| 1 | Get-DbaAgentAlert | DONE | GetDbaAgentAlertCommand.cs | OK | 100% | 2/2 pass | Read-only, no deps |
| 2 | Get-DbaAgentAlertCategory | DONE | GetDbaAgentAlertCategoryCommand.cs | OK | 100% | Pre-existing PSDefaultParameterValues issue | Read-only, no deps |
| 3 | Get-DbaAgentJob | DONE | GetDbaAgentJobCommand.cs | OK | 100% | Pre-existing dev-module sqlCredential issue (6 tests); 15/21 pass | Read-only, no deps |
| 4 | Get-DbaAgentJobCategory | DONE | GetDbaAgentJobCategoryCommand.cs | OK | 100% | Pre-existing dev-module sqlCredential issue (1/4 pass); C# cmdlet verified correct | Read-only, no deps |
| 5 | Get-DbaAgentJobHistory | DONE | GetDbaAgentJobHistoryCommand.cs | OK | 100% | 1/22 pass; 20 failures from InModuleScope mock incompatibility with C# InvokeScript (known limitation); C# cmdlet verified working against real SQL instances | Read-only, uses InvokeScript for Connect-DbaInstance/EnumJobHistory/Get-DbaAgentJobOutputFile |
| 6 | Get-DbaAgentJobOutputFile | DONE | GetDbaAgentJobOutputFileCommand.cs | OK | 100% | 14/14 pass | Read-only, no deps |
| 7 | Get-DbaAgentJobStep | DONE | GetDbaAgentJobStepCommand.cs | OK | 100% | 11/11 pass, 1 skip | Read-only, no deps |
| 8 | Get-DbaAgentLog | DONE | GetDbaAgentLogCommand.cs | OK | 100% | 10/10 pass, 1 skip | Read-only, no deps |
| 9 | Get-DbaAgentOperator | DONE | GetDbaAgentOperatorCommand.cs | OK | 100% | 3/3 pass (fixed pre-existing 2 failures) | Read-only, no deps |
| 10 | Get-DbaAgentProxy | DONE | GetDbaAgentProxyCommand.cs | OK | 100% | 1/1 pass (9 pre-existing failures) | Read-only, no deps |
| 11 | Get-DbaAgentSchedule | DONE | GetDbaAgentScheduleCommand.cs | OK | 100% | 1/1 pass (10 pre-existing failures in BeforeAll) | Read-only, no deps |
| 12 | Get-DbaAgentServer | DONE | GetDbaAgentServerCommand.cs | OK | 100% | 2/2 pass (fixed pre-existing 1 failure) | Read-only, no deps |
| 13 | Get-DbaRunningJob | DONE | GetDbaRunningJobCommand.cs | OK | OK | 1/1 | Read-only, delegates to Get-DbaAgentJob |
| 14 | Test-DbaAgentJobOwner | PENDING | | | | |  |
| 15 | Find-DbaAgentJob | PENDING | | | | |  |
| 16 | New-DbaAgentAlert | PENDING | | | | | ShouldProcess required |
| 17 | New-DbaAgentAlertCategory | PENDING | | | | | ShouldProcess required |
| 18 | New-DbaAgentJob | PENDING | | | | | ShouldProcess required |
| 19 | New-DbaAgentJobCategory | PENDING | | | | | ShouldProcess required |
| 20 | New-DbaAgentJobStep | PENDING | | | | | ShouldProcess required |
| 21 | New-DbaAgentOperator | PENDING | | | | | ShouldProcess required |
| 22 | New-DbaAgentProxy | PENDING | | | | | ShouldProcess required |
| 23 | New-DbaAgentSchedule | PENDING | | | | | ShouldProcess required |
| 24 | Set-DbaAgentAlert | PENDING | | | | | ShouldProcess required, depends on Get-DbaAgentAlert |
| 25 | Set-DbaAgentJob | PENDING | | | | | ShouldProcess required, depends on Get-DbaAgentJob |
| 26 | Set-DbaAgentJobCategory | PENDING | | | | | ShouldProcess required, depends on Get-DbaAgentJobCategory |
| 27 | Set-DbaAgentJobOutputFile | PENDING | | | | | ShouldProcess required, depends on Get-DbaAgentJobOutputFile |
| 28 | Set-DbaAgentJobOwner | PENDING | | | | | ShouldProcess required |
| 29 | Set-DbaAgentJobStep | PENDING | | | | | ShouldProcess required, depends on Get-DbaAgentJobStep |
| 30 | Set-DbaAgentOperator | PENDING | | | | | ShouldProcess required, depends on Get-DbaAgentOperator |
| 31 | Set-DbaAgentSchedule | PENDING | | | | | ShouldProcess required, depends on Get-DbaAgentSchedule |
| 32 | Set-DbaAgentServer | PENDING | | | | | ShouldProcess required, depends on Get-DbaAgentServer |
| 33 | Remove-DbaAgentAlert | PENDING | | | | | ShouldProcess required, depends on Get-DbaAgentAlert |
| 34 | Remove-DbaAgentAlertCategory | PENDING | | | | | ShouldProcess required, depends on Get-DbaAgentAlertCategory |
| 35 | Remove-DbaAgentJob | PENDING | | | | | ShouldProcess required, depends on Get-DbaAgentJob |
| 36 | Remove-DbaAgentJobCategory | PENDING | | | | | ShouldProcess required, depends on Get-DbaAgentJobCategory |
| 37 | Remove-DbaAgentJobStep | PENDING | | | | | ShouldProcess required, depends on Get-DbaAgentJobStep |
| 38 | Remove-DbaAgentOperator | PENDING | | | | | ShouldProcess required, depends on Get-DbaAgentOperator |
| 39 | Remove-DbaAgentProxy | PENDING | | | | | ShouldProcess required, depends on Get-DbaAgentProxy |
| 40 | Remove-DbaAgentSchedule | PENDING | | | | | ShouldProcess required, depends on Get-DbaAgentSchedule |
| 41 | Copy-DbaAgentAlert | PENDING | | | | |  |
| 42 | Copy-DbaAgentJob | PENDING | | | | |  |
| 43 | Copy-DbaAgentJobCategory | PENDING | | | | |  |
| 44 | Copy-DbaAgentJobStep | PENDING | | | | |  |
| 45 | Copy-DbaAgentOperator | PENDING | | | | |  |
| 46 | Copy-DbaAgentProxy | PENDING | | | | |  |
| 47 | Copy-DbaAgentSchedule | PENDING | | | | |  |
| 48 | Copy-DbaAgentServer | PENDING | | | | |  |
| 49 | Install-DbaAgentAdminAlert | PENDING | | | | |  |
| 50 | Start-DbaAgentJob | PENDING | | | | |  |
| 51 | Stop-DbaAgentJob | PENDING | | | | |  |
