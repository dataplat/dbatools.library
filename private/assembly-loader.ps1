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

        # Add native path to PATH if not already present
        $oldPath = [Environment]::GetEnvironmentVariable("PATH", [System.EnvironmentVariableTarget]::Process)
        if (-not $oldPath.Contains($platformInfo.NativePath)) {
            Write-Verbose "Adding native path to PATH: $($platformInfo.NativePath)"
            [Environment]::SetEnvironmentVariable("PATH", "$($platformInfo.NativePath);$oldPath", [System.EnvironmentVariableTarget]::Process)
        }
    }
}

function Initialize-DbatoolsAssemblyLoader {
    [CmdletBinding()]
    param()

    Write-Verbose "Starting assembly loader initialization"
    Write-Verbose "Library root: $script:libraryroot"

    # Log all currently loaded SqlClient assemblies
    Write-Verbose "Currently loaded SqlClient assemblies:"
    [System.AppDomain]::CurrentDomain.GetAssemblies() |
        Where-Object { $_.GetName().Name -like '*SqlClient*' } |
        ForEach-Object {
            Write-Verbose "  Name: $($_.GetName().Name)"
            Write-Verbose "  Version: $($_.GetName().Version)"
            Write-Verbose "  Location: $($_.Location)"
            Write-Verbose "  GAC: $($_.GlobalAssemblyCache)"
        }

    # Get platform information
    $platformInfo = Get-DbatoolsPlatformInfo
    Write-Verbose "Platform: $($platformInfo.Platform), architecture: $($platformInfo.Architecture), runtime: $($platformInfo.Runtime)"

    # Initialize native dependencies first - this must happen before any assembly loading
    if ($platformInfo.Platform -eq 'Windows') {
        Write-Verbose "Initializing native dependencies for Windows $($platformInfo.Architecture)"
        Write-Verbose "Current PATH before native init: $([Environment]::GetEnvironmentVariable('PATH'))"
        Initialize-SqlClientNativeLibraries -Platform $platformInfo.Platform `
                                          -Architecture $platformInfo.Architecture
        Write-Verbose "Current PATH after native init: $([Environment]::GetEnvironmentVariable('PATH'))"

        # Verify native DLLs exist
        $nativePath = $script:PlatformAssemblies['Windows'][$platformInfo.Architecture].NativePath
        $expectedDlls = @('Microsoft.Data.SqlClient.SNI.dll')
        foreach ($dll in $expectedDlls) {
            $dllPath = Join-Path $nativePath $dll
            Write-Verbose "Checking native DLL: $dllPath exists: $(Test-Path $dllPath)"
        }
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
            Write-Verbose "Processing assembly: $assemblyName"

            # Check if already loaded
            $loadedAssembly = [System.AppDomain]::CurrentDomain.GetAssemblies() |
                Where-Object { $_.GetName().Name -eq $assemblyName }

            if ($loadedAssembly) {
                Write-Verbose "Found loaded assembly: $assemblyName"
                Write-Verbose "  Location: $($loadedAssembly.Location)"
                Write-Verbose "  Version: $($loadedAssembly.GetName().Version)"
                Write-Verbose "  Runtime Version: $($loadedAssembly.ImageRuntimeVersion)"
                Write-Verbose "  Global Assembly Cache: $($loadedAssembly.GlobalAssemblyCache)"
                Write-Verbose "  Full Name: $($loadedAssembly.FullName)"
                Write-Verbose "Using already loaded assembly"
                continue
            }

            $assemblyPath = Get-DbatoolsAssemblyPath `
                -AssemblyName $assemblyName `
                -Platform $platformInfo.Platform `
                -Architecture $platformInfo.Architecture `
                -Runtime $platformInfo.Runtime

            if (Test-Path $assemblyPath) {
                Write-Verbose "Loading assembly from path: $assemblyPath"
                try {
                    $assembly = [System.Reflection.Assembly]::LoadFrom($assemblyPath)
                    Write-Verbose "Successfully loaded: $assemblyName from $($assembly.Location)"

                    # Add detailed logging for SqlClient
                    if ($assemblyName -eq 'Microsoft.Data.SqlClient') {
                        Write-Verbose "Checking SqlClient native dependencies..."
                        $nativePath = $script:PlatformAssemblies['Windows'][$platformInfo.Architecture].NativePath
                        Write-Verbose "Native path configured as: $nativePath"
                        Write-Verbose "Current process PATH: $([Environment]::GetEnvironmentVariable('PATH'))"
                    }

                    $assembly | Out-Null
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