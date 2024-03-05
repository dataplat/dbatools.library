# Go compile the DLLs
Set-Location C:\github\dbatools.library
Remove-Item .\project\dbatools\obj -Recurse -ErrorAction Ignore
Remove-Item .\project\dbatools.Tests\lib -Recurse -ErrorAction Ignore
Remove-Item .\project\dbatools.Tests\obj -Recurse -ErrorAction Ignore
Get-ChildItem -Recurse lib | Remove-Item -Recurse -ErrorAction Ignore
Get-ChildItem -Recurse temp | Remove-Item -Recurse -ErrorAction Ignore
Push-Location ".\project"
dotnet clean
dotnet publish --configuration release --framework net6.0 --self-contained | Out-String -OutVariable build
dotnet publish --configuration release --framework net462 --self-contained | Out-String -OutVariable build
#dotnet test --framework net462 --verbosity normal | Out-String -OutVariable test
#dotnet test --framework net6.0 --verbosity normal | Out-String -OutVariable test

Pop-Location

Get-ChildItem .\lib -Recurse -Include *.pdb | Remove-Item -Force
Get-ChildItem .\lib -Recurse -Include *.xml | Remove-Item -Force
Get-ChildItem .\lib\net462\ -Exclude *dbatools*, publish | Remove-Item -Force -Recurse
Get-ChildItem .\lib\ -Include runtimes -Recurse | Remove-Item -Force -Recurse
Get-ChildItem .\lib\*\dbatools.deps.json -Recurse | Remove-Item -Force

#Copy-Item -Recurse ./third-party-licenses ./third-party-licenses

$null = mkdir ./temp/dacfull -Force
$null = mkdir ./temp/xe
$null = mkdir ./third-party
$null = mkdir ./third-party/XESmartTarget
$null = mkdir ./third-party/bogus
$null = mkdir ./third-party/LumenWorks
$null = mkdir ./third-party/LumenWorks/netstandard2.0
$null = mkdir ./third-party/LumenWorks/net461
$null = mkdir ./third-party/bogus/netstandard2.0
$null = mkdir ./third-party/bogus/net40
$null = mkdir ./temp/bogus
$null = mkdir ./lib/net6.0/publish/win

$ProgressPreference = "SilentlyContinue"

Copy-Item C:\temp\sqlpackage-linux.zip .\temp\sqlpackage-linux.zip
Copy-Item C:\temp\sqlpackage-macos.zip .\temp\sqlpackage-macos.zip
Copy-Item C:\temp\DacFramework.msi .\temp\DacFramework.msi
Copy-Item C:\temp\bogus.zip .\temp\bogus.zip
Copy-Item C:\temp\LumenWorksCsvReader.zip .\temp\LumenWorksCsvReader.zip
Copy-Item C:\temp\XESmartTarget_x64.msi .\temp\XESmartTarget_x64.msi

Expand-Archive -Path .\temp\sqlpackage-linux.zip -DestinationPath .\temp\linux
Expand-Archive -Path .\temp\sqlpackage-macos.zip -DestinationPath .\temp\macos
Expand-Archive -Path .\temp\LumenWorksCsvReader.zip -DestinationPath .\temp\LumenWorksCsvReader
Expand-Archive -Path .\temp\bogus.zip -DestinationPath .\temp\bogus
$ProgressPreference = "Continue"


msiexec /a $(Resolve-Path .\temp\DacFramework.msi) /qb TARGETDIR=$(Resolve-Path .\temp\dacfull)
Start-Sleep 3
msiexec /a $(Resolve-Path .\temp\XESmartTarget_x64.msi) /qb TARGETDIR=$(Resolve-Path .\temp\xe)
Start-Sleep 3

$mac = 'libclrjit.dylib', 'libcoreclr.dylib', 'libhostfxr.dylib', 'libhostpolicy.dylib', 'libSystem.Native.dylib', 'libSystem.Security.Cryptography.Native.Apple.dylib', 'Microsoft.Data.Tools.Schema.Sql.dll', 'Microsoft.Data.Tools.Utilities.dll', 'Microsoft.IdentityModel.JsonWebTokens.dll', 'Microsoft.Win32.Primitives.dll', 'sqlpackage', 'sqlpackage.deps.json', 'sqlpackage.dll', 'sqlpackage.pdb', 'sqlpackage.runtimeconfig.json', 'sqlpackage.xml', 'System.Collections.Concurrent.dll', 'System.Collections.dll', 'System.Console.dll', 'System.Diagnostics.FileVersionInfo.dll', 'System.Diagnostics.StackTrace.dll', 'System.Diagnostics.TextWriterTraceListener.dll', 'System.Diagnostics.TraceSource.dll', 'System.Linq.dll', 'System.Memory.dll', 'System.Net.Http.Json.dll', 'System.Private.CoreLib.dll', 'System.Private.Xml.dll', 'System.Reflection.Metadata.dll', 'System.Runtime.dll', 'System.Runtime.Serialization.Json.dll', 'System.Security.Cryptography.Algorithms.dll', 'System.Security.Cryptography.Primitives.dll', 'System.Text.Json.dll', 'System.Threading.dll', 'System.Threading.Thread.dll', 'System.Xml.ReaderWriter.dll'
$linux = 'libclrjit.so', 'libcoreclr.so', 'libcoreclrtraceptprovider.so', 'libhostfxr.so', 'libhostpolicy.so', 'libSystem.Native.so', 'libSystem.Security.Cryptography.Native.OpenSsl.so', 'Microsoft.Win32.Primitives.dll', 'System.Collections.Concurrent.dll', 'System.Collections.dll', 'System.Console.dll', 'System.Diagnostics.FileVersionInfo.dll', 'System.Diagnostics.StackTrace.dll', 'System.Diagnostics.TextWriterTraceListener.dll', 'System.Diagnostics.TraceSource.dll', 'System.Linq.dll', 'System.Memory.dll', 'System.Net.Http.Json.dll', 'System.Private.CoreLib.dll', 'System.Private.Xml.dll', 'System.Reflection.Metadata.dll', 'System.Runtime.dll', 'System.Runtime.Serialization.Json.dll', 'System.Security.Cryptography.Algorithms.dll', 'System.Text.Json.dll', 'System.Threading.dll', 'System.Threading.Thread.dll', 'System.Xml.ReaderWriter.dll', 'sqlpackage', 'sqlpackage.dll', 'sqlpackage.deps.json', 'sqlpackage.runtimeconfig.json'
$winfull = 'Microsoft.Data.SqlClient.dll', 'Microsoft.Data.SqlClient.SNI.x64.dll', 'Microsoft.Data.SqlClient.SNI.x86.dll', 'System.Threading.Tasks.Dataflow.dll', 'Azure.Core.dll', 'Azure.Identity.dll', 'Microsoft.Build.dll', 'Microsoft.Build.Framework.dll', 'Microsoft.Data.Tools.Schema.Sql.dll', 'Microsoft.Data.Tools.Utilities.dll', 'Microsoft.SqlServer.Dac.dll', 'Microsoft.SqlServer.Dac.Extensions.dll', 'Microsoft.SqlServer.TransactSql.ScriptDom.dll', 'Microsoft.SqlServer.Types.dll', 'System.Memory.Data.dll', 'System.Resources.Extensions.dll', 'System.Security.SecureString.dll', 'sqlpackage.exe', 'sqlpackage.dll', 'libhostfxr.so', 'libhostpolicy.so', 'sqlpackage.runtimeconfig.json', 'sqlpackage.deps.json', 'hostpolicy.dll', 'hostfxr.dll', 'sqlpackage.dll'

Get-ChildItem "./temp/dacfull/" -Include *.dll, *.exe -Recurse | Copy-Item -Destination lib/sqlpackage/windows
Get-ChildItem "./temp/xe/*.dll" -Recurse | Copy-Item -Destination third-party/XESmartTarget
Get-ChildItem "./temp/bogus/*/netstandard2.0/bogus.dll" -Recurse | Copy-Item -Destination third-party/bogus/netstandard2.0/bogus.dll
Get-ChildItem "./temp/bogus/*/net40/bogus.dll" -Recurse | Copy-Item -Destination third-party/bogus/net40/bogus.dll
Copy-Item .\temp\LumenWorksCsvReader\lib\net461\LumenWorks.Framework.IO.dll -Destination ./third-party/LumenWorks/net461/LumenWorks.Framework.IO.dll
Copy-Item .\temp\LumenWorksCsvReader\lib\netstandard2.0\LumenWorks.Framework.IO.dll -Destination ./third-party/LumenWorks/netstandard2.0/LumenWorks.Framework.IO.dll

Get-ChildItem lib/net462/dbatools.dll | Remove-Item -Force
Get-ChildItem lib/net6.0/dbatools.dll | Remove-Item -Force
Get-ChildItem lib/net462/dbatools.dll.config | Remove-Item -Force
Get-ChildItem lib/net6.0/dbatools.dll.config | Remove-Item -Force

Get-ChildItem ./temp/linux | Where-Object Name -in $linux | Copy-Item -Destination lib/net6.0
Get-ChildItem ./temp/macos | Where-Object Name -in $mac | Copy-Item -Destination lib/sqlpackage/mac/

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
Install-Package @parms

$parms.Name = "Microsoft.SqlServer.DacFx"
$parms.RequiredVersion = "161.6319.0-preview"
Install-Package @parms

$parms.Name = "Microsoft.SqlServer.SqlManagementObjects"
$parms.RequiredVersion = "170.7.0-preview"
#Install-Package @parms

$parms.Name = "Microsoft.Data.SqlClient"
$parms.RequiredVersion = "5.1.4"
#Install-Package @parms

$parms.Name = "Microsoft.Data.SqlClient.SNI.runtime"
$parms.RequiredVersion = "5.2.0"
#Install-Package @parms

$parms.Name = "Microsoft.Identity.Client"
$parms.RequiredVersion = "4.53.0"
#Install-Package @parms

$parms.Name = "Microsoft.SqlServer.Server"
$parms.RequiredVersion = "1.0.0"
#Install-Package @parms

$parms.Name = "Azure.Identity"
$parms.RequiredVersion = "1.10.3"
#Install-Package @parms

Copy-Item "C:\temp\nuget\Microsoft.Data.SqlClient.5.1.4\runtimes\unix\lib\netcoreapp3.1\Microsoft.Data.SqlClient.dll" -Destination lib/net6.0/publish
Copy-Item "C:\temp\nuget\Microsoft.Identity.Client.4.53.0\lib\net461\Microsoft.Identity.Client.dll" -Destination lib/net462/publish/
Copy-Item "C:\temp\nuget\Microsoft.Data.SqlClient.5.1.4\runtimes\win\lib\netcoreapp3.1\Microsoft.Data.SqlClient.dll" -Destination lib/net6.0/publish/win-sqlclient
Copy-Item "C:\temp\nuget\Microsoft.Identity.Client.4.53.0\lib\netcoreapp2.1\Microsoft.Identity.Client.dll" -Destination lib/net6.0/publish/win-sqlclient
Copy-Item "C:\temp\nuget\Microsoft.Data.SqlClient.SNI.runtime.5.2.0\runtimes\win-x64\native\Microsoft.Data.SqlClient.SNI.dll" -Destination lib/net6.0/publish/win-sqlclient
Copy-Item "C:\temp\nuget\Microsoft.Data.SqlClient.SNI.runtime.5.2.0\runtimes\win-x64\native\Microsoft.Data.SqlClient.SNI.dll" -Destination lib/net462/publish/

Copy-Item "replication/*.dll" -Destination lib/net462/publish/
Copy-Item "replication/*.dll" -Destination lib/net6.0/publish/

Move-Item -Path lib/net6.0/publish/* -Destination lib/net6.0/
Move-Item -Path lib/net462/publish/* -Destination lib/net462/

Remove-Item -Path lib/net6.0/publish -Recurse -ErrorAction Ignore
Remove-Item -Path lib/net462/publish -Recurse -ErrorAction Ignore

Remove-Item -Path lib/*.xml -Recurse -ErrorAction Ignore
Remove-Item -Path lib/*.pdb -Recurse -ErrorAction Ignore

Get-ChildItem -Directory -Path .\lib\net462 | Where-Object Name -notin 'win-sqlclient', 'x64', 'x86', 'win', 'mac', 'macos' | Remove-Item -Recurse
Get-ChildItem -Directory -Path .\lib\net6.0 | Where-Object Name -notin 'win-sqlclient', 'x64', 'x86', 'win', 'mac', 'macos' | Remove-Item -Recurse


Import-Module C:\github\dbatools.library\dbatools.core.library.psd1 -Force; Import-Module C:\github\dbatools -Force

Import-Module C:\github\dbatools -Force

$script:instance1 = $script:instance2 = "sqlcs"
Set-DbatoolsConfig -FullName sql.connection.trustcert -Value $true
$db = Get-DbaDatabase -SqlInstance $script:instance1 -Database dbatoolsci_publishdacpac
$publishprofile = New-DbaDacProfile -SqlInstance $script:instance1 -Database dbatoolsci_publishdacpac -Path C:\temp
$extractOptions = New-DbaDacOption -Action Export
$extractOptions.ExtractAllTableData = $true
$dacpac = Export-DbaDacPackage -SqlInstance $script:instance1 -Database dbatoolsci_publishdacpac -DacOption $extractOptions
$dacpac
$dacpac | Publish-DbaDacPackage -PublishXml $publishprofile.FileName -Database butt -SqlInstance $script:instance2 -Confirm:$false -Verbose
$Error | Select-Object *


#Data Source=sqlcs;Integrated Security=True;Encrypt=True;Trust Server Certificate=True;

<#
New-Object -TypeName Microsoft.SqlServer.Dac.DacServices -ArgumentList 'Data Source=sqlcs;Integrated Security=True;MultipleActiveResultSets=False;Encrypt=False;TrustServerCertificate=true;Packet Size=4096;Application Name="dbatools PowerShell module - dbatools.io";Database=dbatoolsci_publishdacpac';Connect-DbaInstance -SqlInstance sqlcs

$Error | Select-Object *

### LINUX #####
Import-Module ./dbatools.library -Force; Import-Module ./dbatools -Force; New-Object -TypeName Microsoft.SqlServer.Dac.DacServices -ArgumentList 'Data Source=sqlcs;Integrated Security=True;MultipleActiveResultSets=False;Encrypt=False;TrustServerCertificate=true;Packet Size=4096;Application Name="dbatools PowerShell module - dbatools.io";Database=dbatoolsci_publishdacpac';Connect-DbaInstance -SqlInstance sqlcs -TrustServerCertificate


Import-Module /mnt/c/github/dbatools.library/dbatools.core.library.psd1 -Force; ipmo /mnt/c/github/dbatools -Force; Connect-DbaInstance -SqlInstance sqlcs -TrustServerCertificate

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

# Sign the DLLs, how cool -- Set-AuthenticodeSignature works on DLLs (and presumably Exes) too
$buffer = [IO.File]::ReadAllBytes("C:\github\dbatools-code-signing-cert.pfx")
$certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::New($buffer, $pw.Password)
Get-ChildItem dbatools.dll -Recurse | Set-AuthenticodeSignature -Certificate $certificate -TimestampServer http://timestamp.digicert.com -HashAlgorithm SHA256


#>