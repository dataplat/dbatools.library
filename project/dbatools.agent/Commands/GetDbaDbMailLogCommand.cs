#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves Database Mail event log entries from msdb for troubleshooting email delivery.
/// </summary>
/// <remarks>
/// Query construction, the server handle's lifetime across pipeline records, the Debug
/// diagnostic and the output shaping all run the original dbatools PowerShell body inside the
/// dbatools module scope rather than being reimplemented in C#, so the engine keeps deciding
/// the observable details.
///
/// -Since is declared non-nullable to match the script's [DateTime] surface, but an unbound
/// -Since must reach the body as null rather than DateTime.MinValue: the body gates its WHERE
/// clause on the value's truthiness, so a MinValue default would silently add a
/// "log_date >= 0001-01-01" predicate the script never emits.
///
/// The query text is pinned to CRLF. The script is distributed with CRLF line endings, so its
/// $sql carries them, and the Debug record echoes the query verbatim; a raw C# string literal
/// instead carries whatever line endings this file was checked out with.
/// </remarks>
[Cmdlet(VerbsCommon.Get, "DbaDbMailLog")]
public sealed class GetDbaDbMailLogCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(ValueFromPipeline = true, Position = 0)]
    [PsDbaInstanceArrayCast]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Return only log entries recorded on or after this timestamp.</summary>
    [Parameter(Position = 2)]
    [PsDateTimeCast]
    public DateTime Since { get; set; }

    /// <summary>Return only log entries of these event types.</summary>
    [Parameter(Position = 3)]
    [ValidateSet("Error", "Warning", "Success", "Information", "Internal")]
    public string[]? Type { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // The server handle the script keeps for the whole pipeline; the hop's scope only lives for
    // one record. Starts null, matching a local the script has not assigned yet.
    private object? _server;

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        object? since = TestBound(nameof(Since)) ? Since : null;
        if (SqlInstance is null)
        {
            return;
        }

        foreach (DbaInstanceParameter instance in SqlInstance)
        {
            if (Interrupted)
            {
                return;
            }

            NestedCommand.InvokeScopedStreaming(this, item =>
            {
                if (item?.BaseObject is ErrorRecord nestedError)
                {
                    NestedCommand.RemoveDuplicateError(this, nestedError);
                    WriteError(nestedError);
                }
                else if (item is not null && LanguagePrimitives.IsTrue(
                    item.Properties["__GetDbaDbMailLogProcessComplete"]?.Value))
                {
                    object? serverState = item.Properties["Server"]?.Value;
                    _server = serverState is PSObject wrapper ? wrapper.BaseObject : serverState;
                }
                else
                {
                    WriteObject(item);
                }
            }, BodyScript,
            new[] { instance }, SqlCredential, since, Type, EnableException.ToBool(), _server,
                NestedCommand.BoundCommonParameter(this, "Verbose"), NestedCommand.BoundCommonParameter(this, "Debug"));
        }
    }

    private const string BodyScript = """
param($SqlInstance, $SqlCredential, $Since, $Type, $EnableException, $Server, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, $SqlCredential,
        $Since, [string[]]$Type, $EnableException, $Server)

    $server = $Server
    foreach ($instance in $SqlInstance) {

        try {
            $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaDbMailLog
        }

        $sql = "SELECT SERVERPROPERTY('MachineName') AS ComputerName,
            ISNULL(SERVERPROPERTY('InstanceName'), 'MSSQLSERVER') AS InstanceName,
            SERVERPROPERTY('ServerName') AS SqlInstance,
            log_id AS LogId,
            CASE event_type
            WHEN 'error' THEN 'Error'
            WHEN 'warning' THEN 'Warning'
            WHEN 'information' THEN 'Information'
            WHEN 'success' THEN 'Success'
            WHEN 'internal' THEN 'Internal'
            ELSE event_type
            END AS EventType,
            log_date AS LogDate,
            REPLACE(description, CHAR(10)+')', '') AS Description,
            process_id AS ProcessId,
            mailitem_id AS MailItemId,
            account_id AS AccountId,
            last_mod_date AS LastModDate,
            last_mod_user AS LastModUser,
            last_mod_user AS [Login]
            FROM msdb.dbo.sysmail_event_log"

        $sql = $sql.Replace("`r`n", "`n").Replace("`n", "`r`n")

        if ($Since -or $Type) {
            $wherearray = @()

            if ($Since) {
                $wherearray += "log_date >= CONVERT(datetime,'$($Since.ToString("yyyy-MM-ddTHH:mm:ss", [System.Globalization.CultureInfo]::InvariantCulture))',126)"
            }

            if ($Type) {
                $combinedtype = $Type -join "', '"
                $wherearray += "event_type IN ('$combinedtype')"
            }

            $wherearray = $wherearray -join ' AND '
            $where = "WHERE $wherearray"
            $sql = "$sql $where"
        }

        Write-Message -Level Debug -Message $sql -FunctionName Get-DbaDbMailLog -ModuleName "dbatools"

        try {
            $server.Query($sql) | Select-DefaultView -Property ComputerName, InstanceName, SqlInstance, LogDate, EventType, Description, Login
        } catch {
            Stop-Function -Message "Failure" -InnerErrorRecord $_ -Continue -FunctionName Get-DbaDbMailLog
        }
    }

    [pscustomobject]@{
        __GetDbaDbMailLogProcessComplete = $true
        Server = $server
    }
} $SqlInstance $SqlCredential $Since $Type $EnableException $Server @__commonParameters 3>&1 2>&1
""";
}
