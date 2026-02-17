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
| 6 | New-DbaConnectionString | DONE | NewDbaConnectionStringCommand.cs | OK | OK | 1/1 | ShouldProcess preserved; both new and legacy code paths implemented |
| 7 | New-DbaConnectionStringBuilder | DONE | NewDbaConnectionStringBuilderCommand.cs | OK | OK | 30/30 | No ShouldProcess needed; both Microsoft.Data and System.Data providers supported |
| 8 | New-DbaScriptingOption | DONE | NewDbaScriptingOptionCommand.cs | OK | OK | 14/14 | No ShouldProcess needed (in-memory factory); Pester improved from 0/1 to 14/14 |
| 9 | New-DbaSqlParameter | PENDING | | | | | ShouldProcess required |
| 10 | Clear-DbaConnectionPool | PENDING | | | | |  |
| 11 | Connect-DbaInstance | PENDING | | | | |  |
| 12 | Disconnect-DbaInstance | PENDING | | | | |  |
| 13 | Invoke-DbaQuery | PENDING | | | | |  |
| 14 | Join-DbaPath | PENDING | | | | |  |
| 15 | Resolve-DbaNetworkName | PENDING | | | | |  |
| 16 | Resolve-DbaPath | PENDING | | | | |  |
