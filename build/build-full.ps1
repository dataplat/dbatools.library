$PSDefaultParameterValues["*:Force"] = $true
$PSDefaultParameterValues["*:Confirm"] = $false
Push-Location ".\project"

if (Test-Path "C:\github\dbatools-library\lib") {
    write-warning removing
    Remove-Item -Path lib -Recurse -ErrorAction Ignore
    Remove-Item -Path temp -Recurse -ErrorAction Ignore
    Remove-Item -Path third-party -Recurse -ErrorAction Ignore
    Remove-Item -Path third-party-licenses -Recurse -ErrorAction Ignore
}

dotnet publish --configuration release --framework net462 | Out-String -OutVariable build
dotnet test --framework net462 --verbosity normal | Out-String -OutVariable test
Pop-Location

Move-Item -Path lib/net462/* -Destination lib/ -ErrorAction Ignore
Remove-Item -Path lib/net462 -Recurse -ErrorAction Ignore

Get-ChildItem .\lib -Recurse -Include *.pdb | Remove-Item -Force
Get-ChildItem .\lib -Recurse -Include *.xml | Remove-Item -Force
Get-ChildItem .\lib\ -Include runtimes -Recurse | Remove-Item -Force -Recurse
Get-ChildItem .\lib\*\dbatools.deps.json -Recurse | Remove-Item -Force

if ($IsLinux -or $IsMacOs) {
    $tempdir = "/tmp"
} else {
    $tempdir = "C:\temp"
}

$null = New-Item -ItemType Directory $tempdir -Force -ErrorAction Ignore
$null = New-Item -ItemType Directory ./temp/dacfull -Force -ErrorAction Ignore
$null = New-Item -ItemType Directory ./temp/xe -ErrorAction Ignore
$null = New-Item -ItemType Directory ./third-party/XESmartTarget
$null = New-Item -ItemType Directory ./third-party/bogus
$null = New-Item -ItemType Directory ./third-party/LumenWorks
$null = New-Item -ItemType Directory ./third-party/bogus
$null = New-Item -ItemType Directory ./temp/bogus

$ProgressPreference = "SilentlyContinue"

Invoke-WebRequest -Uri https://aka.ms/dacfx-msi -OutFile .\temp\DacFramework.msi
Invoke-WebRequest -Uri https://www.nuget.org/api/v2/package/Bogus -OutFile .\temp\bogus.zip
Invoke-WebRequest -Uri https://www.nuget.org/api/v2/package/LumenWorksCsvReader -OutFile .\temp\LumenWorksCsvReader.zip
Invoke-WebRequest -Uri https://github.com/spaghettidba/XESmartTarget/releases/download/v1.4.9/XESmartTarget_x64.msi -OutFile .\temp\XESmartTarget_x64.msi

$ProgressPreference = "Continue"
7z x .\temp\LumenWorksCsvReader.zip "-o.\temp\LumenWorksCsvReader"
7z x .\temp\bogus.zip "-o.\temp\bogus"

msiexec /a $(Resolve-Path .\temp\DacFramework.msi) /qb TARGETDIR=$(Resolve-Path .\temp\dacfull)
Start-Sleep 3
msiexec /a $(Resolve-Path .\temp\XESmartTarget_x64.msi) /qb TARGETDIR=$(Resolve-Path .\temp\xe)
Start-Sleep 3

Get-ChildItem .\temp\dacfull\* -Include *.dll, *.exe, *.config -Exclude (Get-ChildItem .\lib -Recurse) -Recurse | Copy-Item -Destination ./lib
#Get-ChildItem "./temp/dacfull/" -Include *.dll, *.exe -Recurse | Copy-Item -Destination lib/sqlpackage/windows
Get-ChildItem "./temp/xe/*.dll" -Recurse | Copy-Item -Destination third-party/XESmartTarget
Get-ChildItem "./temp/bogus/*/net40/bogus.dll" -Recurse | Copy-Item -Destination third-party/bogus/bogus.dll

Copy-Item .\temp\LumenWorksCsvReader\lib\net461\LumenWorks.Framework.IO.dll -Destination ./third-party/LumenWorks/LumenWorks.Framework.IO.dll

Register-PackageSource -provider NuGet -name nugetRepository -Location https://www.nuget.org/api/v2 -Trusted -ErrorAction Ignore

$parms = @{
    Provider         = "Nuget"
    Destination      = "$tempdir\nuget"
    Source           = "nugetRepository"
    Scope            = "CurrentUser"
    Force            = $true
    SkipDependencies = $true
}

$parms.Name = "Microsoft.Data.SqlClient"
$parms.RequiredVersion = "5.0.1"
$null = Install-Package @parms

$parms.Name = "Microsoft.Data.SqlClient.SNI.runtime"
$parms.RequiredVersion = "5.0.1"
$null = Install-Package @parms

$parms.Name = "Microsoft.Identity.Client"
$parms.RequiredVersion = "4.45.0"
$null = Install-Package @parms

Copy-Item "$tempdir\nuget\Microsoft.Identity.Client.4.45.0\lib\net461\Microsoft.Identity.Client.dll" -Destination lib/
Copy-Item "$tempdir\nuget\Microsoft.Data.SqlClient.SNI.runtime.5.0.1\runtimes\win-x64\native\Microsoft.Data.SqlClient.SNI.dll" -Destination lib/

Copy-Item "./var/replication/*.dll" -Destination lib/
Copy-Item "./var/third-party-licenses" -Destination ./ -Recurse

Remove-Item -Path lib/*.xml -Recurse -ErrorAction Ignore
Remove-Item -Path lib/*.pdb -Recurse -ErrorAction Ignore

Get-ChildItem -Directory -Path .\lib\ | Where-Object Name -notin 'x64', 'x86' | Remove-Item -Recurse

if ((Get-ChildItem -Path C:\gallery\dbatools-library -ErrorAction Ignore)) {
    $null = Remove-Item C:\gallery\dbatools-library -Recurse
    $null = mkdir C:\gallery\dbatools-library
    $null = robocopy c:\github\dbatools-library C:\gallery\dbatools-library /S /XF actions-build.ps1 .markdownlint.json *.psproj* *.git* *.yml *.md dac.ps1 build*.ps1 dbatools-core*.* /XD .git .github Tests .vscode project temp runtime runtimes replication var opt | Out-String | Out-Null
    
    Remove-Item c:\gallery\dbatools-library\dac.ps1 -ErrorAction Ignore
    Remove-Item c:\gallery\dbatools-library\dbatools-core-library.psd1 -ErrorAction Ignore
    Copy-Item C:\github\dbatools-library\dbatools-library.psd1 C:\gallery\dbatools-library

    Get-ChildItem -Recurse -Path C:\gallery\dbatools-library\*.ps*, C:\gallery\dbatools-library\dbatools.dll | Set-AuthenticodeSignature -Certificate (Get-ChildItem -Path Cert:\CurrentUser\My\fd0dde81152c4d4868afd88d727e78a9b6881cf4) -TimestampServer http://timestamp.digicert.com -HashAlgorithm SHA256
}

Import-Module C:\gallery\dbatools-library\dbatools-library.psd1 -Force

<#
already there
-rwxrwxrwx ctrlb            ctrlb              10/08/2022 03:08       12132752 Microsoft.Data.Tools.Schema.Sql.dll
-rwxrwxrwx ctrlb            ctrlb              10/08/2022 03:08         346040 Microsoft.Data.Tools.Utilities.dll
#>

#(Get-Item C:\github\dbatools-library\lib\Microsoft.Data.Tools.Schema.Sql.dll).VersionInfo.FileVersion
#(Get-Item C:\github\dbatools-library\lib\Microsoft.Data.Tools.Utilities.dll).VersionInfo.FileVersion