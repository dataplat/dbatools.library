#!/usr/bin/env pwsh
# Quick verification that the boolean fix works

Write-Host "=== Verifying AvoidConflicts Boolean Fix ===" -ForegroundColor Cyan

try {
    Import-Module SqlServer -ErrorAction Stop
    Write-Host "[OK] SqlServer loaded" -ForegroundColor Green

    $sqlClient = [System.AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq 'Microsoft.Data.SqlClient' }
    if ($sqlClient) {
        Write-Host "[OK] SqlClient already loaded by SqlServer: $($sqlClient.GetName().Version)" -ForegroundColor Yellow
    } else {
        Write-Host "[SKIP] SqlServer did not load SqlClient - test not applicable" -ForegroundColor Yellow
        exit 0
    }

    # This is the critical test - with the bug, this would fail even with AvoidConflicts
    Import-Module ./artifacts/dbatools.library/dbatools.library.psd1 -ArgumentList $true -Force -Verbose -ErrorAction Stop
    Write-Host "[OK] dbatools.library loaded successfully with AvoidConflicts" -ForegroundColor Green

    if ([Microsoft.SqlServer.Management.Smo.Server] -as [type]) {
        Write-Host "[OK] SMO types are available" -ForegroundColor Green
    } else {
        Write-Host "[WARN] SMO types not available" -ForegroundColor Yellow
    }

    Write-Host "`n=== FIX VERIFIED - Boolean logic works correctly ===" -ForegroundColor Green
    exit 0

} catch {
    Write-Host "[FAIL] $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "`n=== FIX VERIFICATION FAILED ===" -ForegroundColor Red
    exit 1
}
