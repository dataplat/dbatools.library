function Get-DbatoolsLibraryPath {
    [CmdletBinding()]
    param()
    $PSScriptRoot
}

$script:libraryroot = Get-DbatoolsLibraryPath

if ($PSVersionTable.PSEdition -ne "Core") {
    $dir = [System.IO.Path]::Combine($script:libraryroot, "lib", "desktop")
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
                        "Azure.Core",
                        "System.Runtime.CompilerServices.Unsafe",
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
                        "System.Private.CoreLib"
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


if ($IsWindows -and $PSVersionTable.PSEdition -eq "Core") {
    if ($env:PROCESSOR_ARCHITECTURE -eq "x86") {
        $sqlclient = [System.IO.Path]::Combine($script:libraryroot, "lib", "win-sqlclient-x86", "Microsoft.Data.SqlClient.dll")
    } else {
        $sqlclient = [System.IO.Path]::Combine($script:libraryroot, "lib", "win-sqlclient", "Microsoft.Data.SqlClient.dll")
    }
} else {
    $sqlclient = [System.IO.Path]::Combine($script:libraryroot, "lib", "win-sqlclient", "Microsoft.Data.SqlClient.dll")
}

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
        'Microsoft.SqlServer.Dac',
        'Microsoft.SqlServer.Smo',
        'Microsoft.SqlServer.SmoExtended',
        'Microsoft.SqlServer.Management.RegisteredServers',
        'Microsoft.SqlServer.Management.Collector',
        'Microsoft.SqlServer.Management.XEvent',
        'Microsoft.SqlServer.Management.XEventDbScoped',
        'Microsoft.SqlServer.XEvent.XELite',
        [IO.Path]::Combine("lib", "third-party", "LumenWorks", "core", "LumenWorks.Framework.IO")
        'Azure.Core',
        'Azure.Identity',
        'Microsoft.IdentityModel.Abstractions'
    )
} else {
    $names = @(
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
        [IO.Path]::Combine($script:libraryroot, "lib", "third-party", "LumenWorks", "desktop", "LumenWorks.Framework.IO")
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

# Import Bogus from the correct framework directory
try {
    $boguspath = if ($PSVersionTable.PSEdition -eq "Core") {
        [IO.Path]::Combine($script:libraryroot, "lib", "third-party", "bogus", "core", "bogus.dll")
    } else {
        [IO.Path]::Combine($script:libraryroot, "lib", "third-party", "bogus", "desktop", "bogus.dll")
    }
    $null = Import-Module $boguspath
} catch {
    Write-Error "Could not import $boguspath : $($_ | Out-String)"
}

foreach ($name in $names) {
    if ($name.StartsWith("win-sqlclient\") -and ($isLinux -or $IsMacOS)) {
        $name = $name.Replace("win-sqlclient\", "")
        if ($IsMacOS -and $name -in "Azure.Core", "Azure.Identity", "System.Security.SecureString") {
            $name = "mac\$name"
        }
    }
    $x64only = 'Microsoft.SqlServer.Replication', 'Microsoft.SqlServer.XEvent.Linq', 'Microsoft.SqlServer.BatchParser', 'Microsoft.SqlServer.Rmo', 'Microsoft.SqlServer.BatchParserClient'

    if ($name -in $x64only -and $env:PROCESSOR_ARCHITECTURE -eq "x86") {
        Write-Verbose -Message "Skipping $name. x86 not supported for this library."
        continue
    }

    $subfolder = if ($PSVersionTable.PSEdition -eq "Core") { "core" } else { "desktop" }
    $assemblyPath = [IO.Path]::Combine($script:libraryroot, "lib", $subfolder, "$name.dll")
    $assemblyfullname = $assemblies.FullName | Out-String
    if (-not ($assemblyfullname.Contains("$name,".Replace("win-sqlclient\", "")))) {
        $null = try {
            $null = Import-Module $assemblyPath
        } catch {
            Write-Error "Could not import $assemblyPath : $($_ | Out-String)"
        }
    }
}