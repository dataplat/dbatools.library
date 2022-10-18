# Go compile the DLLs
Set-Location C:\github\dbatools-library
Remove-Item .\project\dbatools\obj -Recurse -ErrorAction Ignore
Remove-Item .\project\dbatools.Tests\bin -Recurse -ErrorAction Ignore
Remove-Item .\project\dbatools.Tests\obj -Recurse -ErrorAction Ignore
Get-ChildItem -Recurse bin | Remove-Item -Recurse -ErrorAction Ignore
Get-ChildItem -Recurse temp | Remove-Item -Recurse -ErrorAction Ignore
Push-Location ".\project"
dotnet clean
dotnet publish --configuration release --framework net6.0 --self-contained | Out-String -OutVariable build
dotnet publish --configuration release --framework net462 --self-contained | Out-String -OutVariable build
#dotnet test --framework net462 --verbosity normal | Out-String -OutVariable test
#dotnet test --framework net6.0 --verbosity normal | Out-String -OutVariable test

#https://github.com/dotnet/SqlClient/issues/292
#[System.AppContext]::SetSwitch("Switch.Microsoft.Data.SqlClient.UseManagedNetworkingOnWindows", $false)
# [System.AppDomain]::CurrentDomain.remove_AssemblyResolve($onAssemblyResolveEventHandler)

Pop-Location


Get-ChildItem .\bin -Recurse -Include *.pdb | Remove-Item -Force
Get-ChildItem .\bin -Recurse -Include *.xml | Remove-Item -Force
Get-ChildItem .\bin\net462\ -Exclude *dbatools*, publish | Remove-Item -Force -Recurse
Get-ChildItem .\bin\ -Include runtimes -Recurse | Remove-Item -Force -Recurse
Get-ChildItem .\bin\*\dbatools.deps.json -Recurse | Remove-Item -Force

Copy-Item -Recurse ./third-party-licenses ./bin/third-party-licenses

$null = mkdir ./temp/dacfull -Force
$null = mkdir ./bin/sqlpackage/windows -Force
$null = mkdir ./bin/sqlpackage/mac
$null = mkdir ./temp/xe
$null = mkdir ./bin/third-party
$null = mkdir ./bin/third-party/XESmartTarget
$null = mkdir ./bin/third-party/bogus
$null = mkdir ./bin/third-party/bogus/net31
$null = mkdir ./bin/third-party/bogus/net40
$null = mkdir ./temp/bogus
$null = mkdir ./bin/net6.0/publish/win
#$null = mkdir bin/net6.0/publish/mac

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
#Copy-Item C:\temp\sqlpackage-win.zip .\temp\sqlpackage-win.zip
Copy-Item C:\temp\DacFramework.msi .\temp\DacFramework.msi
Copy-Item C:\temp\bogus.zip .\temp\bogus.zip
Copy-Item C:\temp\XESmartTarget_x64.msi .\temp\XESmartTarget_x64.msi

$ProgressPreference = "Continue"

Expand-Archive -Path .\temp\sqlpackage-linux.zip -DestinationPath .\temp\linux
#Expand-Archive -Path .\temp\sqlpackage-win.zip -DestinationPath .\temp\windows
Expand-Archive -Path .\temp\sqlpackage-macos.zip -DestinationPath .\temp\macos
Expand-Archive -Path .\temp\bogus.zip -DestinationPath .\temp\bogus

msiexec /a $(Resolve-Path .\temp\DacFramework.msi) /qb TARGETDIR=$(Resolve-Path .\temp\dacfull)
Start-Sleep 3
msiexec /a $(Resolve-Path .\temp\XESmartTarget_x64.msi) /qb TARGETDIR=$(Resolve-Path .\temp\xe)
Start-Sleep 3

$mac = 'libclrjit.dylib', 'libcoreclr.dylib', 'libhostfxr.dylib', 'libhostpolicy.dylib', 'libSystem.Native.dylib', 'libSystem.Security.Cryptography.Native.Apple.dylib', 'Microsoft.Data.Tools.Schema.Sql.dll', 'Microsoft.Data.Tools.Utilities.dll', 'Microsoft.IdentityModel.JsonWebTokens.dll', 'Microsoft.Win32.Primitives.dll', 'sqlpackage', 'sqlpackage.deps.json', 'sqlpackage.dll', 'sqlpackage.pdb', 'sqlpackage.runtimeconfig.json', 'sqlpackage.xml', 'System.Collections.Concurrent.dll', 'System.Collections.dll', 'System.Console.dll', 'System.Diagnostics.FileVersionInfo.dll', 'System.Diagnostics.StackTrace.dll', 'System.Diagnostics.TextWriterTraceListener.dll', 'System.Diagnostics.TraceSource.dll', 'System.Linq.dll', 'System.Memory.dll', 'System.Net.Http.Json.dll', 'System.Private.CoreLib.dll', 'System.Private.Xml.dll', 'System.Reflection.Metadata.dll', 'System.Runtime.dll', 'System.Runtime.Serialization.Json.dll', 'System.Security.Cryptography.Algorithms.dll', 'System.Security.Cryptography.Primitives.dll', 'System.Text.Json.dll', 'System.Threading.dll', 'System.Threading.Thread.dll', 'System.Xml.ReaderWriter.dll'
$linux = 'libclrjit.so', 'libcoreclr.so', 'libcoreclrtraceptprovider.so', 'libhostfxr.so', 'libhostpolicy.so', 'libSystem.Native.so', 'libSystem.Security.Cryptography.Native.OpenSsl.so', 'Microsoft.Win32.Primitives.dll', 'System.Collections.Concurrent.dll', 'System.Collections.dll', 'System.Console.dll', 'System.Diagnostics.FileVersionInfo.dll', 'System.Diagnostics.StackTrace.dll', 'System.Diagnostics.TextWriterTraceListener.dll', 'System.Diagnostics.TraceSource.dll', 'System.Linq.dll', 'System.Memory.dll', 'System.Net.Http.Json.dll', 'System.Private.CoreLib.dll', 'System.Private.Xml.dll', 'System.Reflection.Metadata.dll', 'System.Runtime.dll', 'System.Runtime.Serialization.Json.dll', 'System.Security.Cryptography.Algorithms.dll', 'System.Text.Json.dll', 'System.Threading.dll', 'System.Threading.Thread.dll', 'System.Xml.ReaderWriter.dll', 'sqlpackage', 'sqlpackage.dll', 'sqlpackage.deps.json', 'sqlpackage.runtimeconfig.json'
$winfull = 'Microsoft.Data.SqlClient.dll', 'Microsoft.Data.SqlClient.SNI.x64.dll', 'Microsoft.Data.SqlClient.SNI.x86.dll', 'System.Threading.Tasks.Dataflow.dll', 'Azure.Core.dll', 'Azure.Identity.dll', 'Microsoft.Build.dll', 'Microsoft.Build.Framework.dll', 'Microsoft.Data.Tools.Schema.Sql.dll', 'Microsoft.Data.Tools.Utilities.dll', 'Microsoft.SqlServer.Dac.dll', 'Microsoft.SqlServer.Dac.Extensions.dll', 'Microsoft.SqlServer.TransactSql.ScriptDom.dll', 'Microsoft.SqlServer.Types.dll', 'System.Memory.Data.dll', 'System.Resources.Extensions.dll', 'System.Security.SecureString.dll', 'sqlpackage.exe', 'sqlpackage.dll', 'libhostfxr.so', 'libhostpolicy.so', 'sqlpackage.runtimeconfig.json', 'sqlpackage.deps.json', 'hostpolicy.dll', 'hostfxr.dll', 'sqlpackage.dll'
$xe = 'CommandLine.dll', 'CsvHelper.dll', 'DouglasCrockford.JsMin.dll', 'NLog.dll', 'NLog.dll.nlog', 'SmartFormat.dll', 'XESmartTarget.Core.dll'

# 'Microsoft.Data.SqlClient.dll', 'Microsoft.Data.SqlClient.SNI.dll',
# 'Microsoft.Identity.Client.dll', 'Microsoft.Identity.Client.Extensions.Msal.dll',
#Get-ChildItem "./temp/dacfull/*.exe*" -File -Recurse | Copy-Item -Destination bin/net462/publish

Get-ChildItem "./temp/dacfull/" -Recurse | Where-Object Name -in $winfull | Copy-Item -Destination bin/sqlpackage/windows


Get-ChildItem "./temp/xe/*.dll" -Recurse | Where-Object Name -in $xe | Copy-Item -Destination bin/third-party/XESmartTarget
Get-ChildItem "./temp/bogus/*/netstandard2.0/bogus.dll" -Recurse | Copy-Item -Destination bin/third-party/bogus/net31/bogus.dll
Get-ChildItem "./temp/bogus/*/net40/bogus.dll" -Recurse | Copy-Item -Destination bin/third-party/bogus/net40/bogus.dll

Get-ChildItem bin/net462/dbatools.dll | Remove-Item -Force
Get-ChildItem bin/net6.0/dbatools.dll | Remove-Item -Force
Get-ChildItem bin/net462/dbatools.dll.config | Remove-Item -Force
Get-ChildItem bin/net6.0/dbatools.dll.config | Remove-Item -Force

Get-ChildItem ./temp/linux | Where-Object Name -in $linux | Copy-Item -Destination bin/net6.0
Get-ChildItem ./temp/macos | Where-Object Name -in $mac | Copy-Item -Destination bin/sqlpackage/mac/

Register-PackageSource -provider NuGet -name nugetRepository -Location https://www.nuget.org/api/v2 -Trusted -ErrorAction Ignore

$parms = @{
    Provider         = "Nuget"
    Destination      = "C:\temp\nuget"
    Source           = "nugetRepository"
    Scope            = "CurrentUser"
    Force            = $true
    SkipDependencies = $true
}

$parms.Name = "System.Resources.Extensions"
$parms.RequiredVersion = "6.0.0.0"
#Install-Package @parms

$parms.Name = "Microsoft.SqlServer.DacFx"
$parms.RequiredVersion = "161.6319.0-preview"
#Install-Package @parms

$parms.Name = "Microsoft.SqlServer.SqlManagementObjects"
$parms.RequiredVersion = "170.7.0-preview"
#Install-Package @parms

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
Copy-Item "C:\temp\nuget\Microsoft.Identity.Client.4.45.0\lib\net461\Microsoft.Identity.Client.dll" -Destination bin/net462/publish/

Copy-Item "C:\temp\nuget\Microsoft.Data.SqlClient.5.0.1\runtimes\win\lib\netcoreapp3.1\Microsoft.Data.SqlClient.dll" -Destination bin/net6.0/publish/win
Copy-Item "C:\temp\nuget\Microsoft.Identity.Client.4.45.0\lib\netcoreapp2.1\Microsoft.Identity.Client.dll" -Destination bin/net6.0/publish/win

Copy-Item "C:\temp\nuget\Microsoft.Data.SqlClient.SNI.runtime.5.0.1\runtimes\win-x64\native\Microsoft.Data.SqlClient.SNI.dll" -Destination bin/net6.0/publish/win
Copy-Item "C:\temp\nuget\Microsoft.Data.SqlClient.SNI.runtime.5.0.1\runtimes\win-x64\native\Microsoft.Data.SqlClient.SNI.dll" -Destination bin/net462/publish/


Move-Item -Path bin/net6.0/publish/* -Destination bin/net6.0/
Move-Item -Path bin/net462/publish/* -Destination bin/net462/

Remove-Item -Path bin/net6.0/publish -Recurse -ErrorAction Ignore
Remove-Item -Path bin/net462/publish -Recurse -ErrorAction Ignore

Remove-Item -Path bin/*.xml -Recurse -ErrorAction Ignore
Remove-Item -Path bin/*.pdb -Recurse -ErrorAction Ignore

Get-ChildItem -Directory -Path .\bin\net462 | Where-Object Name -notin 'x64', 'x86', 'win', 'mac', 'macos' | Remove-Item -Recurse
Get-ChildItem -Directory -Path .\bin\net6.0 | Where-Object Name -notin 'x64', 'x86', 'win', 'mac', 'macos' | Remove-Item -Recurse


Import-Module C:\github\dbatools-library -Force; Import-Module C:\github\dbatools -Force; New-Object -TypeName Microsoft.SqlServer.Dac.DacServices -ArgumentList 'Data Source=sqlcs;Integrated Security=True;MultipleActiveResultSets=False;Encrypt=False;TrustServerCertificate=true;Packet Size=4096;Application Name="dbatools PowerShell module - dbatools.io";Database=dbatoolsci_publishdacpac';Connect-DbaInstance -SqlInstance sqlcs

$Error | Select-Object *


$script:instance1 = $script:instance2 = "sqlcs"
Set-DbatoolsConfig -FullName sql.connection.trustcert -Value $true
$db = Get-DbaDatabase -SqlInstance $script:instance1 -Database dbatoolsci_publishdacpac
$publishprofile = New-DbaDacProfile -SqlInstance $script:instance1 -Database dbatoolsci_publishdacpac -Path C:\temp
$extractOptions = New-DbaDacOption -Action Export
$extractOptions.ExtractAllTableData = $true
$dacpac = Export-DbaDacPackage -SqlInstance $script:instance1 -Database dbatoolsci_publishdacpac -DacOption $extractOptions
$dacpac
$dacpac | Publish-DbaDacPackage -PublishXml $publishprofile.FileName -Database butt -SqlInstance $script:instance2 -Confirm:$false
$Error | Select-Object *




<#

### LINUX #####
Import-Module ./dbatools-library -Force; Import-Module ./dbatools -Force; New-Object -TypeName Microsoft.SqlServer.Dac.DacServices -ArgumentList 'Data Source=sqlcs;Integrated Security=True;MultipleActiveResultSets=False;Encrypt=False;TrustServerCertificate=true;Packet Size=4096;Application Name="dbatools PowerShell module - dbatools.io";Database=dbatoolsci_publishdacpac';Connect-DbaInstance -SqlInstance sqlcs -TrustServerCertificate


ipmo ./dbatools-library -Force; ipmo ./dbatools -Force; Connect-DbaInstance -SqlInstance sqlcs -TrustServerCertificate

$script:instance1 =  $script:instance2 = "sqlcs"
Set-DbatoolsConfig -FullName sql.connection.trustcert -Value $true
$db = Get-DbaDatabase -SqlInstance $script:instance1 -Database dbatoolsci_publishdacpac
$publishprofile = New-DbaDacProfile -SqlInstance $script:instance1 -Database dbatoolsci_publishdacpac -Path /tmp
$extractOptions = New-DbaDacOption -Action Export
$extractOptions.ExtractAllTableData = $true
$dacpac = Export-DbaDacPackage -SqlInstance $script:instance1 -Database dbatoolsci_publishdacpac -DacOption $extractOptions
$dacpac

($dacpac | Publish-DbaDacPackage -PublishXml $publishprofile.FileName -Database butt -SqlInstance $script:instance2 -Confirm:$false).Result
$Error | Select-Object *



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


#.OnRemove 




Azure.Core.dll', 'Azure.Identity.dll', 'Microsoft.Data.SqlClient.dll', 'Microsoft.Data.SqlClient.SNI.x64.dll', 'Microsoft.Data.SqlClient.SNI.x86.dll', 'Microsoft.Data.Tools.Schema.Sql.dll', 'Microsoft.Data.Tools.Utilities.dll', 'Microsoft.Identity.Client.dll', 'Microsoft.Identity.Client.Extensions.Msal.dll', 'Microsoft.IdentityModel.JsonWebTokens.dll', 'Microsoft.IdentityModel.Logging.dll', 'Microsoft.IdentityModel.Protocols.dll', 'Microsoft.IdentityModel.Protocols.OpenIdConnect.dll', 'Microsoft.IdentityModel.Tokens.dll', 'Microsoft.SqlServer.Dac.dll', 'Microsoft.SqlServer.Dac.Extensions.dll', 'Microsoft.SqlServer.TransactSql.ScriptDom.dll', 'Microsoft.SqlServer.Types.dll', 'netstandard.dll', 'SqlPackage.exe', 'SqlPackage.exe.config', 'System.Data.Common.dll', 'System.Memory.Data.dll', 'System.Memory.dll', 'System.Net.Http.dll', 'System.Resources.Extensions.dll', 'System.Runtime.InteropServices.RuntimeInformation.dll', 'System.Security.SecureString.dll', 'System.Threading.Tasks.Dataflow.dll', 'System.Threading.Tasks.Extensions.dll

#>