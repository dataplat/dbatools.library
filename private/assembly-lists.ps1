# Assembly version and dependency mappings for dbatools.library
# This file defines the required assemblies, their versions, and platform-specific mappings

# Core assemblies required across all platforms
$script:CoreAssemblies = @{
    'Microsoft.Data.SqlClient' = '5.1.5'
    'Microsoft.SqlServer.Smo' = '170.18.0'
    'Microsoft.SqlServer.Management.Sdk.Sfc' = '170.18.0'
    'Microsoft.SqlServer.ConnectionInfo' = '170.18.0'
    'Microsoft.SqlServer.SqlEnum' = '170.18.0'
    'Microsoft.SqlServer.Management.HadrModel' = '170.18.0'
    'Microsoft.SqlServer.Management.HadrData' = '170.18.0'
    'Microsoft.SqlServer.Management.RegisteredServers' = '170.18.0'
    'Microsoft.SqlServer.Management.XEvent' = '170.18.0'
    'Microsoft.SqlServer.Management.XEventDbScoped' = '170.18.0'
}

# DAC-specific assemblies
$script:DacAssemblies = @{
    'Microsoft.SqlServer.Dac' = '170.18.0'
    'Microsoft.SqlServer.Dac.Extensions' = '170.18.0'
    'Microsoft.Data.Tools.Schema.Sql' = '170.18.0'
    'Microsoft.SqlServer.TransactSql.ScriptDom' = '170.18.0'
}

# Define platform-specific paths for assemblies and native dependencies
$script:PlatformAssemblies = @{
    'Windows' = @{
        'x64' = @{
            'Path' = Join-Path $script:libraryroot "lib/win-sqlclient"
            'NativePath' = Join-Path $script:libraryroot "lib/win-sqlclient/native"
        }
        'x86' = @{
            'Path' = Join-Path $script:libraryroot "lib/win-sqlclient-x86"
            'NativePath' = Join-Path $script:libraryroot "lib/win-sqlclient-x86/native"
        }
        'DAC' = Join-Path $script:libraryroot "lib/win-dac"
    }
    'Linux' = @{
        'DAC' = Join-Path $script:libraryroot "lib/linux-dac"
        'SqlClient' = Join-Path $script:libraryroot "lib/core"
    }
    'OSX' = @{
        'DAC' = Join-Path $script:libraryroot "lib/mac-dac"
        'SqlClient' = Join-Path $script:libraryroot "lib/core"
    }
}

# Assembly load order to handle dependencies
[string[]]$script:AssemblyLoadOrder = @(
    # Core SQL Client and basics first
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