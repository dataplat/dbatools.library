# Test for AvoidConflicts parameter functionality
# This test verifies that -AvoidConflicts properly skips loading assemblies that are already loaded

Write-Host "=== Testing AvoidConflicts Parameter ===" -ForegroundColor Cyan

$testsPassed = 0
$testsFailed = 0

function Test-AssemblyLoaded {
    param([string]$AssemblyName)
    $loaded = [System.AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq $AssemblyName }
    return [bool]$loaded
}

# Test 1: Module loads without -AvoidConflicts (default behavior)
Write-Host "`n--- Test 1: Default module loading ---" -ForegroundColor Yellow
try {
    Remove-Module dbatools.library -ErrorAction SilentlyContinue
    Import-Module ./dbatools.library.psd1 -Force

    if (Test-AssemblyLoaded -AssemblyName 'Microsoft.Data.SqlClient') {
        Write-Host "✓ PASS: Microsoft.Data.SqlClient loaded by default" -ForegroundColor Green
        $testsPassed++
    } else {
        Write-Host "✗ FAIL: Microsoft.Data.SqlClient not loaded" -ForegroundColor Red
        $testsFailed++
    }
} catch {
    Write-Host "✗ FAIL: Module import failed - $($_.Exception.Message)" -ForegroundColor Red
    $testsFailed++
}

# Test 2: Module loads with -AvoidConflicts when assembly is already present
Write-Host "`n--- Test 2: AvoidConflicts skips already-loaded assemblies ---" -ForegroundColor Yellow
try {
    Remove-Module dbatools.library -ErrorAction SilentlyContinue

    # First, load SqlClient manually to simulate it being loaded by another module
    $sqlClientPath = Join-Path (Get-DbatoolsLibraryPath) "lib\Microsoft.Data.SqlClient.dll"
    if (Test-Path $sqlClientPath) {
        Import-Module $sqlClientPath
        Write-Host "Pre-loaded Microsoft.Data.SqlClient" -ForegroundColor Gray
    }

    # Now import with AvoidConflicts and capture verbose output
    $verboseOutput = Import-Module ./dbatools.library.psd1 -ArgumentList $true -Verbose 4>&1

    if ($verboseOutput -match "Skipping.*already loaded") {
        Write-Host "✓ PASS: Verbose output shows assemblies were skipped" -ForegroundColor Green
        $testsPassed++
    } else {
        Write-Host "✗ FAIL: No verbose output about skipping assemblies" -ForegroundColor Red
        $testsFailed++
    }
} catch {
    Write-Host "✗ FAIL: Import with AvoidConflicts failed - $($_.Exception.Message)" -ForegroundColor Red
    $testsFailed++
}

# Test 3: Module still functions after using AvoidConflicts
Write-Host "`n--- Test 3: Module remains functional with AvoidConflicts ---" -ForegroundColor Yellow
try {
    Remove-Module dbatools.library -ErrorAction SilentlyContinue
    Import-Module ./dbatools.library.psd1 -ArgumentList $true -Force

    # Verify SMO types are available
    if ([Microsoft.SqlServer.Management.Smo.Server] -as [type]) {
        Write-Host "✓ PASS: SMO types available after AvoidConflicts" -ForegroundColor Green
        $testsPassed++
    } else {
        Write-Host "✗ FAIL: SMO types not available" -ForegroundColor Red
        $testsFailed++
    }
} catch {
    Write-Host "✗ FAIL: Module not functional - $($_.Exception.Message)" -ForegroundColor Red
    $testsFailed++
}

# Test 4: AvoidConflicts has zero impact when no assemblies are pre-loaded
Write-Host "`n--- Test 4: AvoidConflicts with clean session ---" -ForegroundColor Yellow
try {
    Remove-Module dbatools.library -ErrorAction SilentlyContinue

    # Get loaded assembly count before
    $beforeCount = ([System.AppDomain]::CurrentDomain.GetAssemblies()).Count

    Import-Module ./dbatools.library.psd1 -ArgumentList $true -Force

    # Get loaded assembly count after
    $afterCount = ([System.AppDomain]::CurrentDomain.GetAssemblies()).Count

    if ($afterCount -gt $beforeCount) {
        Write-Host "✓ PASS: Assemblies loaded with AvoidConflicts in clean session ($($afterCount - $beforeCount) new assemblies)" -ForegroundColor Green
        $testsPassed++
    } else {
        Write-Host "✗ FAIL: No assemblies loaded" -ForegroundColor Red
        $testsFailed++
    }
} catch {
    Write-Host "✗ FAIL: Test failed - $($_.Exception.Message)" -ForegroundColor Red
    $testsFailed++
}

# Summary
Write-Host "`n=== Test Summary ===" -ForegroundColor Cyan
Write-Host "Passed: $testsPassed" -ForegroundColor Green
Write-Host "Failed: $testsFailed" -ForegroundColor Red

if ($testsFailed -gt 0) {
    exit 1
} else {
    exit 0
}
