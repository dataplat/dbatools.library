# Assembly resolution and platform detection for dbatools.library

function Get-DbatoolsPlatformInfo {
    [CmdletBinding()]
    param()

    # Determine OS platform
    $platform = if ($PSVersionTable.PSVersion.Major -lt 6 -or $IsWindows) {
        'Windows'
    } elseif ($IsLinux) {
        'Linux'
    } else {
        'OSX'
    }

    # Determine architecture
    $architecture = if ($platform -eq 'Windows') {
        if ([System.Environment]::Is64BitProcess) { 'x64' } else { 'x86' }
    } else {
        'x64' # Linux/OSX assumed x64
    }

    # Determine .NET runtime
    $runtime = if ($PSVersionTable.PSEdition -eq 'Core') {
        'core'
    } else {
        'desktop'
    }

    return @{
        Platform = $platform
        Architecture = $architecture
        Runtime = $runtime
    }
}

function Get-DbatoolsAssemblyPath {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory)]
        [string]$AssemblyName,

        [Parameter(Mandatory)]
        [string]$Platform,

        [Parameter(Mandatory)]
        [string]$Architecture,

        [Parameter(Mandatory)]
        [string]$Runtime
    )

    # Check if it's a DAC assembly
    $isDac = $script:DacAssemblies.ContainsKey($AssemblyName)

    # Check if it's SqlClient
    $isSqlClient = $AssemblyName -eq 'Microsoft.Data.SqlClient'

    # Resolution logic based on assembly type
    if ($isDac) {
        # DAC assemblies are strictly platform specific
        $basePath = $script:PlatformAssemblies[$Platform]['DAC']
        $assemblyPath = Join-Path $basePath "$AssemblyName.dll"
    }
    elseif ($isSqlClient -and $Platform -eq 'Windows') {
        # Windows SqlClient has architecture-specific paths
        $basePath = $script:PlatformAssemblies[$Platform][$Architecture]
        $assemblyPath = Join-Path $basePath "$AssemblyName.dll"

        # Fallback to core version if platform-specific not found
        if (-not (Test-Path $assemblyPath)) {
            $basePath = Join-Path $script:libraryroot "lib/core"
            $assemblyPath = Join-Path $basePath "$AssemblyName.dll"
        }
    }
    else {
        # Common assemblies and non-Windows SqlClient use runtime-specific paths
        $basePath = Join-Path $script:libraryroot "lib/$Runtime"
        $assemblyPath = Join-Path $basePath "$AssemblyName.dll"
    }

    return $assemblyPath
}

# Assembly resolution event handler
$script:onAssemblyResolveEventHandler = [System.ResolveEventHandler] {
    param($sender, $args)

    try {
        # Parse assembly name
        $assemblyName = [System.Reflection.AssemblyName]::new($args.Name)

        # Get platform info
        $platformInfo = Get-DbatoolsPlatformInfo

        # Get assembly path
        $assemblyPath = Get-DbatoolsAssemblyPath `
            -AssemblyName $assemblyName.Name `
            -Platform $platformInfo.Platform `
            -Architecture $platformInfo.Architecture `
            -Runtime $platformInfo.Runtime

        if (Test-Path $assemblyPath) {
            Write-Verbose "Loading assembly from: $assemblyPath"
            return [System.Reflection.Assembly]::LoadFrom($assemblyPath)
        }

        Write-Warning "Assembly not found: $($assemblyName.Name)"
        return $null
    }
    catch {
        Write-Warning "Error resolving assembly $($args.Name): $_"
        return $null
    }
}

# Initialize assembly resolution for non-Core PowerShell
if ($PSVersionTable.PSEdition -ne 'Core') {
    [System.AppDomain]::CurrentDomain.add_AssemblyResolve($script:onAssemblyResolveEventHandler)
}

Export-ModuleMember -Function Get-DbatoolsPlatformInfo, Get-DbatoolsAssemblyPath