param(
    [switch]$IncludeDesktop
)
# Fast dev build for migration testing.
# Produces a loadable module at artifacts/dbatools.library/ with the freshly built dbatools.dll.
# Usage:
#   pwsh -NoProfile -File build/build-dev.ps1
#   pwsh -NoProfile -Command 'Import-Module artifacts/dbatools.library/dbatools.library.psd1 -Force'

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$root = Split-Path -Path $PSScriptRoot
Push-Location $root

try {
    $artifactsDir = Join-Path $root "artifacts"
    $moduleDir = Join-Path $artifactsDir "dbatools.library"
    $coreLibDir = Join-Path $moduleDir "core/lib"
    $tempPublish = Join-Path $artifactsDir "publish-temp"

    # Clean previous artifacts
    if (Test-Path $artifactsDir) {
        Remove-Item -Path $artifactsDir -Recurse -Force -ErrorAction SilentlyContinue
    }

    $null = New-Item -ItemType Directory -Path $moduleDir -Force
    $null = New-Item -ItemType Directory -Path $coreLibDir -Force
    $null = New-Item -ItemType Directory -Path $tempPublish -Force

    # Build and publish net8.0 (includes all dependency DLLs)
    Write-Host "Building net8.0..." -ForegroundColor Cyan
    dotnet publish project/dbatools/dbatools.csproj --configuration Debug --framework net8.0 --output $tempPublish --nologo --self-contained true 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build failed." -ForegroundColor Red
        exit 1
    }

    # Copy all publish output to core/lib
    Copy-Item -Path "$tempPublish\*" -Destination $coreLibDir -Recurse -Force

    # SqlClient: the publish output has a Windows version but psm1 expects the unix one for net8.0
    # Copy the unix SqlClient over the Windows one (matches what build.ps1 does)
    $unixSqlClient = Join-Path $coreLibDir "runtimes/unix/lib/net8.0/Microsoft.Data.SqlClient.dll"
    if (Test-Path $unixSqlClient) {
        Copy-Item $unixSqlClient -Destination $coreLibDir -Force
    }

    # Create third-party/bogus directory (psm1 tries to load Bogus.dll)
    $bogusDir = Join-Path $moduleDir "core/third-party/bogus"
    $null = New-Item -ItemType Directory -Path $bogusDir -Force

    # Try to find Bogus.dll from the installed module
    $installedBogus = "C:\Program Files\PowerShell\Modules\dbatools.library\*\core\third-party\bogus\Bogus.dll"
    $bogusSource = Get-Item $installedBogus -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($bogusSource) {
        Copy-Item $bogusSource.FullName -Destination $bogusDir -Force
    } else {
        Write-Host "Warning: Bogus.dll not found, module may show a non-fatal error on load" -ForegroundColor Yellow
    }

    # Copy module manifest and loader
    Copy-Item -Path (Join-Path $root "dbatools.library.psd1") -Destination $moduleDir -Force
    Copy-Item -Path (Join-Path $root "dbatools.library.psm1") -Destination $moduleDir -Force

    # Desktop build (optional, only needed for Windows PowerShell 5.1 testing)
    if ($IncludeDesktop) {
        $desktopLibDir = Join-Path $moduleDir "desktop/lib"
        $tempDesktop = Join-Path $artifactsDir "publish-temp-desktop"
        $null = New-Item -ItemType Directory -Path $desktopLibDir -Force
        $null = New-Item -ItemType Directory -Path $tempDesktop -Force

        Write-Host "Building net472..." -ForegroundColor Cyan
        dotnet publish project/dbatools/dbatools.csproj --configuration Debug --framework net472 --output $tempDesktop --nologo --self-contained true 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Host "Desktop build failed." -ForegroundColor Red
            exit 1
        }
        Copy-Item -Path "$tempDesktop\*" -Destination $desktopLibDir -Recurse -Force
    }

    # Clean up temp
    Remove-Item -Path $tempPublish -Recurse -Force -ErrorAction SilentlyContinue
    if ($IncludeDesktop) {
        Remove-Item -Path (Join-Path $artifactsDir "publish-temp-desktop") -Recurse -Force -ErrorAction SilentlyContinue
    }

    Write-Host "Dev build complete: $moduleDir" -ForegroundColor Green
    Write-Host "Load with: Import-Module $moduleDir\dbatools.library.psd1 -Force" -ForegroundColor Green
} finally {
    Pop-Location
}
