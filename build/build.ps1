param(
    [switch]$NoZip,
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

if ($CoreOnly) {
    Write-Host "CoreOnly specified - returning after core build"
    return
}

# Handle legacy lib/release output directory if it exists (for backward compatibility)
$legacyReleaseDir = Join-Path $root "lib\release"
if (Test-Path $legacyReleaseDir) {
    Write-Host "Found legacy lib/release directory, copying to artifacts/release" -ForegroundColor Yellow
    $artifactsReleaseDir = Join-Path $artifactsDir "release"
    $null = New-Item -ItemType Directory -Path $artifactsReleaseDir -Force
    Copy-Item -Path "$legacyReleaseDir\*" -Destination $artifactsReleaseDir -Recurse -Force

    # Inform about the change
    Write-Host "NOTE: The lib directory in the root is deprecated. Please use artifacts/release instead." -ForegroundColor Yellow
}

# Verify core runtime dependencies
Write-Host "Verifying .NET 8 runtime dependencies..."
$sniPath = Join-Path $libPath "core\lib\runtimes\win-x64\native\Microsoft.Data.SqlClient.SNI.dll"
if (Test-Path $sniPath) {
    $sniBase = Join-Path $libPath "core\lib\runtimes"
    $targetBase = Join-Path $libPath "desktop\lib\runtimes"

    $architectures = @("win-x86", "win-x64", "win-arm", "win-arm64")

    foreach ($arch in $architectures) {
        $sniPath = Join-Path $sniBase "$arch\native\Microsoft.Data.SqlClient.SNI.dll"
        if (Test-Path $sniPath) {
            Write-Host "Found SNI DLL at: $sniPath" -ForegroundColor Green
            $dllInfo = Get-Item $sniPath
            Write-Host "DLL Size: $($dllInfo.Length) bytes"

            $destFolder = Join-Path $targetBase "$arch\native"
            New-Item -ItemType Directory -Path $destFolder -Force | Out-Null
            Copy-Item $sniPath -Destination $destFolder -Force

            Write-Host "Copied to: $destFolder" -ForegroundColor Cyan
        } else {
            Write-Warning "Missing: $sniPath"
        }
    }

} else {
    Write-Host "ERROR: SNI DLL not found at expected location: $sniPath" -ForegroundColor Red
    # Check if runtimes folder exists at all
    if (Test-Path (Join-Path $libPath "core\lib\runtimes")) {
        Write-Host "Runtimes folder exists. Contents:"
        Get-ChildItem -Path (Join-Path $libPath "core\lib\runtimes") -Recurse | Select-Object FullName
    } else {
        Write-Host "ERROR: Runtimes folder not found in output!" -ForegroundColor Red
    }
}

# Run tests specifically for dbatools.Tests
# dotnet test dbatools.Tests/dbatools.Tests.csproj --framework net472 --verbosity normal --no-restore --nologo | Out-String -OutVariable test
Pop-Location

Remove-Item -Path (Join-Path $libPath "desktop/net472") -Recurse -ErrorAction SilentlyContinue
Remove-Item -Path (Join-Path $libPath "core/net8.0") -Recurse -ErrorAction SilentlyContinue

$tempdir = Join-Path ([System.IO.Path]::GetTempPath()) "dbatools-build"

# Create all required directories
$null = New-Item -ItemType Directory $tempdir -Force -ErrorAction Ignore
$null = New-Item -ItemType Directory (Join-Path $tempPath "dacfull") -Force -ErrorAction Ignore
$null = New-Item -ItemType Directory (Join-Path $tempPath "xe") -Force -ErrorAction Ignore
$null = New-Item -ItemType Directory (Join-Path $tempPath "linux") -Force -ErrorAction Ignore
$null = New-Item -ItemType Directory (Join-Path $tempPath "mac") -Force -ErrorAction Ignore
$null = New-Item -ItemType Directory (Join-Path $libPath "desktop/third-party/XESmartTarget") -Force
$null = New-Item -ItemType Directory (Join-Path $libPath "desktop/third-party/bogus") -Force
$null = New-Item -ItemType Directory (Join-Path $libPath "desktop/third-party/LumenWorks") -Force
$null = New-Item -ItemType Directory (Join-Path $libPath "core/lib/dac/windows") -Force
$null = New-Item -ItemType Directory (Join-Path $libPath "core/third-party/XESmartTarget") -Force
$null = New-Item -ItemType Directory (Join-Path $libPath "core/third-party/bogus") -Force
$null = New-Item -ItemType Directory (Join-Path $libPath "core/third-party/LumenWorks") -Force
$null = New-Item -ItemType Directory (Join-Path $libPath "core/lib/dac/mac") -Force
$null = New-Item -ItemType Directory (Join-Path $libPath "core/lib/dac/linux") -Force
$null = New-Item -ItemType Directory (Join-Path $libPath "core/lib/runtimes") -Force
$null = New-Item -ItemType Directory (Join-Path $tempPath "bogus") -Force
$null = New-Item -ItemType Directory (Join-Path $tempdir "nuget") -Force

# Download all required packages
Invoke-WebRequest -Uri https://aka.ms/dacfx-msi -OutFile (Join-Path $tempPath "DacFramework.msi")
Invoke-WebRequest -Uri https://www.nuget.org/api/v2/package/Bogus -OutFile (Join-Path $tempPath "bogus.zip")
Invoke-WebRequest -Uri https://www.nuget.org/api/v2/package/LumenWorksCsvReader -OutFile (Join-Path $tempPath "LumenWorksCsvReader.zip")
Invoke-WebRequest -Uri https://github.com/spaghettidba/XESmartTarget/releases/download/v1.5.7/XESmartTarget_x64.msi -OutFile (Join-Path $tempPath "XESmartTarget_x64.msi")
Invoke-WebRequest -Uri https://github.com/spaghettidba/XESmartTarget/releases/download/2.0.4.0/XESmartTarget-linux-2.0.4.0.zip -OutFile (Join-Path $tempPath "XESmartTarget-linux.zip")
Invoke-WebRequest -Uri https://aka.ms/sqlpackage-linux -OutFile (Join-Path $tempPath "sqlpackage-linux.zip")
Invoke-WebRequest -Uri https://aka.ms/sqlpackage-macos -OutFile (Join-Path $tempPath "sqlpackage-macos.zip")

$ProgressPreference = "Continue"

# Extract all packages
7z x (Join-Path $tempPath "LumenWorksCsvReader.zip") "-o$(Join-Path $tempPath "LumenWorksCsvReader")" -y
7z x (Join-Path $tempPath "bogus.zip") "-o$(Join-Path $tempPath "bogus")" -y
7z x (Join-Path $tempPath "XESmartTarget-linux.zip") "-o$(Join-Path $tempPath "xe-linux")" -y
7z x (Join-Path $tempPath "sqlpackage-linux.zip") "-o$(Join-Path $tempPath "linux")" -y
7z x (Join-Path $tempPath "sqlpackage-macos.zip") "-o$(Join-Path $tempPath "mac")" -y

# Install DacFramework MSI with proper waiting
Write-Host "Installing DacFramework MSI..."
$dacMsiPath = Resolve-Path (Join-Path $tempPath "DacFramework.msi")
$dacTargetDir = Resolve-Path (Join-Path $tempPath "dacfull")
$dacProcess = Start-Process -FilePath "msiexec.exe" -ArgumentList "/a", "`"$dacMsiPath`"", "/qb", "TARGETDIR=`"$dacTargetDir`"" -PassThru -Wait
$dacExitCode = $dacProcess.ExitCode
Write-Host "DacFramework MSI installation completed with exit code: $dacExitCode"
if ($dacExitCode -ne 0) {
    Write-Warning "DacFramework MSI installation may have failed with exit code: $dacExitCode"
}

# Small delay to ensure resources are released
Start-Sleep -Seconds 2

# Install XESmartTarget MSI with proper waiting
Write-Host "Installing XESmartTarget MSI..."
$xeTargetDir = Resolve-Path (Join-Path $tempPath "xe")
$xeMsiPath = Resolve-Path (Join-Path $tempPath "XESmartTarget_x64.msi")
$xeProcess = Start-Process -FilePath "msiexec.exe" -ArgumentList "/a", "`"$xeMsiPath`"", "/qb", "TARGETDIR=`"$xeTargetDir`"" -PassThru -Wait
$xeExitCode = $xeProcess.ExitCode
Write-Host "XESmartTarget MSI installation completed with exit code: $xeExitCode"
if ($xeExitCode -ne 0) {
    Write-Warning "XESmartTarget MSI installation may have failed with exit code: $xeExitCode"
}

# Copy XESmartTarget preserving structure
robocopy (Join-Path $tempPath "xe/XESmartTarget") (Join-Path $libPath "desktop/third-party/XESmartTarget") /E /NFL /NDL /NJH /NJS /nc /ns /np
robocopy (Join-Path $tempPath "xe/XESmartTarget") (Join-Path $libPath "core/third-party/XESmartTarget") /E /NFL /NDL /NJH /NJS /nc /ns /np
$robocopyExitCode = $LASTEXITCODE
Write-Host "Robocopy exit code: $robocopyExitCode"
if ($robocopyExitCode -ge 8) {
    Write-Host "Robocopy failed with exit code $robocopyExitCode" -ForegroundColor Red
    exit $robocopyExitCode
} elseif ($robocopyExitCode -eq 0) {
    Write-Host "Robocopy: No files were copied (source and destination are in sync)" -ForegroundColor Yellow
} else {
    Write-Host "Robocopy completed successfully (exit code $robocopyExitCode means files were copied)" -ForegroundColor Green
}
# Reset exit code to 0 for successful robocopy operations
$LASTEXITCODE = 0

# Copy Linux XESmartTarget
Copy-Item (Join-Path $tempPath "xe-linux/*") -Destination (Join-Path $libPath "desktop/third-party/XESmartTarget/") -Recurse -Force
Copy-Item (Join-Path $tempPath "xe-linux/*") -Destination (Join-Path $libPath "core/third-party/XESmartTarget/") -Recurse -Force

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

# Copy LumenWorks files for both frameworks
Copy-Item (Join-Path $tempPath "LumenWorksCsvReader/lib/net461/LumenWorks.Framework.IO.dll") -Destination (Join-Path $libPath "desktop/third-party/LumenWorks/LumenWorks.Framework.IO.dll") -Force
Copy-Item (Join-Path $tempPath "LumenWorksCsvReader/lib/netstandard2.0/LumenWorks.Framework.IO.dll") -Destination (Join-Path $libPath "core/third-party/LumenWorks/LumenWorks.Framework.IO.dll") -Force

# Copy ALL SqlPackage files for each platform
Write-Host "Copying SqlPackage files for all platforms..." -ForegroundColor Green

# Windows - Copy ALL files from DAC bin directory
$dacPath = Join-Path (Join-Path $tempPath "dacfull") "Microsoft SQL Server\170\DAC\bin"
if (Test-Path $dacPath) {
    Write-Host "Copying ALL SqlPackage files for Windows..." -ForegroundColor Green
    Copy-Item (Join-Path $dacPath "*") -Destination (Join-Path $libPath "core/lib/dac/windows/") -Recurse -Force
} else {
    Write-Warning "Windows DAC path not found: $dacPath"
}

# Linux - Copy ALL files from temp/linux
if (Test-Path (Join-Path $tempPath "linux")) {
    Write-Host "Copying ALL SqlPackage files for Linux..." -ForegroundColor Green
    Copy-Item (Join-Path $tempPath "linux/*") -Destination (Join-Path $libPath "core/lib/dac/linux/") -Recurse -Force

    # Fix SqlPackage naming for Linux - rename lowercase to uppercase if it exists
    $linuxSqlPackageLowerCase = Join-Path $libPath "core/lib/dac/linux/sqlpackage"
    $linuxSqlPackageUpperCase = Join-Path $libPath "core/lib/dac/linux/SqlPackage"
    if (Test-Path $linuxSqlPackageLowerCase) {
        Write-Host "Renaming lowercase 'sqlpackage' to 'SqlPackage' for Linux compatibility"
        Move-Item $linuxSqlPackageLowerCase $linuxSqlPackageUpperCase -Force
    }
} else {
    Write-Warning "Linux SqlPackage path not found: $(Join-Path $tempPath "linux")"
}

# macOS - Copy ALL files from temp/mac
if (Test-Path (Join-Path $tempPath "mac")) {
    Write-Host "Copying ALL SqlPackage files for macOS..." -ForegroundColor Green
    Copy-Item (Join-Path $tempPath "mac/*") -Destination (Join-Path $libPath "core/lib/dac/mac/") -Recurse -Force
} else {
    Write-Warning "macOS SqlPackage path not found: $(Join-Path $tempPath "mac")"
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

# Create private directory for assembly loading scripts
$null = New-Item -ItemType Directory -Path "./private" -Force -ErrorAction SilentlyContinue

# Copy assembly loading scripts to private directory
if (Test-Path (Join-Path $root "private")) {
    Copy-Item -Path (Join-Path $root "private\*") -Destination "./private/" -Force -ErrorAction SilentlyContinue
    Write-Host "Copied assembly loading scripts to private directory"
}

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

# Copy private folder with assembly loading scripts
if (Test-Path (Join-Path $root "private")) {
    Copy-Item -Path (Join-Path $root "private") -Destination $dbatoolsLibraryDir -Recurse -Force
    Write-Host "Included private folder in artifacts/dbatools.library" -ForegroundColor Green
}

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
Remove-Item -Path $zipPath -Force -ErrorAction SilentlyContinue

# Copy third-party-licenses
if (Test-Path (Join-Path $root "var\third-party-licenses")) {
    Copy-Item -Path (Join-Path $root "var\third-party-licenses") -Destination $tempReleaseDir -Recurse -Force
} elseif (Test-Path (Join-Path $artifactsDir "third-party-licenses")) {
    Copy-Item -Path (Join-Path $artifactsDir "third-party-licenses") -Destination $tempReleaseDir -Recurse -Force
}

Write-Host "All build artifacts are centralized in: $artifactsDir" -ForegroundColor Green

# Create the zip file directly from the artifacts/dbatools.library directory
Push-Location $artifactsDir
if (-not $NoZip) {
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