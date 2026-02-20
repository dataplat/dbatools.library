<#
.SYNOPSIS
    Runs dbatools Pester tests from dbatools-ralph against locally-built dbatools.library.

.DESCRIPTION
    Builds dbatools.library from source, imports it along with dbatools-ralph,
    then runs Pester 5 tests one file at a time against the local lab instances.

.PARAMETER Path
    Test file names or patterns (e.g. "Connect-DbaInstance", "Get-DbaAgent*").
    If not specified, runs all tests.

.PARAMETER SkipBuild
    Skip the dotnet build step (use existing build artifacts).

.PARAMETER SkipImport
    Skip module import (use already loaded modules).

.PARAMETER Verbosity
    Pester output verbosity level.

.PARAMETER MaxRetries
    Number of retry attempts for failed test files (1 = no retry).

.PARAMETER ListOnly
    Just list which tests would run, do not execute.

.EXAMPLE
    .\ralph-test-runner.ps1 -Path "Connect-DbaInstance"

.EXAMPLE
    .\ralph-test-runner.ps1 -Path "Get-DbaAgent*" -SkipBuild

.EXAMPLE
    .\ralph-test-runner.ps1 -ListOnly
#>
[CmdletBinding()]
param(
    [string[]]$Path,
    [switch]$SkipBuild,
    [switch]$SkipImport,
    [ValidateSet("None", "Normal", "Detailed", "Diagnostic")]
    [string]$Verbosity = "Normal",
    [int]$MaxRetries = 1,
    [switch]$ListOnly
)

$ErrorActionPreference = "Stop"
$LibraryRoot = Split-Path $PSScriptRoot -Parent
$RalphRoot = "C:\github\dbatools-ralph"
$TestsDir = Join-Path $RalphRoot "tests"

# =============================================================================
# Step 1: Build dbatools.library
# =============================================================================
if (-not $SkipBuild) {
    Write-Host "`n=== Building dbatools.library ===" -ForegroundColor Cyan
    $buildStart = Get-Date
    $csproj = Join-Path $LibraryRoot "project\dbatools\dbatools.csproj"
    $buildResult = & dotnet build $csproj --configuration Debug 2>&1
    $buildTime = (Get-Date) - $buildStart

    if ($LASTEXITCODE -ne 0) {
        Write-Host "BUILD FAILED" -ForegroundColor Red
        $buildResult | ForEach-Object { Write-Host $_ }
        return
    }
    Write-Host "Build succeeded in $([math]::Round($buildTime.TotalSeconds, 1))s" -ForegroundColor Green

    # Copy freshly built DLLs to the module artifacts location
    # The build outputs to artifacts/lib/Debug/ but the module loads from artifacts/dbatools.library/core|desktop/lib/
    $framework = if ($PSVersionTable.PSEdition -eq "Core") { "net8.0" } else { "net472" }
    $targetDir = if ($PSVersionTable.PSEdition -eq "Core") { "core" } else { "desktop" }
    $buildDll = Join-Path $LibraryRoot "artifacts\lib\Debug\$framework\dbatools.dll"
    $targetDll = Join-Path $LibraryRoot "artifacts\dbatools.library\$targetDir\lib\dbatools.dll"
    if (Test-Path $buildDll) {
        Copy-Item $buildDll $targetDll -Force
        Write-Host "  Copied dbatools.dll to $targetDir\lib\" -ForegroundColor Green
    }

    # Also copy the psd1/psm1 from root (they may have been updated)
    Copy-Item (Join-Path $LibraryRoot "dbatools.library.psd1") (Join-Path $LibraryRoot "artifacts\dbatools.library\dbatools.library.psd1") -Force
    Copy-Item (Join-Path $LibraryRoot "dbatools.library.psm1") (Join-Path $LibraryRoot "artifacts\dbatools.library\dbatools.library.psm1") -Force
} else {
    Write-Host "`n=== Skipping build (using existing artifacts) ===" -ForegroundColor Yellow
}

# =============================================================================
# Step 2: Import modules
# =============================================================================
if (-not $SkipImport) {
    Write-Host "`n=== Importing modules ===" -ForegroundColor Cyan

    # Remove any currently loaded versions
    Remove-Module dbatools -Force -ErrorAction SilentlyContinue
    Remove-Module dbatools.library -Force -ErrorAction SilentlyContinue

    # Import freshly built library
    $libraryPsd1 = Join-Path $LibraryRoot "artifacts\dbatools.library\dbatools.library.psd1"
    if (-not (Test-Path $libraryPsd1)) {
        Write-Host "ERROR: Library artifacts not found at $libraryPsd1" -ForegroundColor Red
        Write-Host "Run without -SkipBuild to build first." -ForegroundColor Yellow
        return
    }
    Import-Module $libraryPsd1 -Force
    Write-Host "  Imported dbatools.library from artifacts" -ForegroundColor Green

    # Import dbatools (dot-source mode for test internals access)
    $global:dbatools_dotsourcemodule = $true
    $ralphPsm1 = Join-Path $RalphRoot "dbatools.psm1"
    Import-Module $ralphPsm1 -Force -DisableNameChecking
    Write-Host "  Imported dbatools from $RalphRoot" -ForegroundColor Green

    # Trust certs in lab
    Set-DbatoolsInsecureConnection
} else {
    Write-Host "`n=== Skipping module import ===" -ForegroundColor Yellow
}

# Set up TestConfig so Pester tests can access CommonParameters and instance info
$TestConfig = Get-TestConfig
Write-Host "  TestConfig loaded (CommonParameters: $($TestConfig.CommonParameters.Count), Instances: $($TestConfig.InstanceSingle))" -ForegroundColor Green

# =============================================================================
# Step 3: Resolve test files
# =============================================================================
Write-Host "`n=== Resolving test files ===" -ForegroundColor Cyan

$allTestFiles = Get-ChildItem -File -Path $TestsDir -Filter "*.Tests.ps1" | Sort-Object Name

if ($Path) {
    $selectedTests = @()
    foreach ($p in $Path) {
        if (Test-Path $p) {
            # Direct path to a test file
            $selectedTests += Get-Item $p
        } else {
            # Pattern match against test file names
            $pattern = $p
            if ($pattern -notlike "*.Tests.ps1") {
                $pattern = "*$pattern*.Tests.ps1"
            }
            $matches = $allTestFiles | Where-Object { $_.Name -like $pattern }
            if ($matches) {
                $selectedTests += $matches
            } else {
                Write-Host "  WARNING: No tests matched pattern '$p'" -ForegroundColor Yellow
            }
        }
    }
    $testFiles = $selectedTests | Sort-Object Name | Select-Object -Unique
} else {
    $testFiles = $allTestFiles
}

Write-Host "  Found $($testFiles.Count) test file(s) to run" -ForegroundColor Green

if ($testFiles.Count -eq 0) {
    Write-Host "No tests to run." -ForegroundColor Yellow
    return
}

# =============================================================================
# Step 4: List only mode
# =============================================================================
if ($ListOnly) {
    Write-Host "`n=== Test files that would run ===" -ForegroundColor Cyan
    foreach ($f in $testFiles) {
        Write-Host "  $($f.Name)"
    }
    Write-Host "`nTotal: $($testFiles.Count) files" -ForegroundColor Green
    return
}

# =============================================================================
# Step 5: Initialize Pester
# =============================================================================
Remove-Module Pester -ErrorAction SilentlyContinue
Import-Module Pester -RequiredVersion 5.7.1
Write-Host "  Using Pester $((Get-Command Invoke-Pester).Version)" -ForegroundColor Green

# =============================================================================
# Step 6: Run tests
# =============================================================================
Write-Host "`n=== Running tests ===" -ForegroundColor Cyan

$totalPassed = 0
$totalFailed = 0
$totalSkipped = 0
$failedFiles = @()
$overallStart = Get-Date

$counter = 0
foreach ($f in $testFiles) {
    $counter++
    Write-Host "`n[$counter/$($testFiles.Count)] $($f.Name) " -ForegroundColor White -NoNewline

    $attempt = 0
    $passed = $false

    while ($attempt -lt $MaxRetries -and -not $passed) {
        $attempt++
        if ($attempt -gt 1) {
            Write-Host "  Retry $attempt/$MaxRetries..." -ForegroundColor Yellow
        }

        $pesterConfig = New-PesterConfiguration
        $pesterConfig.Run.Path = $f.FullName
        $pesterConfig.Run.PassThru = $true
        $pesterConfig.Output.Verbosity = $Verbosity

        $result = Invoke-Pester -Configuration $pesterConfig

        if ($result.FailedCount -eq 0) {
            $passed = $true
            $duration = [math]::Round($result.Duration.TotalSeconds, 1)
            Write-Host "PASSED ($($result.PassedCount) tests, ${duration}s)" -ForegroundColor Green
            $totalPassed += $result.PassedCount
            $totalSkipped += $result.SkippedCount
        } else {
            if ($attempt -ge $MaxRetries) {
                $duration = [math]::Round($result.Duration.TotalSeconds, 1)
                Write-Host "FAILED ($($result.FailedCount) failed, $($result.PassedCount) passed, ${duration}s)" -ForegroundColor Red
                $totalPassed += $result.PassedCount
                $totalFailed += $result.FailedCount
                $totalSkipped += $result.SkippedCount

                # Collect failure details
                $failures = $result.Tests | Where-Object { $_.Passed -eq $false } | ForEach-Object {
                    $errMsg = ""
                    if ($_.ErrorRecord -and $_.ErrorRecord.Count -gt 0) {
                        $errMsg = $_.ErrorRecord[0].Exception.Message
                    }
                    [PSCustomObject]@{
                        File    = $f.Name
                        Test    = $_.Name
                        Error   = $errMsg
                    }
                }
                $failedFiles += $failures
            }
        }
    }
}

# =============================================================================
# Step 7: Summary
# =============================================================================
$overallTime = (Get-Date) - $overallStart

Write-Host "`n`n========================================" -ForegroundColor Cyan
Write-Host "  TEST RESULTS SUMMARY" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Files:   $($testFiles.Count)"
Write-Host "  Passed:  $totalPassed" -ForegroundColor Green
Write-Host "  Failed:  $totalFailed" -ForegroundColor $(if ($totalFailed -gt 0) { "Red" } else { "Green" })
Write-Host "  Skipped: $totalSkipped" -ForegroundColor Yellow
Write-Host "  Time:    $([math]::Round($overallTime.TotalSeconds, 1))s"
Write-Host "========================================`n" -ForegroundColor Cyan

if ($failedFiles.Count -gt 0) {
    Write-Host "Failed tests:" -ForegroundColor Red
    foreach ($failure in $failedFiles) {
        Write-Host "  $($failure.File) > $($failure.Test)" -ForegroundColor Red
        if ($failure.Error) {
            Write-Host "    $($failure.Error)" -ForegroundColor DarkRed
        }
    }
}
