# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository and the dbatools repository which is also in the workspace.

## Overview

This is **dbatools.library**, the core library that powers the [dbatools](https://dbatools.io) PowerShell module for SQL Server administrators. It's a hybrid PowerShell module containing both C# compiled assemblies and PowerShell scripts that handle complex SQL Server assembly loading and dependency management.

## Key Architecture

### Dual-Target Framework
- **Target Frameworks**: .NET Framework 4.7.2 (Windows PowerShell) and .NET 8.0 (PowerShell Core)
- **Build System**: Uses MSBuild/dotnet CLI with platform-specific assembly deployment
- **Assembly Loading**: Complex assembly resolution system for SQL Server Management Objects (SMO) and related dependencies

### Core Components

#### 1. PowerShell Module Structure
- **Root Module**: `dbatools.library.psm1` - Main module entry point
- **Manifest**: `dbatools.library.psd1` - PowerShell module manifest
- **Private Scripts**: `private/` directory contains assembly loading infrastructure:
  - `assembly-lists.ps1` - Defines required assemblies and version mappings
  - `assembly-loader.ps1` - Core assembly loading logic
  - `assembly-redirector.ps1` - Assembly binding redirects for version conflicts
  - `assembly-resolution.ps1` - Assembly resolution event handlers
  - `assembly-troubleshoot.ps1` - Diagnostics and troubleshooting tools

#### 2. C# Library Structure (`project/dbatools/`)
- **Commands/**: PowerShell cmdlet implementations
- **Configuration/**: Configuration management system with `ConfigurationHost`
- **Connection/**: Database and remote connection management via `ConnectionHost`
- **Message/**: Comprehensive logging and messaging system via `MessageHost`
- **Parameter/**: Custom parameter types and validation
- **Utility/**: Helper classes for dates, sizes, validation
- **TabExpansion/**: PowerShell tab completion support
- **Database/**: Database-specific functionality
- **Discovery/**: SQL Server instance discovery

#### 3. Assembly Distribution (`lib/`)
- **core/**: .NET 8.0 assemblies for PowerShell Core
- **desktop/**: .NET Framework assemblies for Windows PowerShell
- **win-dac/**, **linux-dac/**, **mac-dac/**: Platform-specific Data-Tier Application Framework
- **third-party/**: External dependencies (Bogus, LumenWorks CSV, XESmartTarget)

## Common Development Commands

### Build Commands
```powershell
# Full build (run from repository root)
.\build\build.ps1

# Build C# projects only
cd project
dotnet build

# Clean build artifacts
dotnet clean

# Build specific target framework
dotnet build --framework net8.0
dotnet build --framework net472
```

### Test Commands
```powershell
# Run all tests
cd project
dotnet test

# Run tests for specific framework
dotnet test --framework net8.0
dotnet test --framework net472

# Run specific test project
dotnet test dbatools.Tests/dbatools.Tests.csproj
```

### Development Workflow
```powershell
# Import module for testing (from repository root)
Import-Module .\dbatools.library.psd1 -Force

# Check assembly loading diagnostics
Test-DbatoolsAssemblyLoading
Get-DbatoolsLoadedAssembly
```

## Build System Details

### Build Process (`build/build.ps1`)
1. **Clean**: Removes previous build artifacts
2. **Compile**: Builds C# projects for both target frameworks
3. **Package Dependencies**: Downloads and extracts:
   - SQL Server DacFramework (sqlpackage)
   - Third-party NuGet packages (Bogus, LumenWorks CSV)
   - XESmartTarget for Extended Events
4. **Platform Distribution**: Copies assemblies to appropriate platform directories
5. **Cleanup**: Removes temporary files and non-essential artifacts

### Assembly Resolution Strategy
- Uses custom `AppDomain.AssemblyResolve` event handlers
- Implements binding redirects for version conflicts
- Platform-specific assembly loading (SMO assemblies differ between Windows/Linux/macOS)
- Graceful handling of missing optional assemblies

## Key Design Patterns

### Host Classes
Static host classes provide cross-runspace shared state:
- `ConfigurationHost`: Manages configuration persistence and type conversion
- `ConnectionHost`: Handles database connections and session caching
- `MessageHost`: Provides logging, messaging, and event subscription

### Assembly Loading
Complex multi-stage assembly loading process:
1. **Assembly Lists**: Define required assemblies per platform
2. **Version Resolution**: Handle multiple versions of SQL Server components
3. **Binding Redirects**: Resolve version conflicts automatically
4. **Fallback Loading**: Graceful degradation when assemblies are missing

### Parameter Classes
Custom parameter types with validation:
- `DbaInstanceParameter`: SQL Server instance connection strings
- `DbaCredentialParameter`: Credential management with type conversion
- `DbaSelectParameter`: Object selection with filtering support

## Important Considerations

### Assembly Loading Dependencies
- **Load Order Matters**: Assembly loading scripts must be loaded in specific order
- **Platform Detection**: Uses `$PSVersionTable.PSEdition` and processor architecture
- **Version Conflicts**: SQL Server has complex assembly versioning - binding redirects are critical
- **Cross-Platform**: Different SMO assemblies for Windows vs Linux/macOS

### Testing Environment
- Tests use MSTest framework
- Separate test project: `dbatools.Tests/`
- Test configurations for both PowerShell editions (ps3/ps4 configurations)

### PowerShell Compatibility
- Supports both Windows PowerShell 5.1 and PowerShell 7+
- Uses conditional compilation for framework-specific features
- Assembly loading differs significantly between .NET Framework and .NET Core

## Development Tips

- Always test on both PowerShell editions when making changes
- Use `Test-DbatoolsAssemblyLoading` to diagnose assembly issues
- The `private/assembly-troubleshoot.ps1` contains helpful diagnostic functions
- Build script paths are hardcoded to `C:\github\dbatools.library\` - adjust for your environment
- Assembly loading happens at module import time - use `Import-Module -Force` when testing changes

# dbatools folder

# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Development Commands

### Testing
```powershell
# Run all tests
.\tests\appveyor.pester.ps1

# Run tests with code coverage
.\tests\appveyor.pester.ps1 -IncludeCoverage

# Run specific test file
Invoke-Pester ./tests/Get-DbaDatabase.Tests.ps1

# Finalize test results
.\tests\appveyor.pester.ps1 -Finalize
```

### Development Setup
```powershell
# Enable debugging mode before importing
$dbatools_dotsourcemodule = $true
Import-Module ./dbatools.psd1

# Format code to match project standards (OTBS)
Invoke-DbatoolsFormatter -Path ./public/YourFunction.ps1
```

### Module Dependencies
- Primary dependency: `dbatools.library` module (contains SMO and other libraries)
- Install with: `Install-Module dbatools.library -Scope CurrentUser`

## Architecture Overview

### Core Structure
- **public/**: All user-facing commands (600+ functions). Each command is in its own file.
- **private/**: Internal functions organized by purpose:
  - **functions/**: Core internal helpers like `Connect-DbaInstance`, `Stop-Function`
  - **configurations/**: Module settings organized by topic (sql.ps1, logging.ps1, etc.)
  - **dynamicparams/**: Dynamic parameter definitions for tab completion
  - **maintenance/**: Background tasks and runspace management
- **bin/**: Resources including XE templates, diagnostic queries, build references
- **tests/**: Pester tests matching public function names

### Key Patterns

#### Command Standards
- **Naming**: `Verb-DbaObjectType` (e.g., `Get-DbaDatabase`, `Set-DbaSpConfigure`)
- **Common Parameters**:
  - `-SqlInstance`: Always accepts array of instances
  - `-SqlCredential`: PSCredential for SQL authentication
  - `-EnableException`: Shows full exception details instead of warnings
  - Always implement `-WhatIf`/`-Confirm` for destructive operations

#### Coding Conventions
- **Parameters**: PascalCase, always singular (`$SqlInstance`, not `$SqlInstances`)
- **Variables**: camelCase for multi-word (`$currentLogin`)
- **Error Handling**: Use `Stop-Function` for consistent error management
- **Output**: Use `Select-DefaultView` to control default property display
- **Connections**: Always use `Connect-DbaInstance` for SQL connections

#### Internal Functions
- `Connect-DbaInstance`: Centralizes all SQL Server connections with retry logic
- `Stop-Function`: Standard error handling with `-EnableException` support
- `Write-ProgressHelper`: Consistent progress bar implementation
- `Test-FunctionInterrupt`: Checks for user cancellation in loops
- `Select-DefaultView`: Controls which properties are displayed by default

### Configuration System
- Get settings: `Get-DbatoolsConfigValue -Name 'sql.connection.timeout'`
- Set settings: `Set-DbatoolsConfig -Name 'sql.connection.timeout' -Value 30`
- Configuration files in `private/configurations/` define module-wide defaults

### Testing Approach
- Test files must match function names: `Get-DbaDatabase.ps1` → `Get-DbaDatabase.Tests.ps1`
- Tests use Pester 4/5 compatible syntax
- AppVeyor CI tests against SQL Server 2008R2, 2016, 2017, 2022
- Mock `Connect-DbaInstance` in unit tests to avoid SQL dependencies

### Module Loading
1. `dbatools.psd1` defines module manifest
2. `dbatools.psm1` handles initialization:
   - Loads dbatools.library dependency
   - Imports all public/private functions
   - Initializes configuration system
   - Starts maintenance runspaces
3. Use `$dbatools_dotsourcemodule = $true` before import for debugging