# Go compile the DLLs
Set-Location ./dbatools-library
Remove-Item .\project\dbatools\obj -Recurse -ErrorAction Ignore
Remove-Item .\project\dbatools.Tests\lib -Recurse -ErrorAction Ignore
Remove-Item .\project\dbatools.Tests\obj -Recurse -ErrorAction Ignore
Get-ChildItem -Recurse lib | Remove-Item -Recurse -ErrorAction Ignore
Get-ChildItem -Recurse temp | Remove-Item -Recurse -ErrorAction Ignore
Push-Location ".\project"
dotnet clean
dotnet publish --configuration release --framework net6.0 --self-contained | Out-String -OutVariable build
dotnet publish --configuration release --framework net462 --self-contained | Out-String -OutVariable build
dotnet test --framework net462 --verbosity normal | Out-String -OutVariable test
dotnet test --framework net6.0 --verbosity normal | Out-String -OutVariable test
Pop-Location

Get-ChildItem .\lib -Recurse -Include *.pdb | Remove-Item -Force
Get-ChildItem .\lib -Recurse -Include *.xml | Remove-Item -Force
Get-ChildItem .\lib\net462\ -Exclude *dbatools*, publish | Remove-Item -Force -Recurse
Get-ChildItem .\lib\ -Include runtimes -Recurse | Remove-Item -Force -Recurse
Get-ChildItem .\lib\*\dbatools.deps.json -Recurse | Remove-Item -Force

if ($IsLinux -or $IsMacOs) {
    $tempdir = "/tmp"
} else {
    $tempdir = "C:\temp"
}

$null = mkdir $tempdir -Force -ErrorAction Ignore
$null = mkdir ./temp/dacfull -Force -ErrorAction Ignore
$null = mkdir ./lib/sqlpackage/windows -Force -ErrorAction Ignore
$null = mkdir ./lib/sqlpackage/mac -ErrorAction Ignore
$null = mkdir ./temp/xe -ErrorAction Ignore
$null = mkdir ./lib/third-party
$null = mkdir ./lib/third-party/XESmartTarget
$null = mkdir ./lib/third-party/bogus
$null = mkdir ./lib/third-party/LumenWorks
$null = mkdir ./lib/third-party/LumenWorks/netstandard2.0
$null = mkdir ./lib/third-party/LumenWorks/net461
$null = mkdir ./lib/third-party/bogus/netstandard2.0
$null = mkdir ./lib/third-party/bogus/net40
$null = mkdir ./temp/bogus
$null = mkdir ./lib/net6.0/publish/win

$ProgressPreference = "SilentlyContinue"


Invoke-WebRequest -Uri https://aka.ms/sqlpackage-linux -OutFile .\temp\sqlpackage-linux.zip
Invoke-WebRequest -Uri https://aka.ms/sqlpackage-macos -OutFile .\temp\sqlpackage-macos.zip
Invoke-WebRequest -Uri https://aka.ms/dacfx-msi -OutFile .\temp\DacFramework.msi
Invoke-WebRequest -Uri https://www.nuget.org/api/v2/package/Bogus -OutFile .\temp\bogus.zip
Invoke-WebRequest -Uri https://www.nuget.org/api/v2/package/LumenWorksCsvReader -OutFile .\temp\LumenWorksCsvReader.zip
Invoke-WebRequest -Uri https://github.com/spaghettidba/XESmartTarget/releases/download/v1.4.9/XESmartTarget_x64.msi -OutFile .\temp\XESmartTarget_x64.msi

$ProgressPreference = "Continue"

Expand-Archive -Path .\temp\sqlpackage-linux.zip -DestinationPath .\temp\linux
Expand-Archive -Path .\temp\sqlpackage-macos.zip -DestinationPath .\temp\macos
Expand-Archive -Path .\temp\LumenWorksCsvReader.zip -DestinationPath .\temp\LumenWorksCsvReader
Expand-Archive -Path .\temp\bogus.zip -DestinationPath .\temp\bogus

msiexec /a $(Resolve-Path .\temp\DacFramework.msi) /qb TARGETDIR=$(Resolve-Path .\temp\dacfull)
Start-Sleep 3
msiexec /a $(Resolve-Path .\temp\XESmartTarget_x64.msi) /qb TARGETDIR=$(Resolve-Path .\temp\xe)
Start-Sleep 3

$mac = 'libclrjit.dylib', 'libcoreclr.dylib', 'libhostfxr.dylib', 'libhostpolicy.dylib', 'libSystem.Native.dylib', 'libSystem.Security.Cryptography.Native.Apple.dylib', 'Microsoft.Data.Tools.Schema.Sql.dll', 'Microsoft.Data.Tools.Utilities.dll', 'Microsoft.IdentityModel.JsonWebTokens.dll', 'Microsoft.Win32.Primitives.dll', 'sqlpackage', 'sqlpackage.deps.json', 'sqlpackage.dll', 'sqlpackage.pdb', 'sqlpackage.runtimeconfig.json', 'sqlpackage.xml', 'System.Collections.Concurrent.dll', 'System.Collections.dll', 'System.Console.dll', 'System.Diagnostics.FileVersionInfo.dll', 'System.Diagnostics.StackTrace.dll', 'System.Diagnostics.TextWriterTraceListener.dll', 'System.Diagnostics.TraceSource.dll', 'System.Linq.dll', 'System.Memory.dll', 'System.Net.Http.Json.dll', 'System.Private.CoreLib.dll', 'System.Private.Xml.dll', 'System.Reflection.Metadata.dll', 'System.Runtime.dll', 'System.Runtime.Serialization.Json.dll', 'System.Security.Cryptography.Algorithms.dll', 'System.Security.Cryptography.Primitives.dll', 'System.Text.Json.dll', 'System.Threading.dll', 'System.Threading.Thread.dll', 'System.Xml.ReaderWriter.dll'
$linux = 'libclrjit.so', 'libcoreclr.so', 'libcoreclrtraceptprovider.so', 'libhostfxr.so', 'libhostpolicy.so', 'libSystem.Native.so', 'libSystem.Security.Cryptography.Native.OpenSsl.so', 'Microsoft.Win32.Primitives.dll', 'System.Collections.Concurrent.dll', 'System.Collections.dll', 'System.Console.dll', 'System.Diagnostics.FileVersionInfo.dll', 'System.Diagnostics.StackTrace.dll', 'System.Diagnostics.TextWriterTraceListener.dll', 'System.Diagnostics.TraceSource.dll', 'System.Linq.dll', 'System.Memory.dll', 'System.Net.Http.Json.dll', 'System.Private.CoreLib.dll', 'System.Private.Xml.dll', 'System.Reflection.Metadata.dll', 'System.Runtime.dll', 'System.Runtime.Serialization.Json.dll', 'System.Security.Cryptography.Algorithms.dll', 'System.Text.Json.dll', 'System.Threading.dll', 'System.Threading.Thread.dll', 'System.Xml.ReaderWriter.dll', 'sqlpackage', 'sqlpackage.dll', 'sqlpackage.deps.json', 'sqlpackage.runtimeconfig.json'

Get-ChildItem "./temp/dacfull/" -Include *.dll, *.exe -Recurse | Copy-Item -Destination lib/sqlpackage/windows
Get-ChildItem "./temp/xe/*.dll" -Recurse | Copy-Item -Destination lib/third-party/XESmartTarget
Get-ChildItem "./temp/bogus/*/netstandard2.0/bogus.dll" -Recurse | Copy-Item -Destination lib/third-party/bogus/netstandard2.0/bogus.dll
Get-ChildItem "./temp/bogus/*/net40/bogus.dll" -Recurse | Copy-Item -Destination lib/third-party/bogus/net40/bogus.dll

Copy-Item .\temp\LumenWorksCsvReader\lib\net461\LumenWorks.Framework.IO.dll -Destination ./lib/third-party/LumenWorks/net461/LumenWorks.Framework.IO.dll

Copy-Item .\temp\LumenWorksCsvReader\lib\netstandard2.0\LumenWorks.Framework.IO.dll -Destination ./lib/third-party/LumenWorks/netstandard2.0/LumenWorks.Framework.IO.dll

Get-ChildItem lib/net462/dbatools.dll | Remove-Item -Force
Get-ChildItem lib/net6.0/dbatools.dll | Remove-Item -Force
Get-ChildItem lib/net462/dbatools.dll.config | Remove-Item -Force
Get-ChildItem lib/net6.0/dbatools.dll.config | Remove-Item -Force

Get-ChildItem ./temp/linux | Where-Object Name -in $linux | Copy-Item -Destination lib/net6.0
Get-ChildItem ./temp/macos | Where-Object Name -in $mac | Copy-Item -Destination lib/sqlpackage/mac/

Register-PackageSource -provider NuGet -name nugetRepository -Location https://www.nuget.org/api/v2 -Trusted -ErrorAction Ignore

$parms = @{
    Provider         = "Nuget"
    Destination      = "$tempdir\nuget"
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
Install-Package @parms

$parms.Name = "Microsoft.Data.SqlClient"
$parms.RequiredVersion = "5.0.1"
Install-Package @parms

$parms.Name = "Microsoft.Data.SqlClient.SNI.runtime"
$parms.RequiredVersion = "5.0.1"
Install-Package @parms

$parms.Name = "Microsoft.Identity.Client"
$parms.RequiredVersion = "4.45.0"
Install-Package @parms

$parms.Name = "Azure.Identity"
$parms.RequiredVersion = "1.6.0"
Install-Package @parms

Copy-Item "$tempdir\nuget\Microsoft.Data.SqlClient.5.0.1\runtimes\unix\lib\netcoreapp3.1\Microsoft.Data.SqlClient.dll" -Destination lib/net6.0/publish
Copy-Item "$tempdir\nuget\Microsoft.Identity.Client.4.45.0\lib\net461\Microsoft.Identity.Client.dll" -Destination lib/net462/publish/
Copy-Item "$tempdir\nuget\Microsoft.Data.SqlClient.5.0.1\runtimes\win\lib\netcoreapp3.1\Microsoft.Data.SqlClient.dll" -Destination lib/net6.0/publish/win
Copy-Item "$tempdir\nuget\Microsoft.Identity.Client.4.45.0\lib\netcoreapp2.1\Microsoft.Identity.Client.dll" -Destination lib/net6.0/publish/win
Copy-Item "$tempdir\nuget\Microsoft.Data.SqlClient.SNI.runtime.5.0.1\runtimes\win-x64\native\Microsoft.Data.SqlClient.SNI.dll" -Destination lib/net6.0/publish/win
Copy-Item "$tempdir\nuget\Microsoft.Data.SqlClient.SNI.runtime.5.0.1\runtimes\win-x64\native\Microsoft.Data.SqlClient.SNI.dll" -Destination lib/net462/publish/

Copy-Item "replication/Microsoft.SqlServer.Rmo.dll" -Destination lib/net462/publish/
Copy-Item "replication/Microsoft.SqlServer.Replication.dll" -Destination lib/net462/publish/
Copy-Item "replication/Microsoft.SqlServer.Rmo.dll" -Destination lib/net6.0/publish/
Copy-Item "replication/Microsoft.SqlServer.Replication.dll" -Destination lib/net6.0/publish/

Move-Item -Path lib/net6.0/publish/* -Destination lib/net6.0/
Move-Item -Path lib/net462/publish/* -Destination lib/net462/

Remove-Item -Path lib/net6.0/publish -Recurse -ErrorAction Ignore
Remove-Item -Path lib/net462/publish -Recurse -ErrorAction Ignore

Remove-Item -Path lib/*.xml -Recurse -ErrorAction Ignore
Remove-Item -Path lib/*.pdb -Recurse -ErrorAction Ignore

Get-ChildItem -Directory -Path .\lib\net462 | Where-Object Name -notin 'x64', 'x86', 'win', 'mac', 'macos' | Remove-Item -Recurse
Get-ChildItem -Directory -Path .\lib\net6.0 | Where-Object Name -notin 'x64', 'x86', 'win', 'mac', 'macos' | Remove-Item -Recurse

Import-Module ./dbatools-library/dbatools-library.psd1 -Force
