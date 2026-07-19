#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves suspect-page records from msdb.dbo.suspect_pages. Port of public/Get-DbaSuspectPage.ps1;
/// the workflow remains a module-scoped PowerShell compatibility hop.
///
/// A process-only port (SqlInstance is Mandatory ValueFromPipeline, so process fires per record). No accumulator,
/// no interrupt, no Test-Bound, no ShouldProcess. The parameter order is unusual - Database (pos1) precedes
/// SqlCredential (pos2) - and is preserved. Two Stop-Function calls carry -Continue (the connect catch and the
/// suspect_pages query catch), both loop-bound inside foreach ($instance). The if ($Database) filter is truthiness.
/// Source structure quirk preserved verbatim: the output foreach ($row in $results) is OUTSIDE the foreach
/// ($instance) loop, so it emits the LAST processed instance's $results/$server (matters only when multiple
/// instances are passed directly to -SqlInstance; per-record piping is unaffected). Edits: -FunctionName
/// Get-DbaSuspectPage on the two Stop-Function. Surface pinned by migration/baselines/Get-DbaSuspectPage.json
/// (SqlInstance Mandatory VFP pos0, Database object pos1, SqlCredential pos2, no ShouldProcess).
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaSuspectPage")]
public sealed class GetDbaSuspectPageCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances (SQL 2005+).</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Filter to suspect pages in the specified database.</summary>
    [Parameter(Position = 1)]
    public object? Database { get; set; }

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 2)]
    public PSCredential? SqlCredential { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
                return;
            }
            WriteObject(item);
        }, ProcessScript,
            SqlInstance, Database, SqlCredential, EnableException.ToBool(),
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
    // PS: the process block VERBATIM. Edit: -FunctionName Get-DbaSuspectPage on the two Stop-Function (both
    // -Continue, loop-bound in foreach $instance). The output foreach is outside the instance loop (source quirk).
    private const string ProcessScript = """
param($SqlInstance, $Database, $SqlCredential, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [object]$Database, [PSCredential]$SqlCredential, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        foreach ($instance in $SqlInstance) {
            try {
                $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential -MinimumVersion 9
            } catch {
                Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -FunctionName Get-DbaSuspectPage -Continue
            }

            $sql = "SELECT
            DB_NAME(database_id) AS DBName,
            file_id,
            page_id,
            CASE event_type
            WHEN 1 THEN '823 or 824'
            WHEN 2 THEN 'Bad Checksum'
            WHEN 3 THEN 'Torn Page'
            WHEN 4 THEN 'Restored'
            WHEN 5 THEN 'Repaired (DBCC)'
            WHEN 7 THEN 'Deallocated (DBCC)'
            END AS EventType,
            error_count,
            last_update_date
            FROM msdb.dbo.suspect_pages"

            try {
                $results = $server.Query($sql)
            } catch {
                Stop-Function -Message "Issue collecting data on $server" -Target $server -ErrorRecord $_ -FunctionName Get-DbaSuspectPage -Continue
            }

            if ($Database) {
                $results = $results | Where-Object DBName -EQ $Database
            }

        }
        foreach ($row in $results) {
            [PSCustomObject]@{
                ComputerName   = $server.ComputerName
                InstanceName   = $server.ServiceName
                SqlInstance    = $server.DomainInstanceName
                Database       = $row.DBName
                FileId         = $row.file_id
                PageId         = $row.page_id
                EventType      = $row.EventType
                ErrorCount     = $row.error_count
                LastUpdateDate = $row.last_update_date
            }
        }
} $SqlInstance $Database $SqlCredential $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
