#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves DDL schema-change history from a SQL Server instance's default trace. Port of
/// public/Get-DbaSchemaChangeHistory.ps1; the workflow remains a module-scoped PowerShell compatibility hop.
///
/// A process-only port (SqlInstance is Mandatory ValueFromPipeline, so process fires per record). No accumulator,
/// no interrupt, no Test-Bound, no ShouldProcess. The single Stop-Function (-Continue, connect catch) and the one
/// continue (source 120, no-default-trace skip in foreach $instance) are the only flow control. Database/
/// ExcludeDatabase/Since/Object filters are truthiness-based. Source quirk preserved verbatim: the "is not
/// accessible" Write-Message (source 131) has NO continue after it, so an inaccessible database still proceeds to
/// build and run the query. Edits: -FunctionName Get-DbaSchemaChangeHistory on the one Stop-Function and four
/// Write-Message. Surface pinned by migration/baselines/Get-DbaSchemaChangeHistory.json (positions 0-5, Since
/// DbaDateTime pos4, SqlInstance Mandatory VFP pos0, no ShouldProcess).
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaSchemaChangeHistory")]
public sealed class GetDbaSchemaChangeHistoryCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances (SQL 2005+).</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The database(s) to process.</summary>
    [Parameter(Position = 2)]
    public object[]? Database { get; set; }

    /// <summary>The database(s) to exclude.</summary>
    [Parameter(Position = 3)]
    public object[]? ExcludeDatabase { get; set; }

    /// <summary>Only return schema changes since this datetime.</summary>
    [Parameter(Position = 4)]
    public Dataplat.Dbatools.Utility.DbaDateTime? Since { get; set; }

    /// <summary>Only return schema changes for the specified object(s).</summary>
    [Parameter(Position = 5)]
    public string[]? Object { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, SqlCredential, Database, ExcludeDatabase, Since, Object, EnableException.ToBool(),
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
    // PS: the process block VERBATIM. Edits: -FunctionName Get-DbaSchemaChangeHistory on the one Stop-Function
    // (-Continue) and four Write-Message. The one continue is inside foreach ($instance) - loop-bound.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $ExcludeDatabase, $Since, $Object, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [object[]]$Database, [object[]]$ExcludeDatabase, [Dataplat.Dbatools.Utility.DbaDateTime]$Since, [string[]]$Object, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        foreach ($instance in $SqlInstance) {
            try {
                $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential -MinimumVersion 9
            } catch {
                Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -FunctionName Get-DbaSchemaChangeHistory -Continue
            }

            $TraceFileQuery = "SELECT path FROM sys.traces WHERE is_default = 1"

            $TraceFile = $server.Query($TraceFileQuery) | Select-Object Path

            if (!$TraceFile -or !$TraceFile.Path) {
                Write-Message -Level Warning -Message "No default trace file found on $instance. Schema change tracking requires the default trace to be enabled." -FunctionName Get-DbaSchemaChangeHistory
                continue
            }

            $Databases = $server.Databases

            if ($Database) { $Databases = $Databases | Where-Object Name -in $database }

            if ($ExcludeDatabase) { $Databases = $Databases | Where-Object Name -notin $ExcludeDatabase }

            foreach ($db in $Databases) {
                if ($db.IsAccessible -eq $false) {
                    Write-Message -Level Verbose -Message "$($db.name) is not accessible, skipping" -FunctionName Get-DbaSchemaChangeHistory
                }

                $sql = "SELECT  SERVERPROPERTY('MachineName') ComputerName
                      , ISNULL(SERVERPROPERTY('InstanceName'), 'MSSQLSERVER') InstanceName
                      , SERVERPROPERTY('ServerName') SqlInstance
                      , tt.DatabaseName DatabaseName
                      , tt.StartTime DateModified
                      , tt.SessionLoginName LoginName
                      , tt.NTUserName UserName
                      , tt.ApplicationName ApplicationName
                      , CASE tt.EventClass
                             WHEN '46' THEN 'Create'
                             WHEN '47' THEN 'Drop'
                             WHEN '164' THEN 'Alter'
                        END DDLOperation
                      , tt.ObjectName Object
                      , ISNULL(tsv.subclass_name, 'Unknown') ObjectType
                FROM    ::fn_trace_gettable('$($TraceFile.path)',DEFAULT) tt
                        LEFT JOIN sys.trace_subclass_values tsv ON
                            tsv.trace_event_id = tt.EventClass
                            AND tsv.subclass_value = tt.ObjectType
                            AND tsv.trace_column_id = 28
                WHERE   tt.ObjectType NOT IN ( 21587 )
                        AND tt.DatabaseID = DB_ID()
                        AND tt.EventSubClass = 0"

                if ($null -ne $since) {
                    $sql = $sql + " AND tt.StartTime>'$Since' "
                }
                if ($null -ne $object) {
                    $sql = $sql + " AND tt.ObjectName IN ('$($object -join ''',''')') "
                }

                $sql = $sql + " ORDER BY tt.StartTime ASC"
                Write-Message -Level Verbose -Message "Querying Database $db on $instance" -FunctionName Get-DbaSchemaChangeHistory
                Write-Message -Level Debug -Message "SQL: $sql" -FunctionName Get-DbaSchemaChangeHistory

                $db.Query($sql) | Select-DefaultView -Property ComputerName, InstanceName, SqlInstance, DatabaseName, DateModified, LoginName, UserName, ApplicationName, DDLOperation, Object, ObjectType
            }
        }
} $SqlInstance $SqlCredential $Database $ExcludeDatabase $Since $Object $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}