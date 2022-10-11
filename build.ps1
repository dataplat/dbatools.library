# Go compile the DLLs
Set-Location C:\github\dbatools-library
Push-Location ".\project"
dotnet publish --configuration release --framework net6.0 | Out-String -OutVariable build
dotnet publish --configuration release --framework net462 | Out-String -OutVariable build
dotnet test --framework net462 --verbosity normal | Out-String -OutVariable test
dotnet test --framework net6.0 --verbosity normal | Out-String -OutVariable test


Get-ChildItem ..\bin\net462\ -Exclude *dbatools*, publish | Remove-Item -Force -Recurse
Get-ChildItem ..\bin\ -Include runtimes -Recurse | Remove-Item -Force -Recurse
Get-ChildItem ..\bin\*\dbatools.deps.json -Recurse | Remove-Item -Force

Pop-Location
<#
# Remove all the SMO directories that the build created -- they are elsewhere in the project
Get-ChildItem -Directory ".\bin\net462" | Remove-Item -Recurse -Confirm:$false
Get-ChildItem -Directory ".\bin\net6.0" | Remove-Item -Recurse -Confirm:$false

# Remove all the SMO files that the build created -- they are elsewhere in the project
Get-ChildItem ".\bin\net6.0" -Recurse -Exclude dbatools.* | Move-Item -Destination ".\bin\smo\coreclr" -Confirm:$false -Force
Get-ChildItem ".\bin\net462" -Recurse -Exclude dbatools.* | Move-Item -Destination ".\bin\smo\" -Confirm:$false -Force
Get-ChildItem ".\bin" -Recurse -Include dbatools.deps.json | Remove-Item -Confirm:$false

# Sign the DLLs, how cool -- Set-AuthenticodeSignature works on DLLs (and presumably Exes) too
#$buffer = [IO.File]::ReadAllBytes("C:\github\dbatools-code-signing-cert.pfx")
#$certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::New($buffer, $pw.Password)
#Get-ChildItem dbatools\dbatools.dll -Recurse | Set-AuthenticodeSignature -Certificate $certificate -TimestampServer http://timestamp.digicert.com -HashAlgorithm SHA256
#>