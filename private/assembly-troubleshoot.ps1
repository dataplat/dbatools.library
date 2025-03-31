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
    $allAssemblies = @($script:CoreAssemblies) + @($script:DacAssemblies) | Select-Object -Unique
    foreach ($assembly in $allAssemblies) {
        try {
            $params = @{
                AssemblyName = $assembly
                Platform = $platformInfo.Platform
                Architecture = $platformInfo.Architecture
                Runtime = $platformInfo.Runtime
            }
            $path = Get-DbatoolsAssemblyPath @params

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
        Write-Verbose "Starting assembly cache reset"

        # Remove assembly resolve handler
        if ($PSVersionTable.PSEdition -ne 'Core') {
            Write-Verbose "Removing assembly resolve handler"
            [System.AppDomain]::CurrentDomain.remove_AssemblyResolve($script:onAssemblyResolveEventHandler)
        }

        # Clear loaded assemblies from current session if possible
        $loadedAssemblies = [System.AppDomain]::CurrentDomain.GetAssemblies()
        $ourAssemblies = $loadedAssemblies | Where-Object {
            $name = $_.GetName().Name
            $script:CoreAssemblies -contains $name -or $script:DacAssemblies -contains $name
        }

        Write-Verbose "Found $($ourAssemblies.Count) assemblies to process:"
        foreach ($asm in $ourAssemblies) {
            Write-Verbose "  $($asm.GetName().Name) v$($asm.GetName().Version) from $($asm.Location)"
        }

        # Force GC collection to help unload assemblies
        [System.GC]::Collect()
        [System.GC]::WaitForPendingFinalizers()

        Write-Verbose "Reinitializing assembly loader"
        Initialize-DbatoolsAssemblyLoader

        # Re-add assembly resolve handler
        if ($PSVersionTable.PSEdition -ne 'Core') {
            Write-Verbose "Re-adding assembly resolve handler"
            [System.AppDomain]::CurrentDomain.add_AssemblyResolve($script:onAssemblyResolveEventHandler)
        }

        Write-Host "Assembly cache reset complete"

        # Verify state after reset
        $remainingAssemblies = [System.AppDomain]::CurrentDomain.GetAssemblies() |
            Where-Object { $script:CoreAssemblies -contains $_.GetName().Name }
        if ($remainingAssemblies) {
            Write-Verbose "Remaining assemblies after reset:"
            foreach ($asm in $remainingAssemblies) {
                Write-Verbose "  $($asm.GetName().Name) v$($asm.GetName().Version) from $($asm.Location)"
            }
        }
    }
    catch {
        Write-Warning "Error resetting assembly cache: $_"
        Write-Warning "Stack trace: $($_.ScriptStackTrace)"
        throw
    }
}

Export-ModuleMember -Function Test-DbatoolsAssemblyEnvironment, Reset-DbatoolsAssemblyCache