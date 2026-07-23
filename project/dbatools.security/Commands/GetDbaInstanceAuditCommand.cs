#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Gets the server-level audits configured on a SQL Server instance.
/// </summary>
/// <remarks>
/// <para>
/// The workflow remains a module-scoped PowerShell compatibility hop so that the audit enumeration, the
/// derived file paths, the added note properties, the default view, and dbatools stream and error handling
/// stay observable-identical to the script implementation.
/// </para>
/// <para>
/// The command is process-only and carries no cross-record state, so it ships as a single hop per record.
/// It streams through InvokeScopedStreaming rather than buffering: SqlInstance is an array, so one record
/// can emit audits for an early instance and then hit the connection Stop-Function on a later one, which
/// terminates under -EnableException - a buffered call would discard the audits already produced (DEF-001).
/// The two Test-Bound reads map to carried by-name flags, since Test-Bound scope-walks the caller and
/// cannot ride a hop. EnableException is carried as a plain (untyped) value, because a switch in the inner
/// CmdletBinding scriptblock is excluded from positional binding. There is no ShouldProcess in the source.
/// </para>
/// </remarks>
[Cmdlet(VerbsCommon.Get, "DbaInstanceAudit")]
[OutputType(typeof(Microsoft.SqlServer.Management.Smo.Audit))]
public sealed class GetDbaInstanceAuditCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The audits to return.</summary>
    [Parameter(Position = 2)]
    [PsStringArrayCast]
    public string[]? Audit { get; set; }

    /// <summary>Audits to exclude from the results.</summary>
    [Parameter(Position = 3)]
    [PsStringArrayCast]
    public string[]? ExcludeAudit { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    /// <summary>Returns the audits for the instances bound to the current record.</summary>
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
            }
            else
            {
                WriteObject(item);
            }
        }, ProcessScript,
            SqlInstance, SqlCredential, Audit, ExcludeAudit, EnableException.ToBool(),
            TestBound(nameof(Audit)), TestBound(nameof(ExcludeAudit)),
            NestedCommand.BoundCommonParameter(this, "Verbose"), NestedCommand.BoundCommonParameter(this, "Debug"));
    }

    // PS: the process body VERBATIM. Substitutions only: the two Test-Bound reads -> the carried by-name
    // flags; -FunctionName on the single DIRECT Stop-Function call. EnableException received untyped.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Audit, $ExcludeAudit, $EnableException, $__auditBound, $__excludeAuditBound, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [System.Management.Automation.PSCredential]$SqlCredential, [string[]]$Audit, [string[]]$ExcludeAudit, $EnableException, $__auditBound, $__excludeAuditBound, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        foreach ($instance in $SqlInstance) {
            try {
                $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential -MinimumVersion 10
            } catch {
                Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaInstanceAudit
            }

            $audits = $server.Audits

            if ($__auditBound) {
                $audits = $audits | Where-Object Name -in $Audit
            }
            if ($__excludeAuditBound) {
                $audits = $audits | Where-Object Name -notin $ExcludeAudit
            }

            foreach ($currentaudit in $audits) {
                $directory = $currentaudit.FilePath.TrimEnd("\")
                $filename = $currentaudit.FileName
                $fullname = "$directory\$filename"
                $remote = $fullname.Replace(":", "$")
                $remote = "\\$($currentaudit.Parent.ComputerName)\$remote"

                Add-Member -Force -InputObject $currentaudit -MemberType NoteProperty -Name ComputerName -value $currentaudit.Parent.ComputerName
                Add-Member -Force -InputObject $currentaudit -MemberType NoteProperty -Name InstanceName -value $currentaudit.Parent.ServiceName
                Add-Member -Force -InputObject $currentaudit -MemberType NoteProperty -Name SqlInstance -value $currentaudit.Parent.DomainInstanceName
                Add-Member -Force -InputObject $currentaudit -MemberType NoteProperty -Name FullName -value $fullname
                Add-Member -Force -InputObject $currentaudit -MemberType NoteProperty -Name RemoteFullName -value $remote

                Select-DefaultView -InputObject $currentaudit -Property ComputerName, InstanceName, SqlInstance, Name, 'Enabled as IsEnabled', OnFailure, MaximumFiles, MaximumFileSize, MaximumFileSizeUnit, MaximumRolloverFiles, QueueDelay, ReserveDiskSpace, FullName
            }
        }
} $SqlInstance $SqlCredential $Audit $ExcludeAudit $EnableException $__auditBound $__excludeAuditBound $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1

""";
}
