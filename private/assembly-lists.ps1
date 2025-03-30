$script:CoreAssemblies = @(
    'Microsoft.SqlServer.Server',
    'Microsoft.SqlServer.Dac',
    'Microsoft.SqlServer.Smo',
    'Microsoft.SqlServer.SmoExtended',
    'Microsoft.SqlServer.Management.RegisteredServers',
    'Microsoft.SqlServer.Management.Collector',
    'Microsoft.SqlServer.Management.XEvent',
    'Microsoft.SqlServer.Management.XEventDbScoped',
    'Microsoft.SqlServer.XEvent.XELite',
    [IO.Path]::Combine("lib", "third-party", "LumenWorks", "core", "LumenWorks.Framework.IO"),
    'Azure.Core',
    'Azure.Identity',
    'Microsoft.IdentityModel.Abstractions'
)

$script:DesktopAssemblies = @(
    'Microsoft.SqlServer.Dac',
    'Microsoft.SqlServer.Smo',
    'Microsoft.SqlServer.SmoExtended',
    'Microsoft.SqlServer.Management.RegisteredServers',
    'Microsoft.SqlServer.Management.IntegrationServices',
    'Microsoft.SqlServer.Management.Collector',
    'Microsoft.SqlServer.Management.XEvent',
    'Microsoft.SqlServer.Management.XEventDbScoped',
    'Microsoft.SqlServer.XEvent.XELite',
    'Azure.Core',
    'Azure.Identity',
    'Microsoft.Data.SqlClient',
    "Microsoft.SqlServer.SqlWmiManagement",
    [IO.Path]::Combine($PSScriptRoot, "..", "lib", "third-party", "LumenWorks", "desktop", "LumenWorks.Framework.IO")
)

$script:AnalysisAssemblies = @(
    "Microsoft.AnalysisServices.Core",
    "Microsoft.AnalysisServices",
    "Microsoft.AnalysisServices.Tabular",
    "Microsoft.AnalysisServices.Tabular.Json"
)

$script:x64OnlyAssemblies = @(
    'Microsoft.SqlServer.Replication',
    'Microsoft.SqlServer.XEvent.Linq',
    'Microsoft.SqlServer.BatchParser',
    'Microsoft.SqlServer.Rmo',
    'Microsoft.SqlServer.BatchParserClient'
)