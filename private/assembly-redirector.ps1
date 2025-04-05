# Assembly redirection for handling version conflicts
# This implements a direct assembly loading approach similar to the original implementation

function Initialize-DbatoolsAssemblyRedirector {
    [CmdletBinding()]
    param()

    $dir = [System.IO.Path]::Combine($script:libraryroot, "lib")
    $dir = ("$dir\").Replace('\', '\\')

    if (-not ("Redirector" -as [type])) {
            $source = @"
using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

public class Redirector
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    public Redirector()
    {
        this.EventHandler = new ResolveEventHandler(AssemblyResolve);

        // Detect architecture
        string arch = "x64";
        if (Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") == "x86")
            arch = "x86";
        else if (Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") == "ARM64")
            arch = "arm64";
        else if (Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") == "ARM")
            arch = "arm";

        // Try both native paths
        string nativePath = "$dir" + "core\\runtimes\\win-" + arch + "\\native";
        string sniDll = System.IO.Path.Combine(nativePath, "Microsoft.Data.SqlClient.SNI.dll");

        if (!System.IO.File.Exists(sniDll))
        {
            // Try architecture-specific name as fallback
            sniDll = System.IO.Path.Combine(nativePath, "Microsoft.Data.SqlClient.SNI." + arch + ".dll");
        }

        if (System.IO.File.Exists(sniDll))
        {
            LoadLibrary(sniDll);
        }
        else
        {
            Console.WriteLine("Redirector: Warning - Could not find SNI DLL in: " + nativePath);
        }
    }

    public readonly ResolveEventHandler EventHandler;

    protected Assembly AssemblyResolve(object sender, ResolveEventArgs e)
    {

        // Only track essential system dependencies
        string[] systemDlls = {
            "System.Memory",
            "System.Runtime.CompilerServices.Unsafe"
        };

        // Only track core Azure dependencies
        string[] azureDlls = {
            "Azure.Core",
            "Azure.Identity"
        };

        // Only track essential SQL dependencies
        string[] sqlDlls = {
            "Microsoft.Data.SqlClient",
            "Microsoft.SqlServer.Smo",
            "Microsoft.SqlServer.Management.Sdk.Sfc"
        };

        var name = new AssemblyName(e.Name);
        var assemblyName = name.Name.ToString();

        // Handle System dependencies first
        if (systemDlls.Contains(assemblyName))
        {
            // Try desktop folder first for Windows PowerShell
            string filelocation = "$dir" + "desktop\\net472\\" + assemblyName + ".dll";
            if (System.IO.File.Exists(filelocation))
            {
                var asm = Assembly.LoadFrom(filelocation);
                return asm;
            }

            // Try core folder as fallback
            filelocation = "$dir" + "core\\" + assemblyName + ".dll";
            if (System.IO.File.Exists(filelocation))
            {
                var asm = Assembly.LoadFrom(filelocation);
                return asm;
            }
        }

        // Handle Azure dependencies next
        if (azureDlls.Contains(assemblyName))
        {
            string filelocation = "$dir" + "win-sqlclient\\" + assemblyName + ".dll";
            if (System.IO.File.Exists(filelocation))
            {
                var asm = Assembly.LoadFrom(filelocation);
                return asm;
            }

            // Try core folder as fallback
            filelocation = "$dir" + "core\\" + assemblyName + ".dll";
            if (System.IO.File.Exists(filelocation))
            {
                var asm = Assembly.LoadFrom(filelocation);
                return asm;
            }
        }

        // Handle SQL assemblies last
        if (sqlDlls.Contains(assemblyName))
        {
            // Try win-sqlclient folder first
            string filelocation = "$dir" + "win-sqlclient\\" + assemblyName + ".dll";
            if (System.IO.File.Exists(filelocation))
            {
                var asm = Assembly.LoadFrom(filelocation);
                return asm;
            }

            // Try desktop folder next for Windows PowerShell
            filelocation = "$dir" + "desktop\\net472\\" + assemblyName + ".dll";
            if (System.IO.File.Exists(filelocation))
            {
                var asm = Assembly.LoadFrom(filelocation);
                return asm;
            }

            // Try root lib folder as fallback
            filelocation = "$dir" + assemblyName + ".dll";
            if (System.IO.File.Exists(filelocation))
            {
                var asm = Assembly.LoadFrom(filelocation);
                return asm;
            }
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var info = assembly.GetName();
            if (info.FullName == e.Name) {
                return assembly;
            }
        }
        return null;
    }
}
"@
            try {
                Add-Type -TypeDefinition $source -ErrorAction Stop
            }
            catch {
                Write-Warning "Failed to add Redirector type: $($_.Exception.Message)"
                Write-Warning "Source code that failed:"
                Write-Warning $source
                throw "Failed to initialize assembly redirector: $($_.Exception.Message)"
            }
    }

    try {
        $redirector = New-Object Redirector
        [System.AppDomain]::CurrentDomain.add_AssemblyResolve($redirector.EventHandler)

        # Store the redirector instance in script scope for cleanup
        $script:dbatoolsRedirector = $redirector
    }
    catch {
        Write-Warning "Failed to initialize assembly redirector: $_"
        throw
    }
}

Export-ModuleMember -Function Initialize-DbatoolsAssemblyRedirector