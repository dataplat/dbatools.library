# Assembly version and dependency mappings for dbatools.library
# This file defines the required assemblies, their versions, and platform-specific mappings

# Core assemblies required across all platforms
$script:CoreAssemblies = @(
    # System dependencies
    'System.Memory',
    'System.Runtime.CompilerServices.Unsafe',

    # Azure dependencies
    'Azure.Core',
    'Azure.Identity',
    'Microsoft.Identity.Client',
    #'Microsoft.IdentityModel.Abstractions',

    # Third-party dependencies
    'Bogus',
    'LumenWorks.Framework.IO',

    # SQL dependencies
    'Microsoft.Data.SqlClient',
    'Microsoft.SqlServer.Smo',
    'Microsoft.SqlServer.Management.Sdk.Sfc',
    'Microsoft.SqlServer.ConnectionInfo',
    'Microsoft.SqlServer.SqlWmiManagement',
    'Microsoft.SqlServer.WmiEnum',
    'Microsoft.SqlServer.Management.RegisteredServers',
    'Microsoft.SqlServer.Management.Collector',
    'Microsoft.SqlServer.Management.XEvent',
    'Microsoft.SqlServer.Management.XEventDbScoped',
    'Microsoft.SqlServer.XEvent.XELite',
    'Microsoft.SqlServer.SmoExtended'
)

# Add Integration Services for non-Core PowerShell
if ($PSVersionTable.PSEdition -ne "Core") {
    $script:CoreAssemblies += 'Microsoft.SqlServer.Management.IntegrationServices'
}

# x64-only assemblies that should only be loaded on 64-bit systems
$script:X64Assemblies = @(
    'Microsoft.SqlServer.Replication',
    'Microsoft.SqlServer.Rmo'
)

# Add x64-only assemblies if on 64-bit system and Windows
if ($env:PROCESSOR_ARCHITECTURE -ne "x86") {
    # Determine platform for replication assembly filtering
    $currentPlatform = if ($PSVersionTable.PSVersion.Major -ge 6) {
        if ($IsWindows) { 'Windows' }
        elseif ($IsLinux) { 'Linux' }
        else { 'OSX' }
    } else {
        'Windows' # PowerShell 5.1 and below only runs on Windows
    }

    if ($currentPlatform -eq 'Windows') {
        $script:CoreAssemblies += $script:X64Assemblies
    } else {
        # On non-Windows platforms, only add Rmo (skip Replication)
        $script:CoreAssemblies += 'Microsoft.SqlServer.Rmo'
    }
}

# Add Analysis Services assemblies if running under DSC
if ($Env:SMODefaultModuleName) {
    $script:CoreAssemblies += @(
        'Microsoft.AnalysisServices.Core',
        'Microsoft.AnalysisServices',
        'Microsoft.AnalysisServices.Tabular',
        'Microsoft.AnalysisServices.Tabular.Json'
    )
}

# Remove XEvent assemblies for ARM64 platforms
if ($PSVersionTable.OS -match "ARM64") {
    $script:CoreAssemblies = $script:CoreAssemblies | Where-Object { $PSItem -notmatch "XE" }
}

# DAC-specific assemblies
$script:DacAssemblies = @(
    'Microsoft.SqlServer.Dac',
    'Microsoft.Data.Tools.Schema.Sql'
)

# Assembly load order to handle dependencies
$script:AssemblyLoadOrder = @(
    # System dependencies first
    'System.Memory',
    'System.Runtime.CompilerServices.Unsafe',

    # Azure dependencies next
    #'Microsoft.IdentityModel.Abstractions',
    'Microsoft.Identity.Client',
    'Azure.Core',
    'Azure.Identity',

    # Third-party dependencies
    'Bogus',
    'LumenWorks.Framework.IO',

    # Core SQL Client and basics next
    'Microsoft.Data.SqlClient',
    'Microsoft.SqlServer.Management.Sdk.Sfc',
    'Microsoft.SqlServer.ConnectionInfo',
    'Microsoft.SqlServer.Smo',
    'Microsoft.SqlServer.SqlWmiManagement',
    'Microsoft.SqlServer.WmiEnum',
    'Microsoft.SqlServer.Management.RegisteredServers',
    'Microsoft.SqlServer.Management.Collector',
    'Microsoft.SqlServer.Management.XEvent',
    'Microsoft.SqlServer.Management.XEventDbScoped',
    'Microsoft.SqlServer.XEvent.XELite',
    'Microsoft.SqlServer.SmoExtended'
)

# Add Integration Services for non-Core PowerShell
if ($PSVersionTable.PSEdition -ne "Core") {
    $script:AssemblyLoadOrder += 'Microsoft.SqlServer.Management.IntegrationServices'
}

# Add x64-only assemblies if on 64-bit system and Windows
if ($env:PROCESSOR_ARCHITECTURE -ne "x86") {
    # Determine platform for replication assembly filtering
    $currentPlatform = if ($PSVersionTable.PSVersion.Major -ge 6) {
        if ($IsWindows) { 'Windows' }
        elseif ($IsLinux) { 'Linux' }
        else { 'OSX' }
    } else {
        'Windows' # PowerShell 5.1 and below only runs on Windows
    }

    if ($currentPlatform -eq 'Windows') {
        $script:AssemblyLoadOrder += @(
            'Microsoft.SqlServer.Replication',
            'Microsoft.SqlServer.Rmo'
        )
    }
}

# Add Analysis Services assemblies to load order if running under DSC
if ($Env:SMODefaultModuleName) {
    $script:AssemblyLoadOrder += @(
        'Microsoft.AnalysisServices.Core',
        'Microsoft.AnalysisServices',
        'Microsoft.AnalysisServices.Tabular',
        'Microsoft.AnalysisServices.Tabular.Json'
    )
}

# Add DAC components last
$script:AssemblyLoadOrder += @(
    'Microsoft.Data.Tools.Schema.Sql',
    'Microsoft.SqlServer.Dac'
)

# Remove XEvent assemblies from load order for ARM64 platforms
if ($PSVersionTable.OS -match "ARM64") {
    $script:AssemblyLoadOrder = $script:AssemblyLoadOrder | Where-Object { $PSItem -notmatch "XE" }
}

# Common assemblies that are platform-independent
$script:CommonAssemblies = @(
    'Microsoft.SqlServer.Management.Sdk.Sfc',
    'Microsoft.SqlServer.Smo',
    'Microsoft.SqlServer.ConnectionInfo',
    'Microsoft.SqlServer.SqlWmiManagement',
    'Microsoft.SqlServer.WmiEnum',
    'Microsoft.SqlServer.Management.RegisteredServers',
    'Microsoft.SqlServer.Management.Collector',
    'Microsoft.SqlServer.Management.XEvent',
    'Microsoft.SqlServer.Management.XEventDbScoped',
    'Microsoft.SqlServer.XEvent.XELite',
    'Microsoft.SqlServer.SmoExtended'
)

# Define platform-specific paths for assemblies and native dependencies
$script:PlatformAssemblies = @{
    'Windows' = @{
        'x64' = @{
            'Path' = Join-Path $script:libraryroot "core/lib"
            'NativePath' = Join-Path $script:libraryroot "core/lib/runtimes/win-x64/native"
            'Dependencies' = Join-Path $script:libraryroot "core/lib"
            'ThirdParty' = @{
                'Bogus' = Join-Path $script:libraryroot "core/third-party/bogus"
                'LumenWorks.Framework.IO' = Join-Path $script:libraryroot "core/third-party/LumenWorks"
            }
        }
        'x86' = @{
            'Path' = Join-Path $script:libraryroot "core/lib"
            'NativePath' = Join-Path $script:libraryroot "core/lib/runtimes/win-x86/native"
            'Dependencies' = Join-Path $script:libraryroot "core/lib"
            'ThirdParty' = @{
                'Bogus' = Join-Path $script:libraryroot "core/third-party/bogus"
                'LumenWorks.Framework.IO' = Join-Path $script:libraryroot "core/third-party/LumenWorks"
            }
        }
        'arm64' = @{
            'Path' = Join-Path $script:libraryroot "core/lib"
            'NativePath' = Join-Path $script:libraryroot "core/lib/runtimes/win-arm64/native"
            'Dependencies' = Join-Path $script:libraryroot "core/lib"
            'ThirdParty' = @{
                'Bogus' = Join-Path $script:libraryroot "core/third-party/bogus"
                'LumenWorks.Framework.IO' = Join-Path $script:libraryroot "core/third-party/LumenWorks"
            }
        }
        'arm' = @{
            'Path' = Join-Path $script:libraryroot "core/lib"
            'NativePath' = Join-Path $script:libraryroot "core/lib/runtimes/win-arm/native"
            'Dependencies' = Join-Path $script:libraryroot "core/lib"
            'ThirdParty' = @{
                'Bogus' = Join-Path $script:libraryroot "core/third-party/bogus"
                'LumenWorks.Framework.IO' = Join-Path $script:libraryroot "core/third-party/LumenWorks"
            }
        }
        'DAC' = Join-Path $script:libraryroot "core/lib/dac/windows"
    }
    'Linux' = @{
        'x64' = @{
            'Path' = Join-Path $script:libraryroot "core/lib"
            'NativePath' = Join-Path $script:libraryroot "core/lib/runtimes/linux-x64/native"
            'Dependencies' = Join-Path $script:libraryroot "core/lib"
            'ThirdParty' = @{
                'Bogus' = Join-Path $script:libraryroot "core/third-party/bogus"
                'LumenWorks.Framework.IO' = Join-Path $script:libraryroot "core/third-party/LumenWorks"
            }
        }
        'arm' = @{
            'Path' = Join-Path $script:libraryroot "core/lib"
            'NativePath' = Join-Path $script:libraryroot "core/lib/runtimes/linux-arm/native"
            'Dependencies' = Join-Path $script:libraryroot "core/lib"
            'ThirdParty' = @{
                'Bogus' = Join-Path $script:libraryroot "core/third-party/bogus"
                'LumenWorks.Framework.IO' = Join-Path $script:libraryroot "core/third-party/LumenWorks"
            }
        }
        'arm64' = @{
            'Path' = Join-Path $script:libraryroot "core/lib"
            'NativePath' = Join-Path $script:libraryroot "core/lib/runtimes/linux-arm64/native"
            'Dependencies' = Join-Path $script:libraryroot "core/lib"
            'ThirdParty' = @{
                'Bogus' = Join-Path $script:libraryroot "core/third-party/bogus"
                'LumenWorks.Framework.IO' = Join-Path $script:libraryroot "core/third-party/LumenWorks"
            }
        }
        'musl-x64' = @{
            'Path' = Join-Path $script:libraryroot "core/lib"
            'NativePath' = Join-Path $script:libraryroot "core/lib/runtimes/linux-musl-x64/native"
            'Dependencies' = Join-Path $script:libraryroot "core/lib"
            'ThirdParty' = @{
                'Bogus' = Join-Path $script:libraryroot "core/third-party/bogus"
                'LumenWorks.Framework.IO' = Join-Path $script:libraryroot "core/third-party/LumenWorks"
            }
        }
        'DAC' = Join-Path $script:libraryroot "core/lib/dac/linux"
    }
    'OSX' = @{
        'x64' = @{
            'Path' = Join-Path $script:libraryroot "core/lib"
            'NativePath' = Join-Path $script:libraryroot "core/lib/runtimes/osx/native"
            'Dependencies' = Join-Path $script:libraryroot "core/lib"
            'ThirdParty' = @{
                'Bogus' = Join-Path $script:libraryroot "core/third-party/bogus"
                'LumenWorks.Framework.IO' = Join-Path $script:libraryroot "core/third-party/LumenWorks"
            }
        }
        'arm64' = @{
            'Path' = Join-Path $script:libraryroot "core/lib"
            'NativePath' = Join-Path $script:libraryroot "core/lib/runtimes/osx-arm64/native"
            'Dependencies' = Join-Path $script:libraryroot "core/lib"
            'ThirdParty' = @{
                'Bogus' = Join-Path $script:libraryroot "core/third-party/bogus"
                'LumenWorks.Framework.IO' = Join-Path $script:libraryroot "core/third-party/LumenWorks"
            }
        }
        'DAC' = Join-Path $script:libraryroot "core/lib/dac/mac"
    }
}

if ($PSVersionTable.PSEdition -ne 'Core') {
    # Change the path for Windows PowerShell (5.1) to use the desktop version
    # But preserve the DAC path
    $dacPath = $script:PlatformAssemblies['Windows']['DAC']
    $script:PlatformAssemblies['Windows'] = @{
        'x64' = @{
            'Path' = Join-Path $script:libraryroot "desktop/lib"
            'NativePath' = Join-Path $script:libraryroot "desktop/lib"
            'Dependencies' = Join-Path $script:libraryroot "desktop/lib"
            'ThirdParty' = @{
                'Bogus' = Join-Path $script:libraryroot "desktop/third-party/bogus"
                'LumenWorks.Framework.IO' = Join-Path $script:libraryroot "desktop/third-party/LumenWorks"
            }
        }
        'x86' = @{
            'Path' = Join-Path $script:libraryroot "desktop/lib"
            'NativePath' = Join-Path $script:libraryroot "desktop/lib"
            'Dependencies' = Join-Path $script:libraryroot "desktop/lib"
            'ThirdParty' = @{
                'Bogus' = Join-Path $script:libraryroot "desktop/third-party/bogus"
                'LumenWorks.Framework.IO' = Join-Path $script:libraryroot "desktop/third-party/LumenWorks"
            }
        }
        'DAC' = $dacPath
    }
}