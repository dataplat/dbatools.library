#!/usr/bin/env pwsh
# Test the AvoidConflicts parameter for dbatools.library
#
# IMPORTANT: Test order matters due to system-level assembly caching.
# Tests that check for conflicts must run BEFORE tests that load dbatools.library
# successfully, otherwise the cached assemblies may prevent conflicts from occurring.

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  AvoidConflicts Parameter Test Suite  " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$testsPassed = 0
$testsFailed = 0

# Get the module path (relative to this script)
$modulePath = Join-Path $PSScriptRoot "..\artifacts\dbatools.library\dbatools.library.psd1"
if (-not (Test-Path $modulePath)) {
    Write-Host "[ERROR] Module not found at: $modulePath" -ForegroundColor Red
    Write-Host "Please build the module first using: dotnet build" -ForegroundColor Yellow
    exit 1
}

# ============================================================================
# CONFLICT TESTS FIRST - These must run before any successful dbatools.library load
# ============================================================================

# Test 1: SqlServer first, then dbatools.library WITHOUT AvoidConflicts (expect failure)
Write-Host "`n--- Test 1: SqlServer first, WITHOUT AvoidConflicts ---" -ForegroundColor Yellow
Write-Host "Expected: FAIL - Original behavior throws on conflict" -ForegroundColor Gray
Write-Host "(This test MUST run first due to system-level assembly caching)" -ForegroundColor DarkGray

$result1 = pwsh -NoProfile -Command {
    param($modulePath)
    try {
        Import-Module SqlServer -ErrorAction Stop
        $sqlclient = [System.AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq 'Microsoft.Data.SqlClient' }
        if (-not $sqlclient) {
            Write-Output "SKIP: SqlServer did not load SqlClient"
            return
        }
        Import-Module $modulePath -Force -ErrorAction Stop
        Write-Output "UNEXPECTED_PASS"
    } catch {
        if ($_.Exception.Message -match "Assembly with same name is already loaded|already loaded|SqlClient") {
            Write-Output "EXPECTED_FAIL"
        } else {
            Write-Output "FAIL: Unexpected error: $($_.Exception.Message)"
        }
    }
} -args $modulePath

Write-Host "  Result: $result1" -ForegroundColor Gray

if ($result1 -eq "EXPECTED_FAIL") {
    Write-Host "[PASS] Correctly fails when SqlServer loads conflicting DLLs (original behavior)" -ForegroundColor Green
    $testsPassed++
} elseif ($result1 -eq "UNEXPECTED_PASS") {
    Write-Host "[FAIL] Module loaded without error (conflict should have occurred)" -ForegroundColor Red
    Write-Host "       Note: This may happen if dbatools.library was previously loaded in this session" -ForegroundColor DarkGray
    $testsFailed++
} elseif ($result1 -match "^SKIP") {
    Write-Host "[SKIP] $result1" -ForegroundColor Yellow
} else {
    Write-Host "[FAIL] $result1" -ForegroundColor Red
    $testsFailed++
}

# Test 2: Wrong ArgumentList syntax (hashtable) - should fail
Write-Host "`n--- Test 2: Wrong ArgumentList syntax (hashtable) ---" -ForegroundColor Yellow
Write-Host "Expected: Error about converting hashtable to SwitchParameter" -ForegroundColor Gray

$result2 = pwsh -NoProfile -Command {
    param($modulePath)
    try {
        Import-Module $modulePath -ArgumentList @{AvoidConflicts=$true} -Force -ErrorAction Stop
        Write-Output "UNEXPECTED_PASS"
    } catch {
        if ($_.Exception.Message -match "Cannot convert.*Hashtable.*SwitchParameter|Boolean parameters") {
            Write-Output "EXPECTED_FAIL"
        } else {
            Write-Output "FAIL: Unexpected error: $($_.Exception.Message)"
        }
    }
} -args $modulePath

if ($result2 -eq "EXPECTED_FAIL") {
    Write-Host "[PASS] Hashtable syntax correctly rejected with helpful error" -ForegroundColor Green
    $testsPassed++
} elseif ($result2 -eq "UNEXPECTED_PASS") {
    Write-Host "[FAIL] Hashtable syntax unexpectedly worked" -ForegroundColor Red
    $testsFailed++
} else {
    Write-Host "[FAIL] $result2" -ForegroundColor Red
    $testsFailed++
}

# ============================================================================
# SUCCESS TESTS - These can run after conflict tests
# ============================================================================

# Test 3: SqlServer first, then dbatools.library WITH AvoidConflicts (expect success)
Write-Host "`n--- Test 3: SqlServer first, WITH AvoidConflicts ---" -ForegroundColor Yellow
Write-Host "Expected: SUCCESS - AvoidConflicts skips conflicting assemblies" -ForegroundColor Gray

$result3 = pwsh -NoProfile -Command {
    param($modulePath)
    try {
        Import-Module SqlServer -ErrorAction Stop

        # Capture verbose output
        $verboseMessages = @()
        Import-Module $modulePath -ArgumentList $true -Force -Verbose -ErrorAction Stop 4>&1 | ForEach-Object {
            if ($_ -is [System.Management.Automation.VerboseRecord]) {
                $verboseMessages += $_.Message
            }
        }

        # Check if SMO is available
        if ([Microsoft.SqlServer.Management.Smo.Server] -as [type]) {
            $skipped = $verboseMessages | Where-Object { $_ -match "Skipping.*already loaded" }
            if ($skipped) {
                Write-Output "PASS: Module loaded, assemblies skipped"
            } else {
                Write-Output "PASS: Module loaded successfully"
            }
        } else {
            Write-Output "FAIL: SMO types not available"
        }
    } catch {
        Write-Output "FAIL: $($_.Exception.Message)"
    }
} -args $modulePath

if ($result3 -match "^PASS") {
    Write-Host "[PASS] Module loads with AvoidConflicts after SqlServer" -ForegroundColor Green
    $testsPassed++
} else {
    Write-Host "[FAIL] $result3" -ForegroundColor Red
    $testsFailed++
}

# Test 4: Verify verbose output shows skipped assemblies
Write-Host "`n--- Test 4: Verbose output shows skipped assemblies ---" -ForegroundColor Yellow
Write-Host "Expected: Verbose messages indicate SqlClient was skipped" -ForegroundColor Gray

$result4 = pwsh -NoProfile -Command {
    param($modulePath)
    try {
        Import-Module SqlServer -ErrorAction Stop

        $verboseOutput = ""
        Import-Module $modulePath -ArgumentList $true -Force -Verbose 4>&1 | ForEach-Object {
            if ($_ -is [System.Management.Automation.VerboseRecord]) {
                $verboseOutput += $_.Message + "`n"
            }
        }

        if ($verboseOutput -match "Skipping Microsoft\.Data\.SqlClient\.dll.*already loaded") {
            Write-Output "PASS: SqlClient skip message found"
        } else {
            Write-Output "INFO: SqlClient skip message not found (module may use compatible versions)"
        }
    } catch {
        Write-Output "FAIL: $($_.Exception.Message)"
    }
} -args $modulePath

if ($result4 -match "^PASS") {
    Write-Host "[PASS] Verbose output confirms SqlClient was skipped" -ForegroundColor Green
    $testsPassed++
} elseif ($result4 -match "^INFO") {
    Write-Host "[INFO] $result4" -ForegroundColor Yellow
    # Informational - don't count as pass or fail
} else {
    Write-Host "[FAIL] $result4" -ForegroundColor Red
    $testsFailed++
}

# Test 5: Default import without SqlServer (expect success)
Write-Host "`n--- Test 5: Default import without SqlServer ---" -ForegroundColor Yellow
Write-Host "Expected: SUCCESS - Module loads normally" -ForegroundColor Gray

$result5 = pwsh -NoProfile -Command {
    param($modulePath)
    try {
        Import-Module $modulePath -Force -ErrorAction Stop
        $loaded = [System.AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq 'Microsoft.Data.SqlClient' }
        if ($loaded) {
            if ([Microsoft.SqlServer.Management.Smo.Server] -as [type]) {
                Write-Output "PASS"
            } else {
                Write-Output "FAIL: SMO types not available"
            }
        } else {
            Write-Output "FAIL: Microsoft.Data.SqlClient not loaded"
        }
    } catch {
        Write-Output "FAIL: $($_.Exception.Message)"
    }
} -args $modulePath

if ($result5 -eq "PASS") {
    Write-Host "[PASS] dbatools.library loads successfully without SqlServer" -ForegroundColor Green
    $testsPassed++
} else {
    Write-Host "[FAIL] $result5" -ForegroundColor Red
    $testsFailed++
}

# Summary
Write-Host "`n========================================" -ForegroundColor White
Write-Host "         TEST SUMMARY                   " -ForegroundColor White
Write-Host "========================================" -ForegroundColor White
Write-Host "Passed: $testsPassed" -ForegroundColor Green
Write-Host "Failed: $testsFailed" -ForegroundColor $(if ($testsFailed -gt 0) { 'Red' } else { 'Green' })
Write-Host "Total:  $($testsPassed + $testsFailed)" -ForegroundColor White
Write-Host ""

if ($testsFailed -eq 0) {
    Write-Host "ALL TESTS PASSED!" -ForegroundColor Green
    exit 0
} else {
    Write-Host "SOME TESTS FAILED" -ForegroundColor Red
    exit 1
}
