<#
.SYNOPSIS
    Verifies that all required DLLs are in the expected locations after a build.

.DESCRIPTION
    This script checks that all critical DLLs are in the correct locations in the dbatools.library
    directory structure. It's designed to help diagnose SNI loading issues and other assembly
    loading problems.

.PARAMETER Root
    The root directory of the dbatools.library module. If not specified, the script will use the
    parent directory of the script's location.

.EXAMPLE
    .\Verify-DllLocations.ps1

.NOTES
    Author: dbatools team
    Date: July 2025
#>

[CmdletBinding()]
param (
    [string]$Root = (Split-Path -Path (Split-Path -Path $PSScriptRoot -Parent))
)

# If we're in the build directory, move up one level
if ((Split-Path -Leaf $PSScriptRoot) -eq "build") {
    Write-Host "Running from build directory, using parent as root" -ForegroundColor Yellow
}

Write-Host "Verifying DLL locations in: $Root" -ForegroundColor Cyan

# Define the expected DLL locations
$expectedDlls = @(
    # Core runtime DLLs
    @{
        Path = "core/lib/dbatools.dll"
        Description = ".NET Core dbatools assembly"
        Critical = $true
    },
    @{
        Path = "core/lib/Microsoft.Data.SqlClient.dll"
        Description = ".NET Core SqlClient"
        Critical = $true
    },
    @{
        Path = "core/lib/runtimes/win-x64/native/Microsoft.Data.SqlClient.SNI.dll"
        Description = ".NET Core SNI DLL (Windows x64)"
        Critical = $true
    },
    @{
        Path = "core/lib/runtimes/win-x86/native/Microsoft.Data.SqlClient.SNI.dll"
        Description = ".NET Core SNI DLL (Windows x86)"
        Critical = $false
    },
    @{
        Path = "core/lib/runtimes/win-arm64/native/Microsoft.Data.SqlClient.SNI.dll"
        Description = ".NET Core SNI DLL (Windows ARM64)"
        Critical = $false
    },

    # Desktop runtime DLLs
    @{
        Path = "desktop/lib/dbatools.dll"
        Description = ".NET Framework dbatools assembly"
        Critical = $true
    },
    @{
        Path = "desktop/lib/Microsoft.Data.SqlClient.dll"
        Description = ".NET Framework SqlClient"
        Critical = $true
    },
    @{
        Path = "desktop/lib/runtimes/win-x64/native/Microsoft.Data.SqlClient.SNI.dll"
        Description = ".NET Framework SNI DLL (Windows x64)"
        Critical = $true
    },

    # Third-party libraries
    @{
        Path = "desktop/third-party/bogus/Bogus.dll"
        Description = "Bogus library for .NET Framework"
        Critical = $false
    },
    @{
        Path = "core/third-party/bogus/Bogus.dll"
        Description = "Bogus library for .NET Core"
        Critical = $false
    },
    @{
        Path = "desktop/third-party/LumenWorks/LumenWorks.Framework.IO.dll"
        Description = "LumenWorks CSV Reader for .NET Framework"
        Critical = $false
    },
    @{
        Path = "core/third-party/LumenWorks/LumenWorks.Framework.IO.dll"
        Description = "LumenWorks CSV Reader for .NET Core"
        Critical = $false
    },

    # DAC components
    @{
        Path = "core/lib/dac/windows/SqlPackage.exe"
        Description = "SqlPackage for Windows"
        Critical = $false
    },
    @{
        Path = "core/lib/dac/linux/SqlPackage"
        Description = "SqlPackage for Linux"
        Critical = $false
    },
    @{
        Path = "core/lib/dac/mac/SqlPackage"
        Description = "SqlPackage for macOS"
        Critical = $false
    }
)

# Check each expected DLL
$missingCritical = @()
$missingOptional = @()
$found = @()

foreach ($dll in $expectedDlls) {
    $fullPath = Join-Path -Path $Root -ChildPath $dll.Path
    if (Test-Path $fullPath) {
        $found += $dll
        Write-Host "[OK] Found: $($dll.Description)" -ForegroundColor Green
    } else {
        if ($dll.Critical) {
            $missingCritical += $dll
            Write-Host "[ERROR] Missing critical file: $($dll.Description) at $($dll.Path)" -ForegroundColor Red
        } else {
            $missingOptional += $dll
            Write-Host "[WARNING] Missing optional file: $($dll.Description) at $($dll.Path)" -ForegroundColor Yellow
        }
    }
}

# Check if runtimes folder structure exists
$runtimesPath = Join-Path -Path $Root -ChildPath "core/lib/runtimes"
if (Test-Path $runtimesPath) {
    Write-Host "[OK] Runtimes folder structure exists" -ForegroundColor Green
} else {
    Write-Host "[ERROR] Runtimes folder structure missing!" -ForegroundColor Red
    $missingCritical += @{Path = "core/lib/runtimes"; Description = "Runtimes folder structure"}
}

# Summary
Write-Host "`n=== Verification Summary ===" -ForegroundColor Cyan
Write-Host "Found: $($found.Count) files" -ForegroundColor Green
Write-Host "Missing critical: $($missingCritical.Count) files" -ForegroundColor $(if ($missingCritical.Count -gt 0) { "Red" } else { "Green" })
Write-Host "Missing optional: $($missingOptional.Count) files" -ForegroundColor $(if ($missingOptional.Count -gt 0) { "Yellow" } else { "Green" })

# Return result
if ($missingCritical.Count -gt 0) {
    Write-Host "`nVerification failed! Missing critical files." -ForegroundColor Red
    return $false
} else {
    Write-Host "`nVerification successful! All critical files are present." -ForegroundColor Green
    return $true
}