# Assembly loading and initialization for dbatools.library

function Initialize-SqlClientNativeLibraries {
    [CmdletBinding()]
    param(
        [string]$Platform,
        [string]$Architecture
    )

    if ($Platform -eq 'Windows') {
        $platformInfo = $script:PlatformAssemblies[$Platform][$Architecture]
        if (-not $platformInfo -or -not $platformInfo.NativePath) {
            Write-Warning "Native path configuration not found for $Platform $Architecture"
            return
        }

        # Add native path to PATH environment variable temporarily
        $oldPath = [Environment]::GetEnvironmentVariable("PATH")
        [Environment]::SetEnvironmentVariable("PATH", "$($platformInfo.NativePath);$oldPath")

        # Load native SqlClient SNI library first
        $sniPath = Join-Path $platformInfo.NativePath "Microsoft.Data.SqlClient.SNI.$Architecture.dll"
        if (Test-Path $sniPath) {
            try {
                Write-Verbose "Loading native SqlClient dependency from: $sniPath"
                Add-Type -Path $sniPath -ErrorAction Stop
                Write-Verbose "Successfully loaded native SqlClient dependency"
            } catch {
                Write-Warning "Failed to load native SqlClient dependency from $sniPath : $_"
            }
        } else {
            Write-Warning "Native SqlClient dependency not found: $sniPath"
        }
    }
}

function Initialize-DbatoolsAssemblyLoader {
    [CmdletBinding()]
    param()

    # Get platform information
    $platformInfo = Get-DbatoolsPlatformInfo

    Write-Verbose "Initializing assembly loader for platform: $($platformInfo.Platform), architecture: $($platformInfo.Architecture), runtime: $($platformInfo.Runtime)"

    # Initialize native dependencies first
    if ($platformInfo.Platform -eq 'Windows') {
        Write-Verbose "Initializing native dependencies for Windows $($platformInfo.Architecture)"
        Initialize-SqlClientNativeLibraries -Platform $platformInfo.Platform `
                                          -Architecture $platformInfo.Architecture
    }

    # Pre-load dependency assemblies first
    $dependencyAssemblies = @(
        'Microsoft.Identity.Client'
        'Microsoft.Identity.Client.Extensions.Msal'
    )

    foreach ($depAssembly in $dependencyAssemblies) {
        try {
            Write-Verbose "Pre-loading dependency: $depAssembly"
            $assemblyPath = Get-DbatoolsAssemblyPath `
                -AssemblyName $depAssembly `
                -Platform $platformInfo.Platform `
                -Architecture $platformInfo.Architecture `
                -Runtime $platformInfo.Runtime

            if (Test-Path $assemblyPath) {
                [System.Reflection.Assembly]::LoadFrom($assemblyPath) | Out-Null
                Write-Verbose "Successfully loaded dependency: $depAssembly"
            }
        } catch {
            Write-Warning "Failed to load dependency $depAssembly : $_"
        }
    }

    # Pre-load core assemblies in specified order
    Write-Verbose "Loading core assemblies..."
    foreach ($assemblyName in $script:AssemblyLoadOrder) {
        try {
            Write-Verbose "Attempting to load: $assemblyName"
            $assemblyPath = Get-DbatoolsAssemblyPath `
                -AssemblyName $assemblyName `
                -Platform $platformInfo.Platform `
                -Architecture $platformInfo.Architecture `
                -Runtime $platformInfo.Runtime

            if (Test-Path $assemblyPath) {
                Write-Verbose "Pre-loading assembly: $assemblyName"
                try {
                    [System.Reflection.Assembly]::LoadFrom($assemblyPath) | Out-Null
                    Write-Verbose "Successfully loaded: $assemblyName"
                } catch {
                    # Special handling for SqlClient
                    if ($assemblyName -eq 'Microsoft.Data.SqlClient') {
                        Write-Warning "Failed to load platform-specific SqlClient, attempting fallback: $_"
                        $fallbackPath = Join-Path $script:libraryroot "lib/core/$assemblyName.dll"
                        if (Test-Path $fallbackPath) {
                            [System.Reflection.Assembly]::LoadFrom($fallbackPath) | Out-Null
                            Write-Verbose "Successfully loaded core SqlClient as fallback"
                        } else {
                            throw "Failed to load SqlClient and no fallback available"
                        }
                    } else {
                        throw
                    }
                }
            } else {
                Write-Warning "Assembly not found: $assemblyPath"
            }
        }
        catch {
            throw "Failed to load assembly $assemblyName : $_"
        }
    }
}

function Test-DbatoolsAssemblyLoading {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory)]
        [string]$AssemblyName
    )

    try {
        $platformInfo = Get-DbatoolsPlatformInfo
        $assemblyPath = Get-DbatoolsAssemblyPath `
            -AssemblyName $AssemblyName `
            -Platform $platformInfo.Platform `
            -Architecture $platformInfo.Architecture `
            -Runtime $platformInfo.Runtime

        if (-not (Test-Path $assemblyPath)) {
            Write-Warning "Assembly file not found: $assemblyPath"
            return $false
        }

        $assembly = [System.Reflection.Assembly]::LoadFrom($assemblyPath)
        Write-Verbose "Successfully loaded $($assembly.FullName)"
        return $true
    }
    catch {
        Write-Warning "Failed to load $AssemblyName : $_"
        return $false
    }
}

function Get-DbatoolsLoadedAssembly {
    [CmdletBinding()]
    param()

    $loadedAssemblies = [System.AppDomain]::CurrentDomain.GetAssemblies()
    $relevantAssemblies = $loadedAssemblies | Where-Object {
        $name = $_.GetName().Name
        $script:CoreAssemblies.ContainsKey($name) -or $script:DacAssemblies.ContainsKey($name)
    }

    return $relevantAssemblies | ForEach-Object {
        [PSCustomObject]@{
            Name = $_.GetName().Name
            Version = $_.GetName().Version
            Location = $_.Location
            Runtime = if ($_.ImageRuntimeVersion -like 'v4.*') { 'desktop' } else { 'core' }
        }
    }
}

function Reset-DbatoolsAssemblyLoader {
    [CmdletBinding()]
    param()

    # Remove assembly resolve handler in non-Core PowerShell
    if ($PSVersionTable.PSEdition -ne 'Core') {
        [System.AppDomain]::CurrentDomain.remove_AssemblyResolve($script:onAssemblyResolveEventHandler)
    }

    # Re-initialize the assembly loader
    Initialize-DbatoolsAssemblyLoader

    # Re-add assembly resolve handler in non-Core PowerShell
    if ($PSVersionTable.PSEdition -ne 'Core') {
        [System.AppDomain]::CurrentDomain.add_AssemblyResolve($script:onAssemblyResolveEventHandler)
    }
}

# Export functions for module use
Export-ModuleMember -Function Initialize-DbatoolsAssemblyLoader,
                              Test-DbatoolsAssemblyLoading,
                              Get-DbatoolsLoadedAssembly,
                              Reset-DbatoolsAssemblyLoader