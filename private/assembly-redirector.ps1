# Assembly redirection for handling version conflicts
# This implements a direct assembly loading approach similar to the original implementation

function Initialize-DbatoolsAssemblyRedirector {
    [CmdletBinding()]
    param()

    # Use the library root directly without adding "lib"
    $dir = ("$script:libraryroot\").Replace('\', '\\')

    # Only create type if not already defined
    if (-not ("Redirector" -as [type]) -and $PSVersionTable.PSEdition -ne 'Core') {
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

        // Try desktop path first for Windows PowerShell
        string desktopPath = "$dir" + "desktop\\lib\\runtimes\\win-" + arch + "\\native";
        string corePath = "$dir" + "core\\lib\\runtimes\\win-" + arch + "\\native";
        string sniDll = System.IO.Path.Combine(desktopPath, "Microsoft.Data.SqlClient.SNI.dll");

        if (!System.IO.File.Exists(sniDll))
        {
            // Try architecture-specific name in desktop path
            sniDll = System.IO.Path.Combine(desktopPath, "Microsoft.Data.SqlClient.SNI." + arch + ".dll");

            if (!System.IO.File.Exists(sniDll))
            {
                // Fall back to core path
                sniDll = System.IO.Path.Combine(corePath, "Microsoft.Data.SqlClient.SNI.dll");

                if (!System.IO.File.Exists(sniDll))
                {
                    // Try architecture-specific name in core path as last resort
                    sniDll = System.IO.Path.Combine(corePath, "Microsoft.Data.SqlClient.SNI." + arch + ".dll");
                }
            }
        }

        if (System.IO.File.Exists(sniDll))
        {
            LoadLibrary(sniDll);
        }
        else
        {
            Console.WriteLine("Redirector: Warning - Could not find SNI DLL in desktop or core paths. Tried: " + desktopPath + " and " + corePath);
        }
    }

    public readonly ResolveEventHandler EventHandler;

    private static bool isResolving = false;

    protected Assembly AssemblyResolve(object sender, ResolveEventArgs e)
    {
        // Guard against empty assembly names
        if (e == null || string.IsNullOrEmpty(e.Name))
        {
            return null;
        }

        // Prevent recursive resolution
        if (isResolving)
        {
            return null;
        }

        isResolving = true;
        try
        {
        // Only track essential system dependencies
        string[] systemDlls = {
            "System.Memory",
            "System.Runtime.CompilerServices.Unsafe"
        };

        // Only track core Azure dependencies
        string[] azureDlls = {
            "Azure.Core",
            "Azure.Identity",
            "Microsoft.Identity.Client",
            "Microsoft.IdentityModel.Abstractions"
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
            string filelocation = "$dir" + "desktop\\" + assemblyName + ".dll";
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
            // Try desktop folder first for Windows PowerShell
            string filelocation = "$dir" + "desktop\\" + assemblyName + ".dll";
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
            // Try desktop folder first for Windows PowerShell
            string filelocation = "$dir" + "desktop\\" + assemblyName + ".dll";
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

            // Try root lib folder as last resort
            filelocation = "$dir" + assemblyName + ".dll";
            if (System.IO.File.Exists(filelocation))
            {
                var asm = Assembly.LoadFrom(filelocation);
                return asm;
            }
        }

        // Handle version binding redirects
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var info = assembly.GetName();
            if (info.Name == name.Name) {
                // For System.Memory, allow newer versions to satisfy older version requests
                if (info.Name == "System.Memory" && info.Version > name.Version) {
                    return assembly;
                }
                // For Microsoft.Data.SqlClient, allow newer versions to satisfy older version requests
                if (info.Name == "Microsoft.Data.SqlClient" && info.Version > name.Version) {
                    return assembly;
                }
                // For Identity DLLs, allow newer versions to satisfy older version requests
                if (info.Name == "Microsoft.Identity.Client" && info.Version > name.Version) {
                    return assembly;
                }
                if (info.Name == "Azure.Identity" && info.Version > name.Version) {
                    return assembly;
                }
                if (info.Name == "Azure.Core" && info.Version > name.Version) {
                    return assembly;
                }
                // For exact version matches
                if (info.FullName == e.Name) {
                    return assembly;
                }
            }
        }
            return null;
        }
        finally
        {
            isResolving = false;
        }
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

    # Only initialize redirector for non-Core PowerShell if not already initialized
    if ($PSVersionTable.PSEdition -ne 'Core' -and -not $script:dbatoolsRedirector) {
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
}

Export-ModuleMember -Function Initialize-DbatoolsAssemblyRedirector