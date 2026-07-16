#nullable enable

using System;
using System.Collections;
using System.Globalization;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves Database Mail history. The query construction, process-scope server lifetime,
/// diagnostics, and output shaping remain a module-scoped PowerShell compatibility hop.
/// Surface pinned by migration/baselines/Get-DbaDbMailHistory.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbMailHistory")]
public sealed class GetDbaDbMailHistoryCommand : DbaBaseCmdlet
{
    /// <summary>Target SQL Server instances.</summary>
    [Parameter(ValueFromPipeline = true, Position = 0)]
    [PsDbaInstanceArrayCast]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Return mail requested on or after this timestamp.</summary>
    [Parameter(Position = 2)]
    [PsDateTimeCast]
    public DateTime Since { get; set; }

    /// <summary>Return mail with this delivery status.</summary>
    [Parameter(Position = 3)]
    [PsDbMailHistoryStringCast]
    [ValidateSet("Unsent", "Sent", "Failed", "Retrying")]
    public string? Status { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    private object? _server;

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        object? since = TestBound(nameof(Since)) ? Since : null;
        if (SqlInstance is null)
            return;

        foreach (DbaInstanceParameter instance in SqlInstance)
        {
            if (Interrupted)
                return;

            foreach (PSObject? item in NestedCommand.InvokeScoped(this, BodyScript,
                new[] { instance }, SqlCredential, since, Status, EnableException.ToBool(), _server,
                BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
            {
                if (item?.BaseObject is ErrorRecord nestedError)
                {
                    RemoveHopErrorBookkeeping(nestedError);
                    WriteError(nestedError);
                }
                else if (item is not null && LanguagePrimitives.IsTrue(
                    item.Properties["__GetDbaDbMailHistoryProcessComplete"]?.Value))
                {
                    object? serverState = item.Properties["Server"]?.Value;
                    _server = serverState is PSObject wrapper ? wrapper.BaseObject : serverState;
                }
                else
                {
                    WriteObject(item);
                }
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

    private const string BodyScript = """
param($SqlInstance, $SqlCredential, $Since, $Status, $EnableException, $Server, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, $SqlCredential,
        $Since, [string]$Status, $EnableException, $Server)

    $server = $Server
    foreach ($instance in $SqlInstance) {

        try {
            $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaDbMailHistory
        }

        $sql = "SELECT SERVERPROPERTY('MachineName') AS ComputerName,
                    ISNULL(SERVERPROPERTY('InstanceName'), 'MSSQLSERVER') AS InstanceName,
                    SERVERPROPERTY('ServerName') AS SqlInstance,
                    mailitem_id AS MailItemId,
                    a.profile_id AS ProfileId,
                    p.name AS Profile,
                    recipients AS Recipients,
                    copy_recipients AS CopyRecipients,
                    blind_copy_recipients AS BlindCopyRecipients,
                    subject AS Subject,
                    body AS Body,
                    body_format AS BodyFormat,
                    importance AS Importance,
                    sensitivity AS Sensitivity,
                    file_attachments AS FileAttachments,
                    attachment_encoding AS AttachmentEncoding,
                    query AS Query,
                    execute_query_database AS ExecuteQueryDatabase,
                    attach_query_result_as_file AS AttachQueryResultAsFile,
                    query_result_header AS QueryResultHeader,
                    query_result_width AS QueryResultWidth,
                    query_result_separator AS QueryResultSeparator,
                    exclude_query_output AS ExcludeQueryOutput,
                    append_query_error AS AppendQueryError,
                    send_request_date AS SendRequestDate,
                    send_request_user AS SendRequestUser,
                    sent_account_id AS SentAccountId,
                    CASE sent_status
                    WHEN 'unsent' THEN 'Unsent'
                    WHEN 'sent' THEN 'Sent'
                    WHEN 'failed' THEN 'Failed'
                    WHEN 'retrying' THEN 'Retrying'
                    END AS SentStatus,
                    sent_date AS SentDate,
                    last_mod_date AS LastModDate,
                    a.last_mod_user AS LastModUser
                    FROM msdb.dbo.sysmail_allitems a
                    JOIN msdb.dbo.sysmail_profile p
                    ON a.profile_id = p.profile_id"

        # The retired script is distributed with CRLF query text. Raw C# string literals retain
        # the source file's line endings, so pin the diagnostic/query string to that contract.
        $sql = $sql.Replace("`r`n", "`n").Replace("`n", "`r`n")

        if ($Since -or $Status) {
            $wherearray = @()

            if ($Since) {
                $wherearray += "send_request_date >= CONVERT(datetime,'$($Since.ToString("yyyy-MM-ddTHH:mm:ss", [System.Globalization.CultureInfo]::InvariantCulture))',126)"
            }

            if ($Status) {
                $Status = $Status -join "', '"
                $wherearray += "sent_status IN ('$Status')"
            }

            $wherearray = $wherearray -join ' AND '
            $where = "WHERE $wherearray"
            $sql = "$sql $where"
        }

        Write-Message -Level Debug -Message $sql -FunctionName Get-DbaDbMailHistory

        try {
            $server.Query($sql) | Select-DefaultView -Property ComputerName, InstanceName, SqlInstance, Profile, Recipients, CopyRecipients, BlindCopyRecipients, Subject, Importance, Sensitivity, FileAttachments, AttachmentEncoding, SendRequestDate, SendRequestUser, SentStatus, SentDate
        } catch {
            Stop-Function -Message "Query failure" -ErrorRecord $_ -Continue -FunctionName Get-DbaDbMailHistory
        }
    }

    [pscustomobject]@{
        __GetDbaDbMailHistoryProcessComplete = $true
        Server = $server
    }
} $SqlInstance $SqlCredential $Since $Status $EnableException $Server @__commonParameters 3>&1 2>&1
""";
}

/// <summary>Reproduces the advanced-function [string] cast before ValidateSet executes.</summary>
internal sealed class PsDbMailHistoryStringCastAttribute : ArgumentTransformationAttribute
{
    public override object? Transform(EngineIntrinsics engineIntrinsics, object? inputData)
    {
        return LanguagePrimitives.ConvertTo(inputData, typeof(string), CultureInfo.InvariantCulture);
    }
}
