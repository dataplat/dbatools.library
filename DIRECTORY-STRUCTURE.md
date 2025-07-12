# dbatools.library Module Structure

This document outlines the directory structure of the dbatools.library module as it appears when installed or deployed. Understanding this structure is essential for development, troubleshooting, and maintaining the module.

## Final Module Structure

When installed or imported, the dbatools.library module has the following structure:

```
dbatools.library/
├── desktop/                    # .NET Framework (Windows PowerShell) assemblies
│   ├── lib/                    # Main assemblies
│   │   ├── runtimes/           # Runtime-specific files
│   │   │   ├── win-x64/        # Windows x64 runtime files
│   │   │   │   └── native/     # Native dependencies (SNI DLL)
│   │   │   ├── win-x86/        # Windows x86 runtime files
│   │   │   └── win-arm64/      # Windows ARM64 runtime files
│   │   └── *.dll               # Main assemblies
│   └── third-party/            # Third-party libraries for desktop
├── core/                       # .NET Core (PowerShell Core) assemblies
│   ├── lib/                    # Main assemblies
│   │   ├── runtimes/           # Runtime-specific files
│   │   │   ├── win-x64/        # Windows x64 runtime files
│   │   │   │   └── native/     # Native dependencies (SNI DLL)
│   │   │   ├── win-x86/        # Windows x86 runtime files
│   │   │   └── win-arm64/      # Windows ARM64 runtime files
│   │   └── *.dll               # Main assemblies
│   ├── third-party/            # Third-party libraries for core
│   └── lib/dac/                # DAC components
│       ├── windows/            # Windows DAC files
│       ├── linux/              # Linux DAC files
│       └── mac/                # macOS DAC files
├── private/                    # Private PowerShell functions
│   ├── assembly-lists.ps1      # Lists of assemblies to load
│   ├── assembly-loader.ps1     # Handles loading assemblies
│   ├── assembly-redirector.ps1 # Handles assembly binding redirects
│   ├── assembly-resolution.ps1 # Resolves assembly paths
│   └── assembly-troubleshoot.ps1 # Helps diagnose assembly loading issues
├── third-party-licenses/       # License files
├── dbatools.library.psd1       # Module manifest
└── dbatools.library.psm1       # Module script
```

## Key Paths for Assembly Loading

### Windows PowerShell (.NET Framework)
- Main assemblies: `desktop/lib/*.dll`
- Native dependencies: `desktop/lib/runtimes/win-{arch}/native/*.dll`
- Third-party libraries: `desktop/third-party/{library-name}/*.dll`

### PowerShell Core (.NET Core)
- Main assemblies: `core/lib/*.dll`
- Native dependencies: `core/lib/runtimes/win-{arch}/native/*.dll`
- Third-party libraries: `core/third-party/{library-name}/*.dll`

## Critical Files

### SNI DLL Locations
The SQL Server Native Interface (SNI) DLL is essential for SQL Server connectivity:

- Windows PowerShell (.NET Framework):
  ```
  desktop/lib/runtimes/win-x64/native/Microsoft.Data.SqlClient.SNI.dll
  ```

- PowerShell Core (.NET Core):
  ```
  core/lib/runtimes/win-x64/native/Microsoft.Data.SqlClient.SNI.dll
  ```

### Core Assemblies
- `desktop/lib/dbatools.dll` - Main assembly for Windows PowerShell
- `desktop/lib/Microsoft.Data.SqlClient.dll` - SQL Client for Windows PowerShell
- `core/lib/dbatools.dll` - Main assembly for PowerShell Core
- `core/lib/Microsoft.Data.SqlClient.dll` - SQL Client for PowerShell Core

## Assembly Loading Process

1. When the module is imported, it initializes the assembly loader
2. For Windows PowerShell, it also initializes the assembly redirector
3. The loader first tries to find assemblies in the runtime-specific path (desktop or core)
4. If not found, it falls back to the alternative runtime path
5. For native dependencies like the SNI DLL, it follows a similar process but with architecture-specific paths

## Runtime Detection

The module automatically detects:
- PowerShell runtime (Core vs Desktop)
- Operating system (Windows, Linux, macOS)
- Architecture (x64, x86, ARM64)

Based on this detection, it loads the appropriate assemblies from the corresponding directories.

## Troubleshooting Assembly Loading

If you encounter assembly loading issues:

1. Check that the SNI DLL exists in the correct location for your runtime
2. Verify that the path structure matches what the code expects
3. Use the assembly-troubleshoot.ps1 script to diagnose issues
4. Ensure the module structure hasn't been modified or corrupted