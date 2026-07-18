#nullable enable

using System;
using System.Collections;
using System.Management.Automation;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Builds a DACPAC from a directory of SQL script files using the DacFx model API. Port of
/// public/New-DbaDacPackage.ps1; the workflow remains a module-scoped PowerShell compatibility hop.
///
/// BEGIN+PROCESS, two hops. NO parameter is ValueFromPipeline, so process fires exactly once - the
/// cross-record carry axis does not exist for this row. Stated explicitly rather than skipped,
/// because a parameter-only check is what let a destructive-path carry through on Move-DbaDbFile.
///
/// BEGIN CARRIES ONE VALUE. Begin verifies the DacFx model types are loadable and resolves -Path to
/// a full filesystem path, leaving $resolvedPath, which process reads five times (Get-Item, the
/// Get-ChildItem enumeration, and three messages). That single value rides the begin sentinel; the
/// only other begin statement is a discard.
///
/// INTERRUPT CARRY IS LIVE. Both begin guards - DacFx types unavailable, and -Path not found - are
/// Stop-Function WITHOUT -Continue followed by return, so each sets the module interrupt flag, and
/// the source's process opens with "if (Test-FunctionInterrupt) { return }". Within one function
/// scope that means a begin failure produces no output at all. Across separate hop invocations the
/// flag does not survive, so the begin hop reads it at Get-Variable -Scope 0 after its dot-sourced
/// body and carries it, and C# skips process when begin set it. The body keeps its own verbatim
/// Test-FunctionInterrupt line. Every one of the ten Stop-Function calls in this command omits
/// -Continue, so any failure ends the run rather than skipping an item.
///
/// The two $PSCmdlet.ShouldProcess gates - creating the output directory, and creating the DACPAC
/// itself - route to the real cmdlet via $__realCmdlet (SupportsShouldProcess, ConfirmImpact Low
/// mirrored). Both bodies are dot-sourced so every "Stop-Function; return" exits the body only
/// while the sentinel still emits. In-hop Stop-Function/Write-Message carry -FunctionName. Implicit
/// positions 0-6 are made explicit (Path 0 Mandatory, OutputPath 1, DacVersion 2, DacDescription 3,
/// DatabaseName 4, SqlServerVersion 5, Filter 6); the two switches correctly carry none. Surface
/// pinned by migration/baselines/New-DbaDacPackage.json.
/// </summary>
[Cmdlet(VerbsCommon.New, "DbaDacPackage", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low)]
public sealed class NewDbaDacPackageCommand : DbaBaseCmdlet
{
    /// <summary>Directory containing the SQL script files.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [PsStringCast]
    public string Path { get; set; } = null!;

    /// <summary>Where the resulting DACPAC is written.</summary>
    [Parameter(Position = 1)]
    [PsStringCast]
    public string? OutputPath { get; set; }

    /// <summary>Version stamped into the package.</summary>
    [Parameter(Position = 2)]
    public Version DacVersion { get; set; } = new Version("1.0.0.0");

    /// <summary>Description stamped into the package.</summary>
    [Parameter(Position = 3)]
    [PsStringCast]
    public string? DacDescription { get; set; }

    /// <summary>Database name recorded in the package.</summary>
    [Parameter(Position = 4)]
    [PsStringCast]
    public string? DatabaseName { get; set; }

    /// <summary>Target SQL Server version for the model.</summary>
    [Parameter(Position = 5)]
    [ValidateSet("Sql90", "Sql100", "Sql110", "Sql120", "Sql130", "Sql140", "Sql150", "Sql160", "SqlAzure")]
    [PsStringCast]
    public string SqlServerVersion { get; set; } = "Sql160";

    /// <summary>File filter applied when enumerating scripts.</summary>
    [Parameter(Position = 6)]
    [PsStringCast]
    public string Filter { get; set; } = "*.sql";

    /// <summary>Recurse into subdirectories when enumerating scripts.</summary>
    [Parameter]
    public SwitchParameter Recursive { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // The resolved -Path, computed once in begin and read throughout process (opaque to C#).
    private Hashtable? _beginState;
    // A begin guard failure silences process entirely.
    private bool _interrupted;

    protected override void BeginProcessing()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BeginScript,
            Path, EnableException.ToBool(),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (item?.BaseObject is Hashtable sentinel && sentinel.ContainsKey("__newDbaDacPackageBegin"))
            {
                if (sentinel["__newDbaDacPackageBegin"] is Hashtable state)
                {
                    _beginState = state;
                    _interrupted = LanguagePrimitives.IsTrue(state["Interrupted"]);
                }
                continue;
            }
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
                continue;
            }
            WriteObject(item);
        }
    }

    protected override void ProcessRecord()
    {
        if (Interrupted || _interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            Path, OutputPath, DacVersion, DacDescription, DatabaseName, SqlServerVersion, Filter,
            Recursive.ToBool(), EnableException.ToBool(), _beginState, this,
            BoundCommonParameter("WhatIf"), BoundCommonParameter("Confirm"),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
                continue;
            }
            WriteObject(item);
        }
    }

    private object? BoundCommonParameter(string name)
    {
        if (MyInvocation.BoundParameters.TryGetValue(name, out object? value))
            return LanguagePrimitives.IsTrue(value);
        return null;
    }

    private void RemoveHopErrorBookkeeping(ErrorRecord record)
    {
        try
        {
            if (SessionState.PSVariable.GetValue("Error") is not ArrayList errorList || errorList.Count == 0)
                return;
            if (errorList[0] is not ErrorRecord first)
                return;
            if (ReferenceEquals(first, record) || ReferenceEquals(first.Exception, record.Exception) ||
                string.Equals(first.Exception?.Message, record.Exception?.Message, StringComparison.Ordinal))
            {
                errorList.RemoveAt(0);
            }
        }
        catch
        {
            // Best-effort bookkeeping only.
        }
    }

    // PS: the begin block VERBATIM, dot-sourced so each guard's "Stop-Function; return" exits only
    // the body while the sentinel still emits. Edits: -FunctionName on the three calls. The sentinel
    // carries the resolved path and whether either guard set the interrupt.
    private const string BeginScript = """
param($Path, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([string]$Path, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    . {
        # The DacFx types are loaded by dbatools.library - verify they are available
        try {
            $null = [Microsoft.SqlServer.Dac.Model.TSqlModel]
            Write-Message -Level Verbose -Message "DacFx Model types are available from dbatools.library" -FunctionName New-DbaDacPackage
        } catch {
            Stop-Function -Message "DacFx Model types are not available. Ensure dbatools.library is properly loaded." -FunctionName New-DbaDacPackage
            return
        }

        # Resolve the full path
        $resolvedPath = Resolve-Path -Path $Path -ErrorAction SilentlyContinue

        if (-not $resolvedPath) {
            Stop-Function -Message "Path not found: $Path" -FunctionName New-DbaDacPackage
            return
        }

        $resolvedPath = $resolvedPath.Path
    }

    $__iv = Get-Variable -Name __dbatools_interrupt_function_78Q9VPrM6999g6zo24Qn83m09XF56InEn4hFrA8Fwhu5xJrs6r -Scope 0 -ErrorAction Ignore
    $__rp = Get-Variable -Name resolvedPath -Scope 0 -ErrorAction Ignore
    @{ __newDbaDacPackageBegin = @{ Interrupted = [bool]($__iv -and $__iv.Value); ResolvedPathAssigned = [bool]$__rp; ResolvedPath = $(if ($__rp) { $__rp.Value }) } }
} $Path $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
    // PS: the process block VERBATIM, dot-sourced so every "Stop-Function; return" exits only the
    // body. Edits: the two $PSCmdlet gates route to $__realCmdlet and -FunctionName is stamped on
    // the 22 direct Stop-Function/Write-Message calls. $resolvedPath restores from the begin
    // sentinel; unset-vs-assigned is preserved so a begin that never reached the assignment leaves
    // it unset here too.
    private const string ProcessScript = """
param($Path, $OutputPath, $DacVersion, $DacDescription, $DatabaseName, $SqlServerVersion, $Filter, $Recursive, $EnableException, $__beginState, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = "Low")]
    param([string]$Path, [string]$OutputPath, [version]$DacVersion, [string]$DacDescription, [string]$DatabaseName, [string]$SqlServerVersion, [string]$Filter, $Recursive, $EnableException, $__beginState, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    # the one value begin leaves for process
    if ($__beginState.ResolvedPathAssigned) { $resolvedPath = $__beginState.ResolvedPath }

    . {
        if (Test-FunctionInterrupt) { return }

        $resultsTime = [System.Diagnostics.Stopwatch]::StartNew()

        # Determine if path is a directory or .sqlproj file
        $pathItem = Get-Item -Path $resolvedPath

        if ($pathItem.PSIsContainer) {
            # Path is a directory - scan for SQL files
            $searchOption = if ($Recursive) { "AllDirectories" } else { "TopDirectoryOnly" }

            try {
                $sqlFiles = Get-ChildItem -Path $resolvedPath -Filter $Filter -File -Recurse:$Recursive -ErrorAction Stop
            } catch {
                Stop-Function -Message "Failed to enumerate SQL files in $resolvedPath" -ErrorRecord $_ -FunctionName New-DbaDacPackage
                return
            }

            if (-not $sqlFiles -or $sqlFiles.Count -eq 0) {
                Stop-Function -Message "No SQL files found in $resolvedPath matching filter '$Filter'" -FunctionName New-DbaDacPackage
                return
            }

            Write-Message -Level Verbose -Message "Found $($sqlFiles.Count) SQL files in $resolvedPath" -FunctionName New-DbaDacPackage

            # Default database name from directory name
            if (-not $DatabaseName) {
                $DatabaseName = $pathItem.Name
            }
        } elseif ($pathItem.Extension -eq ".sqlproj") {
            # Path is a .sqlproj file - parse the project
            Stop-Function -Message "Parsing .sqlproj files is not yet implemented. Please specify a directory path containing SQL files." -FunctionName New-DbaDacPackage
            return
        } else {
            Stop-Function -Message "Path must be a directory containing SQL files or a .sqlproj file. Got: $($pathItem.FullName)" -FunctionName New-DbaDacPackage
            return
        }

        # Set default output path if not specified
        if (-not $OutputPath) {
            $OutputPath = Join-Path -Path (Get-Location).Path -ChildPath "$DatabaseName.dacpac"
        }

        # Ensure output directory exists
        $outputDir = Split-Path -Path $OutputPath -Parent
        if ($outputDir -and -not (Test-Path -Path $outputDir)) {
            if ($__realCmdlet.ShouldProcess($outputDir, "Create output directory")) {
                try {
                    $null = New-Item -Path $outputDir -ItemType Directory -Force -ErrorAction Stop
                } catch {
                    Stop-Function -Message "Failed to create output directory: $outputDir" -ErrorRecord $_ -FunctionName New-DbaDacPackage
                    return
                }
            }
        }

        # Map version string to SqlServerVersion enum
        $sqlVersionEnum = [Microsoft.SqlServer.Dac.Model.SqlServerVersion]::$SqlServerVersion

        Write-Message -Level Verbose -Message "Creating TSqlModel with target version: $SqlServerVersion" -FunctionName New-DbaDacPackage

        # Create TSqlModel - handle different DacFx versions
        try {
            # First try with TSqlModelOptions (newer DacFx versions)
            [Microsoft.SqlServer.Dac.Model.TSqlModelOptions]$modelOptions = New-Object Microsoft.SqlServer.Dac.Model.TSqlModelOptions
            try {
                $model = New-Object Microsoft.SqlServer.Dac.Model.TSqlModel -ArgumentList @($sqlVersionEnum, $modelOptions)
            } catch {
                # Fallback: try with $null for options (some DacFx versions require this)
                Write-Message -Level Verbose -Message "Retrying TSqlModel creation with null options" -FunctionName New-DbaDacPackage
                $model = New-Object Microsoft.SqlServer.Dac.Model.TSqlModel -ArgumentList @($sqlVersionEnum, $null)
            }
        } catch {
            Stop-Function -Message "Failed to create TSqlModel. Ensure DacFx is properly loaded." -ErrorRecord $_ -FunctionName New-DbaDacPackage
            return
        }

        # Track errors and warnings
        $buildErrors = New-Object System.Collections.ArrayList
        $buildWarnings = New-Object System.Collections.ArrayList
        $fileCount = 0
        $objectCount = 0

        # Add SQL files to model
        foreach ($sqlFile in $sqlFiles) {
            $fileCount++
            Write-Message -Level Verbose -Message "Processing file $fileCount of $($sqlFiles.Count): $($sqlFile.Name)" -FunctionName New-DbaDacPackage

            try {
                $sqlContent = Get-Content -Path $sqlFile.FullName -Raw -ErrorAction Stop

                if ([string]::IsNullOrWhiteSpace($sqlContent)) {
                    Write-Message -Level Warning -Message "Skipping empty file: $($sqlFile.FullName)" -FunctionName New-DbaDacPackage
                    continue
                }

                # Add the SQL script to the model
                $model.AddObjects($sqlContent)

            } catch {
                $errorMessage = "Error processing file $($sqlFile.FullName): $($_.Exception.Message)"
                $null = $buildErrors.Add($errorMessage)
                Write-Message -Level Warning -Message $errorMessage -FunctionName New-DbaDacPackage
            }
        }

        # Validate the model
        Write-Message -Level Verbose -Message "Validating model..." -FunctionName New-DbaDacPackage

        try {
            $validationMessages = $model.Validate()

            foreach ($msg in $validationMessages) {
                $msgText = "$($msg.MessageType): $($msg.Message)"

                # DacMessageType is in Microsoft.SqlServer.Dac namespace, not Microsoft.SqlServer.Dac.Model
                if ($msg.MessageType -eq [Microsoft.SqlServer.Dac.DacMessageType]::Error) {
                    $null = $buildErrors.Add($msgText)
                    Write-Message -Level Warning -Message $msgText -FunctionName New-DbaDacPackage
                } else {
                    $null = $buildWarnings.Add($msgText)
                    Write-Message -Level Verbose -Message $msgText -FunctionName New-DbaDacPackage
                }
            }
        } catch {
            $errorMessage = "Model validation failed: $($_.Exception.Message)"
            $null = $buildErrors.Add($errorMessage)
            Write-Message -Level Warning -Message $errorMessage -FunctionName New-DbaDacPackage
        }

        # Count objects in model
        try {
            $allObjects = $model.GetObjects([Microsoft.SqlServer.Dac.Model.DacQueryScopes]::UserDefined)
            $objectCount = ($allObjects | Measure-Object).Count
            Write-Message -Level Verbose -Message "Model contains $objectCount user-defined objects" -FunctionName New-DbaDacPackage
        } catch {
            Write-Message -Level Warning -Message "Could not count model objects: $($_.Exception.Message)" -FunctionName New-DbaDacPackage
        }

        # Build the DACPAC
        if ($buildErrors.Count -gt 0) {
            $resultsTime.Stop()

            # Return result with errors but don't build
            $result = [PSCustomObject]@{
                DacpacPath   = $null
                DatabaseName = $DatabaseName
                Version      = $DacVersion.ToString()
                FileCount    = $fileCount
                ObjectCount  = $objectCount
                Duration     = [prettytimespan]($resultsTime.Elapsed)
                Success      = $false
                Errors       = $buildErrors.ToArray()
                Warnings     = $buildWarnings.ToArray()
            }

            Stop-Function -Message "Build failed with $($buildErrors.Count) error(s). Use -Verbose for details." -FunctionName New-DbaDacPackage
            return $result
        }

        if ($__realCmdlet.ShouldProcess($OutputPath, "Create DACPAC from $fileCount SQL files")) {
            try {
                # Create package metadata
                $packageMetadata = New-Object Microsoft.SqlServer.Dac.PackageMetadata
                $packageMetadata.Name = $DatabaseName
                $packageMetadata.Version = $DacVersion

                if ($DacDescription) {
                    $packageMetadata.Description = $DacDescription
                }

                # Create package options
                $packageOptions = New-Object Microsoft.SqlServer.Dac.PackageOptions

                # Build the DACPAC
                Write-Message -Level Verbose -Message "Creating DACPAC at $OutputPath" -FunctionName New-DbaDacPackage

                [Microsoft.SqlServer.Dac.DacPackageExtensions]::BuildPackage($OutputPath, $model, $packageMetadata, $packageOptions)

                Write-Message -Level Output -Message "Successfully created DACPAC: $OutputPath" -FunctionName New-DbaDacPackage

            } catch {
                $errorMessage = "Failed to create DACPAC: $($_.Exception.Message)"
                $null = $buildErrors.Add($errorMessage)
                Stop-Function -Message $errorMessage -ErrorRecord $_ -FunctionName New-DbaDacPackage
                return
            }
        }

        $resultsTime.Stop()

        # Return result object (pipeline-friendly for Publish-DbaDacPackage)
        [PSCustomObject]@{
            ComputerName = $env:COMPUTERNAME
            Path         = $OutputPath
            Database     = $DatabaseName
            DatabaseName = $DatabaseName
            Version      = $DacVersion.ToString()
            FileCount    = $fileCount
            ObjectCount  = $objectCount
            Duration     = [prettytimespan]($resultsTime.Elapsed)
            Success      = $true
            Errors       = $buildErrors.ToArray()
            Warnings     = $buildWarnings.ToArray()
        } | Select-DefaultView -Property Path, DatabaseName, Version, FileCount, ObjectCount, Duration, Success
    }
} $Path $OutputPath $DacVersion $DacDescription $DatabaseName $SqlServerVersion $Filter $Recursive $EnableException $__beginState $__realCmdlet $__boundWhatIf $__boundConfirm $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}