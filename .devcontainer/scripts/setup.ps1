$PSDefaultParameterValues["*:Confirm"] = $false
$PSDefaultParameterValues["*:Force"] = $true

if (-not (Get-Module -ListAvailable -Name dbatools)) {
    Write-Output "Installing PowerShell dependencies..."
    Set-PSRepository PSGallery -InstallationPolicy Trusted
    Install-Module Pester
}

claude config set autoUpdates false --global

. $profile
