# Assembly redirection for handling version conflicts
# This implements a direct assembly loading approach similar to the original implementation

function Initialize-DbatoolsAssemblyRedirector {
    [CmdletBinding()]
    param()

    Write-Verbose "Starting assembly redirector initialization"
    Write-Verbose "PSEdition: $($PSVersionTable.PSEdition)"
    Write-Verbose "Redirector type exists before: $('Redirector' -as [type])"

    Write-Verbose "Initializing assembly redirector"
    $dir = [System.IO.Path]::Combine($script:libraryroot, "lib")
    $dir = ("$dir\").Replace('\', '\\')
    Write-Verbose "Redirector lib directory: $dir"

    Write-Verbose "Library directory for redirector: $dir"

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
        Console.WriteLine("Redirector: AssemblyResolve called for " + e.Name);

        string[] systemDlls = {
            "System.Memory",
            "System.Runtime.CompilerServices.Unsafe",
            "System.Resources.Extensions",
            "System.Diagnostics.DiagnosticSource",
            "System.Private.CoreLib"
        };

        string[] azureDlls = {
            "Azure.Core",
            "Azure.Identity",
            "Microsoft.Identity.Client",
            "Microsoft.Identity.Client.Extensions.Msal",
            "Microsoft.IdentityModel.Abstractions"
        };

        string[] sqlDlls = {
            "Microsoft.Data.SqlClient",
            "Microsoft.SqlServer.ConnectionInfo",
            "Microsoft.SqlServer.Smo",
            "Microsoft.SqlServer.Types",
            "System.Configuration.ConfigurationManager",
            "Microsoft.SqlServer.Management.Sdk.Sfc",
            "Microsoft.SqlServer.Management.IntegrationServices",
           # "Microsoft.SqlServer.Replication",
            "Microsoft.SqlServer.Rmo"
        };

        var name = new AssemblyName(e.Name);
        var assemblyName = name.Name.ToString();

        // Handle System dependencies first
        if (systemDlls.Contains(assemblyName))
        {
            string filelocation = "$dir" + assemblyName + ".dll";
            Console.WriteLine("Redirector: Loading System dependency from " + filelocation);
            if (System.IO.File.Exists(filelocation))
            {
                var asm = Assembly.LoadFrom(filelocation);
                Console.WriteLine("Redirector: Loaded assembly version: " + asm.GetName().Version);
                return asm;
            }
        }

        // Handle Azure dependencies next
        if (azureDlls.Contains(assemblyName))
        {
            string filelocation = "$dir" + "win-sqlclient\\" + assemblyName + ".dll";
            Console.WriteLine("Redirector: Loading Azure dependency from " + filelocation);
            Console.WriteLine("Redirector: File exists: " + System.IO.File.Exists(filelocation));
            if (System.IO.File.Exists(filelocation))
            {
                var asm = Assembly.LoadFrom(filelocation);
                Console.WriteLine("Redirector: Loaded assembly version: " + asm.GetName().Version);
                return asm;
            }

            // Try core folder as fallback
            filelocation = "$dir" + "core\\" + assemblyName + ".dll";
            Console.WriteLine("Redirector: Trying core folder for Azure dependency: " + filelocation);
            if (System.IO.File.Exists(filelocation))
            {
                var asm = Assembly.LoadFrom(filelocation);
                Console.WriteLine("Redirector: Loaded assembly version: " + asm.GetName().Version);
                return asm;
            }
        }

        // Handle SQL assemblies last
        if (sqlDlls.Contains(assemblyName))
        {
            string filelocation = "$dir" + assemblyName + ".dll";
            Console.WriteLine("Redirector: Loading SQL dependency from " + filelocation);
            if (System.IO.File.Exists(filelocation))
            {
                var asm = Assembly.LoadFrom(filelocation);
                Console.WriteLine("Redirector: Loaded assembly version: " + asm.GetName().Version);
                return asm;
            }
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var info = assembly.GetName();
            if (info.FullName == e.Name) {
                Console.WriteLine("Redirector: Using already loaded assembly " + info.FullName);
                return assembly;
            }
        }
        Console.WriteLine("Redirector: Could not resolve " + e.Name);
        return null;
    }
}
"@
            try {
                Write-Verbose "Adding Redirector type definition"
                Add-Type -TypeDefinition $source
                Write-Verbose "Successfully added Redirector type"
                Write-Verbose "Redirector type exists: $('Redirector' -as [type])"
            }
            catch {
                Write-Warning "Failed to add Redirector type: $_"
                throw
            }
    }

    try {
        Write-Verbose "Creating Redirector instance"
        $redirector = New-Object Redirector
        Write-Verbose "Adding assembly resolve handler"
        [System.AppDomain]::CurrentDomain.add_AssemblyResolve($redirector.EventHandler)
        Write-Verbose "Successfully initialized assembly redirector"

        # Store the redirector instance in script scope for cleanup
        $script:dbatoolsRedirector = $redirector
        Write-Verbose "Stored redirector instance in script scope"
    }
    catch {
        Write-Warning "Failed to initialize assembly redirector: $_"
        throw
    }
}

Export-ModuleMember -Function Initialize-DbatoolsAssemblyRedirector