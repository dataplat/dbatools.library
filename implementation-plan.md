# dbatools.library Implementation Plan

## Overview
This document outlines the implementation plan for finalizing the dbatools.library module, focusing on cross-platform compatibility and proper assembly loading across different .NET runtimes.

## 1. Completing build.ps1

### DAC File Distribution
```powershell
# After line 91 ($dacPath = ".\temp\dacfull\Microsoft SQL Server\160\DAC\bin")
# Copy Windows DAC files
Copy-Item "$dacPath\Microsoft.SqlServer.Dac.dll" -Destination "./lib/win-dac/" -Force
Copy-Item "$dacPath\Microsoft.SqlServer.Dac.Extensions.dll" -Destination "./lib/win-dac/" -Force
Copy-Item "$dacPath\Microsoft.Data.Tools.Schema.Sql.dll" -Destination "./lib/win-dac/" -Force

# Copy platform-specific DAC files from temp
Copy-Item "./temp/linux/*" -Destination "./lib/linux-dac/" -Force -Recurse
Copy-Item "./temp/mac/*" -Destination "./lib/mac-dac/" -Force -Recurse

# Handle SqlClient Runtime Files
Copy-Item "./temp/sqlclient/x64/*" -Destination "./lib/win-sqlclient/" -Force
Copy-Item "./temp/sqlclient/x86/*" -Destination "./lib/win-sqlclient-x86/" -Force
```

## 2. Assembly Loading Scripts

### assembly-lists.ps1
```powershell
# Define required assemblies and their versions
$script:CoreAssemblies = @{
    'Microsoft.Data.SqlClient' = '5.1.5'
    'Microsoft.SqlServer.Smo' = '170.18.0'
    'Microsoft.SqlServer.Management.Sdk.Sfc' = '170.18.0'
    # Add other core assemblies
}

$script:DacAssemblies = @{
    'Microsoft.SqlServer.Dac' = '170.18.0'
    'Microsoft.SqlServer.Dac.Extensions' = '170.18.0'
    'Microsoft.Data.Tools.Schema.Sql' = '170.18.0'
}

# Platform-specific assembly mappings
$script:PlatformAssemblies = @{
    'Windows' = @{
        'x64' = "./lib/win-sqlclient"
        'x86' = "./lib/win-sqlclient-x86"
        'DAC' = "./lib/win-dac"
    }
    'Linux' = @{
        'DAC' = "./lib/linux-dac"
    }
    'OSX' = @{
        'DAC' = "./lib/mac-dac"
    }
}

# Define load order for assemblies
$script:AssemblyLoadOrder = @(
    'Microsoft.Data.SqlClient'
    'Microsoft.SqlServer.Management.Sdk.Sfc'
    'Microsoft.SqlServer.Smo'
    'Microsoft.SqlServer.Dac'
    # Add other assemblies in correct load order
)
```

### assembly-resolution.ps1
```powershell
function Get-PlatformInfo {
    $platform = if ($IsWindows -or (!$PSVersionTable.Platform)) {
        'Windows'
    } elseif ($IsLinux) {
        'Linux'
    } else {
        'OSX'
    }

    $architecture = if ($platform -eq 'Windows') {
        if ([System.Environment]::Is64BitProcess) { 'x64' } else { 'x86' }
    } else {
        'x64' # Linux/OSX are assumed x64
    }

    @{
        Platform = $platform
        Architecture = $architecture
    }
}

function Get-AssemblyPath {
    param (
        [string]$AssemblyName,
        [string]$Platform,
        [string]$Architecture
    )

    # Check platform-specific paths first
    if ($PlatformAssemblies[$Platform]) {
        if ($AssemblyName -like 'Microsoft.SqlServer.Dac*') {
            return Join-Path $PlatformAssemblies[$Platform]['DAC'] "$AssemblyName.dll"
        }

        if ($Platform -eq 'Windows' -and $AssemblyName -eq 'Microsoft.Data.SqlClient') {
            return Join-Path $PlatformAssemblies[$Platform][$Architecture] "$AssemblyName.dll"
        }
    }

    # Fallback to appropriate framework directory
    $framework = if ($PSVersionTable.PSEdition -eq 'Core') { 'core' } else { 'desktop' }
    Join-Path $script:libraryroot "lib/$framework/$AssemblyName.dll"
}

# Set up assembly resolution handler
$script:onAssemblyResolveEventHandler = [System.ResolveEventHandler] {
    param($sender, $args)

    $assemblyName = [System.Reflection.AssemblyName]::new($args.Name)
    $platformInfo = Get-PlatformInfo

    $assemblyPath = Get-AssemblyPath -AssemblyName $assemblyName.Name `
                                    -Platform $platformInfo.Platform `
                                    -Architecture $platformInfo.Architecture

    if (Test-Path $assemblyPath) {
        return [System.Reflection.Assembly]::LoadFrom($assemblyPath)
    }

    # Fallback for SqlClient only
    if ($assemblyName.Name -eq 'Microsoft.Data.SqlClient') {
        $fallbackPath = Join-Path $script:libraryroot "lib/core/$($assemblyName.Name).dll"
        if (Test-Path $fallbackPath) {
            return [System.Reflection.Assembly]::LoadFrom($fallbackPath)
        }
    }

    return $null
}
```

### assembly-loader.ps1
```powershell
function Initialize-AssemblyLoader {
    # Register assembly resolution handler for non-Core PowerShell
    if ($PSVersionTable.PSEdition -ne 'Core') {
        [System.AppDomain]::CurrentDomain.add_AssemblyResolve($script:onAssemblyResolveEventHandler)
    }

    # Pre-load critical assemblies in correct order
    $platformInfo = Get-PlatformInfo
    foreach ($assemblyName in $script:AssemblyLoadOrder) {
        try {
            $assemblyPath = Get-AssemblyPath -AssemblyName $assemblyName `
                                           -Platform $platformInfo.Platform `
                                           -Architecture $platformInfo.Architecture

            if (Test-Path $assemblyPath) {
                [System.Reflection.Assembly]::LoadFrom($assemblyPath) | Out-Null
            }
        } catch {
            Write-Warning "Failed to pre-load assembly $assemblyName : $_"
            if ($assemblyName -notlike 'Microsoft.Data.SqlClient') {
                throw
            }
        }
    }
}

function Test-AssemblyLoading {
    param (
        [string]$AssemblyName
    )

    try {
        $assembly = [System.Reflection.Assembly]::LoadFrom(
            (Get-AssemblyPath -AssemblyName $AssemblyName `
                             -Platform (Get-PlatformInfo).Platform `
                             -Architecture (Get-PlatformInfo).Architecture)
        )
        return $true
    } catch {
        Write-Warning "Failed to load $AssemblyName : $_"
        return $false
    }
}
```

## 3. Updated dbatools.library.psm1

```powershell
function Get-DbatoolsLibraryPath {
    [CmdletBinding()]
    param()
    $PSScriptRoot
}

$script:libraryroot = Get-DbatoolsLibraryPath

$components = @(
    'assembly-lists.ps1',
    'assembly-resolution.ps1',
    'assembly-loader.ps1'
)

# Load components
foreach ($component in $components) {
    $componentPath = Join-Path $PSScriptRoot "private\$component"
    . $componentPath
}

# Initialize assembly handling
try {
    Initialize-AssemblyLoader
} catch {
    throw "Failed to initialize assembly loader: $_"
}

# Clean up on module removal
$MyInvocation.MyCommand.ScriptBlock.Module.OnRemove = {
    if ($PSVersionTable.PSEdition -ne "Core") {
        [System.AppDomain]::CurrentDomain.remove_AssemblyResolve($onAssemblyResolveEventHandler)
    }
}

Export-ModuleMember -Function Get-DbatoolsLibraryPath
```

## Implementation Steps

1. Complete build.ps1 with the DAC and SqlClient file distribution
2. Create the private directory in the module root
3. Implement the three assembly handling scripts
4. Update the main module file
5. Test on multiple platforms:
   - Windows PowerShell 5.1 (x86/x64)
   - PowerShell 7.4+ on Windows/Linux/macOS
   - Test with different architectures

## Notes

- SqlClient has fallback to core version if platform-specific version fails
- DAC components are strictly platform-specific without fallback
- Assembly resolution is handled differently for Core vs Desktop PowerShell
- All paths are relative to module root for portability
- Native dependencies are managed per platform/architecture