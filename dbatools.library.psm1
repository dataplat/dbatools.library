function Get-DbatoolsLibraryPath {
    [CmdletBinding()]
    param()
    Write-Verbose "PSScriptRoot: $PSScriptRoot"
    Write-Verbose "Module Base: $($MyInvocation.MyCommand.Module.ModuleBase)"

    # Use ModuleBase as it's more reliable when importing via absolute path
    $MyInvocation.MyCommand.Module.ModuleBase
}

$script:libraryroot = Get-DbatoolsLibraryPath

# Ensure private directory exists
$privateDir = Join-Path $PSScriptRoot "private"
if (-not (Test-Path $privateDir)) {
    $null = New-Item -ItemType Directory -Path $privateDir -Force
}

# Define component load order (important for dependencies)
$components = @(
    'assembly-lists.ps1',          # Must be first as others depend on its variables
    'assembly-redirector.ps1',     # Assembly redirection for version conflicts
    'assembly-resolution.ps1',      # Depends on assembly lists
    'assembly-loader.ps1',         # Depends on both above
    'assembly-troubleshoot.ps1'    # Troubleshooting tools
)

# Load component scripts
foreach ($component in $components) {
    $componentPath = Join-Path $PSScriptRoot "private\$component"
    if (Test-Path $componentPath) {
        . $componentPath
    } else {
        throw "Required component not found: $componentPath"
    }
}

# Initialize assembly handling
try {
    Initialize-DbatoolsAssemblyLoader
    Write-Verbose "Assembly loader initialized successfully"
} catch {
    throw "Failed to initialize assembly loader: $_"
}

# Register cleanup for module removal
$MyInvocation.MyCommand.ScriptBlock.Module.OnRemove = {
    if ($PSVersionTable.PSEdition -ne "Core") {
        try {
            # Remove both event handlers
            [System.AppDomain]::CurrentDomain.remove_AssemblyResolve($script:onAssemblyResolveEventHandler)

            # Get the redirector instance and remove its handler
            $redirector = New-Object Redirector
            [System.AppDomain]::CurrentDomain.remove_AssemblyResolve($redirector.EventHandler)

            Write-Verbose "Successfully removed assembly resolve handlers"
        }
        catch {
            Write-Warning "Error removing assembly resolve handlers: $_"
        }
    }
}

# Export module functions
Export-ModuleMember -Function @(
    'Get-DbatoolsLibraryPath',
    'Get-DbatoolsPlatformInfo',
    'Get-DbatoolsLoadedAssembly',
    'Test-DbatoolsAssemblyLoading',
    'Reset-DbatoolsAssemblyLoader',
    'Test-DbatoolsAssemblyEnvironment',
    'Reset-DbatoolsAssemblyCache'
)