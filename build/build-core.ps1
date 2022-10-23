$PSDefaultParameterValues["*:Force"] = $true
$PSDefaultParameterValues["*:Confirm"] = $false
Push-Location "./project"
dotnet publish --configuration release --framework net6.0 --self-contained | Out-String -OutVariable build
dotnet test --framework net6.0 --verbosity normal | Out-String -OutVariable test
Pop-Location

Move-Item -Path lib/net6.0/* -Destination lib/ -ErrorAction Ignore
Remove-Item -Path lib/net6.0 -Recurse -ErrorAction Ignore

Get-ChildItem ./lib -Recurse -Include *.pdb | Remove-Item
Get-ChildItem ./lib -Recurse -Include *.xml | Remove-Item
Get-ChildItem ./lib/ -Include runtimes -Recurse | Remove-Item -Recurse
Get-ChildItem ./lib/*/dbatools.deps.json -Recurse | Remove-Item

if ($IsLinux -or $IsMacOs) {
    $tempdir = "/tmp"
} else {
    $tempdir = "C:/temp"
}

$null = New-Item -ItemType Directory $tempdir -ErrorAction Ignore
$null = New-Item -ItemType Directory ./temp/dacfull -ErrorAction Ignore
$null = New-Item -ItemType Directory ./temp/xe -ErrorAction Ignore
$null = New-Item -ItemType Directory ./lib/third-party
$null = New-Item -ItemType Directory ./lib/third-party/XESmartTarget
$null = New-Item -ItemType Directory ./lib/third-party/bogus
$null = New-Item -ItemType Directory ./lib/third-party/LumenWorks
$null = New-Item -ItemType Directory ./temp/bogus
$null = New-Item -ItemType Directory ./temp/linux
$null = New-Item -ItemType Directory ./lib/win

$ProgressPreference = "SilentlyContinue"


Invoke-WebRequest -Uri https://aka.ms/sqlpackage-linux -OutFile ./temp/sqlpackage-linux.zip
Invoke-WebRequest -Uri https://aka.ms/dacfx-msi -OutFile .\temp\DacFramework.msi
Invoke-WebRequest -Uri https://www.nuget.org/api/v2/package/Bogus -OutFile ./temp/bogus.zip
Invoke-WebRequest -Uri https://www.nuget.org/api/v2/package/LumenWorksCsvReader -OutFile ./temp/LumenWorksCsvReader.zip
Invoke-WebRequest -Uri https://github.com/spaghettidba/XESmartTarget/releases/download/v1.4.9/XESmartTarget_x64.msi -OutFile ./temp/XESmartTarget_x64.msi

$ProgressPreference = "Continue"

if ($IsLinux -or $IsMacOs) {
    # Expand-Archive is not fun on linux cuz it's prompts galore
    unzip ./temp/sqlpackage-linux.zip -d ./temp/linux
    unzip ./temp/LumenWorksCsvReader.zip -d ./temp/LumenWorksCsvReader
    unzip ./temp/bogus.zip -d ./temp/bogus
} else {
    Expand-Archive -Path ./temp/sqlpackage-linux.zip -DestinationPath ./temp/linux
    Expand-Archive -Path ./temp/LumenWorksCsvReader.zip -DestinationPath ./temp/LumenWorksCsvReader
    Expand-Archive -Path ./temp/bogus.zip -DestinationPath ./temp/bogus
}

if ($IsLinux) {
    msiextract --directory $(Resolve-Path .\temp\dacfull) $(Resolve-Path .\temp\DacFramework.msi)
    msiextract --directory $(Resolve-Path .\temp\xe) $(Resolve-Path .\temp\XESmartTarget_x64.msi)
} else {
    msiexec /a $(Resolve-Path .\temp\DacFramework.msi) /qb TARGETDIR=$(Resolve-Path .\temp\dacfull)
    Start-Sleep 3
    msiexec /a $(Resolve-Path .\temp\XESmartTarget_x64.msi) /qb TARGETDIR=$(Resolve-Path .\temp\xe)
    Start-Sleep 3
}


Get-ChildItem "./temp/xe/*.dll" -Recurse | Copy-Item -Destination lib/third-party/XESmartTarget
Get-ChildItem "./temp/dacfull/" -Include *.dll, *.exe -Recurse | Copy-Item -Destination ./lib/win
Get-ChildItem "./temp/bogus/*/netstandard2.0/bogus.dll" -Recurse | Copy-Item -Destination lib/third-party/bogus/bogus.dll
Copy-Item ./temp/LumenWorksCsvReader/lib/netstandard2.0/LumenWorks.Framework.IO.dll -Destination ./lib/third-party/LumenWorks/LumenWorks.Framework.IO.dll

Get-ChildItem lib/dbatools.dll | Remove-Item
Get-ChildItem lib/dbatools.dll.config | Remove-Item

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
$parms.RequiredVersion = "5.0.1"
$null = Install-Package @parms

$parms.Name = "Microsoft.Data.SqlClient.SNI.runtime"
$parms.RequiredVersion = "5.0.1"
$null = Install-Package @parms

$parms.Name = "Microsoft.Identity.Client"
$parms.RequiredVersion = "4.45.0"
$null = Install-Package @parms

Copy-Item "$tempdir/nuget/Microsoft.Data.SqlClient.5.0.1/runtimes/unix/lib/netcoreapp3.1/Microsoft.Data.SqlClient.dll" -Destination lib
Copy-Item "$tempdir/nuget/Microsoft.Data.SqlClient.5.0.1/runtimes/win/lib/netcoreapp3.1/Microsoft.Data.SqlClient.dll" -Destination lib/win
Copy-Item "$tempdir/nuget/Microsoft.Identity.Client.4.45.0/lib/netcoreapp2.1/Microsoft.Identity.Client.dll" -Destination lib/win
Copy-Item "$tempdir/nuget/Microsoft.Data.SqlClient.SNI.runtime.5.0.1/runtimes/win-x64/native/Microsoft.Data.SqlClient.SNI.dll" -Destination lib/win

Copy-Item "./var/replication/*.dll" -Destination ./lib/
Copy-Item (Join-Path ./temp/linux "*") lib -Exclude (Get-ChildItem lib -Recurse) -Recurse

if ($isLinux -or $IsMacOs) {
    chmod +x ./lib/sqlpackage
}

#Move-Item -Path lib/* -Destination lib/
#Remove-Item -Path lib -Recurse -ErrorAction Ignore

Remove-Item -Path lib/*.xml -Recurse -ErrorAction Ignore
Remove-Item -Path lib/*.pdb -Recurse -ErrorAction Ignore

Get-ChildItem -Directory -Path ./lib | Where-Object Name -notin 'x64', 'x86', 'win', 'mac', 'macos' | Remove-Item -Recurse

Import-Module ./dbatools-core-library.psd1
