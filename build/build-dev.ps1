param(
    [switch]$IncludeDesktop,
    [switch]$SkipSatellites,
    [switch]$SkipRuntime
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
# -SkipRuntime is the inverse: refresh only the satellites, leaving the staged base dbatools.dll
# alone. Use it when you touched a satellite and NOT dbatools/ - the common case for a port
# iteration. It exists because the base drop cannot be re-staged at all while any live session has
# it loaded (see the holder check below), and that blocked satellite-only work that never needed
# the base refreshed (#849). Skew is still caught downstream: the gate's base-library parity guard
# compares the guest base dbatools.dll against the host build, so a -SkipRuntime run that DID
# change dbatools/ source reds there rather than shipping quietly.
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

if ($SkipRuntime -and $SkipSatellites) {
    Write-Host "ERROR: -SkipRuntime and -SkipSatellites together leave nothing to build." -ForegroundColor Red
    exit 1
}

# Fail fast if the dependency base is not staged - see PREREQUISITE above. This applies to both
# paths: a satellite-only run still loads against the staged dependency tree.
foreach ($edition in $editions) {
    $baseDll = Join-Path -Path $moduleDir -ChildPath "$($edition.Name)/lib/dbatools.dll"
    if (-not (Test-Path -LiteralPath $baseDll)) {
        Write-Host "ERROR: base drop missing $baseDll - run build/build.ps1 once to seed the dependency tree before using build-dev.ps1." -ForegroundColor Red
        exit 1
    }
}

function Test-StagedDllWritable {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )
    try {
        $stream = [System.IO.File]::Open($Path, "Open", "ReadWrite", "None")
        $stream.Close()
        return $true
    } catch {
        return $false
    }
}

function Get-StagedDllHolder {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )
    foreach ($proc in (Get-Process -ErrorAction SilentlyContinue)) {
        if ($proc.Id -eq $PID) {
            continue
        }
        try {
            $loaded = @($proc.Modules | Where-Object { $_.FileName -eq $Path })
        } catch {
            # .Modules throws for processes this session cannot open. Not attributable, not a
            # reason to call the file free - Test-StagedDllWritable already decided that.
            continue
        }
        if ($loaded.Count -gt 0) {
            [PSCustomObject]@{
                Id      = $proc.Id
                Name    = $proc.ProcessName
                Started = $proc.StartTime
            }
        }
    }
}

# A .NET assembly is locked for the whole lifetime of any process that loaded it, and long-running
# campaign windows (regate side runs, full-suite runs, the editor's PowerShell host) import the
# core drop straight out of artifacts. So base staging fails whenever one of them is alive. That
# surfaced as a bare Copy-Item "used by another process" AFTER a multi-minute build and was read as
# an edit-lease conflict (#849) - the lease neither causes this nor can fix it. Check up front and
# name the holders, so the next reader gets an answer instead of filing an outage.
if (-not $SkipRuntime) {
    foreach ($edition in $editions) {
        $targetDll = Join-Path -Path $moduleDir -ChildPath "$($edition.Name)/lib/dbatools.dll"
        if (Test-StagedDllWritable -Path $targetDll) {
            continue
        }
        Write-Host "ERROR: cannot stage $($edition.Name)/lib/dbatools.dll - it is loaded by a live process:" -ForegroundColor Red
        $holders = @(Get-StagedDllHolder -Path $targetDll)
        if ($holders.Count -eq 0) {
            Write-Host "  no holder could be attributed - it is likely held under another user account." -ForegroundColor Red
        }
        foreach ($holder in $holders) {
            Write-Host "  $($holder.Name) pid=$($holder.Id) started=$($holder.Started)" -ForegroundColor Red
        }
        Write-Host "  This is NOT the library edit lease. Do not kill these - a peer gate or regate run may be live." -ForegroundColor Yellow
        Write-Host "  If you did not change dbatools/ source, re-run with -SkipRuntime to refresh satellites only." -ForegroundColor Yellow
        exit 1
    }
}

Push-Location -Path $projectRoot
try {
    # Refresh the shared runtime dbatools.dll per requested edition (incremental Release build).
    foreach ($edition in $editions) {
        if ($SkipRuntime) {
            Write-Host "Skipping runtime dbatools.dll ($($edition.Name)) - -SkipRuntime; the staged base is unchanged." -ForegroundColor Yellow
            continue
        }
        Write-Host "Building runtime dbatools.dll ($($edition.Framework), Release)..." -ForegroundColor Cyan
        dotnet build dbatools/dbatools.csproj --configuration Release --framework $edition.Framework --nologo | Out-String -OutVariable runtimeBuild
        if ($LASTEXITCODE -ne 0) {
            Write-Host "ERROR: runtime build ($($edition.Framework)) failed with exit code $LASTEXITCODE" -ForegroundColor Red
            exit $LASTEXITCODE
        }
        # dbatools.csproj redirects Release output to artifacts/lib/Release/<tfm>/ (custom OutputPath),
        # NOT the default bin/ - so read the freshly built runtime dll from there.
        $builtDll = Join-Path -Path $artifactsDir -ChildPath "lib/Release/$($edition.Framework)/dbatools.dll"
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
                Write-Host "Building satellite $satelliteName ($($edition.Framework), Release)..." -ForegroundColor Cyan
                dotnet build "$satelliteName/$satelliteName.csproj" --configuration Release --framework $edition.Framework --nologo | Out-String -OutVariable satelliteBuild
                if ($LASTEXITCODE -ne 0) {
                    Write-Host "ERROR: satellite build ($satelliteName, $($edition.Framework)) failed with exit code $LASTEXITCODE" -ForegroundColor Red
                    exit $LASTEXITCODE
                }
                $builtSat = Join-Path -Path $projectRoot -ChildPath "$satelliteName/bin/Release/$($edition.Framework)/$satelliteName.dll"
                if (-not (Test-Path -LiteralPath $builtSat)) {
                    Write-Host "ERROR: expected satellite output not found: $builtSat" -ForegroundColor Red
                    exit 1
                }
                $editionStage = Join-Path -Path $moduleStage -ChildPath $edition.Name
                $null = New-Item -ItemType Directory -Path $editionStage -Force
                Copy-Item -Path $builtSat -Destination $editionStage -Force
            }
            # dll-Help.xml (MAML) is generated only from the net8.0 build (CmdletHelp.props) but
            # is target-framework independent, so the one file is staged beside the assembly in
            # every edition. Mirrors the same step in build/build.ps1.
            $helpFile = Join-Path -Path $projectRoot -ChildPath "$satelliteName/bin/Release/net8.0/$satelliteName.dll-Help.xml"
            if (Test-Path -LiteralPath $helpFile) {
                foreach ($edition in $editions) {
                    Copy-Item -Path $helpFile -Destination (Join-Path -Path $moduleStage -ChildPath $edition.Name) -Force
                }
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
