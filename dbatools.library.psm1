function Get-DbatoolsLibraryPath {
    [CmdletBinding()]
    param()
    $PSScriptRoot
}

$script:libraryroot = Get-DbatoolsLibraryPath

$components = @(
    'assembly-resolution.ps1',
    'assembly-lists.ps1',
    'assembly-loader.ps1'
)

foreach ($component in $components) {
    $componentPath = Join-Path $PSScriptRoot "private\$component"
    . $componentPath
}

if ($PSVersionTable.PSEdition -ne "Core") {
    [System.AppDomain]::CurrentDomain.remove_AssemblyResolve($onAssemblyResolveEventHandler)
}

Export-ModuleMember -Function Get-DbatoolsLibraryPath