#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Masks data in database tables from a masking configuration file. Port of
/// public/Invoke-DbaDbDataMasking.ps1; the workflow remains a module-scoped PowerShell
/// compatibility hop.
///
/// This is a BEGIN+PROCESS command shipped as TWO hops. FilePath is ValueFromPipeline, so
/// process fires once per piped configuration file. The begin block runs once: it resolves the
/// supported randomizer type/subtype lists (two Get-DbaRandomizedType calls) and applies the
/// falsy-int defaults (ModulusFactor 10, CommandTimeout 300, BatchSize 1000, Retry 1000, each
/// with its Verbose message). Folding begin into every record would re-run those calls and
/// re-emit those messages per record, so the split is required for parity. The begin results
/// carry to process in an OPAQUE hashtable the C# side never interprets (container cast only -
/// values built by PS functions arrive PSObject-wrapped, and a C# "as" cast on a wrapped value
/// silently nulls it; the Compare-DbaDbSchema carry defect class).
///
/// The process hop STREAMS (InvokeScopedStreaming, not buffered InvokeScoped): this command
/// mass-mutates table data and emits one result object per table - the audit trail of work
/// already done. Buffered invocation would discard those records when a later table's failure
/// terminates the hop under -EnableException (the DEF-001 class).
///
/// INTERRUPT CARRY. The source's process opens with "if (Test-FunctionInterrupt) { return }":
/// a prior record's direct Stop-Function halts later records. Across separate hop invocations
/// the module interrupt flag does not survive, so each hop reads it at Get-Variable -Scope 0
/// after the dot-sourced body and carries it; C# skips the hop when a prior record carried
/// true. Only DIRECT body Stop-Functions set it - one buried in a nested helper does not,
/// exactly as the function-scope Test-FunctionInterrupt behaved. Several body Stop-Functions
/// have neither -Continue nor a following return; the current record keeps executing after
/// them and only the NEXT record short-circuits - preserved verbatim.
///
/// CROSS-RECORD STATE. These function-scope locals are branch-assigned and read on paths that
/// can run before any assignment in the current record, so in the function world they carried
/// values across pipeline records: identityColumn (only assigned on the non-WhatIf branch;
/// read under -WhatIf), convertedValue and lookupResult and maskingErrorFlag (read in the
/// unique-index section before the standard-column section assigns them), charstring/min/max
/// and columnobject (read in the composites/unique-index sections from a previous loop's
/// iteration), and dictionaryFileName (Replace'd before assignment on the non-Windows path);
/// plus Database (when unbound, record one derives it from its config file and later records
/// must keep that value, not re-derive from their own) and insertValue (its conversion catches
/// lack -Continue, so a failure falls through to a read of the prior iteration's value).
/// They ride a state sentinel with per-name Assigned flags, restored at the next hop's top,
/// so unset-vs-assigned semantics survive (an unset read walks the scope chain as the
/// function's did). The source also reads variables it NEVER assigns - $Force (begin),
/// $uniqueIndex, $columnValue, $faker - which ride verbatim as unset reads.
///
/// The begin block's "if ($Force) { $ConfirmPreference = 'none' }" effect is resolved ONCE in
/// the begin hop ($Force is undeclared and dynamic) and carried as a flag the process hop
/// applies to its own $ConfirmPreference, so a mid-pipeline change to an upstream $Force
/// cannot alter confirmation behavior between records - the function's run-once begin exactly.
///
/// ATTRIBUTION SHIM (the Get-PSCallStack class): Write-ProgressHelper derives its caller name
/// from (Get-PSCallStack)[1].Command; a bare hop call would read the generated scriptblock
/// frame. Both call sites route through a thin named wrapper function Invoke-DbaDbDataMasking
/// inside the hop, so the helper sees the real command name. The helper has no Stop-Function
/// latch, so the wrapper needs no dot-sourcing.
///
/// Source quirks preserved bug-for-bug: the KeepNull condition's unparenthesized -and/-or
/// precedence; $convertedValue.ErrorMessage checked where the assignment went to $insertValue;
/// $columnobject read inside the unique-index loop whose iterator is $columnMaskInfo; the
/// dictionary filename Replace on the prior record's value. The one ShouldProcess gate routes
/// to the real cmdlet via $__realCmdlet (ConfirmImpact High mirrored); Test-Bound never rides
/// a hop, so its three MaxValue probes read the carried $__boundMaxValue flag. In-hop
/// Stop-Function/Write-Message carry -FunctionName. Surface pinned by
/// migration/baselines/Invoke-DbaDbDataMasking.json (implicit positions 0-16 made explicit).
/// </summary>
[Cmdlet(VerbsLifecycle.Invoke, "DbaDbDataMasking", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.High)]
public sealed class InvokeDbaDbDataMaskingCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The database(s) to mask; defaults to the database named in the config file.</summary>
    [Parameter(Position = 2)]
    [PsStringArrayCast]
    public string[]? Database { get; set; }

    /// <summary>The masking configuration file path (or URL), typically from
    /// New-DbaDbMaskingConfig; pipes from Get-ChildItem.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 3)]
    [Alias("Path", "FullName")]
    public object? FilePath { get; set; }

    /// <summary>The faker locale used for generated values.</summary>
    [Parameter(Position = 4)]
    [PsStringCast]
    public string Locale { get; set; } = "en";

    /// <summary>The character set used for random string generation.</summary>
    [Parameter(Position = 5)]
    [PsStringCast]
    public string CharacterString { get; set; } = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    /// <summary>Mask only these tables.</summary>
    [Parameter(Position = 6)]
    [PsStringArrayCast]
    public string[]? Table { get; set; }

    /// <summary>Mask only these columns.</summary>
    [Parameter(Position = 7)]
    [PsStringArrayCast]
    public string[]? Column { get; set; }

    /// <summary>Skip these tables.</summary>
    [Parameter(Position = 8)]
    [PsStringArrayCast]
    public string[]? ExcludeTable { get; set; }

    /// <summary>Skip these columns.</summary>
    [Parameter(Position = 9)]
    [PsStringArrayCast]
    public string[]? ExcludeColumn { get; set; }

    /// <summary>Cap for generated numeric/string lengths; config-file values win below the cap.</summary>
    [Parameter(Position = 10)]
    public int MaxValue { get; set; }

    /// <summary>Modulus deciding how often nullable columns receive NULL (default 10, applied
    /// in the begin hop).</summary>
    [Parameter(Position = 11)]
    public int ModulusFactor { get; set; }

    /// <summary>Query timeout in seconds (default 300, applied in the begin hop).</summary>
    [Parameter(Position = 12)]
    public int CommandTimeout { get; set; }

    /// <summary>Rows per UPDATE batch (default 1000, applied in the begin hop).</summary>
    [Parameter(Position = 13)]
    public int BatchSize { get; set; }

    /// <summary>Unique-value generation retry cap (default 1000, applied in the begin hop).</summary>
    [Parameter(Position = 14)]
    public int Retry { get; set; }

    /// <summary>CSV dictionary files of deterministic value mappings to pre-load.</summary>
    [Parameter(Position = 15)]
    [PsStringArrayCast]
    public string[]? DictionaryFilePath { get; set; }

    /// <summary>Directory to export the deterministic value dictionary to.</summary>
    [Parameter(Position = 16)]
    [PsStringCast]
    public string? DictionaryExportPath { get; set; }

    /// <summary>Declared by the source but never read in its body; kept for surface parity.</summary>
    [Parameter]
    public SwitchParameter ExactLength { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // Begin-hop results (supported type lists + defaulted ints), carried as an OPAQUE
    // hashtable: C# casts only the container, never the values (function-emitted values are
    // PSObject-wrapped and a C# "as" cast would silently null them).
    private Hashtable? _beginState;
    // The cross-record function-scope locals, carried with per-name Assigned flags.
    private Hashtable? _state;
    // A direct process Stop-Function on an earlier record halts the remaining records.
    private bool _interrupted;

    protected override void BeginProcessing()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BeginScript,
            ModulusFactor, CommandTimeout, BatchSize, Retry,
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (item?.BaseObject is Hashtable sentinel && sentinel.ContainsKey("__invokeDbaDbDataMaskingBegin"))
            {
                _beginState = sentinel["__invokeDbaDbDataMaskingBegin"] as Hashtable;
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

        // Streaming, not buffered (DEF-001): per-table result objects are the audit trail of
        // destructive updates already executed; they must reach the caller before a later
        // table or record terminates the hop under -EnableException.
        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is Hashtable sentinel && sentinel.ContainsKey("__invokeDbaDbDataMaskingProcess"))
            {
                if (sentinel["__invokeDbaDbDataMaskingProcess"] is Hashtable result)
                {
                    _state = result["State"] as Hashtable;
                    _interrupted = LanguagePrimitives.IsTrue(result["Interrupted"]);
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
            SqlInstance, SqlCredential, Database, FilePath, Locale, CharacterString, Table,
            Column, ExcludeTable, ExcludeColumn, MaxValue, DictionaryFilePath,
            DictionaryExportPath, EnableException.ToBool(), _beginState, _state, this,
            MyInvocation.BoundParameters.ContainsKey("MaxValue"),
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

    // PS: the begin block VERBATIM, dot-sourced so its assignments land in the hop scope for
    // the sentinel. Edits: -FunctionName on the four Write-Message defaults. $Force rides as
    // the source's undeclared read. The sentinel carries the computed type lists and the
    // defaulted ints as one opaque state hashtable.
    private const string BeginScript = """
param($ModulusFactor, $CommandTimeout, $BatchSize, $Retry, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([int]$ModulusFactor, [int]$CommandTimeout, [int]$BatchSize, [int]$Retry, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    . {
        if ($Force) { $ConfirmPreference = 'none' }

        $supportedDataTypes = @(
            'bit', 'bigint', 'bool',
            'char', 'date',
            'datetime', 'datetime2', 'decimal',
            'float',
            'int',
            'money',
            'nchar', 'ntext', 'nvarchar',
            'smalldatetime', 'smallint',
            'text', 'time', 'tinyint',
            'uniqueidentifier', 'userdefineddatatype',
            'varchar'
        )

        $supportedFakerMaskingTypes = Get-DbaRandomizedType | Select-Object Type -ExpandProperty Type -Unique

        $supportedFakerSubTypes = Get-DbaRandomizedType | Select-Object Subtype -ExpandProperty Subtype -Unique

        $supportedFakerSubTypes += "Date"

        # Set defaults
        if (-not $ModulusFactor) {
            $ModulusFactor = 10
            Write-Message -Level Verbose -Message "Modulus factor set to $ModulusFactor" -FunctionName Invoke-DbaDbDataMasking -ModuleName "dbatools"
        }

        if (-not $CommandTimeout) {
            $CommandTimeout = 300
            Write-Message -Level Verbose -Message "Command time-out set to $CommandTimeout" -FunctionName Invoke-DbaDbDataMasking -ModuleName "dbatools"
        }

        if (-not $BatchSize) {
            $BatchSize = 1000
            Write-Message -Level Verbose -Message "Batch size set to $BatchSize" -FunctionName Invoke-DbaDbDataMasking -ModuleName "dbatools"
        }

        if (-not $Retry) {
            $Retry = 1000
            Write-Message -Level Verbose -Message "Retry count set to $Retry" -FunctionName Invoke-DbaDbDataMasking -ModuleName "dbatools"
        }
    }

    @{ __invokeDbaDbDataMaskingBegin = @{ SupportedDataTypes = $supportedDataTypes; SupportedFakerMaskingTypes = $supportedFakerMaskingTypes; SupportedFakerSubTypes = $supportedFakerSubTypes; ModulusFactor = $ModulusFactor; CommandTimeout = $CommandTimeout; BatchSize = $BatchSize; Retry = $Retry; BeginForce = [bool]$Force } }
} $ModulusFactor $CommandTimeout $BatchSize $Retry $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
    // PS: the ENTIRE process block VERBATIM per record, dot-sourced so its early returns
    // (including "return $configErrors", which emits before exiting) leave the body while the
    // sentinel still emits. Substitutions only: three Test-Bound MaxValue probes -> the
    // carried $__boundMaxValue, $Pscmdlet -> $__realCmdlet on the one ShouldProcess gate, and
    // the two Write-ProgressHelper call sites -> the attribution wrapper. Begin results and
    // the cross-record state restore at the top; the end sentinel snapshots the interrupt and
    // the nine carried locals with per-name Assigned flags.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $FilePath, $Locale, $CharacterString, $Table, $Column, $ExcludeTable, $ExcludeColumn, $MaxValue, $DictionaryFilePath, $DictionaryExportPath, $EnableException, $__beginState, $__state, $__realCmdlet, $__boundMaxValue, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = "High")]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [string[]]$Database, $FilePath, [string]$Locale, [string]$CharacterString, [string[]]$Table, [string[]]$Column, [string[]]$ExcludeTable, [string[]]$ExcludeColumn, [int]$MaxValue, [string[]]$DictionaryFilePath, [string]$DictionaryExportPath, $EnableException, $__beginState, $__state, $__realCmdlet, $__boundMaxValue, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    # begin-hop results: the supported type lists and defaulted ints the source computed once
    $supportedDataTypes = $__beginState.SupportedDataTypes
    $supportedFakerMaskingTypes = $__beginState.SupportedFakerMaskingTypes
    $supportedFakerSubTypes = $__beginState.SupportedFakerSubTypes
    $ModulusFactor = $__beginState.ModulusFactor
    $CommandTimeout = $__beginState.CommandTimeout
    $BatchSize = $__beginState.BatchSize
    $Retry = $__beginState.Retry

    # the source's begin ConfirmPreference effect: $Force (undeclared) was resolved ONCE in the
    # begin hop and carried, so a mid-pipeline change to an upstream $Force cannot alter
    # confirmation behavior between records - exactly the function's run-once begin semantics
    if ($__beginState.BeginForce) { $ConfirmPreference = 'none' }

    # cross-record function-scope locals: restore only what an earlier record assigned
    if ($null -ne $__state) {
        foreach ($__name in "identityColumn", "convertedValue", "lookupResult", "charstring", "min", "max", "columnobject", "maskingErrorFlag", "dictionaryFileName", "Database", "insertValue") {
            if ($__state[$__name + "Assigned"]) { Set-Variable -Name $__name -Value $__state[$__name] }
        }
    }

    # ATTRIBUTION SHIM (the Get-PSCallStack class): Write-ProgressHelper reads
    # (Get-PSCallStack)[1].Command; routing both call sites through this named wrapper shows it
    # the real command name instead of the generated scriptblock frame.
    function Invoke-DbaDbDataMasking { param($__progressParams) Write-ProgressHelper @__progressParams }

    . {
        if (Test-FunctionInterrupt) {
            return
        }

        if ($FilePath.ToString().StartsWith('http')) {
            $tables = Invoke-RestMethod -Uri $FilePath
        } else {
            # Test the configuration file
            try {
                $configErrors = @()

                $configErrors += Test-DbaDbDataMaskingConfig -FilePath $FilePath -EnableException

                if ($configErrors.Count -ge 1) {
                    Stop-Function -Message "Errors found testing the configuration file." -Target $FilePath -FunctionName Invoke-DbaDbDataMasking
                    return $configErrors
                }
            } catch {
                Stop-Function -Message "Something went wrong testing the configuration file" -ErrorRecord $_ -Target $FilePath -FunctionName Invoke-DbaDbDataMasking
                return
            }

            # Get all the items that should be processed
            try {
                $tables = Get-Content -Path $FilePath -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
            } catch {
                Stop-Function -Message "Could not parse masking config file" -ErrorRecord $_ -Target $FilePath -FunctionName Invoke-DbaDbDataMasking
                return
            }
        }

        # Test the columns for data types
        foreach ($tabletest in $tables.Tables) {
            if ($Table -and $tabletest.Name -notin $Table) {
                continue
            }

            foreach ($columntest in $tabletest.Columns) {
                if ($columntest.ColumnType -in 'hierarchyid', 'geography', 'xml', 'geometry' -and $columntest.Name -notin $Column) {
                    Stop-Function -Message "$($columntest.ColumnType) is not supported, please remove the column $($columntest.Name) from the $($tabletest.Name) table" -Target $tables -Continue -FunctionName Invoke-DbaDbDataMasking
                }
            }
        }

        foreach ($instance in $SqlInstance) {
            try {
                $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential -MinimumVersion 9
            } catch {
                Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Invoke-DbaDbDataMasking
            }

            # Check if the deterministic values table is already present
            if ($server.Databases['tempdb'].Tables.Name -contains 'DeterministicValues') {
                Write-Message -Level Verbose -Message "Deterministic values table already exists. Dropping it...." -FunctionName Invoke-DbaDbDataMasking -ModuleName "dbatools"
                $query = "DROP TABLE [dbo].[DeterministicValues];"
                $server.Databases['tempdb'].Query($query)
            }

            # Create the deterministic value table
            $query = "
                CREATE TABLE dbo.DeterministicValues
                (
                    [ValueKey] VARCHAR(900),
                    [NewValue] VARCHAR(900)
                )

                CREATE UNIQUE NONCLUSTERED INDEX UNX__DeterministicValues_ValueKey
                ON dbo.DeterministicValues ( ValueKey )
            "

            $null = $server.Databases['tempdb'].Query($query)

            # Import the dictionary files
            if ($DictionaryFilePath.Count -ge 1) {
                foreach ($file in $DictionaryFilePath) {
                    Write-Message -Level Verbose -Message "Importing dictionary file '$file'" -FunctionName Invoke-DbaDbDataMasking -ModuleName "dbatools"
                    if (Test-Path -Path $file) {
                        try {
                            # Import the keys and values
                            Import-DbaCsv -Path $file -SqlInstance $server -Database tempdb -Schema dbo -Table DeterministicValues
                        } catch {
                            Stop-Function -Message "Could not import csv data from file '$file'" -ErrorRecord $_ -Target $file -FunctionName Invoke-DbaDbDataMasking
                        }
                    } else {
                        Stop-Function -Message "Could not import dictionary file '$file'" -Target $file -FunctionName Invoke-DbaDbDataMasking
                    }
                }
            }

            # Get the database name
            if (-not $Database) {
                $Database = $tables.Name
            }

            # Loop through the databases
            foreach ($dbName in $Database) {
                if ($server.VersionMajor -lt 9) {
                    Stop-Function -Message "SQL Server version must be 2005 or greater" -Continue -FunctionName Invoke-DbaDbDataMasking
                }

                $db = $server.Databases[$($dbName)]

                $nullmod = 0

                #region for each table
                foreach ($tableobject in $tables.Tables) {
                    $elapsed = [System.Diagnostics.Stopwatch]::StartNew()

                    $uniqueDataTableName = $null
                    $uniqueValueColumns = @()
                    $stringBuilder = [System.Text.StringBuilder]''

                    if ($tableobject.Name -in $ExcludeTable) {
                        Write-Message -Level Verbose -Message "Skipping $($tableobject.Name) because it is explicitly excluded" -FunctionName Invoke-DbaDbDataMasking -ModuleName "dbatools"
                        continue
                    }

                    if ($tableobject.Name -notin $db.Tables.Name) {
                        Stop-Function -Message "Table $($tableobject.Name) is not present in $db" -Target $db -Continue -FunctionName Invoke-DbaDbDataMasking
                    }

                    $dbTable = $db.Tables | Where-Object { $_.Schema -eq $tableobject.Schema -and $_.Name -eq $tableobject.Name }

                    [bool]$cleanupIdentityColumn = $false
                    [bool]$cleanupMaskingIndex = $false

                    # The masking index name used for cleanup checks
                    $maskingIndexName = "NIX__$($dbTable.Schema)_$($dbTable.Name)_Masking"

                    # Make sure there is an identity column present to speed things up
                    # Skip column and index creation when -WhatIf is active to avoid leaving behind schema changes
                    if (-not $WhatIfPreference) {
                        if (-not ($dbTable.Columns | Where-Object { $_.Identity -eq $true })) {
                            Write-Message -Level Verbose -Message "Adding identity column to table [$($dbTable.Schema)].[$($dbTable.Name)]" -FunctionName Invoke-DbaDbDataMasking -ModuleName "dbatools"
                            $query = "ALTER TABLE [$($dbTable.Schema)].[$($dbTable.Name)] ADD MaskingID BIGINT IDENTITY(1, 1) NOT NULL;"

                            try {
                                Invoke-DbaQuery -SqlInstance $server -SqlCredential $SqlCredential -Database $db.Name -Query $query
                            } catch {
                                Stop-Function -Message "Could not alter the table to add the masking id" -Target $db -Continue -FunctionName Invoke-DbaDbDataMasking
                            }

                            $cleanupIdentityColumn = $true

                            $identityColumn = "MaskingID"

                            $dbTable.Columns.Refresh()
                        } else {
                            $identityColumn = $dbTable.Columns | Where-Object { $_.Identity } | Select-Object -ExpandProperty Name
                        }

                        # Check if the index for the identity column is already present
                        try {
                            if ($dbTable.Indexes.Name -contains $maskingIndexName) {
                                Write-Message -Level Verbose -Message "Masking index already exists in table [$($dbTable.Schema)].[$($dbTable.Name)]. Dropping it..." -FunctionName Invoke-DbaDbDataMasking -ModuleName "dbatools"
                                $dbTable.Indexes[$($maskingIndexName)].Drop()
                            }
                        } catch {
                            Stop-Function -Message "Could not remove identity index to table [$($dbTable.Schema)].[$($dbTable.Name)]" -Continue -FunctionName Invoke-DbaDbDataMasking
                        }

                        # Create the index for the identity column
                        try {
                            Write-Message -Level Verbose -Message "Adding index on identity column [$($identityColumn)] in table [$($dbTable.Schema)].[$($dbTable.Name)]" -FunctionName Invoke-DbaDbDataMasking -ModuleName "dbatools"

                            $query = "CREATE NONCLUSTERED INDEX [$($maskingIndexName)] ON [$($dbTable.Schema)].[$($dbTable.Name)]([$($identityColumn)])"

                            $queryParams = @{
                                SqlInstance   = $server
                                SqlCredential = $SqlCredential
                                Database      = $db.Name
                                Query         = $query
                                QueryTimeout  = $CommandTimeout
                            }

                            Invoke-DbaQuery @queryParams
                            $cleanupMaskingIndex = $true
                        } catch {
                            Stop-Function -Message "Could not add identity index to table [$($dbTable.Schema)].[$($dbTable.Name)]" -Continue -FunctionName Invoke-DbaDbDataMasking
                        }
                    }

                    $actionIdentityValues = @()

                    try {
                        if ($WhatIfPreference) {
                            # In WhatIf mode, only get the row count without modifying the table structure
                            if ($tableobject.FilterQuery) {
                                $trimmedFilterQuery = ($tableobject.FilterQuery).Trim()

                                if ($trimmedFilterQuery.EndsWith(";")) {
                                    $trimmedFilterQuery = $trimmedFilterQuery.Substring(0, $trimmedFilterQuery.Length - 1)
                                }

                                $query = "SELECT COUNT(*) AS RowCount FROM ($trimmedFilterQuery) AS [dbatools_masking_source]"
                            } else {
                                $query = "SELECT COUNT(*) AS RowCount FROM [$($tableobject.Schema)].[$($tableobject.Name)]"
                            }

                            $rowCount = ($db.Query($query)).RowCount
                            $data = New-Object object[] $rowCount
                        } elseif (-not $tableobject.FilterQuery) {
                            # Get all the columns from the table
                            $columnString = "[" + (($dbTable.Columns | Where-Object { $_.DataType -in $supportedDataTypes } | Select-Object Name -ExpandProperty Name) -join "],[") + "]"

                            # Add the identifier column
                            $columnString += ",[$($identityColumn)]"

                            # Put it all together
                            $query = "SELECT $($columnString) FROM [$($tableobject.Schema)].[$($tableobject.Name)]"

                            # Get the data
                            [array]$data = $db.Query($query)
                        } else {
                            # Get the query from the table objects
                            $query = ($tableobject.FilterQuery).ToLower()

                            # Check if the query already contains the identifier column
                            if (-not ($query | Select-String -Pattern $identityColumn)) {
                                # Split up the query from the first "FROM"
                                $queryParts = $query -split "FROM", 2

                                # Put it all together again with the identifier
                                $query = "$($queryParts[0].Trim()), $($identityColumn) FROM $($queryParts[1].Trim())"
                            }

                            # Get the data
                            [array]$data = $db.Query($query)

                            $actionIdentityValues = @($data | ForEach-Object { $PSItem.$identityColumn } | Where-Object { $null -ne $PSItem } | Select-Object -Unique)
                        }
                    } catch {
                        Stop-Function -Message "Failure retrieving the data from table [$($tableobject.Schema)].[$($tableobject.Name)]" -Target $Database -ErrorRecord $_ -Continue -FunctionName Invoke-DbaDbDataMasking
                    }

                    #region unique indexes
                    # Check if the table contains unique indexes
                    if ($WhatIfPreference -and $tableobject.HasUniqueIndex) {
                        Write-Message -Level Verbose -Message "Skipping unique value preparation for [$($tableobject.Schema)].[$($tableobject.Name)] because -WhatIf is active" -FunctionName Invoke-DbaDbDataMasking -ModuleName "dbatools"
                    } elseif ($tableobject.HasUniqueIndex) {

                        # Loop through the rows and generate a unique value for each row
                        Write-Message -Level Verbose -Message "Generating unique values for [$($tableobject.Schema)].[$($tableobject.Name)]" -FunctionName Invoke-DbaDbDataMasking -ModuleName "dbatools"

                        $params = @{
                            SqlInstance   = $server
                            SqlCredential = $SqlCredential
                            Database      = $db.name
                            Schema        = $tableobject.Schema
                            Table         = $tableobject.Name
                        }

                        $indexToTable = Convert-DbaIndexToTable @params

                        if ($indexToTable) {
                            # compare the index columns to the column in the json table object
                            $compareParams = @{
                                ReferenceObject  = $indexToTable.Columns
                                DifferenceObject = $tableobject.Columns.Name
                                IncludeEqual     = $true
                            }
                            $maskingColumnIndexCount = (Compare-Object @compareParams | Where-Object { $_.SideIndicator -eq "==" }).Count

                            # Check if there is any need to generate unique values
                            if ($maskingColumnIndexCount -ge 1) {

                                # Check if the temporary table already exists
                                $server.Databases['tempdb'].Tables.Refresh()
                                $uniqueDataTableName = $indexToTable.TempTableName

                                if ($server.Databases['tempdb'].Tables.Name -contains $indexToTable.TempTableName) {
                                    Write-Message -Level Verbose -Message "Table '$($indexToTable.TempTableName)' already exists. Dropping it.." -FunctionName Invoke-DbaDbDataMasking -ModuleName "dbatools"
                                    try {
                                        $query = "DROP TABLE $($indexToTable.TempTableName)"
                                        Invoke-DbaQuery -SqlInstance $server -SqlCredential $SqlCredential -Database 'tempdb' -Query $query
                                    } catch {
                                        Stop-Function -Message "Could not drop temporary table" -FunctionName Invoke-DbaDbDataMasking
                                    }
                                }

                                # Create the temporary table
                                try {
                                    Write-Message -Level Verbose -Message "Creating temporary table '$($indexToTable.TempTableName)'" -FunctionName Invoke-DbaDbDataMasking -ModuleName "dbatools"
                                    Invoke-DbaQuery -SqlInstance $server -SqlCredential $SqlCredential -Database 'tempdb' -Query $indexToTable.CreateStatement
                                } catch {
                                    Stop-Function -Message "Could not create temporary table #[$($tableobject.Schema)].[$($tableobject.Name)]" -FunctionName Invoke-DbaDbDataMasking
                                }

                                # Create the unique index table
                                try {
                                    Write-Message -Level Verbose -Message "Creating the unique index for temporary table '$($indexToTable.TempTableName)'" -FunctionName Invoke-DbaDbDataMasking -ModuleName "dbatools"
                                    Invoke-DbaQuery -SqlInstance $server -SqlCredential $SqlCredential -Database 'tempdb' -Query $indexToTable.UniqueIndexStatement
                                } catch {
                                    Stop-Function -Message "Could not create temporary table #[$($tableobject.Schema)].[$($tableobject.Name)]" -FunctionName Invoke-DbaDbDataMasking
                                }

                                # Create a unique row
                                $retryCount = 0
                                for ($i = 0; $i -lt $data.Count; $i++) {
                                    $insertQuery = "INSERT INTO [$($indexToTable.TempTableName)]([$($indexToTable.Columns -join '],[')]) VALUES("
                                    $insertFailed = $false
                                    $insertValues = @()

                                    foreach ($indexColumn in $indexToTable.Columns) {
                                        $columnMaskInfo = $tableobject.Columns | Where-Object { $_.Name -eq $indexColumn }

                                        if ($indexColumn -eq "RowNr") {
                                            $newValue = $i + 1
                                        } elseif ($columnMaskInfo) {
                                            # make sure min is good
                                            if ($columnMaskInfo.MinValue) {
                                                $min = $columnMaskInfo.MinValue
                                            } else {
                                                if ($columnMaskInfo.CharacterString) {
                                                    $min = 1
                                                } else {
                                                    $min = 0
                                                }
                                            }

                                            # make sure max is good
                                            if ($MaxValue) {
                                                if ($columnMaskInfo.MaxValue -le $MaxValue) {
                                                    $max = $columnMaskInfo.MaxValue
                                                } else {
                                                    $max = $MaxValue
                                                }
                                            } else {
                                                $max = $columnMaskInfo.MaxValue
                                            }

                                            if (-not $columnMaskInfo.MaxValue -and -not $__boundMaxValue) {
                                                $max = 10
                                            }

                                            if ((-not $columnMaskInfo.MinValue -or -not $columnMaskInfo.MaxValue) -and ($columnMaskInfo.ColumnType -match 'date')) {
                                                if (-not $columnMaskInfo.MinValue) {
                                                    $min = (Get-Date).AddDays(-365)
                                                }
                                                if (-not $columnMaskInfo.MaxValue) {
                                                    $max = (Get-Date).AddDays(365)
                                                }
                                            }

                                            if ($columnMaskInfo.CharacterString) {
                                                $charstring = $columnMaskInfo.CharacterString
                                            } else {
                                                $charstring = $CharacterString
                                            }

                                            # Generate a new value
                                            $newValue = $null

                                            $newValueParams = $null

                                            try {
                                                $newValueParams = $null
                                                if (-not $columnobject.SubType -and $columnobject.ColumnType -in $supportedDataTypes) {
                                                    $newValueParams = @{
                                                        DataType = $columnMaskInfo.SubType
                                                        Min      = $columnMaskInfo.MinValue
                                                        Max      = $columnMaskInfo.MaxValue
                                                        Locale   = $Locale
                                                    }
                                                } else {
                                                    $newValueParams = @{
                                                        RandomizerType    = $columnMaskInfo.MaskingType
                                                        RandomizerSubtype = $columnMaskInfo.SubType
                                                        Min               = $min
                                                        Max               = $max
                                                        CharacterString   = $charstring
                                                        Format            = $columnMaskInfo.Format
                                                        Separator         = $columnMaskInfo.Separator
                                                        Locale            = $Locale
                                                    }
                                                }

                                                $newValue = Get-DbaRandomizedValue @newValueParams
                                            } catch {
                                                Stop-Function -Message "Failure" -Target $columnMaskInfo -Continue -ErrorRecord $_ -FunctionName Invoke-DbaDbDataMasking
                                            }
                                        } else {
                                            $newValue = $null
                                        }

                                        if ($columnMaskInfo) {
                                            try {
                                                $insertValue = Convert-DbaMaskingValue -Value $newValue -DataType $columnMaskInfo.ColumnType -Nullable:$columnMaskInfo.Nullable -EnableException

                                                if ($convertedValue.ErrorMessage) {
                                                    $maskingErrorFlag = $true
                                                    Stop-Function "Could not convert the value. $($convertedValue.ErrorMessage)" -Target $convertedValue -FunctionName Invoke-DbaDbDataMasking
                                                }
                                            } catch {
                                                Stop-Function -Message "Could not convert value" -ErrorRecord $_ -Target $newValue -FunctionName Invoke-DbaDbDataMasking
                                            }

                                            $insertValues += $insertValue.NewValue
                                        } elseif ($indexColumn -eq "RowNr") {
                                            $insertValues += $newValue
                                        } else {
                                            $insertValues += "NULL"
                                        }

                                        $uniqueValueColumns += $columnMaskInfo.Name
                                    }

                                    # Join all the values to the insert query
                                    $insertQuery += "$($insertValues -join ','));"

                                    # Try inserting the value
                                    try {
                                        $null = $server.Databases['tempdb'].Query($insertQuery)
                                        $insertFailed = $false
                                    } catch {
                                        Write-Message -Level Verbose -Message "Could not insert value" -FunctionName Invoke-DbaDbDataMasking -ModuleName "dbatools"
                                        $insertFailed = $true
                                    }

                                    # Try to insert the value as long it's failed
                                    while ($insertFailed) {
                                        if ($retryCount -eq $Retry) {
                                            Stop-Function -Message "Could not create a unique row after $retryCount tries. Stopping..." -FunctionName Invoke-DbaDbDataMasking
                                            return
                                        }

                                        $insertQuery = "INSERT INTO [$($indexToTable.TempTableName)]([$($indexToTable.Columns -join '],[')]) VALUES("

                                        foreach ($indexColumn in $indexToTable.Columns) {
                                            $columnMaskInfo = $tableobject.Columns | Where-Object { $_.Name -eq $indexColumn }

                                            if ($indexColumn -eq "RowNr") {
                                                $newValue = $i + 1
                                            } elseif ($columnMaskInfo) {
                                                # make sure min is good
                                                if ($columnMaskInfo.MinValue) {
                                                    $min = $columnMaskInfo.MinValue
                                                } else {
                                                    if ($columnMaskInfo.CharacterString) {
                                                        $min = 1
                                                    } else {
                                                        $min = 0
                                                    }
                                                }

                                                # make sure max is good
                                                if ($MaxValue) {
                                                    if ($columnMaskInfo.MaxValue -le $MaxValue) {
                                                        $max = $columnMaskInfo.MaxValue
                                                    } else {
                                                        $max = $MaxValue
                                                    }
                                                } else {
                                                    $max = $columnMaskInfo.MaxValue
                                                }

                                                if (-not $columnMaskInfo.MaxValue -and -not $__boundMaxValue) {
                                                    $max = 10
                                                }

                                                if ((-not $columnMaskInfo.MinValue -or -not $columnMaskInfo.MaxValue) -and ($columnMaskInfo.ColumnType -match 'date')) {
                                                    if (-not $columnMaskInfo.MinValue) {
                                                        $min = (Get-Date).AddDays(-365)
                                                    }
                                                    if (-not $columnMaskInfo.MaxValue) {
                                                        $max = (Get-Date).AddDays(365)
                                                    }
                                                }

                                                if ($columnMaskInfo.CharacterString) {
                                                    $charstring = $columnMaskInfo.CharacterString
                                                } else {
                                                    $charstring = $CharacterString
                                                }

                                                # Generate a new value
                                                $newValue = $null

                                                $newValueParams = $null

                                                try {
                                                    $newValueParams = $null
                                                    if (-not $columnobject.SubType -and $columnobject.ColumnType -in $supportedDataTypes) {
                                                        $newValueParams = @{
                                                            DataType = $columnMaskInfo.SubType
                                                            Min      = $columnMaskInfo.MinValue
                                                            Max      = $columnMaskInfo.MaxValue
                                                            Locale   = $Locale
                                                        }
                                                    } else {
                                                        $newValueParams = @{
                                                            RandomizerType    = $columnMaskInfo.MaskingType
                                                            RandomizerSubtype = $columnMaskInfo.SubType
                                                            Min               = $min
                                                            Max               = $max
                                                            CharacterString   = $charstring
                                                            Format            = $columnMaskInfo.Format
                                                            Separator         = $columnMaskInfo.Separator
                                                            Locale            = $Locale
                                                        }
                                                    }

                                                    $newValue = Get-DbaRandomizedValue @newValueParams
                                                } catch {
                                                    Stop-Function -Message "Failure" -Target $columnMaskInfo -Continue -ErrorRecord $_ -FunctionName Invoke-DbaDbDataMasking
                                                }
                                            } else {
                                                $newValue = $null
                                            }

                                            if ($columnMaskInfo) {
                                                try {
                                                    $insertValue = Convert-DbaMaskingValue -Value $newValue -DataType $columnMaskInfo.ColumnType -Nullable:$columnMaskInfo.Nullable -EnableException

                                                    if ($convertedValue.ErrorMessage) {
                                                        $maskingErrorFlag = $true
                                                        Stop-Function "Could not convert the value. $($convertedValue.ErrorMessage)" -Target $convertedValue -FunctionName Invoke-DbaDbDataMasking
                                                    }
                                                } catch {
                                                    Stop-Function -Message "Could not convert value" -ErrorRecord $_ -Target $newValue -FunctionName Invoke-DbaDbDataMasking
                                                }

                                                $insertValues += $insertValue.NewValue
                                            } elseif ($indexColumn -eq "RowNr") {
                                                $insertValues += $newValue
                                            } else {
                                                $insertValues += "NULL"
                                            }
                                        }

                                        # Join all the values to the insert query
                                        $insertQuery += "$($insertValues -join ','));"

                                        # Try inserting the value
                                        try {
                                            $null = $server.Databases['tempdb'].Query($insertQuery)
                                            $insertFailed = $false
                                        } catch {
                                            Write-Message -Level Verbose -Message "Could not insert value" -FunctionName Invoke-DbaDbDataMasking -ModuleName "dbatools"
                                            $insertFailed = $true
                                            $retryCount++
                                        }
                                    }
                                }

                                try {
                                    Write-Message -Level Verbose -Message "Creating masking index for [$($indexToTable.TempTableName)]" -FunctionName Invoke-DbaDbDataMasking -ModuleName "dbatools"
                                    $query = "CREATE NONCLUSTERED INDEX [NIX_$($indexToTable.TempTableName)_MaskID] ON [$($indexToTable.TempTableName)]([RowNr])"
                                    $null = $server.Databases['tempdb'].Query($query)
                                } catch {
                                    Stop-Function -Message "Could not add masking index for [$($indexToTable.TempTableName)]" -ErrorRecord $_ -FunctionName Invoke-DbaDbDataMasking
                                }
                            } else {
                                Write-Message -Level Verbose -Message "Table [$($tableobject.Schema)].[$($tableobject.Name)] does not contain any masking index columns to process" -FunctionName Invoke-DbaDbDataMasking -ModuleName "dbatools"
                            }
                        } else {
                            Stop-Function -Message "The table does not have any indexes" -FunctionName Invoke-DbaDbDataMasking
                        }
                    }

                    #endregion unique indexes

                    $tablecolumns = $tableobject.Columns

                    if ($Column) {
                        $tablecolumns = $tablecolumns | Where-Object { $_.Name -in $Column }
                    }

                    if ($ExcludeColumn) {
                        if ([string]$uniqueIndex.Columns -match ($ExcludeColumn -join "|")) {
                            Stop-Function -Message "Column present in -ExcludeColumn cannot be excluded because it's part of an unique index" -Target $ExcludeColumn -Continue -FunctionName Invoke-DbaDbDataMasking
                        }

                        $tablecolumns = $tablecolumns | Where-Object { $_.Name -notin $ExcludeColumn }
                    }

                    if (-not $tablecolumns) {
                        Write-Message -Level Verbose "No columns to process in [$($dbName)].[$($tableobject.Schema)].[$($tableobject.Name)], moving on" -FunctionName Invoke-DbaDbDataMasking -ModuleName "dbatools"
                        continue
                    }

                    # Figure out if the columns has actions
                    $columnsWithActions = @()
                    $columnsWithActions += $tableobject.Columns | Where-Object { $null -ne $_.Action }

                    # Figure out if the columns has composites
                    $columnsWithComposites = @()
                    $columnsWithComposites += $tableobject.Columns | Where-Object { $null -ne $_.Composite }

                    # Check for both special actions
                    if (($columnsWithComposites.Count -ge 1) -and ($columnsWithActions.Count -ge 1)) {
                        Stop-Function -Message "You cannot use both composites and actions" -FunctionName Invoke-DbaDbDataMasking
                    }

                    # Filter out columns with actions or composites for separate processing
                    $standardColumns = $tablecolumns | Where-Object {
                        ($_.Name -notin $columnsWithActions.Name) -and
                        ($_.Name -notin $columnsWithComposites.Name)
                    }

                    if ($__realCmdlet.ShouldProcess($instance, "Masking $($data.Count) row(s) for column [$($tablecolumns.Name -join ', ')] in $($dbName).$($tableobject.Schema).$($tableobject.Name)")) {
                        $totalBatches = [System.Math]::Ceiling($data.Count / $BatchSize)
                        [bool]$maskingErrorFlag = $false

                        # OPTIMIZED SECTION: Process rows in batches, updating all columns for each row at once
                        $batchNr = 0
                        $batchRowNr = 0
                        $rowNumber = 0

                        # Process rows in batches
                        for ($rowIndex = 0; $rowIndex -lt $data.Count; $rowIndex++) {
                            $row = $data[$rowIndex]
                            $rowNumber++
                            $batchRowNr++

                            if ((($batchRowNr - 1) % 100) -eq 0) {
                                $progressParams = @{
                                    StepNumber = $batchNr
                                    TotalSteps = $totalBatches
                                    Activity   = "Masking $($data.Count) rows in $($tableobject.Schema).$($tableobject.Name) in $($dbName) on $instance"
                                    Message    = "Generating Updates"
                                }

                                Invoke-DbaDbDataMasking $progressParams
                            }

                            # Create array to hold all column updates for this row
                            $updates = @()

                            # Process all standard columns for this row
                            foreach ($columnobject in $standardColumns) {
                                $newValue = $null

                                # Handle static values
                                if ($null -ne $columnobject.StaticValue) {
                                    $newValue = $columnobject.StaticValue

                                    if ($null -eq $newValue -and -not $columnobject.Nullable) {
                                        Write-Message -Message "Column '$($columnobject.Name)' static value cannot be null when column is set not to be nullable." -Level Warning -FunctionName Invoke-DbaDbDataMasking -ModuleName "dbatools"
                                        continue
                                    }
                                }
                                # Check for various conditions to determine the new value
                                elseif ($columnobject.KeepNull -and $columnobject.Nullable -and
                                    (($row.($columnobject.Name)).GetType().Name -eq 'DBNull') -or
                                    ($row.($columnobject.Name) -eq '')) {
                                    $newValue = $null
                                } elseif (-not $columnobject.KeepNull -and $columnobject.Nullable -and
                                    (($nullmod++) % $ModulusFactor -eq 0)) {
                                    $newValue = $null
                                } elseif ($tableobject.HasUniqueIndex -and $columnobject.Name -in $uniqueValueColumns) {
                                    # Get value from unique data table
                                    $query = "SELECT $($columnobject.Name) FROM $($uniqueDataTableName) WHERE [RowNr] = $rowNumber"

                                    try {
                                        $uniqueData = Invoke-DbaQuery -SqlInstance $server -SqlCredential $SqlCredential -Database tempdb -Query $query
                                    } catch {
                                        Stop-Function -Message "Something went wrong getting the unique data" -Target $query -ErrorRecord $_ -continue -FunctionName Invoke-DbaDbDataMasking
                                    }

                                    if ($null -eq $uniqueData) {
                                        Stop-Function -Message "Could not find any unique values" -Target $tableobject -FunctionName Invoke-DbaDbDataMasking
                                        return
                                    }

                                    $newValue = $uniqueData.$($columnobject.Name)
                                } elseif ($columnobject.Deterministic) {
                                    # Check for deterministic value
                                    if (($null -ne $row.($columnobject.Name)) -and ($row.($columnobject.Name) -ne '')) {
                                        try {
                                            $lookupValue = Convert-DbaMaskingValue -Value $row.($columnobject.Name) -DataType varchar -Nullable:$columnobject.Nullable -EnableException

                                            if ($convertedValue.ErrorMessage) {
                                                $maskingErrorFlag = $true
                                                Stop-Function "Could not convert the value. $($convertedValue.ErrorMessage)" -Target $convertedValue -continue -FunctionName Invoke-DbaDbDataMasking
                                            }
                                        } catch {
                                            Stop-Function -Message "Could not convert value" -ErrorRecord $_ -Target $row.($columnobject.Name) -continue -FunctionName Invoke-DbaDbDataMasking
                                        }

                                        $query = "SELECT [NewValue] FROM dbo.DeterministicValues WHERE [ValueKey] = $($lookupValue.NewValue)"

                                        try {
                                            $lookupResult = $null
                                            $lookupResult = $server.Databases['tempdb'].Query($query)

                                            if ($lookupResult.NewValue) {
                                                $newValue = $lookupResult.NewValue
                                            }
                                        } catch {
                                            Stop-Function -Message "Something went wrong retrieving the deterministic values" -Target $query -ErrorRecord $_ -continue -FunctionName Invoke-DbaDbDataMasking
                                        }
                                    }
                                }

                                # If we haven't determined a value yet, generate one
                                if ($null -eq $newValue -and $null -eq $columnobject.StaticValue) {
                                    # make sure min is good
                                    if ($columnobject.MinValue) {
                                        $min = $columnobject.MinValue
                                    } else {
                                        if ($columnobject.CharacterString) {
                                            $min = 1
                                        } else {
                                            $min = 0
                                        }
                                    }

                                    # make sure max is good
                                    if ($MaxValue) {
                                        if ($columnobject.MaxValue -le $MaxValue) {
                                            $max = $columnobject.MaxValue
                                        } else {
                                            $max = $MaxValue
                                        }
                                    } else {
                                        $max = $columnobject.MaxValue
                                    }

                                    if (-not $columnobject.MaxValue -and -not $__boundMaxValue) {
                                        $max = 10
                                    }

                                    if ((-not $columnobject.MinValue -or -not $columnobject.MaxValue) -and ($columnobject.ColumnType -match 'date')) {
                                        if (-not $columnobject.MinValue) {
                                            $min = (Get-Date).AddDays(-365)
                                        }
                                        if (-not $columnobject.MaxValue) {
                                            $max = (Get-Date).AddDays(365)
                                        }
                                    }

                                    if ($columnobject.CharacterString) {
                                        $charstring = $columnobject.CharacterString
                                    } else {
                                        $charstring = $CharacterString
                                    }

                                    # Setup the new value parameters
                                    $newValueParams = $null

                                    if ($null -eq $columnobject.SubType) {
                                        $newValueParams = @{
                                            DataType        = $columnobject.ColumnType
                                            Min             = $min
                                            Max             = $max
                                            CharacterString = $charstring
                                            Format          = $columnobject.Format
                                            Locale          = $Locale
                                        }
                                    } elseif ($columnobject.SubType.ToLowerInvariant() -in 'shuffle', 'string2', 'string') {
                                        if ($columnobject.ColumnType -in 'bigint', 'char', 'int', 'nchar', 'nvarchar', 'smallint', 'tinyint', 'varchar') {
                                            $newValueParams = @{
                                                RandomizerType    = "Random"
                                                RandomizerSubtype = "Shuffle"
                                                Value             = ($row.$($columnobject.Name))
                                                Locale            = $Locale
                                            }
                                        } elseif ($columnobject.ColumnType -in 'decimal', 'numeric', 'float', 'money', 'smallmoney', 'real') {
                                            $newValueParams = @{
                                                RandomizerType    = "Random"
                                                RandomizerSubtype = "Shuffle"
                                                Value             = ($row.$($columnobject.Name))
                                                Locale            = $Locale
                                            }
                                        }
                                    } else {
                                        $newValueParams = @{
                                            RandomizerType    = $columnobject.MaskingType
                                            RandomizerSubtype = $columnobject.SubType
                                            Min               = $min
                                            Max               = $max
                                            CharacterString   = $charstring
                                            Format            = $columnobject.Format
                                            Separator         = $columnobject.Separator
                                            Locale            = $Locale
                                        }
                                    }

                                    # Generate the new value
                                    try {
                                        $newValue = Get-DbaRandomizedValue @newValueParams
                                    } catch {
                                        $maskingErrorFlag = $true
                                        Stop-Function -Message "Failure" -Target $columnobject -Continue -ErrorRecord $_ -FunctionName Invoke-DbaDbDataMasking
                                    }
                                }

                                # Convert the value for SQL
                                try {
                                    if ($row.($columnobject.Name) -eq '' -and $columnobject.ColumnType -in 'decimal') {
                                        $newvalue = "0.00"
                                    }
                                    $convertedValue = Convert-DbaMaskingValue -Value $newValue -DataType $columnobject.ColumnType -Nullable:$columnobject.Nullable -EnableException

                                    if ($convertedValue.ErrorMessage) {
                                        $maskingErrorFlag = $true
                                        Stop-Function "Could not convert the value. $($convertedValue.ErrorMessage)" -Target $convertedValue -continue -FunctionName Invoke-DbaDbDataMasking
                                    }
                                } catch {
                                    Stop-Function -Message "Could not convert value" -ErrorRecord $_ -Target $newValue -continue -FunctionName Invoke-DbaDbDataMasking
                                }

                                # Add to the updates
                                $updates += "[$($columnobject.Name)] = $($convertedValue.NewValue)"

                                # Handle deterministic values storage
                                if ($columnobject.Deterministic -and ($null -ne $row.($columnobject.Name)) -and
                                    ($row.($columnobject.Name) -ne '') -and ($null -eq $lookupResult.NewValue)) {
                                    try {
                                        $previous = Convert-DbaMaskingValue -Value $row.($columnobject.Name) -DataType $columnobject.ColumnType -Nullable:$columnobject.Nullable -EnableException

                                        if ($convertedValue.ErrorMessage) {
                                            $maskingErrorFlag = $true
                                            Stop-Function "Could not convert the value. $($convertedValue.ErrorMessage)" -Target $convertedValue -FunctionName Invoke-DbaDbDataMasking
                                            continue
                                        }

                                        $query = "INSERT INTO dbo.DeterministicValues (ValueKey, NewValue) VALUES ($($previous.NewValue), $($convertedValue.NewValue));"
                                        $null = $server.Databases['tempdb'].Query($query)
                                    } catch {
                                        Stop-Function -Message "Could not save deterministic value.`n$_" -Target $query -ErrorRecord $_ -FunctionName Invoke-DbaDbDataMasking
                                        continue
                                    }
                                }
                            }

                            # Only create an update if we have columns to update
                            if ($updates.Count -gt 0) {
                                # Create one UPDATE statement for all columns in this row
                                $updateQuery = "UPDATE [$($tableobject.Schema)].[$($tableobject.Name)] SET $($updates -join ', ') WHERE [$($identityColumn)] = $($row.$($identityColumn)); "
                                $null = $stringBuilder.AppendLine($updateQuery)
                            }

                            # If we've reached the batch size or this is the last row, execute the batch
                            if ($batchRowNr -eq $BatchSize -or $rowIndex -eq ($data.Count - 1)) {
                                # Increase the batch counter
                                $batchNr++

                                # Execute the batch if we have updates
                                if ($stringBuilder.Length -gt 0) {
                                    try {
                                        $progressParams = @{
                                            StepNumber = $batchNr
                                            TotalSteps = $totalBatches
                                            Activity   = "Masking $($data.Count) rows in $($tableobject.Schema).$($tableobject.Name) in $($dbName) on $instance"
                                            Message    = "Executing Batch $batchNr/$totalBatches"
                                        }

                                        Invoke-DbaDbDataMasking $progressParams

                                        Write-Message -Level Verbose -Message "Executing batch $batchNr/$totalBatches" -FunctionName Invoke-DbaDbDataMasking -ModuleName "dbatools"

                                        $queryParams = @{
                                            SqlInstance     = $instance
                                            SqlCredential   = $SqlCredential
                                            Database        = $db.Name
                                            Query           = $stringBuilder.ToString()
                                            EnableException = $EnableException
                                            QueryTimeout    = $CommandTimeout
                                        }

                                        Invoke-DbaQuery @queryParams
                                    } catch {
                                        $maskingErrorFlag = $true
                                        Stop-Function -Message "Error updating $($tableobject.Schema).$($tableobject.Name): $_ `n$($stringBuilder.ToString())" -Target $stringBuilder.ToString() -Continue -ErrorRecord $_ -FunctionName Invoke-DbaDbDataMasking
                                    }

                                    # Clear the string builder for the next batch
                                    $null = $stringBuilder.Clear()
                                }

                                # Reset batch row counter
                                $batchRowNr = 0
                            }
                        }

                        # Process Actions separately
                        if ($columnsWithActions.Count -ge 1) {
                            foreach ($columnObject in $columnsWithActions) {
                                Write-Message -Level Verbose -Message "Processing action for [$($columnObject.Name)]" -FunctionName Invoke-DbaDbDataMasking -ModuleName "dbatools"

                                [bool]$validAction = $true
                                $columnAction = $columnobject.Action
                                $query = "UPDATE [$($tableobject.Schema)].[$($tableobject.Name)] SET [$($columnObject.Name)] = "



                                if ($columnAction.Category -eq 'DateTime') {
                                    switch ($columnAction.Type) {
                                        "Add" {
                                            $query += "DATEADD($($columnAction.SubCategory), $($columnAction.Value), [$($columnObject.Name)]);"
                                        }
                                        "Subtract" {
                                            $query += "DATEADD($($columnAction.SubCategory), - $($columnAction.Value), [$($columnObject.Name)]);"
                                        }
                                        default {
                                            $validAction = $false
                                        }
                                    }
                                } elseif ($columnAction.Category -eq 'Number') {
                                    switch ($columnAction.Type) {
                                        "Add" {
                                            $query += "[$($columnObject.Name)] + $($columnAction.Value);"
                                        }
                                        "Divide" {
                                            $query += "[$($columnObject.Name)] / $($columnAction.Value);"
                                        }
                                        "Multiply" {
                                            $query += "[$($columnObject.Name)] * $($columnAction.Value);"
                                        }
                                        "Subtract" {
                                            $query += "[$($columnObject.Name)] - $($columnAction.Value);"
                                        }
                                        default {
                                            $validAction = $false
                                        }
                                    }
                                } elseif ($columnAction.Category -eq 'Column') {
                                    switch ($columnAction.Type) {
                                        "Set" {
                                            if ($columnobject.ColumnType -like '*int*' -or $columnobject.ColumnType -in 'bit', 'bool', 'decimal', 'numeric', 'float', 'money', 'smallmoney', 'real') {
                                                $query += "$($columnAction.Value)"
                                            } elseif ($columnobject.ColumnType -in '*date*', 'time', 'uniqueidentifier') {
                                                $query += "'$($columnAction.Value)'"
                                            } else {
                                                $query += "'$($columnAction.Value)'"
                                            }
                                        }
                                        "Nullify" {
                                            if ($columnobject.Nullable) {
                                                $query += "NULL"
                                            } else {
                                                $validAction = $false
                                            }
                                        }
                                        default {
                                            $validAction = $false
                                        }
                                    }
                                }
                                # Apply actions only to the rows returned by FilterQuery
                                if ($validAction -and $tableobject.FilterQuery -and $actionIdentityValues.Count -ge 1) {
                                    for ($batchStart = 0; $batchStart -lt $actionIdentityValues.Count; $batchStart += $BatchSize) {
                                        $batchEnd = [System.Math]::Min($batchStart + $BatchSize - 1, $actionIdentityValues.Count - 1)
                                        $identityBatch = $actionIdentityValues[$batchStart .. $batchEnd] -join ", "
                                        $null = $stringBuilder.AppendLine($query.TrimEnd(";") + " WHERE [$identityColumn] IN ($identityBatch);")
                                    }
                                }

                                # Add the query to the rest
                                if ($validAction -and -not $tableobject.FilterQuery) {
                                    $null = $stringBuilder.AppendLine($query)
                                }
                            }

                            try {
                                if ($stringBuilder.Length -ge 1) {
                                    Invoke-DbaQuery -SqlInstance $instance -SqlCredential $SqlCredential -Database $db.Name -Query $stringBuilder.ToString() -EnableException
                                }
                            } catch {
                                $stringBuilder.ToString()
                                Stop-Function -Message "Error updating $($tableobject.Schema).$($tableobject.Name): $_" -Target $stringBuilder -Continue -ErrorRecord $_ -FunctionName Invoke-DbaDbDataMasking
                            }

                            $null = $stringBuilder.Clear()
                        }

                        # Process Composites separately
                        if ($columnsWithComposites.Count -ge 1) {
                            foreach ($columnObject in $columnsWithComposites) {
                                Write-Message -Level Verbose -Message "Processing composite for [$($columnObject.Name)]" -FunctionName Invoke-DbaDbDataMasking -ModuleName "dbatools"

                                $compositeItems = @()

                                foreach ($columnComposite in $columnObject.Composite) {
                                    if ($columnComposite.Type -eq 'Column') {
                                        $compositeItems += "[$($columnComposite.Value)]"
                                    } elseif ($columnComposite.Type -eq 'Static') {
                                        $compositeItems += "'$($columnComposite.Value)'"
                                    } elseif ($columnComposite.Type -in $supportedFakerMaskingTypes) {
                                        try {
                                            $newValue = $null

                                            if ($columnobject.SubType -in $supportedDataTypes) {
                                                $newValueParams = @{
                                                    DataType        = $columnobject.SubType
                                                    CharacterString = $charstring
                                                    Min             = $columnComposite.Min
                                                    Max             = $columnComposite.Max
                                                    Locale          = $Locale
                                                }

                                                $newValue = Get-DbaRandomizedValue @newValueParams
                                            } else {
                                                $newValueParams = @{
                                                    RandomizerType    = $columnobject.MaskingType
                                                    RandomizerSubtype = $columnobject.SubType
                                                    Min               = $min
                                                    Max               = $max
                                                    CharacterString   = $charstring
                                                    Format            = $columnobject.Format
                                                    Separator         = $columnobject.Separator
                                                    Locale            = $Locale
                                                }

                                                $newValue = Get-DbaRandomizedValue @newValueParams
                                            }
                                        } catch {
                                            Stop-Function -Message "Failure" -Target $faker -Continue -ErrorRecord $_ -FunctionName Invoke-DbaDbDataMasking
                                        }

                                        if ($columnobject.ColumnType -match 'int') {
                                            $compositeItems += " $newValue"
                                        } elseif ($columnobject.ColumnType -in 'bit', 'bool') {
                                            if ($columnValue) {
                                                $compositeItems += "1"
                                            } else {
                                                $compositeItems += "0"
                                            }
                                        } else {
                                            $newValue = ($newValue).Tostring().Replace("'", "''")
                                            $compositeItems += "'$newValue'"
                                        }
                                    } else {
                                        $compositeItems += ""
                                    }
                                }

                                $compositeItemsUpdated = $compositeItems | ForEach-Object { $_ = "ISNULL($($_), '')"; $_ }

                                $null = $stringBuilder.AppendLine("UPDATE [$($tableobject.Schema)].[$($tableobject.Name)] SET [$($columnObject.Name)] = $($compositeItemsUpdated -join ' + ')")
                            }

                            try {
                                $stringBuilder.ToString()
                                Invoke-DbaQuery -SqlInstance $instance -SqlCredential $SqlCredential -Database $db.Name -Query $stringBuilder.ToString() -EnableException
                            } catch {
                                Stop-Function -Message "Error updating $($tableobject.Schema).$($tableobject.Name): $_" -Target $stringBuilder -Continue -ErrorRecord $_ -FunctionName Invoke-DbaDbDataMasking
                            }

                            $null = $stringBuilder.Clear()
                        }

                        # Return the masking results
                        if ($maskingErrorFlag) {
                            $maskingStatus = "Failed"
                        } else {
                            $maskingStatus = "Successful"
                        }

                        [PSCustomObject]@{
                            ComputerName = $db.Parent.ComputerName
                            InstanceName = $db.Parent.ServiceName
                            SqlInstance  = $db.Parent.DomainInstanceName
                            Database     = $dbName
                            Schema       = $tableobject.Schema
                            Table        = $tableobject.Name
                            Columns      = $tableobject.Columns.Name
                            Rows         = $($data.Count)
                            Elapsed      = [prettytimespan]$elapsed.Elapsed
                            Status       = $maskingStatus
                        }


                        # Reset time
                        $null = $elapsed.Reset()
                    }

                    # Clean up the masking index created for this masking run
                    if ($cleanupMaskingIndex) {
                        try {
                            # Refresh the indexes to make sure to have the latest list
                            $dbTable.Indexes.Refresh()

                            # Check if the index is there
                            if ($dbTable.Indexes.Name -contains $maskingIndexName) {
                                Write-Message -Level verbose -Message "Removing identity index from table [$($dbTable.Schema)].[$($dbTable.Name)]" -FunctionName Invoke-DbaDbDataMasking -ModuleName "dbatools"
                                $dbTable.Indexes[$($maskingIndexName)].Drop()
                            }
                        } catch {
                            Stop-Function -Message "Could not remove identity index from table [$($dbTable.Schema)].[$($dbTable.Name)]" -Continue -FunctionName Invoke-DbaDbDataMasking
                        }
                    }

                    # Clean up the identity column (always runs, regardless of -WhatIf or errors during masking)
                    if ($cleanupIdentityColumn) {
                        try {
                            Write-Message -Level Verbose -Message "Removing identity column [$($identityColumn)] from table [$($dbTable.Schema)].[$($dbTable.Name)]" -FunctionName Invoke-DbaDbDataMasking -ModuleName "dbatools"

                            $query = "ALTER TABLE [$($dbTable.Schema)].[$($dbTable.Name)] DROP COLUMN [$($identityColumn)]"

                            Invoke-DbaQuery -SqlInstance $instance -SqlCredential $SqlCredential -Database $db.Name -Query $query -EnableException
                        } catch {
                            Stop-Function -Message "Could not remove identity column from table [$($dbTable.Schema)].[$($dbTable.Name)]" -Continue -FunctionName Invoke-DbaDbDataMasking
                        }
                    }

                    # Cleanup
                    if ($uniqueDataTableName) {
                        Write-Message -Message "Cleaning up unique temporary table '$uniqueDataTableName'" -Level verbose -FunctionName Invoke-DbaDbDataMasking -ModuleName "dbatools"
                        $query = "DROP TABLE [$($uniqueDataTableName)];"
                        try {
                            $null = Invoke-DbaQuery -SqlInstance $server -SqlCredential $SqlCredential -Database 'tempdb' -Query $query -EnableException
                        } catch {
                            Stop-Function -Message "Could not clean up unique values table '$uniqueDataTableName'" -Target $uniqueDataTableName -ErrorRecord $_ -FunctionName Invoke-DbaDbDataMasking
                        }
                    }
                }
                #endregion for each table

                # Export the dictionary when needed
                if ($DictionaryExportPath) {
                    try {
                        # Handle dictionary
                        $query = "SELECT [ValueKey], [NewValue] FROM dbo.DeterministicValues"
                        [array]$dictResult = $server.Databases['tempdb'].Query($query)

                        if ($dictResult.Count -ge 1) {
                            Write-Message -Message "Writing dictionary for $($db.Name)" -Level Verbose -FunctionName Invoke-DbaDbDataMasking -ModuleName "dbatools"

                            # Check if the output directory already exists
                            if (-not (Test-Path -Path $DictionaryExportPath)) {
                                $null = New-Item -Path $DictionaryExportPath -ItemType Directory
                            }

                            # Of course with Linux we need to change the slashes
                            if (-not $script:isWindows) {
                                $dictionaryFileName = $dictionaryFileName.Replace("\", "/")
                            }

                            # Setup the file paths
                            $filenamepart = $server.Name.Replace('\', '$').Replace('TCP:', '').Replace(',', '.')
                            $dictionaryFileName = "$DictionaryExportPath\$($filenamepart).$($db.Name).Dictionary.csv"

                            # Export dictionary
                            $null = $dictResult | Export-Csv -Path $dictionaryFileName -NoTypeInformation

                            Get-ChildItem -Path $dictionaryFileName
                        } else {
                            Write-Message -Level Verbose -Message "No values to export as a dictionary" -FunctionName Invoke-DbaDbDataMasking -ModuleName "dbatools"
                        }
                    } catch {
                        Stop-Function -Message "Something went wrong writing the dictionary to the $DictionaryExportPath" -Target $DictionaryExportPath -Continue -ErrorRecord $_ -FunctionName Invoke-DbaDbDataMasking
                    }
                }
            } # End foreach database

            # Do some cleanup
            $null = $server.Databases['tempdb'].Tables.Refresh()

            if ($server.Databases['tempdb'].Tables.Name -contains 'DeterministicValues') {
                $query = "DROP TABLE dbo.DeterministicValues"

                try {
                    Write-Message -Level Verbose -Message "Cleaning up deterministic values table" -FunctionName Invoke-DbaDbDataMasking -ModuleName "dbatools"
                    $null = $server.Databases['tempdb'].Query($query)
                } catch {
                    Stop-Function -Message "Could not remove deterministic value table" -ErrorRecord $_ -FunctionName Invoke-DbaDbDataMasking
                }
            }

        } # End foreach instance
    }

    $__iv = Get-Variable -Name __dbatools_interrupt_function_78Q9VPrM6999g6zo24Qn83m09XF56InEn4hFrA8Fwhu5xJrs6r -Scope 0 -ErrorAction Ignore
    $__snap = @{}
    foreach ($__name in "identityColumn", "convertedValue", "lookupResult", "charstring", "min", "max", "columnobject", "maskingErrorFlag", "dictionaryFileName", "Database", "insertValue") {
        $__v = Get-Variable -Name $__name -Scope 0 -ErrorAction Ignore
        if ($__v) { $__snap[$__name + "Assigned"] = $true; $__snap[$__name] = $__v.Value } else { $__snap[$__name + "Assigned"] = $false }
    }
    @{ __invokeDbaDbDataMaskingProcess = @{ Interrupted = [bool]($__iv -and $__iv.Value); State = $__snap } }
} $SqlInstance $SqlCredential $Database $FilePath $Locale $CharacterString $Table $Column $ExcludeTable $ExcludeColumn $MaxValue $DictionaryFilePath $DictionaryExportPath $EnableException $__beginState $__state $__realCmdlet $__boundMaxValue $__boundWhatIf $__boundConfirm $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
