# Assembly loading and initialization for dbatools.library

# Function to determine the platform and architecture for assembly loading
function Get-PlatformDetails {
    [CmdletBinding()]
    param()

    # Detect platform
    # isLinux and isMacOS already exist

    if ($PSVersionTable.PSEdition -eq 'Core') {
        # PowerShell Core - use RuntimeInformation
        $isWin = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)
    } else {
        # Windows PowerShell - use environment
        $isWin = $true
    }

    # Detect architecture
    $arch = 'x64'
    if ($env:PROCESSOR_ARCHITECTURE -eq 'x86') {
        $arch = 'x86'
    } elseif ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') {
        $arch = 'arm64'
    } elseif ($env:PROCESSOR_ARCHITECTURE -eq 'ARM') {
        $arch = 'arm'
    }

    # For Linux/OSX on PowerShell Core, we can try to detect ARM
    if (($isLinux -or $isMacOS) -and ($PSVersionTable.PSEdition -eq 'Core')) {
        if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') {
            $arch = 'arm64'
        } elseif ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm') {
            $arch = 'arm'
        }
    }

    # Determine platform string
    $platform = if ($isWin) {
        'Windows'
    } elseif ($isLinux) {
        'Linux'
    } elseif ($isMacOS) {
        'OSX'
    } else {
        throw "Unsupported platform"
    }

    return @{
        Platform = $platform
        Architecture = $arch
        IsWindows = $isWin
        IsLinux = $isLinux
        isMacOS = $isMacOS
    }
}

function Initialize-DbatoolsAssemblyLoader {
    [CmdletBinding()]
    param()

    Write-Verbose "Starting assembly loader initialization"
    Write-Verbose "Library root: $script:libraryroot"

    # Initialize the redirector first for non-Core PowerShell
    if ($PSVersionTable.PSEdition -ne 'Core') {
        Write-Verbose "Initializing assembly redirector for non-Core PowerShell"
        Initialize-DbatoolsAssemblyRedirector
        Write-Verbose "Redirector type exists after init: $('Redirector' -as [type])"
        Write-Verbose "Redirector instance stored: $($null -ne $script:dbatoolsRedirector)"
    }

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
    $platformDetails = Get-PlatformDetails
    Write-Host "Loading assemblies for: $($platformDetails.Platform) $($platformDetails.Architecture)"

    # Get runtime info from Get-DbatoolsPlatformInfo for consistency
    $runtimeInfo = Get-DbatoolsPlatformInfo

    # Initialize native dependencies first - this must happen before any assembly loading
    if ($platformDetails.IsWindows) {
        # Get the correct runtime path for the current architecture
        $nativePath = Join-Path $script:libraryroot "lib/core/runtimes/win-$($platformDetails.Architecture)/native"
        $runtimePath = Join-Path $script:libraryroot "lib/core/runtimes/win/lib/net6.0"

        Write-Host "Using runtime path: $runtimePath"
        Write-Host "Using native path: $nativePath"

        # Verify the SNI DLL exists and get detailed info
        $sniDll = Join-Path $nativePath "Microsoft.Data.SqlClient.SNI.dll"
        if (-not (Test-Path $sniDll)) {
            Write-Warning "SNI DLL not found at: $sniDll"
            Write-Verbose "Directory contents of $nativePath :"
            Get-ChildItem $nativePath | ForEach-Object {
                Write-Verbose "  $($_.Name) - Size: $($_.Length) bytes, LastWrite: $($_.LastWriteTime)"
            }
            throw "SNI DLL not found at: $sniDll"
        }

        # Get detailed file info
        $sniFileInfo = Get-Item $sniDll
        Write-Verbose "Found SNI DLL:"
        Write-Verbose "  Path: $sniDll"
        Write-Verbose "  Size: $($sniFileInfo.Length) bytes"
        Write-Verbose "  Last Modified: $($sniFileInfo.LastWriteTime)"
        Write-Verbose "  Version: $((Get-Item $sniDll).VersionInfo.FileVersion)"
        Write-Host "Found SNI DLL at: $sniDll"

        Write-Verbose "Current PATH before native init: $([Environment]::GetEnvironmentVariable('PATH'))"

        # Update PATH to include native DLL location
        $oldPath = [Environment]::GetEnvironmentVariable("PATH", [System.EnvironmentVariableTarget]::Process)
        if (-not $oldPath.Contains($nativePath)) {
            Write-Host "Adding native path to PATH: $nativePath"
            Write-Verbose "Adding native path to PATH: $nativePath"
            [Environment]::SetEnvironmentVariable("PATH", "$nativePath;$oldPath", [System.EnvironmentVariableTarget]::Process)
        }

        # Double check PATH was updated and verify native path location
        $newPath = [Environment]::GetEnvironmentVariable("PATH", [System.EnvironmentVariableTarget]::Process)
        Write-Verbose "Updated PATH: $newPath"

        # Split PATH and check native path position
        $pathElements = $newPath -split ';'
        $nativePathIndex = [array]::IndexOf($pathElements, $nativePath)
        Write-Verbose "Native path position in PATH: $nativePathIndex"
        Write-Verbose "PATH elements in order:"
        $pathElements | ForEach-Object {
            Write-Verbose "  $_"
        }

        # Verify native path is in PATH
        if (-not $newPath.Contains($nativePath)) {
            throw "Failed to update PATH with native DLL location"
        }
        Write-Verbose "Current PATH after native init: $([Environment]::GetEnvironmentVariable('PATH'))"

        # Add extra debug logging with file details
        Write-Verbose "Native DLL directory contents with details:"
        Get-ChildItem $nativePath | ForEach-Object {
            Write-Verbose "  $($_.Name)"
            Write-Verbose "    Size: $($_.Length) bytes"
            Write-Verbose "    LastWrite: $($_.LastWriteTime)"
            if ($_.Extension -eq '.dll') {
                Write-Verbose "    Version: $($_.VersionInfo.FileVersion)"
                Write-Verbose "    Product: $($_.VersionInfo.ProductName)"
                Write-Verbose "    Description: $($_.VersionInfo.FileDescription)"
            }
        }

        # Try to verify DLL can be loaded
        Write-Verbose "Attempting to verify SNI DLL can be loaded..."
        try {
            Add-Type -Path $sniDll -ErrorAction Stop
            Write-Verbose "Successfully verified SNI DLL can be loaded"
        }
        catch {
            Write-Warning "Failed to verify SNI DLL loading: $($_.Exception.Message)"
            Write-Verbose "Full error details: $($_)"
        }
    }

    # Pre-load Azure identity and other required dependencies
    $dependencyAssemblies = @(
        'Microsoft.Identity.Client'
        'Microsoft.Identity.Client.Extensions.Msal'
        'Microsoft.IdentityModel.Abstractions'
        'System.Configuration.ConfigurationManager'
    )

    foreach ($depAssembly in $dependencyAssemblies) {
        try {
            Write-Verbose "Pre-loading dependency: $depAssembly"
            $assemblyParams = @{
                AssemblyName = $depAssembly
                Platform = $platformDetails.Platform
                Architecture = $platformDetails.Architecture
                Runtime = $runtimeInfo.Runtime
            }
            $assemblyPath = Get-DbatoolsAssemblyPath @assemblyParams

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

            $assemblyParams = @{
                AssemblyName = $assemblyName
                Platform = $platformDetails.Platform
                Architecture = $platformDetails.Architecture
                Runtime = $runtimeInfo.Runtime
            }
            $assemblyPath = Get-DbatoolsAssemblyPath @assemblyParams

            if (Test-Path $assemblyPath) {
                Write-Verbose "Loading assembly from path: $assemblyPath"
                try {
                    $assembly = [System.Reflection.Assembly]::LoadFrom($assemblyPath)

                    # Special logging for SqlClient
                    if ($assemblyName -eq 'Microsoft.Data.SqlClient') {
                        Write-Host "Successfully loaded SqlClient:"
                        Write-Host "  Version: $($assembly.GetName().Version)"
                        Write-Host "  Location: $($assembly.Location)"
                    } else {
                        Write-Verbose "Successfully loaded: $assemblyName from $($assembly.Location)"
                    }

                    # Add detailed logging for SqlClient
                    # Extra logging for SqlClient
                    if ($assemblyName -eq 'Microsoft.Data.SqlClient') {
                        Write-Verbose "Successfully loaded SqlClient"
                        Write-Verbose "Version: $($assembly.GetName().Version)"
                        Write-Verbose "Location: $($assembly.Location)"
                        Write-Verbose "Checking native dependencies..."
                        $nativePath = Join-Path $script:libraryroot "lib/core/runtimes/win-$($platformDetails.Architecture)/native"
                        Write-Verbose "Native path configured as: $nativePath"
                        Write-Verbose "Current process PATH: $([Environment]::GetEnvironmentVariable('PATH'))"
                        Write-Verbose "Verifying SNI DLL exists in native path..."
                        $sniDll = Join-Path $nativePath "Microsoft.Data.SqlClient.SNI.dll"
                        Write-Verbose "SNI DLL exists: $(Test-Path $sniDll)"
                    }

                    $assembly | Out-Null
                } catch {
                    throw
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
        $assemblyParams = @{
            AssemblyName = $AssemblyName
            Platform = $platformInfo.Platform
            Architecture = $platformInfo.Architecture
            Runtime = $platformInfo.Runtime
        }
        $assemblyPath = Get-DbatoolsAssemblyPath @assemblyParams

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
        $script:CoreAssemblies -contains $name -or $script:DacAssemblies -contains $name
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

    Write-Verbose "Starting assembly loader reset"

    # Remove assembly resolve handlers in non-Core PowerShell
    if ($PSVersionTable.PSEdition -ne 'Core') {
        Write-Verbose "Removing existing assembly resolve handlers"

        # Remove standard handler
        if ($script:onAssemblyResolveEventHandler) {
            Write-Verbose "Removing standard resolve handler"
            [System.AppDomain]::CurrentDomain.remove_AssemblyResolve($script:onAssemblyResolveEventHandler)
        }

        # Remove redirector handler if it exists
        if ($script:dbatoolsRedirector) {
            Write-Verbose "Removing redirector resolve handler"
            [System.AppDomain]::CurrentDomain.remove_AssemblyResolve($script:dbatoolsRedirector.EventHandler)
            $script:dbatoolsRedirector = $null
        }
    }

    # Re-initialize the assembly loader
    Write-Verbose "Re-initializing assembly loader"
    Initialize-DbatoolsAssemblyLoader
}

# Export functions for module use
Export-ModuleMember -Function Initialize-DbatoolsAssemblyLoader,
                             Test-DbatoolsAssemblyLoading,
                             Get-DbatoolsLoadedAssembly,
                             Reset-DbatoolsAssemblyLoader,
                             Get-PlatformDetails