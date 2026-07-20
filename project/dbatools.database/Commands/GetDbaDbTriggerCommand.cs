#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves database DDL trigger objects and metadata from databases. Port of public/Get-DbaDbTrigger.ps1;
/// the workflow remains a module-scoped PowerShell compatibility hop.
///
/// A process-only port; InputObject is the only ValueFromPipeline parameter (SqlInstance is NOT VFP). The
/// -SqlInstance path gathers databases via Get-DbaDatabase and appends to $InputObject within the record. No
/// accumulator, no interrupt, no Test-Bound, no ShouldProcess. The single Stop-Function (-Continue, in the per-db
/// catch) gets -FunctionName Get-DbaDbTrigger. Source quirk preserved verbatim: the gather uses the whole
/// $SqlInstance array (not the loop variable $Instance). Surface pinned by migration/baselines/Get-DbaDbTrigger.json
/// (positions 0-4, InputObject VFP pos4, no ShouldProcess).
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbTrigger")]
public sealed class GetDbaDbTriggerCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The database(s) to process.</summary>
    [Parameter(Position = 2)]
    public object[]? Database { get; set; }

    /// <summary>The database(s) to exclude.</summary>
    [Parameter(Position = 3)]
    public object[]? ExcludeDatabase { get; set; }

    /// <summary>Database object(s) piped in from Get-DbaDatabase.</summary>
    [Parameter(ValueFromPipeline = true, Position = 4)]
    public Microsoft.SqlServer.Management.Smo.Database[]? InputObject { get; set; }

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
            SqlInstance, SqlCredential, Database, ExcludeDatabase, InputObject, EnableException.ToBool(),
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
    // PS: the process block VERBATIM. Edit: -FunctionName Get-DbaDbTrigger on the one Stop-Function (-Continue,
    // per-db catch). Source quirk preserved: Get-DbaDatabase uses the whole $SqlInstance array, not $Instance.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $ExcludeDatabase, $InputObject, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [object[]]$Database, [object[]]$ExcludeDatabase, [Microsoft.SqlServer.Management.Smo.Database[]]$InputObject, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        foreach ($Instance in $SqlInstance) {
            $InputObject += Get-DbaDatabase -SqlInstance $SqlInstance -SqlCredential $SqlCredential -Database $Database -ExcludeDatabase $ExcludeDatabase
        }

        foreach ($db in $InputObject) {
            try {
                foreach ($trigger in ($db.Triggers)) {
                    Add-Member -Force -InputObject $trigger -MemberType NoteProperty -Name ComputerName -value $db.Parent.ComputerName
                    Add-Member -Force -InputObject $trigger -MemberType NoteProperty -Name InstanceName -value $db.Parent.ServiceName
                    Add-Member -Force -InputObject $trigger -MemberType NoteProperty -Name SqlInstance -value $db.Parent.DomainInstanceName
                    Select-DefaultView -InputObject $trigger -Property ComputerName, InstanceName, SqlInstance, Name, IsEnabled, DateLastModified
                }
            } catch {
                Stop-Function -Message "Failure" -ErrorRecord $_ -FunctionName Get-DbaDbTrigger -Continue
            }
        }
} $SqlInstance $SqlCredential $Database $ExcludeDatabase $InputObject $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
