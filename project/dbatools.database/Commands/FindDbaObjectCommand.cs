#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Finds database objects (tables, views, procedures, functions, synonyms, triggers) and optionally
/// columns whose name matches a regex pattern, across instances and databases. Port of
/// public/Find-DbaObject.ps1; the workflow remains a module-scoped PowerShell compatibility hop.
///
/// SqlInstance is ValueFromPipeline, so the source is begin + process with process firing per record.
/// The begin block is PURE STRING BUILDING: from ObjectType (via a type-code map) and
/// IncludeSystemObjects it builds two constant T-SQL query strings ($sqlObjects and $sqlColumns, with an
/// optional trigger UNION). The process block, per instance, connects, filters the databases, runs
/// $sqlObjects and regex-matches the object names, then optionally runs $sqlColumns for column-name
/// matches. The ONLY begin-to-process dependency is those two constant strings, which process reads
/// read-only and never mutates - so this is a ONE-WAY constant carry: begin emits them via a sentinel
/// Hashtable (stored in _state), process restores them; there is no process re-emit. Unlike
/// Find-DbaDatabase there is no cross-record $Pattern mutation (Pattern is only matched, never
/// reassigned), so no per-record state carry is needed.
///
/// No ShouldProcess, no early return (the bare Continue on version &lt; 9 is a loop keyword continuing
/// the in-scriptblock foreach), no interrupt (the one Stop-Function is -Continue). Body edits are
/// -FunctionName Find-DbaObject on the process block's one Stop-Function and five Write-Message calls;
/// begin has none. Surface pinned by migration/baselines/Find-DbaObject.json (positions 0-5, ObjectType
/// ValidateSet, no ShouldProcess).
/// </summary>
[Cmdlet(VerbsCommon.Find, "DbaObject")]
public sealed class FindDbaObjectCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The database(s) to search.</summary>
    [Parameter(Position = 2)]
    public object[]? Database { get; set; }

    /// <summary>The database(s) to exclude.</summary>
    [Parameter(Position = 3)]
    public object[]? ExcludeDatabase { get; set; }

    /// <summary>The regular-expression pattern to match object (and column) names against.</summary>
    [Parameter(Mandatory = true, Position = 4)]
    [PsStringCast]
    public string Pattern { get; set; } = null!;

    /// <summary>The object type(s) to search.</summary>
    [Parameter(Position = 5)]
    [ValidateSet("Table", "View", "StoredProcedure", "ScalarFunction", "TableValuedFunction", "Synonym", "Trigger", "All")]
    [PsStringArrayCast]
    public string[] ObjectType { get; set; } = new[] { "All" };

    /// <summary>Also search column names.</summary>
    [Parameter]
    public SwitchParameter IncludeColumns { get; set; }

    /// <summary>Include system objects in the search.</summary>
    [Parameter]
    public SwitchParameter IncludeSystemObjects { get; set; }

    /// <summary>Include system databases in the search.</summary>
    [Parameter]
    public SwitchParameter IncludeSystemDatabases { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // The two constant query strings built once in begin, carried one-way begin->process.
    private Hashtable? _state;

    protected override void BeginProcessing()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BeginScript,
            ObjectType, IncludeSystemObjects.ToBool(), EnableException.ToBool(),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (item?.BaseObject is Hashtable sentinel && sentinel.ContainsKey("__findDbaObjectBegin"))
            {
                _state = sentinel["__findDbaObjectBegin"] as Hashtable;
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
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, SqlCredential, Database, ExcludeDatabase, Pattern,
            IncludeColumns.ToBool(), IncludeSystemDatabases.ToBool(), EnableException.ToBool(), _state,
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

    // PS: the begin block VERBATIM (pure string building - no Stop-Function/Write-Message) plus a
    // sentinel that hands the two constant query strings to the process hop.
    private const string BeginScript = """
param($ObjectType, $IncludeSystemObjects, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([string[]]$ObjectType, $IncludeSystemObjects, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        $typeCodeMap = @{
            "Table"               = @("'U'")
            "View"                = @("'V'")
            "StoredProcedure"     = @("'P'")
            "ScalarFunction"      = @("'FN'")
            "TableValuedFunction" = @("'IF'", "'TF'")
            "Synonym"             = @("'SN'")
            "Trigger"             = @("'TR'")
        }

        if ("All" -in $ObjectType) {
            $typeFilter = "'U', 'V', 'P', 'FN', 'IF', 'TF', 'SN', 'TR'"
        } else {
            $typeCodes = @()
            foreach ($type in $ObjectType) {
                $typeCodes += $typeCodeMap[$type]
            }
            $typeFilter = ($typeCodes | Select-Object -Unique) -join ", "
        }

        $includeDatabaseTriggers = "All" -in $ObjectType -or "Trigger" -in $ObjectType
        $sysFilter = if ($IncludeSystemObjects) { "" } else { "AND o.is_ms_shipped = 0" }
        $triggerFilter = if ($IncludeSystemObjects) { "" } else { "AND tr.is_ms_shipped = 0" }

        $sqlObjects = "
            SELECT
                OBJECT_SCHEMA_NAME(o.object_id) AS SchemaName,
                o.name                          AS ObjectName,
                RTRIM(o.type)                   AS ObjectTypeCode,
                o.type_desc                     AS ObjectType,
                o.create_date                   AS CreateDate,
                o.modify_date                   AS LastModified
            FROM sys.objects o
            WHERE o.type IN ($typeFilter)
            $sysFilter"

        if ($includeDatabaseTriggers) {
            $sqlObjects += "
            UNION ALL
            SELECT
                CAST(NULL AS sysname)            AS SchemaName,
                tr.name                          AS ObjectName,
                RTRIM(tr.type)                   AS ObjectTypeCode,
                tr.type_desc                     AS ObjectType,
                tr.create_date                   AS CreateDate,
                tr.modify_date                   AS LastModified
            FROM sys.triggers tr
            WHERE tr.parent_class = 0
            AND tr.type = 'TR'
            $triggerFilter"
        }

        $sqlColumns = "
            SELECT
                OBJECT_SCHEMA_NAME(c.object_id) AS SchemaName,
                OBJECT_NAME(c.object_id)        AS ObjectName,
                RTRIM(o.type)                   AS ObjectTypeCode,
                o.type_desc                     AS ObjectType,
                o.create_date                   AS CreateDate,
                o.modify_date                   AS LastModified,
                c.name                          AS ColumnName
            FROM sys.columns c
            INNER JOIN sys.objects o ON c.object_id = o.object_id
            WHERE o.type IN ($typeFilter)
            $sysFilter"

    @{ __findDbaObjectBegin = @{ SqlObjects = $sqlObjects; SqlColumns = $sqlColumns } }
} $ObjectType $IncludeSystemObjects $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";

    // PS: the process block VERBATIM. Edits: -FunctionName Find-DbaObject on the one Stop-Function and
    // five Write-Message calls. The two constant query strings are restored from the carried state; the
    // block emits no sentinel (it never mutates the state).
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $ExcludeDatabase, $Pattern, $IncludeColumns, $IncludeSystemDatabases, $EnableException, $__state, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [object[]]$Database, [object[]]$ExcludeDatabase, [string]$Pattern, $IncludeColumns, $IncludeSystemDatabases, $EnableException, $__state, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    $sqlObjects = $__state.SqlObjects
    $sqlColumns = $__state.SqlColumns

        foreach ($instance in $SqlInstance) {
            try {
                $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
            } catch {
                Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -FunctionName Find-DbaObject -Continue
            }

            if ($server.versionMajor -lt 9) {
                Write-Message -Level Warning -Message "This command only supports SQL Server 2005 and above." -FunctionName Find-DbaObject -ModuleName "dbatools"
                Continue
            }

            if ($IncludeSystemDatabases) {
                $dbs = $server.Databases | Where-Object { $_.Status -eq "normal" }
            } else {
                $dbs = $server.Databases | Where-Object { $_.Status -eq "normal" -and $_.IsSystemObject -eq $false }
            }

            if ($Database) {
                $dbs = $dbs | Where-Object Name -In $Database
            }

            if ($ExcludeDatabase) {
                $dbs = $dbs | Where-Object Name -NotIn $ExcludeDatabase
            }

            foreach ($db in $dbs) {
                Write-Message -Level Verbose -Message "Searching object names in database $db on $instance" -FunctionName Find-DbaObject -ModuleName "dbatools"

                Write-Message -Level Debug -Message $sqlObjects -FunctionName Find-DbaObject -ModuleName "dbatools"
                $objectRows = $db.ExecuteWithResults($sqlObjects).Tables.Rows

                foreach ($row in $objectRows) {
                    if ($row.ObjectName -match $Pattern) {
                        [PSCustomObject]@{
                            ComputerName = $server.ComputerName
                            SqlInstance  = $server.ServiceName
                            Database     = $db.Name
                            Schema       = $row.SchemaName
                            Name         = $row.ObjectName
                            ObjectType   = $row.ObjectType
                            MatchType    = "ObjectName"
                            ColumnName   = $null
                            CreateDate   = $row.CreateDate
                            LastModified = $row.LastModified
                        }
                    }
                }

                if ($IncludeColumns) {
                    Write-Message -Level Verbose -Message "Searching column names in database $db on $instance" -FunctionName Find-DbaObject -ModuleName "dbatools"

                    Write-Message -Level Debug -Message $sqlColumns -FunctionName Find-DbaObject -ModuleName "dbatools"
                    $columnRows = $db.ExecuteWithResults($sqlColumns).Tables.Rows

                    foreach ($row in $columnRows) {
                        if ($row.ColumnName -match $Pattern) {
                            [PSCustomObject]@{
                                ComputerName = $server.ComputerName
                                SqlInstance  = $server.ServiceName
                                Database     = $db.Name
                                Schema       = $row.SchemaName
                                Name         = $row.ObjectName
                                ObjectType   = $row.ObjectType
                                MatchType    = "ColumnName"
                                ColumnName   = $row.ColumnName
                                CreateDate   = $row.CreateDate
                                LastModified = $row.LastModified
                            }
                        }
                    }
                }
            }
        }
} $SqlInstance $SqlCredential $Database $ExcludeDatabase $Pattern $IncludeColumns $IncludeSystemDatabases $EnableException $__state $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
