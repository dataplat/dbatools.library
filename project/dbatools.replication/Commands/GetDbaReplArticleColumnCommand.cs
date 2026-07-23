#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves column-level replication configuration details for SQL Server publication articles.
/// Port of public/Get-DbaReplArticleColumn.ps1. The whole process body rides ONE VERBATIM module
/// hop per pipeline record: the Get-DbaReplArticle lookup (still module-scope), the per-article
/// LoadProperties / source-table column enumeration, the vertical-partition sysarticlecolumns
/// query branch, the Column filter, and the per-column Add-Member / Select-DefaultView emit. The
/// command has no ShouldProcess, so no real-cmdlet routing is needed. In-hop Stop-Function/
/// Write-Message carry -FunctionName and read $EnableException from the hop param scope; merged-back
/// 6..2&gt;&amp;1 records re-emit via the host warning/error streams (InvokeScopedStreaming). The
/// source's Stop-Function -Target $instance references an $instance variable this function never
/// defines (there is no per-instance foreach here) - preserved verbatim as the null it resolves to.
/// No cross-record state (the one Stop-Function is -Continue, which does not latch). Surface pinned
/// by migration/baselines/Get-DbaReplArticleColumn.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaReplArticleColumn")]
public sealed class GetDbaReplArticleColumnCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Position = 0, ValueFromPipeline = true)]
    public DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Filters results to specific database(s) containing publications.</summary>
    [Parameter(Position = 2)]
    public object[]? Database { get; set; }

    /// <summary>Filters results to specific publication(s) within the specified databases.</summary>
    [Parameter(Position = 3, ValueFromPipeline = true)]
    public object[]? Publication { get; set; }

    /// <summary>Filters results to specific article(s) within the publications.</summary>
    [Parameter(Position = 4)]
    public string[]? Article { get; set; }

    /// <summary>Filters results to specific column name(s) across all matched articles.</summary>
    [Parameter(Position = 5)]
    public string[]? Column { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                NestedCommand.RemoveDuplicateError(this, nestedError);
                WriteError(nestedError);
            }
            else
            {
                WriteObject(item);
            }
        }, BodyScript,
        SqlInstance, SqlCredential, Database, Publication, Article, Column, EnableException.ToBool(),
            NestedCommand.BoundCommonParameter(this, "Verbose"), NestedCommand.BoundCommonParameter(this, "Debug"));
    }

    // The whole process body VERBATIM in the dbatools module scope: the Get-DbaReplArticle lookup,
    // the per-article column enumeration (direct columns or the vertical-partition query branch),
    // the Column filter, and the per-column emit. Stop-Function/Write-Message carry -FunctionName.
    private const string BodyScript = """
param($SqlInstance, $SqlCredential, $Database, $Publication, $Article, $Column, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, $SqlCredential, [object[]]$Database, [object[]]$Publication, [string[]]$Article, [string[]]$Column, $EnableException)

    $articles = Get-DbaReplArticle -SqlInstance $SqlInstance -SqlCredential $SqlCredential -Database $Database -Publication $Publication -Name $Article -EnableException:$EnableException

    foreach ($art in $articles) {
        try {
            # Load the article properties to ensure we have current data
            $null = $art.LoadProperties()

            # Get the source table to enumerate columns
            $server = $art.SqlInstance
            $database = $server.Databases[$art.DatabaseName]
            $table = $database.Tables[$art.SourceObjectName, $art.SourceObjectOwner]

            if ($null -eq $table) {
                Write-Message -Level Warning -Message "Could not find source table [$($art.SourceObjectOwner)].[$($art.SourceObjectName)] for article $($art.Name)" -FunctionName Get-DbaReplArticleColumn -ModuleName "dbatools"
                continue
            }

            # Get all columns from the table
            $allColumns = $table.Columns

            # If vertical partitioning is not enabled, all columns are replicated
            # If it is enabled, we need to check which columns are included
            if (-not $art.VerticalPartition) {
                # All columns are replicated
                $columns = $allColumns.Name
            } else {
                # Only specific columns are replicated - enumerate them from the article
                # For vertical partitioning, we need to get the columns from the article's metadata
                # Unfortunately, RMO doesn't expose this directly in a simple way
                # We'll use the SMO connection to query the replication metadata
                $splatQuery = @{
                    SqlInstance = $server
                    Database    = $art.DatabaseName
                    Query       = @"
SELECT c.name as ColumnName
FROM sysarticlecolumns ac
INNER JOIN sys.columns c ON ac.colid = c.column_id
INNER JOIN sys.tables t ON c.object_id = t.object_id AND ac.artid = (
    SELECT artid FROM sysarticles WHERE name = @articleName
)
WHERE t.name = @tableName
    AND SCHEMA_NAME(t.schema_id) = @schemaName
ORDER BY c.column_id
"@
                    SqlParameter = @{
                        articleName = $art.Name
                        tableName   = $art.SourceObjectName
                        schemaName  = $art.SourceObjectOwner
                    }
                }
                $columnData = Invoke-DbaQuery @splatQuery
                $columns = $columnData.ColumnName
            }

            if ($Column) {
                $columns = $columns | Where-Object { $_ -In $Column }
            }

            foreach ($col in $columns) {
                Add-Member -Force -InputObject $art -MemberType NoteProperty -Name ComputerName -Value $art.ComputerName
                Add-Member -Force -InputObject $art -MemberType NoteProperty -Name InstanceName -Value $art.InstanceName
                Add-Member -Force -InputObject $art -MemberType NoteProperty -Name SqlInstance -Value $art.SqlInstance
                Add-Member -Force -InputObject $art -MemberType NoteProperty -Name DatabaseName -Value $art.DatabaseName
                Add-Member -Force -InputObject $art -MemberType NoteProperty -Name PublicationName -Value $art.PublicationName
                Add-Member -Force -InputObject $art -MemberType NoteProperty -Name ArticleName -Value $art.Name
                Add-Member -Force -InputObject $art -MemberType NoteProperty -Name ArticleId -Value $art.ArticleId
                Add-Member -Force -InputObject $art -MemberType NoteProperty -Name Description -Value $art.Description
                Add-Member -Force -InputObject $art -MemberType NoteProperty -Name Type -Value $art.Type
                Add-Member -Force -InputObject $art -MemberType NoteProperty -Name VerticalPartition -Value $art.VerticalPartition
                Add-Member -Force -InputObject $art -MemberType NoteProperty -Name SourceObjectOwner -Value $art.SourceObjectOwner
                Add-Member -Force -InputObject $art -MemberType NoteProperty -Name SourceObjectName -Value $art.SourceObjectName
                Add-Member -Force -InputObject $art -MemberType NoteProperty -Name ColumnName -Value $col

                Select-DefaultView -InputObject $art -Property ComputerName, InstanceName, SqlInstance, DatabaseName, PublicationName, ArticleName, ArticleId, ColumnName
            }
        } catch {
            Stop-Function -Message "Error occurred while getting article columns from $instance" -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaReplArticleColumn
        }
    }
} $SqlInstance $SqlCredential $Database $Publication $Article $Column $EnableException @__commonParameters 3>&1 2>&1
""";
}
