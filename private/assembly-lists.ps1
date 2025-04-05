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

    # SQL dependencies
    'Microsoft.Data.SqlClient',
    'Microsoft.SqlServer.Smo',
    'Microsoft.SqlServer.Management.Sdk.Sfc',
    'Microsoft.SqlServer.ConnectionInfo',
    'Microsoft.SqlServer.Types'
)

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
    'Azure.Core',
    'Azure.Identity',

    # Core SQL Client and basics next
    'Microsoft.Data.SqlClient',
    'Microsoft.SqlServer.Management.Sdk.Sfc',
    'Microsoft.SqlServer.ConnectionInfo',
    'Microsoft.SqlServer.Smo',
    'Microsoft.SqlServer.Types',

    # DAC components last
    'Microsoft.Data.Tools.Schema.Sql',
    'Microsoft.SqlServer.Dac'
)

# Common assemblies that are platform-independent
$script:CommonAssemblies = @(
    'Microsoft.SqlServer.Management.Sdk.Sfc',
    'Microsoft.SqlServer.Smo',
    'Microsoft.SqlServer.ConnectionInfo',
    'Microsoft.SqlServer.Types'
)

# Define platform-specific paths for assemblies and native dependencies
$script:PlatformAssemblies = @{
    'Windows' = @{
        'x64' = @{
            'Path' = Join-Path $script:libraryroot "lib/core/runtimes/win/lib/net6.0"
            'NativePath' = Join-Path $script:libraryroot "lib/core/runtimes/win-x64/native"
            'Dependencies' = Join-Path $script:libraryroot "lib/core/runtimes/win/lib/net6.0"
        }
        'x86' = @{
            'Path' = Join-Path $script:libraryroot "lib/core/runtimes/win/lib/net6.0"
            'NativePath' = Join-Path $script:libraryroot "lib/core/runtimes/win-x86/native"
            'Dependencies' = Join-Path $script:libraryroot "lib/core/runtimes/win/lib/net6.0"
        }
        'arm64' = @{
            'Path' = Join-Path $script:libraryroot "lib/core/runtimes/win/lib/net6.0"
            'NativePath' = Join-Path $script:libraryroot "lib/core/runtimes/win-arm64/native"
            'Dependencies' = Join-Path $script:libraryroot "lib/core/runtimes/win/lib/net6.0"
        }
        'arm' = @{
            'Path' = Join-Path $script:libraryroot "lib/core/runtimes/win/lib/net6.0"
            'NativePath' = Join-Path $script:libraryroot "lib/core/runtimes/win-arm/native"
            'Dependencies' = Join-Path $script:libraryroot "lib/core/runtimes/win/lib/net6.0"
        }
        'DAC' = Join-Path $script:libraryroot "lib/win-dac"
    }
    'Linux' = @{
        'x64' = @{
            'Path' = Join-Path $script:libraryroot "lib/core/runtimes/unix/lib/net6.0"
            'NativePath' = Join-Path $script:libraryroot "lib/core/runtimes/linux-x64/native"
            'Dependencies' = Join-Path $script:libraryroot "lib/core/runtimes/unix/lib/net6.0"
        }
        'arm' = @{
            'Path' = Join-Path $script:libraryroot "lib/core/runtimes/unix/lib/net6.0"
            'NativePath' = Join-Path $script:libraryroot "lib/core/runtimes/linux-arm/native"
            'Dependencies' = Join-Path $script:libraryroot "lib/core/runtimes/unix/lib/net6.0"
        }
        'arm64' = @{
            'Path' = Join-Path $script:libraryroot "lib/core/runtimes/unix/lib/net6.0"
            'NativePath' = Join-Path $script:libraryroot "lib/core/runtimes/linux-arm64/native"
            'Dependencies' = Join-Path $script:libraryroot "lib/core/runtimes/unix/lib/net6.0"
        }
        'musl-x64' = @{
            'Path' = Join-Path $script:libraryroot "lib/core/runtimes/unix/lib/net6.0"
            'NativePath' = Join-Path $script:libraryroot "lib/core/runtimes/linux-musl-x64/native"
            'Dependencies' = Join-Path $script:libraryroot "lib/core/runtimes/unix/lib/net6.0"
        }
        'DAC' = Join-Path $script:libraryroot "lib/linux-dac"
    }
    'OSX' = @{
        'x64' = @{
            'Path' = Join-Path $script:libraryroot "lib/core/runtimes/unix/lib/net6.0"
            'NativePath' = Join-Path $script:libraryroot "lib/core/runtimes/osx/native"
            'Dependencies' = Join-Path $script:libraryroot "lib/core/runtimes/unix/lib/net6.0"
        }
        'DAC' = Join-Path $script:libraryroot "lib/mac-dac"
    }
}
