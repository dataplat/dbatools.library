#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Searches trigger definitions (server-level and database/object-level) for a regex pattern across
/// instances and databases, returning matching triggers with per-line match context. Port of
/// public/Find-DbaTrigger.ps1; the workflow remains a module-scoped PowerShell compatibility hop.
///
/// A 3-hop begin+process+end port (SqlInstance is ValueFromPipeline, so process fires per record). begin
/// builds two constant queries ($sqlDatabaseTriggers and $sqlTableTriggers, the latter with the
/// is_ms_shipped filter unless IncludeSystemObjects), the $eol separator, and seeds $everyserverstcount = 0.
/// process, per instance, searches server-level triggers (unless -Database) and then four database/object
/// sections, emitting a PSCustomObject per matching trigger. end writes the grand total. Two counters are
/// carried across records: $everyserverstcount (never reset, read in end) and $triggercount (reset at the
/// start of each database section but NOT before the server-level section, so the server-level verbose
/// message reflects the value carried from the previous record - a source quirk preserved verbatim).
/// $totalcount is reset per record and stays local. The three constants plus the two counters ride a
/// sentinel (_state) carried begin->process(xN)->end: begin seeds
/// { SqlDatabaseTriggers; SqlTableTriggers; Eol; EveryServerStCount = 0; TriggerCount = $null }, each
/// process record restores them, accumulates the two counters, and re-emits, and end restores
/// $everyserverstcount. Body edits: -FunctionName Find-DbaTrigger on process's Stop-Function and thirteen
/// Write-Message and end's Write-Message (begin has none). No ShouldProcess, no early return (the version
/// Continue is a loop keyword), no interrupt (the one Stop-Function is -Continue). Surface pinned by
/// migration/baselines/Find-DbaTrigger.json (positions 0-5, TriggerLevel ValidateSet, no ShouldProcess).
/// </summary>
[Cmdlet(VerbsCommon.Find, "DbaTrigger")]
public sealed class FindDbaTriggerCommand : DbaBaseCmdlet
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

    /// <summary>The regular-expression pattern to match against trigger definitions.</summary>
    [Parameter(Mandatory = true, Position = 4)]
    [PsStringCast]
    public string Pattern { get; set; } = null!;

    /// <summary>Which trigger level(s) to search.</summary>
    [Parameter(Position = 5)]
    [ValidateSet("All", "Server", "Database", "Object")]
    [PsStringCast]
    public string TriggerLevel { get; set; } = "All";

    /// <summary>Include system triggers in the search.</summary>
    [Parameter]
    public SwitchParameter IncludeSystemObjects { get; set; }

    /// <summary>Include system databases in the search.</summary>
    [Parameter]
    public SwitchParameter IncludeSystemDatabases { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // The two constant queries + $eol plus the two cross-record counters, carried begin->process(xN)->end.
    private Hashtable? _state;

    protected override void BeginProcessing()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BeginScript,
            IncludeSystemObjects.ToBool(), EnableException.ToBool(),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (item?.BaseObject is Hashtable sentinel && sentinel.ContainsKey("__findDbaTriggerBegin"))
            {
                _state = sentinel["__findDbaTriggerBegin"] as Hashtable;
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
            SqlInstance, SqlCredential, Database, ExcludeDatabase, Pattern, TriggerLevel,
            IncludeSystemDatabases.ToBool(), EnableException.ToBool(), _state,
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (item?.BaseObject is Hashtable sentinel && sentinel.ContainsKey("__findDbaTriggerProcess"))
            {
                if (sentinel["__findDbaTriggerProcess"] is Hashtable state)
                {
                    _state = state["State"] as Hashtable ?? _state;
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

    protected override void EndProcessing()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, EndScript,
            _state, EnableException.ToBool(), BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
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
    // PS: the begin block VERBATIM (builds the two queries, $eol, and seeds the counter) plus a sentinel
    // carrying them to the process hop. begin has no Stop-Function/Write-Message.
    private const string BeginScript = """
param($IncludeSystemObjects, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param($IncludeSystemObjects, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        $sqlDatabaseTriggers = "SELECT tr.name, m.definition AS TextBody FROM sys.sql_modules m, sys.triggers tr WHERE m.object_id = tr.object_id AND tr.parent_class = 0"

        $sqlTableTriggers = "SELECT OBJECT_SCHEMA_NAME(tr.parent_id) TableSchema, OBJECT_NAME(tr.parent_id) AS TableName, tr.name, m.definition AS TextBody FROM sys.sql_modules m, sys.triggers tr WHERE m.object_id = tr.object_id AND tr.parent_class = 1"
        if (!$IncludeSystemObjects) { $sqlTableTriggers = "$sqlTableTriggers AND tr.is_ms_shipped = 0" }

        $everyserverstcount = 0

        $eol = [System.Environment]::NewLine

    @{ __findDbaTriggerBegin = @{ SqlDatabaseTriggers = $sqlDatabaseTriggers; SqlTableTriggers = $sqlTableTriggers; Eol = $eol; EveryServerStCount = $everyserverstcount; TriggerCount = $null } }
} $IncludeSystemObjects $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
    // PS: the process block VERBATIM. Edits: -FunctionName on the Stop-Function and thirteen Write-Message.
    // The two constant queries, $eol, and the two counters are restored from the carried state; the two
    // counters are accumulated and re-emitted so they persist record-to-record and into end.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $ExcludeDatabase, $Pattern, $TriggerLevel, $IncludeSystemDatabases, $EnableException, $__state, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [object[]]$Database, [object[]]$ExcludeDatabase, [string]$Pattern, [string]$TriggerLevel, $IncludeSystemDatabases, $EnableException, $__state, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    $sqlDatabaseTriggers = $__state.SqlDatabaseTriggers
    $sqlTableTriggers = $__state.SqlTableTriggers
    $eol = $__state.Eol
    $everyserverstcount = $__state.EveryServerStCount
    $triggercount = $__state.TriggerCount

        foreach ($instance in $SqlInstance) {
            try {
                $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
            } catch {
                Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Find-DbaTrigger
            }

            if ($server.versionMajor -lt 9) {
                Write-Message -Level Warning -Message "This command only supports SQL Server 2005 and above." -FunctionName Find-DbaTrigger
                Continue
            }

            #search at instance level. Only if no database was specified
            if ((-Not $Database) -and ($TriggerLevel -in @('All', 'Server'))) {
                foreach ($trigger in $server.Triggers) {
                    $everyserverstcount++; $triggercount++
                    Write-Message -Level Debug -Message "Looking in Trigger: $trigger TextBody for $pattern" -FunctionName Find-DbaTrigger
                    if ($trigger.TextBody -match $Pattern) {

                        $triggerText = $trigger.TextBody.split($eol)
                        $trTextFound = $triggerText | Select-String -Pattern $Pattern | ForEach-Object { "(LineNumber: $($_.LineNumber)) $($_.ToString().Trim())" }

                        [PSCustomObject]@{
                            ComputerName     = $server.ComputerName
                            SqlInstance      = $server.ServiceName
                            TriggerLevel     = "Server"
                            Database         = $null
                            DatabaseId       = $null
                            Object           = $null
                            Name             = $trigger.Name
                            IsSystemObject   = $trigger.IsSystemObject
                            CreateDate       = $trigger.CreateDate
                            LastModified     = $trigger.DateLastModified
                            TriggerTextFound = $trTextFound -join "`n"
                            Trigger          = $trigger
                            TriggerFullText  = $trigger.TextBody
                        } | Select-DefaultView -ExcludeProperty Trigger, TriggerFullText
                    }
                }
                Write-Message -Level Verbose -Message "Evaluated $triggercount triggers in $server" -FunctionName Find-DbaTrigger
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

            $totalcount = 0
            $dbcount = $dbs.count

            if ($TriggerLevel -in @('All', 'Database', 'Object')) {
                foreach ($db in $dbs) {

                    Write-Message -Level Verbose -Message "Searching on database $db" -FunctionName Find-DbaTrigger

                    # If system objects aren't needed, find trigger text using SQL
                    # This prevents SMO from having to enumerate

                    if (!$IncludeSystemObjects) {
                        if ($TriggerLevel -in @('All', 'Database')) {
                            #Get Database Level triggers (DDL)
                            Write-Message -Level Debug -Message $sqlDatabaseTriggers -FunctionName Find-DbaTrigger
                            $rows = $db.ExecuteWithResults($sqlDatabaseTriggers).Tables.Rows
                            $triggercount = 0

                            foreach ($row in $rows) {
                                $totalcount++; $triggercount++; $everyserverstcount++

                                $trigger = $row.name

                                Write-Message -Level Verbose -Message "Looking in trigger $trigger for textBody with pattern $pattern on database $db" -FunctionName Find-DbaTrigger
                                if ($row.TextBody -match $Pattern) {
                                    $tr = $db.Triggers | Where-Object name -eq $row.name

                                    $triggerText = $tr.TextBody.split($eol)
                                    $trTextFound = $triggerText | Select-String -Pattern $Pattern | ForEach-Object { "(LineNumber: $($_.LineNumber)) $($_.ToString().Trim())" }

                                    [PSCustomObject]@{
                                        ComputerName     = $server.ComputerName
                                        SqlInstance      = $server.ServiceName
                                        TriggerLevel     = "Database"
                                        Database         = $db.name
                                        DatabaseId       = $db.ID
                                        Object           = $tr.Parent
                                        Name             = $tr.Name
                                        IsSystemObject   = $tr.IsSystemObject
                                        CreateDate       = $tr.CreateDate
                                        LastModified     = $tr.DateLastModified
                                        TriggerTextFound = $trTextFound -join "`n"
                                        Trigger          = $tr
                                        TriggerFullText  = $tr.TextBody
                                    } | Select-DefaultView -ExcludeProperty Trigger, TriggerFullText
                                }
                            }
                        }

                        if ($TriggerLevel -in @('All', 'Object')) {
                            #Get Object Level triggers (DML)
                            Write-Message -Level Debug -Message $sqlTableTriggers -FunctionName Find-DbaTrigger
                            $rows = $db.ExecuteWithResults($sqlTableTriggers).Tables.Rows
                            $triggercount = 0

                            foreach ($row in $rows) {
                                $totalcount++; $triggercount++; $everyserverstcount++

                                $trigger = $row.name
                                $triggerParentSchema = $row.TableSchema
                                $triggerParent = $row.TableName

                                Write-Message -Level Verbose -Message "Looking in trigger $trigger for textBody with pattern $pattern in object $triggerParentSchema.$triggerParent at database $db" -FunctionName Find-DbaTrigger
                                if ($row.TextBody -match $Pattern) {

                                    $tr = ($db.Tables | Where-Object { $_.Name -eq $triggerParent -and $_.Schema -eq $triggerParentSchema }).Triggers | Where-Object name -eq $row.name
                                    if ($null -eq $tr) {
                                        Write-Message -Level Verbose -Message "Could not find table named $($row.Name). Will try to find on Views." -FunctionName Find-DbaTrigger
                                        $tr = ($db.Views | Where-Object { $_.Name -eq $triggerParent -and $_.Schema -eq $triggerParentSchema }).Triggers | Where-Object name -eq $row.name
                                    }

                                    $triggerText = $tr.TextBody.split($eol)
                                    $trTextFound = $triggerText | Select-String -Pattern $Pattern | ForEach-Object { "(LineNumber: $($_.LineNumber)) $($_.ToString().Trim())" }

                                    [PSCustomObject]@{
                                        ComputerName     = $server.ComputerName
                                        SqlInstance      = $server.ServiceName
                                        TriggerLevel     = "Object"
                                        Database         = $db.name
                                        DatabaseId       = $db.ID
                                        Object           = $tr.Parent
                                        Name             = $tr.Name
                                        IsSystemObject   = $tr.IsSystemObject
                                        CreateDate       = $tr.CreateDate
                                        LastModified     = $tr.DateLastModified
                                        TriggerTextFound = $trTextFound -join "`n"
                                        Trigger          = $tr
                                        TriggerFullText  = $tr.TextBody
                                    } | Select-DefaultView -ExcludeProperty Trigger, TriggerFullText
                                }
                            }
                        }
                    } else {
                        if ($TriggerLevel -in @('All', 'Database')) {
                            #Get Database Level triggers (DDL)
                            $triggers = $db.Triggers

                            $triggercount = 0

                            foreach ($tr in $triggers) {
                                $totalcount++; $triggercount++; $everyserverstcount++
                                $trigger = $tr.Name

                                Write-Message -Level Verbose -Message "Looking in trigger $trigger for textBody with pattern $pattern on database $db" -FunctionName Find-DbaTrigger
                                if ($tr.TextBody -match $Pattern) {

                                    $triggerText = $tr.TextBody.split($eol)
                                    $trTextFound = $triggerText | Select-String -Pattern $Pattern | ForEach-Object { "(LineNumber: $($_.LineNumber)) $($_.ToString().Trim())" }

                                    [PSCustomObject]@{
                                        ComputerName     = $server.ComputerName
                                        SqlInstance      = $server.ServiceName
                                        TriggerLevel     = "Database"
                                        Database         = $db.name
                                        DatabaseId       = $db.ID
                                        Object           = $tr.Parent
                                        Name             = $tr.Name
                                        IsSystemObject   = $tr.IsSystemObject
                                        CreateDate       = $tr.CreateDate
                                        LastModified     = $tr.DateLastModified
                                        TriggerTextFound = $trTextFound -join "`n"
                                        Trigger          = $tr
                                        TriggerFullText  = $tr.TextBody
                                    } | Select-DefaultView -ExcludeProperty Trigger, TriggerFullText
                                }
                            }
                        }

                        if ($TriggerLevel -in @('All', 'Object')) {
                            #Get Object Level triggers (DML)
                            $triggers = $db.Tables | ForEach-Object { $_.Triggers }
                            $triggers += $db.Views | ForEach-Object { $_.Triggers }

                            $triggercount = 0

                            foreach ($tr in $triggers) {
                                $totalcount++; $triggercount++; $everyserverstcount++
                                $trigger = $tr.Name

                                Write-Message -Level Verbose -Message "Looking in trigger $trigger for textBody with pattern $pattern in object $($tr.Parent) at database $db" -FunctionName Find-DbaTrigger
                                if ($tr.TextBody -match $Pattern) {

                                    $triggerText = $tr.TextBody.split($eol)
                                    $trTextFound = $triggerText | Select-String -Pattern $Pattern | ForEach-Object { "(LineNumber: $($_.LineNumber)) $($_.ToString().Trim())" }

                                    [PSCustomObject]@{
                                        ComputerName     = $server.ComputerName
                                        SqlInstance      = $server.ServiceName
                                        TriggerLevel     = "Object"
                                        Database         = $db.name
                                        DatabaseId       = $db.ID
                                        Object           = $tr.Parent
                                        Name             = $tr.Name
                                        IsSystemObject   = $tr.IsSystemObject
                                        CreateDate       = $tr.CreateDate
                                        LastModified     = $tr.DateLastModified
                                        TriggerTextFound = $trTextFound -join "`n"
                                        Trigger          = $tr
                                        TriggerFullText  = $tr.TextBody
                                    } | Select-DefaultView -ExcludeProperty Trigger, TriggerFullText
                                }
                            }
                        }
                    }
                    Write-Message -Level Verbose -Message "Evaluated $triggercount triggers in $db" -FunctionName Find-DbaTrigger
                }
            }
            Write-Message -Level Verbose -Message "Evaluated $totalcount total triggers in $dbcount databases" -FunctionName Find-DbaTrigger
        }

    $__state.EveryServerStCount = $everyserverstcount
    $__state.TriggerCount = $triggercount
    @{ __findDbaTriggerProcess = @{ State = $__state } }
} $SqlInstance $SqlCredential $Database $ExcludeDatabase $Pattern $TriggerLevel $IncludeSystemDatabases $EnableException $__state $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
    // PS: the end block VERBATIM. Edit: -FunctionName on the Write-Message. $everyserverstcount is
    // restored from the carried state so the grand total reflects all records.
    private const string EndScript = """
param($__state, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param($__state, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    $everyserverstcount = $__state.EveryServerStCount

        Write-Message -Level Verbose -Message "Evaluated $everyserverstcount total triggers" -FunctionName Find-DbaTrigger
} $__state $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}