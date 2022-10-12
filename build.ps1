# Go compile the DLLs
Set-Location C:\github\dbatools-library
Remove-Item .\project\dbatools\obj -Recurse -ErrorAction Ignore
Remove-Item .\project\dbatools.Tests\bin -Recurse -ErrorAction Ignore
Remove-Item .\project\dbatools.Tests\obj -Recurse -ErrorAction Ignore
Get-ChildItem -Recurse bin | Remove-Item -Recurse -ErrorAction Ignore
Get-ChildItem -Recurse temp | Remove-Item -Recurse -ErrorAction Ignore
Push-Location ".\project"
dotnet publish --configuration release --framework net6.0 | Out-String -OutVariable build
dotnet publish --configuration release --framework net462 | Out-String -OutVariable build
dotnet test --framework net462 --verbosity normal | Out-String -OutVariable test
dotnet test --framework net6.0 --verbosity normal | Out-String -OutVariable test


Get-ChildItem ..\bin -Recurse -Include *.pdb | Remove-Item -Force
Get-ChildItem ..\bin -Recurse -Include *.xml | Remove-Item -Force
Get-ChildItem ..\bin\net462\ -Exclude *dbatools*, publish | Remove-Item -Force -Recurse
Get-ChildItem ..\bin\ -Include runtimes -Recurse | Remove-Item -Force -Recurse
Get-ChildItem ..\bin\*\dbatools.deps.json -Recurse | Remove-Item -Force

#https://github.com/dotnet/SqlClient/issues/292
#[System.AppContext]::SetSwitch("Switch.Microsoft.Data.SqlClient.UseManagedNetworkingOnWindows", $false)

Pop-Location

Copy-Item -Recurse ./third-party-licenses ./bin/third-party-licenses

$null = mkdir ./temp/dacfull -Force
$null = mkdir ./temp/xe
$null = mkdir ./bin/third-party
$null = mkdir ./bin/third-party/XESmartTarget
$null = mkdir ./bin/third-party/bogus
$null = mkdir ./bin/third-party/bogus/net31
$null = mkdir ./bin/third-party/bogus/net40
$null = mkdir ./temp/bogus
$null = mkdir bin/net6.0/publish/win
$null = mkdir bin/net6.0/publish/mac

$ProgressPreference = "SilentlyContinue"

<#
Invoke-WebRequest -Uri https://aka.ms/sqlpackage-linux -OutFile .\temp\sqlpackage-linux.zip
Invoke-WebRequest -Uri https://aka.ms/sqlpackage-macos -OutFile .\temp\sqlpackage-macos.zip
Invoke-WebRequest -Uri https://aka.ms/sqlpackage-windows -OutFile .\temp\sqlpackage-win.zip
Invoke-WebRequest -Uri https://aka.ms/dacfx-msi -OutFile .\temp\DacFramework.msi
Invoke-WebRequest -Uri https://www.nuget.org/api/v2/package/Bogus -OutFile .\temp\bogus.zip
Invoke-WebRequest -Uri https://github.com/spaghettidba/XESmartTarget/releases/download/v1.4.9/XESmartTarget_x64.msi -OutFile .\temp\XESmartTarget_x64.msi
#>

Copy-Item C:\temp\sqlpackage-linux.zip .\temp\sqlpackage-linux.zip
Copy-Item C:\temp\sqlpackage-macos.zip .\temp\sqlpackage-macos.zip
Copy-Item C:\temp\sqlpackage-win.zip .\temp\sqlpackage-win.zip
Copy-Item C:\temp\DacFramework.msi .\temp\DacFramework.msi
Copy-Item C:\temp\bogus.zip .\temp\bogus.zip
Copy-Item C:\temp\XESmartTarget_x64.msi .\temp\XESmartTarget_x64.msi

$ProgressPreference = "Continue"

Expand-Archive -Path .\temp\sqlpackage-linux.zip -DestinationPath .\temp\linux
Expand-Archive -Path .\temp\sqlpackage-win.zip -DestinationPath .\temp\windows
Expand-Archive -Path .\temp\sqlpackage-macos.zip -DestinationPath .\temp\macos
Expand-Archive -Path .\temp\bogus.zip -DestinationPath .\temp\bogus

msiexec /a $(Resolve-Path .\temp\DacFramework.msi) /qb TARGETDIR=$(Resolve-Path .\temp\dacfull)
Start-Sleep 3
msiexec /a $(Resolve-Path .\temp\XESmartTarget_x64.msi) /qb TARGETDIR=$(Resolve-Path .\temp\xe)
Start-Sleep 3

$mac = 'Azure.Core.dll', 'Azure.Identity.dll', 'sqlpackage', 'System.Memory.Data.dll', 'System.Security.SecureString.dll'
$other = 'Azure.Core.dll', 'Azure.Identity.dll', 'Microsoft.Build.dll', 'Microsoft.Build.Framework.dll', 'Microsoft.Data.Tools.Schema.Sql.dll', 'Microsoft.Data.Tools.Utilities.dll', 'Microsoft.SqlServer.Dac.dll', 'Microsoft.SqlServer.Dac.Extensions.dll', 'Microsoft.SqlServer.TransactSql.ScriptDom.dll', 'Microsoft.SqlServer.Types.dll', 'System.Memory.Data.dll', 'System.Resources.Extensions.dll', 'System.Security.SecureString.dll', 'sqlpackage.exe', 'sqlpackage'
$xe = 'CommandLine.dll', 'CsvHelper.dll', 'DouglasCrockford.JsMin.dll', 'NLog.dll', 'NLog.dll.nlog', 'SmartFormat.dll', 'XESmartTarget.Core.dll'

# 'Microsoft.Data.SqlClient.dll', 'Microsoft.Data.SqlClient.SNI.dll',
# 'Microsoft.Identity.Client.dll', 'Microsoft.Identity.Client.Extensions.Msal.dll',

Get-ChildItem "./temp/dacfull/*.dll" -Recurse | Where-Object Name -in $other | Copy-Item -Destination bin/net462/publish
Get-ChildItem "./temp/xe/*.dll" -Recurse | Where-Object Name -in $xe | Copy-Item -Destination bin/third-party/XESmartTarget
Get-ChildItem "./temp/bogus/*/netstandard2.0/bogus.dll" -Recurse | Copy-Item -Destination bin/third-party/bogus/net31/bogus.dll
Get-ChildItem "./temp/bogus/*/net40/bogus.dll" -Recurse | Copy-Item -Destination bin/third-party/bogus/net40/bogus.dll

Get-ChildItem bin/net462/publish/dbatools.dll | Remove-Item -Force
Get-ChildItem bin/net6.0/publish/dbatools.dll | Remove-Item -Force

Get-ChildItem ./temp/linux | Where-Object Name -in $other | Copy-Item -Destination bin/net6.0/publish
Get-ChildItem ./temp/windows | Where-Object Name -in $other | Copy-Item -Destination bin/net6.0/publish/win
#Get-ChildItem ./temp/windows/*Microsoft.Data.SqlClient*.dll | Copy-Item -Destination bin/net6.0/publish/win
Get-ChildItem ./temp/macos | Where-Object Name -in $mac | Copy-Item -Destination bin/net6.0/publish/mac

Register-PackageSource -provider NuGet -name nugetRepository -Location https://www.nuget.org/api/v2 -Trusted -ErrorAction SilentlyContinue

$parms = @{
    Provider         = "Nuget"
    Destination      = "C:\temp\nuget"
    Source           = "nugetRepository"
    Scope            = "CurrentUser"
    Force            = $true
    SkipDependencies = $true
}

$parms.Name = "Microsoft.Data.SqlClient"
$parms.RequiredVersion = "5.0.1"
#Install-Package @parms

$parms.Name = "Microsoft.Data.SqlClient.SNI.runtime"
$parms.RequiredVersion = "5.0.1"
#Install-Package @parms

$parms.Name = "Microsoft.Identity.Client"
$parms.RequiredVersion = "4.45.0"
#Install-Package @parms

$parms.Name = "Azure.Identity"
$parms.RequiredVersion = "1.6.0"
#Install-Package @parms

Copy-Item "C:\temp\nuget\Microsoft.Data.SqlClient.5.0.1\runtimes\unix\lib\netcoreapp3.1\Microsoft.Data.SqlClient.dll" -Destination bin/net6.0/publish
Copy-Item "C:\temp\nuget\Microsoft.Data.SqlClient.5.0.1\runtimes\win\lib\netcoreapp3.1\Microsoft.Data.SqlClient.dll" -Destination bin/net6.0/publish/win
Copy-Item "C:\temp\nuget\Microsoft.Identity.Client.4.45.0\lib\netcoreapp2.1\Microsoft.Identity.Client.dll" -Destination bin/net6.0/publish/win
Copy-Item "C:\temp\nuget\Microsoft.Data.SqlClient.SNI.runtime.5.0.1\runtimes\win-x64\native\Microsoft.Data.SqlClient.SNI.dll" -Destination bin/net6.0/publish/win




Import-Module C:\github\dbatools-library -Force
Import-Module C:\github\dbatools -Force

Connect-DbaInstance -SqlInstance sqlcs



<#
# Remove all the SMO directories that the build created -- they are elsewhere in the project
Get-ChildItem -Directory ".\bin\net462" | Remove-Item -Recurse -Confirm:$false
Get-ChildItem -Directory ".\bin\net6.0" | Remove-Item -Recurse -Confirm:$false

# Remove all the SMO files that the build created -- they are elsewhere in the project
Get-ChildItem ".\bin\net6.0" -Recurse -Exclude dbatools.* | Move-Item -Destination ".\bin\smo\coreclr" -Confirm:$false -Force
Get-ChildItem ".\bin\net462" -Recurse -Exclude dbatools.* | Move-Item -Destination ".\bin\smo\" -Confirm:$false -Force
Get-ChildItem ".\bin" -Recurse -Include dbatools.deps.json | Remove-Item -Confirm:$false

# Sign the DLLs, how cool -- Set-AuthenticodeSignature works on DLLs (and presumably Exes) too
$buffer = [IO.File]::ReadAllBytes("C:\github\dbatools-code-signing-cert.pfx")
$certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::New($buffer, $pw.Password)
Get-ChildItem dbatools.dll -Recurse | Set-AuthenticodeSignature -Certificate $certificate -TimestampServer http://timestamp.digicert.com -HashAlgorithm SHA256
#>