# Assembly version and dependency mappings for dbatools.library
# This file defines the required assemblies, their versions, and platform-specific mappings

# Core assemblies required across all platforms
$script:CoreAssemblies = @{
    # System dependencies
    'System.Memory' = $null
    'System.Runtime.CompilerServices.Unsafe' = $null
    'System.Resources.Extensions' = $null
    'System.Diagnostics.DiagnosticSource' = $null
    'System.Private.CoreLib' = $null

    # Azure dependencies
    'Azure.Core' = $null
    'Azure.Identity' = $null
    'Microsoft.Identity.Client' = $null
    'Microsoft.Identity.Client.Extensions.Msal' = $null
    'Microsoft.IdentityModel.Abstractions' = $null

    # SQL dependencies
    'Microsoft.Data.SqlClient' = $null
    'Microsoft.SqlServer.Smo' = $null
    'Microsoft.SqlServer.Management.Sdk.Sfc' = $null
    'Microsoft.SqlServer.ConnectionInfo' = $null
    'Microsoft.SqlServer.SqlEnum' = $null
    'Microsoft.SqlServer.Management.HadrModel' = $null
    'Microsoft.SqlServer.Management.HadrData' = $null
    'Microsoft.SqlServer.Management.RegisteredServers' = $null
    'Microsoft.SqlServer.Management.XEvent' = $null
    'Microsoft.SqlServer.Management.XEventDbScoped' = $null
}

# DAC-specific assemblies
$script:DacAssemblies = @{
    'Microsoft.SqlServer.Dac' = $null
    'Microsoft.SqlServer.Dac.Extensions' = $null
    'Microsoft.Data.Tools.Schema.Sql' = $null
    'Microsoft.SqlServer.TransactSql.ScriptDom' = $null
}

# Define platform-specific paths for assemblies and native dependencies
$script:PlatformAssemblies = @{
    'Windows' = @{
        'x64' = @{
            'Path' = Join-Path $script:libraryroot "lib/win-sqlclient"
            'NativePath' = Join-Path $script:libraryroot "lib/win-sqlclient/native"
            'Dependencies' = Join-Path $script:libraryroot "lib/win-sqlclient"  # For Azure dependencies
        }
        'x86' = @{
            'Path' = Join-Path $script:libraryroot "lib/win-sqlclient-x86"
            'NativePath' = Join-Path $script:libraryroot "lib/win-sqlclient-x86/native"
            'Dependencies' = Join-Path $script:libraryroot "lib/win-sqlclient-x86"  # For Azure dependencies
        }
        'DAC' = Join-Path $script:libraryroot "lib/win-dac"
    }
    'Linux' = @{
        'DAC' = Join-Path $script:libraryroot "lib/linux-dac"
        'SqlClient' = Join-Path $script:libraryroot "lib/core"
        'Dependencies' = Join-Path $script:libraryroot "lib/core"  # For Azure dependencies
    }
    'OSX' = @{
        'DAC' = Join-Path $script:libraryroot "lib/mac-dac"
        'SqlClient' = Join-Path $script:libraryroot "lib/core"
        'Dependencies' = Join-Path $script:libraryroot "lib/core"  # For Azure dependencies
    }
}

# Assembly load order to handle dependencies
[string[]]$script:AssemblyLoadOrder = @(
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

    # Additional SMO features
    'Microsoft.SqlServer.Management.RegisteredServers',
    'Microsoft.SqlServer.Management.XEvent',
    'Microsoft.SqlServer.Management.XEventDbScoped',
    'Microsoft.SqlServer.Management.HadrData',
    'Microsoft.SqlServer.Management.HadrModel',

    # DAC components last
    'Microsoft.SqlServer.TransactSql.ScriptDom',
    'Microsoft.Data.Tools.Schema.Sql',
    'Microsoft.SqlServer.Dac',
    'Microsoft.SqlServer.Dac.Extensions'
)

# Common assemblies that are platform-independent
[string[]]$script:CommonAssemblies = @(
    'Microsoft.SqlServer.Management.Sdk.Sfc',
    'Microsoft.SqlServer.Smo',
    'Microsoft.SqlServer.ConnectionInfo',
    'Microsoft.SqlServer.SqlEnum'
)