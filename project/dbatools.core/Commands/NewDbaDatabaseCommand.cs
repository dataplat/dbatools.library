#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Creates databases with optional advanced file-layout configuration. Port of
/// public/New-DbaDatabase.ps1 (W3-066). Begin: the advancedconfig flag is a pure function of
/// sixteen bound parameters (TestBound any-of) computed C#-side; when set, a begin HOP emits
/// the once-per-invocation "Advanced data file configuration will be invoked" Verbose. The
/// process body rides one VERBATIM module hop per record inside a DOT-SOURCED inner block
/// (W1-108: the secondary-file do/while catch fires Stop-Function WITHOUT -Continue then
/// `return` - an early exit that ALSO latches Test-FunctionInterrupt, so the sentinel carries
/// an interrupted flag feeding the C#-side latch exactly like the Move-family begin latch).
/// The __w3066State sentinel carries the SIX size parameters the source mutates WITHOUT a
/// Test-Bound re-gate (PrimaryFilesize/PrimaryFileMaxSize/LogSize/LogMaxSize/
/// SecondaryFilesize/SecondaryFileMaxSize - later instances compare model-db sizes against
/// the MUTATED values) plus the stale-able locals. QUIRKS PRESERVED VERBATIM: unbound -Name
/// re-randomizes PER INSTANCE (Test-Bound reads bound state, not the variable); the do/while
/// $bail flag is dead (the return exits first); the SQL 2000 advancedconfig abort; Azure
/// https path separators; $PSCmdlet/$Pscmdlet casing. ShouldProcess routes to the REAL
/// cmdlet (no ConfirmPreference override; ConfirmImpact Low mirrored). Bind-time casts:
/// [PsStringCast] on the ValidateSet RecoveryModel and DefaultFileGroup (W1-032). Private
/// Add-TeppCacheItem and nested Get-DbaAvailableCollation/Get-DbaDefaultPath/Test-DbaPath/
/// New-DbaDirectory/Get-DbaDatabase ride the hop. NO WarningAction carrier (codex W3-005 r3:
/// host replay owns every value). Surface pinned by migration/baselines/New-DbaDatabase.json.
/// </summary>
[Cmdlet(VerbsCommon.New, "DbaDatabase", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low)]
public sealed class NewDbaDatabaseCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    public DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The database name(s); a random name is generated when omitted.</summary>
    [Parameter]
    [Alias("Database")]
    public string[]? Name { get; set; }

    /// <summary>The collation for the new database; instance default when omitted.</summary>
    [Parameter]
    public string? Collation { get; set; }

    /// <summary>The recovery model; model-database default when omitted.</summary>
    [Parameter]
    [PsStringCast]
    [ValidateSet("Simple", "Full", "BulkLogged")]
    public string? RecoveryModel { get; set; }

    /// <summary>The database owner login set after creation.</summary>
    [Parameter]
    public string? Owner { get; set; }

    /// <summary>Data file directory (or Azure URL); instance default data path when omitted.</summary>
    [Parameter]
    public string? DataFilePath { get; set; }

    /// <summary>Log file directory (or Azure URL); instance default log path when omitted.</summary>
    [Parameter]
    public string? LogFilePath { get; set; }

    /// <summary>Primary file size in MB; model-derived when omitted.</summary>
    [Parameter]
    public int PrimaryFilesize { get; set; }

    /// <summary>Primary file growth in MB.</summary>
    [Parameter]
    public int PrimaryFileGrowth { get; set; }

    /// <summary>Primary file max size in MB.</summary>
    [Parameter]
    public int PrimaryFileMaxSize { get; set; }

    /// <summary>Log file size in MB.</summary>
    [Parameter]
    public int LogSize { get; set; }

    /// <summary>Log file growth in MB.</summary>
    [Parameter]
    public int LogGrowth { get; set; }

    /// <summary>Log file max size in MB.</summary>
    [Parameter]
    public int LogMaxSize { get; set; }

    /// <summary>Secondary file size in MB.</summary>
    [Parameter]
    public int SecondaryFilesize { get; set; }

    /// <summary>Secondary file growth in MB.</summary>
    [Parameter]
    public int SecondaryFileGrowth { get; set; }

    /// <summary>Secondary file max size in MB.</summary>
    [Parameter]
    public int SecondaryFileMaxSize { get; set; }

    /// <summary>Number of secondary data files.</summary>
    [Parameter]
    public int SecondaryFileCount { get; set; }

    /// <summary>Which filegroup becomes the default (Primary or Secondary).</summary>
    [Parameter]
    [PsStringCast]
    [ValidateSet("Primary", "Secondary")]
    public string? DefaultFileGroup { get; set; }

    /// <summary>Suffix appended to the primary data file name.</summary>
    [Parameter]
    public string? DataFileSuffix { get; set; }

    /// <summary>Suffix appended to the log file name; defaults to _log.</summary>
    [Parameter]
    public string LogFileSuffix { get; set; } = "_log";

    /// <summary>Suffix appended to the secondary filegroup and file names.</summary>
    [Parameter]
    public string? SecondaryDataFileSuffix { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // The source's Stop-Function-without-Continue latch inside the secondary-file catch
    // (Test-FunctionInterrupt): the sentinel reports it, subsequent records early-return.
    private bool _hopInterrupted;

    // PS begin: $advancedconfig is a pure function of the sixteen bound parameters.
    private bool _advancedConfig;

    // Fn-scope state persisting across records: the six size params the source mutates
    // WITHOUT Test-Bound re-gates, plus stale-able locals (the bag rides the hop).
    private Hashtable? _state;

    protected override void BeginProcessing()
    {
        // PS begin: if (Test-Bound -ParameterName <16 names>) { $advancedconfig = $true;
        // Write-Message "Advanced data file configuration will be invoked" -Level Verbose }
        if (TestBound("DataFilePath", "DefaultFileGroup", "LogFilePath", "LogGrowth", "LogMaxSize",
            "LogSize", "PrimaryFileGrowth", "PrimaryFileMaxSize", "PrimaryFilesize",
            "SecondaryFileCount", "SecondaryFileGrowth", "SecondaryFileMaxSize", "SecondaryFilesize",
            "DataFileSuffix", "LogFileSuffix", "SecondaryDataFileSuffix"))
        {
            _advancedConfig = true;
            foreach (PSObject? item in NestedCommand.InvokeScoped(this, BeginScript,
                EnableException.ToBool(),
                BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
            {
                if (item?.BaseObject is ErrorRecord nestedError)
                {
                    RemoveHopErrorBookkeeping(nestedError);
                    WriteError(nestedError);
                }
                else
                {
                    WriteObject(item);
                }
            }
        }
    }

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        // PS: if (Test-FunctionInterrupt) { return } - latched by the secondary-file
        // catch's Stop-Function-without-Continue on an earlier record.
        if (_hopInterrupted)
            return;

        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            Hashtable? sentinel = item?.BaseObject as Hashtable;
            if (sentinel is not null && sentinel.ContainsKey("__w3066State"))
            {
                _state = sentinel["__w3066State"] as Hashtable;
                if (_state is not null && LanguagePrimitives.IsTrue(_state["interrupted"]))
                {
                    _hopInterrupted = true;
                }
                return;
            }
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
                return;
            }
            WriteObject(item);
        }, ProcessScript,
            SqlInstance, SqlCredential, Name, Collation, RecoveryModel, Owner ?? "",
            DataFilePath, LogFilePath, PrimaryFilesize, PrimaryFileGrowth, PrimaryFileMaxSize,
            LogSize, LogGrowth, LogMaxSize, SecondaryFilesize, SecondaryFileGrowth,
            SecondaryFileMaxSize, SecondaryFileCount, DefaultFileGroup, DataFileSuffix ?? "",
            LogFileSuffix, SecondaryDataFileSuffix ?? "", EnableException.ToBool(),
            _advancedConfig, _state,
            TestBound(nameof(Name)), TestBound(nameof(DataFilePath)), TestBound(nameof(LogFilePath)),
            TestBound(nameof(PrimaryFilesize)), TestBound(nameof(PrimaryFileGrowth)),
            TestBound(nameof(PrimaryFileMaxSize)), TestBound(nameof(LogSize)),
            TestBound(nameof(LogGrowth)), TestBound(nameof(LogMaxSize)),
            TestBound(nameof(SecondaryFilesize)), TestBound(nameof(SecondaryFileGrowth)),
            TestBound(nameof(SecondaryFileMaxSize)), TestBound(nameof(SecondaryFileCount)),
            this,
            BoundCommonParameter("WhatIf"), BoundCommonParameter("Confirm"),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug"));
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

    // PS: the begin-block Verbose, emitted once per invocation when advancedconfig is set.
    private const string BeginScript = """
param($EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param($EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    Write-Message -Message "Advanced data file configuration will be invoked" -Level Verbose -FunctionName New-DbaDatabase
} $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";

    // PS: the ENTIRE process body VERBATIM inside a dot-sourced inner block (W1-108: the
    // secondary-file catch's Stop-Function-without-Continue + return exits the block while
    // the trailing state sentinel still emits, carrying the Test-FunctionInterrupt latch).
    // Substitutions only: Test-Bound X -> carried $__boundX flags, $advancedconfig ->
    // carried flag, $Pscmdlet/$PSCmdlet -> $__realCmdlet, and explicit -FunctionName
    // New-DbaDatabase on Stop-Function/Write-Message (W1-090).
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Name, $Collation, $RecoveryModel, $Owner, $DataFilePath, $LogFilePath, $PrimaryFilesize, $PrimaryFileGrowth, $PrimaryFileMaxSize, $LogSize, $LogGrowth, $LogMaxSize, $SecondaryFilesize, $SecondaryFileGrowth, $SecondaryFileMaxSize, $SecondaryFileCount, $DefaultFileGroup, $DataFileSuffix, $LogFileSuffix, $SecondaryDataFileSuffix, $EnableException, $__advancedconfig, $__state, $__boundName, $__boundDataFilePath, $__boundLogFilePath, $__boundPrimaryFilesize, $__boundPrimaryFileGrowth, $__boundPrimaryFileMaxSize, $__boundLogSize, $__boundLogGrowth, $__boundLogMaxSize, $__boundSecondaryFilesize, $__boundSecondaryFileGrowth, $__boundSecondaryFileMaxSize, $__boundSecondaryFileCount, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = "Low")]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [string[]]$Name, [string]$Collation, [string]$RecoveryModel, [string]$Owner, [string]$DataFilePath, [string]$LogFilePath, [int32]$PrimaryFilesize, [int32]$PrimaryFileGrowth, [int32]$PrimaryFileMaxSize, [int32]$LogSize, [int32]$LogGrowth, [int32]$LogMaxSize, [int32]$SecondaryFilesize, [int32]$SecondaryFileGrowth, [int32]$SecondaryFileMaxSize, [int32]$SecondaryFileCount, [string]$DefaultFileGroup, [string]$DataFileSuffix, [string]$LogFileSuffix, [string]$SecondaryDataFileSuffix, $EnableException, $__advancedconfig, $__state, $__boundName, $__boundDataFilePath, $__boundLogFilePath, $__boundPrimaryFilesize, $__boundPrimaryFileGrowth, $__boundPrimaryFileMaxSize, $__boundLogSize, $__boundLogGrowth, $__boundLogMaxSize, $__boundSecondaryFilesize, $__boundSecondaryFileGrowth, $__boundSecondaryFileMaxSize, $__boundSecondaryFileCount, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    $advancedconfig = $__advancedconfig

    # restore fn-scope state mutated by earlier records (size params mutate WITHOUT a
    # Test-Bound re-gate, so later instances compare against the mutated values)
    if ($null -ne $__state) {
        $PrimaryFilesize = $__state.PrimaryFilesize
        $PrimaryFileMaxSize = $__state.PrimaryFileMaxSize
        $LogSize = $__state.LogSize
        $LogMaxSize = $__state.LogMaxSize
        $SecondaryFilesize = $__state.SecondaryFilesize
        $SecondaryFileMaxSize = $__state.SecondaryFileMaxSize
        $Name = $__state.Name
        $DataFilePath = $__state.DataFilePath
        $LogFilePath = $__state.LogFilePath
    }

    . {
        foreach ($instance in $SqlInstance) {
            try {
                $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
            } catch {
                Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName New-DbaDatabase
            }

            if ($advancedconfig -and $server.VersionMajor -eq 8) {
                Stop-Function -Message "Advanced configuration options are not available to SQL Server 2000. Aborting creation of database on $instance" -Target $instance -Continue -FunctionName New-DbaDatabase
            }

            # validate the collation
            if ($Collation) {
                $collations = Get-DbaAvailableCollation -SqlInstance $server

                if ($collations.Name -notcontains $Collation) {
                    Stop-Function -Message "$Collation is not a valid collation on $instance" -Target $instance -Continue -FunctionName New-DbaDatabase
                }
            }

            if (-not ($__boundName)) {
                $Name = "random-$(Get-Random)"
            }

            if (-not ($__boundDataFilePath)) {
                $DataFilePath = (Get-DbaDefaultPath -SqlInstance $server).Data
            }

            if (-not ($__boundLogFilePath)) {
                $LogFilePath = (Get-DbaDefaultPath -SqlInstance $server).Log
            }

            # Detect Azure Blob Storage URLs to skip filesystem directory operations
            $dataPathIsAzure = $DataFilePath -like "https://*"
            $logPathIsAzure = $LogFilePath -like "https://*"

            $dataFileDirectoryPath = $DataFilePath
            $logFileDirectoryPath = $LogFilePath

            # Trim trailing separators to avoid double-separators when concatenating file names
            $dataFileNamePath = $DataFilePath.TrimEnd("\", "/")
            $logFileNamePath = $LogFilePath.TrimEnd("\", "/")

            # Choose the path separator based on whether the path is an Azure Blob Storage URL
            $dataPathSeparator = if ($dataPathIsAzure) { "/" } else { "\" }
            $logPathSeparator = if ($logPathIsAzure) { "/" } else { "\" }

            if (-not $logPathIsAzure -and -not (Test-DbaPath -SqlInstance $server -Path $logFileDirectoryPath)) {
                try {
                    Write-Message -Message "Creating directory $logFileDirectoryPath" -Level Verbose -FunctionName New-DbaDatabase
                    $null = New-DbaDirectory -SqlInstance $server -Path $logFileDirectoryPath -EnableException
                } catch {
                    Stop-Function -Message "Error creating log file directory $logFileDirectoryPath" -Target $instance -Continue -FunctionName New-DbaDatabase
                }
            }

            if (-not $dataPathIsAzure -and -not (Test-DbaPath -SqlInstance $server -Path $dataFileDirectoryPath)) {
                try {
                    Write-Message -Message "Creating directory $dataFileDirectoryPath" -Level Verbose -FunctionName New-DbaDatabase
                    $null = New-DbaDirectory -SqlInstance $server -Path $dataFileDirectoryPath -EnableException
                } catch {
                    Stop-Function -Message "Error creating secondary file directory $dataFileDirectoryPath on $instance" -Target $instance -Continue -FunctionName New-DbaDatabase
                }
            }

            Write-Message -Message "Set local data path to $dataFileDirectoryPath and local log path to $logFileDirectoryPath" -Level Verbose -FunctionName New-DbaDatabase

            foreach ($dbName in $Name) {
                if ($server.Databases[$dbName].Name) {
                    Stop-Function -Message "Database $dbName already exists on $instance" -Target $instance -Continue -FunctionName New-DbaDatabase
                }

                try {
                    Write-Message -Message "Creating smo object for new database $dbName" -Level Verbose -FunctionName New-DbaDatabase
                    $newdb = New-Object Microsoft.SqlServer.Management.Smo.Database($server, $dbName)
                } catch {
                    Stop-Function -Message "Error creating database object for $dbName on server $server" -ErrorRecord $_ -Target $instance -Continue -FunctionName New-DbaDatabase
                }

                if ($Collation) {
                    Write-Message -Message "Setting collation to $Collation" -Level Verbose -FunctionName New-DbaDatabase
                    $newdb.Collation = $Collation
                }

                if ($RecoveryModel) {
                    Write-Message -Message "Setting recovery model to $RecoveryModel" -Level Verbose -FunctionName New-DbaDatabase
                    $newdb.RecoveryModel = $RecoveryModel
                }

                if ($advancedconfig) {
                    try {
                        Write-Message -Message "Creating PRIMARY filegroup" -Level Verbose -FunctionName New-DbaDatabase
                        $primaryfg = New-Object Microsoft.SqlServer.Management.Smo.Filegroup($newdb, "PRIMARY")
                        $newdb.Filegroups.Add($primaryfg)
                    } catch {
                        Stop-Function -Message "Error creating Primary filegroup object" -ErrorRecord $_ -Target $instance -Continue -FunctionName New-DbaDatabase
                    }

                    #add the primary file
                    try {
                        $primaryfilename = $dbName + $DataFileSuffix
                        Write-Message -Message "Creating file name $primaryfilename in filegroup PRIMARY" -Level Verbose -FunctionName New-DbaDatabase

                        # if PrimaryFilesize and PrimaryFileMaxSize were passed in then check the size of the modeldev file; if larger than our $PrimaryFilesize setting use that instead
                        if ($server.Databases["model"].FileGroups["PRIMARY"].Files["modeldev"].Size -gt ($PrimaryFilesize * 1024)) {
                            Write-Message -Message "model database modeldev larger than our the PrimaryFilesize so using modeldev size for Primary file" -Level Verbose -FunctionName New-DbaDatabase
                            $PrimaryFilesize = ($server.Databases["model"].FileGroups["PRIMARY"].Files["modeldev"].Size / 1024)
                            if ($PrimaryFilesize -gt $PrimaryFileMaxSize) {
                                Write-Message -Message "Resetting Primary File Max size to be the new Primary File Size setting" -Level Verbose -FunctionName New-DbaDatabase
                                $PrimaryFileMaxSize = $PrimaryFilesize
                            }
                        }

                        #create the primary file
                        $primaryfile = New-Object Microsoft.SqlServer.Management.Smo.DataFile($primaryfg, $primaryfilename)
                        $primaryfile.FileName = $dataFileNamePath + $dataPathSeparator + $primaryfilename + ".mdf"
                        $primaryfile.IsPrimaryFile = $true

                        if ($__boundPrimaryFilesize) {
                            $primaryfile.Size = ($PrimaryFilesize * 1024)
                        } else {
                            $primaryfile.Size = $server.Databases["model"].FileGroups["PRIMARY"].Files["modeldev"].Size
                        }
                        if ($__boundPrimaryFileGrowth) {
                            $primaryfile.Growth = ($PrimaryFileGrowth * 1024)
                            $primaryfile.GrowthType = "KB"
                        } else {
                            $primaryfile.Growth = $server.Databases["model"].FileGroups["PRIMARY"].Files["modeldev"].Growth
                            $primaryfile.GrowthType = $server.Databases["model"].FileGroups["PRIMARY"].Files["modeldev"].GrowthType
                        }
                        if ($__boundPrimaryFileMaxSize) {
                            $primaryfile.MaxSize = ($PrimaryFileMaxSize * 1024)
                        } else {
                            $primaryfile.MaxSize = $server.Databases["model"].FileGroups["PRIMARY"].Files["modeldev"].MaxSize
                        }

                        #add the file to the filegroup
                        $primaryfg.Files.Add($primaryfile)
                    } catch {
                        Stop-Function -Message "Error adding file to Primary filegroup" -ErrorRecord $_ -Target $instance -Continue -FunctionName New-DbaDatabase
                    }

                    try {
                        $logname = $dbName + $LogFileSuffix
                        Write-Message -Message "Creating log $logname" -Level Verbose -FunctionName New-DbaDatabase

                        # if LogSize and LogMaxSize were passed in then check the size of the modellog file; if larger than our $LogSize setting use that instead
                        if ($server.Databases["model"].LogFiles["modellog"].Size -gt ($LogSize * 1024)) {
                            Write-Message -Message "model database modellog larger than our the LogSize so using modellog size for Log file size" -Level Verbose -FunctionName New-DbaDatabase
                            $LogSize = ($server.Databases["model"].LogFiles["modellog"].Size / 1024)
                            if ($LogSize -gt $LogMaxSize) {
                                Write-Message -Message "Resetting Log File Max size to be the new Log File Size setting" -Level Verbose -FunctionName New-DbaDatabase
                                $LogMaxSize = $LogSize
                            }
                        }

                        $tlog = New-Object Microsoft.SqlServer.Management.Smo.LogFile($newdb, $logname)
                        $tlog.FileName = $logFileNamePath + $logPathSeparator + $logname + ".ldf"

                        if ($__boundLogSize) {
                            $tlog.Size = ($LogSize * 1024)
                        } else {
                            $tlog.Size = $server.Databases["model"].LogFiles["modellog"].Size
                        }
                        if ($__boundLogGrowth) {
                            $tlog.Growth = ($LogGrowth * 1024)
                            $tlog.GrowthType = "KB"
                        } else {
                            $tlog.Growth = $server.Databases["model"].LogFiles["modellog"].Growth
                            $tlog.GrowthType = $server.Databases["model"].LogFiles["modellog"].GrowthType
                        }
                        if ($__boundLogMaxSize) {
                            $tlog.MaxSize = ($LogMaxSize * 1024)
                        } else {
                            $tlog.MaxSize = $server.Databases["model"].LogFiles["modellog"].MaxSize
                        }

                        #add the log to the db
                        $newdb.LogFiles.Add($tlog)
                    } catch {
                        Stop-Function -Message "Error adding log file to database." -ErrorRecord $_ -Target $instance -Continue -FunctionName New-DbaDatabase
                    }

                    if ($DefaultFileGroup -eq "Secondary" -or ($__boundSecondaryFileMaxSize -or $__boundSecondaryFileGrowth -or $__boundSecondaryFilesize -or $__boundSecondaryFileCount)) {
                        #add the Secondary data file group
                        try {
                            $secondaryfilegroupname = $dbName + $SecondaryDataFileSuffix
                            Write-Message -Message "Creating Secondary filegroup $secondaryfilegroupname" -Level Verbose -FunctionName New-DbaDatabase

                            $secondaryfg = New-Object Microsoft.SqlServer.Management.Smo.Filegroup($newdb, $secondaryfilegroupname)
                            $newdb.Filegroups.Add($secondaryfg)
                        } catch {
                            Stop-Function -Message "Error creating Secondary filegroup" -ErrorRecord $_ -Target $instance -Continue -FunctionName New-DbaDatabase
                        }

                        # if SecondaryFilesize and SecondaryFileMaxSize were passed in then check the size of the modeldev file; if larger than our $SecondaryFilesize setting use that instead
                        if ($server.Databases["model"].FileGroups["PRIMARY"].Files["modeldev"].Size -gt ($SecondaryFilesize * 1024)) {
                            Write-Message -Message "model database modeldev larger than our the SecondaryFilesize so using modeldev size for the Secondary file" -Level Verbose -FunctionName New-DbaDatabase
                            $SecondaryFilesize = ($server.Databases["model"].FileGroups["PRIMARY"].Files["modeldev"].Size / 1024)
                            if ($SecondaryFilesize -gt $SecondaryFileMaxSize) {
                                Write-Message -Message "Resetting Secondary File Max size to be the new Secondary File Size setting" -Level Verbose -FunctionName New-DbaDatabase
                                $SecondaryFileMaxSize = $SecondaryFilesize
                            }
                        }

                        # add the required number of files to the filegroup in a loop
                        $secondaryfgcount = $bail = 0

                        # open a loop while the filecounter is less than the required number of files
                        do {
                            $secondaryfgcount++
                            try {
                                $secondaryfilename = "$($secondaryfilegroupname)_$($secondaryfgcount)"
                                Write-Message -Message "Creating file name $secondaryfilename in filegroup $secondaryfilegroupname" -Level Verbose -FunctionName New-DbaDatabase
                                $secondaryfile = New-Object Microsoft.SQLServer.Management.Smo.Datafile($secondaryfg, $secondaryfilename)
                                $secondaryfile.FileName = $dataFileNamePath + $dataPathSeparator + $secondaryfilename + ".ndf"

                                if ($__boundSecondaryFilesize) {
                                    $secondaryfile.Size = ($SecondaryFilesize * 1024)
                                } else {
                                    $secondaryfile.Size = $server.Databases["model"].FileGroups["PRIMARY"].Files["modeldev"].Size
                                }
                                if ($__boundSecondaryFileGrowth) {
                                    $secondaryfile.Growth = ($SecondaryFileGrowth * 1024)
                                    $secondaryfile.GrowthType = "KB"
                                } else {
                                    $secondaryfile.Growth = $server.Databases["model"].FileGroups["PRIMARY"].Files["modeldev"].Growth
                                    $secondaryfile.GrowthType = $server.Databases["model"].FileGroups["PRIMARY"].Files["modeldev"].GrowthType
                                }
                                if ($__boundSecondaryFileMaxSize) {
                                    $secondaryfile.MaxSize = ($SecondaryFileMaxSize * 1024)
                                } else {
                                    $secondaryfile.MaxSize = $server.Databases["model"].FileGroups["PRIMARY"].Files["modeldev"].MaxSize
                                }

                                $secondaryfg.Files.Add($secondaryfile)
                            } catch {
                                $bail = $true
                                Stop-Function -Message "Error adding file $secondaryfg to $secondaryfilegroupname" -ErrorRecord $_ -Target $instance -FunctionName New-DbaDatabase
                                return
                            }
                        } while ($secondaryfgcount -lt $SecondaryFileCount -or $bail)
                    }
                }

                Write-Message -Message "Creating Database $dbName" -Level Verbose -FunctionName New-DbaDatabase
                if ($__realCmdlet.ShouldProcess($instance, "Creating the database $dbName on instance $instance")) {
                    try {
                        $newdb.Create()
                    } catch {
                        Stop-Function -Message "Error creating Database $dbName on server $instance" -ErrorRecord $_ -Target $instance -Continue -FunctionName New-DbaDatabase
                    }

                    if ($Owner) {
                        Write-Message -Message "Setting database owner to $Owner" -Level Verbose -FunctionName New-DbaDatabase
                        try {
                            $newdb.SetOwner($Owner)
                            $newdb.Refresh()
                        } catch {
                            Stop-Function -Message "Error setting Database Owner to $Owner" -ErrorRecord $_ -Target $instance -Continue -FunctionName New-DbaDatabase
                        }
                    }

                    if ($DefaultFileGroup -eq "Secondary") {
                        Write-Message -Message "Setting default filegroup to $secondaryfilegroupname" -Level Verbose -FunctionName New-DbaDatabase
                        try {
                            $newdb.SetDefaultFileGroup($secondaryfilegroupname)
                        } catch {
                            Stop-Function -Message "Error setting default filegroup to $secondaryfilegroupname" -ErrorRecord $_ -Target $instance -Continue -FunctionName New-DbaDatabase
                        }
                    }

                    Add-TeppCacheItem -SqlInstance $server -Type database -Name $dbName
                    Get-DbaDatabase -SqlInstance $server -Database $dbName
                }
            }
        }
    }
    @{ __w3066State = @{ PrimaryFilesize = $PrimaryFilesize; PrimaryFileMaxSize = $PrimaryFileMaxSize; LogSize = $LogSize; LogMaxSize = $LogMaxSize; SecondaryFilesize = $SecondaryFilesize; SecondaryFileMaxSize = $SecondaryFileMaxSize; Name = $Name; DataFilePath = $DataFilePath; LogFilePath = $LogFilePath; interrupted = [bool](Get-Variable -Name "__dbatools_interrupt_function_78Q9VPrM6999g6zo24Qn83m09XF56InEn4hFrA8Fwhu5xJrs6r" -ErrorAction Ignore -ValueOnly) } }
} $SqlInstance $SqlCredential $Name $Collation $RecoveryModel $Owner $DataFilePath $LogFilePath $PrimaryFilesize $PrimaryFileGrowth $PrimaryFileMaxSize $LogSize $LogGrowth $LogMaxSize $SecondaryFilesize $SecondaryFileGrowth $SecondaryFileMaxSize $SecondaryFileCount $DefaultFileGroup $DataFileSuffix $LogFileSuffix $SecondaryDataFileSuffix $EnableException $__advancedconfig $__state $__boundName $__boundDataFilePath $__boundLogFilePath $__boundPrimaryFilesize $__boundPrimaryFileGrowth $__boundPrimaryFileMaxSize $__boundLogSize $__boundLogGrowth $__boundLogMaxSize $__boundSecondaryFilesize $__boundSecondaryFileGrowth $__boundSecondaryFileMaxSize $__boundSecondaryFileCount $__realCmdlet $__boundWhatIf $__boundConfirm $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
