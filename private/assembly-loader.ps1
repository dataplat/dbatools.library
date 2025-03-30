# Handle SQL Client loading based on platform
if ($IsWindows -and $PSVersionTable.PSEdition -eq "Core") {
    if ($env:PROCESSOR_ARCHITECTURE -eq "x86") {
        $sqlclient = [System.IO.Path]::Combine($PSScriptRoot, "..", "lib", "win-sqlclient-x86", "Microsoft.Data.SqlClient.dll")
    } else {
        $sqlclient = [System.IO.Path]::Combine($PSScriptRoot, "..", "lib", "win-sqlclient", "Microsoft.Data.SqlClient.dll")
    }
} else {
    $sqlclient = [System.IO.Path]::Combine($PSScriptRoot, "..", "lib", "win-sqlclient", "Microsoft.Data.SqlClient.dll")
}

try {
    Import-Module $sqlclient
} catch {
    throw "Couldn't import $sqlclient | $PSItem"
}

# Get current assemblies
$assemblies = [System.AppDomain]::CurrentDomain.GetAssemblies()

# Determine which assemblies to load based on PowerShell edition
$names = if ($PSVersionTable.PSEdition -eq "Core") {
    $script:CoreAssemblies
} else {
    $script:DesktopAssemblies
}

# Add analysis assemblies if needed
if ($Env:SMODefaultModuleName) {
    $names += $script:AnalysisAssemblies
}

# Remove XEvent assemblies for ARM64
if ($PSVersionTable.OS -match "ARM64") {
    $names = $names | Where-Object { $PSItem -notmatch "XE" }
}

# Import Bogus assembly
try {
    $boguspath = if ($PSVersionTable.PSEdition -eq "Core") {
        [IO.Path]::Combine($PSScriptRoot, "..", "lib", "third-party", "bogus", "core", "bogus.dll")
    } else {
        [IO.Path]::Combine($PSScriptRoot, "..", "lib", "third-party", "bogus", "desktop", "bogus.dll")
    }
    $null = Import-Module $boguspath
} catch {
    Write-Error "Could not import $boguspath : $($_ | Out-String)"
}

# Import required assemblies
foreach ($name in $names) {
    if ($name.StartsWith("win-sqlclient\") -and ($isLinux -or $IsMacOS)) {
        $name = $name.Replace("win-sqlclient\", "")
        if ($IsMacOS -and $name -in "Azure.Core", "Azure.Identity", "System.Security.SecureString") {
            $name = "mac\$name"
        }
    }

    # Skip x64-only assemblies on x86
    if ($name -in $script:x64OnlyAssemblies -and $env:PROCESSOR_ARCHITECTURE -eq "x86") {
        Write-Verbose -Message "Skipping $name. x86 not supported for this library."
        continue
    }

    $subfolder = if ($PSVersionTable.PSEdition -eq "Core") { "core" } else { "desktop" }
    $assemblyPath = [IO.Path]::Combine($PSScriptRoot, "..", "lib", $subfolder, "$name.dll")
    $assemblyfullname = $assemblies.FullName | Out-String

    if (-not ($assemblyfullname.Contains("$name,".Replace("win-sqlclient\", "")))) {
        try {
            $null = Import-Module $assemblyPath
        } catch {
            Write-Error "Could not import $assemblyPath : $($_ | Out-String)"
        }
    }
}