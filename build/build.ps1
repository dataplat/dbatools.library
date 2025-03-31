$PSDefaultParameterValues["*:Force"] = $true
$PSDefaultParameterValues["*:Confirm"] = $false
Push-Location C:\github\dbatools.library\

# Silently clean up previous build artifacts
# Clean up previous build artifacts
if (Test-Path "C:\github\dbatools.library\lib") {
    Remove-Item -Path lib -Recurse -ErrorAction SilentlyContinue
    Remove-Item -Path temp -Recurse -ErrorAction SilentlyContinue
    Remove-Item -Path third-party-licenses -Recurse -ErrorAction SilentlyContinue
}

$scriptroot = $PSScriptRoot
if (-not $scriptroot) {
    $scriptroot = "C:\github\dbatools.library\build"
}
$root = Split-Path -Path $scriptroot
Push-Location "$root\project"

dotnet clean
# Publish .NET Framework (desktop)
Write-Host "Publishing .NET Framework build..."
dotnet publish dbatools/dbatools.csproj --configuration release --framework net472 --output C:\github\dbatools.library\lib\desktop --nologo | Out-String -OutVariable build

# Verify desktop publish results
Write-Host "Verifying desktop publish..."
if (Test-Path "C:\github\dbatools.library\lib\desktop\Microsoft.Data.SqlClient.SNI.x64.dll") {
    Write-Host "Found SNI x64 DLL in desktop output"
    $dllInfo = Get-Item "C:\github\dbatools.library\lib\desktop\Microsoft.Data.SqlClient.SNI.x64.dll"
    Write-Host "DLL Size: $($dllInfo.Length) bytes"
} else {
    Write-Host "WARNING: SNI x64 DLL not found in desktop output"
}

# Publish .NET 8 (core)
dotnet publish dbatools/dbatools.csproj --configuration release --framework net8.0 --output C:\github\dbatools.library\lib\core --nologo | Out-String -OutVariable build

# Run tests specifically for dbatools.Tests
# dotnet test dbatools.Tests/dbatools.Tests.csproj --framework net472 --verbosity normal --no-restore --nologo | Out-String -OutVariable test
Pop-Location

Remove-Item -Path lib/net472 -Recurse -ErrorAction SilentlyContinue
Remove-Item -Path lib/net8.0 -Recurse -ErrorAction SilentlyContinue

$tempdir = "C:\temp"

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
$null = New-Item -ItemType Directory ./lib/win-sqlclient -Force
$null = New-Item -ItemType Directory ./lib/win-sqlclient-x86 -Force
$null = New-Item -ItemType Directory ./lib/mac-dac -Force
$null = New-Item -ItemType Directory ./lib/linux-dac -Force
$null = New-Item -ItemType Directory ./lib/common -Force
$null = New-Item -ItemType Directory ./temp/bogus -Force

$ProgressPreference = "SilentlyContinue"

# Download all required packages
Invoke-WebRequest -Uri https://aka.ms/dacfx-msi -OutFile .\temp\DacFramework.msi
Invoke-WebRequest -Uri https://www.nuget.org/api/v2/package/Bogus -OutFile .\temp\bogus.zip
Invoke-WebRequest -Uri https://www.nuget.org/api/v2/package/LumenWorksCsvReader -OutFile .\temp\LumenWorksCsvReader.zip
Invoke-WebRequest -Uri https://github.com/spaghettidba/XESmartTarget/releases/download/v1.5.7/XESmartTarget_x64.msi -OutFile .\temp\XESmartTarget_x64.msi
Invoke-WebRequest -Uri https://aka.ms/sqlpackage-linux -OutFile .\temp\sqlpackage-linux.zip
Invoke-WebRequest -Uri https://aka.ms/sqlpackage-macos -OutFile .\temp\sqlpackage-macos.zip

$ProgressPreference = "Continue"

# Extract all packages
7z x .\temp\LumenWorksCsvReader.zip "-o.\temp\LumenWorksCsvReader" -y
7z x .\temp\bogus.zip "-o.\temp\bogus" -y
7z x .\temp\sqlpackage-linux.zip "-o.\temp\linux" -y
7z x .\temp\sqlpackage-macos.zip "-o.\temp\mac" -y

msiexec /a $(Resolve-Path .\temp\DacFramework.msi) /qb TARGETDIR=$(Resolve-Path .\temp\dacfull)
Start-Sleep 3
msiexec /a $(Resolve-Path .\temp\XESmartTarget_x64.msi) /qb TARGETDIR=$(Resolve-Path .\temp\xe)
Start-Sleep 3

# Copy XESmartTarget preserving structure
robocopy ./temp/xe/XESmartTarget ./lib/third-party/XESmartTarget /E /NFL /NDL /NJH /NJS /nc /ns /np

# Copy Bogus files for both frameworks
Copy-Item "./temp/bogus/lib/net40/bogus.dll" -Destination "./lib/third-party/bogus/desktop/bogus.dll" -Force
Copy-Item "./temp/bogus/lib/net6.0/bogus.dll" -Destination "./lib/third-party/bogus/core/bogus.dll" -Force

# Copy LumenWorks files for both frameworks
Copy-Item "./temp/LumenWorksCsvReader/lib/net461/LumenWorks.Framework.IO.dll" -Destination "./lib/third-party/LumenWorks/desktop/LumenWorks.Framework.IO.dll" -Force
Copy-Item "./temp/LumenWorksCsvReader/lib/netstandard2.0/LumenWorks.Framework.IO.dll" -Destination "./lib/third-party/LumenWorks/core/LumenWorks.Framework.IO.dll" -Force

# Copy DAC files based on architecture
$dacPath = ".\temp\dacfull\Microsoft SQL Server\160\DAC\bin"

# Ensure lib directories exist for SqlClient native dependencies
$null = New-Item -ItemType Directory ./lib/win-sqlclient/native -Force
$null = New-Item -ItemType Directory ./lib/win-sqlclient-x86/native -Force

# Copy DAC files for each platform
# Windows
Copy-Item "$dacPath\Microsoft.SqlServer.Dac.dll" -Destination "./lib/win-dac/" -Force
Copy-Item "$dacPath\Microsoft.SqlServer.Dac.Extensions.dll" -Destination "./lib/win-dac/" -Force
Copy-Item "$dacPath\Microsoft.Data.Tools.Schema.Sql.dll" -Destination "./lib/win-dac/" -Force
Copy-Item "$dacPath\Microsoft.SqlServer.TransactSql.ScriptDom.dll" -Destination "./lib/win-dac/" -Force

# Linux and macOS
Copy-Item "./temp/linux/Microsoft.SqlServer.Dac*" -Destination "./lib/linux-dac/" -Force
Copy-Item "./temp/linux/Microsoft.Data.Tools*" -Destination "./lib/linux-dac/" -Force
Copy-Item "./temp/mac/Microsoft.SqlServer.Dac*" -Destination "./lib/mac-dac/" -Force
Copy-Item "./temp/mac/Microsoft.Data.Tools*" -Destination "./lib/mac-dac/" -Force

# Copy SQL Client files with proper native dependency handling
# x64 files
# Verify source files before copy
Write-Host "`nVerifying source files before copy..."
$sourcePath = "./lib/desktop"
$sourceFiles = @(
    "Microsoft.Data.SqlClient.dll",
    "Microsoft.Identity.Client.dll",
    "Microsoft.Identity.Client.Extensions.Msal.dll",
    "Microsoft.Data.SqlClient.SNI.x64.dll"
)
foreach ($file in $sourceFiles) {
    if (Test-Path "$sourcePath/$file") {
        $fileInfo = Get-Item "$sourcePath/$file"
        Write-Host "Found $file - Size: $($fileInfo.Length) bytes"
    } else {
        Write-Host "WARNING: Missing source file $file"
    }
}

# Use robocopy to preserve file integrity for native dependencies
Write-Host "`nCopying files to win-sqlclient..."
robocopy "./lib/desktop" "./lib/win-sqlclient" Microsoft.Data.SqlClient.dll Microsoft.Identity.Client.dll Microsoft.Identity.Client.Extensions.Msal.dll /NFL /NDL /NJH /NJS /nc /ns /np
robocopy "./lib/desktop" "./lib/win-sqlclient/native" Microsoft.Data.SqlClient.SNI.x64.dll /NFL /NDL /NJH /NJS /nc /ns /np

# Verify destination files after copy
Write-Host "`nVerifying destination files after copy..."
$destPaths = @{
    "Microsoft.Data.SqlClient.dll" = "./lib/win-sqlclient"
    "Microsoft.Identity.Client.dll" = "./lib/win-sqlclient"
    "Microsoft.Identity.Client.Extensions.Msal.dll" = "./lib/win-sqlclient"
    "Microsoft.Data.SqlClient.SNI.x64.dll" = "./lib/win-sqlclient/native"
}
foreach ($file in $destPaths.Keys) {
    $path = Join-Path $destPaths[$file] $file
    if (Test-Path $path) {
        $fileInfo = Get-Item $path
        Write-Host "Found $file in $($destPaths[$file]) - Size: $($fileInfo.Length) bytes"
    } else {
        Write-Host "WARNING: Missing destination file $path"
    }
}

# x86 files
# Use robocopy to preserve file integrity for x86 native dependencies
robocopy "./lib/desktop" "./lib/win-sqlclient-x86" Microsoft.Data.SqlClient.dll Microsoft.Identity.Client.dll Microsoft.Identity.Client.Extensions.Msal.dll /NFL /NDL /NJH /NJS /nc /ns /np
robocopy "./lib/desktop" "./lib/win-sqlclient-x86/native" Microsoft.Data.SqlClient.SNI.x86.dll /NFL /NDL /NJH /NJS /nc /ns /np

# Core files are already in place from dotnet publish

# Copy var/misc files to appropriate locations
Write-Host "Copying additional assemblies from var/misc..."
# Copy files that go to both core and desktop
Get-ChildItem "./var/misc/both" -Filter "*.dll" | ForEach-Object {
    Copy-Item $_.FullName -Destination "./lib/core/" -Force
    Copy-Item $_.FullName -Destination "./lib/desktop/" -Force
}

# Copy core-specific files
Get-ChildItem "./var/misc/core" -Filter "*.dll" | Copy-Item -Destination "./lib/core/" -Force

# Copy desktop-specific files
Get-ChildItem "./var/misc/desktop" -Filter "*.dll" | Copy-Item -Destination "./lib/desktop/" -Force

# Copy common files that are platform-independent
Get-ChildItem "./lib/desktop" -Filter "Microsoft.SqlServer.*.dll" |
    Where-Object { $_.Name -notlike "*SqlClient*" } |
    ForEach-Object {
        Copy-Item $_.FullName -Destination "./lib/common/" -Force
    }

# Cleanup temporary files and artifacts
Remove-Item -Path "./temp" -Recurse -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path "./lib" -Recurse -Include "*.pdf","*.xml" | Remove-Item -Force

# Create private directory for assembly loading scripts
$null = New-Item -ItemType Directory -Path "./private" -Force -ErrorAction SilentlyContinue

 # Ensure System.Runtime.CompilerServices.Unsafe is in place
    $nugetCache = "$env:USERPROFILE\.nuget\packages"; Get-ChildItem -Path "$nugetCache\system.runtime.compilerservices.unsafe\*\lib\net6.0\System.Runtime.CompilerServices.Unsafe.dll" -Recurse | Select-Object -Last 1 | Copy-Item -Destination "C:\github\dbatools.library\lib\core\" -PassThru | Out-Null

Write-Host "Build completed successfully. Files organized and temporary artifacts cleaned up."