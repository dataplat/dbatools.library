# Assembly resolver for System.Runtime.CompilerServices.Unsafe.dll
# This handles the version conflict between SMO (requires 4.0.4.1) and SSAS (requires ≥ 6.0.0.0)

# Get the library root path
$libRoot = $script:libraryroot

# Define the resolver function for System.Runtime.CompilerServices.Unsafe
$script:resolveCompilerServicesUnsafe = {
    param($sender, [System.ResolveEventArgs]$e)

    # Only handle System.Runtime.CompilerServices.Unsafe assembly
    if ($e.Name -like 'System.Runtime.CompilerServices.Unsafe,*') {
        Write-Debug "Resolving System.Runtime.CompilerServices.Unsafe: $($e.Name)"

        try {
            # Extract version from assembly name
            $ver = [Version]($e.Name.Split(',')[1].Split('=')[1])
            Write-Debug "Requested version: $ver"

            # Determine which version to load based on major version
            $path = if ($ver.Major -lt 6) {
                # SMO requires version 4.0.4.1
                Join-Path $libRoot "desktop/v4/System.Runtime.CompilerServices.Unsafe.dll"
            } else {
                # SSAS requires version ≥ 6.0.0.0
                Join-Path $libRoot "desktop/lib/v6/System.Runtime.CompilerServices.Unsafe.dll"
            }

            Write-Debug "Loading from path: $path"

            # Check if the file exists
            if (Test-Path $path) {
                return [System.Reflection.Assembly]::LoadFrom($path)
            } else {
                Write-Warning "Could not find System.Runtime.CompilerServices.Unsafe at path: $path"
            }
        }
        catch {
            Write-Warning "Error resolving System.Runtime.CompilerServices.Unsafe: $_"
        }
    }

    # Return null to let other resolvers handle it
    return $null
}

# Register the resolver if not already registered
if (-not $script:compilerServicesResolverRegistered) {
    try {
        [System.AppDomain]::CurrentDomain.add_AssemblyResolve($script:resolveCompilerServicesUnsafe)
        $script:compilerServicesResolverRegistered = $true
        Write-Debug "Registered System.Runtime.CompilerServices.Unsafe resolver"
    }
    catch {
        Write-Warning "Failed to register System.Runtime.CompilerServices.Unsafe resolver: $_"
    }
}

# Function to reset the resolver if needed
function Reset-CompilerServicesUnsafeResolver {
    [CmdletBinding()]
    param()

    if ($script:compilerServicesResolverRegistered) {
        try {
            [System.AppDomain]::CurrentDomain.remove_AssemblyResolve($script:resolveCompilerServicesUnsafe)
            $script:compilerServicesResolverRegistered = $false
            Write-Debug "Removed System.Runtime.CompilerServices.Unsafe resolver"

            # Re-register
            [System.AppDomain]::CurrentDomain.add_AssemblyResolve($script:resolveCompilerServicesUnsafe)
            $script:compilerServicesResolverRegistered = $true
            Write-Debug "Re-registered System.Runtime.CompilerServices.Unsafe resolver"
        }
        catch {
            Write-Warning "Failed to reset System.Runtime.CompilerServices.Unsafe resolver: $_"
        }
    }
}

# Export the reset function
Export-ModuleMember -Function Reset-CompilerServicesUnsafeResolver