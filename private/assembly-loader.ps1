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
            Write-Verbose "  $($_.GetName().Name) v$($_.GetName().Version) at $($_.Location)"
        }

    # Get platform information
    $platformDetails = Get-PlatformDetails
    Write-Verbose "Loading assemblies for: $($platformDetails.Platform) $($platformDetails.Architecture)"

    # Get runtime info from Get-DbatoolsPlatformInfo for consistency
    $runtimeInfo = Get-DbatoolsPlatformInfo

    # Load native dependencies using PowerShell's Add-Type
    if ($platformDetails.IsWindows) {
        # Define native methods
        $nativeCode = @'
using System;
using System.Runtime.InteropServices;

public class NativeMethods {
    [DllImport("kernel32.dll")]
    public static extern IntPtr LoadLibrary(string dllToLoad);
}
'@
        Add-Type -TypeDefinition $nativeCode -ErrorAction Stop

        # Get the correct native path
        $nativePath = Join-Path $script:libraryroot "lib/core/runtimes/win-$($platformDetails.Architecture)/native"

        # Load SNI DLL using LoadLibrary
        $sniPath = Join-Path $nativePath "Microsoft.Data.SqlClient.SNI.dll"
        Write-Verbose "Loading native SNI DLL from: $sniPath"
        [NativeMethods]::LoadLibrary($sniPath) | Out-Null

        # Update PATH to include native DLL location first
        $oldPath = [Environment]::GetEnvironmentVariable("PATH", [System.EnvironmentVariableTarget]::Process)
        if (-not $oldPath.Contains($nativePath)) {
            Write-Verbose "Adding native path to PATH: $nativePath"
            [Environment]::SetEnvironmentVariable("PATH", "$nativePath;$oldPath", [System.EnvironmentVariableTarget]::Process)
        }

        # Check system capabilities for native DLL loading
        Write-Verbose "Checking system native DLL loading capabilities..."
        Write-Verbose "  OS Version: $([System.Environment]::OSVersion.Version)"
        Write-Verbose "  OS Platform: $([System.Environment]::OSVersion.Platform)"
        Write-Verbose "  Process Architecture: $([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture)"
        Write-Verbose "  Framework Description: $([System.Runtime.InteropServices.RuntimeInformation]::FrameworkDescription)"
        Write-Verbose "  Runtime Identifier: $([System.Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier)"

        # Get the correct runtime path for the current architecture
        $nativePath = Join-Path $script:libraryroot "lib/core/runtimes/win-$($platformDetails.Architecture)/native"
        $runtimePath = Join-Path $script:libraryroot "lib/core/runtimes/win/lib/net6.0"

        Write-Verbose "Using runtime path: $runtimePath"
        Write-Verbose "Using native path: $nativePath"

        # Try both generic and architecture-specific SNI DLLs
        $genericDll = Join-Path $nativePath "Microsoft.Data.SqlClient.SNI.dll"
        $archSpecificDll = Join-Path $nativePath "Microsoft.Data.SqlClient.SNI.$($platformDetails.Architecture).dll"

        # Check if either DLL exists
        if (-not (Test-Path $genericDll) -and -not (Test-Path $archSpecificDll)) {
            Write-Verbose "Neither generic nor architecture-specific SNI DLL found"
            Write-Verbose "Directory contents of $nativePath :"
            Get-ChildItem $nativePath | ForEach-Object {
                Write-Verbose "  $($_.Name) - Size: $($_.Length) bytes, LastWrite: $($_.LastWriteTime)"
            }
            throw "SNI DLL not found at: $genericDll or $archSpecificDll"
        }

        # If arch-specific exists, copy it to generic name
        if (Test-Path $archSpecificDll) {
            Write-Verbose "Found architecture-specific SNI DLL, copying to generic name"
            # Copy-Item -Path $archSpecificDll -Destination $genericDll -Force
            $sniDll = $genericDll
        } else {
            Write-Verbose "Using generic SNI DLL - this may not work for current architecture"
            $sniDll = $genericDll
        }

        # Get detailed file info for found DLL
        $sniFileInfo = Get-Item $sniDll
        Write-Verbose "Found SNI DLL:"
        Write-Verbose "  Path: $sniDll"
        Write-Verbose "  Size: $($sniFileInfo.Length) bytes"
        Write-Verbose "  Last Modified: $($sniFileInfo.LastWriteTime)"
        Write-Verbose "  Version: $($sniFileInfo.VersionInfo.FileVersion)"

        # Verify native path is in PATH and check its position
        $newPath = [Environment]::GetEnvironmentVariable("PATH", [System.EnvironmentVariableTarget]::Process)
        Write-Verbose "Current PATH: $newPath"

        # Check native path position
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

        # Check for potential DLL conflicts in PATH
        Write-Verbose "Scanning PATH for potential SNI DLL conflicts..."
        $pathDirs = $newPath -split ';'
        foreach ($dir in $pathDirs) {
            if (Test-Path (Join-Path $dir "Microsoft.Data.SqlClient.SNI*.dll")) {
                $conflictDll = Get-Item (Join-Path $dir "Microsoft.Data.SqlClient.SNI*.dll")
                Write-Verbose "Found potential conflicting SNI DLL:"
                Write-Verbose "  Path: $($conflictDll.FullName)"
                Write-Verbose "  Version: $($conflictDll.VersionInfo.FileVersion)"
                Write-Verbose "  Product: $($conflictDll.VersionInfo.ProductName)"
            }
        }

        # Check native DLL dependencies
        Write-Verbose "Checking native DLL dependencies..."
        try {
            $dllPath = Join-Path $nativePath "Microsoft.Data.SqlClient.SNI.dll"
            $assembly = [System.Reflection.Assembly]::LoadFile($dllPath)
            Write-Verbose "Native DLL assembly details:"
            Write-Verbose "  Runtime Version: $($assembly.ImageRuntimeVersion)"
            Write-Verbose "  Architecture: $([System.Reflection.Assembly]::LoadFile($dllPath).GetName().ProcessorArchitecture)"
        }
        catch {
            Write-Verbose "Failed to inspect native DLL: $($_.Exception.Message)"
            Write-Verbose "Full error: $_"
        }

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

        # Note: SNI DLL is native - cannot verify loading with Add-Type
        Write-Verbose "Note: Cannot verify native SNI DLL loading directly - will be loaded by SqlClient"
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
            Write-Verbose "Failed to load dependency $depAssembly : $_"
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
                Write-Verbose "Using already loaded assembly: $assemblyName v$($loadedAssembly.GetName().Version) at $($loadedAssembly.Location)"
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
                    # Enhanced logging for SqlClient pre-load
                    if ($assemblyName -eq 'Microsoft.Data.SqlClient') {
                        Write-Verbose "Preparing to load SqlClient..."
                        Write-Verbose "Assembly path: $assemblyPath"
                        Write-Verbose "Checking assembly file details:"
                        $fileInfo = Get-Item $assemblyPath
                        Write-Verbose "  Size: $($fileInfo.Length) bytes"
                        Write-Verbose "  Last Modified: $($fileInfo.LastWriteTime)"
                        Write-Verbose "  Version: $($fileInfo.VersionInfo.FileVersion)"

                        Write-Verbose "Checking loaded assemblies for potential conflicts:"
                        [System.AppDomain]::CurrentDomain.GetAssemblies() |
                            Where-Object { $_.GetName().Name -like '*SqlClient*' } |
                            ForEach-Object {
                                Write-Verbose "  Already loaded: $($_.GetName().Name)"
                                Write-Verbose "    Version: $($_.GetName().Version)"
                                Write-Verbose "    Location: $($_.Location)"
                            }
                    }

                    $assembly = [System.Reflection.Assembly]::LoadFrom($assemblyPath)

                    # Enhanced logging for SqlClient post-load
                    if ($assemblyName -eq 'Microsoft.Data.SqlClient') {
                        Write-Verbose "Successfully loaded SqlClient:"
                        Write-Verbose "  Version: $($assembly.GetName().Version)"
                        Write-Verbose "  Location: $($assembly.Location)"

                        Write-Verbose "Checking loaded assembly details:"
                        Write-Verbose "  Full Name: $($assembly.FullName)"
                        Write-Verbose "  Runtime Version: $($assembly.ImageRuntimeVersion)"
                        Write-Verbose "  Global Assembly Cache: $($assembly.GlobalAssemblyCache)"

                        Write-Verbose "Checking assembly dependencies:"
                        $assembly.GetReferencedAssemblies() | ForEach-Object {
                            Write-Verbose "  Referenced: $($_.Name) v$($_.Version)"
                        }
                    } else {
                        Write-Verbose "Successfully loaded: $assemblyName from $($assembly.Location)"
                    }

                    # Extra logging for SqlClient
                    if ($assemblyName -eq 'Microsoft.Data.SqlClient') {
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
                Write-Verbose "Assembly not found: $assemblyPath"
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
            Write-Verbose "Assembly file not found: $assemblyPath"
            return $false
        }

        $assembly = [System.Reflection.Assembly]::LoadFrom($assemblyPath)
        Write-Verbose "Successfully loaded $($assembly.FullName)"
        return $true
    }
    catch {
        Write-Verbose "Failed to load $AssemblyName : $_"
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