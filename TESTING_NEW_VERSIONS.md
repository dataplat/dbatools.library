# Testing Unpublished dbatools.library Versions

This guide explains how to test new versions of dbatools.library that haven't been published to the PowerShell Gallery yet. The dbatools project has a sophisticated system for testing preview and development versions of its core library dependency.

## Overview

The dbatools module depends on dbatools.library, which contains SMO and other critical libraries. When developing new features or fixes in dbatools.library, you need to test them with dbatools before publishing to the PowerShell Gallery.

## How the System Works

### 1. Version Configuration
The default dbatools.library version is stored in `.github/dbatools-library-version.json`:
```json
{
  "version": "2024.4.12",
  "allowPrerelease": false,
  "comment": "Update this file to change the dbatools.library version used in GitHub Actions workflows"
}
```

### 2. GitHub Workflows
All GitHub workflows support overriding the dbatools.library version via workflow dispatch inputs:
- `integration-tests.yml` - Main cross-platform testing
- `gallery.yml` - Tests PowerShell Gallery version
- `prerelease.yml` - Tests prerelease versions
- `xplat-import.yml` - Tests module import across all platforms

### 3. Installation Process
The custom GitHub Action (`.github/actions/install-dbatools-library`) attempts to install dbatools.library in this order:
1. **PowerShell Gallery** - Checks if the version exists in the gallery
2. **appveyor-lab templib branch** - Falls back to the templib branch for unpublished versions

## Step-by-Step Guide

### Method 1: Testing Locally

1. **Clone the appveyor-lab repository** (if testing a version stored there):
   ```powershell
   git clone https://github.com/dataplat/appveyor-lab.git /tmp/appveyor-lab
   ```

2. **Install the preview version**:
   ```powershell
   # Using the installation script
   ./.github/scripts/Install-DbatoolsLibrary.ps1 -Version "2024.5.1-preview"
   
   # Or if you have a direct download URL
   ./.github/scripts/Install-DbatoolsLibrary.ps1 -Version "2024.5.1-preview" -DownloadUrl "https://github.com/dataplat/appveyor-lab/raw/templib/modules/dbatools.library/2024.5.1-preview.zip"
   ```

3. **Import and test dbatools**:
   ```powershell
   Import-Module ./dbatools.psd1 -Force
   Get-Module dbatools.library | Select-Object Name, Version
   ```

### Method 2: Testing via GitHub Actions

1. **Go to the Actions tab** in the dbatools GitHub repository

2. **Select a workflow** (e.g., "Run Cross Platform Tests")

3. **Click "Run workflow"** and enter your preview version:
   - Branch: `development` (or your feature branch)
   - Override dbatools.library version: `2024.5.1-preview`

4. **Monitor the workflow** to see if your preview version works correctly

### Method 3: Updating Default Version for All Tests

1. **Update the version file**:
   ```powershell
   # Edit .github/dbatools-library-version.json
   @{
       version = "2024.5.1-preview"
       allowPrerelease = $true
       comment = "Testing new SMO updates"
   } | ConvertTo-Json | Set-Content ./.github/dbatools-library-version.json
   ```

2. **Commit and push** to trigger all workflows with the new version

## Publishing Preview Versions to appveyor-lab

If you need to publish a new preview version for testing:

1. **Build your dbatools.library package** as a zip file

2. **Upload to the templib branch**:
   ```bash
   # Clone appveyor-lab
   git clone https://github.com/dataplat/appveyor-lab.git
   cd appveyor-lab
   git checkout templib
   
   # Add your module zip
   mkdir -p modules/dbatools.library
   cp /path/to/dbatools.library-2024.5.1-preview.zip modules/dbatools.library/
   
   # Commit and push
   git add modules/dbatools.library/2024.5.1-preview.zip
   git commit -m "Add dbatools.library 2024.5.1-preview for testing"
   git push origin templib
   ```

3. **The module will now be available** for GitHub Actions to download

## Troubleshooting

### Version Not Found
If you get "version not found" errors:
- Verify the version exists in the templib branch: https://github.com/dataplat/appveyor-lab/tree/templib/modules/dbatools.library
- Check the exact version string (including -preview suffix)
- Ensure the zip file is properly formatted

### Installation Failures
- Check that the zip file contains the module in the correct structure
- Verify you have write permissions to the module path
- Try installing to a different path using the `-ModulePath` parameter

### Testing Specific Scenarios
To test how dbatools behaves with your preview library:
```powershell
# Enable debug mode for detailed loading information
$dbatools_dotsourcemodule = $true
Import-Module ./dbatools.psd1 -Force

# Run specific tests
Invoke-Pester ./tests/Get-DbaDatabase.Tests.ps1
```

## Best Practices

1. **Always test locally first** before triggering GitHub Actions
2. **Use descriptive version numbers** (e.g., `2024.5.1-preview-smo-fix`)
3. **Document what changes** are in your preview version
4. **Clean up old preview versions** from appveyor-lab after publishing to gallery
5. **Test on multiple platforms** if your changes might have platform-specific impacts

## Notes

- The fallback to appveyor-lab only works if the version is NOT in the PowerShell Gallery
- Preview versions should follow PowerShell versioning standards (e.g., `-preview`, `-beta`)
- The system supports both Windows PowerShell and PowerShell Core
- All paths in GitHub Actions use forward slashes, even on Windows