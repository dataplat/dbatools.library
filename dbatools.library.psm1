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

try {
    Import-Module $sqlclient
} catch {
    throw "Couldn't import $sqlclient | $PSItem"
}

if ($PSVersionTable.PSEdition -ne "Core") {
    [System.AppDomain]::CurrentDomain.remove_AssemblyResolve($onAssemblyResolveEventHandler)
}

if ($PSVersionTable.PSEdition -eq "Core") {
    $names = @(
        'Microsoft.SqlServer.Server',
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
        'Azure.Core',
        'Azure.Identity',
        'Microsoft.IdentityModel.Abstractions',
        'Microsoft.Data.SqlClient',
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

try {
    $null = Import-Module ([IO.Path]::Combine($script:libraryroot, "third-party", "LumenWorks", "LumenWorks.Framework.IO.dll"))
} catch {
    Write-Error "Could not import LumenWorks.Framework.IO.dll : $($_ | Out-String)"
}

foreach ($name in $names) {
    # REMOVED win-sqlclient handling and mac-specific logic since files are in standard lib folder

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