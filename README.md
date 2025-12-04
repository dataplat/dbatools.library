# dbatools.library

[![PowerShell Gallery](https://img.shields.io/powershellgallery/v/dbatools.library)](https://www.powershellgallery.com/packages/dbatools.library)
[![NuGet - Dataplat.Dbatools.Csv](https://img.shields.io/nuget/v/Dataplat.Dbatools.Csv.svg?label=nuget%20-%20Csv)](https://www.nuget.org/packages/Dataplat.Dbatools.Csv)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://opensource.org/licenses/MIT)

The library that powers [dbatools](https://dbatools.io), the community module for SQL Server professionals.

## Overview

dbatools.library is a .NET library that provides the core functionality for the dbatools PowerShell module. It includes:

- SQL Server Management Objects (SMO) integration
- Microsoft.Data.SqlClient for SQL Server connectivity
- DacFx for database deployment operations
- Extended Events (XEvent) processing capabilities
- **High-performance CSV reader** for bulk data import (also available as standalone NuGet package)
- Multi-framework support (.NET Framework 4.7.2 and .NET 8.0)

This library enables dbatools to work seamlessly across Windows PowerShell 5.1 and PowerShell 7+ on Windows, macOS, and Linux.

## Standalone NuGet Packages

### Dataplat.Dbatools.Csv

[![NuGet](https://img.shields.io/nuget/v/Dataplat.Dbatools.Csv.svg)](https://www.nuget.org/packages/Dataplat.Dbatools.Csv)

High-performance CSV reader and writer for .NET. **20%+ faster than LumenWorks CsvReader** with modern features:

- Streaming `IDataReader` for SqlBulkCopy (~25,000 rows/sec)
- Automatic compression support (GZip, Deflate, Brotli, ZLib)
- Parallel processing for large files
- Multi-character delimiters, smart quote handling
- Robust error handling and security protections

```bash
dotnet add package Dataplat.Dbatools.Csv
```

See the [CSV package documentation](project/Dataplat.Dbatools.Csv/README.md) for full details.

## Installation

Install from the PowerShell Gallery:

```powershell
# Recommended: Install for all users (requires admin/sudo)
Install-Module dbatools.library -Scope AllUsers

# Or install for current user only
Install-Module dbatools.library -Scope CurrentUser
```

### ⚠️ Important: PowerShell Core + Credentials Issue

**If you plan to use SQL Server credentials with PowerShell Core (pwsh), you MUST install to AllUsers scope or grant appropriate permissions.**

#### The Issue

When using `-SqlCredential` with PowerShell Core, you may encounter this error:

```
unable to load DLL 'Microsoft.Data.SqlClient.SNI.dll'
```

#### Root Cause

This is a **PowerShell Core + Microsoft.Data.SqlClient architectural limitation**, not a bug in dbatools.library:

1. **Credential Impersonation**: When credentials are passed to SQL Server connections, the .NET runtime performs thread-level impersonation using those credentials.

2. **DLL Access Under Impersonation**: During impersonation, `Microsoft.Data.SqlClient` tries to load its native dependency `Microsoft.Data.SqlClient.SNI.dll`, but file system access occurs under the **impersonated credential's security context**, not your current user's context.

3. **Permission Denied**: The impersonated account often lacks read permissions to the module files in your local profile directory (e.g., `C:\Users\<user>\Documents\PowerShell\Modules\`), causing the DLL load to fail.

#### Why Windows PowerShell 5.1 Works

- Uses `System.Data.SqlClient` which is available in the Global Assembly Cache (GAC)
- Different assembly loading behavior that doesn't trigger the same impersonation/access pattern
- The native SNI.dll is already loaded system-wide

#### Solutions

**Option 1: Install to AllUsers Scope (Recommended)**

```powershell
# Uninstall any existing CurrentUser installation first
Uninstall-Module dbatools.library -Force
Uninstall-Module dbatools -Force

# Install to AllUsers scope (requires admin on Windows, sudo on Linux/macOS)
Install-Module dbatools.library -Scope AllUsers
Install-Module dbatools -Scope AllUsers
```

This places the module in `C:\Program Files\PowerShell\Modules\` (Windows) or `/usr/local/share/powershell/Modules` (Linux/macOS), which typically has broader read permissions.

**Option 2: Grant Permissions (Windows)**

If you cannot use AllUsers scope, grant the credential account read access to your PowerShell modules folder:

```powershell
$modulePath = "$env:USERPROFILE\Documents\PowerShell\Modules"
icacls $modulePath /grant "DOMAIN\User:(OI)(CI)R" /T
```

**Option 3: Use Windows PowerShell 5.1**

If neither option above works for your environment, use Windows PowerShell instead of PowerShell Core for credential-based connections:

```powershell
# From Windows PowerShell 5.1
Import-Module dbatools
Connect-DbaInstance -SqlInstance server -SqlCredential $cred
```

#### Additional Notes

- Commands like `Get-DbaDiskSpace -Credential` work fine because they use WinRM/PowerShell Remoting, not SQL Server authentication
- This issue affects any PowerShell Core module using Microsoft.Data.SqlClient with credentials
- Related to [PowerShell Issue #11616](https://github.com/PowerShell/PowerShell/issues/11616)

For more details, see [Issue #28](https://github.com/dataplat/dbatools.library/issues/28).

## Development

### Prerequisites

- .NET SDK 8.0 or later
- .NET Framework 4.7.2 Developer Pack (Windows only)
- PowerShell 7.2+ or Windows PowerShell 5.1
- Git

### Building

```bash
# Clone the repository
git clone https://github.com/dataplat/dbatools.library.git
cd dbatools.library

# Build the library
dotnet build project/dbatools.sln

# Or build for specific configuration
dotnet build project/dbatools.sln -c Release
```

The compiled assemblies will be output to `artifacts/lib/`.

### Multi-Framework Support

The library targets both:
- **.NET Framework 4.7.2**: For Windows PowerShell 5.1 compatibility
- **.NET 8.0**: For PowerShell 7+ cross-platform support

### Project Structure

```
dbatools.library/
├── project/
│   ├── dbatools/              # Main C# library project
│   │   └── Csv/               # CSV reader/writer source
│   ├── Dataplat.Dbatools.Csv/ # Standalone CSV NuGet package
│   ├── dbatools.Tests/        # Unit tests
│   └── dbatools.sln           # Solution file
├── build/                     # Build scripts
├── var/                       # Runtime dependencies
├── dbatools.library.psm1      # PowerShell module script
├── dbatools.library.psd1      # PowerShell module manifest
└── README.md
```

### Testing

```bash
# Run tests
dotnet test project/dbatools.Tests/dbatools.Tests.csproj
```

### Importing for Development

To use your local development version:

```powershell
# Import the module from your local clone
Import-Module /path/to/dbatools.library/dbatools.library.psd1 -Force

# Verify the version and path
Get-Module dbatools.library | Select-Object Name, Version, Path
```

## Key Dependencies

This library includes several major SQL Server components:

| Package | Version | Purpose |
|---------|---------|---------|
| Microsoft.Data.SqlClient | 6.0.2 | SQL Server connectivity |
| Microsoft.SqlServer.SqlManagementObjects | 172.76.0 | SQL Server Management Objects (SMO) |
| Microsoft.SqlServer.DacFx | 170.0.94 | Data-tier Application Framework |
| Microsoft.AnalysisServices | 19.101.1 | Analysis Services management |
| Microsoft.SqlServer.XEvent.XELite | 2024.2.5.1 | Extended Events processing |

### Standalone Packages

| Package | Purpose |
|---------|---------|
| [Dataplat.Dbatools.Csv](https://www.nuget.org/packages/Dataplat.Dbatools.Csv) | High-performance CSV reader/writer for .NET |

## Contributing

Contributions are welcome! This library is primarily maintained by the dbatools team.

### Reporting Issues

- For bugs specific to this library, open an issue in this repository
- For dbatools-related issues, use the [dbatools repository](https://github.com/dataplat/dbatools)
- For the DLL loading issue with credentials, see [Issue #28](https://github.com/dataplat/dbatools.library/issues/28)

### Pull Requests

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/your-feature`)
3. Make your changes
4. Add tests if applicable
5. Ensure the build succeeds
6. Commit your changes (`git commit -m 'Add some feature'`)
7. Push to the branch (`git push origin feature/your-feature`)
8. Open a Pull Request

## Resources

- **dbatools Website**: https://dbatools.io
- **dbatools Documentation**: https://docs.dbatools.io
- **dbatools Repository**: https://github.com/dataplat/dbatools
- **Community Slack**: https://dbatools.io/slack

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.