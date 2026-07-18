#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves the DML triggers attached to tables and views. Port of public/Get-DbaDbObjectTrigger.ps1; the
/// workflow remains a module-scoped PowerShell compatibility hop.
///
/// A process-only port (InputObject is ValueFromPipeline; the -SqlInstance path gathers tables and/or views
/// via Get-DbaDbTable / Get-DbaDbView per the Type filter). No begin/end, no accumulator, no interrupt, no
/// Test-Bound. The validation block pipes InputObject into a ForEach-Object whose body runs Stop-Function
/// -Continue then return for non-Table/View items; probing confirmed that Stop-Function -Continue plus the
/// return inside a ForEach-Object scriptblock skip only the bad item and let the code after the ForEach-Object
/// run - they do NOT propagate out of the module scriptblock - so no continue-guard wrapper or dot-source is
/// needed. The only edits are -FunctionName Get-DbaDbObjectTrigger on the two Stop-Function calls. Surface
/// pinned by migration/baselines/Get-DbaDbObjectTrigger.json (positions 0-5, Type ValidateSet, InputObject VFP,
/// no ShouldProcess).
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbObjectTrigger")]
public sealed class GetDbaDbObjectTriggerCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The database(s) to search.</summary>
    [Parameter(Position = 2)]
    public object[]? Database { get; set; }

    /// <summary>The database(s) to exclude.</summary>
    [Parameter(Position = 3)]
    public object[]? ExcludeDatabase { get; set; }

    /// <summary>Which object type(s) to search for triggers.</summary>
    [Parameter(Position = 4)]
    [ValidateSet("All", "Table", "View")]
    [PsStringCast]
    public string Type { get; set; } = "All";

    /// <summary>Table or view object(s) piped in.</summary>
    [Parameter(ValueFromPipeline = true, Position = 5)]
    public object[]? InputObject { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, SqlCredential, Database, ExcludeDatabase, Type, InputObject, EnableException.ToBool(),
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
    // PS: the process block VERBATIM. Edit: -FunctionName Get-DbaDbObjectTrigger on the two Stop-Function
    // calls. The ForEach-Object validation's Stop-Function -Continue + return skips only the bad item and does
    // not propagate out of the module scriptblock (probe-verified), so no wrapper or dot-source is used.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $ExcludeDatabase, $Type, $InputObject, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [object[]]$Database, [object[]]$ExcludeDatabase, [string]$Type, [object[]]$InputObject, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        if ($InputObject.Count -gt 0) {
            $InputObject | ForEach-Object {
                if (-not ($_ -is [Microsoft.SqlServer.Management.Smo.TableViewBase])) {
                    Stop-Function -Message "InputObject $_ is not of type Table or View." -Continue -FunctionName Get-DbaDbObjectTrigger
                    return
                }
            }
        }

        foreach ($Instance in $SqlInstance) {
            if ($Type -in @('All', 'Table')) {
                $InputObject += Get-DbaDbTable -SqlInstance $SqlInstance -SqlCredential $SqlCredential -Database $Database -ExcludeDatabase $ExcludeDatabase
            }
            if ($Type -in @('All', 'View')) {
                $InputObject += Get-DbaDbView -SqlInstance $SqlInstance -SqlCredential $SqlCredential -Database $Database -ExcludeDatabase $ExcludeDatabase
            }
        }

        foreach ($obj in $InputObject) {
            try {
                foreach ($trigger in ($obj.Triggers)) {
                    Add-Member -Force -InputObject $trigger -MemberType NoteProperty -Name ComputerName -value $trigger.Parent.ComputerName
                    Add-Member -Force -InputObject $trigger -MemberType NoteProperty -Name InstanceName -value $trigger.Parent.InstanceName
                    Add-Member -Force -InputObject $trigger -MemberType NoteProperty -Name SqlInstance -value $trigger.Parent.SqlInstance
                    Select-DefaultView -InputObject $trigger -Property ComputerName, InstanceName, SqlInstance, Name, Parent, IsEnabled, DateLastModified
                }
            } catch {
                Stop-Function -Message "Failure" -ErrorRecord $_ -Continue -FunctionName Get-DbaDbObjectTrigger
            }
        }
} $SqlInstance $SqlCredential $Database $ExcludeDatabase $Type $InputObject $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}