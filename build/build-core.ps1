$PSDefaultParameterValues["*:Force"] = $true
$PSDefaultParameterValues["*:Confirm"] = $false
Push-Location /mnt/c/github/dbatools.library

if (Test-Path ./lib) {
    write-warning "removing ./lib"
    rm -rf lib
    rm -rf temp
    rm -rf third-party
    rm -rf third-party-licenses
}

$scriptroot = $PSScriptRoot
if (-not $scriptroot) {
    $scriptroot = "/mnt/c/github/dbatools.library/build"
}

$root = Split-Path -Path $scriptroot
Push-Location "$root/project"


dotnet publish --configuration release --framework net6.0 | Out-String -OutVariable build
dotnet test --framework net6.0 --verbosity normal | Out-String -OutVariable test
Pop-Location

Remove-Item -Path lib/dbatools.xml
Get-ChildItem -Path lib/net6.0 -File | Remove-Item
Move-Item -Path lib/net6.0/publish/* -Destination lib/ #-ErrorAction Ignore
Remove-Item -Path lib/net6.0 -Recurse -ErrorAction Ignore

Get-ChildItem ./lib -Recurse -Include *.pdb | Remove-Item
Get-ChildItem ./lib -Recurse -Include *.xml | Remove-Item
Get-ChildItem ./lib/ -Include runtimes -Recurse | Remove-Item -Recurse
Get-ChildItem ./lib/*/dbatools.deps.json -Recurse | Remove-Item

if ($IsLinux -or $IsMacOs) {
    $tempdir = "/tmp"
    $null = mkdir ./temp
    $null = mkdir ./temp/dacfull
    $null = mkdir ./temp/xe
    $null = mkdir ./third-party
    $null = mkdir ./third-party/XESmartTarget
    $null = mkdir ./third-party/bogus
    $null = mkdir ./third-party/LumenWorks
    $null = mkdir ./temp/bogus
    $null = mkdir ./temp/linux
    $null = mkdir ./lib/win
    $null = mkdir ./lib/mac
    $null = mkdir ./lib/win-sqlclient
    $null = mkdir ./lib/win-sqlclient-x86
} else {
    $tempdir = "C:/temp"
    $null = New-Item -ItemType Directory $tempdir -ErrorAction Ignore
    $null = New-Item -ItemType Directory ./temp/dacfull -ErrorAction Ignore
    $null = New-Item -ItemType Directory ./temp/xe -ErrorAction Ignore
    $null = New-Item -ItemType Directory ./third-party/XESmartTarget
    $null = New-Item -ItemType Directory ./third-party/bogus
    $null = New-Item -ItemType Directory ./third-party/LumenWorks
    $null = New-Item -ItemType Directory ./temp/bogus
    $null = New-Item -ItemType Directory ./temp/linux
    $null = New-Item -ItemType Directory ./lib/win
    $null = New-Item -ItemType Directory ./lib/mac
    $null = New-Item -ItemType Directory ./lib/win-sqlclient
    $null = New-Item -ItemType Directory ./lib/win-sqlclient-x86
}



$ProgressPreference = "SilentlyContinue"


Invoke-WebRequest -Uri https://aka.ms/sqlpackage-linux -OutFile ./temp/sqlpackage-linux.zip
Invoke-WebRequest -Uri https://aka.ms/sqlpackage-macos -OutFile ./temp/sqlpackage-macos.zip
Invoke-WebRequest -Uri https://aka.ms/dacfx-msi -OutFile .\temp\DacFramework.msi
Invoke-WebRequest -Uri https://www.nuget.org/api/v2/package/Bogus -OutFile ./temp/bogus.zip
Invoke-WebRequest -Uri https://www.nuget.org/api/v2/package/LumenWorksCsvReader -OutFile ./temp/LumenWorksCsvReader.zip
Invoke-WebRequest -Uri https://github.com/spaghettidba/XESmartTarget/releases/download/v1.4.9/XESmartTarget_x64.msi -OutFile ./temp/XESmartTarget_x64.msi

$ProgressPreference = "Continue"


# Expand-Archive is not fun on linux cuz it's prompts galore
unzip ./temp/sqlpackage-linux.zip -d ./temp/linux
unzip ./temp/sqlpackage-macos.zip -d ./lib/mac
unzip ./temp/LumenWorksCsvReader.zip -d ./temp/LumenWorksCsvReader
unzip ./temp/bogus.zip -d ./temp/bogus

msiextract --directory $(Resolve-Path .\temp\dacfull) $(Resolve-Path .\temp\DacFramework.msi)
msiextract --directory $(Resolve-Path .\temp\xe) $(Resolve-Path .\temp\XESmartTarget_x64.msi)


Get-ChildItem "./temp/xe/*.dll" -Recurse | Copy-Item -Destination third-party/XESmartTarget
Get-ChildItem "./temp/dacfull/" -Include *.dll, *.exe, *.config -Recurse | Copy-Item -Destination ./lib/win
Get-ChildItem "./temp/bogus/*/net6.0/bogus.dll" -Recurse | Copy-Item -Destination ./third-party/bogus/bogus.dll
Copy-Item ./temp/LumenWorksCsvReader/lib/netstandard2.0/LumenWorks.Framework.IO.dll -Destination ./third-party/LumenWorks/LumenWorks.Framework.IO.dll

Register-PackageSource -provider NuGet -name nugetRepository -Location https://www.nuget.org/api/v2 -Trusted -ErrorAction Ignore

$parms = @{
    Provider         = "Nuget"
    Destination      = "$tempdir/nuget"
    Source           = "nugetRepository"
    Scope            = "CurrentUser"
    Force            = $true
    SkipDependencies = $true
}

$parms.Name = "Microsoft.Data.SqlClient"
$parms.RequiredVersion = "5.1.4"
$null = Install-Package @parms

$parms.Name = "Microsoft.Data.SqlClient.SNI.runtime"
$parms.RequiredVersion = "5.2.0"
$null = Install-Package @parms

$parms.Name = "Microsoft.Identity.Client"
$parms.RequiredVersion = "4.53.0"
$null = Install-Package @parms

Copy-Item "$tempdir/nuget/Microsoft.Data.SqlClient.5.1.4/runtimes/unix/lib/net6.0/Microsoft.Data.SqlClient.dll" -Destination lib

# Copy to the 'x64' directory
Copy-Item "$tempdir/nuget/Microsoft.Data.SqlClient.5.1.4/runtimes/win/lib/net6.0/Microsoft.Data.SqlClient.dll" -Destination lib/win-sqlclient/
Copy-Item "$tempdir/nuget/Microsoft.Identity.Client.4.53.0/lib/net6.0/Microsoft.Identity.Client.dll" -Destination lib/win-sqlclient/ #Maybe this will be a problem, i dont know
Copy-Item "$tempdir/nuget/Microsoft.Data.SqlClient.SNI.runtime.5.2.0/runtimes/win-x64/native/Microsoft.Data.SqlClient.SNI.dll" -Destination lib/win-sqlclient/

# Copy to the 'x86' directory, but with a different SNI DLL file. Remember,the SNI file is not managed code, it's _native_.
Copy-Item "$tempdir/nuget/Microsoft.Data.SqlClient.5.1.4/runtimes/win/lib/net6.0/Microsoft.Data.SqlClient.dll" -Destination lib/win-sqlclient-x86/
Copy-Item "$tempdir/nuget/Microsoft.Identity.Client.4.53.0/lib/net6.0/Microsoft.Identity.Client.dll" -Destination lib/win-sqlclient-x86/ #Maybe this will be a problem, i dont know
Copy-Item "$tempdir/nuget/Microsoft.Data.SqlClient.SNI.runtime.5.2.0/runtimes/win-x86/native/Microsoft.Data.SqlClient.SNI.dll" -Destination lib/win-sqlclient-x86/

Copy-Item ./temp/linux/* -Destination lib -Exclude (Get-ChildItem lib -Recurse) -Recurse -Include *.exe, *.config -Verbose

Copy-Item "./var/misc/core/*.dll" -Destination ./lib/
Copy-Item "./var/misc/both/*.dll" -Destination ./lib/
Copy-Item "./var/third-party-licenses" -Destination ./ -Recurse

$linux = 'libclrjit.so', 'libcoreclr.so', 'libhostfxr.so', 'libhostpolicy.so', 'libSystem.Native.so', 'libSystem.Security.Cryptography.Native.OpenSsl.so', 'Microsoft.Win32.Primitives.dll', 'sqlpackage', 'sqlpackage.deps.json', 'sqlpackage.dll', 'sqlpackage.pdb', 'sqlpackage.runtimeconfig.json', 'sqlpackage.xml', 'System.Collections.Concurrent.dll', 'System.Collections.dll', 'System.Console.dll', 'System.Diagnostics.FileVersionInfo.dll', 'System.Diagnostics.TraceSource.dll', 'System.Linq.dll', 'System.Memory.dll', 'System.Private.CoreLib.dll', 'System.Private.Xml.dll', 'System.Reflection.Metadata.dll', 'System.Runtime.dll', 'System.Security.Cryptography.Algorithms.dll', 'System.Security.Cryptography.Primitives.dll', 'System.Threading.dll', 'System.Threading.Thread.dll', 'System.Xml.ReaderWriter.dll', 'sqlpackage', 'sqlpackage.deps.json', 'sqlpackage.dll', 'sqlpackage.pdb', 'sqlpackage.runtimeconfig.json', 'sqlpackage.xml'

$sqlp = Get-ChildItem ./temp/linux/* -Exclude (Get-ChildItem lib -Recurse) | Where-Object Name -in $linux
Copy-Item -Path $sqlp.FullName -Destination ./lib/

Get-ChildItem -Directory -Path ./lib | Where-Object Name -notin 'win-sqlclient', 'win-sqlclient-x86', 'x64', 'x86', 'win', 'mac', 'macos' | Remove-Item -Recurse

Get-ChildItem ./lib, ./lib/win, ./lib/mac | Where-Object BaseName -in (Get-ChildItem /opt/microsoft/powershell/7).BaseName -OutVariable files

if ($files) {
    Remove-Item $files -Recurse
}


if ($isLinux -or $IsMacOs) {
    chmod +x ./lib/sqlpackage
    chmod +x ./lib/mac/sqlpackage
}

Get-ChildItem ./lib/*.xml, ./lib/*.pdb -Recurse -OutVariable xmlpdb
if ($xmlpdb) {
    Remove-Item -Path $xmlpdb -Recurse -ErrorAction Ignore
}

#Import-Module ./dbatools.core.library.psd1

<#
    if ((Get-ChildItem -Path C:\gallery\dbatools.library\core -ErrorAction Ignore)) {
        $null = Remove-Item C:\gallery\dbatools.library\core -Recurse
        $null = mkdir C:\gallery\dbatools.library\core
        $null = robocopy c:\github\dbatools.library C:\gallery\dbatools.library\core /S /XF actions-build.ps1 .markdownlint.json *.psproj* *.git* *.yml *.md dac.ps1 *build*.ps1 /XD .git .github Tests .vscode project temp runtime runtimes replication var opt | Out-String | Out-Null
        Remove-Item c:\gallery\dbatools.library\core\dac.ps1 -ErrorAction Ignore
        Remove-Item c:\gallery\dbatools.library\core\dbatools.library.psd1 -ErrorAction Ignore
        #Copy-Item C:\github\dbatools.library\dbatools.core.library.psd1 C:\github\dbatools.core.library

        Get-ChildItem -Recurse -Path C:\gallery\dbatools.library\*.ps*, C:\gallery\dbatools.library\*\dbatools.dll | Set-AuthenticodeSignature -Certificate (Get-ChildItem -Path Cert:\CurrentUser\My\1c735258e8b34ce113ad86a501235c1f2e263106) -TimestampServer http://timestamp.digicert.com -HashAlgorithm SHA256
    }
#>
