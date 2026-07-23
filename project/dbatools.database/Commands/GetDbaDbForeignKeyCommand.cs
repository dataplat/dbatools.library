#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves foreign keys from SQL Server database tables. Port of public/Get-DbaDbForeignKey.ps1; the
/// workflow remains a module-scoped PowerShell compatibility hop.
///
/// A process-only port (SqlInstance is ValueFromPipeline, so process fires per record); the simplest kind -
/// no begin/end, no accumulator, no interrupt (the one Stop-Function is -Continue and there is no
/// Test-FunctionInterrupt or early return). The three continue statements (a not-accessible skip and the
/// two table-level filters) sit inside foreach loops, so they are normal loop continues - NOT bare, so no
/// continue-guard wrapper is needed. The only sanctioned edits are the one Test-Bound read -
/// Test-Bound -ParameterName ExcludeSystemTable becomes the carried boolean flag $__boundExcludeSystemTable
/// (from C# TestBound(nameof(ExcludeSystemTable))) - and -FunctionName Get-DbaDbForeignKey on the one
/// Stop-Function and two Write-Message. Database/ExcludeDatabase are truthiness checks. Surface pinned by
/// migration/baselines/Get-DbaDbForeignKey.json (positions 0-3, no sets, no ShouldProcess).
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbForeignKey")]
public sealed class GetDbaDbForeignKeyCommand : DbaBaseCmdlet
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

    /// <summary>Exclude foreign keys on system tables.</summary>
    [Parameter]
    public SwitchParameter ExcludeSystemTable { get; set; }

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
            SqlInstance, SqlCredential, Database, ExcludeDatabase, EnableException.ToBool(),
            TestBound(nameof(ExcludeSystemTable)),
            NestedCommand.BoundCommonParameter(this, "Verbose"), NestedCommand.BoundCommonParameter(this, "Debug"));
    }
    // PS: the process block VERBATIM. Edits: Test-Bound -ParameterName ExcludeSystemTable -> the carried
    // $__boundExcludeSystemTable flag, and -FunctionName Get-DbaDbForeignKey on the one Stop-Function and
    // two Write-Message. The three continue statements are inside foreach loops - normal loop continues.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $ExcludeDatabase, $EnableException, $__boundExcludeSystemTable, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [object[]]$Database, [object[]]$ExcludeDatabase, $EnableException, $__boundExcludeSystemTable, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        foreach ($instance in $SqlInstance) {
            try {
                $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
            } catch {
                Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaDbForeignKey
            }

            $databases = $server.Databases | Where-Object IsAccessible

            if ($Database) {
                $databases = $databases | Where-Object Name -In $Database
            }
            if ($ExcludeDatabase) {
                $databases = $databases | Where-Object Name -NotIn $ExcludeDatabase
            }

            foreach ($db in $databases) {
                if (!$db.IsAccessible) {
                    Write-Message -Level Warning -Message "Database $db is not accessible. Skipping." -FunctionName Get-DbaDbForeignKey -ModuleName "dbatools"
                    continue
                }

                foreach ($tbl in $db.Tables) {
                    if ( $__boundExcludeSystemTable -and $tbl.IsSystemObject ) {
                        continue
                    }

                    if ($tbl.ForeignKeys.Count -eq 0) {
                        Write-Message -Message "No Foreign Keys exist in $tbl table on the $db database on $instance" -Target $tbl -Level Verbose -FunctionName Get-DbaDbForeignKey -ModuleName "dbatools"
                        continue
                    }

                    foreach ($fk in $tbl.ForeignKeys) {
                        Add-Member -Force -InputObject $fk -MemberType NoteProperty -Name ComputerName -value $server.ComputerName
                        Add-Member -Force -InputObject $fk -MemberType NoteProperty -Name InstanceName -value $server.ServiceName
                        Add-Member -Force -InputObject $fk -MemberType NoteProperty -Name SqlInstance -value $server.DomainInstanceName
                        Add-Member -Force -InputObject $fk -MemberType NoteProperty -Name Database -value $db.Name
                        Add-Member -Force -InputObject $fk -MemberType NoteProperty -Name Schema -Value $tbl.Schema
                        Add-Member -Force -InputObject $fk -MemberType NoteProperty -Name Table -Value $tbl.Name

                        $defaults = 'ComputerName', 'InstanceName', 'SqlInstance', 'Database', 'Schema', 'Table', 'ID', 'CreateDate',
                        'DateLastModified', 'Name', 'IsEnabled', 'IsChecked', 'NotForReplication', 'ReferencedKey', 'ReferencedTable', 'ReferencedTableSchema'
                        Select-DefaultView -InputObject $fk -Property $defaults
                    }
                }
            }
        }
} $SqlInstance $SqlCredential $Database $ExcludeDatabase $EnableException $__boundExcludeSystemTable $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
