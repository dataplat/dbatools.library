# Test script to verify that Windows Core loads from core path and Desktop loads from desktop path
param(
    [switch]$Verbose
)

Write-Host "Testing dbatools.library runtime selection..." -ForegroundColor Cyan
Write-Host "PowerShell Edition: $($PSVersionTable.PSEdition)" -ForegroundColor Yellow
Write-Host "PowerShell Version: $($PSVersionTable.PSVersion)" -ForegroundColor Yellow

# Import the module
try {
    Import-Module "$PSScriptRoot\dbatools.library.psd1" -Force -ErrorAction Stop
    Write-Host "Module imported successfully" -ForegroundColor Green
} catch {
    Write-Host "Failed to import module: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# Get platform info
$platformInfo = Get-DbatoolsPlatformInfo
Write-Host "Platform: $($platformInfo.Platform)" -ForegroundColor Yellow
Write-Host "Architecture: $($platformInfo.Architecture)" -ForegroundColor Yellow
Write-Host "Runtime: $($platformInfo.Runtime)" -ForegroundColor Yellow

# Check loaded assemblies
Write-Host "`nChecking loaded assemblies..." -ForegroundColor Cyan
$loadedAssemblies = Get-DbatoolsLoadedAssembly

# Focus on SqlClient assembly
$sqlClientAssembly = $loadedAssemblies | Where-Object { $_.Name -eq 'Microsoft.Data.SqlClient' }
if ($sqlClientAssembly) {
    Write-Host "SqlClient Assembly Found:" -ForegroundColor Green
    Write-Host "  Version: $($sqlClientAssembly.Version)" -ForegroundColor White
    Write-Host "  Location: $($sqlClientAssembly.Location)" -ForegroundColor White

    # Check if the path contains the expected runtime
    $expectedPath = if ($platformInfo.Runtime -eq 'core') { 'core' } else { 'desktop' }
    if ($sqlClientAssembly.Location -like "*$expectedPath*") {
        Write-Host "  ✓ Correct runtime path detected ($expectedPath)" -ForegroundColor Green
    } else {
        Write-Host "  ✗ Incorrect runtime path! Expected '$expectedPath' but found: $($sqlClientAssembly.Location)" -ForegroundColor Red
    }
} else {
    Write-Host "SqlClient assembly not found!" -ForegroundColor Red
}

# Check for SNI DLL in process modules
Write-Host "`nChecking loaded SNI DLL..." -ForegroundColor Cyan
try {
    $sniModules = Get-Process -Id $PID | ForEach-Object {
        $_.Modules | Where-Object { $_.ModuleName -like "*SNI*" }
    }

    if ($sniModules) {
        foreach ($module in $sniModules) {
            Write-Host "SNI Module Found:" -ForegroundColor Green
            Write-Host "  Name: $($module.ModuleName)" -ForegroundColor White
            Write-Host "  Path: $($module.FileName)" -ForegroundColor White

            # Check if the path contains the expected runtime
            $expectedPath = if ($platformInfo.Runtime -eq 'core') { 'core' } else { 'desktop' }
            if ($module.FileName -like "*$expectedPath*") {
                Write-Host "  ✓ Correct runtime path detected ($expectedPath)" -ForegroundColor Green
            } else {
                Write-Host "  ✗ Incorrect runtime path! Expected '$expectedPath' but found: $($module.FileName)" -ForegroundColor Red
            }
        }
    } else {
        Write-Host "No SNI modules found in process" -ForegroundColor Yellow
    }
} catch {
    Write-Host "Error checking process modules: $($_.Exception.Message)" -ForegroundColor Red
}

# Test SqlClient functionality
Write-Host "`nTesting SqlClient functionality..." -ForegroundColor Cyan
try {
    $connectionString = "Server=localhost;Database=master;Integrated Security=true;TrustServerCertificate=true"
    $connection = New-Object Microsoft.Data.SqlClient.SqlConnection($connectionString)
    Write-Host "SqlClient connection object created successfully" -ForegroundColor Green
    $connection.Dispose()
} catch {
    Write-Host "Error creating SqlClient connection: $($_.Exception.Message)" -ForegroundColor Yellow
    Write-Host "This may be expected if no SQL Server is available" -ForegroundColor Yellow
}

Write-Host "`nTest completed!" -ForegroundColor Cyan