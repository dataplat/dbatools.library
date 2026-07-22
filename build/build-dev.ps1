param(
    [switch]$IncludeDesktop,
    [switch]$SkipSatellites
)
# Fast per-iteration dev build for the libmigration campaign (migration PHASE-0 row P0-004).
#
# WHY THIS EXISTS: build/build.ps1 -CoreOnly is NOT a fast path - by the time it returns it has
# already run dotnet clean, then dotnet publish --self-contained for net472 AND net8.0, then a
# release build of every satellite. That is minutes per iteration. This script instead REUSES the
# heavy dependency tree (SMO, SqlClient, DacFx, Bogus, native runtimes) already staged by a prior
# full build under artifacts/dbatools.library, and recompiles only the two thin things a port
# iteration actually changes: the shared runtime dbatools.dll and the satellite cmdlet assemblies.
# An incremental dotnet build (no clean, no publish, no self-contained) rebuilds only what changed.
#
# DEFAULT is core-only (net8.0) for speed. Pass -IncludeDesktop to also refresh the net472 drop,
# which the acceptance check and any Windows PowerShell 5.1 gate need. Pass -SkipSatellites to
# refresh only the runtime dll (fastest; use only when you did not touch a satellite).
#
# ACCEPTANCE (P0-004): after `build-dev.ps1 -IncludeDesktop`, Use-LocalDbatoolsLibrary -Validate is
# green (both editions import warning-clean) because the dependency tree is reused untouched and
# only dbatools.dll is refreshed.
#
# PREREQUISITE: a full drop must exist under artifacts/dbatools.library (run build/build.ps1 once
# to seed it). This script deliberately does NOT rebuild the dependency tree - that is the whole
# source of its speed - so it fails fast and loud if the base drop is missing rather than silently
# emitting an unloadable runtime with no dependencies.

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$scriptroot = $PSScriptRoot
if (-not $scriptroot) {
    $scriptroot = Split-Path -Path $MyInvocation.MyCommand.Path
}
$root = Split-Path -Path $scriptroot
$projectRoot = Join-Path -Path $root -ChildPath "project"
$artifactsDir = Join-Path -Path $root -ChildPath "artifacts"
$moduleDir = Join-Path -Path $artifactsDir -ChildPath "dbatools.library"

$stopwatch = New-Object System.Diagnostics.Stopwatch
$stopwatch.Start()

# Editions to refresh. Core (net8.0) always; desktop (net472) only when asked.
$editions = @(
    [PSCustomObject]@{ Name = "core"; Framework = "net8.0" }
)
if ($IncludeDesktop) {
    $editions += [PSCustomObject]@{ Name = "desktop"; Framework = "net472" }
}

# Fail fast if the dependency base is not staged - see PREREQUISITE above.
foreach ($edition in $editions) {
    $baseDll = Join-Path -Path $moduleDir -ChildPath "$($edition.Name)/lib/dbatools.dll"
    if (-not (Test-Path -LiteralPath $baseDll)) {
        Write-Host "ERROR: base drop missing $baseDll - run build/build.ps1 once to seed the dependency tree before using build-dev.ps1." -ForegroundColor Red
        exit 1
    }
}

Push-Location -Path $projectRoot
try {
    # Refresh the shared runtime dbatools.dll per requested edition (incremental Debug build).
    foreach ($edition in $editions) {
        Write-Host "Building runtime dbatools.dll ($($edition.Framework), Debug)..." -ForegroundColor Cyan
        dotnet build dbatools/dbatools.csproj --configuration Debug --framework $edition.Framework --nologo | Out-String -OutVariable runtimeBuild
        if ($LASTEXITCODE -ne 0) {
            Write-Host "ERROR: runtime build ($($edition.Framework)) failed with exit code $LASTEXITCODE" -ForegroundColor Red
            exit $LASTEXITCODE
        }
        # dbatools.csproj redirects Debug output to artifacts/lib/Debug/<tfm>/ (custom OutputPath),
        # NOT the default bin/ - so read the freshly built runtime dll from there.
        $builtDll = Join-Path -Path $artifactsDir -ChildPath "lib/Debug/$($edition.Framework)/dbatools.dll"
        if (-not (Test-Path -LiteralPath $builtDll)) {
            Write-Host "ERROR: expected build output not found: $builtDll" -ForegroundColor Red
            exit 1
        }
        $targetDll = Join-Path -Path $moduleDir -ChildPath "$($edition.Name)/lib/dbatools.dll"
        Copy-Item -Path $builtDll -Destination $targetDll -Force
        Write-Host "Staged runtime: $($edition.Name)/lib/dbatools.dll" -ForegroundColor Green
    }

    # Refresh the satellite cmdlet assemblies. These ship inside each satellite package, staged to
    # artifacts/modules/dbatools.<module>/{core,desktop}/ exactly as build/build.ps1 does, so
    # Ship-Satellite.ps1 pushes the freshly built cmdlet dll on the next gate. Branch 30's
    # build-dev staged dbatools.dll ONLY, which left a runtime with no commands - this closes that.
    if (-not $SkipSatellites) {
        $satelliteProjects = Get-ChildItem -Path $projectRoot -Directory -Filter "dbatools.*" | Where-Object {
            $_.Name -ne "dbatools" -and $_.Name -ne "dbatools.Tests" -and (Test-Path (Join-Path $_.FullName "$($_.Name).csproj"))
        }
        foreach ($satellite in $satelliteProjects) {
            $satelliteName = $satellite.Name
            $moduleStage = Join-Path -Path $artifactsDir -ChildPath "modules/$satelliteName"
            foreach ($edition in $editions) {
                Write-Host "Building satellite $satelliteName ($($edition.Framework), Debug)..." -ForegroundColor Cyan
                dotnet build "$satelliteName/$satelliteName.csproj" --configuration Debug --framework $edition.Framework --nologo | Out-String -OutVariable satelliteBuild
                if ($LASTEXITCODE -ne 0) {
                    Write-Host "ERROR: satellite build ($satelliteName, $($edition.Framework)) failed with exit code $LASTEXITCODE" -ForegroundColor Red
                    exit $LASTEXITCODE
                }
                $builtSat = Join-Path -Path $projectRoot -ChildPath "$satelliteName/bin/Debug/$($edition.Framework)/$satelliteName.dll"
                if (-not (Test-Path -LiteralPath $builtSat)) {
                    Write-Host "ERROR: expected satellite output not found: $builtSat" -ForegroundColor Red
                    exit 1
                }
                $editionStage = Join-Path -Path $moduleStage -ChildPath $edition.Name
                $null = New-Item -ItemType Directory -Path $editionStage -Force
                Copy-Item -Path $builtSat -Destination $editionStage -Force
            }
            Write-Host "Staged satellite: $satelliteName" -ForegroundColor Green
        }
    }
} finally {
    Pop-Location
}

$stopwatch.Stop()
Write-Host "Dev build complete in $([math]::Round($stopwatch.Elapsed.TotalSeconds, 1))s: $moduleDir" -ForegroundColor Green
Write-Host "Validate with: Use-LocalDbatoolsLibrary.ps1 -Validate" -ForegroundColor Green
