# Assembly resolution and platform detection for dbatools.library

function Get-DbatoolsPlatformInfo {
    [CmdletBinding()]
    param()

    # Determine OS platform - handle legacy PowerShell properly
    $platform = if ($PSVersionTable.PSVersion.Major -ge 6) {
        if ($IsWindows) { 'Windows' }
        elseif ($IsLinux) { 'Linux' }
        else { 'OSX' }
    } else {
        'Windows' # PowerShell 5.1 and below only runs on Windows
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

    if ([string]::IsNullOrEmpty($AssemblyName)) {
        throw "Assembly name cannot be empty"
    }

    # Check assembly type
    $isDac = $script:DacAssemblies.ContainsKey($AssemblyName)
    $isSqlClient = $AssemblyName -eq 'Microsoft.Data.SqlClient'
    $isDependency = @(
        'Microsoft.Identity.Client',
        'Microsoft.Identity.Client.Extensions.Msal',
        'Azure.Core',
        'Azure.Identity'
    ) -contains $AssemblyName

    # Base directory for all assemblies
    $libraryBase = $script:libraryroot
    if (-not $libraryBase) {
        throw "Library root path not set"
    }

    # Resolution logic based on assembly type
    if ($isDac) {
        # DAC assemblies are strictly platform specific
        if (-not $script:PlatformAssemblies[$Platform]) {
            throw "Invalid platform: $Platform"
        }
        $basePath = $script:PlatformAssemblies[$Platform]['DAC']
        if (-not $basePath) {
            throw "DAC path not found for platform: $Platform"
        }
        $assemblyPath = Join-Path $basePath "$AssemblyName.dll"
    }
    elseif ($isSqlClient) {
        if ($Platform -eq 'Windows') {
            # Windows SqlClient and dependencies need platform-specific handling
            $platformInfo = $script:PlatformAssemblies[$Platform][$Architecture]
            if (-not $platformInfo) {
                throw "Platform configuration not found for Windows $Architecture"
            }

            if ($isSqlClient) {
                # Check for native dependencies first
                if ($platformInfo.NativePath) {
                    $nativePath = Join-Path $platformInfo.NativePath "Microsoft.Data.SqlClient.SNI.$Architecture.dll"
                    if (Test-Path $nativePath) {
                        Write-Verbose "Native SqlClient dependency found at: $nativePath"
                    } else {
                        Write-Warning "Native SqlClient dependency missing: $nativePath"
                    }
                }
            }

            # For SqlClient and its dependencies, use the platform-specific path
            if ($isSqlClient -or $isDependency) {
                $basePath = $platformInfo.Path
                if (-not $basePath) {
                    throw "Assembly path not found for Windows $Architecture"
                }
                $assemblyPath = Join-Path $basePath "$AssemblyName.dll"

                # Try fallback to core for dependencies
                if (-not (Test-Path $assemblyPath) -and $isDependency) {
                    Write-Verbose "Attempting fallback to core for dependency: $AssemblyName"
                    $basePath = Join-Path $script:libraryroot "lib/core"
                    $assemblyPath = Join-Path $basePath "$AssemblyName.dll"
                }
            }

            # Get main assembly path
            $basePath = $platformInfo.Path
            if (-not $basePath) {
                throw "SqlClient path not found for Windows $Architecture"
            }
            $assemblyPath = Join-Path $basePath "$AssemblyName.dll"

            # Ensure native dependencies are available
            $nativeDll = Join-Path $basePath "Microsoft.Data.SqlClient.SNI.$Architecture.dll"
            if (-not (Test-Path $nativeDll)) {
                Write-Warning "Native SqlClient dependency missing: $nativeDll"
            }

            # Fallback to core version if needed
            if (-not (Test-Path $assemblyPath)) {
                $basePath = Join-Path $libraryBase "lib/core"
                $assemblyPath = Join-Path $basePath "$AssemblyName.dll"
            }
        } else {
            # Non-Windows platforms use core SqlClient
            $basePath = Join-Path $libraryBase "lib/core"
            $assemblyPath = Join-Path $basePath "$AssemblyName.dll"
        }
    }
    else {
        # Common assemblies use runtime-specific paths
        $basePath = Join-Path $libraryBase "lib/$Runtime"
        if (-not (Test-Path $basePath)) {
            throw "Runtime path not found: $basePath"
        }
        $assemblyPath = Join-Path $basePath "$AssemblyName.dll"
    }

    if (-not (Test-Path $assemblyPath)) {
        Write-Warning "Assembly not found at: $assemblyPath"
    }

    return $assemblyPath
}

# Assembly resolution event handler
$script:onAssemblyResolveEventHandler = [System.ResolveEventHandler] {
    param($sender, $args)

    try {
        if (-not $args -or [string]::IsNullOrEmpty($args.Name)) {
            Write-Warning "Empty assembly name provided to resolver"
            return $null
        }

        # Parse assembly name
        $assemblyName = [System.Reflection.AssemblyName]::new($args.Name)
        if ([string]::IsNullOrEmpty($assemblyName.Name)) {
            Write-Warning "Could not parse assembly name: $($args.Name)"
            return $null
        }

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
            try {
                return [System.Reflection.Assembly]::LoadFrom($assemblyPath)
            } catch {
                Write-Warning "Failed to load assembly $($assemblyName.Name): $_"
                return $null
            }
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