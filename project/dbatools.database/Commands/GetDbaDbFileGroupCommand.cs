#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Returns the filegroups of each database. Port of public/Get-DbaDbFileGroup.ps1; the workflow remains a
/// module-scoped PowerShell compatibility hop.
///
/// A process-only port (both SqlInstance and InputObject are ValueFromPipeline; the -SqlInstance path gathers
/// databases via Get-DbaDatabase). No begin/end, no accumulator, no interrupt. The neither-bound guard runs
/// Write-Message then `return` (not `continue`), which exits the hop scriptblock cleanly, so no continue-guard
/// wrapper is needed. Four Test-Bound reads become carried boundness flags: the compound
/// Test-Bound -not 'SqlInstance','InputObject' -> (-not $__boundSqlInstance) -and (-not $__boundInputObject);
/// Test-Bound -Not -ParameterName InputObject -> -not $__boundInputObject; Test-Bound -ParameterName Database
/// -> $__boundDatabase; Test-Bound -ParameterName Filegroup -> $__boundFilegroup (each keeping the if's own
/// parentheses; flags from C# TestBound(nameof(...))). Body edits also add -FunctionName Get-DbaDbFileGroup to
/// the four Write-Message. Surface pinned by migration/baselines/Get-DbaDbFileGroup.json (positions 0-4, both
/// SqlInstance/InputObject VFP, no ShouldProcess).
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbFileGroup")]
public sealed class GetDbaDbFileGroupCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The database(s) to process.</summary>
    [Parameter(Position = 2)]
    public object[]? Database { get; set; }

    /// <summary>Database object(s) piped in.</summary>
    [Parameter(ValueFromPipeline = true, Position = 3)]
    public Microsoft.SqlServer.Management.Smo.Database[]? InputObject { get; set; }

    /// <summary>Filter to the specified filegroup(s).</summary>
    [Parameter(Position = 4)]
    [PsStringArrayCast]
    public string[]? FileGroup { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, SqlCredential, Database, InputObject, FileGroup, EnableException.ToBool(),
            TestBound(nameof(SqlInstance)), TestBound(nameof(InputObject)), TestBound(nameof(Database)),
            TestBound(nameof(FileGroup)),
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
    // PS: the process block VERBATIM. Edits: the four Test-Bound reads -> carried boundness flags
    // ($__boundSqlInstance/$__boundInputObject/$__boundDatabase/$__boundFilegroup, each keeping the if
    // parentheses), and -FunctionName Get-DbaDbFileGroup on the four Write-Message. The neither-bound guard -ModuleName "dbatools"
    // returns to exit the hop scriptblock cleanly (not dot-sourced; no re-emit).
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $InputObject, $FileGroup, $EnableException, $__boundSqlInstance, $__boundInputObject, $__boundDatabase, $__boundFilegroup, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [object[]]$Database, [Microsoft.SqlServer.Management.Smo.Database[]]$InputObject, [string[]]$FileGroup, $EnableException, $__boundSqlInstance, $__boundInputObject, $__boundDatabase, $__boundFilegroup, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        if ((-not $__boundSqlInstance) -and (-not $__boundInputObject)) {
            Write-Message -Level Warning -Message "You must specify either a SQL instance or supply an InputObject" -FunctionName Get-DbaDbFileGroup -ModuleName "dbatools"
            return
        }

        if (-not $__boundInputObject) {
            $InputObject += Get-DbaDatabase -SqlInstance $SqlInstance -SqlCredential $SqlCredential -Database $Database
        }

        foreach ($db in $InputObject) {
            if ($db.IsAccessible) {
                Write-Message -Level Verbose -Message "Processing database: $db" -FunctionName Get-DbaDbFileGroup -ModuleName "dbatools"
                $server = $db.Parent
                if ($__boundDatabase) {
                    $db = $db | Where-Object { $Database -contains $_.Name }
                }
                $fileGroups = $db.Filegroups

                if ($__boundFilegroup) {
                    $fileGroups = $fileGroups | Where-Object { $Filegroup -contains $_.Name }
                }

                foreach ($fg in $fileGroups) {
                    Write-Message -Level Verbose -Message "Processing filegroup $($fg.Name)" -FunctionName Get-DbaDbFileGroup -ModuleName "dbatools"
                    $fg | Add-Member -Force -MemberType NoteProperty -Name ComputerName -Value $server.ComputerName
                    $fg | Add-Member -Force -MemberType NoteProperty -Name InstanceName -Value $server.ServiceName
                    $fg | Add-Member -Force -MemberType NoteProperty -Name SqlInstance -Value $server.DomainInstanceName

                    $defaultprops = "ComputerName", "InstanceName", "SqlInstance", "Parent", "FileGroupType", "Name", "Size"

                    Select-DefaultView -InputObject $fg -Property $defaultprops
                }
            } else {
                Write-Message -Level Verbose -Message "Skipping processing of database: $db as database is not accessible" -FunctionName Get-DbaDbFileGroup -ModuleName "dbatools"
            }
        }
} $SqlInstance $SqlCredential $Database $InputObject $FileGroup $EnableException $__boundSqlInstance $__boundInputObject $__boundDatabase $__boundFilegroup $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
