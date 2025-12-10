param(
    # Skip loading Microsoft.Data.SqlClient.dll (useful when SqlServer module is already loaded)
    [switch]$SkipSqlClient,

    # Skip loading Azure-related DLLs (Azure.Core, Azure.Identity, Microsoft.Identity.Client)
    [switch]$SkipAzure,

    # Skip loading SMO assemblies (useful when SqlServer module is already loaded)
    [switch]$SkipSmo,

    # Array of specific assembly names to skip (e.g., @('Microsoft.Data.SqlClient', 'Azure.Core'))
    [string[]]$SkipAssemblies = @()
)

# Support module-scoped variables for configuration
# Users can set these before importing the module
if (-not $SkipSqlClient -and $script:SkipSqlClient) {
    $SkipSqlClient = $true
}
if (-not $SkipAzure -and $script:SkipAzure) {
    $SkipAzure = $true
}
if (-not $SkipSmo -and $script:SkipSmo) {
    $SkipSmo = $true
}
if (-not $SkipAssemblies -and $script:SkipAssemblies) {
    $SkipAssemblies = $script:SkipAssemblies
}

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
                        "Azure.Identity"
                    };

                    var name = new AssemblyName(e.Name);
                    var assemblyName = name.Name.ToString();
                    foreach (string dll in dlls)
                    {
                        if (assemblyName == dll)
                        {
                            string filelocation = "$dir" + dll + ".dll";
                            //Console.WriteLine(filelocation);
                            return Assembly.LoadFrom(filelocation);
                        }
                    }

                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        // maybe this needs to change?
                        var info = assembly.GetName();
                        if (info.FullName == e.Name) {
                            return assembly;
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

if (-not $SkipSqlClient -and 'Microsoft.Data.SqlClient' -notin $SkipAssemblies) {
    try {
        Import-Module $sqlclient
    } catch {
        throw "Couldn't import $sqlclient | $PSItem"
    }
} else {
    Write-Verbose "Skipping Microsoft.Data.SqlClient.dll import as requested"
}

if ($PSVersionTable.PSEdition -ne "Core") {
    [System.AppDomain]::CurrentDomain.remove_AssemblyResolve($redirector.EventHandler)
}

if ($PSVersionTable.PSEdition -eq "Core") {
    $names = @(
        'Microsoft.SqlServer.Server',
        'Microsoft.Bcl.AsyncInterfaces',
        'Azure.Core',
        'Azure.Identity',
        'Microsoft.IdentityModel.Abstractions',
        'Microsoft.SqlServer.Dac',
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
        'Microsoft.Bcl.AsyncInterfaces',
        'Azure.Core',
        'Azure.Identity',
        'Microsoft.IdentityModel.Abstractions',
        'Microsoft.SqlServer.Dac',
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

# this takes 10ms
$assemblies = [System.AppDomain]::CurrentDomain.GetAssemblies()

try {
    $null = Import-Module ([IO.Path]::Combine($script:libraryroot, "third-party", "bogus", "Bogus.dll"))
} catch {
    Write-Error "Could not import $assemblyPath : $($_ | Out-String)"
}

foreach ($name in $names) {
    # REMOVED win-sqlclient handling and mac-specific logic since files are in standard lib folder

    # Check if this assembly should be skipped based on parameters
    $skipThisAssembly = $false

    # Check SkipAzure parameter
    if ($SkipAzure -and $name -in @('Microsoft.Bcl.AsyncInterfaces', 'Azure.Core', 'Azure.Identity', 'Microsoft.IdentityModel.Abstractions', 'Microsoft.Identity.Client')) {
        Write-Verbose "Skipping $name due to -SkipAzure parameter"
        $skipThisAssembly = $true
    }

    # Check SkipSmo parameter
    if ($SkipSmo -and $name -match '^Microsoft\.SqlServer\.') {
        Write-Verbose "Skipping $name due to -SkipSmo parameter"
        $skipThisAssembly = $true
    }

    # Check SkipAssemblies parameter
    if ($name -in $SkipAssemblies) {
        Write-Verbose "Skipping $name as it is in -SkipAssemblies list"
        $skipThisAssembly = $true
    }

    if ($skipThisAssembly) {
        continue
    }

    $x64only = 'Microsoft.SqlServer.Replication', 'Microsoft.SqlServer.XEvent.Linq', 'Microsoft.SqlServer.BatchParser', 'Microsoft.SqlServer.Rmo', 'Microsoft.SqlServer.BatchParserClient'

    if ($name -in $x64only -and $env:PROCESSOR_ARCHITECTURE -eq "x86") {
        Write-Verbose -Message "Skipping $name. x86 not supported for this library."
        continue
    }

    $assemblyPath = [IO.Path]::Combine($script:libraryroot, "lib", "$name.dll")
    $assemblyfullname = $assemblies.FullName | Out-String
    if (-not ($assemblyfullname.Contains("$name,"))) {
        $null = try {
            $null = Import-Module $assemblyPath
        } catch {
            Write-Error "Could not import $assemblyPath : $($_ | Out-String)"
        }
    }
}