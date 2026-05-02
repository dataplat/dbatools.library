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
    if (-not ("Redirector" -as [type])) {
        $source = @"
            using System;
            using System.IO;
            using System.Reflection;

            public class Redirector
            {
                private static string _libPath;

                public Redirector(string libPath)
                {
                    _libPath = libPath;
                    this.EventHandler = new ResolveEventHandler(AssemblyResolve);
                }

                public readonly ResolveEventHandler EventHandler;

                protected static Assembly AssemblyResolve(object sender, ResolveEventArgs e)
                {
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

                    // Try to load from our lib folder if the file exists
                    string dllPath = Path.Combine(_libPath, assemblyName + ".dll");
                    if (File.Exists(dllPath))
                    {
                        try
                        {
                            return Assembly.LoadFrom(dllPath);
                        }
                        catch
                        {
                            // Failed to load, return null to let default resolution continue
                        }
                    }

                    return null;
                }
            }
"@

        $null = Add-Type -TypeDefinition $source
    }

    try {
        $libPath = [System.IO.Path]::Combine($script:libraryroot, "lib")
        $redirector = New-Object Redirector($libPath)
        [System.AppDomain]::CurrentDomain.add_AssemblyResolve($redirector.EventHandler)
    } catch {
        Write-Verbose "Could not register Redirector: $_"
    }
} else {
    # PowerShell Core: Use AssemblyLoadContext.Resolving event for version redirection
    # This handles version mismatches when SqlServer module loads different versions of assemblies
    # IMPORTANT: Must be implemented in C# because the resolver runs on .NET threads without PowerShell runspaces
    $dir = [System.IO.Path]::Combine($script:libraryroot, "lib")
    $dir = ("$dir" + [System.IO.Path]::DirectorySeparatorChar).Replace('\', '\\')

    if (-not ("CoreRedirector" -as [type])) {
        $coreSource = @"
            using System;
            using System.Collections.Generic;
            using System.IO;
            using System.Reflection;
            using System.Runtime.InteropServices;
            using System.Runtime.Loader;

            public class CoreRedirector
            {
                private static string _libPath;
                private static bool _registered = false;

                public static void Register(string libPath)
                {
                    if (_registered) return;
                    _libPath = libPath;
                    AssemblyLoadContext.Default.Resolving += OnResolving;
                    AssemblyLoadContext.Default.ResolvingUnmanagedDll += OnResolvingUnmanagedDll;
                    _registered = true;
                }

                private static Assembly OnResolving(AssemblyLoadContext context, AssemblyName assemblyName)
                {
                    string name = assemblyName.Name;

                    // First, check if any version of this assembly is already loaded
                    // This handles version mismatches (e.g., dbatools.dll requesting ConnectionInfo 17.100.0.0 when 17.200.0.0 is loaded)
                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        try
                        {
                            if (assembly.GetName().Name == name)
                            {
                                return assembly;
                            }
                        }
                        catch
                        {
                            // Some assemblies may throw when accessing GetName()
                        }
                    }

                    foreach (string dllPath in GetManagedAssemblyPaths(name))
                    {
                        if (File.Exists(dllPath))
                        {
                            try
                            {
                                return AssemblyLoadContext.Default.LoadFromAssemblyPath(dllPath);
                            }
                            catch
                            {
                                // Failed to load, try the next candidate
                            }
                        }
                    }

                    return null;
                }

                private static IntPtr OnResolvingUnmanagedDll(Assembly assembly, string libraryName)
                {
                    string architectureRid = GetArchitectureRid();
                    if (String.IsNullOrEmpty(architectureRid))
                    {
                        return IntPtr.Zero;
                    }

                    foreach (string fileName in GetNativeLibraryNames(libraryName))
                    {
                        string nativePath = Path.Combine(_libPath, "runtimes", architectureRid, "native", fileName);
                        if (File.Exists(nativePath))
                        {
                            try
                            {
                                return NativeLibrary.Load(nativePath);
                            }
                            catch
                            {
                                // Failed to load, try the next candidate
                            }
                        }
                    }

                    return IntPtr.Zero;
                }

                private static string[] GetManagedAssemblyPaths(string name)
                {
                    string fileName = name + ".dll";
                    string platformRid = GetPlatformRid();
                    string architectureRid = GetArchitectureRid();

                    var paths = new List<string>();
                    paths.Add(Path.Combine(_libPath, fileName));

                    if (!String.IsNullOrEmpty(platformRid))
                    {
                        paths.Add(Path.Combine(_libPath, "runtimes", platformRid, "lib", "net8.0", fileName));
                        paths.Add(Path.Combine(_libPath, "runtimes", platformRid, "lib", "netstandard1.6", fileName));
                    }

                    if (!String.IsNullOrEmpty(architectureRid))
                    {
                        paths.Add(Path.Combine(_libPath, "runtimes", architectureRid, "lib", "net8.0", fileName));
                        paths.Add(Path.Combine(_libPath, "runtimes", architectureRid, "lib", "netstandard1.6", fileName));
                    }

                    return paths.ToArray();
                }

                private static string[] GetNativeLibraryNames(string libraryName)
                {
                    var names = new List<string>();
                    names.Add(libraryName);

                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        if (!libraryName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        {
                            names.Add(libraryName + ".dll");
                        }
                    }
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        if (!libraryName.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase))
                        {
                            names.Add(libraryName + ".dylib");
                            names.Add("lib" + libraryName + ".dylib");
                        }
                    }
                    else
                    {
                        if (!libraryName.EndsWith(".so", StringComparison.OrdinalIgnoreCase))
                        {
                            names.Add(libraryName + ".so");
                            names.Add("lib" + libraryName + ".so");
                        }
                    }

                    return names.ToArray();
                }

                private static string GetPlatformRid()
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        return "win";
                    }

                    return "unix";
                }

                private static string GetArchitectureRid()
                {
                    string osPart;
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        osPart = "win";
                    }
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    {
                        osPart = "linux";
                    }
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        osPart = "osx";
                    }
                    else
                    {
                        return null;
                    }

                    string architecture;
                    switch (RuntimeInformation.ProcessArchitecture)
                    {
                        case Architecture.X86:
                            architecture = "x86";
                            break;
                        case Architecture.X64:
                            architecture = "x64";
                            break;
                        case Architecture.Arm:
                            architecture = "arm";
                            break;
                        case Architecture.Arm64:
                            architecture = "arm64";
                            break;
                        default:
                            return null;
                    }

                    return osPart + "-" + architecture;
                }
            }
"@

        try {
            $null = Add-Type -TypeDefinition $coreSource -ReferencedAssemblies 'System.Runtime.Loader','System.Runtime.InteropServices','System.Collections'
        } catch {
            Write-Verbose "Could not compile CoreRedirector: $_"
        }
    }

    try {
        [CoreRedirector]::Register($dir)
    } catch {
        Write-Verbose "Could not register CoreRedirector: $_"
    }
}

function Get-DbatoolsSqlClientPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$LibraryRoot
    )

    $libPath = [System.IO.Path]::Combine($LibraryRoot, "lib")
    if ($PSVersionTable.PSEdition -eq "Core") {
        if ($IsWindows) {
            $runtimeSqlClient = [System.IO.Path]::Combine($libPath, "runtimes", "win", "lib", "net8.0", "Microsoft.Data.SqlClient.dll")
        } else {
            $runtimeSqlClient = [System.IO.Path]::Combine($libPath, "runtimes", "unix", "lib", "net8.0", "Microsoft.Data.SqlClient.dll")
        }

        if (Test-Path $runtimeSqlClient) {
            return $runtimeSqlClient
        }
    }

    [System.IO.Path]::Combine($libPath, "Microsoft.Data.SqlClient.dll")
}

function Add-DbatoolsNativeSearchPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$LibraryRoot
    )

    if ($PSVersionTable.PSEdition -ne "Core" -or -not $IsWindows) {
        return
    }

    $architecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
    $nativeRid = "win-$architecture"
    $nativePath = [System.IO.Path]::Combine($LibraryRoot, "lib", "runtimes", $nativeRid, "native")
    if (-not (Test-Path $nativePath)) {
        return
    }

    $pathSeparator = [System.IO.Path]::PathSeparator
    $pathParts = $env:PATH -split [Regex]::Escape([string]$pathSeparator)
    if ($pathParts -contains $nativePath) {
        return
    }

    $env:PATH = $nativePath + $pathSeparator + $env:PATH
    [System.Environment]::SetEnvironmentVariable("PATH", $env:PATH, "Process")
}

Add-DbatoolsNativeSearchPath -LibraryRoot $script:libraryroot
$sqlclient = Get-DbatoolsSqlClientPath -LibraryRoot $script:libraryroot

# Get loaded assemblies once for reuse (used for AvoidConflicts checks and later assembly loading)
$script:loadedAssemblies = [System.AppDomain]::CurrentDomain.GetAssemblies()

# Check for incompatible System.ClientModel or SqlClient versions
# Azure.Core 1.44+ requires System.ClientModel 1.1+ with IPersistableModel.Write() method
# SqlServer module's SqlClient 5.x includes older System.ClientModel that's incompatible
$script:hasIncompatibleClientModel = $false
if ($AvoidConflicts) {
    # Check if System.ClientModel is already loaded with incompatible version
    $existingClientModel = $script:loadedAssemblies | Where-Object { $_.GetName().Name -eq 'System.ClientModel' }
    if ($existingClientModel) {
        $clientModelVersion = $existingClientModel.GetName().Version
        # System.ClientModel 1.1.0+ has the required IPersistableModel interface changes
        if ($clientModelVersion -lt [Version]'1.1.0') {
            $script:hasIncompatibleClientModel = $true
            Write-Verbose "Detected incompatible System.ClientModel version $clientModelVersion - will skip Azure.Core and Azure.Identity to avoid MissingMethodException"
        }
    }

    # Check if SqlServer's older SqlClient is loaded (which bundles incompatible System.ClientModel)
    # SqlClient 5.x from SqlServer module uses System.ClientModel 1.0.x
    # Our Azure.Core requires System.ClientModel 1.1+
    if (-not $script:hasIncompatibleClientModel) {
        $existingSqlClient = $script:loadedAssemblies | Where-Object { $_.GetName().Name -eq 'Microsoft.Data.SqlClient' }
        if ($existingSqlClient) {
            $sqlClientVersion = $existingSqlClient.GetName().Version
            # SqlClient 5.x bundles older System.ClientModel; 6.x bundles compatible versions
            if ($sqlClientVersion.Major -lt 6) {
                $script:hasIncompatibleClientModel = $true
                Write-Verbose "Detected SqlClient $sqlClientVersion (pre-6.0) which uses incompatible System.ClientModel - will skip Azure.Core and Azure.Identity to avoid MissingMethodException"
            }
        }
    }
}

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

    # Skip Azure.Core and Azure.Identity if System.ClientModel is incompatible
    # These assemblies depend on System.ClientModel 1.1+ which has breaking API changes
    if ($script:hasIncompatibleClientModel -and $name -in @('Azure.Core', 'Azure.Identity')) {
        Write-Verbose "Skipping $name.dll - incompatible System.ClientModel already loaded"
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
