#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves database snapshots and metadata from instances. Port of public/Get-DbaDbSnapshot.ps1;
/// the workflow remains a module-scoped PowerShell compatibility hop.
///
/// A process-only port (SqlInstance is Mandatory ValueFromPipeline, so process fires per record). No accumulator,
/// no interrupt, no Test-Bound, no ShouldProcess. Both Stop-Function calls carry -Continue (the connect catch and
/// the per-db BytesOnDisk-query catch), so they are safe and need no interrupt carry. The Database/ExcludeDatabase/
/// Snapshot/ExcludeSnapshot Where-Object filters are truthiness-based. The only edits are -FunctionName
/// Get-DbaDbSnapshot on the two Stop-Function. Surface pinned by migration/baselines/Get-DbaDbSnapshot.json
/// (positions 0-5, SqlInstance Mandatory VFP pos0, no ShouldProcess).
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbSnapshot")]
public sealed class GetDbaDbSnapshotCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances (SQL 2005+).</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Filter to snapshots of the specified base database(s).</summary>
    [Parameter(Position = 2)]
    public object[]? Database { get; set; }

    /// <summary>Exclude snapshots of the specified base database(s).</summary>
    [Parameter(Position = 3)]
    public object[]? ExcludeDatabase { get; set; }

    /// <summary>Filter to the specified snapshot(s) by name.</summary>
    [Parameter(Position = 4)]
    public object[]? Snapshot { get; set; }

    /// <summary>Exclude the specified snapshot(s) by name.</summary>
    [Parameter(Position = 5)]
    public object[]? ExcludeSnapshot { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, SqlCredential, Database, ExcludeDatabase, Snapshot, ExcludeSnapshot,
            EnableException.ToBool(), BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
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
    // PS: the process block VERBATIM. Edit: -FunctionName Get-DbaDbSnapshot on the two Stop-Function (both
    // -Continue: the connect catch and the per-db BytesOnDisk-query catch).
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $ExcludeDatabase, $Snapshot, $ExcludeSnapshot, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [object[]]$Database, [object[]]$ExcludeDatabase, [object[]]$Snapshot, [object[]]$ExcludeSnapshot, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        foreach ($instance in $SqlInstance) {
            try {
                $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential -MinimumVersion 9
            } catch {
                Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -FunctionName Get-DbaDbSnapshot -Continue
            }
            $dbs = $server.Databases | Where-Object DatabaseSnapshotBaseName
            if ($Database) {
                $dbs = $dbs | Where-Object { $Database -contains $_.DatabaseSnapshotBaseName }
            }
            if ($ExcludeDatabase) {
                $dbs = $dbs | Where-Object { $ExcludeDatabase -notcontains $_.DatabaseSnapshotBaseName }
            }
            if ($Snapshot) {
                $dbs = $dbs | Where-Object { $Snapshot -contains $_.Name }
            }
            if (!$Snapshot -and !$Database) {
                $dbs = $dbs | Where-Object IsDatabaseSnapshot -eq $true | Sort-Object DatabaseSnapshotBaseName, Name
            }
            if ($ExcludeSnapshot) {
                $dbs = $dbs | Where-Object { $ExcludeSnapshot -notcontains $_.Name }
            }
            foreach ($db in $dbs) {
                try {
                    $BytesOnDisk = $db.Query("SELECT SUM(BytesOnDisk) AS BytesOnDisk FROM fn_virtualfilestats(DB_ID(),NULL) S JOIN sys.databases D ON D.database_id = S.dbid", $db.Name)
                    Add-Member -Force -InputObject $db -MemberType NoteProperty -Name ComputerName -value $server.ComputerName
                    Add-Member -Force -InputObject $db -MemberType NoteProperty -Name InstanceName -value $server.ServiceName
                    Add-Member -Force -InputObject $db -MemberType NoteProperty -Name SqlInstance -value $server.DomainInstanceName
                    Add-Member -Force -InputObject $db -MemberType NoteProperty -Name DiskUsage -value ([dbasize]($BytesOnDisk.BytesOnDisk))
                    Select-DefaultView -InputObject $db -Property ComputerName, InstanceName, SqlInstance, Name, 'DatabaseSnapshotBaseName as SnapshotOf', CreateDate, DiskUsage
                } catch {
                    Stop-Function -Message "Failure" -ErrorRecord $_ -Target $db -FunctionName Get-DbaDbSnapshot -Continue
                }
            }
        }
} $SqlInstance $SqlCredential $Database $ExcludeDatabase $Snapshot $ExcludeSnapshot $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}