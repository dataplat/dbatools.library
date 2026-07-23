#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves detailed information about replication articles from SQL Server publications.
/// Port of public/Get-DbaReplArticle.ps1. The whole process body rides ONE VERBATIM module hop
/// per pipeline record: the begin-block Add-ReplicationLibrary, the foreach over SqlInstance, the
/// Connect-DbaInstance lookup, the accessible-database enumeration (IsAccessible guard preserved),
/// the still-module-scope Get-DbaReplPublication call per database, the Publication/Schema/Name
/// filters, and the per-article Add-Member / Select-DefaultView emit. The command has no
/// ShouldProcess, so no real-cmdlet routing is needed. In-hop Stop-Function/Write-Message carry
/// -FunctionName and read $EnableException from the hop param scope; merged-back 6..2&gt;&amp;1
/// records re-emit via the host warning/error streams (InvokeScopedStreaming), so -WarningVariable
/// capture matches the function world. No cross-record state (each SqlInstance record is
/// self-contained; every Stop-Function is -Continue, which does not latch). Surface pinned by
/// migration/baselines/Get-DbaReplArticle.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaReplArticle")]
public sealed class GetDbaReplArticleCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Position = 0, ValueFromPipeline = true)]
    public DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The databases to examine for replication articles.</summary>
    [Parameter(Position = 2)]
    public object[]? Database { get; set; }

    /// <summary>Filters results to articles within specific replication publications.</summary>
    [Parameter(Position = 3)]
    public object[]? Publication { get; set; }

    /// <summary>Filters articles by the schema of their source objects.</summary>
    [Parameter(Position = 4)]
    public string[]? Schema { get; set; }

    /// <summary>Filters results to articles with specific names.</summary>
    [Parameter(Position = 5)]
    public string[]? Name { get; set; }

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
        SqlInstance, SqlCredential, Database, Publication, Schema, Name, EnableException.ToBool(),
            NestedCommand.BoundCommonParameter(this, "Verbose"), NestedCommand.BoundCommonParameter(this, "Debug"));
    }

    // The whole process body VERBATIM in the dbatools module scope: the begin-block library load,
    // the per-instance foreach, the Connect-DbaInstance / accessible-database enumeration, the
    // still-PS Get-DbaReplPublication, the article filters, and the per-article emit.
    // Stop-Function/Write-Message carry -FunctionName.
    private const string BodyScript = """
param($SqlInstance, $SqlCredential, $Database, $Publication, $Schema, $Name, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, $SqlCredential, [object[]]$Database, [object[]]$Publication, [string[]]$Schema, [string[]]$Name, $EnableException)

    Add-ReplicationLibrary

    foreach ($instance in $SqlInstance) {
        try {
            $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
        } catch {
            Stop-Function -Message "Error occurred while establishing connection to $instance" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaReplArticle
        }

        try {
            $databases = $server.Databases | Where-Object IsAccessible -eq $true
            if ($Database) {
                $databases = $databases | Where-Object Name -in $Database
            }
        } catch {
            Stop-Function -Message "Error occurred while getting databases from $instance" -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaReplArticle
        }

        try {
            foreach ($db in $databases) {
                Write-Message -Level Verbose -Message ('Working on {0}' -f $db.Name) -FunctionName Get-DbaReplArticle -ModuleName "dbatools"

                $publications = Get-DbaReplPublication -SqlInstance $server -Database $db.Name -EnableException:$EnableException

                if ($Publication) {
                    $publications = $publications | Where-Object Name -in $Publication
                }

                $articles = $publications.Articles

                if ($Schema) {
                    $articles = $articles | Where-Object SourceObjectOwner -in $Schema
                }
                if ($Name) {
                    $articles = $articles | Where-Object Name -in $Name
                }

                foreach ($art in $articles) {
                    Add-Member -Force -InputObject $art -MemberType NoteProperty -Name ComputerName -Value $server.ComputerName
                    Add-Member -Force -InputObject $art -MemberType NoteProperty -Name InstanceName -Value $server.ServiceName
                    Add-Member -Force -InputObject $art -MemberType NoteProperty -Name SqlInstance -Value $server

                    Select-DefaultView -InputObject $art -Property ComputerName, InstanceName, SqlInstance, DatabaseName, PublicationName, Name, Type, VerticalPartition, SourceObjectOwner, SourceObjectName
                }
            }
        } catch {
            Stop-Function -Message "Error occurred while getting articles from $instance" -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaReplArticle
        }
    }
} $SqlInstance $SqlCredential $Database $Publication $Schema $Name $EnableException @__commonParameters 3>&1 2>&1
""";
}
