#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves data-classification metadata (information type + sensitivity label extended properties) for
/// classified columns across databases. Port of public/Get-DbaDbDataClassification.ps1; the workflow remains
/// a module-scoped PowerShell compatibility hop.
///
/// A process-only port (InputObject is ValueFromPipeline, so process fires per record; the -SqlInstance path
/// gathers accessible databases via Get-DbaDatabase). No begin/end, no accumulator, no interrupt (both
/// Stop-Function calls are -Continue), no Test-Bound. The three `continue` statements (the Schema/Table/Column
/// row filters) sit inside `foreach ($row in $results)`, so they are normal loop continues - NOT a bare
/// continue, so no continue-guard wrapper is needed. Schema/Table/Column/Database are truthiness/value uses.
/// The only edits are -FunctionName Get-DbaDbDataClassification on the two Stop-Function calls. Surface pinned
/// by migration/baselines/Get-DbaDbDataClassification.json (positions 0-6, no sets, no ShouldProcess).
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbDataClassification")]
public sealed class GetDbaDbDataClassificationCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The database(s) to process.</summary>
    [Parameter(Position = 2)]
    [PsStringArrayCast]
    public string[]? Database { get; set; }

    /// <summary>Filter to the specified schema(s).</summary>
    [Parameter(Position = 3)]
    [PsStringArrayCast]
    public string[]? Schema { get; set; }

    /// <summary>Filter to the specified table(s).</summary>
    [Parameter(Position = 4)]
    [PsStringArrayCast]
    public string[]? Table { get; set; }

    /// <summary>Filter to the specified column(s).</summary>
    [Parameter(Position = 5)]
    [PsStringArrayCast]
    public string[]? Column { get; set; }

    /// <summary>Database object(s) piped in.</summary>
    [Parameter(ValueFromPipeline = true, Position = 6)]
    public Microsoft.SqlServer.Management.Smo.Database[]? InputObject { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                NestedCommand.RemoveDuplicateError(this, nestedError);
                WriteError(nestedError);
                return;
            }
            WriteObject(item);
        }, ProcessScript,
            SqlInstance, SqlCredential, Database, Schema, Table, Column, InputObject, EnableException.ToBool(),
            NestedCommand.BoundCommonParameter(this, "Verbose"), NestedCommand.BoundCommonParameter(this, "Debug"));
    }
    // PS: the process block VERBATIM. Edit: -FunctionName Get-DbaDbDataClassification on the two Stop-Function
    // calls. The Schema/Table/Column continue statements are inside foreach ($row) - normal loop continues.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $Schema, $Table, $Column, $InputObject, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [string[]]$Database, [string[]]$Schema, [string[]]$Table, [string[]]$Column, [Microsoft.SqlServer.Management.Smo.Database[]]$InputObject, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        if ($SqlInstance) {
            $InputObject = Get-DbaDatabase -SqlInstance $SqlInstance -SqlCredential $SqlCredential -Database $Database | Where-Object IsAccessible
        }

        foreach ($db in $InputObject) {
            $server = $db.Parent

            if ($server.VersionMajor -lt 9) {
                Stop-Function -Message "Data classification requires SQL Server 2005 or later. Skipping $server" -Target $server -Continue -FunctionName Get-DbaDbDataClassification
            }

            $sql = "
SELECT
    SCHEMA_NAME(o.schema_id) AS SchemaName,
    o.name AS TableName,
    c.name AS ColumnName,
    CAST(ep1.value AS NVARCHAR(MAX)) AS InformationTypeId,
    CAST(ep2.value AS NVARCHAR(MAX)) AS InformationType,
    CAST(ep3.value AS NVARCHAR(MAX)) AS SensitivityLabelId,
    CAST(ep4.value AS NVARCHAR(MAX)) AS SensitivityLabel
FROM sys.objects o
INNER JOIN sys.columns c ON o.object_id = c.object_id
LEFT JOIN sys.extended_properties ep1
    ON ep1.major_id = o.object_id AND ep1.minor_id = c.column_id
    AND ep1.name = 'sys_information_type_id' AND ep1.class = 1
LEFT JOIN sys.extended_properties ep2
    ON ep2.major_id = o.object_id AND ep2.minor_id = c.column_id
    AND ep2.name = 'sys_information_type_name' AND ep2.class = 1
LEFT JOIN sys.extended_properties ep3
    ON ep3.major_id = o.object_id AND ep3.minor_id = c.column_id
    AND ep3.name = 'sys_sensitivity_label_id' AND ep3.class = 1
LEFT JOIN sys.extended_properties ep4
    ON ep4.major_id = o.object_id AND ep4.minor_id = c.column_id
    AND ep4.name = 'sys_sensitivity_label_name' AND ep4.class = 1
WHERE o.type = 'U'
  AND (ep1.value IS NOT NULL OR ep2.value IS NOT NULL OR ep3.value IS NOT NULL OR ep4.value IS NOT NULL)
ORDER BY SCHEMA_NAME(o.schema_id), o.name, c.name"

            try {
                $results = $db.Query($sql)
            } catch {
                Stop-Function -Message "Error querying data classifications in $($db.Name) on $server" -ErrorRecord $_ -Target $db -Continue -FunctionName Get-DbaDbDataClassification
            }

            foreach ($row in $results) {
                if ($Schema -and $row.SchemaName -notin $Schema) { continue }
                if ($Table -and $row.TableName -notin $Table) { continue }
                if ($Column -and $row.ColumnName -notin $Column) { continue }

                [PSCustomObject]@{
                    ComputerName       = $db.ComputerName
                    InstanceName       = $db.InstanceName
                    SqlInstance        = $db.SqlInstance
                    Database           = $db.Name
                    Schema             = $row.SchemaName
                    Table              = $row.TableName
                    Column             = $row.ColumnName
                    InformationTypeId  = $row.InformationTypeId
                    InformationType    = $row.InformationType
                    SensitivityLabelId = $row.SensitivityLabelId
                    SensitivityLabel   = $row.SensitivityLabel
                    DatabaseObject     = $db
                }
            }
        }
} $SqlInstance $SqlCredential $Database $Schema $Table $Column $InputObject $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
