param(
    [string]$Version,
    [switch]$Sign,
    [switch]$Publish,
    [string]$NuGetApiKey
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# Get script root and project root
$scriptroot = $PSScriptRoot
if (-not $scriptroot) {
    $scriptroot = Split-Path -Path $MyInvocation.MyCommand.Path
}
$root = Split-Path -Path $scriptroot
$csvProjectPath = Join-Path $root "project\Dataplat.Dbatools.Csv"
$csvCsproj = Join-Path $csvProjectPath "Dataplat.Dbatools.Csv.csproj"
$artifactsDir = Join-Path $root "artifacts"
$csvArtifacts = Join-Path $artifactsDir "csv"

Write-Host "=== Dataplat.Dbatools.Csv Build Script ===" -ForegroundColor Cyan
Write-Host ""

# Update version if specified
if ($Version) {
    Write-Host "Updating version to: $Version" -ForegroundColor Yellow
    $csprojContent = Get-Content $csvCsproj -Raw
    $csprojContent = $csprojContent -replace '<Version>[\d\.]+</Version>', "<Version>$Version</Version>"
    Set-Content -Path $csvCsproj -Value $csprojContent -NoNewline
} else {
    # Read current version
    $csprojContent = Get-Content $csvCsproj -Raw
    if ($csprojContent -match '<Version>([\d\.]+)</Version>') {
        $Version = $matches[1]
        Write-Host "Building version: $Version" -ForegroundColor Yellow
    }
}

# Clean and create artifacts directory
if (Test-Path $csvArtifacts) {
    Remove-Item $csvArtifacts -Recurse -Force
}
$null = New-Item -ItemType Directory -Path $csvArtifacts -Force

# Clean previous builds
Write-Host "Cleaning previous builds..." -ForegroundColor Yellow
Push-Location $csvProjectPath
try {
    dotnet clean -c Release --nologo 2>$null
} catch { }
Pop-Location

# Build and pack
Write-Host "Building and packing NuGet package..." -ForegroundColor Yellow
Push-Location $csvProjectPath
try {
    dotnet pack -c Release --nologo -o $csvArtifacts
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet pack failed with exit code $LASTEXITCODE"
    }
} finally {
    Pop-Location
}

# Find the generated packages
$nupkg = Get-ChildItem -Path $csvArtifacts -Filter "*.nupkg" | Select-Object -First 1
$snupkg = Get-ChildItem -Path $csvArtifacts -Filter "*.snupkg" | Select-Object -First 1

if (-not $nupkg) {
    throw "No .nupkg file found in $csvArtifacts"
}

Write-Host "Package created: $($nupkg.Name)" -ForegroundColor Green
if ($snupkg) {
    Write-Host "Symbols package: $($snupkg.Name)" -ForegroundColor Green
}

# Sign the DLLs if requested and Invoke-DbatoolsTrustedSigning exists
if ($Sign) {
    if (Get-Command Invoke-DbatoolsTrustedSigning -ErrorAction SilentlyContinue) {
        Write-Host ""
        Write-Host "=== Signing with Azure Trusted Signing ===" -ForegroundColor Cyan

        $tempExtract = Join-Path ([System.IO.Path]::GetTempPath()) "csv-sign-$(Get-Random)"
        $null = New-Item -ItemType Directory -Path $tempExtract -Force

        try {
            # Extract the package
            Write-Host "Extracting package for signing..." -ForegroundColor Yellow
            Expand-Archive -Path $nupkg.FullName -DestinationPath $tempExtract -Force

            # Find DLLs to sign
            $dllsToSign = Get-ChildItem -Path $tempExtract -Filter "*.dll" -Recurse |
                Where-Object { $_.Name -like "*Dbatools*" -or $_.Name -like "*Dataplat*" }

            if ($dllsToSign) {
                Write-Host "Signing $($dllsToSign.Count) DLL(s)..." -ForegroundColor Yellow

                foreach ($dll in $dllsToSign) {
                    Write-Host "  Signing: $($dll.Name)" -ForegroundColor Gray
                    $result = $dll.FullName | Invoke-DbatoolsTrustedSigning
                    if ($result.Status -ne 'Valid') {
                        Write-Warning "Signing failed for $($dll.Name): $($result.Status)"
                    } else {
                        Write-Host "    Signed (Thumbprint: $($result.Thumbprint))" -ForegroundColor Green
                    }
                }

                # Repack the signed package
                Write-Host "Repacking signed package..." -ForegroundColor Yellow
                Remove-Item $nupkg.FullName -Force
                Push-Location $tempExtract
                Compress-Archive -Path "*" -DestinationPath $nupkg.FullName -Force
                Pop-Location
                Write-Host "Repacked: $($nupkg.Name)" -ForegroundColor Green
            }
        } finally {
            if (Test-Path $tempExtract) {
                Remove-Item $tempExtract -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    } else {
        Write-Warning "Invoke-DbatoolsTrustedSigning not found - skipping signing"
    }
}

# Publish to NuGet if requested
if ($Publish) {
    if (-not $NuGetApiKey) {
        $NuGetApiKey = $env:NUGET_API_KEY
    }

    if (-not $NuGetApiKey) {
        Write-Warning "No NuGet API key provided. Set -NuGetApiKey or `$env:NUGET_API_KEY"
    } else {
        Write-Host ""
        Write-Host "=== Publishing to NuGet ===" -ForegroundColor Cyan

        dotnet nuget push $nupkg.FullName --api-key $NuGetApiKey --source https://api.nuget.org/v3/index.json --skip-duplicate

        if ($snupkg) {
            dotnet nuget push $snupkg.FullName --api-key $NuGetApiKey --source https://api.nuget.org/v3/index.json --skip-duplicate
        }

        if ($LASTEXITCODE -eq 0) {
            Write-Host "Published to NuGet!" -ForegroundColor Green
        }
    }
}

Write-Host ""
Write-Host "=== Build Complete ===" -ForegroundColor Cyan
Write-Host "Output: $csvArtifacts" -ForegroundColor White
Get-ChildItem $csvArtifacts | ForEach-Object {
    Write-Host "  $($_.Name)" -ForegroundColor Gray
}
