<#
.SYNOPSIS
    Tests the dbatools.library build with both PowerShell 5.1 and PowerShell 7+.

.DESCRIPTION
    This script runs a series of tests to verify that the dbatools.library build works correctly
    with both PowerShell 5.1 and PowerShell 7+. It checks that the module can be imported and
    that critical assemblies can be loaded.

.PARAMETER ModulePath
    The path to the dbatools.library module. If not specified, the script will use the parent
    directory of the script's location.

.EXAMPLE
    .\Test-Build.ps1

.NOTES
    Author: dbatools team
    Date: July 2025
#>

[CmdletBinding()]
param (
    [string]$ModulePath = (Split-Path -Path (Split-Path -Path $PSScriptRoot -Parent))
)

function Test-PowerShellVersion {
    [CmdletBinding()]
    param()

    $psVersion = $PSVersionTable.PSVersion
    $currentEdition = $PSVersionTable.PSEdition

    Write-Host "Testing with PowerShell $psVersion ($currentEdition edition)" -ForegroundColor Cyan

    # Verify module can be imported
    try {
        Import-Module $ModulePath -Force -ErrorAction Stop
        Write-Host "[OK] Module imported successfully" -ForegroundColor Green
    }
    catch {
        Write-Host "[ERROR] Failed to import module: $_" -ForegroundColor Red
        return $false
    }

    # Verify Get-DbatoolsLibraryPath works
    try {
        $libraryPath = Get-DbatoolsLibraryPath
        Write-Host "[OK] Get-DbatoolsLibraryPath returned: $libraryPath" -ForegroundColor Green
    }
    catch {
        Write-Host "[ERROR] Get-DbatoolsLibraryPath failed: $_" -ForegroundColor Red
        return $false
    }

    # Verify critical assemblies can be loaded
    try {
        $assemblies = Get-DbatoolsLoadedAssembly
        Write-Host "[OK] Found $($assemblies.Count) loaded assemblies" -ForegroundColor Green

        # Check for critical assemblies
        $criticalAssemblies = @(
            'Microsoft.Data.SqlClient',
            'dbatools'
        )

        foreach ($name in $criticalAssemblies) {
            $assembly = $assemblies | Where-Object { $_.Name -eq $name }
            if ($assembly) {
                Write-Host "[OK] Found $name assembly (version $($assembly.Version))" -ForegroundColor Green
            }
            else {
                Write-Host "[ERROR] Critical assembly not loaded: $name" -ForegroundColor Red
                return $false
            }
        }
    }
    catch {
        Write-Host "[ERROR] Failed to check loaded assemblies: $_" -ForegroundColor Red
        return $false
    }

    # Verify SNI DLL is accessible
    try {
        if ($currentEdition -eq 'Core') {
            $sniPath = Join-Path $libraryPath "core/lib/runtimes/win-x64/native/Microsoft.Data.SqlClient.SNI.dll"
        }
        else {
            $sniPath = Join-Path $libraryPath "desktop/lib/runtimes/win-x64/native/Microsoft.Data.SqlClient.SNI.dll"
        }

        if (Test-Path $sniPath) {
            Write-Host "[OK] SNI DLL found at: $sniPath" -ForegroundColor Green
        }
        else {
            Write-Host "[ERROR] SNI DLL not found at: $sniPath" -ForegroundColor Red
            return $false
        }
    }
    catch {
        Write-Host "[ERROR] Failed to check SNI DLL: $_" -ForegroundColor Red
        return $false
    }

    # All tests passed
    return $true
}

# Main test logic
$currentPSVersion = $PSVersionTable.PSVersion
$currentPSEdition = $PSVersionTable.PSEdition

Write-Host "=== Testing dbatools.library Build ===" -ForegroundColor Cyan
Write-Host "Module path: $ModulePath" -ForegroundColor Cyan
Write-Host "Current PowerShell: $currentPSVersion ($currentPSEdition edition)" -ForegroundColor Cyan

# Run tests with current PowerShell version
$currentResult = Test-PowerShellVersion
Write-Host "`nTest result with current PowerShell: $($currentResult ? 'PASSED' : 'FAILED')" -ForegroundColor ($currentResult ? 'Green' : 'Red')

# If running in PowerShell 5.1, suggest testing with PowerShell 7+
if ($currentPSEdition -ne 'Core') {
    Write-Host "`nTo test with PowerShell 7+, run:" -ForegroundColor Yellow
    Write-Host "pwsh -Command ""& '$PSCommandPath' -ModulePath '$ModulePath'""" -ForegroundColor Yellow
}

# If running in PowerShell 7+, suggest testing with PowerShell 5.1
if ($currentPSEdition -eq 'Core') {
    Write-Host "`nTo test with PowerShell 5.1, run:" -ForegroundColor Yellow
    Write-Host "powershell -Command ""& '$PSCommandPath' -ModulePath '$ModulePath'""" -ForegroundColor Yellow
}

# Return overall result
return $currentResult