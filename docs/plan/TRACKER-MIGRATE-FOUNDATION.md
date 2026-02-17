# Migration Tracker: foundation

## Dependencies
- Requires: None (must be converted first)

## Commands

| # | Command | Status | C# File | Build | Parity | Pester | Notes |
|---|---------|--------|---------|-------|--------|--------|-------|
| 1 | Get-DbaConnectedInstance | DONE | GetDbaConnectedInstanceCommand.cs | OK | OK | 2/2 | Read-only, no deps |
| 2 | Get-DbaConnection | DONE | GetDbaConnectionCommand.cs | OK | OK | 2/2 | Read-only, no deps |
| 3 | Test-DbaConnection | DONE | TestDbaConnectionCommand.cs | OK | OK | 2/2 | Pester improved: 1 pre-existing failure now passes |
| 4 | Test-DbaPath | DONE | TestDbaPathCommand.cs | OK | OK | 7/7 | Pester improved: 6 pre-existing failures now pass |
| 5 | New-DbaAzAccessToken | DONE | NewDbaAzAccessTokenCommand.cs | OK | OK | 1/1 | ADAL replaced with direct OAuth2 REST; ServicePrincipal now works on Core |
| 6 | New-DbaConnectionString | PENDING | | | | | ShouldProcess required |
| 7 | New-DbaConnectionStringBuilder | PENDING | | | | | ShouldProcess required |
| 8 | New-DbaScriptingOption | PENDING | | | | | ShouldProcess required |
| 9 | New-DbaSqlParameter | PENDING | | | | | ShouldProcess required |
| 10 | Clear-DbaConnectionPool | PENDING | | | | |  |
| 11 | Connect-DbaInstance | PENDING | | | | |  |
| 12 | Disconnect-DbaInstance | PENDING | | | | |  |
| 13 | Invoke-DbaQuery | PENDING | | | | |  |
| 14 | Join-DbaPath | PENDING | | | | |  |
| 15 | Resolve-DbaNetworkName | PENDING | | | | |  |
| 16 | Resolve-DbaPath | PENDING | | | | |  |
