$PSDefaultParameterValues["*:Force"] = $true
$PSDefaultParameterValues["*:Confirm"] = $false

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

# Clean up previous build artifacts
$libPath = Join-Path $root "lib"
$tempPath = Join-Path $root "temp"
$licensePath = Join-Path $root "third-party-licenses"

if (Test-Path $libPath) {
    Remove-Item -Path $libPath -Recurse -ErrorAction SilentlyContinue
}
if (Test-Path $tempPath) {
    Remove-Item -Path $tempPath -Recurse -ErrorAction SilentlyContinue
}
if (Test-Path $licensePath) {
    Remove-Item -Path $licensePath -Recurse -ErrorAction SilentlyContinue
}
Push-Location "$root\project"

dotnet clean
# Publish .NET Framework (desktop)
Write-Host "Publishing .NET Framework build..."
dotnet publish dbatools/dbatools.csproj --configuration release --framework net472 --output (Join-Path $root "lib\desktop") --nologo --self-contained true | Out-String -OutVariable build

# Verify desktop publish results
Write-Host "Verifying desktop publish..."
if (Test-Path (Join-Path $root "lib\desktop\Microsoft.Data.SqlClient.SNI.x64.dll")) {
    Write-Host "Found SNI x64 DLL in desktop output"
    $dllInfo = Get-Item (Join-Path $root "lib\desktop\Microsoft.Data.SqlClient.SNI.x64.dll")
    Write-Host "DLL Size: $($dllInfo.Length) bytes"
} else {
    Write-Host "WARNING: SNI x64 DLL not found in desktop output"
}

# Publish .NET 8 (core)
dotnet publish dbatools/dbatools.csproj --configuration release --framework net8.0 --output (Join-Path $root "lib\core") --nologo --self-contained true | Out-String -OutVariable build

# Run tests specifically for dbatools.Tests
# dotnet test dbatools.Tests/dbatools.Tests.csproj --framework net472 --verbosity normal --no-restore --nologo | Out-String -OutVariable test
Pop-Location

Remove-Item -Path lib/net472 -Recurse -ErrorAction SilentlyContinue
Remove-Item -Path lib/net8.0 -Recurse -ErrorAction SilentlyContinue

$tempdir = Join-Path $env:TEMP "dbatools-build"

# Create all required directories
$null = New-Item -ItemType Directory $tempdir -Force -ErrorAction Ignore
$null = New-Item -ItemType Directory ./temp/dacfull -Force -ErrorAction Ignore
$null = New-Item -ItemType Directory ./temp/xe -Force -ErrorAction Ignore
$null = New-Item -ItemType Directory ./temp/linux -Force -ErrorAction Ignore
$null = New-Item -ItemType Directory ./temp/mac -Force -ErrorAction Ignore
$null = New-Item -ItemType Directory ./lib/third-party/XESmartTarget -Force
$null = New-Item -ItemType Directory ./lib/third-party/bogus -Force
$null = New-Item -ItemType Directory ./lib/third-party/bogus/core -Force
$null = New-Item -ItemType Directory ./lib/third-party/bogus/desktop -Force
$null = New-Item -ItemType Directory ./lib/third-party/LumenWorks -Force
$null = New-Item -ItemType Directory ./lib/third-party/LumenWorks/core -Force
$null = New-Item -ItemType Directory ./lib/third-party/LumenWorks/desktop -Force
$null = New-Item -ItemType Directory ./lib/win-dac -Force
$null = New-Item -ItemType Directory ./lib/mac-dac -Force
$null = New-Item -ItemType Directory ./lib/linux-dac -Force
$null = New-Item -ItemType Directory ./temp/bogus -Force

$ProgressPreference = "SilentlyContinue"

# Download all required packages
Invoke-WebRequest -Uri https://aka.ms/dacfx-msi -OutFile .\temp\DacFramework.msi
Invoke-WebRequest -Uri https://www.nuget.org/api/v2/package/Bogus -OutFile .\temp\bogus.zip
Invoke-WebRequest -Uri https://www.nuget.org/api/v2/package/LumenWorksCsvReader -OutFile .\temp\LumenWorksCsvReader.zip
Invoke-WebRequest -Uri https://github.com/spaghettidba/XESmartTarget/releases/download/v1.5.7/XESmartTarget_x64.msi -OutFile .\temp\XESmartTarget_x64.msi
Invoke-WebRequest -Uri https://github.com/spaghettidba/XESmartTarget/releases/download/2.0.4.0/XESmartTarget-linux-2.0.4.0.zip -OutFile .\temp\XESmartTarget-linux.zip
Invoke-WebRequest -Uri https://aka.ms/sqlpackage-linux -OutFile .\temp\sqlpackage-linux.zip
Invoke-WebRequest -Uri https://aka.ms/sqlpackage-macos -OutFile .\temp\sqlpackage-macos.zip

$ProgressPreference = "Continue"

# Extract all packages
7z x .\temp\LumenWorksCsvReader.zip "-o.\temp\LumenWorksCsvReader" -y
7z x .\temp\bogus.zip "-o.\temp\bogus" -y
7z x .\temp\XESmartTarget-linux.zip "-o.\temp\xe-linux" -y
7z x .\temp\sqlpackage-linux.zip "-o.\temp\linux" -y
7z x .\temp\sqlpackage-macos.zip "-o.\temp\mac" -y

# Install DacFramework MSI with proper waiting
Write-Host "Installing DacFramework MSI..."
$dacMsiPath = Resolve-Path .\temp\DacFramework.msi
$dacTargetDir = Resolve-Path .\temp\dacfull
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
$xeTargetDir = Resolve-Path .\temp\xe
$xeMsiPath = Resolve-Path .\temp\XESmartTarget_x64.msi
$xeProcess = Start-Process -FilePath "msiexec.exe" -ArgumentList "/a", "`"$xeMsiPath`"", "/qb", "TARGETDIR=`"$xeTargetDir`"" -PassThru -Wait
$xeExitCode = $xeProcess.ExitCode
Write-Host "XESmartTarget MSI installation completed with exit code: $xeExitCode"
if ($xeExitCode -ne 0) {
    Write-Warning "XESmartTarget MSI installation may have failed with exit code: $xeExitCode"
}

# Copy XESmartTarget preserving structure
robocopy ./temp/xe/XESmartTarget ./lib/third-party/XESmartTarget /E /NFL /NDL /NJH /NJS /nc /ns /np
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
Copy-Item "./temp/xe-linux/*" -Destination "./lib/third-party/XESmartTarget/" -Recurse -Force

# Copy Bogus files for both frameworks
Copy-Item "./temp/bogus/lib/net40/bogus.dll" -Destination "./lib/third-party/bogus/desktop/bogus.dll" -Force
Copy-Item "./temp/bogus/lib/net6.0/bogus.dll" -Destination "./lib/third-party/bogus/core/bogus.dll" -Force

# Copy LumenWorks files for both frameworks
Copy-Item "./temp/LumenWorksCsvReader/lib/net461/LumenWorks.Framework.IO.dll" -Destination "./lib/third-party/LumenWorks/desktop/LumenWorks.Framework.IO.dll" -Force
Copy-Item "./temp/LumenWorksCsvReader/lib/netstandard2.0/LumenWorks.Framework.IO.dll" -Destination "./lib/third-party/LumenWorks/core/LumenWorks.Framework.IO.dll" -Force

# Copy DAC files based on architecture
$dacPath = Join-Path ".\temp\dacfull" "Microsoft SQL Server\170\DAC\bin"

# Copy DAC files for each platform
# Windows
Copy-Item (Join-Path $dacPath "Microsoft.SqlServer.Dac.dll") -Destination "./lib/win-dac/" -Force
Copy-Item (Join-Path $dacPath "Microsoft.SqlServer.Dac.Extensions.dll") -Destination "./lib/win-dac/" -Force
Copy-Item (Join-Path $dacPath "Microsoft.Data.Tools.Schema.Sql.dll") -Destination "./lib/win-dac/" -Force
Copy-Item (Join-Path $dacPath "Microsoft.SqlServer.TransactSql.ScriptDom.dll") -Destination "./lib/win-dac/" -Force
# Copy SqlPackage.exe for Windows
Copy-Item (Join-Path $dacPath "SqlPackage.exe") -Destination "./lib/win-dac/" -Force

# Linux
Copy-Item "./temp/linux/Microsoft.SqlServer.Dac*" -Destination "./lib/linux-dac/" -Force
Copy-Item "./temp/linux/Microsoft.Data.Tools*" -Destination "./lib/linux-dac/" -Force
# Copy sqlpackage executable for Linux (it's a .NET app launcher)
if (Test-Path "./temp/linux/sqlpackage") {
    Copy-Item "./temp/linux/sqlpackage" -Destination "./lib/linux-dac/" -Force
} else {
    # Create a launcher script if sqlpackage doesn't exist
    $sqlpackageScript = @'
#!/usr/bin/env bash
DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
exec dotnet "$DIR/sqlpackage.dll" "$@"
'@
    Set-Content -Path "./lib/linux-dac/sqlpackage" -Value $sqlpackageScript -Force
    # Note: chmod +x will need to be done on Linux
}
# Copy sqlpackage.dll for Linux
Copy-Item "./temp/linux/sqlpackage.dll" -Destination "./lib/linux-dac/" -Force -ErrorAction SilentlyContinue

# macOS
Copy-Item "./temp/mac/Microsoft.SqlServer.Dac*" -Destination "./lib/mac-dac/" -Force
Copy-Item "./temp/mac/Microsoft.Data.Tools*" -Destination "./lib/mac-dac/" -Force
# Copy sqlpackage executable for macOS
if (Test-Path "./temp/mac/sqlpackage") {
    Copy-Item "./temp/mac/sqlpackage" -Destination "./lib/mac-dac/" -Force
} else {
    # Create a launcher script if sqlpackage doesn't exist
    $sqlpackageScript = @'
#!/usr/bin/env bash
DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
exec dotnet "$DIR/sqlpackage.dll" "$@"
'@
    Set-Content -Path "./lib/mac-dac/sqlpackage" -Value $sqlpackageScript -Force
    # Note: chmod +x will need to be done on macOS
}
# Copy sqlpackage.dll for macOS
Copy-Item "./temp/mac/sqlpackage.dll" -Destination "./lib/mac-dac/" -Force -ErrorAction SilentlyContinue

# Core files are already in place from dotnet publish

# Copy var/misc files to appropriate locations
Write-Host "Copying additional assemblies from var/misc..."
# Copy files that go to both core and desktop
Get-ChildItem "./var/misc/both" -Filter "*.dll" | ForEach-Object {
    Copy-Item $_.FullName -Destination "./lib/core/" -Force
    Copy-Item $_.FullName -Destination "./lib/desktop/" -Force
}

# Copy desktop-specific files
Get-ChildItem "./var/misc/desktop" -Filter "*.dll" | Copy-Item -Destination "./lib/desktop/" -Force

# Cleanup temporary files and artifacts
Remove-Item -Path "./temp" -Recurse -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path "./lib" -Recurse -Include "*.pdf","*.xml" | Remove-Item -Force

# Create private directory for assembly loading scripts
$null = New-Item -ItemType Directory -Path "./private" -Force -ErrorAction SilentlyContinue

# Ensure System.Runtime.CompilerServices.Unsafe is in place
$nugetCache = "$env:USERPROFILE\.nuget\packages"; Get-ChildItem -Path "$nugetCache\system.runtime.compilerservices.unsafe\*\lib\net6.0\System.Runtime.CompilerServices.Unsafe.dll" -Recurse | Select-Object -Last 1 | Copy-Item -Destination (Join-Path $root "lib\core\") -PassThru | Out-Null
# Remove lib/release folder
Remove-Item -Path "./lib/release" -Recurse -Force -ErrorAction SilentlyContinue

# Create zip file for testing (release format)
Write-Host "Creating dbatools.library.zip for testing..."
$zipPath = Join-Path $root "dbatools.library.zip"
Remove-Item -Path $zipPath -Force -ErrorAction SilentlyContinue

# Create a temporary directory with the release structure
$tempReleaseDir = Join-Path $tempdir "dbatools.library"
$null = New-Item -ItemType Directory -Path $tempReleaseDir -Force

# Copy root module files
Copy-Item -Path (Join-Path $root "dbatools.library.psd1") -Destination $tempReleaseDir -Force
Copy-Item -Path (Join-Path $root "dbatools.library.psm1") -Destination $tempReleaseDir -Force
Copy-Item -Path (Join-Path $root "LICENSE") -Destination $tempReleaseDir -Force -ErrorAction SilentlyContinue

# Create core structure
$coreDir = Join-Path $tempReleaseDir "core"
$null = New-Item -ItemType Directory -Path $coreDir -Force
$null = New-Item -ItemType Directory -Path (Join-Path $coreDir "lib") -Force

# Copy core assemblies
Copy-Item -Path (Join-Path $root "lib\core\*") -Destination (Join-Path $coreDir "lib") -Recurse -Force

# Move third-party to core root
if (Test-Path (Join-Path $root "lib\third-party")) {
    Copy-Item -Path (Join-Path $root "lib\third-party") -Destination $coreDir -Recurse -Force
}

# Create platform-specific folders in core/lib
$null = New-Item -ItemType Directory -Path (Join-Path $coreDir "lib\mac") -Force
$null = New-Item -ItemType Directory -Path (Join-Path $coreDir "lib\win") -Force
$null = New-Item -ItemType Directory -Path (Join-Path $coreDir "lib\win-sqlclient") -Force

# Copy mac-specific DAC files to core/lib/mac if they exist
if (Test-Path (Join-Path $root "lib\mac-dac")) {
    Copy-Item -Path (Join-Path $root "lib\mac-dac\*") -Destination (Join-Path $coreDir "lib\mac") -Force
}

# Copy win-specific DAC files to core/lib/win if they exist
if (Test-Path (Join-Path $root "lib\win-dac")) {
    Copy-Item -Path (Join-Path $root "lib\win-dac\*") -Destination (Join-Path $coreDir "lib\win") -Force
}

# Create desktop structure
$desktopDir = Join-Path $tempReleaseDir "desktop"
$null = New-Item -ItemType Directory -Path $desktopDir -Force
$null = New-Item -ItemType Directory -Path (Join-Path $desktopDir "lib") -Force

# Copy desktop assemblies
Copy-Item -Path (Join-Path $root "lib\desktop\*") -Destination (Join-Path $desktopDir "lib") -Recurse -Force

# Move third-party to desktop root
if (Test-Path (Join-Path $root "lib\third-party")) {
    Copy-Item -Path (Join-Path $root "lib\third-party") -Destination $desktopDir -Recurse -Force
}

# Copy third-party-licenses
if (Test-Path (Join-Path $root "var\third-party-licenses")) {
    Copy-Item -Path (Join-Path $root "var\third-party-licenses") -Destination $tempReleaseDir -Recurse -Force
}

# Create the zip file from the parent directory to get the right structure
Push-Location $tempdir
Compress-Archive -Path "dbatools.library" -DestinationPath $zipPath -CompressionLevel Optimal -Force
Pop-Location

# Clean up temp directory
Remove-Item -Path $tempReleaseDir -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "Created test zip: $zipPath"
Write-Host "Zip structure: dbatools.library.zip contains 'dbatools.library' folder at root"
Write-Host "For testing: Extract to a folder in `$env:PSModulePath or use Install-DbatoolsLibrary.ps1"
Write-Host "Build completed successfully. Files organized and temporary artifacts cleaned up."

# if github actions and lastexitcode =0 then exit with success
if ($env:GITHUB_ACTIONS -and $LASTEXITCODE -eq 0) {
    exit 0
} else {
    exit $LASTEXITCODE
}