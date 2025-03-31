# Assembly loading troubleshooting functions for dbatools.library

function Test-DbatoolsAssemblyEnvironment {
    [CmdletBinding()]
    param()

    $platformInfo = Get-DbatoolsPlatformInfo
    Write-Host "Environment Information:"
    Write-Host "======================="
    Write-Host "Platform: $($platformInfo.Platform)"
    Write-Host "Architecture: $($platformInfo.Architecture)"
    Write-Host "Runtime: $($platformInfo.Runtime)"
    Write-Host ""

    Write-Host "Assembly Search Paths:"
    Write-Host "===================="
    if ($platformInfo.Platform -eq 'Windows') {
        $config = $script:PlatformAssemblies['Windows'][$platformInfo.Architecture]
        if ($config) {
            Write-Host "Main Path: $($config.Path)"
            Write-Host "Native Path: $($config.NativePath)"

            # Check native dependencies
            $sniPath = Join-Path $config.NativePath "Microsoft.Data.SqlClient.SNI.$($platformInfo.Architecture).dll"
            Write-Host "SNI Library Present: $(Test-Path $sniPath)"
        }
    }

    $corePath = Join-Path $script:libraryroot "lib/core"
    $desktopPath = Join-Path $script:libraryroot "lib/desktop"
    Write-Host "Core Path: $corePath"
    Write-Host "Desktop Path: $desktopPath"
    Write-Host ""

    Write-Host "Required Assemblies Status:"
    Write-Host "========================="
    $allAssemblies = @($script:CoreAssemblies.Keys) + @($script:DacAssemblies.Keys) | Select-Object -Unique
    foreach ($assembly in $allAssemblies) {
        try {
            $path = Get-DbatoolsAssemblyPath `
                -AssemblyName $assembly `
                -Platform $platformInfo.Platform `
                -Architecture $platformInfo.Architecture `
                -Runtime $platformInfo.Runtime

            $exists = Test-Path $path
            $loaded = $null
            if ($exists) {
                $loaded = [System.AppDomain]::CurrentDomain.GetAssemblies() |
                    Where-Object { $_.GetName().Name -eq $assembly }
            }

            Write-Host "$assembly"
            Write-Host "  Path: $path"
            Write-Host "  Exists: $exists"
            Write-Host "  Loaded: $($null -ne $loaded)"
            if ($loaded) {
                Write-Host "  Version: $($loaded.GetName().Version)"
            }
            Write-Host ""
        }
        catch {
            Write-Host "$assembly"
            Write-Host "  Error: $_"
            Write-Host ""
        }
    }
}

function Reset-DbatoolsAssemblyCache {
    [CmdletBinding()]
    param()

    try {
        # Remove assembly resolve handler
        if ($PSVersionTable.PSEdition -ne 'Core') {
            [System.AppDomain]::CurrentDomain.remove_AssemblyResolve($script:onAssemblyResolveEventHandler)
        }

        # Clear loaded assemblies from current session if possible
        $loadedAssemblies = [System.AppDomain]::CurrentDomain.GetAssemblies()
        $ourAssemblies = $loadedAssemblies | Where-Object {
            $name = $_.GetName().Name
            $script:CoreAssemblies.ContainsKey($name) -or $script:DacAssemblies.ContainsKey($name)
        }

        Write-Verbose "Attempting to clear $($ourAssemblies.Count) assemblies from current session"

        # Reinitialize assembly loader
        Initialize-DbatoolsAssemblyLoader

        # Re-add assembly resolve handler
        if ($PSVersionTable.PSEdition -ne 'Core') {
            [System.AppDomain]::CurrentDomain.add_AssemblyResolve($script:onAssemblyResolveEventHandler)
        }

        Write-Host "Assembly cache reset complete"
    }
    catch {
        Write-Warning "Error resetting assembly cache: $_"
    }
}

Export-ModuleMember -Function Test-DbatoolsAssemblyEnvironment, Reset-DbatoolsAssemblyCache