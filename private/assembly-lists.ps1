# Assembly version and dependency mappings for dbatools.library
# This file defines the required assemblies, their versions, and platform-specific mappings

# Core assemblies required across all platforms
$script:CoreAssemblies = @(
    # System dependencies
    'System.Memory',
    'System.Runtime.CompilerServices.Unsafe',
    'System.Resources.Extensions',
    'System.Diagnostics.DiagnosticSource',
    'System.Private.CoreLib',

    # Azure dependencies
    'Azure.Core',
    'Azure.Identity',
    'Microsoft.Identity.Client',
    'Microsoft.Identity.Client.Extensions.Msal',
    'Microsoft.IdentityModel.Abstractions',

    # SQL dependencies
    'Microsoft.Data.SqlClient',
    'Microsoft.SqlServer.Smo',
    'Microsoft.SqlServer.Management.Sdk.Sfc',
    'Microsoft.SqlServer.ConnectionInfo',
    'Microsoft.SqlServer.SqlEnum',
    'Microsoft.SqlServer.Types',
    'Microsoft.SqlServer.Management.HadrModel',
    'Microsoft.SqlServer.Management.HadrData',
    'Microsoft.SqlServer.Management.RegisteredServers',
    'Microsoft.SqlServer.Management.XEvent',
    'Microsoft.SqlServer.Management.XEventDbScoped',
    #'Microsoft.SqlServer.Replication',
    'Microsoft.SqlServer.Rmo',
    'Microsoft.AnalysisServices',
    'Microsoft.AnalysisServices.Core',
    'Microsoft.AnalysisServices.Tabular',
    'Microsoft.AnalysisServices.Tabular.Json'
)

# DAC-specific assemblies
$script:DacAssemblies = @(
    'Microsoft.SqlServer.Dac',
    'Microsoft.SqlServer.Dac.Extensions',
    'Microsoft.Data.Tools.Schema.Sql'
    #'Microsoft.SqlServer.TransactSql.ScriptDom'
)

# Assembly load order to handle dependencies
$script:AssemblyLoadOrder = @(
    # System dependencies first
    'System.Memory',
    'System.Runtime.CompilerServices.Unsafe',
    'System.Resources.Extensions',
    'System.Diagnostics.DiagnosticSource',
    'System.Private.CoreLib',

    # Azure dependencies next
    'Azure.Core',
    'Azure.Identity',
    'Microsoft.Identity.Client',
    'Microsoft.Identity.Client.Extensions.Msal',
    'Microsoft.IdentityModel.Abstractions',

    # Core SQL Client and basics next
    'Microsoft.Data.SqlClient',
    'Microsoft.SqlServer.Management.Sdk.Sfc',

    # SMO components
    'Microsoft.SqlServer.ConnectionInfo',
    'Microsoft.SqlServer.SqlEnum',
    'Microsoft.SqlServer.Smo',
    'Microsoft.SqlServer.Types',

    # Additional SMO features
    'Microsoft.SqlServer.Management.RegisteredServers',
    'Microsoft.SqlServer.Management.XEvent',
    'Microsoft.SqlServer.Management.XEventDbScoped',
    'Microsoft.SqlServer.Management.HadrData',
    'Microsoft.SqlServer.Management.HadrModel',

    # Replication components
    #'Microsoft.SqlServer.Replication',
    #'Microsoft.SqlServer.Rmo',

    # SSIS components

    # Analysis Services components
    'Microsoft.AnalysisServices.Core',
    'Microsoft.AnalysisServices',
    'Microsoft.AnalysisServices.Tabular',
    'Microsoft.AnalysisServices.Tabular.Json',

    # DAC components last
    #'Microsoft.SqlServer.TransactSql.ScriptDom',
    'Microsoft.Data.Tools.Schema.Sql',
    'Microsoft.SqlServer.Dac',
    'Microsoft.SqlServer.Dac.Extensions'
)

# Common assemblies that are platform-independent
$script:CommonAssemblies = @(
    'Microsoft.SqlServer.Management.Sdk.Sfc',
    'Microsoft.SqlServer.Smo',
    'Microsoft.SqlServer.ConnectionInfo',
    'Microsoft.SqlServer.SqlEnum',
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
