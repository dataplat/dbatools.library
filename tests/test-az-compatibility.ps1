#!/usr/bin/env pwsh

[CmdletBinding()]
param (
    [string]$ModulePath = (Join-Path $PSScriptRoot '..\artifacts\dbatools.library\dbatools.library.psd1'),

    [string]$AzAccountsVersion = '5.5.1'
)

$ErrorActionPreference = 'Stop'

$minimumVersions = [ordered]@{
    'Azure.Core'               = [Version]'1.56.0'
    'Azure.Identity'           = [Version]'1.21.0'
    'System.ClientModel'       = [Version]'1.12.0'
    'Microsoft.Identity.Client' = [Version]'4.84.0'
    'Microsoft.Identity.Client.Extensions.Msal' = [Version]'4.84.0'
}

function Get-ProductVersion {
    param (
        [Parameter(Mandatory)]
        [string]$Path
    )

    $versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo((Resolve-Path -LiteralPath $Path))
    return [Version](($versionInfo.ProductVersion -split '\+')[0])
}

function Assert-PackagedDependencyVersions {
    param (
        [Parameter(Mandatory)]
        [string]$ManifestPath
    )

    $moduleRoot = Split-Path -Parent (Resolve-Path -LiteralPath $ManifestPath)
    foreach ($runtime in 'core', 'desktop') {
        foreach ($dependency in $minimumVersions.GetEnumerator()) {
            $assemblyPath = Join-Path $moduleRoot "$runtime\lib\$($dependency.Key).dll"
            if (-not (Test-Path -LiteralPath $assemblyPath)) {
                throw "Missing packaged dependency: $assemblyPath"
            }

            $actualVersion = Get-ProductVersion -Path $assemblyPath
            if ($actualVersion -lt $dependency.Value) {
                throw "$runtime/lib/$($dependency.Key).dll is $actualVersion; Az.Accounts $AzAccountsVersion compatibility requires $($dependency.Value) or newer."
            }
        }
    }
}

function Invoke-ImportOrderTest {
    param (
        [Parameter(Mandatory)]
        [string]$PowerShellExecutable,

        [Parameter(Mandatory)]
        [string]$HostName,

        [Parameter(Mandatory)]
        [ValidateSet('DbatoolsFirst', 'AzFirst')]
        [string]$ImportOrder,

        [Parameter(Mandatory)]
        [string]$ManifestPath,

        [Parameter(Mandatory)]
        [string]$AzAccountsManifest,

        [Parameter(Mandatory)]
        [string]$DbatoolsMsalExtensionsPath,

        [Parameter(Mandatory)]
        [string]$AzMsalExtensionsPath
    )

    $result = & $PowerShellExecutable -NoProfile -Command {
        param($DbatoolsManifest, $AzManifest, $DbatoolsMsalPath, $AzMsalPath, $RequiredAzVersion, $Order, $RequiredVersions)

        $ErrorActionPreference = 'Stop'
        try {
            if ($Order -eq 'DbatoolsFirst') {
                Import-Module $DbatoolsManifest -Force -ErrorAction Stop
                Add-Type -Path $DbatoolsMsalPath
                Import-Module $AzManifest -Force -ErrorAction Stop
            } else {
                Import-Module $AzManifest -Force -ErrorAction Stop
                Add-Type -Path $AzMsalPath
                Import-Module $DbatoolsManifest -Force -ErrorAction Stop
            }

            $azAccounts = Get-Module Az.Accounts
            if (-not $azAccounts -or $azAccounts.Version -ne [Version]$RequiredAzVersion) {
                throw "Az.Accounts $RequiredAzVersion was not loaded."
            }
            if (-not (Get-AzEnvironment -Name AzureCloud)) {
                throw 'Az.Accounts commands are not usable.'
            }
            if (-not ([Microsoft.SqlServer.Management.Smo.Server] -as [type])) {
                throw 'dbatools SMO types are not usable.'
            }
            foreach ($dependencyName in $RequiredVersions.Keys) {
                $loaded = @([AppDomain]::CurrentDomain.GetAssemblies() |
                    Where-Object { $_.GetName().Name -eq $dependencyName })
                if ($dependencyName -in @('Azure.Core', 'Microsoft.Identity.Client.Extensions.Msal') -and $loaded.Count -eq 0) {
                    throw "Expected $dependencyName to be loaded."
                }
                if ($loaded.Count -eq 0) {
                    continue
                }

                foreach ($loadedAssembly in $loaded) {
                    $versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($loadedAssembly.Location)
                    $actualVersion = [Version](($versionInfo.ProductVersion -split '\+')[0])
                    $requiredVersion = [Version]$RequiredVersions[$dependencyName]
                    if ($actualVersion -lt $requiredVersion) {
                        throw "$dependencyName $actualVersion was loaded; expected $requiredVersion or newer."
                    }
                }
            }

            'PASS'
        } catch {
            "FAIL: $($_.Exception.Message)"
        }
    } -args $ManifestPath, $AzAccountsManifest, $DbatoolsMsalExtensionsPath, $AzMsalExtensionsPath, $AzAccountsVersion, $ImportOrder, $minimumVersions

    if ($LASTEXITCODE -ne 0 -or $result -ne 'PASS') {
        throw "$HostName $ImportOrder compatibility test failed: $result"
    }
}

if (-not (Test-Path -LiteralPath $ModulePath)) {
    throw "Packaged dbatools.library module not found: $ModulePath"
}

Assert-PackagedDependencyVersions -ManifestPath $ModulePath

$installedAzAccounts = Get-Module -ListAvailable Az.Accounts |
    Where-Object { $_.Version -eq [Version]$AzAccountsVersion } |
    Select-Object -First 1
if (-not $installedAzAccounts) {
    throw "Az.Accounts $AzAccountsVersion is required for this compatibility test."
}

$moduleRoot = Split-Path -Parent (Resolve-Path -LiteralPath $ModulePath)
$azModuleRoot = Split-Path -Parent $installedAzAccounts.Path
$azMsalExtensions = Get-ChildItem -LiteralPath $azModuleRoot -Recurse -Filter 'Microsoft.Identity.Client.Extensions.Msal.dll' |
    Select-Object -First 1
if (-not $azMsalExtensions) {
    throw "Az.Accounts $AzAccountsVersion does not contain Microsoft.Identity.Client.Extensions.Msal.dll."
}

$powerShellHosts = [ordered]@{
    'PowerShell Core' = [pscustomobject]@{
        Executable = (Get-Command pwsh -ErrorAction Stop).Source
        Runtime = 'core'
    }
}
if ([Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::Windows)) {
    $powerShellHosts['Windows PowerShell'] = [pscustomobject]@{
        Executable = (Get-Command powershell.exe -ErrorAction Stop).Source
        Runtime = 'desktop'
    }
}

foreach ($powerShellHost in $powerShellHosts.GetEnumerator()) {
    $dbatoolsMsalExtensions = Join-Path $moduleRoot "$($powerShellHost.Value.Runtime)\lib\Microsoft.Identity.Client.Extensions.Msal.dll"
    Invoke-ImportOrderTest -PowerShellExecutable $powerShellHost.Value.Executable -HostName $powerShellHost.Key -ImportOrder DbatoolsFirst -ManifestPath $ModulePath -AzAccountsManifest $installedAzAccounts.Path -DbatoolsMsalExtensionsPath $dbatoolsMsalExtensions -AzMsalExtensionsPath $azMsalExtensions.FullName
    Invoke-ImportOrderTest -PowerShellExecutable $powerShellHost.Value.Executable -HostName $powerShellHost.Key -ImportOrder AzFirst -ManifestPath $ModulePath -AzAccountsManifest $installedAzAccounts.Path -DbatoolsMsalExtensionsPath $dbatoolsMsalExtensions -AzMsalExtensionsPath $azMsalExtensions.FullName
}

Write-Host "Az.Accounts $AzAccountsVersion compatibility tests passed in both import orders on $($powerShellHosts.Keys -join ' and ')."
