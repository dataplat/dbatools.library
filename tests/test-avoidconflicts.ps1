# Test the AvoidConflicts parameter
Write-Host "=== Testing AvoidConflicts Parameter ===" -ForegroundColor Cyan

$testsPassed = 0
$testsFailed = 0

# Test 1: Import dbatools.library normally (without AvoidConflicts)
Write-Host "`n--- Test 1: Default import without AvoidConflicts ---" -ForegroundColor Yellow
try {
    Remove-Module dbatools.library -ErrorAction SilentlyContinue
    Import-Module ./artifacts/dbatools.library/dbatools.library.psd1 -Force

    $loaded = [System.AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq 'Microsoft.Data.SqlClient' }
    if ($loaded) {
        Write-Host "[PASS] Microsoft.Data.SqlClient loaded by default" -ForegroundColor Green
        $testsPassed++
    } else {
        Write-Host "[FAIL] Microsoft.Data.SqlClient not loaded" -ForegroundColor Red
        $testsFailed++
    }
} catch {
    Write-Host "[FAIL] Exception: $_" -ForegroundColor Red
    $testsFailed++
}

# Test 2: Import SqlServer module first, then dbatools.library with AvoidConflicts
Write-Host "`n--- Test 2: Import with AvoidConflicts after SqlServer module ---" -ForegroundColor Yellow
try {
    Remove-Module dbatools.library -ErrorAction SilentlyContinue
    Remove-Module SqlServer -ErrorAction SilentlyContinue

    # Import SqlServer module first
    Write-Host "Importing SqlServer module..." -ForegroundColor Gray
    Import-Module SqlServer -ErrorAction Stop

    # Get the SqlClient version loaded by SqlServer
    $sqlServerClient = [System.AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq 'Microsoft.Data.SqlClient' }
    if ($sqlServerClient) {
        Write-Host "SqlServer module loaded Microsoft.Data.SqlClient version: $($sqlServerClient.GetName().Version)" -ForegroundColor Gray
    }

    # Import dbatools.library with AvoidConflicts and capture verbose output
    Write-Host "Importing dbatools.library with -AvoidConflicts..." -ForegroundColor Gray
    $verboseOutput = Import-Module ./artifacts/dbatools.library/dbatools.library.psd1 -ArgumentList $true -Verbose 4>&1 | Out-String

    if ($verboseOutput -match "Skipping.*already loaded") {
        Write-Host "[PASS] Verbose output shows assemblies were skipped" -ForegroundColor Green
        Write-Host "  Verbose message: $($verboseOutput -split "`n" | Where-Object { $_ -match 'Skipping' } | Select-Object -First 1)" -ForegroundColor Gray
        $testsPassed++
    } else {
        Write-Host "[FAIL] No verbose output about skipping assemblies" -ForegroundColor Red
        Write-Host "  Verbose output was: $verboseOutput" -ForegroundColor Gray
        $testsFailed++
    }
} catch {
    Write-Host "[FAIL] Exception: $_" -ForegroundColor Red
    $testsFailed++
}

# Test 3: Verify module still functions with AvoidConflicts
Write-Host "`n--- Test 3: Module functional after AvoidConflicts ---" -ForegroundColor Yellow
try {
    if ([Microsoft.SqlServer.Management.Smo.Server] -as [type]) {
        Write-Host "[PASS] SMO types available after AvoidConflicts" -ForegroundColor Green
        $testsPassed++
    } else {
        Write-Host "[FAIL] SMO types not available" -ForegroundColor Red
        $testsFailed++
    }
} catch {
    Write-Host "[FAIL] Exception: $_" -ForegroundColor Red
    $testsFailed++
}

# Test 4: Verify no conflicts when importing both modules
Write-Host "`n--- Test 4: No conflicts with both modules loaded ---" -ForegroundColor Yellow
try {
    # Try using both modules
    $smoType = [Microsoft.SqlServer.Management.Smo.Server]
    $sqlClientType = [Microsoft.Data.SqlClient.SqlConnection]

    Write-Host "[PASS] Both modules can coexist without conflicts" -ForegroundColor Green
    Write-Host "  SMO type available: $($smoType.FullName)" -ForegroundColor Gray
    Write-Host "  SqlClient type available: $($sqlClientType.FullName)" -ForegroundColor Gray
    $testsPassed++
} catch {
    Write-Host "[FAIL] Exception: $_" -ForegroundColor Red
    $testsFailed++
}

# Summary
Write-Host "`n========================================" -ForegroundColor White
Write-Host "       TEST SUMMARY" -ForegroundColor White
Write-Host "========================================" -ForegroundColor White
Write-Host "Passed: $testsPassed" -ForegroundColor Green
Write-Host "Failed: $testsFailed" -ForegroundColor $(if ($testsFailed -gt 0) { 'Red' } else { 'Green' })
Write-Host "Total:  $($testsPassed + $testsFailed)" -ForegroundColor White

if ($testsFailed -eq 0) {
    Write-Host "`nALL AVOIDCONFLICTS TESTS PASSED!" -ForegroundColor Green
    exit 0
} else {
    Write-Host "`nSOME TESTS FAILED - REVIEW BEFORE MERGE" -ForegroundColor Red
    exit 1
}
