# Migration Tracker: instance

## Dependencies
- Requires: foundation

## Commands

| # | Command | Status | C# File | Build | Parity | Pester | Notes |
|---|---------|--------|---------|-------|--------|--------|-------|
| 1 | Get-DbaBuild | PENDING | | | | | Read-only, no deps |
| 2 | Get-DbaCpuRingBuffer | PENDING | | | | | Read-only, no deps |
| 3 | Get-DbaCpuUsage | PENDING | | | | | Read-only, no deps |
| 4 | Get-DbaDeprecatedFeature | PENDING | | | | | Read-only, no deps |
| 5 | Get-DbaDump | PENDING | | | | | Read-only, no deps |
| 6 | Get-DbaErrorLog | PENDING | | | | | Read-only, no deps |
| 7 | Get-DbaErrorLogConfig | PENDING | | | | | Read-only, no deps |
| 8 | Get-DbaEstimatedCompletionTime | PENDING | | | | | Read-only, no deps |
| 9 | Get-DbaExecutionPlan | PENDING | | | | | Read-only, no deps |
| 10 | Get-DbaExternalProcess | PENDING | | | | | Read-only, no deps |
| 11 | Get-DbaFeature | PENDING | | | | | Read-only, no deps |
| 12 | Get-DbaFile | PENDING | | | | | Read-only, no deps |
| 13 | Get-DbaInstalledPatch | PENDING | | | | | Read-only, no deps |
| 14 | Get-DbaInstanceAudit | PENDING | | | | | Read-only, no deps |
| 15 | Get-DbaInstanceAuditSpecification | PENDING | | | | | Read-only, no deps |
| 16 | Get-DbaInstanceInstallDate | PENDING | | | | | Read-only, no deps |
| 17 | Get-DbaInstanceProperty | PENDING | | | | | Read-only, no deps |
| 18 | Get-DbaInstanceProtocol | PENDING | | | | | Read-only, no deps |
| 19 | Get-DbaInstanceTrigger | PENDING | | | | | Read-only, no deps |
| 20 | Get-DbaInstanceUserOption | PENDING | | | | | Read-only, no deps |
| 21 | Get-DbaIoLatency | PENDING | | | | | Read-only, no deps |
| 22 | Get-DbaLatchStatistic | PENDING | | | | | Read-only, no deps |
| 23 | Get-DbaLinkedServer | PENDING | | | | | Read-only, no deps |
| 24 | Get-DbaLinkedServerLogin | PENDING | | | | | Read-only, no deps |
| 25 | Get-DbaManagementObject | PENDING | | | | | Read-only, no deps |
| 26 | Get-DbaMaxMemory | PENDING | | | | | Read-only, no deps |
| 27 | Get-DbaNetworkActivity | PENDING | | | | | Read-only, no deps |
| 28 | Get-DbaOleDbProvider | PENDING | | | | | Read-only, no deps |
| 29 | Get-DbaOpenTransaction | PENDING | | | | | Read-only, no deps |
| 30 | Get-DbaPlanCache | PENDING | | | | | Read-only, no deps |
| 31 | Get-DbaProcess | PENDING | | | | | Read-only, no deps |
| 32 | Get-DbaProductKey | PENDING | | | | | Read-only, no deps |
| 33 | Get-DbaQueryExecutionTime | PENDING | | | | | Read-only, no deps |
| 34 | Get-DbaRegistryRoot | PENDING | | | | | Read-only, no deps |
| 35 | Get-DbaSpConfigure | PENDING | | | | | Read-only, no deps |
| 36 | Get-DbaSpinLockStatistic | PENDING | | | | | Read-only, no deps |
| 37 | Get-DbaStartupParameter | PENDING | | | | | Read-only, no deps |
| 38 | Get-DbaTempdbUsage | PENDING | | | | | Read-only, no deps |
| 39 | Get-DbaTopResourceUsage | PENDING | | | | | Read-only, no deps |
| 40 | Get-DbaTrace | PENDING | | | | | Read-only, no deps |
| 41 | Get-DbaTraceFlag | PENDING | | | | | Read-only, no deps |
| 42 | Get-DbaUptime | PENDING | | | | | Read-only, no deps |
| 43 | Get-DbaWaitingTask | PENDING | | | | | Read-only, no deps |
| 44 | Get-DbaWaitResource | PENDING | | | | | Read-only, no deps |
| 45 | Get-DbaWaitStatistic | PENDING | | | | | Read-only, no deps |
| 46 | Test-DbaBuild | PENDING | | | | |  |
| 47 | Test-DbaInstanceName | PENDING | | | | |  |
| 48 | Test-DbaLinkedServerConnection | PENDING | | | | |  |
| 49 | Test-DbaManagementObject | PENDING | | | | |  |
| 50 | Test-DbaMaxDop | PENDING | | | | |  |
| 51 | Test-DbaMaxMemory | PENDING | | | | |  |
| 52 | Test-DbaOptimizeForAdHoc | PENDING | | | | |  |
| 53 | Test-DbaTempDbConfig | PENDING | | | | |  |
| 54 | Find-DbaCommand | PENDING | | | | |  |
| 55 | Find-DbaInstance | PENDING | | | | |  |
| 56 | New-DbaDirectory | PENDING | | | | | ShouldProcess required |
| 57 | New-DbaLinkedServer | PENDING | | | | | ShouldProcess required |
| 58 | New-DbaLinkedServerLogin | PENDING | | | | | ShouldProcess required |
| 59 | Set-DbaErrorLogConfig | PENDING | | | | | ShouldProcess required, depends on Get-DbaErrorLogConfig |
| 60 | Set-DbaMaxDop | PENDING | | | | | ShouldProcess required |
| 61 | Set-DbaMaxMemory | PENDING | | | | | ShouldProcess required, depends on Get-DbaMaxMemory |
| 62 | Set-DbaSpConfigure | PENDING | | | | | ShouldProcess required, depends on Get-DbaSpConfigure |
| 63 | Set-DbaStartupParameter | PENDING | | | | | ShouldProcess required, depends on Get-DbaStartupParameter |
| 64 | Set-DbaTempDbConfig | PENDING | | | | | ShouldProcess required |
| 65 | Disable-DbaTraceFlag | PENDING | | | | | ShouldProcess required |
| 66 | Enable-DbaTraceFlag | PENDING | | | | | ShouldProcess required |
| 67 | Remove-DbaLinkedServer | PENDING | | | | | ShouldProcess required, depends on Get-DbaLinkedServer |
| 68 | Remove-DbaLinkedServerLogin | PENDING | | | | | ShouldProcess required, depends on Get-DbaLinkedServerLogin |
| 69 | Remove-DbaTrace | PENDING | | | | | ShouldProcess required, depends on Get-DbaTrace |
| 70 | Copy-DbaInstanceAudit | PENDING | | | | |  |
| 71 | Copy-DbaInstanceAuditSpecification | PENDING | | | | |  |
| 72 | Copy-DbaInstanceTrigger | PENDING | | | | |  |
| 73 | Copy-DbaLinkedServer | PENDING | | | | |  |
| 74 | Copy-DbaSpConfigure | PENDING | | | | |  |
| 75 | Clear-DbaLatchStatistics | PENDING | | | | |  |
| 76 | Clear-DbaPlanCache | PENDING | | | | |  |
| 77 | Clear-DbaWaitStatistics | PENDING | | | | |  |
| 78 | Export-DbaExecutionPlan | PENDING | | | | |  |
| 79 | Export-DbaInstance | PENDING | | | | |  |
| 80 | Export-DbaLinkedServer | PENDING | | | | |  |
| 81 | Export-DbaScript | PENDING | | | | |  |
| 82 | Export-DbaSpConfigure | PENDING | | | | |  |
| 83 | Import-DbaSpConfigure | PENDING | | | | |  |
| 84 | Install-DbaInstance | PENDING | | | | |  |
| 85 | Invoke-DbaCycleErrorLog | PENDING | | | | |  |
| 86 | Read-DbaTraceFile | PENDING | | | | |  |
| 87 | Read-DbaTransactionLog | PENDING | | | | |  |
| 88 | Repair-DbaInstanceName | PENDING | | | | |  |
| 89 | Show-DbaInstanceFileSystem | PENDING | | | | |  |
| 90 | Start-DbaTrace | PENDING | | | | |  |
| 91 | Stop-DbaExternalProcess | PENDING | | | | |  |
| 92 | Stop-DbaProcess | PENDING | | | | |  |
| 93 | Stop-DbaTrace | PENDING | | | | |  |
| 94 | Update-DbaBuildReference | PENDING | | | | |  |
| 95 | Update-DbaInstance | PENDING | | | | |  |
