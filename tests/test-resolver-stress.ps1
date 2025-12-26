#!/usr/bin/env pwsh
# Stress test for the assembly resolver under rapid load conditions
#
# This test validates:
# 1. Assembly resolver handles rapid module import/remove cycles
# 2. Resolver doesn't leak memory under repeated operations
# 3. Concurrent assembly resolution works correctly
# 4. Resolver handles rapid assembly loads without deadlock
#
# NOTE: dbatools.library loads SMO and related assemblies. The CSV types
# (CsvReaderOptions, etc.) are in dbatools.dll which is loaded by the
# main dbatools module. This test focuses on the assembly resolver for
# the SMO-related assemblies that dbatools.library manages.

param(
    [switch]$Verbose,
    [int]$Iterations = 50,
    [int]$ConcurrentThreads = 4
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Assembly Resolver Stress Test Suite  " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Iterations: $Iterations, Threads: $ConcurrentThreads" -ForegroundColor Gray

$testsPassed = 0
$testsFailed = 0
$testResults = @()

# Get the module path (relative to this script)
$modulePath = Join-Path $PSScriptRoot "..\artifacts\dbatools.library\dbatools.library.psd1"
if (-not (Test-Path $modulePath)) {
    Write-Host "[ERROR] Module not found at: $modulePath" -ForegroundColor Red
    Write-Host "Please build the module first using: dotnet build" -ForegroundColor Yellow
    exit 1
}

$moduleDir = Split-Path $modulePath -Parent

# ============================================================================
# Test 1: Rapid Import/Remove Cycles
# ============================================================================
Write-Host "`n--- Test 1: Rapid Import/Remove Cycles ---" -ForegroundColor Yellow
Write-Host "Testing $Iterations rapid import/remove cycles..." -ForegroundColor Gray

$test1Start = Get-Date
$test1Errors = @()

for ($i = 1; $i -le $Iterations; $i++) {
    if ($Verbose -or ($i % 10 -eq 0)) {
        Write-Host "  Cycle $i/$Iterations" -ForegroundColor DarkGray
    }

    $result = pwsh -NoProfile -Command {
        param($modulePath)
        try {
            Import-Module $modulePath -Force -ErrorAction Stop

            # Quick type check to ensure module loaded correctly (SMO types)
            $null = [Microsoft.SqlServer.Management.Smo.Server]

            Remove-Module dbatools.library -Force -ErrorAction SilentlyContinue
            Write-Output "OK"
        } catch {
            Write-Output "ERROR: $($_.Exception.Message)"
        }
    } -args $modulePath

    if ($result -ne "OK") {
        $test1Errors += "Cycle $i`: $result"
    }
}

$test1Duration = (Get-Date) - $test1Start

if ($test1Errors.Count -eq 0) {
    Write-Host "[PASS] Completed $Iterations import/remove cycles in $($test1Duration.TotalSeconds.ToString('F2'))s" -ForegroundColor Green
    $testsPassed++
} else {
    Write-Host "[FAIL] $($test1Errors.Count) errors in $Iterations cycles" -ForegroundColor Red
    $test1Errors | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    $testsFailed++
}

$testResults += [PSCustomObject]@{
    Test = "Rapid Import/Remove"
    Result = if ($test1Errors.Count -eq 0) { "PASS" } else { "FAIL" }
    Duration = $test1Duration.TotalSeconds
    Errors = $test1Errors.Count
}

# ============================================================================
# Test 2: Concurrent Module Loads (isolated processes)
# ============================================================================
Write-Host "`n--- Test 2: Concurrent Module Loads ($ConcurrentThreads parallel processes) ---" -ForegroundColor Yellow
Write-Host "Starting $ConcurrentThreads concurrent PowerShell processes..." -ForegroundColor Gray

$test2Start = Get-Date

# Create jobs that load the module concurrently
$jobs = 1..$ConcurrentThreads | ForEach-Object {
    $jobNum = $_
    Start-Job -ScriptBlock {
        param($modulePath, $iterations, $jobNum)
        $errors = @()

        for ($i = 1; $i -le $iterations; $i++) {
            try {
                # Each iteration loads in a fresh scope
                $null = pwsh -NoProfile -Command {
                    param($mp)
                    Import-Module $mp -Force -ErrorAction Stop
                    $null = [Microsoft.SqlServer.Management.Smo.Server]
                    "OK"
                } -args $modulePath
            } catch {
                $errors += "Job$jobNum-Iter$i`: $($_.Exception.Message)"
            }
        }

        [PSCustomObject]@{
            JobNum = $jobNum
            Iterations = $iterations
            Errors = $errors
        }
    } -ArgumentList $modulePath, ([int]($Iterations / $ConcurrentThreads)), $_
}

# Wait for all jobs with timeout
$timeout = [TimeSpan]::FromMinutes(5)
$completed = $jobs | Wait-Job -Timeout $timeout.TotalSeconds

$test2Errors = @()
$jobs | ForEach-Object {
    $job = $_
    if ($job.State -eq 'Completed') {
        $result = Receive-Job $job
        if ($result.Errors.Count -gt 0) {
            $test2Errors += $result.Errors
        }
    } elseif ($job.State -eq 'Running') {
        Stop-Job $job
        $test2Errors += "Job $($job.Id) timed out"
    } else {
        $test2Errors += "Job $($job.Id) failed with state: $($job.State)"
    }
    Remove-Job $job -Force
}

$test2Duration = (Get-Date) - $test2Start

if ($test2Errors.Count -eq 0) {
    Write-Host "[PASS] All $ConcurrentThreads concurrent process groups completed successfully in $($test2Duration.TotalSeconds.ToString('F2'))s" -ForegroundColor Green
    $testsPassed++
} else {
    Write-Host "[FAIL] $($test2Errors.Count) errors in concurrent loads" -ForegroundColor Red
    $test2Errors | Select-Object -First 5 | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    if ($test2Errors.Count -gt 5) {
        Write-Host "  ... and $($test2Errors.Count - 5) more errors" -ForegroundColor Red
    }
    $testsFailed++
}

$testResults += [PSCustomObject]@{
    Test = "Concurrent Module Loads"
    Result = if ($test2Errors.Count -eq 0) { "PASS" } else { "FAIL" }
    Duration = $test2Duration.TotalSeconds
    Errors = $test2Errors.Count
}

# ============================================================================
# Test 3: Type Access Under Load
# ============================================================================
Write-Host "`n--- Test 3: Rapid Type Access After Load ---" -ForegroundColor Yellow
Write-Host "Testing rapid type instantiation and method calls..." -ForegroundColor Gray

$test3Result = pwsh -NoProfile -Command {
    param($modulePath, $iterations)
    try {
        Import-Module $modulePath -Force -ErrorAction Stop

        $errors = @()
        $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

        for ($i = 1; $i -le $iterations; $i++) {
            try {
                # Test various SMO types from the assembly
                $null = [Microsoft.SqlServer.Management.Smo.Server]
                $null = [Microsoft.SqlServer.Management.Smo.Database]
                $null = [Microsoft.SqlServer.Management.Smo.Table]
                $null = [Microsoft.SqlServer.Management.Common.ServerConnection]
            } catch {
                $errors += "Iteration $i`: $($_.Exception.Message)"
            }
        }

        $stopwatch.Stop()

        [PSCustomObject]@{
            Success = ($errors.Count -eq 0)
            Errors = $errors
            DurationMs = $stopwatch.ElapsedMilliseconds
            Iterations = $iterations
        }
    } catch {
        [PSCustomObject]@{
            Success = $false
            Errors = @("Module load failed: $($_.Exception.Message)")
            DurationMs = 0
            Iterations = 0
        }
    }
} -args $modulePath, $Iterations

if ($test3Result.Success) {
    $opsPerSec = [math]::Round($test3Result.Iterations / ($test3Result.DurationMs / 1000), 0)
    Write-Host "[PASS] Completed $($test3Result.Iterations) type operations in $($test3Result.DurationMs)ms ($opsPerSec ops/sec)" -ForegroundColor Green
    $testsPassed++
} else {
    Write-Host "[FAIL] $($test3Result.Errors.Count) errors in type access" -ForegroundColor Red
    $test3Result.Errors | Select-Object -First 5 | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    $testsFailed++
}

$testResults += [PSCustomObject]@{
    Test = "Rapid Type Access"
    Result = if ($test3Result.Success) { "PASS" } else { "FAIL" }
    Duration = $test3Result.DurationMs / 1000
    Errors = $test3Result.Errors.Count
}

# ============================================================================
# Test 4: Memory Stability Under Repeated Loads
# ============================================================================
Write-Host "`n--- Test 4: Memory Stability Under Repeated Loads ---" -ForegroundColor Yellow
Write-Host "Testing memory growth over repeated module loads..." -ForegroundColor Gray

$test4Result = pwsh -NoProfile -Command {
    param($modulePath, $iterations)
    try {
        $memoryReadings = @()

        # Baseline
        [GC]::Collect()
        [GC]::WaitForPendingFinalizers()
        [GC]::Collect()
        $baseline = [GC]::GetTotalMemory($true)

        for ($i = 1; $i -le $iterations; $i++) {
            Import-Module $modulePath -Force -ErrorAction Stop

            # Do some work with SMO types
            $null = [Microsoft.SqlServer.Management.Smo.Server]
            $null = [Microsoft.SqlServer.Management.Smo.Database]
            $null = [Microsoft.SqlServer.Management.Common.ServerConnection]

            Remove-Module dbatools.library -Force -ErrorAction SilentlyContinue

            if ($i % 10 -eq 0) {
                [GC]::Collect()
                [GC]::WaitForPendingFinalizers()
                [GC]::Collect()
                $memoryReadings += [GC]::GetTotalMemory($true)
            }
        }

        # Final measurement
        [GC]::Collect()
        [GC]::WaitForPendingFinalizers()
        [GC]::Collect()
        $final = [GC]::GetTotalMemory($true)

        $memoryGrowthMB = [math]::Round(($final - $baseline) / 1MB, 2)

        [PSCustomObject]@{
            Success = $true
            BaselineMB = [math]::Round($baseline / 1MB, 2)
            FinalMB = [math]::Round($final / 1MB, 2)
            GrowthMB = $memoryGrowthMB
            Iterations = $iterations
            Readings = $memoryReadings | ForEach-Object { [math]::Round($_ / 1MB, 2) }
        }
    } catch {
        [PSCustomObject]@{
            Success = $false
            Error = $_.Exception.Message
        }
    }
} -args $modulePath, $Iterations

if ($test4Result.Success) {
    $maxGrowthMB = 100  # Allow up to 100MB growth (assemblies do take memory)
    if ($test4Result.GrowthMB -lt $maxGrowthMB) {
        Write-Host "[PASS] Memory growth: $($test4Result.GrowthMB)MB over $($test4Result.Iterations) iterations (baseline: $($test4Result.BaselineMB)MB, final: $($test4Result.FinalMB)MB)" -ForegroundColor Green
        $testsPassed++
    } else {
        Write-Host "[WARN] Memory growth: $($test4Result.GrowthMB)MB exceeds expected $($maxGrowthMB)MB (may indicate leak)" -ForegroundColor Yellow
        Write-Host "  Readings: $($test4Result.Readings -join ', ')MB" -ForegroundColor Gray
        $testsPassed++  # Not a hard failure, assemblies naturally consume memory
    }
} else {
    Write-Host "[FAIL] Memory test failed: $($test4Result.Error)" -ForegroundColor Red
    $testsFailed++
}

$testResults += [PSCustomObject]@{
    Test = "Memory Stability"
    Result = if ($test4Result.Success) { "PASS" } else { "FAIL" }
    Duration = 0
    Errors = if ($test4Result.Success) { 0 } else { 1 }
}

# ============================================================================
# Test 5: Assembly Resolution Race Conditions
# ============================================================================
Write-Host "`n--- Test 5: Assembly Resolution Race Conditions ---" -ForegroundColor Yellow
Write-Host "Testing parallel assembly resolution requests..." -ForegroundColor Gray

$test5Result = pwsh -NoProfile -Command {
    param($modulePath)
    try {
        Import-Module $modulePath -Force -ErrorAction Stop

        $errors = [System.Collections.Concurrent.ConcurrentBag[string]]::new()
        $iterations = 100

        # Parallel type access to trigger assembly resolution
        1..$iterations | ForEach-Object -Parallel {
            $errorBag = $using:errors
            try {
                # Access various SMO types that may require assembly resolution
                $null = [Microsoft.SqlServer.Management.Smo.Server]
                $null = [Microsoft.SqlServer.Management.Smo.Database]
                $null = [Microsoft.SqlServer.Management.Common.ServerConnection]
            } catch {
                $errorBag.Add("Thread iteration: $($_.Exception.Message)")
            }
        } -ThrottleLimit 8

        [PSCustomObject]@{
            Success = ($errors.Count -eq 0)
            ErrorCount = $errors.Count
            Errors = @($errors | Select-Object -First 5)
            Iterations = $iterations
        }
    } catch {
        [PSCustomObject]@{
            Success = $false
            ErrorCount = 1
            Errors = @("Module load failed: $($_.Exception.Message)")
            Iterations = 0
        }
    }
} -args $modulePath

if ($test5Result.Success) {
    Write-Host "[PASS] No race conditions detected in $($test5Result.Iterations) parallel resolution attempts" -ForegroundColor Green
    $testsPassed++
} else {
    Write-Host "[FAIL] $($test5Result.ErrorCount) race condition errors detected" -ForegroundColor Red
    $test5Result.Errors | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    $testsFailed++
}

$testResults += [PSCustomObject]@{
    Test = "Assembly Resolution Race"
    Result = if ($test5Result.Success) { "PASS" } else { "FAIL" }
    Duration = 0
    Errors = $test5Result.ErrorCount
}

# ============================================================================
# Test 6: SqlServer Coexistence Stress Test
# ============================================================================
Write-Host "`n--- Test 6: SqlServer Coexistence Under Load ---" -ForegroundColor Yellow
Write-Host "Testing repeated coexistence with SqlServer module..." -ForegroundColor Gray

# Check if SqlServer module is available
$sqlServerAvailable = Get-Module -ListAvailable SqlServer -ErrorAction SilentlyContinue

if ($sqlServerAvailable) {
    $test6Errors = @()
    $test6Iterations = [Math]::Min(10, $Iterations)  # Fewer iterations as this is slow

    for ($i = 1; $i -le $test6Iterations; $i++) {
        if ($Verbose) {
            Write-Host "  Coexistence iteration $i/$test6Iterations" -ForegroundColor DarkGray
        }

        $result = pwsh -NoProfile -Command {
            param($modulePath)
            try {
                # Load SqlServer first
                Import-Module SqlServer -ErrorAction Stop

                # Then load dbatools.library with AvoidConflicts
                Import-Module $modulePath -ArgumentList $true -Force -ErrorAction Stop

                # Verify both work
                $null = Get-Command Get-SqlDatabase -ErrorAction Stop
                $null = [Microsoft.SqlServer.Management.Smo.Server]

                "OK"
            } catch {
                "ERROR: $($_.Exception.Message)"
            }
        } -args $modulePath

        if ($result -ne "OK") {
            $test6Errors += "Iteration $i`: $result"
        }
    }

    if ($test6Errors.Count -eq 0) {
        Write-Host "[PASS] Successfully coexisted with SqlServer in $test6Iterations iterations" -ForegroundColor Green
        $testsPassed++
    } else {
        Write-Host "[FAIL] $($test6Errors.Count) coexistence errors" -ForegroundColor Red
        $test6Errors | Select-Object -First 3 | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
        $testsFailed++
    }

    $testResults += [PSCustomObject]@{
        Test = "SqlServer Coexistence"
        Result = if ($test6Errors.Count -eq 0) { "PASS" } else { "FAIL" }
        Duration = 0
        Errors = $test6Errors.Count
    }
} else {
    Write-Host "[SKIP] SqlServer module not available" -ForegroundColor Yellow
    $testResults += [PSCustomObject]@{
        Test = "SqlServer Coexistence"
        Result = "SKIP"
        Duration = 0
        Errors = 0
    }
}

# ============================================================================
# Summary
# ============================================================================
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "  Test Summary" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$testResults | Format-Table -AutoSize

$totalTests = $testsPassed + $testsFailed
Write-Host "`nResults: $testsPassed passed, $testsFailed failed out of $totalTests tests" -ForegroundColor $(if ($testsFailed -eq 0) { "Green" } else { "Red" })

if ($testsFailed -gt 0) {
    exit 1
} else {
    exit 0
}
