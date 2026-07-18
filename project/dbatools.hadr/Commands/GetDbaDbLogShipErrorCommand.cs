#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Returns log shipping error detail from msdb monitor tables, filterable by database,
/// action, time window and primary/secondary role. Port of
/// public/Get-DbaDbLogShipError.ps1; surface pinned by
/// migration/baselines/Get-DbaDbLogShipError.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbLogShipError")]
public sealed class GetDbaDbLogShipErrorCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instances using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Restricts results to these databases.</summary>
    [Parameter(Position = 2)]
    public string[]? Database { get; set; }

    /// <summary>Excludes these databases from the results.</summary>
    [Parameter(Position = 3)]
    public string[]? ExcludeDatabase { get; set; }

    /// <summary>Restricts results to these log shipping agent actions.</summary>
    [Parameter(Position = 4)]
    [ValidateSet("Backup", "Copy", "Restore")]
    public string[]? Action { get; set; }

    /// <summary>Only errors logged at or after this time.</summary>
    [Parameter(Position = 5)]
    public DateTime DateTimeFrom { get; set; }

    /// <summary>Only errors logged at or before this time.</summary>
    [Parameter(Position = 6)]
    public DateTime DateTimeTo { get; set; }

    /// <summary>Restricts results to primary-side errors.</summary>
    [Parameter]
    public SwitchParameter Primary { get; set; }

    /// <summary>Restricts results to secondary-side errors.</summary>
    [Parameter]
    public SwitchParameter Secondary { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        // The datetime filters ride as NULL when unbound: a PS function's unbound
        // [datetime] parameter is $null (falsy), while default(DateTime)/MinValue is
        // TRUTHY in PowerShell - passing the raw property would fire the source's
        // `if ($DateTimeFrom)` gates on every call (probe-verified both editions).
        // The inner hop params stay untyped for the same reason.
        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, SqlCredential, Database, ExcludeDatabase, Action,
            TestBound(nameof(DateTimeFrom)) ? (object?)DateTimeFrom : null,
            TestBound(nameof(DateTimeTo)) ? (object?)DateTimeTo : null,
            Primary.ToBool(), Secondary.ToBool(), EnableException.ToBool(),
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
        {
            return LanguagePrimitives.IsTrue(value);
        }
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

    // PS: the source process block VERBATIM - and for the first time in this lane,
    // CRLF-PRESERVED (the 40-line expandable T-SQL string is string CONTENT; an
    // EOL-normalized slice would change the query bytes the function world sends -
    // the per-row law noted on W4-014). cmp-proven byte-exact against the raw source
    // slice after stripping the three -FunctionName appends (two Write-Message, one
    // Stop-Function); no gates, no Test-Bound. The bare `continue` on the Express
    // check sits inside the source foreach - loop-local in both worlds.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $ExcludeDatabase, $Action, $DateTimeFrom, $DateTimeTo, $Primary, $Secondary, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [string[]]$Database, [string[]]$ExcludeDatabase, [string[]]$Action, $DateTimeFrom, $DateTimeTo, $Primary, $Secondary, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        foreach ($instance in $SqlInstance) {
            try {
                $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential -MinimumVersion 9
            } catch {
                Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaDbLogShipError
            }

            if ($server.EngineEdition -match "Express") {
                Write-Message -Level Warning -Message "$instance is Express Edition which does not support Log Shipping" -FunctionName Get-DbaDbLogShipError
                continue
            }

            $query = "
CREATE TABLE #DatabaseID
(
    DatabaseName VARCHAR(128),
    DatabaseID UNIQUEIDENTIFIER,
    Instance VARCHAR(20)
);

INSERT INTO #DatabaseID
(
    DatabaseName,
    DatabaseID,
    Instance
)
SELECT secondary_database,
        secondary_id,
        'Secondary'
FROM msdb.dbo.log_shipping_secondary_databases;


INSERT INTO #DatabaseID
(
    DatabaseName,
    DatabaseID,
    Instance
)
SELECT primary_database,
        primary_id,
        'Primary'
FROM msdb.dbo.log_shipping_primary_databases;


SELECT di.DatabaseName,
        di.Instance,
        CASE lsmed.[agent_type]
            WHEN 0 THEN
                'Backup'
            WHEN 1 THEN
                'Copy'
            WHEN 2 THEN
                'Restore'
            ELSE
                ''
        END AS [Action],
        lsmed.[session_id] AS SessionID,
        lsmed.[sequence_number] AS SequenceNumber,
        lsmed.[log_time] AS LogTime,
        lsmed.[message] AS [Message]
FROM msdb.dbo.log_shipping_monitor_error_detail AS lsmed
    INNER JOIN #DatabaseID AS di
        ON di.DatabaseID = lsmed.agent_id
ORDER BY lsmed.[log_time],
            lsmed.[database_name],
            lsmed.[agent_type],
            lsmed.[session_id],
            lsmed.[sequence_number];

DROP TABLE #DatabaseID;"

            # Get the log shipping errors
            $results = $server.Query($query)

            if ($results.Count -ge 1) {

                # Filter the results
                if ($Database) {
                    $results = $results | Where-Object { $_.DatabaseName -in $Database }
                }

                if ($Action) {
                    $results = $results | Where-Object { $_.Action -in $Action }
                }

                if ($DateTimeFrom) {
                    $results = $results | Where-Object { $_.Logtime -ge $DateTimeFrom }
                }

                if ($DateTimeTo) {
                    $results = $results | Where-Object { $_.Logtime -le $DateTimeTo }
                }

                if ($Primary) {
                    $results = $results | Where-Object { $_.Instance -eq 'Primary' }
                }

                if ($Secondary) {
                    $results = $results | Where-Object { $_.Instance -eq 'Secondary' }
                }

                foreach ($result in $results) {
                    [PSCustomObject]@{
                        ComputerName   = $server.ComputerName
                        InstanceName   = $server.ServiceName
                        SqlInstance    = $server.DomainInstanceName
                        Database       = $result.DatabaseName
                        Instance       = $result.Instance
                        Action         = $result.Action
                        SessionID      = $result.SessionID
                        SequenceNumber = $result.SequenceNumber
                        LogTime        = $result.LogTime
                        Message        = $result.Message
                    }

                }
            } else {
                Write-Message -Message "No log shipping errors found" -Level Verbose -FunctionName Get-DbaDbLogShipError
            }
        }
} $SqlInstance $SqlCredential $Database $ExcludeDatabase $Action $DateTimeFrom $DateTimeTo $Primary $Secondary $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
