# dbatools.library Preview Release Guide

This guide shows how to create and consume preview releases of dbatools.library using the existing GitHub Actions workflow.

## 1. Creating a Preview Release

### Method 1: Manual Workflow Trigger (Recommended)
```bash
# Using GitHub CLI
gh workflow run build-release.yml --ref main

# Using GitHub UI
# 1. Go to: https://github.com/dataplat/dbatools.library/actions/workflows/build-release.yml
# 2. Click "Run workflow"
# 3. Select branch (usually 'main' or your feature branch)
# 4. Click "Run workflow"
```

### Method 2: Version-Based Automatic Trigger
```powershell
# Edit dbatools.library.psd1 to include "preview" in version
# Example: Change ModuleVersion = '2025.7.12' to ModuleVersion = '2025.7.12-preview'
# Then commit and push - workflow will trigger automatically

git add dbatools.library.psd1
git commit -m "Trigger preview release"
git push origin main
```

### Method 3: API Trigger
```bash
# Using curl with GitHub Personal Access Token
curl -X POST \
  -H "Accept: application/vnd.github.v3+json" \
  -H "Authorization: token YOUR_GITHUB_TOKEN" \
  https://api.github.com/repos/dataplat/dbatools.library/actions/workflows/build-release.yml/dispatches \
  -d '{"ref": "main"}'
```

## 2. Preview Release Naming Convention

The workflow automatically generates preview versions in this format:
```
YYYY.M.D-preview-BRANCH-YYYYMMDD.HHMMSS
```

Examples:
- `2025.7.12-preview-main-20250712.143022`
- `2025.7.12-preview-feature-new-connection-20250712.143022`

## 3. Consuming Preview Releases

### Method 1: Direct Download from GitHub Releases
```powershell
# Find the latest preview release at:
# https://github.com/dataplat/dbatools.library/releases

# Download and install manually
$previewVersion = "2025.7.12-preview-main-20250712.143022"  # Use actual version
$downloadUrl = "https://github.com/dataplat/dbatools.library/releases/download/v$previewVersion/dbatools.library.zip"

# Create temp directory and download
$tempDir = New-TemporaryFile | %{ Remove-Item $_; New-Item -ItemType Directory -Path $_ }
Invoke-WebRequest $downloadUrl -OutFile "$tempDir/dbatools.library.zip"
Expand-Archive "$tempDir/dbatools.library.zip" "$tempDir/extracted" -Force

# Install to PowerShell modules directory
$modulePath = "$env:USERPROFILE\Documents\PowerShell\Modules\dbatools.library\$previewVersion"
New-Item -Path $modulePath -ItemType Directory -Force
Copy-Item "$tempDir/extracted/dbatools.library/*" $modulePath -Recurse -Force

# Import the preview module
Import-Module $modulePath -Force
```

### Method 2: PowerShell Gallery (if published)
```powershell
# Check if preview is available on PowerShell Gallery
Find-Module dbatools.library -AllowPrerelease -AllVersions | Select-Object Name, Version

# Install specific preview version
Install-Module dbatools.library -RequiredVersion "2025.7.12-preview.1" -AllowPrerelease -Force

# Import and verify
Import-Module dbatools.library -Force
Get-Module dbatools.library
```

## 4. Testing Preview Releases with dbatools

### Update dbatools Development Environment
```powershell
# Option 1: Modify dbatools.psd1 RequiredModules (temporary)
RequiredModules = @(
    @{
        ModuleName = 'dbatools.library'
        RequiredVersion = '2025.7.12-preview-main-20250712.143022'
    }
)

# Option 2: Force load preview before importing dbatools
Remove-Module dbatools.library -Force -ErrorAction SilentlyContinue
Import-Module dbatools.library -RequiredVersion "2025.7.12-preview-main-20250712.143022" -Force
Import-Module ./dbatools.psd1 -Force
```

### Test Script for Preview Validation
```powershell
# test-preview.ps1
param(
    [Parameter(Mandatory)]
    [string]$PreviewVersion,
    [string]$DbatoolsPath = "C:\Source\dbatools"
)

Write-Host "Testing dbatools.library preview: $PreviewVersion"

# Download and install preview
$downloadUrl = "https://github.com/dataplat/dbatools.library/releases/download/v$PreviewVersion/dbatools.library.zip"
$tempDir = New-TemporaryFile | %{ Remove-Item $_; New-Item -ItemType Directory -Path $_ }
Invoke-WebRequest $downloadUrl -OutFile "$tempDir/dbatools.library.zip"
Expand-Archive "$tempDir/dbatools.library.zip" "$tempDir/extracted" -Force

# Install preview module
$modulePath = "$env:USERPROFILE\Documents\PowerShell\Modules\dbatools.library\$PreviewVersion"
New-Item -Path $modulePath -ItemType Directory -Force
Copy-Item "$tempDir/extracted/dbatools.library/*" $modulePath -Recurse -Force

# Test import
try {
    Import-Module $modulePath -Force
    Write-Host "✅ Preview library imported successfully" -ForegroundColor Green

    $module = Get-Module dbatools.library
    Write-Host "Version: $($module.Version)" -ForegroundColor Green
    Write-Host "Path: $($module.ModuleBase)" -ForegroundColor Green
} catch {
    Write-Host "❌ Failed to import preview library: $_" -ForegroundColor Red
    exit 1
}

# Test with dbatools if path provided
if (Test-Path $DbatoolsPath) {
    Push-Location $DbatoolsPath
    try {
        Import-Module ./dbatools.psd1 -Force
        Write-Host "✅ dbatools imported with preview library" -ForegroundColor Green

        # Basic functionality test
        $result = Test-DbaConnection -SqlInstance "localhost" -ErrorAction SilentlyContinue
        Write-Host "✅ Basic connection test completed" -ForegroundColor Green
    } catch {
        Write-Host "❌ dbatools import failed: $_" -ForegroundColor Red
    } finally {
        Pop-Location
    }
}

# Cleanup
Remove-Item $tempDir -Recurse -Force
Write-Host "✅ Test completed"
```

## 5. GitHub Actions Integration for dbatools

Add to dbatools repository `.github/workflows/test-preview.yml`:

```yaml
name: Test with dbatools.library Preview
on:
  workflow_dispatch:
    inputs:
      preview_version:
        description: 'Preview version to test (e.g., 2025.7.12-preview-main-20250712.143022)'
        required: true
        type: string

jobs:
  test-preview:
    runs-on: windows-latest
    steps:
    - uses: actions/checkout@v4

    - name: Download and install preview library
      shell: pwsh
      run: |
        $previewVersion = "${{ github.event.inputs.preview_version }}"
        Write-Host "Installing preview version: $previewVersion"

        # Download from GitHub releases
        $downloadUrl = "https://github.com/dataplat/dbatools.library/releases/download/v$previewVersion/dbatools.library.zip"
        $tempDir = New-TemporaryFile | %{ Remove-Item $_; New-Item -ItemType Directory -Path $_ }
        Invoke-WebRequest $downloadUrl -OutFile "$tempDir/dbatools.library.zip"
        Expand-Archive "$tempDir/dbatools.library.zip" "$tempDir/extracted" -Force

        # Install to modules directory
        $modulePath = "$env:USERPROFILE\Documents\PowerShell\Modules\dbatools.library\$previewVersion"
        New-Item -Path $modulePath -ItemType Directory -Force
        Copy-Item "$tempDir/extracted/dbatools.library/*" $modulePath -Recurse -Force

        # Import and verify
        Import-Module $modulePath -Force
        Get-Module dbatools.library

    - name: Test dbatools with preview library
      shell: pwsh
      run: |
        Import-Module ./dbatools.psd1 -Force

        # Run basic tests
        $tests = @(
            "Get-DbaDatabase.Tests.ps1",
            "Connect-DbaInstance.Tests.ps1"
        )

        foreach ($test in $tests) {
            if (Test-Path "./tests/$test") {
                Write-Host "Running $test"
                Invoke-Pester "./tests/$test" -PassThru
            }
        }
```

## 6. Common Commands and Workflow

### Check Available Preview Releases
```powershell
# List all releases (including previews)
$releases = Invoke-RestMethod "https://api.github.com/repos/dataplat/dbatools.library/releases"
$previews = $releases | Where-Object { $_.prerelease -eq $true }
$previews | Select-Object tag_name, published_at, name
```

### Monitor Workflow Progress
```bash
# Using GitHub CLI
gh run list --workflow=build-release.yml --limit=5
gh run watch  # Watch the latest run
```

### Automated Preview Testing
```powershell
# monitor-and-test.ps1 - Automatically test new previews
$lastChecked = Get-Date
while ($true) {
    $releases = Invoke-RestMethod "https://api.github.com/repos/dataplat/dbatools.library/releases"
    $newPreviews = $releases | Where-Object {
        $_.prerelease -eq $true -and
        [datetime]$_.published_at -gt $lastChecked
    }

    foreach ($preview in $newPreviews) {
        Write-Host "New preview available: $($preview.tag_name)"

        # Run your test script
        ./test-preview.ps1 -PreviewVersion $preview.tag_name.TrimStart('v')
    }

    $lastChecked = Get-Date
    Start-Sleep 300  # Check every 5 minutes
}
```

## 7. Version Management

### Current Version Strategy
- Base version follows date format: `YYYY.M.D`
- Preview versions append: `-preview-BRANCH-TIMESTAMP`
- Manual triggers always create previews regardless of base version

### Force New Preview from Same Commit
```bash
# Trigger workflow multiple times to get different timestamps
gh workflow run build-release.yml --ref main
# Wait a minute, then run again for different timestamp
gh workflow run build-release.yml --ref main
```

### Feature Branch Preview Testing
```bash
# Create feature branch
git checkout -b feature/new-assembly-loading
# Make changes and commit
git commit -am "Improve assembly loading logic"
git push origin feature/new-assembly-loading

# Trigger preview from feature branch
gh workflow run build-release.yml --ref feature/new-assembly-loading
```

This guide provides the complete workflow for creating and consuming dbatools.library preview releases using the existing GitHub Actions infrastructure.