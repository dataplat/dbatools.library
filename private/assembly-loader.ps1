# Assembly loading and initialization for dbatools.library

function Initialize-DbatoolsAssemblyLoader {
    [CmdletBinding()]
    param()

    # Get platform information
    $platformInfo = Get-DbatoolsPlatformInfo

    # Pre-load assemblies in specified order
    foreach ($assemblyName in $script:AssemblyLoadOrder) {
        try {
            $assemblyPath = Get-DbatoolsAssemblyPath `
                -AssemblyName $assemblyName `
                -Platform $platformInfo.Platform `
                -Architecture $platformInfo.Architecture `
                -Runtime $platformInfo.Runtime

            if (Test-Path $assemblyPath) {
                Write-Verbose "Pre-loading assembly: $assemblyName"
                [System.Reflection.Assembly]::LoadFrom($assemblyPath) | Out-Null
            } else {
                Write-Warning "Assembly not found: $assemblyPath"
            }
        }
        catch {
            # For SqlClient, we'll try fallback if available
            if ($assemblyName -eq 'Microsoft.Data.SqlClient') {
                Write-Warning "Falling back to core SqlClient due to: $_"
                $fallbackPath = Join-Path $script:libraryroot "lib/core/$assemblyName.dll"
                if (Test-Path $fallbackPath) {
                    [System.Reflection.Assembly]::LoadFrom($fallbackPath) | Out-Null
                } else {
                    throw "Failed to load SqlClient and no fallback available"
                }
            } else {
                throw "Failed to load assembly $assemblyName : $_"
            }
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