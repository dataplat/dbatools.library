#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Lists available SMO/SqlClient versions. Port of public/Get-DbaManagementObject.ps1
/// (W1-080). The begin-block SCRIPTBLOCK rides the module hop VERBATIM
/// (tooling-extracted) together with the local/remote split: a local computer runs it
/// through Invoke-Command with NO ArgumentList (the bound -VersionNumber is IGNORED
/// locally - $args[0] casts to 0, quirk kept) while a remote computer rides the PRIVATE
/// Invoke-Command2 with the version and the remote flag; the process loop walks the
/// member-enumerated $ComputerName.ComputerName strings and the catch targets the WHOLE
/// parameter array. Surface pinned by migration/baselines/Get-DbaManagementObject.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaManagementObject")]
public sealed class GetDbaManagementObjectCommand : DbaBaseCmdlet
{
    /// <summary>The target computer(s); defaults to the local computer.</summary>
    [Parameter(ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[] ComputerName { get; set; } = new DbaInstanceParameter[] { new DbaInstanceParameter(Environment.GetEnvironmentVariable("COMPUTERNAME")) };

    /// <summary>Windows credential for the remote invocation.</summary>
    [Parameter(Position = 1)]
    public PSCredential? Credential { get; set; }

    /// <summary>The SMO major version to look for (remote calls only - quirk kept).</summary>
    [Parameter(Position = 2)]
    public int VersionNumber { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        // PS: foreach ($computer in $ComputerName.ComputerName) - member enumeration.
        List<object?> computers = new List<object?>();
        foreach (DbaInstanceParameter item in ComputerName)
            computers.Add(item?.ComputerName);

        foreach (object? computer in computers)
        {
            try
            {
                WriteMessage(MessageLevel.Verbose, "Executing scriptblock against " + PsText(computer));
                foreach (PSObject? item in NestedCommand.InvokeScoped(this, InvokeScript, computer, Credential, VersionNumber, BoundVerbose()))
                    WriteObject(item);
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                StopFunction("Failure", target: ComputerName, errorRecord: StatementFault.Record(ex, "Get-DbaManagementObject"), continueLoop: true);
                continue;
            }
        }
    }

    /// <summary>PS string interpolation via LanguagePrimitives (invariant).</summary>
    private static string PsText(object? value)
    {
        if (value is null)
            return "";
        return (string)LanguagePrimitives.ConvertTo(value, typeof(string), CultureInfo.InvariantCulture);
    }

    /// <summary>A bound -Verbose carrier for the hop scopes (W1-044 convention).</summary>
    private object? BoundVerbose()
    {
        object? verbose;
        if (MyInvocation.BoundParameters.TryGetValue("Verbose", out verbose))
            return LanguagePrimitives.IsTrue(verbose);
        return null;
    }

    // PS: the begin-block scriptblock VERBATIM + the local/remote invocation split.
    private const string InvokeScript = """
param($__computer, $Credential, $VersionNumber, $__boundVerbose)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($__computer, $Credential, $VersionNumber, $__boundVerbose)
    if ($null -ne $__boundVerbose) { $VerbosePreference = $(if ($__boundVerbose) { "Continue" } else { "SilentlyContinue" }) }
        $scriptBlock = {
            $VersionNumber = [int]$args[0]
            $remote = $args[1]
            <# DO NOT use Write-Message as this is inside of a script block #>
            Write-Verbose -Message "Checking currently loaded SMO, SqlClient, and related assemblies"
            $loadedassemblies = [AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.Fullname -like "Microsoft.SqlServer.SMO,*" -or $_.Fullname -like "*.smo.*" -or $_.Fullname -like "*SqlClient*" -or $_.Fullname -like "*sqlclient*sni*" }
            $loadedversion = @()
            $loadedversionPath = $null
            if ($loadedassemblies) {
                Write-Verbose -Message "Found $($loadedassemblies.Count) loaded SQL-related assemblies: $($loadedassemblies.FullName -join ', ')"
                $loadedversion = $loadedassemblies | ForEach-Object {
                    # Extract version from assembly FullName (e.g., "Microsoft.SqlServer.Smo, Version=17.100.0.0, Culture=neutral, PublicKeyToken=89845dcd8080cc91")
                    if ($_.FullName -match "Version=([^,]+)") {
                        $matches[1]
                    } elseif ($_.Location -match "__") {
                        ((Split-Path (Split-Path $_.Location) -Leaf) -split "__")[0]
                    } else {
                        ((Get-ChildItem -Path $_.Location).VersionInfo.ProductVersion)
                    }
                }
                $loadedversionPath = $loadedassemblies[0].Location
            } else {
                Write-Verbose -Message "No SQL-related assemblies currently loaded in AppDomain"
            }

            # Check for SNI modules loaded in the current process
            $sniModules = @()
            try {
                $sniModules = Get-Process -Id $PID | ForEach-Object {
                    $_.Modules | Where-Object { $_.ModuleName -like '*SNI*' }
                }
                if ($sniModules) {
                    Write-Verbose -Message "Found $($sniModules.Count) SNI modules: $($sniModules.ModuleName -join ', ')"
                }
            } catch {
                Write-Verbose -Message "Error checking for SNI modules: $($_.Exception.Message)"
            }

            if (-not $remote) {
                <# DO NOT use Write-Message as this is inside of a script block #>
                $liblocation = ([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.Fullname -like "Microsoft.SqlServer.SMO,*" -or $_.Fullname -like "*.smo.*" -or $_.Fullname -like "*SqlClient*" -or $_.Fullname -like "*sqlclient*sni*" } | Select-Object -First 1).Location

                Write-Verbose -Message "Looking for included smo library at $liblocation"
                $initialversion = (Get-ChildItem -Path $liblocation).VersionInfo.ProductVersion -split "\+" | Select-Object -First 1
                $localversion = [version]$initialversion

                foreach ($version in $localversion) {
                    if ($VersionNumber -eq 0) {
                        <# DO NOT use Write-Message as this is inside of a script block #>
                        Write-Verbose -Message "Did not pass a version"
                        # Check if any loaded version matches this local version (compare major.minor versions)
                        $isLoaded = $false
                        foreach ($loadedVer in $loadedversion) {
                            $loadedVerObj = [version]$loadedVer
                            if ($loadedVerObj.Major -eq $localversion.Major -and $loadedVerObj.Minor -eq $localversion.Minor) {
                                $isLoaded = $true
                                break
                            }
                        }
                        [PSCustomObject]@{
                            ComputerName = [string]$env:COMPUTERNAME
                            Version      = [string]$localversion
                            Loaded       = [bool]$isLoaded
                            Path         = [string]$loadedversionPath
                            LoadTemplate = [string]("Add-Type -Path " + [string]$loadedversionPath)
                        }
                    } else {
                        <# DO NOT use Write-Message as this is inside of a script block #>
                        Write-Verbose -Message "Passed version $VersionNumber, looking for that specific version"
                        if ($localversion.ToString().StartsWith("$VersionNumber.")) {

                            $loadedversionPath = $loadedversion.Location
                            <# DO NOT use Write-Message as this is inside of a script block #>
                            Write-Verbose -Message "Found the Version $VersionNumber"
                            # Check if any loaded version matches this local version (compare major.minor versions)
                            $isLoaded = $false
                            foreach ($loadedVer in $loadedversion) {
                                $loadedVerObj = [version]$loadedVer
                                if ($loadedVerObj.Major -eq $localversion.Major -and $loadedVerObj.Minor -eq $localversion.Minor) {
                                    $isLoaded = $true
                                    break
                                }
                            }
                            [PSCustomObject]@{
                                ComputerName = [string]$env:COMPUTERNAME
                                Version      = [string]$localversion
                                Loaded       = [bool]$isLoaded
                                Path         = [string]$loadedversionPath
                                LoadTemplate = [string]("Add-Type -Path " + [string]$loadedversionPath)
                            }
                        }
                    }
                }

                # Output loaded assemblies that don't have corresponding local files
                foreach ($assembly in $loadedassemblies) {
                    $assemblyVersion = ""
                    if ($assembly.FullName -match "Version=([^,]+)") {
                        $assemblyVersion = $matches[1]
                    }

                    # Check if this assembly version is already covered by local files
                    $alreadyCovered = $false
                    if ($assemblyVersion) {
                        $assemblyVerObj = [version]$assemblyVersion
                        if ($localversion -and $assemblyVerObj.Major -eq $localversion.Major -and $assemblyVerObj.Minor -eq $localversion.Minor) {
                            $alreadyCovered = $true
                        }
                    }

                    # Only output if not already covered by local file detection
                    if (-not $alreadyCovered -and $assemblyVersion) {
                        [PSCustomObject]@{
                            ComputerName = [string]$env:COMPUTERNAME
                            Version      = [string]$assemblyVersion
                            Loaded       = $true
                            Path         = [string]$assembly.Location
                            LoadTemplate = [string]("Add-Type -Path `"" + [string]$assembly.Location + "`"")
                        }
                    }
                }
            }

            <# DO NOT use Write-Message as this is inside of a script block #>
            if (-not $IsLinux -and -not $IsMacOs) {
                $smolist = (Get-ChildItem -Path "$env:SystemRoot\assembly\GAC_MSIL\Microsoft.SqlServer.Smo" -ErrorAction Ignore | Sort-Object Name -Descending).Name
                $second = $false

                if (-not $smoList) {
                    $smoList = (Get-ChildItem -Path "$($env:SystemRoot)\Microsoft.NET\assembly\GAC_MSIL\Microsoft.SqlServer.Smo" -Filter "*$number.*" -ErrorAction Ignore | Where-Object FullName -match "_$number" | Sort-Object Name -Descending).Name
                    $second = $true
                }

                if (-not $smolist) {
                    Write-Verbose -Message "No SMO versions found in GAC"
                    continue
                }

                foreach ($version in $smolist) {
                    if ($second) {
                        $array = $version.Split("_")
                        $currentversion = $array[1]
                    } else {
                        $array = $version.Split("__")
                        $currentversion = $array[0]
                    }
                    if ($VersionNumber -eq 0) {
                        <# DO NOT use Write-Message as this is inside of a script block #>
                        Write-Verbose -Message "Did not pass a version, looking for all versions"

                        [PSCustomObject]@{
                            ComputerName = [string]$env:COMPUTERNAME
                            Version      = [string]$currentversion
                            Loaded       = [bool]($loadedversion -contains $currentversion)
                            Path         = $null
                            LoadTemplate = [string]("Add-Type -AssemblyName `"Microsoft.SqlServer.Smo, Version=" + [string]$currentversion + ", Culture=neutral, PublicKeyToken=89845dcd8080cc91`"")
                        }
                    } else {
                        <# DO NOT use Write-Message as this is inside of a script block #>
                        Write-Verbose -Message "Passed version $VersionNumber, looking for that specific version"
                        if ($currentversion.StartsWith("$VersionNumber.")) {
                            <# DO NOT use Write-Message as this is inside of a script block #>
                            Write-Verbose -Message "Found the Version $VersionNumber"

                            [PSCustomObject]@{
                                ComputerName = [string]$env:COMPUTERNAME
                                Version      = [string]$currentversion
                                Loaded       = [bool]($loadedversion -contains $currentversion)
                                Path         = $null
                                LoadTemplate = [string]("Add-Type -AssemblyName `"Microsoft.SqlServer.Smo, Version=" + [string]$currentversion + ", Culture=neutral, PublicKeyToken=89845dcd8080cc91`"")
                            }
                        }

                    }
                }
            }

            # Output SNI modules found (always run this regardless of other conditions)
            foreach ($sniModule in $sniModules) {
                $moduleVersion = "Unknown"
                try {
                    if ($sniModule.FileVersionInfo -and $sniModule.FileVersionInfo.FileVersion) {
                        $moduleVersion = $sniModule.FileVersionInfo.FileVersion
                    }
                } catch {
                    # Ignore version extraction errors
                }

                # Find the corresponding SqlClient assembly for this SNI module by matching directory structure
                $sqlClientPath = ""
                $sniPath = $sniModule.FileName

                # Look for SqlClient in the same directory tree (usually parent of runtimes folder)
                $sqlClientAssembly = $loadedassemblies | Where-Object {
                    $_.FullName -like "*SqlClient*" -and
                    $sniPath -like "*$([System.IO.Path]::GetDirectoryName([System.IO.Path]::GetDirectoryName([System.IO.Path]::GetDirectoryName($_.Location))))*"
                }

                if (-not $sqlClientAssembly) {
                    # Fallback: try to find SqlClient in parent directories of SNI path
                    $sniDir = [System.IO.Path]::GetDirectoryName($sniPath)
                    while ($sniDir -and -not $sqlClientAssembly) {
                        $sniDir = [System.IO.Path]::GetDirectoryName($sniDir)
                        $potentialSqlClientPath = Join-Path $sniDir "Microsoft.Data.SqlClient.dll"
                        $sqlClientAssembly = $loadedassemblies | Where-Object { $_.Location -eq $potentialSqlClientPath }
                    }
                }

                if ($sqlClientAssembly) {
                    $sqlClientPath = $sqlClientAssembly.Location
                }

                [PSCustomObject]@{
                    ComputerName = [string]$env:COMPUTERNAME
                    Version      = [string]$moduleVersion
                    Loaded       = $true
                    Path         = [string]$sniModule.FileName
                    LoadTemplate = if ($sqlClientPath) { [string]("Add-Type -Path `"" + [string]$sqlClientPath + "`" -ReferencedAssemblies `"" + [string]$sniModule.FileName + "`"") } else { "" }
                }
            }
        }
    # the foreach shell absorbs the scriptblock's bare `continue` (the no-GAC path) the
    # way the function's computer loop did - rows emitted before it are kept, the
    # remainder is skipped, and the caller moves to the next computer
    foreach ($__w1080Shell in 1) {
        if ($__computer -eq $env:COMPUTERNAME -or $__computer -eq "localhost") {
            # local Invoke-Command cannot host inside this nested pipeline; the
            # scope-equivalent invocation with a Stop preference matches -ErrorAction Stop
            $ErrorActionPreference = "Stop"
            & $scriptBlock
        } else {
            Invoke-Command2 -ComputerName $__computer -ScriptBlock $scriptBlock -Credential $Credential -ArgumentList $VersionNumber, $true -ErrorAction Stop
        }
    }
} $__computer $Credential $VersionNumber $__boundVerbose 3>&1
""";
}
