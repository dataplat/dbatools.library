# Assembly loading and initialization for dbatools.library

# Function to determine the platform and architecture for assembly loading
function Get-PlatformDetails {
    [CmdletBinding()]
    param()
    $isWin = $IsWindows -or (-not $isLinux -and -not $isMacOS)
    if (-not $env:PROCESSOR_ARCHITECTURE) {
        $env:PROCESSOR_ARCHITECTURE = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLower()
    }
    $arch = switch ($env:PROCESSOR_ARCHITECTURE.ToLower()) {
        "x86"   { "x86" }
        "amd64" { "x64" }
        "arm64" { "arm64" }
        "arm"   { "arm" }
        default {
            if ($PSVersionTable.PSEdition -eq 'Core') {
                [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLower()
            } else {
                "x64"
            }
        }
    }

    $platform = if ($isWin) {
        "Windows"
    } elseif ($isLinux) {
        "Linux"
    } elseif ($isMacOS) {
        "OSX"
    } else {
        throw "Unsupported platform"
    }

    return @{
        Platform = $platform
        Architecture = $arch
        IsWindows = $isWin
        IsLinux = $isLinux
        IsMacOS = $isMacOS
    }
}

function Initialize-DbatoolsAssemblyLoader {
    [CmdletBinding()]
    param()

    try {
        # Get platform information
        $platformDetails = Get-PlatformDetails

        # Reset resolver states
        $script:isResolving = $false
        $script:resolverInitialized = $false

        # Initialize resolvers for non-Core PowerShell
        if ($PSVersionTable.PSEdition -ne 'Core') {
            try {
                # Initialize redirector first
                Initialize-DbatoolsAssemblyRedirector

                # Then initialize resolution handler if redirector succeeded
                if ($script:dbatoolsRedirector) {
                    [System.AppDomain]::CurrentDomain.add_AssemblyResolve($script:onAssemblyResolveEventHandler)
                    $script:resolverInitialized = $true
                }
            }
            catch {
                Write-Warning "Failed to initialize assembly handlers: $($_.Exception.Message)"
                Write-Warning "This may cause issues with assembly loading in Windows PowerShell"
            }
        }

        # Get runtime info from Get-DbatoolsPlatformInfo for consistency
        $runtimeInfo = Get-DbatoolsPlatformInfo

        # Load native dependencies using PowerShell's Add-Type
        if ($platformDetails.IsWindows) {
            # Define native methods
            $nativeCode = @'
using System;
using System.Runtime.InteropServices;

public class NativeMethods {
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadLibrary(string dllToLoad);

    public static bool TryLoadLibrary(string dllPath, out string errorMessage) {
        errorMessage = null;
        IntPtr result = LoadLibrary(dllPath);
        if (result == IntPtr.Zero) {
            int error = Marshal.GetLastWin32Error();
            errorMessage = new System.ComponentModel.Win32Exception(error).Message;
            return false;
        }
        return true;
    }
}
'@
            Add-Type -TypeDefinition $nativeCode -ErrorAction Stop

            $nativePath = Join-Path $script:libraryroot "lib/desktop/runtimes/win-$($platformDetails.Architecture)/native"
            $sniPath = Join-Path $nativePath "Microsoft.Data.SqlClient.SNI.dll"

            if (-not (Test-Path $sniPath)) {
                throw "SNI DLL not found at: $sniPath"
            }

            $errorMessage = $null
            if (-not [NativeMethods]::TryLoadLibrary($sniPath, [ref]$errorMessage)) {
                throw "Failed to load SNI DLL: $errorMessage"
            }

            # Add native path to PATH
            $oldPath = [Environment]::GetEnvironmentVariable("PATH", [System.EnvironmentVariableTarget]::Process)
            if (-not $oldPath.Contains($nativePath)) {
                [Environment]::SetEnvironmentVariable("PATH", "$nativePath;$oldPath", [System.EnvironmentVariableTarget]::Process)
            }

        }

        # Pre-load required dependencies
        $dependencyAssemblies = @(
            #'Microsoft.Identity.Client'
            #'System.Configuration.ConfigurationManager'
        )

        # Load dependencies
        foreach ($depAssembly in $dependencyAssemblies) {
            try {
                $assemblyParams = @{
                    AssemblyName = $depAssembly
                    Platform = $platformDetails.Platform
                    Architecture = $platformDetails.Architecture
                    Runtime = $runtimeInfo.Runtime
                }
                $assemblyPath = Get-DbatoolsAssemblyPath @assemblyParams

                if (-not (Test-Path $assemblyPath)) {
                    Write-Warning "Dependency not found: $assemblyPath"
                    continue
                }

                # Skip if already loaded
                if ([System.AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq $depAssembly }) {
                    continue
                }

                # Load assembly
                [System.Reflection.Assembly]::LoadFrom($assemblyPath) | Out-Null
            }
            catch {
                Write-Warning "Failed to load dependency $depAssembly : $($_.Exception.Message)"
                continue
            }
        }

        # Load core assemblies
        foreach ($assemblyName in $script:AssemblyLoadOrder) {
            try {
                # Skip if already loaded
                if ([System.AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq $assemblyName }) {
                    continue
                }

                $assemblyParams = @{
                    AssemblyName = $assemblyName
                    Platform = $platformDetails.Platform
                    Architecture = $platformDetails.Architecture
                    Runtime = $runtimeInfo.Runtime
                }
                $assemblyPath = Get-DbatoolsAssemblyPath @assemblyParams

                if (-not (Test-Path $assemblyPath)) {
                    Write-Warning "Assembly not found: $assemblyPath"
                    continue
                }

                # Load assembly
                $assembly = [System.Reflection.Assembly]::LoadFrom($assemblyPath)

                # Special handling for SqlClient
                if ($assemblyName -eq 'Microsoft.Data.SqlClient') {
                    $requiredTypes = @(
                        'Microsoft.Data.SqlClient.SqlConnection',
                        'Microsoft.Data.SqlClient.SqlCommand'
                    )
                    foreach ($type in $requiredTypes) {
                        if (-not ($assembly.GetType($type))) {
                            throw "Required type not found in SqlClient: $type"
                        }
                    }
                }
            }
            catch {
                Write-Warning "Error loading $assemblyName : $($_.Exception.Message)"
                throw
            }
        }
    }
    catch {
        Write-Warning "Failed to initialize assembly loader: $($_.Exception.Message)"
        if ($_.Exception.InnerException) {
            Write-Warning "Inner Exception: $($_.Exception.InnerException.Message)"
        }
        throw
    }
}

function Test-DbatoolsAssemblyLoading {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory)]
        [string]$AssemblyName
    )

    try {
        # Check if already loaded
        if ([System.AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq $AssemblyName }) {
            return $true
        }

        # Try loading
        $assemblyPath = Get-DbatoolsAssemblyPath -AssemblyName $AssemblyName -Platform $platformDetails.Platform -Architecture $platformDetails.Architecture -Runtime $runtimeInfo.Runtime
        return (Test-Path $assemblyPath)
    }
    catch {
        return $false
    }
}

function Get-DbatoolsLoadedAssembly {
    [CmdletBinding()]
    param()

    [System.AppDomain]::CurrentDomain.GetAssemblies() |
        Where-Object { $name = $_.GetName().Name; $script:CoreAssemblies -contains $name -or $script:DacAssemblies -contains $name } |
        ForEach-Object {
            [PSCustomObject]@{
                Name = $_.GetName().Name
                Version = $_.GetName().Version
                Location = $_.Location
            }
        }
}

function Reset-DbatoolsAssemblyLoader {
    [CmdletBinding()]
    param()

    # Remove assembly resolve handlers in non-Core PowerShell
    if ($PSVersionTable.PSEdition -ne 'Core') {
        try {
            # Remove standard handler
            if ($script:onAssemblyResolveEventHandler -and $script:resolverInitialized) {
                [System.AppDomain]::CurrentDomain.remove_AssemblyResolve($script:onAssemblyResolveEventHandler)
                $script:resolverInitialized = $false
            }

            # Remove redirector handler if it exists
            if ($script:dbatoolsRedirector) {
                [System.AppDomain]::CurrentDomain.remove_AssemblyResolve($script:dbatoolsRedirector.EventHandler)
                $script:dbatoolsRedirector = $null
            }

            # Reset resolver state
            $script:isResolving = $false
        }
        catch {
            Write-Warning "Error removing assembly handlers: $_"
        }
    }

    # Re-initialize the assembly loader
    Initialize-DbatoolsAssemblyLoader
}

# Export functions for module use
Export-ModuleMember -Function Initialize-DbatoolsAssemblyLoader,
                            Test-DbatoolsAssemblyLoading,
                            Get-DbatoolsLoadedAssembly,
                            Reset-DbatoolsAssemblyLoader,
                            Get-PlatformDetails