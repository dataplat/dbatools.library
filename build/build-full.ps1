$PSDefaultParameterValues["*:Force"] = $true
$PSDefaultParameterValues["*:Confirm"] = $false
Push-Location C:\github\dbatools.library\

# Silently clean up previous build artifacts
# Clean up previous build artifacts
if (Test-Path "C:\github\dbatools.library\lib") {
    Remove-Item -Path lib -Recurse -ErrorAction SilentlyContinue
    Remove-Item -Path temp -Recurse -ErrorAction SilentlyContinue
    Remove-Item -Path third-party -Recurse -ErrorAction SilentlyContinue
    Remove-Item -Path third-party-licenses -Recurse -ErrorAction SilentlyContinue
}

$scriptroot = $PSScriptRoot
if (-not $scriptroot) {
    $scriptroot = "C:\github\dbatools.library\build"
}
$root = Split-Path -Path $scriptroot
Push-Location "$root\project"

# Publish .NET Framework (desktop)
dotnet publish dbatools/dbatools.csproj --configuration release --framework net472 --output C:\github\dbatools.library\lib\desktop --nologo | Out-String -OutVariable build

# Publish .NET 8 (core)
dotnet publish dbatools/dbatools.csproj --configuration release --framework net8.0 --output C:\github\dbatools.library\lib\core --nologo | Out-String -OutVariable build

# Run tests specifically for dbatools.Tests
dotnet test dbatools.Tests/dbatools.Tests.csproj --framework net472 --verbosity normal --no-restore --nologo | Out-String -OutVariable test
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
$null = New-Item -ItemType Directory ./third-party/XESmartTarget -Force
$null = New-Item -ItemType Directory ./third-party/bogus -Force
$null = New-Item -ItemType Directory ./third-party/bogus/core -Force
$null = New-Item -ItemType Directory ./third-party/bogus/desktop -Force
$null = New-Item -ItemType Directory ./third-party/LumenWorks -Force
$null = New-Item -ItemType Directory ./third-party/LumenWorks/core -Force
$null = New-Item -ItemType Directory ./third-party/LumenWorks/desktop -Force
$null = New-Item -ItemType Directory ./lib/win -Force
$null = New-Item -ItemType Directory ./lib/win-sqlclient -Force
$null = New-Item -ItemType Directory ./lib/win-sqlclient-x86 -Force
$null = New-Item -ItemType Directory ./lib/mac -Force
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
7z x .\temp\LumenWorksCsvReader.zip "-o.\temp\LumenWorksCsvReader"
7z x .\temp\bogus.zip "-o.\temp\bogus"
7z x .\temp\sqlpackage-linux.zip "-o.\temp\linux"
7z x .\temp\sqlpackage-macos.zip "-o.\temp\mac"

msiexec /a $(Resolve-Path .\temp\DacFramework.msi) /qb TARGETDIR=$(Resolve-Path .\temp\dacfull)
Start-Sleep 3
msiexec /a $(Resolve-Path .\temp\XESmartTarget_x64.msi) /qb TARGETDIR=$(Resolve-Path .\temp\xe)
Start-Sleep 3

# Copy XESmartTarget preserving structure
robocopy ./temp/xe/XESmartTarget ./third-party/XESmartTarget /E /NFL /NDL /NJH /NJS /nc /ns /np

# Copy Bogus files for both frameworks
Copy-Item "./temp/bogus/lib/net40/bogus.dll" -Destination "./third-party/bogus/desktop/bogus.dll" -Force
Copy-Item "./temp/bogus/lib/net6.0/bogus.dll" -Destination "./third-party/bogus/core/bogus.dll" -Force

# Copy LumenWorks files for both frameworks
Copy-Item "./temp/LumenWorksCsvReader/lib/net461/LumenWorks.Framework.IO.dll" -Destination "./third-party/LumenWorks/desktop/LumenWorks.Framework.IO.dll" -Force
Copy-Item "./temp/LumenWorksCsvReader/lib/netstandard2.0/LumenWorks.Framework.IO.dll" -Destination "./third-party/LumenWorks/core/LumenWorks.Framework.IO.dll" -Force

# Copy DAC files based on architecture
$dacPath = ".\temp\dacfull\Microsoft SQL Server\160\DAC\bin"
if ($env:PROCESSOR_ARCHITECTURE -eq "x86") {
    Copy-Item "$dacPath\Microsoft.SqlServer.Types.dll" -Destination "./lib/win-sqlclient-x86/" -Force
    Copy-Item "$dacPath\Microsoft.Data.Tools.Schema.Sql.dll" -Destination "./lib/win-sqlclient-x86/" -Force
    Copy-Item "$dacPath\Microsoft.Data.Tools.Utilities.dll" -Destination "./lib/win-sqlclient-x86/" -Force
    Copy-Item "$dacPath\Microsoft.Data.SqlClient.*" -Destination "./lib/win-sqlclient-x86/" -Force
} else {
    Copy-Item "$dacPath\Microsoft.SqlServer.Types.dll" -Destination "./lib/win-sqlclient/" -Force
    Copy-Item "$dacPath\Microsoft.Data.Tools.Schema.Sql.dll" -Destination "./lib/win-sqlclient/" -Force
    Copy-Item "$dacPath\Microsoft.Data.Tools.Utilities.dll" -Destination "./lib/win-sqlclient/" -Force
    Copy-Item "$dacPath\Microsoft.Data.SqlClient.*" -Destination "./lib/win-sqlclient/" -Force
}

# Copy Linux files
$linux = @(
    'libclrjit.so', 'libcoreclr.so', 'libhostfxr.so', 'libhostpolicy.so', 'libSystem.Native.so',
    'libSystem.Security.Cryptography.Native.OpenSsl.so', 'Microsoft.Win32.Primitives.dll',
    'sqlpackage', 'sqlpackage.deps.json', 'sqlpackage.dll', 'sqlpackage.runtimeconfig.json',
    'System.Collections.Concurrent.dll', 'System.Collections.dll', 'System.Console.dll',
    'System.Diagnostics.FileVersionInfo.dll', 'System.Diagnostics.TraceSource.dll', 'System.Linq.dll',
    'System.Memory.dll', 'System.Private.CoreLib.dll', 'System.Private.Xml.dll',
    'System.Reflection.Metadata.dll', 'System.Runtime.dll',
    'System.Security.Cryptography.Algorithms.dll', 'System.Security.Cryptography.Primitives.dll',
    'System.Threading.dll', 'System.Threading.Thread.dll', 'System.Xml.ReaderWriter.dll'
)

$sqlp = Get-ChildItem ./temp/linux/* -Exclude (Get-ChildItem lib -Recurse) | Where-Object Name -in $linux
Copy-Item -Path $sqlp.FullName -Destination ./lib/

# Copy Mac files
Copy-Item "./temp/mac/*" -Destination "./lib/mac/" -Recurse -Force

# Copy other framework-specific files
Get-ChildItem .\temp\dacfull\* -Include *.dll, *.exe, *.config -Exclude (Get-ChildItem .\lib -Recurse) -Recurse | Copy-Item -Destination ./lib/desktop -Force

Copy-Item "./var/misc/core/*.dll" -Destination ./lib/core -Force
Copy-Item "./var/misc/both/*.dll" -Destination ./lib -Force
Copy-Item "./var/third-party-licenses" -Destination ./ -Recurse -Force

Remove-Item -Path lib/*.xml -Recurse -ErrorAction Ignore
Remove-Item -Path lib/*.pdb -Recurse -ErrorAction Ignore

# Set executable permissions for Linux/Mac
if ($isLinux -or $IsMacOs) {
    chmod +x ./lib/sqlpackage
    chmod +x ./lib/mac/sqlpackage
}


Get-ChildItem .\lib -Recurse -Include *.pdb | Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem .\lib -Recurse -Include *.xml | Remove-Item -Force
Get-ChildItem .\lib\*\dbatools.deps.json -Recurse | Remove-Item -Force

return
# Remove existing gallery location if it exists
if ((Test-Path -Path C:\gallery\dbatools.library)) {
    Remove-Item C:\gallery\dbatools.library -Recurse -Force
}

# Create gallery directory structure
$null = New-Item -ItemType Directory -Path C:\gallery\dbatools.library -Force
$null = New-Item -ItemType Directory -Path C:\gallery\dbatools.library\desktop -Force
$null = New-Item -ItemType Directory -Path C:\gallery\dbatools.library\desktop\lib -Force

# Copy module files and other content
$null = robocopy c:\github\dbatools.library C:\gallery\dbatools.library /S /XF actions-build.ps1 .markdownlint.json *.psproj* *.git* *.yml *.md dac.ps1 build*.ps1 dbatools-core*.* /XD .git .github Tests .vscode project temp runtime runtimes replication var opt

# Clean up and copy module files
Remove-Item c:\gallery\dbatools.library\dac.ps1 -ErrorAction Ignore
Remove-Item c:\gallery\dbatools.library\dbatools.core.library.psd1 -ErrorAction Ignore
Copy-Item C:\github\dbatools.library\dbatools.library.psd1 C:\gallery\dbatools.library -Force

# Move third-party and lib directories to desktop folder
Move-Item C:\github\dbatools.library\third-party C:\gallery\dbatools.library\desktop\third-party -Force
Move-Item C:\github\dbatools.library\lib C:\gallery\dbatools.library\desktop\lib -Force

# Verify required files exist
$requiredFiles = @(
    "C:\gallery\dbatools.library\dbatools.library.psd1",
    "C:\gallery\dbatools.library\dbatools.library.psm1",
    "C:\gallery\dbatools.library\desktop\lib\Microsoft.Data.SqlClient.dll",
    "C:\gallery\dbatools.library\desktop\lib\Microsoft.SqlServer.Smo.dll",
    "C:\gallery\dbatools.library\desktop\lib\Microsoft.Identity.Client.dll"
)

foreach ($file in $requiredFiles) {
    if (-not (Test-Path $file)) {
        Write-Error "Required file not found: $file"
        exit 1
    }
}

Import-Module C:\gallery\dbatools.library\dbatools.library.psd1 -Force

Pop-Location