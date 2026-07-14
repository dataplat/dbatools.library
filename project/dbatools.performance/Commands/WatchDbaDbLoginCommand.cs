#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Captures active client-login activity and writes it to a central table. Port of
/// public/Watch-DbaDbLogin.ps1 (W1-136). The complete per-record body rides an advanced
/// module-scoped PowerShell hop so private helpers, SMO/ETS behavior, DMV result shaping,
/// filtering, conversion, the database write pipeline, test mocks, and streams retain the
/// function's engine semantics. Surface pinned by
/// migration/baselines/Watch-DbaDbLogin.json.
/// </summary>
[Cmdlet(VerbsCommon.Watch, "DbaDbLogin", DefaultParameterSetName = "Default")]
public sealed class WatchDbaDbLoginCommand : DbaBaseCmdlet
{
    /// <summary>SQL Server instance that stores the captured login activity.</summary>
    [Parameter(Position = 0)]
    public DbaInstanceParameter? SqlInstance { get; set; }

    /// <summary>Alternative SQL credential.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Destination database.</summary>
    [Parameter(Position = 2)]
    public string? Database { get; set; }

    /// <summary>Destination table.</summary>
    [Parameter(Position = 3)]
    public string? Table { get; set; } = "DbaTools-WatchDbLogins";

    /// <summary>Central Management Server used to discover monitored instances.</summary>
    [Parameter(Position = 4)]
    public string? SqlCms { get; set; }

    /// <summary>Text file containing monitored instances.</summary>
    [Parameter(Position = 5)]
    public string? ServersFromFile { get; set; }

    /// <summary>Pre-connected SQL Server instances to monitor.</summary>
    [Parameter(ValueFromPipeline = true, Position = 6)]
    public Microsoft.SqlServer.Management.Smo.Server[]? InputObject { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, SqlCredential, Database, Table, SqlCms, ServersFromFile, InputObject,
            EnableException.ToBool(), TestBound("SqlCms"), TestBound("ServersFromFile"),
            TestBound("InputObject"), BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
            }
            else
            {
                WriteObject(item);
            }
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

    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $Table, $SqlCms, $ServersFromFile, $InputObject, $EnableException, $__boundSqlCms, $__boundServersFromFile, $__boundInputObject, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param($SqlInstance, $SqlCredential, $Database, $Table, $SqlCms, $ServersFromFile, [Microsoft.SqlServer.Management.Smo.Server[]]$InputObject, $EnableException, $__boundSqlCms, $__boundServersFromFile, $__boundInputObject)

        if (-not ($__boundSqlCms -or $__boundServersFromFile -or $__boundInputObject)) {
            Stop-Function -Message "You must specify a server list source using -SqlCms or -ServersFromFile or pipe in connected instances. See the command documentation and examples for more details." -FunctionName Watch-DbaDbLogin
            return
        }

        try {
            $serverDest = Connect-DbaInstance -SqlInstance $SqlInstance -SqlCredential $SqlCredential
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $SqlInstance -FunctionName Watch-DbaDbLogin
            return
        }

        $systemdbs = "master", "msdb", "model", "tempdb"
        $excludedPrograms = "Microsoft SQL Server Management Studio - Query", "SQL Management"

        <#
            Get servers to query from Central Management Server or File
        #>
        if ($SqlCms) {
            try {
                $servers = Get-DbaRegServer -SqlInstance $SqlCms -SqlCredential $SqlCredential -EnableException
            } catch {
                Stop-Function -Message "The CMS server, $SqlCms, was not accessible." -Target $SqlCms -ErrorRecord $_ -FunctionName Watch-DbaDbLogin
                return
            }
        }
        if ($ServersFromFile) {
            if (Test-Path $ServersFromFile) {
                $servers = Get-Content $ServersFromFile
            } else {
                Stop-Function -Message "$ServersFromFile was not found." -Target $ServersFromFile -FunctionName Watch-DbaDbLogin
                return
            }
        }

        <#
            Connect each server
        #>
        foreach ($instance in $servers) {
            try {
                if ($instance -is [Microsoft.SqlServer.Management.RegisteredServers.RegisteredServer]) {
                    $InputObject += Connect-DbaInstance -SqlInstance $instance.ServerName -SqlCredential $SqlCredential -MinimumVersion 9
                } else {
                    $InputObject += Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential -MinimumVersion 9
                }
            } catch {
                Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Watch-DbaDbLogin
            }
        }

        <#
            Process each server
        #>
        foreach ($instance in $InputObject) {

            if (-not (Test-SqlSa $instance)) {
                Write-Message -Level Warning -Message "Not a sysadmin on $instance, resultset would be underwhelming. Skipping." -FunctionName Watch-DbaDbLogin;
                continue
            }

            if ($instance.VersionMajor -le 10) {
                $sql = "
                SELECT
                    s.login_time AS [LoginTime]
                    , s.login_name AS [Login]
                    , ISNULL(s.host_name,N'') AS [Host]
                    , ISNULL(s.program_name,N'') AS [Program]
                    , ISNULL(r.database_id,N'') AS [DatabaseId]
                    , ISNULL(DB_NAME(r.database_id),N'') AS [Database]
                    , CAST(~s.is_user_process AS BIT) AS [IsSystem]
                    , CaptureTime = (SELECT GETDATE())
                FROM sys.dm_exec_sessions AS s
                LEFT OUTER JOIN sys.dm_exec_requests AS r
                    ON r.session_id = s.session_id"
            } else {
                $sql = "
                SELECT
                    s.login_time AS [LoginTime]
                    , s.login_name AS [Login]
                    , ISNULL(s.host_name,N'') AS [Host]
                    , ISNULL(s.program_name,N'') AS [Program]
                    , ISNULL(r.database_id,s.database_id) AS [DatabaseId]
                    , ISNULL(DB_NAME(r.database_id),(DB_NAME(s.database_id))) AS [Database]
                    , CAST(~s.is_user_process AS BIT) AS [IsSystem]
                    , CaptureTime = (SELECT GETDATE())
                    ,s.database_id
                    FROM sys.dm_exec_sessions AS s
                    LEFT OUTER JOIN sys.dm_exec_requests AS r
                    ON r.session_id = s.session_id"
            }

            Write-Message -Level Debug -Message $sql -FunctionName Watch-DbaDbLogin

            $procs = $instance.Query($sql) | Where-Object { $_.Host -ne $instance.ComputerName -and ![string]::IsNullOrEmpty($_.Host) }
            $procs = $procs | Where-Object { $systemdbs -notcontains $_.Database -and $excludedPrograms -notcontains $_.Program }

            if ($procs.Count -gt 0) {
                $procs | Select-Object @{Label = "ComputerName"; Expression = { $instance.ComputerName } }, @{Label = "InstanceName"; Expression = { $instance.ServiceName } }, @{Label = "SqlInstance"; Expression = { $instance.DomainInstanceName } }, LoginTime, Login, Host, Program, DatabaseId, Database, IsSystem, CaptureTime | ConvertTo-DbaDataTable | Write-DbaDbTableData -SqlInstance $serverDest -Database $Database -Table $Table -AutoCreateTable

                Write-Message -Level Output -Message "Added process information for $instance to datatable." -FunctionName Watch-DbaDbLogin
            } else {
                Write-Message -Level Verbose -Message "No data returned for $instance." -FunctionName Watch-DbaDbLogin
            }
        }

} $SqlInstance $SqlCredential $Database $Table $SqlCms $ServersFromFile $InputObject $EnableException $__boundSqlCms $__boundServersFromFile $__boundInputObject @__commonParameters 3>&1 2>&1
""";
}
