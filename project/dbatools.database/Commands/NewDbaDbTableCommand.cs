#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Creates a table in one or more databases. Port of public/New-DbaDbTable.ps1 (631 lines); the
/// workflow remains a module-scoped PowerShell compatibility hop.
///
/// PROCESS-ONLY, one hop. $InputObject is ValueFromPipeline, so process fires per piped database.
/// This row has the LARGEST parameter surface in the satellite: 58 parameters, 40 positional, 18
/// switches, a dozen SMO enum types, and Alias("Table") on -Name.
///
/// THE DEFINING CLASS HERE IS $PSBoundParameters PROJECTION, and it is the fail-dangerous variant.
/// Source :512 iterates the CALLER's bound parameters and assigns them straight onto the SMO Table:
///
///     foreach ($param in $PSBoundParameters.Keys) {
///         if ($param -notin $excludeParams) { $object.$param = $PSBoundParameters[$param] }
///     }
///
/// Inside a hop, $PSBoundParameters is the HOP SCRIPTBLOCK's own bindings - it would contain the
/// plumbing ($__realCmdlet, $__boundWhatIf, the boundness flags) and every parameter passed
/// positionally as $null for properties the user never supplied. A naive hop would therefore null
/// out dozens of real SMO properties and then hard-fail on "$object.__realCmdlet". The C# side
/// passes MyInvocation.BoundParameters - the caller's REAL table - and the hop preamble substitutes
/// it for $PSBoundParameters before the dot-sourced body, so the body sees exactly what the source
/// sees. Same remedy as Invoke-DbaDbTransfer (W2-136), which met this class first.
///
/// NOTE the severity split between the two variants, because it decides how much to trust a clean
/// review: W2-136 filters with an ALLOW list ($key -in $newTransferParams), so stray hop keys are
/// harmlessly dropped and a naive port can still appear to work. This row filters with an EXCLUDE
/// list, so every stray key is actively assigned. Allow-list is fail-safe; exclude-list is
/// fail-dangerous. Do not generalise a clean result from one to the other.
///
/// ONLY 10 PARAMETERS CROSS THE HOP, NOT 58. The process body references just nine parameters BY
/// NAME (SqlInstance, SqlCredential, Database, Name, Schema, ColumnMap, ColumnObject, Passthru,
/// InputObject) - verified by AST, not by reading. The other 49 exist solely to be projected, and
/// they reach the body through the carried bound-parameter table instead. Passing all 58 would add
/// 48 needless DEF-005 positions to keep aligned for no behavioural gain.
///
/// $EnableException is the tenth, and it is NOT optional even though the AST reports it unreferenced:
/// Stop-Function's -EnableException parameter DEFAULTS to $EnableException read from its CALLER's
/// scope, so if the hop does not define it, argument transformation throws ("Cannot convert value
/// \"\" to type System.Boolean") before Stop-Function ever runs. Measured the hard way - that exact
/// failure produced a false FAIL in migration/logs/probe-20260718-latch-sentinel.
///
/// THE 58 C# PROPERTIES ARE GENERATED FROM THE BASELINE, not transcribed, with defaults read from
/// the source param block (the baseline does not record defaults - the W2-141 lesson, where reading
/// a position dump lost a type and three defaults). At 58 parameters, hand-typing is where the
/// errors live.
///
/// FIVE Test-Bound SITES BECOME CARRIED CALLER-BOUNDNESS FLAGS: SqlInstance and Database and Name
/// (:465-466), Name again (:473), and Schema (:480). The Schema flag is load-bearing rather than
/// cosmetic: :480 lets a parsed two-part -Name like "[dbo].[t1]" override -Schema ONLY when the
/// caller did not pass -Schema explicitly, so a value test would silently let the parsed schema win
/// over an explicit one.
///
/// NO INTERRUPT BRIDGE: the guards at :467 and :476 and :483 are non-Continue and do set the latch,
/// but this source has NO Test-FunctionInterrupt to read it back, so they re-warn per record.
/// NO CROSS-RECORD CARRY: :490 "$InputObject +=" targets the pipeline-bound parameter, and
/// Find-AccumulatorCarry.ps1 reports only $commonParams (:507), which is reset in-block.
/// NO preference assignment (checked with the unanchored pattern) and NO .IsPresent sites.
///
/// The 18 switches cross as SwitchParameter OBJECTS received untyped per B's combined rule; only
/// -Passthru is read by name in the body, but the projection reads the rest out of the bound table
/// where their SwitchParameter-ness is what the source would have assigned to the SMO property.
///
/// STREAMING, NOT BUFFERED (DEF-001): tables are created per database and -Passthru emits each one,
/// so a buffered hop would discard the record of tables already created when a later database's
/// failure terminated the hop under -EnableException.
///
/// The one $Pscmdlet.ShouldProcess gate at :496 routes to the real cmdlet via $__realCmdlet. In-hop
/// Stop-Function/Write-Message calls carry -FunctionName. Positions 0-39 are made explicit and were
/// confirmed against the exported baseline. Surface pinned by migration/baselines/New-DbaDbTable.json.
/// </summary>
[Cmdlet(VerbsCommon.New, "DbaDbTable", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low)]
public sealed class NewDbaDbTableCommand : DbaBaseCmdlet
{
    /// <summary>SqlInstance.</summary>
    [Parameter(Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>SqlCredential.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Database.</summary>
    [Parameter(Position = 2)]
    [PsStringArrayCast]
    public string[]? Database { get; set; }

    /// <summary>Name.</summary>
    [Parameter(Position = 3)]
    [Alias("Table")]
    [PsStringCast]
    public string? Name { get; set; }

    /// <summary>Schema.</summary>
    [Parameter(Position = 4)]
    [PsStringCast]
    public string Schema { get; set; } = "dbo";

    /// <summary>ColumnMap.</summary>
    [Parameter(Position = 5)]
    public Hashtable[]? ColumnMap { get; set; }

    /// <summary>ColumnObject.</summary>
    [Parameter(Position = 6)]
    public Microsoft.SqlServer.Management.Smo.Column[]? ColumnObject { get; set; }

    /// <summary>AnsiNullsStatus.</summary>
    [Parameter]
    public SwitchParameter AnsiNullsStatus { get; set; }

    /// <summary>ChangeTrackingEnabled.</summary>
    [Parameter]
    public SwitchParameter ChangeTrackingEnabled { get; set; }

    /// <summary>DataSourceName.</summary>
    [Parameter(Position = 7)]
    [PsStringCast]
    public string? DataSourceName { get; set; }

    /// <summary>Durability.</summary>
    [Parameter(Position = 8)]
    public Microsoft.SqlServer.Management.Smo.DurabilityType Durability { get; set; }

    /// <summary>ExternalTableDistribution.</summary>
    [Parameter(Position = 9)]
    public Microsoft.SqlServer.Management.Smo.ExternalTableDistributionType ExternalTableDistribution { get; set; }

    /// <summary>FileFormatName.</summary>
    [Parameter(Position = 10)]
    [PsStringCast]
    public string? FileFormatName { get; set; }

    /// <summary>FileGroup.</summary>
    [Parameter(Position = 11)]
    [PsStringCast]
    public string? FileGroup { get; set; }

    /// <summary>FileStreamFileGroup.</summary>
    [Parameter(Position = 12)]
    [PsStringCast]
    public string? FileStreamFileGroup { get; set; }

    /// <summary>FileStreamPartitionScheme.</summary>
    [Parameter(Position = 13)]
    [PsStringCast]
    public string? FileStreamPartitionScheme { get; set; }

    /// <summary>FileTableDirectoryName.</summary>
    [Parameter(Position = 14)]
    [PsStringCast]
    public string? FileTableDirectoryName { get; set; }

    /// <summary>FileTableNameColumnCollation.</summary>
    [Parameter(Position = 15)]
    [PsStringCast]
    public string? FileTableNameColumnCollation { get; set; }

    /// <summary>FileTableNamespaceEnabled.</summary>
    [Parameter]
    public SwitchParameter FileTableNamespaceEnabled { get; set; }

    /// <summary>HistoryTableName.</summary>
    [Parameter(Position = 16)]
    [PsStringCast]
    public string? HistoryTableName { get; set; }

    /// <summary>HistoryTableSchema.</summary>
    [Parameter(Position = 17)]
    [PsStringCast]
    public string? HistoryTableSchema { get; set; }

    /// <summary>IsExternal.</summary>
    [Parameter]
    public SwitchParameter IsExternal { get; set; }

    /// <summary>IsFileTable.</summary>
    [Parameter]
    public SwitchParameter IsFileTable { get; set; }

    /// <summary>IsMemoryOptimized.</summary>
    [Parameter]
    public SwitchParameter IsMemoryOptimized { get; set; }

    /// <summary>IsSystemVersioned.</summary>
    [Parameter]
    public SwitchParameter IsSystemVersioned { get; set; }

    /// <summary>Location.</summary>
    [Parameter(Position = 18)]
    [PsStringCast]
    public string? Location { get; set; }

    /// <summary>LockEscalation.</summary>
    [Parameter(Position = 19)]
    public Microsoft.SqlServer.Management.Smo.LockEscalationType LockEscalation { get; set; }

    /// <summary>Owner.</summary>
    [Parameter(Position = 20)]
    [PsStringCast]
    public string? Owner { get; set; }

    /// <summary>PartitionScheme.</summary>
    [Parameter(Position = 21)]
    [PsStringCast]
    public string? PartitionScheme { get; set; }

    /// <summary>QuotedIdentifierStatus.</summary>
    [Parameter]
    public SwitchParameter QuotedIdentifierStatus { get; set; }

    /// <summary>RejectSampleValue.</summary>
    [Parameter(Position = 22)]
    public double RejectSampleValue { get; set; }

    /// <summary>RejectType.</summary>
    [Parameter(Position = 23)]
    public Microsoft.SqlServer.Management.Smo.ExternalTableRejectType RejectType { get; set; }

    /// <summary>RejectValue.</summary>
    [Parameter(Position = 24)]
    public double RejectValue { get; set; }

    /// <summary>RemoteDataArchiveDataMigrationState.</summary>
    [Parameter(Position = 25)]
    public Microsoft.SqlServer.Management.Smo.RemoteDataArchiveMigrationState RemoteDataArchiveDataMigrationState { get; set; }

    /// <summary>RemoteDataArchiveEnabled.</summary>
    [Parameter]
    public SwitchParameter RemoteDataArchiveEnabled { get; set; }

    /// <summary>RemoteDataArchiveFilterPredicate.</summary>
    [Parameter(Position = 26)]
    [PsStringCast]
    public string? RemoteDataArchiveFilterPredicate { get; set; }

    /// <summary>RemoteObjectName.</summary>
    [Parameter(Position = 27)]
    [PsStringCast]
    public string? RemoteObjectName { get; set; }

    /// <summary>RemoteSchemaName.</summary>
    [Parameter(Position = 28)]
    [PsStringCast]
    public string? RemoteSchemaName { get; set; }

    /// <summary>RemoteTableName.</summary>
    [Parameter(Position = 29)]
    [PsStringCast]
    public string? RemoteTableName { get; set; }

    /// <summary>RemoteTableProvisioned.</summary>
    [Parameter]
    public SwitchParameter RemoteTableProvisioned { get; set; }

    /// <summary>ShardingColumnName.</summary>
    [Parameter(Position = 30)]
    [PsStringCast]
    public string? ShardingColumnName { get; set; }

    /// <summary>TextFileGroup.</summary>
    [Parameter(Position = 31)]
    [PsStringCast]
    public string? TextFileGroup { get; set; }

    /// <summary>TrackColumnsUpdatedEnabled.</summary>
    [Parameter]
    public SwitchParameter TrackColumnsUpdatedEnabled { get; set; }

    /// <summary>HistoryRetentionPeriod.</summary>
    [Parameter(Position = 32)]
    public int HistoryRetentionPeriod { get; set; }

    /// <summary>HistoryRetentionPeriodUnit.</summary>
    [Parameter(Position = 33)]
    public Microsoft.SqlServer.Management.Smo.TemporalHistoryRetentionPeriodUnit HistoryRetentionPeriodUnit { get; set; }

    /// <summary>DwTableDistribution.</summary>
    [Parameter(Position = 34)]
    public Microsoft.SqlServer.Management.Smo.DwTableDistributionType DwTableDistribution { get; set; }

    /// <summary>RejectedRowLocation.</summary>
    [Parameter(Position = 35)]
    [PsStringCast]
    public string? RejectedRowLocation { get; set; }

    /// <summary>OnlineHeapOperation.</summary>
    [Parameter]
    public SwitchParameter OnlineHeapOperation { get; set; }

    /// <summary>LowPriorityMaxDuration.</summary>
    [Parameter(Position = 36)]
    public int LowPriorityMaxDuration { get; set; }

    /// <summary>DataConsistencyCheck.</summary>
    [Parameter]
    public SwitchParameter DataConsistencyCheck { get; set; }

    /// <summary>LowPriorityAbortAfterWait.</summary>
    [Parameter(Position = 37)]
    public Microsoft.SqlServer.Management.Smo.AbortAfterWait LowPriorityAbortAfterWait { get; set; }

    /// <summary>MaximumDegreeOfParallelism.</summary>
    [Parameter(Position = 38)]
    public int MaximumDegreeOfParallelism { get; set; }

    /// <summary>IsNode.</summary>
    [Parameter]
    public SwitchParameter IsNode { get; set; }

    /// <summary>IsEdge.</summary>
    [Parameter]
    public SwitchParameter IsEdge { get; set; }

    /// <summary>IsVarDecimalStorageFormatEnabled.</summary>
    [Parameter]
    public SwitchParameter IsVarDecimalStorageFormatEnabled { get; set; }

    /// <summary>Passthru.</summary>
    [Parameter]
    public SwitchParameter Passthru { get; set; }

    /// <summary>InputObject.</summary>
    [Parameter(ValueFromPipeline = true, Position = 39)]
    public Microsoft.SqlServer.Management.Smo.Database[]? InputObject { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // $object / $schemaObject carried across records for the catch-cleanup failure path (codex r1):
    // if the try throws before those assignments, the SOURCE's persistent process scope still holds
    // the PREVIOUS record's objects and the cleanup drops them. Opaque to C#.
    private Hashtable? _state;

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        // The CALLER's real bound parameters. The body projects these onto the SMO Table, so it must
        // see what the user typed - not the hop scriptblock's own bindings. Copied into a plain
        // Hashtable so the hop indexes it exactly like the automatic variable it replaces.
        Hashtable boundParameters = new Hashtable(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.Generic.KeyValuePair<string, object> kv in MyInvocation.BoundParameters)
        {
            boundParameters[kv.Key] = kv.Value;
        }

        // Streaming, not buffered (DEF-001): tables are created per database and -Passthru emits
        // each one, so a buffered hop would drop the audit trail of tables already created.
        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is Hashtable sentinel && sentinel.ContainsKey("__newDbaDbTableProcess"))
            {
                _state = sentinel["__newDbaDbTableProcess"] as Hashtable;
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
            SqlInstance, SqlCredential, Database, Name, Schema, ColumnMap, ColumnObject,
            Passthru, InputObject, EnableException, boundParameters, _state,
            MyInvocation.BoundParameters.ContainsKey("SqlInstance"),
            MyInvocation.BoundParameters.ContainsKey("Database"),
            MyInvocation.BoundParameters.ContainsKey("Name"),
            MyInvocation.BoundParameters.ContainsKey("Schema"),
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

    // PS: the process block VERBATIM, dot-sourced so its three early returns exit only the body.
    // Edits: five Test-Bound probes become carried boundness flags, the one $Pscmdlet gate routes to
    // $__realCmdlet, and -FunctionName is stamped on the message calls.
    //
    // THE PREAMBLE REPLACES $PSBoundParameters with the CALLER's table. Source :512 iterates it and
    // assigns each key onto the SMO Table; the hop's own $PSBoundParameters would carry the plumbing
    // and 49 null placeholders, nulling real properties and then failing on $object.__realCmdlet.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $Name, $Schema, $ColumnMap, $ColumnObject, $Passthru, $InputObject, $EnableException, $__boundParameters, $__state, $__boundSqlInstance, $__boundDatabase, $__boundName, $__boundSchema, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = "Low")]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [String[]]$Database, [String]$Name, [String]$Schema, [hashtable[]]$ColumnMap, [Microsoft.SqlServer.Management.Smo.Column[]]$ColumnObject, $Passthru, [Microsoft.SqlServer.Management.Smo.Database[]]$InputObject, $EnableException, $__boundParameters, $__state, $__boundSqlInstance, $__boundDatabase, $__boundName, $__boundSchema, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    # the body projects the CALLER's bound parameters onto the SMO Table (source :512); the hop's own
    # automatic $PSBoundParameters would carry plumbing and null placeholders instead
    $PSBoundParameters = $__boundParameters

    # FAILURE-PATH CROSS-RECORD CARRY (codex r1). $object (:503) and $schemaObject (:586) are read by
    # the catch cleanup at :617-626, which calls .Drop() on them. If the try throws BEFORE the
    # assignment - New-Object Smo.Table at :503 is the obvious case - the source still holds the
    # PREVIOUS record's objects in its persistent process scope, so the cleanup drops the previously
    # created table and schema. The hop's fresh scope would instead see $null and merely warn.
    # Carried with Assigned flags so a first record leaves them genuinely undefined, as the source does.
    if ($null -ne $__state -and $__state.ObjectAssigned) { $object = $__state.Object }
    if ($null -ne $__state -and $__state.SchemaObjectAssigned) { $schemaObject = $__state.SchemaObject }

    . {
        if (($__boundSqlInstance)) {
            if ((-not $__boundDatabase) -or (-not $__boundName)) {
                Stop-Function -Message "You must specify one or more databases and one Name when using the SqlInstance parameter." -FunctionName New-DbaDbTable
                return
            }
        }

        # Parse the Name parameter to handle bracket-quoted names and two-part names like [schema].[table]
        if ($__boundName) {
            $parsedName = Get-ObjectNameParts -ObjectName $Name
            if ($parsedName.Parsed) {
                if ($parsedName.Database) {
                    Stop-Function -Message "The -Name parameter only accepts one- or two-part names. Specify the database separately with -Database or by piping in a database object." -FunctionName New-DbaDbTable
                    return
                }
                if ($parsedName.Schema -and -not ($__boundSchema)) {
                    $Schema = $parsedName.Schema
                }
                $Name = $parsedName.Name
            } else {
                Stop-Function -Message "Could not parse -Name '$Name' as a valid object name." -FunctionName New-DbaDbTable
                return
            }
        }

        foreach ($instance in $SqlInstance) {
            $InputObject += Get-DbaDatabase -SqlInstance $instance -SqlCredential $SqlCredential -Database $Database
        }

        foreach ($db in $InputObject) {
            $server = $db.Parent
            if ($__realCmdlet.ShouldProcess("Creating new table [$Schema].[$Name] in $db on $server")) {
                # Test if table already exists. This ways we can drop the table if part of the creation fails.
                $existingTable = $db.tables | Where-Object { $_.Schema -eq $Schema -and $_.Name -eq $Name }
                if ($existingTable) {
                    Stop-Function -Message "Table [$Schema].[$Name] already exists in $db on $server" -Continue -FunctionName New-DbaDbTable
                }
                try {
                    $object = New-Object -TypeName Microsoft.SqlServer.Management.Smo.Table $db, $Name, $Schema

                    # Get common parameters dynamically
                    $commonParams = [System.Management.Automation.PSCmdlet]::CommonParameters
                    $commonParams += [System.Management.Automation.PSCmdlet]::OptionalCommonParameters

                    $excludeParams = @(
                        'SqlInstance', 'SqlCredential', 'Database', 'Name', 'Schema',
                        'ColumnMap', 'ColumnObject', 'InputObject', 'EnableException', 'Passthru'
                    ) + $commonParams

                    foreach ($param in $PSBoundParameters.Keys) {
                        if ($param -notin $excludeParams) {
                            # IsNode and IsEdge are only supported in SQL Server 2017+ (version 14+)
                            if ($param -in 'IsNode', 'IsEdge' -and $server.VersionMajor -lt 14) {
                                Write-Message -Level Warning -Message "Parameter $param is only supported on SQL Server 2017 and above. Current version is $($server.VersionMajor). Skipping." -FunctionName New-DbaDbTable
                                continue
                            }
                            $object.$param = $PSBoundParameters[$param]
                        }
                    }

                    foreach ($column in $ColumnObject) {
                        $object.Columns.Add($column)
                    }

                    foreach ($column in $ColumnMap) {
                        $sqlDbType = [Microsoft.SqlServer.Management.Smo.SqlDataType]$($column.Type)
                        if ($sqlDbType -in @('VarBinary', 'VarChar', 'NVarChar', 'Char', 'NChar')) {
                            if ($column.MaxLength -gt 0) {
                                $dataType = New-Object Microsoft.SqlServer.Management.Smo.DataType $sqlDbType, $column.MaxLength
                            } else {
                                $sqlDbType = [Microsoft.SqlServer.Management.Smo.SqlDataType]"$($column.Type)Max"
                                $dataType = New-Object Microsoft.SqlServer.Management.Smo.DataType $sqlDbType
                            }
                        } elseif ($sqlDbType -eq 'Decimal') {
                            if ($column.MaxLength -gt 0) {
                                $dataType = New-Object Microsoft.SqlServer.Management.Smo.DataType $sqlDbType, $column.MaxLength
                            } elseif ($column.Precision -gt 0) {
                                $dataType = New-Object Microsoft.SqlServer.Management.Smo.DataType $sqlDbType, $column.Precision, $column.Scale
                            } else {
                                $dataType = New-Object Microsoft.SqlServer.Management.Smo.DataType $sqlDbType
                            }
                        } else {
                            $dataType = New-Object Microsoft.SqlServer.Management.Smo.DataType $sqlDbType
                        }
                        $sqlColumn = New-Object Microsoft.SqlServer.Management.Smo.Column $object, $column.Name, $dataType
                        $sqlColumn.Nullable = $column.Nullable

                        if ($column.DefaultName) {
                            $dfName = $column.DefaultName
                        } else {
                            $dfName = "DF_$name`_$($column.Name)"
                        }
                        if ($column.DefaultExpression) {
                            # override the default that would add quotes to an expression
                            $sqlColumn.AddDefaultConstraint($dfName).Text = $column.DefaultExpression
                        } elseif ($column.DefaultString) {
                            # override the default that would not add quotes to a date string
                            $sqlColumn.AddDefaultConstraint($dfName).Text = "'$($column.DefaultString)'"
                        } elseif ($column.Default) {
                            if ($sqlDbType -in @('NVarchar', 'NChar', 'NVarcharMax', 'NCharMax')) {
                                $sqlColumn.AddDefaultConstraint($dfName).Text = "N'$($column.Default)'"
                            } elseif ($sqlDbType -in @('Varchar', 'Char', 'VarcharMax', 'CharMax')) {
                                $sqlColumn.AddDefaultConstraint($dfName).Text = "'$($column.Default)'"
                            } else {
                                $sqlColumn.AddDefaultConstraint($dfName).Text = $column.Default
                            }
                        }

                        if ($column.Identity) {
                            $sqlColumn.Identity = $true
                            if ($column.IdentitySeed) {
                                $sqlColumn.IdentitySeed = $column.IdentitySeed
                            }
                            if ($column.IdentityIncrement) {
                                $sqlColumn.IdentityIncrement = $column.IdentityIncrement
                            }
                        }
                        $object.Columns.Add($sqlColumn)
                    }

                    # user has specified a schema that does not exist yet
                    $schemaObject = $null
                    if (-not ($db | Get-DbaDbSchema -Schema $Schema -IncludeSystemSchemas)) {
                        Write-Message -Level Verbose -Message "Schema $Schema does not exist in $db and will be created." -FunctionName New-DbaDbTable
                        $schemaObject = New-Object -TypeName Microsoft.SqlServer.Management.Smo.Schema $db, $Schema
                    }

                    if ($Passthru) {
                        $ScriptingOptionsObject = New-DbaScriptingOption
                        $ScriptingOptionsObject.ContinueScriptingOnError = $false
                        $ScriptingOptionsObject.DriAllConstraints = $true

                        if ($schemaObject) {
                            $schemaObject.Script($ScriptingOptionsObject)
                        }

                        $object.Script($ScriptingOptionsObject)
                    } else {
                        if ($schemaObject) {
                            $null = Invoke-Create -Object $schemaObject
                        }
                        $null = Invoke-Create -Object $object
                        $db.Tables.Refresh()
                    }
                    $db | Get-DbaDbTable -Table "[$Schema].[$Name]"
                } catch {
                    $exception = $_
                    Write-Message -Level Verbose -Message "Failed to create table or failure while adding constraints. Will try to remove table (and schema)." -FunctionName New-DbaDbTable
                    try {
                        $object.Refresh()
                        if ($object.State -ne 'Dropped') {
                            $object.Drop()
                        }
                        if ($schemaObject) {
                            $schemaObject.Refresh()
                            if ($schemaObject.State -ne 'Dropped') {
                                $schemaObject.Drop()
                            }
                        }
                    } catch {
                        Write-Message -Level Warning -Message "Failed to drop table: $_. Maybe table still exists." -FunctionName New-DbaDbTable
                    }
                    Stop-Function -Message "Failure" -ErrorRecord $exception -Continue -FunctionName New-DbaDbTable
                }
            }
        }
    }

    $__ob = Get-Variable -Name object -Scope 0 -ErrorAction Ignore
    $__so = Get-Variable -Name schemaObject -Scope 0 -ErrorAction Ignore
    @{ __newDbaDbTableProcess = @{
        ObjectAssigned       = [bool]$__ob
        Object               = $(if ($__ob) { $__ob.Value } else { $null })
        SchemaObjectAssigned = [bool]$__so
        SchemaObject         = $(if ($__so) { $__so.Value } else { $null })
    } }
} $SqlInstance $SqlCredential $Database $Name $Schema $ColumnMap $ColumnObject $Passthru $InputObject $EnableException $__boundParameters $__state $__boundSqlInstance $__boundDatabase $__boundName $__boundSchema $__realCmdlet $__boundWhatIf $__boundConfirm $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}