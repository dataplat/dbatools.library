#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;
using Microsoft.SqlServer.Management.Smo.Mail;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves SMTP server configurations from SQL Server Database Mail accounts.
/// </summary>
/// <remarks>
/// The SqlMail aggregation, the account and mail-server traversal, the name filtering, the parent
/// lookup and the output shaping all run the original dbatools PowerShell body inside the dbatools
/// module scope rather than being reimplemented in C#, so the engine keeps deciding the observable
/// details.
///
/// InputObject binds from the pipeline. The body appends the mail objects retrieved for any
/// -SqlInstance to InputObject, but that append does not leak between pipeline records: the module
/// scope lives for one record and receives InputObject fresh from this cmdlet each record, which
/// reproduces the function's per-record parameter rebinding. No cross-record state is carried.
///
/// Output streams as produced. A record can hold several mail servers (a directly bound InputObject
/// array, or servers gathered across a multi-instance -SqlInstance), and an early one is emitted before
/// a later one may throw under -EnableException; the script streamed those early results, so buffering
/// and losing them to a later terminating failure would diverge.
/// </remarks>
[Cmdlet(VerbsCommon.Get, "DbaDbMailServer")]
public sealed class GetDbaDbMailServerCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Position = 0)]
    [PsDbaInstanceArrayCast]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>One or more exact SMTP server names to return.</summary>
    [Parameter(Position = 2)]
    [Alias("Name")]
    public string[]? Server { get; set; }

    /// <summary>Restricts results to mail servers on these Database Mail account names.</summary>
    [Parameter(Position = 3)]
    public string[]? Account { get; set; }

    /// <summary>SqlMail objects supplied directly or through the pipeline.</summary>
    [Parameter(ValueFromPipeline = true, Position = 4)]
    [PsDbMailArrayCast]
    public SqlMail[]? InputObject { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
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
            else
            {
                WriteObject(item);
            }
        }, BodyScript,
            SqlInstance, SqlCredential, Server, Account, InputObject,
            EnableException.ToBool(), NestedCommand.BoundCommonParameter(this, "Verbose"),
            NestedCommand.BoundCommonParameter(this, "Debug"));
    }

    private const string BodyScript = """
param($SqlInstance, $SqlCredential, $Server, $Account, $InputObject, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, $SqlCredential,
        [string[]]$Server, [string[]]$Account,
        [Microsoft.SqlServer.Management.Smo.Mail.SqlMail[]]$InputObject, $EnableException)

    if ($SqlInstance) {
        $InputObject += Get-DbaDbMail -SqlInstance $SqlInstance -SqlCredential $SqlCredential
    }

    if (-not $InputObject) {
        Stop-Function -Message "No servers to process" -FunctionName Get-DbaDbMailServer
        return
    }

    foreach ($mailserver in $InputObject) {
        try {
            $accounts = $mailserver | Get-DbaDbMailAccount -Account $Account
            $servers = $accounts.MailServers

            if ($Server) {
                $servers = $servers | Where-Object Name -in $Server
            }

            if ($servers) {
                $servers | Add-Member -Force -MemberType NoteProperty -Name ComputerName -value $mailserver.ComputerName
                $servers | Add-Member -Force -MemberType NoteProperty -Name InstanceName -value $mailserver.InstanceName
                $servers | Add-Member -Force -MemberType NoteProperty -Name SqlInstance -value $mailserver.SqlInstance
                $servers | Add-Member -Force -MemberType NoteProperty -Name Account -value $servers.Parent.Name
                $servers | Select-DefaultView -Property ComputerName, InstanceName, SqlInstance, Account, Name, Port, EnableSsl, ServerType, UserName, UseDefaultCredentials, NoCredentialChange
            }
        } catch {
            Stop-Function -Message "Failure" -ErrorRecord $_ -Continue -FunctionName Get-DbaDbMailServer
        }
    }
} $SqlInstance $SqlCredential $Server $Account $InputObject $EnableException @__commonParameters 3>&1 2>&1
""";
}
