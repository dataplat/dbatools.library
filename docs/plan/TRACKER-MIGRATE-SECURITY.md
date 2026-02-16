# Migration Tracker: security

## Dependencies
- Requires: foundation

## Commands

| # | Command | Status | C# File | Build | Parity | Pester | Notes |
|---|---------|--------|---------|-------|--------|--------|-------|
| 1 | Get-DbaCredential | PENDING | | | | | Read-only, no deps |
| 2 | Get-DbaDbAsymmetricKey | PENDING | | | | | Read-only, no deps |
| 3 | Get-DbaDbCertificate | PENDING | | | | | Read-only, no deps |
| 4 | Get-DbaDbEncryption | PENDING | | | | | Read-only, no deps |
| 5 | Get-DbaDbEncryptionKey | PENDING | | | | | Read-only, no deps |
| 6 | Get-DbaDbMasterKey | PENDING | | | | | Read-only, no deps |
| 7 | Get-DbaDbOrphanUser | PENDING | | | | | Read-only, no deps |
| 8 | Get-DbaDbRole | PENDING | | | | | Read-only, no deps |
| 9 | Get-DbaDbRoleMember | PENDING | | | | | Read-only, no deps |
| 10 | Get-DbaDbUser | PENDING | | | | | Read-only, no deps |
| 11 | Get-DbaLogin | PENDING | | | | | Read-only, no deps |
| 12 | Get-DbaPermission | PENDING | | | | | Read-only, no deps |
| 13 | Get-DbaServerRole | PENDING | | | | | Read-only, no deps |
| 14 | Get-DbaServerRoleMember | PENDING | | | | | Read-only, no deps |
| 15 | Get-DbaSpn | PENDING | | | | | Read-only, no deps |
| 16 | Get-DbaUserPermission | PENDING | | | | | Read-only, no deps |
| 17 | Test-DbaKerberos | PENDING | | | | |  |
| 18 | Test-DbaLoginPassword | PENDING | | | | |  |
| 19 | Test-DbaSpn | PENDING | | | | |  |
| 20 | Test-DbaWindowsLogin | PENDING | | | | |  |
| 21 | Find-DbaLoginInGroup | PENDING | | | | |  |
| 22 | New-DbaCredential | PENDING | | | | | ShouldProcess required |
| 23 | New-DbaDbAsymmetricKey | PENDING | | | | | ShouldProcess required |
| 24 | New-DbaDbCertificate | PENDING | | | | | ShouldProcess required |
| 25 | New-DbaDbEncryptionKey | PENDING | | | | | ShouldProcess required |
| 26 | New-DbaDbMasterKey | PENDING | | | | | ShouldProcess required |
| 27 | New-DbaDbRole | PENDING | | | | | ShouldProcess required |
| 28 | New-DbaDbUser | PENDING | | | | | ShouldProcess required |
| 29 | New-DbaLogin | PENDING | | | | | ShouldProcess required |
| 30 | New-DbaServerRole | PENDING | | | | | ShouldProcess required |
| 31 | New-DbaServiceMasterKey | PENDING | | | | | ShouldProcess required |
| 32 | Set-DbaLogin | PENDING | | | | | ShouldProcess required, depends on Get-DbaLogin |
| 33 | Set-DbaSpn | PENDING | | | | | ShouldProcess required, depends on Get-DbaSpn |
| 34 | Add-DbaDbRoleMember | PENDING | | | | |  |
| 35 | Add-DbaServerRoleMember | PENDING | | | | |  |
| 36 | Disable-DbaDbEncryption | PENDING | | | | | ShouldProcess required |
| 37 | Enable-DbaDbEncryption | PENDING | | | | | ShouldProcess required |
| 38 | Remove-DbaCredential | PENDING | | | | | ShouldProcess required, depends on Get-DbaCredential |
| 39 | Remove-DbaDbAsymmetricKey | PENDING | | | | | ShouldProcess required, depends on Get-DbaDbAsymmetricKey |
| 40 | Remove-DbaDbCertificate | PENDING | | | | | ShouldProcess required, depends on Get-DbaDbCertificate |
| 41 | Remove-DbaDbEncryptionKey | PENDING | | | | | ShouldProcess required, depends on Get-DbaDbEncryptionKey |
| 42 | Remove-DbaDbMasterKey | PENDING | | | | | ShouldProcess required, depends on Get-DbaDbMasterKey |
| 43 | Remove-DbaDbOrphanUser | PENDING | | | | | ShouldProcess required, depends on Get-DbaDbOrphanUser |
| 44 | Remove-DbaDbRole | PENDING | | | | | ShouldProcess required, depends on Get-DbaDbRole |
| 45 | Remove-DbaDbRoleMember | PENDING | | | | | ShouldProcess required, depends on Get-DbaDbRoleMember |
| 46 | Remove-DbaDbUser | PENDING | | | | | ShouldProcess required, depends on Get-DbaDbUser |
| 47 | Remove-DbaLogin | PENDING | | | | | ShouldProcess required, depends on Get-DbaLogin |
| 48 | Remove-DbaServerRole | PENDING | | | | | ShouldProcess required, depends on Get-DbaServerRole |
| 49 | Remove-DbaServerRoleMember | PENDING | | | | | ShouldProcess required, depends on Get-DbaServerRoleMember |
| 50 | Remove-DbaSpn | PENDING | | | | | ShouldProcess required, depends on Get-DbaSpn |
| 51 | Copy-DbaCredential | PENDING | | | | |  |
| 52 | Copy-DbaDbCertificate | PENDING | | | | |  |
| 53 | Copy-DbaLogin | PENDING | | | | |  |
| 54 | Copy-DbaServerRole | PENDING | | | | |  |
| 55 | Backup-DbaDbCertificate | PENDING | | | | |  |
| 56 | Backup-DbaDbMasterKey | PENDING | | | | |  |
| 57 | Backup-DbaServiceMasterKey | PENDING | | | | |  |
| 58 | Export-DbaCredential | PENDING | | | | |  |
| 59 | Export-DbaDbRole | PENDING | | | | |  |
| 60 | Export-DbaLogin | PENDING | | | | |  |
| 61 | Export-DbaServerRole | PENDING | | | | |  |
| 62 | Export-DbaUser | PENDING | | | | |  |
| 63 | Rename-DbaLogin | PENDING | | | | |  |
| 64 | Repair-DbaDbOrphanUser | PENDING | | | | |  |
| 65 | Reset-DbaAdmin | PENDING | | | | |  |
| 66 | Restore-DbaDbCertificate | PENDING | | | | |  |
| 67 | Start-DbaDbEncryption | PENDING | | | | |  |
| 68 | Stop-DbaDbEncryption | PENDING | | | | |  |
| 69 | Sync-DbaLoginPassword | PENDING | | | | |  |
| 70 | Sync-DbaLoginPermission | PENDING | | | | |  |
| 71 | Update-DbaServiceAccount | PENDING | | | | |  |
