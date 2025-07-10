$PSDefaultParameterValues["*:Confirm"] = $false
$PSDefaultParameterValues["*:Force"] = $true

# Check if modules are already installed
if (-not (Get-Module -ListAvailable -Name dbatools)) {
    Write-Output "Installing PowerShell dependencies..."
    Set-PSRepository PSGallery -InstallationPolicy Trusted
    Install-Module Pester, aitoolkit, psopenai
}

# Fix PSOpenAI ApiBase bug
$psopenaiModule = Get-Module -ListAvailable -Name PSOpenAI | Select-Object -First 1
if ($psopenaiModule) {
    $parameterPath = Join-Path $psopenaiModule.ModuleBase "Private/Get-OpenAIAPIParameter.ps1"
    $content = Get-Content $parameterPath
    $content = $content -replace '\$OpenAIParameter\.ApiBase = \$null', '#$OpenAIParameter.ApiBase = $null'
    Set-Content $parameterPath $content
}

# Reload profile with some settings we need
. $profile