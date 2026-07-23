#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Reports data-compression usage (None/Row/Page/Columnstore) for tables, indexes and physical partitions
/// across databases. Port of public/Get-DbaDbCompression.ps1; the workflow remains a module-scoped
/// PowerShell compatibility hop.
///
/// A process-only port (SqlInstance is ValueFromPipeline, so process fires per record); the simplest kind -
/// no begin/end, no accumulator, no interrupt (all three Stop-Function calls are -Continue and there is no
/// Test-FunctionInterrupt, return, or bare continue/break), no Test-Bound. Database/ExcludeDatabase/Table are
/// truthiness checks. The only edits are -FunctionName Get-DbaDbCompression on the three Stop-Function calls.
/// The Cmdlet attribute carries DefaultParameterSetName = "Default" to match the source (no ParameterSetName
/// on any parameter). Surface pinned by migration/baselines/Get-DbaDbCompression.json (positions 0-4, one set
/// Default, no ShouldProcess).
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbCompression", DefaultParameterSetName = "Default")]
public sealed class GetDbaDbCompressionCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The database(s) to process.</summary>
    [Parameter(Position = 2)]
    [PsStringArrayCast]
    public string[]? Database { get; set; }

    /// <summary>The database(s) to exclude.</summary>
    [Parameter(Position = 3)]
    [PsStringArrayCast]
    public string[]? ExcludeDatabase { get; set; }

    /// <summary>The table(s) to process.</summary>
    [Parameter(Position = 4)]
    [PsStringArrayCast]
    public string[]? Table { get; set; }

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
            SqlInstance, SqlCredential, Database, ExcludeDatabase, Table, EnableException.ToBool(),
            NestedCommand.BoundCommonParameter(this, "Verbose"), NestedCommand.BoundCommonParameter(this, "Debug"));
    }
    // PS: the process block VERBATIM. Edit: -FunctionName Get-DbaDbCompression on the three Stop-Function
    // calls. Database/ExcludeDatabase/Table are truthiness checks (no Test-Bound).
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $ExcludeDatabase, $Table, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [string[]]$Database, [string[]]$ExcludeDatabase, [string[]]$Table, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        foreach ($instance in $SqlInstance) {
            try {
                $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential -MinimumVersion 10
            } catch {
                Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaDbCompression
            }

            try {
                $dbs = $server.Databases | Where-Object { $_.IsAccessible -and $_.IsSystemObject -eq 0 }

                if ($Database) {
                    $dbs = $dbs | Where-Object { $_.Name -In $Database }
                }

                if ($ExcludeDatabase) {
                    $dbs = $dbs | Where-Object { $_.Name -NotIn $ExcludeDatabase }
                }
            } catch {
                Stop-Function -Message "Unable to gather list of databases for $instance" -Target $instance -ErrorRecord $_ -Continue -FunctionName Get-DbaDbCompression
            }

            foreach ($db in $dbs) {
                try {
                    $tables = $server.Databases[$($db.name)].Tables

                    if ($Table) {
                        $tables = $tables | Where-Object Name -in $Table
                    }

                    foreach ($obj in $tables) {
                        if ($obj.HasHeapIndex) {
                            foreach ($p in $obj.PhysicalPartitions) {
                                [PSCustomObject]@{
                                    ComputerName    = $server.ComputerName
                                    InstanceName    = $server.ServiceName
                                    SqlInstance     = $server.DomainInstanceName
                                    Database        = $db.Name
                                    DatabaseId      = $db.Id
                                    Schema          = $obj.Schema
                                    TableName       = $obj.Name
                                    IndexName       = $null
                                    Partition       = $p.PartitionNumber
                                    IndexID         = 0
                                    IndexType       = "Heap"
                                    DataCompression = $p.DataCompression
                                    SizeCurrent     = [dbasize]($obj.DataSpaceUsed * 1024)
                                    RowCount        = $obj.RowCount
                                }
                            }
                        }

                        foreach ($index in $obj.Indexes) {
                            foreach ($p in $index.PhysicalPartitions) {
                                [PSCustomObject]@{
                                    ComputerName    = $server.ComputerName
                                    InstanceName    = $server.ServiceName
                                    SqlInstance     = $server.DomainInstanceName
                                    Database        = $db.Name
                                    DatabaseId      = $db.Id
                                    Schema          = $obj.Schema
                                    TableName       = $obj.Name
                                    IndexName       = $index.Name
                                    Partition       = $p.PartitionNumber
                                    IndexID         = $index.ID
                                    IndexType       = $index.IndexType
                                    DataCompression = $p.DataCompression
                                    SizeCurrent     = if ($index.IndexType -eq "ClusteredIndex") { [dbasize]($obj.DataSpaceUsed * 1024) } else { [dbasize]($index.SpaceUsed * 1024) }
                                    RowCount        = $p.RowCount
                                }
                            }
                        }

                    }
                } catch {
                    Stop-Function -Message "Unable to query $instance - $db" -Target $db -ErrorRecord $_ -Continue -FunctionName Get-DbaDbCompression
                }
            }
        }
} $SqlInstance $SqlCredential $Database $ExcludeDatabase $Table $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
