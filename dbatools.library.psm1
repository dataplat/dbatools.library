param(
    # Automatically skip loading assemblies that are already loaded (useful when SqlServer module is already imported)
    [Alias('SkipLoadedAssemblies', 'AllowSharedAssemblies')]
    [switch]$AvoidConflicts
)

function Get-DbatoolsLibraryPath {
    [CmdletBinding()]
    param()
    if ($PSVersionTable.PSEdition -eq "Core") {
        Join-Path -Path $PSScriptRoot -ChildPath core
    } else {
        Join-Path -Path $PSScriptRoot -ChildPath desktop
    }
}

$script:libraryroot = Get-DbatoolsLibraryPath

if ($PSVersionTable.PSEdition -ne "Core") {
    $dir = [System.IO.Path]::Combine($script:libraryroot, "lib")
    $dir = ("$dir\").Replace('\', '\\')

    if (-not ("Redirector" -as [type])) {
        $source = @"
            using System;
            using System.Linq;
            using System.Reflection;
            using System.Text.RegularExpressions;

            public class Redirector
            {
                public Redirector()
                {
                    this.EventHandler = new ResolveEventHandler(AssemblyResolve);
                }

                public readonly ResolveEventHandler EventHandler;

                protected Assembly AssemblyResolve(object sender, ResolveEventArgs e)
                {
                    string[] dlls = {
                        "System.Memory",
                        "System.Runtime",
                        "System.Management.Automation",
                        "System.Runtime.CompilerServices.Unsafe",
                        "Microsoft.Bcl.AsyncInterfaces",
                        "System.Text.Json",
                        "System.Resources.Extensions",
                        "Microsoft.SqlServer.ConnectionInfo",
                        "Microsoft.SqlServer.Smo",
                        "Microsoft.Identity.Client",
                        "System.Diagnostics.DiagnosticSource",
                        "Microsoft.IdentityModel.Abstractions",
                        "Microsoft.Data.SqlClient",
                        "Microsoft.SqlServer.Types",
                        "System.Configuration.ConfigurationManager",
                        "Microsoft.SqlServer.Management.Sdk.Sfc",
                        "Microsoft.SqlServer.Management.IntegrationServices",
                        "Microsoft.SqlServer.Replication",
                        "Microsoft.SqlServer.Rmo",
                        "System.Private.CoreLib",
                        "Azure.Core",
                        "Azure.Identity",
                        "Microsoft.Data.Tools.Utilities",
                        "Microsoft.Data.Tools.Schema.Sql",
                        "Microsoft.SqlServer.TransactSql.ScriptDom"
                    };

                    var requestedName = new AssemblyName(e.Name);
                    var assemblyName = requestedName.Name;

                    // First, check if any version of this assembly is already loaded
                    // This handles version mismatches (e.g., SMO requesting SqlClient 5.0.0.0 when 6.0.2 is loaded)
                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        try
                        {
                            if (assembly.GetName().Name == assemblyName)
                            {
                                return assembly;
                            }
                        }
                        catch
                        {
                            // Some assemblies may throw when accessing GetName()
                        }
                    }

                    // Only load from disk if not already loaded and it's in our list
                    foreach (string dll in dlls)
                    {
                        if (assemblyName == dll)
                        {
                            string filelocation = "$dir" + dll + ".dll";
                            return Assembly.LoadFrom(filelocation);
                        }
                    }

                    return null;
                }
            }
"@

        $null = Add-Type -TypeDefinition $source
    }

    try {
        $redirector = New-Object Redirector
        [System.AppDomain]::CurrentDomain.add_AssemblyResolve($redirector.EventHandler)
    } catch {
        # unsure
    }
}

# REMOVED win-sqlclient logic - SqlClient is now directly in lib
$sqlclient = [System.IO.Path]::Combine($script:libraryroot, "lib", "Microsoft.Data.SqlClient.dll")

# Get loaded assemblies once for reuse (used for AvoidConflicts checks and later assembly loading)
$script:loadedAssemblies = [System.AppDomain]::CurrentDomain.GetAssemblies()

# Check if SqlClient is already loaded when AvoidConflicts is set
$skipSqlClient = $false
if ($AvoidConflicts) {
    $existingAssembly = $script:loadedAssemblies | Where-Object { $_.GetName().Name -eq 'Microsoft.Data.SqlClient' }
    if ($existingAssembly) {
        $skipSqlClient = $true
        Write-Verbose "Skipping Microsoft.Data.SqlClient.dll - already loaded"
    }
}

if (-not $skipSqlClient) {
    try {
        Import-Module $sqlclient
    } catch {
        throw "Couldn't import $sqlclient | $PSItem"
    }
}

if ($PSVersionTable.PSEdition -eq "Core") {
    $names = @(
        'Microsoft.SqlServer.Server',
        'Azure.Core',
        'Azure.Identity',
        'Microsoft.IdentityModel.Abstractions',
        'Microsoft.SqlServer.Dac',
        'Microsoft.SqlServer.Dac.Extensions',
        'Microsoft.Data.Tools.Utilities',
        'Microsoft.Data.Tools.Schema.Sql',
        'Microsoft.SqlServer.TransactSql.ScriptDom',
        'Microsoft.SqlServer.Smo',
        'Microsoft.SqlServer.SmoExtended',
        'Microsoft.SqlServer.SqlWmiManagement',
        'Microsoft.SqlServer.WmiEnum',
        'Microsoft.SqlServer.Management.RegisteredServers',
        'Microsoft.SqlServer.Management.Collector',
        'Microsoft.SqlServer.Management.XEvent',
        'Microsoft.SqlServer.Management.XEventDbScoped',
        'Microsoft.SqlServer.XEvent.XELite'
    )
} else {
    $names = @(
        'Azure.Core',
        'Azure.Identity',
        'Microsoft.IdentityModel.Abstractions',
        'Microsoft.SqlServer.Dac',
        'Microsoft.SqlServer.Dac.Extensions',
        'Microsoft.Data.Tools.Utilities',
        'Microsoft.Data.Tools.Schema.Sql',
        'Microsoft.SqlServer.TransactSql.ScriptDom',
        'Microsoft.SqlServer.Smo',
        'Microsoft.SqlServer.SmoExtended',
        'Microsoft.SqlServer.SqlWmiManagement',
        'Microsoft.SqlServer.WmiEnum',
        'Microsoft.SqlServer.Management.RegisteredServers',
        'Microsoft.SqlServer.Management.IntegrationServices',
        'Microsoft.SqlServer.Management.Collector',
        'Microsoft.SqlServer.Management.XEvent',
        'Microsoft.SqlServer.Management.XEventDbScoped',
        'Microsoft.SqlServer.XEvent.XELite'
    )
}

if ($Env:SMODefaultModuleName) {
    # then it's DSC, load other required assemblies
    $names += "Microsoft.AnalysisServices.Core"
    $names += "Microsoft.AnalysisServices"
    $names += "Microsoft.AnalysisServices.Tabular"
    $names += "Microsoft.AnalysisServices.Tabular.Json"
}

# XEvent stuff kills CI/CD
if ($PSVersionTable.OS -match "ARM64") {
    $names = $names | Where-Object { $PSItem -notmatch "XE" }
}
#endregion Names

# Build string of loaded assembly names once for efficient checking
$script:loadedAssemblyNames = $script:loadedAssemblies.FullName | Out-String

try {
    $null = Import-Module ([IO.Path]::Combine($script:libraryroot, "third-party", "bogus", "Bogus.dll"))
} catch {
    Write-Error "Could not import $assemblyPath : $($_ | Out-String)"
}

foreach ($name in $names) {
    $x64only = 'Microsoft.SqlServer.Replication', 'Microsoft.SqlServer.XEvent.Linq', 'Microsoft.SqlServer.BatchParser', 'Microsoft.SqlServer.Rmo', 'Microsoft.SqlServer.BatchParserClient'

    if ($name -in $x64only -and $env:PROCESSOR_ARCHITECTURE -eq "x86") {
        Write-Verbose -Message "Skipping $name. x86 not supported for this library."
        continue
    }

    # Check if assembly is already loaded (always check to avoid duplicate loads)
    if ($script:loadedAssemblyNames.Contains("$name,")) {
        if ($AvoidConflicts) {
            Write-Verbose "Skipping $name.dll - already loaded"
        }
        continue
    }

    # Load the assembly
    $assemblyPath = [IO.Path]::Combine($script:libraryroot, "lib", "$name.dll")
    try {
        $null = Import-Module $assemblyPath
    } catch {
        Write-Error "Could not import $assemblyPath : $($_ | Out-String)"
    }
}

# Keep the assembly resolver registered for Windows PowerShell
# It's needed at runtime when SMO and other assemblies try to resolve dependencies
# The resolver handles version mismatches (e.g., SMO requesting SqlClient 5.0.0.0 when 6.0.2 is loaded)
if ($PSVersionTable.PSEdition -ne "Core" -and $redirector) {
    # Store the redirector in script scope so it stays alive and can be accessed if needed
    $script:assemblyRedirector = $redirector
}