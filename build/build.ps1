param(
    [switch]$BuildZip,
    [switch]$CoreOnly
)
$PSDefaultParameterValues["*:Force"] = $true
$PSDefaultParameterValues["*:Confirm"] = $false
$ProgressPreference = "SilentlyContinue"
# Import MSI waiting functions
. "$PSScriptRoot\Wait-MsiInstall.ps1"

# Get script root and project root
$scriptroot = $PSScriptRoot
if (-not $scriptroot) {
    $scriptroot = Split-Path -Path $MyInvocation.MyCommand.Path
}
$root = Split-Path -Path $scriptroot
Push-Location $root

# Update module version to today's date
$today = Get-Date -Format "yyyy.M.d"
$psd1Path = Join-Path $root "dbatools.library.psd1"
$psd1Content = Get-Content $psd1Path -Raw
$psd1Content = $psd1Content -replace "ModuleVersion\s*=\s*'[\d\.]+'", "ModuleVersion          = '$today'"
Set-Content -Path $psd1Path -Value $psd1Content -NoNewline
Write-Host "Updated module version to: $today"

# Create centralized build directory
$artifactsDir = Join-Path $root "artifacts"
$dbatoolsLibraryDir = Join-Path $artifactsDir "dbatools.library"
$libPath = $dbatoolsLibraryDir
$tempPath = Join-Path $artifactsDir "temp"
$publishDir = Join-Path $artifactsDir "publish"
$licensePath = Join-Path $dbatoolsLibraryDir "third-party-licenses"

# Clean up previous build artifacts
if (Test-Path $artifactsDir) {
    Remove-Item -Path $artifactsDir -Recurse -ErrorAction SilentlyContinue
}

# Create build directory structure
$null = New-Item -ItemType Directory -Path $artifactsDir -Force
$null = New-Item -ItemType Directory -Path $dbatoolsLibraryDir -Force
$null = New-Item -ItemType Directory -Path $tempPath -Force
$null = New-Item -ItemType Directory -Path $publishDir -Force

Write-Host "Created centralized build directory at: $artifactsDir" -ForegroundColor Cyan
Write-Host "All build artifacts will be placed in this directory to keep the root clean." -ForegroundColor Yellow
Push-Location "$root\project"

dotnet clean

# Create temp directories for publish output
$tempDesktopPublish = Join-Path $publishDir "desktop-temp"
$tempCorePublish = Join-Path $publishDir "core-temp"
$null = New-Item -ItemType Directory -Path $tempDesktopPublish -Force
$null = New-Item -ItemType Directory -Path $tempCorePublish -Force

Write-Host "Created publish directories at: $publishDir" -ForegroundColor Cyan

# Publish .NET Framework (desktop) to temp directory first
Write-Host "Publishing .NET Framework build..."
dotnet publish dbatools/dbatools.csproj --configuration release --framework net472 --output $tempDesktopPublish --nologo --self-contained true | Out-String -OutVariable build

# Copy desktop publish output preserving structure
Write-Host "Copying .NET Framework output with preserved structure..."
$null = New-Item -ItemType Directory -Path (Join-Path $libPath "desktop/lib") -Force
Copy-Item -Path "$tempDesktopPublish\*" -Destination (Join-Path $libPath "desktop/lib") -Recurse -Force


# Verify and patch desktop publish output
Write-Host "Verifying desktop publish..."

# Publish .NET 8 (core) to temp directory first
Write-Host "Publishing .NET 8 build..."
dotnet publish dbatools/dbatools.csproj --configuration release --framework net8.0 --output $tempCorePublish --nologo --self-contained true | Out-String -OutVariable build

# Copy core publish output preserving structure
Write-Host "Copying .NET 8 output with preserved structure..."
$null = New-Item -ItemType Directory -Path (Join-Path $libPath "core/lib") -Force
Copy-Item -Path "$tempCorePublish\*" -Destination (Join-Path $libPath "core/lib") -Recurse -Force

# Verify and organize runtime dependencies
Write-Host "Verifying .NET 8 runtime dependencies..."
$coreRuntimesPath = Join-Path $libPath "core\lib\runtimes"
$desktopRuntimesPath = Join-Path $libPath "desktop\lib\runtimes"

# Ensure desktop runtimes directory exists
New-Item -ItemType Directory -Path $desktopRuntimesPath -Force | Out-Null

if (Test-Path $coreRuntimesPath) {
    Write-Host "Core runtimes folder found. Processing native dependencies..." -ForegroundColor Green

    $architectures = @("win-x86", "win-x64", "win-arm", "win-arm64")

    foreach ($arch in $architectures) {
        $coreArchPath = Join-Path $coreRuntimesPath "$arch\native"
        $desktopArchPath = Join-Path $desktopRuntimesPath "$arch\native"

        if (Test-Path $coreArchPath) {
            Write-Host "Processing architecture: $arch" -ForegroundColor Cyan

            # Ensure desktop architecture directory exists
            New-Item -ItemType Directory -Path $desktopArchPath -Force | Out-Null

            # Copy all native files from core to desktop for Windows PowerShell compatibility
            Get-ChildItem -Path $coreArchPath -File | ForEach-Object {
                $sourcePath = $_.FullName
                $destPath = Join-Path $desktopArchPath $_.Name

                Copy-Item $sourcePath -Destination $destPath -Force
                Write-Host "  Copied: $($_.Name)" -ForegroundColor Green
            }

            # Verify SNI DLL specifically
            $sniPath = Join-Path $coreArchPath "Microsoft.Data.SqlClient.SNI.dll"
            if (Test-Path $sniPath) {
                $dllInfo = Get-Item $sniPath
                Write-Host "  SNI DLL verified - Size: $($dllInfo.Length) bytes" -ForegroundColor Green
            } else {
                Write-Warning "  SNI DLL not found for $arch"
            }
        } else {
            Write-Warning "Architecture path not found: $coreArchPath"
        }
    }
} else {
    Write-Host "ERROR: Core runtimes folder not found at: $coreRuntimesPath" -ForegroundColor Red

    # Check what's actually in the core lib directory
    $coreLibPath = Join-Path $libPath "core\lib"
    if (Test-Path $coreLibPath) {
        Write-Host "Core lib directory contents:" -ForegroundColor Yellow
        Get-ChildItem -Path $coreLibPath -Recurse | Where-Object { $_.Name -like "*SNI*" } | Select-Object FullName
    }
}

Copy-Item (Join-Path $libPath "core\lib\runtimes\unix\lib\net8.0\Microsoft.Data.SqlClient.dll") -Destination (Join-Path $libPath "core/lib/") -Force

if ($CoreOnly) {
    Write-Host "CoreOnly specified - returning after core build"
    return
}
# Run tests specifically for dbatools.Tests
# dotnet test dbatools.Tests/dbatools.Tests.csproj --framework net472 --verbosity normal --no-restore --nologo | Out-String -OutVariable test
Pop-Location

$targetDesktop = (Join-Path $libPath "desktop/net472")
if (Test-Path $targetDesktop) {
    Remove-Item -Path $targetDesktop -Recurse -ErrorAction SilentlyContinue
}
$targetCore = (Join-Path $libPath "core/net8.0")
if (Test-Path $targetCore) {
    Remove-Item -Path $targetCore -Recurse -ErrorAction SilentlyContinue
}

$tempdir = Join-Path ([System.IO.Path]::GetTempPath()) "dbatools-build"

# Create all required directories
$null = New-Item -ItemType Directory $tempdir -Force -ErrorAction Ignore
$null = New-Item -ItemType Directory (Join-Path $libPath "desktop/third-party/bogus") -Force
$null = New-Item -ItemType Directory (Join-Path $libPath "core/third-party/bogus") -Force
$null = New-Item -ItemType Directory (Join-Path $libPath "core/lib/runtimes") -Force
$null = New-Item -ItemType Directory (Join-Path $tempPath "bogus") -Force
$null = New-Item -ItemType Directory (Join-Path $tempdir "nuget") -Force

Register-PackageSource -provider NuGet -name nugetRepository -Location https://www.nuget.org/api/v2 -Trusted -ErrorAction Ignore

# Download all required packages
Invoke-WebRequest -Uri https://www.nuget.org/api/v2/package/Bogus -OutFile (Join-Path $tempPath "bogus.zip")

$ProgressPreference = "Continue"

# Extract all packages
7z x (Join-Path $tempPath "bogus.zip") "-o$(Join-Path $tempPath "bogus")" -y


# Copy Bogus files for both frameworks
Write-Host "Copying Bogus.dll..." -ForegroundColor Green

# Copy Bogus.dll for .NET Framework (handle case sensitivity)
if (Test-Path (Join-Path $tempPath "bogus/lib/net40/bogus.dll")) {
    Copy-Item (Join-Path $tempPath "bogus/lib/net40/bogus.dll") -Destination (Join-Path $libPath "desktop/third-party/bogus/Bogus.dll") -Force
} elseif (Test-Path (Join-Path $tempPath "bogus/lib/net40/Bogus.dll")) {
    Copy-Item (Join-Path $tempPath "bogus/lib/net40/Bogus.dll") -Destination (Join-Path $libPath "desktop/third-party/bogus/Bogus.dll") -Force
} else {
    Write-Warning "Bogus.dll for .NET Framework (net40) not found at expected location"
}

# Copy Bogus.dll for .NET Core (handle case sensitivity)
$bogusCoreCopied = $false
# Try net6.0 first (both lowercase and uppercase)
if (Test-Path (Join-Path $tempPath "bogus/lib/net6.0/bogus.dll")) {
    Copy-Item (Join-Path $tempPath "bogus/lib/net6.0/bogus.dll") -Destination (Join-Path $libPath "core/third-party/bogus/Bogus.dll") -Force
    $bogusCoreCopied = $true
} elseif (Test-Path (Join-Path $tempPath "bogus/lib/net6.0/Bogus.dll")) {
    Copy-Item (Join-Path $tempPath "bogus/lib/net6.0/Bogus.dll") -Destination (Join-Path $libPath "core/third-party/bogus/Bogus.dll") -Force
    $bogusCoreCopied = $true
} elseif (Test-Path (Join-Path $tempPath "bogus/lib/netstandard2.0/bogus.dll")) {
    Copy-Item (Join-Path $tempPath "bogus/lib/netstandard2.0/bogus.dll") -Destination (Join-Path $libPath "core/third-party/bogus/Bogus.dll") -Force
    $bogusCoreCopied = $true
} elseif (Test-Path (Join-Path $tempPath "bogus/lib/netstandard2.0/Bogus.dll")) {
    Copy-Item (Join-Path $tempPath "bogus/lib/netstandard2.0/Bogus.dll") -Destination (Join-Path $libPath "core/third-party/bogus/Bogus.dll") -Force
    $bogusCoreCopied = $true
}

if (-not $bogusCoreCopied) {
    Write-Warning "Bogus.dll for .NET Core not found in expected locations"
}

# Core files are already in place from dotnet publish

# Copy var/misc files to appropriate locations
Write-Host "Copying additional assemblies from var/misc..."
# Copy files that go to both core and desktop
Get-ChildItem "./var/misc/both" -Filter "*.dll" | ForEach-Object {
    Copy-Item $_.FullName -Destination (Join-Path $libPath "core/lib/") -Force
    Copy-Item $_.FullName -Destination (Join-Path $libPath "desktop/lib/") -Force
}

# Copy desktop-specific files
Get-ChildItem "./var/misc/desktop" -Filter "*.dll" | Copy-Item -Destination (Join-Path $libPath "desktop/lib/") -Force

# Cleanup temporary files and artifacts
Write-Host "Cleaning up temporary files..."
# We don't remove the artifacts directory since it's our centralized build output
Get-ChildItem -Path (Join-Path $libPath "*") -Recurse -Include "*.pdf","*.xml" | Remove-Item -Force

# Create directory structure for different versions of System.Runtime.CompilerServices.Unsafe.dll
$null = New-Item -ItemType Directory -Path (Join-Path $libPath "desktop/v4") -Force
$null = New-Item -ItemType Directory -Path (Join-Path $libPath "desktop/lib/v6") -Force

# Copy v4.0.4.1 version from var/misc/desktop to desktop/v4
Write-Host "Copying System.Runtime.CompilerServices.Unsafe v4.0.4.1 for SMO..."
Copy-Item -Path "./var/misc/desktop/System.Runtime.CompilerServices.Unsafe.dll" -Destination (Join-Path $libPath "desktop/v4/") -Force

# Copy v6.0.0.0 version from NuGet cache to desktop/lib/v6 and core/lib
Write-Host "Copying System.Runtime.CompilerServices.Unsafe v6.0.0.0 for SSAS..."
$nugetCache = "$env:USERPROFILE\.nuget\packages";
$v6Unsafe = Get-ChildItem -Path "$nugetCache\system.runtime.compilerservices.unsafe\*\lib\net6.0\System.Runtime.CompilerServices.Unsafe.dll" -Recurse | Select-Object -Last 1
if ($v6Unsafe) {
    Copy-Item -Path $v6Unsafe.FullName -Destination (Join-Path $libPath "core/lib/") -Force
    Copy-Item -Path $v6Unsafe.FullName -Destination (Join-Path $libPath "desktop/lib/v6/") -Force
} else {
    Write-Warning "Could not find System.Runtime.CompilerServices.Unsafe v6.0.0.0 in NuGet cache"
}

# Copy root module files
Copy-Item -Path (Join-Path $root "dbatools.library.psd1") -Destination $dbatoolsLibraryDir -Force
Copy-Item -Path (Join-Path $root "dbatools.library.psm1") -Destination $dbatoolsLibraryDir -Force
Copy-Item -Path (Join-Path $root "LICENSE") -Destination $dbatoolsLibraryDir -Force -ErrorAction SilentlyContinue
Write-Host "Copied module files to artifacts/dbatools.library" -ForegroundColor Green

# Copy third-party-licenses
$licensePath = Join-Path $dbatoolsLibraryDir "third-party-licenses"
$null = New-Item -ItemType Directory -Path $licensePath -Force

if (Test-Path (Join-Path $root "var/third-party-licenses")) {
    Copy-Item -Path (Join-Path $root "var/third-party-licenses/*") -Destination $licensePath -Recurse -Force
    Write-Host "Included third-party-licenses in artifacts/dbatools.library" -ForegroundColor Green
} elseif (Test-Path (Join-Path $artifactsDir "third-party-licenses")) {
    Copy-Item -Path (Join-Path $artifactsDir "third-party-licenses/*") -Destination $licensePath -Recurse -Force
    Write-Host "Included third-party-licenses from artifacts in artifacts/dbatools.library" -ForegroundColor Green
}

# Create zip file for testing (release format)
Write-Host "Creating dbatools.library.zip for testing..."
$zipPath = Join-Path $artifactsDir "dbatools.library.zip"
if (Test-Path $zipPath) {
    Remove-Item -Path $zipPath -Force -ErrorAction SilentlyContinue
}

# Copy third-party-licenses
if (Test-Path (Join-Path $root "var\third-party-licenses")) {
    Copy-Item -Path (Join-Path $root "var\third-party-licenses") -Destination $tempReleaseDir -Recurse -Force
} elseif (Test-Path (Join-Path $artifactsDir "third-party-licenses")) {
    Copy-Item -Path (Join-Path $artifactsDir "third-party-licenses") -Destination $tempReleaseDir -Recurse -Force
}

Write-Host "All build artifacts are centralized in: $artifactsDir" -ForegroundColor Green

# Create the zip file directly from the artifacts/dbatools.library directory
Push-Location $artifactsDir
if ($BuildZip) {
    Compress-Archive -Path "dbatools.library" -DestinationPath $zipPath -CompressionLevel Optimal -Force
}
Pop-Location

Write-Host "Created test zip: $zipPath"
Write-Host "Zip structure: dbatools.library.zip contains 'dbatools.library' folder at root"
Write-Host "For testing: Extract to a folder in `$env:PSModulePath or use Install-DbatoolsLibrary.ps1"

# Final validation of critical files
Write-Host "`n=== Final Build Validation ===" -ForegroundColor Cyan
$validationErrors = @()

# Check for critical runtime dependencies
$criticalFiles = @(
    @{Path = "core\lib\runtimes\win-x64\native\Microsoft.Data.SqlClient.SNI.dll"; Description = ".NET Core SNI DLL (Windows x64)"},
    @{Path = "core\lib\Microsoft.Data.SqlClient.dll"; Description = ".NET Core SqlClient"},
    @{Path = "desktop\lib\Microsoft.Data.SqlClient.dll"; Description = ".NET Framework SqlClient"},
    @{Path = "core\lib\dbatools.dll"; Description = ".NET Core dbatools assembly"},
    @{Path = "desktop\lib\dbatools.dll"; Description = ".NET Framework dbatools assembly"}
)

foreach ($file in $criticalFiles) {
    $fullPath = Join-Path $libPath $file.Path
    if (Test-Path $fullPath) {
        Write-Host "[OK] Found: $($file.Description)" -ForegroundColor Green
    } else {
        Write-Host "[ERROR] Missing: $($file.Description) at $($file.Path)" -ForegroundColor Red
        $validationErrors += "Missing: $($file.Description)"
    }
}

# Check if runtimes folder structure exists
if (Test-Path (Join-Path $libPath "core\lib\runtimes")) {
    Write-Host "[OK] Runtimes folder structure preserved" -ForegroundColor Green
} else {
    Write-Host "[ERROR] Runtimes folder structure missing!" -ForegroundColor Red
    $validationErrors += "Runtimes folder structure missing"
}

if ($validationErrors.Count -eq 0) {
    Write-Host "`nBuild completed successfully. All critical files present." -ForegroundColor Green
} else {
    Write-Host "`nBuild completed with errors:" -ForegroundColor Red
    $validationErrors | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
}

# if github actions and lastexitcode =0 then exit with success
if ($env:GITHUB_ACTIONS -and $LASTEXITCODE -eq 0) {
    exit 0
} else {
    exit $LASTEXITCODE
}