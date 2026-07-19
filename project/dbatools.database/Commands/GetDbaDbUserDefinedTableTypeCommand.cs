#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves user-defined table type objects and metadata from databases. Port of
/// public/Get-DbaDbUserDefinedTableType.ps1; the workflow remains a module-scoped PowerShell compatibility hop.
///
/// A process-only port (SqlInstance is ValueFromPipeline), the same shape as Get-DbaDbServiceBrokerQueue. The
/// positional Test-Bound SqlInstance -> $__boundSqlInstance gates the Get-DbaDatabase gather into the LOCAL
/// $InputObject ($InputObject is not a parameter; assigned only when SqlInstance is bound - when unbound the
/// foreach is empty). The three continue statements (not-accessible skip, no-table-types skip, and the
/// system-object skip via if ($tabletype.IsSystemObject)) are all inside foreach loops - loop-bound. The system
/// object skip is a plain truthiness check, NOT gated by a parameter. The Type filter is truthiness-based. No
/// Stop-Function, no accumulator, no interrupt, no value-passed switch, no ShouldProcess. The only other edits are
/// -FunctionName Get-DbaDbUserDefinedTableType on the two Write-Message. Source quirk preserved: the no-table-types
/// message references an undefined $instance (no foreach instance loop) -> empty. Surface pinned by
/// migration/baselines/Get-DbaDbUserDefinedTableType.json (positions 0-4, no ShouldProcess).
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbUserDefinedTableType")]
public sealed class GetDbaDbUserDefinedTableTypeCommand : DbaBaseCmdlet
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

    /// <summary>The database(s) to exclude.</summary>
    [Parameter(Position = 3)]
    public object[]? ExcludeDatabase { get; set; }

    /// <summary>Filter to the specified user-defined table type(s) by name.</summary>
    [Parameter(Position = 4)]
    public string[]? Type { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, SqlCredential, Database, ExcludeDatabase, Type, EnableException.ToBool(),
            TestBound(nameof(SqlInstance)), BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
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
    // PS: the process block VERBATIM. Edits: -FunctionName Get-DbaDbUserDefinedTableType on the two Write-Message; -ModuleName "dbatools"
    // Test-Bound SqlInstance -> $__boundSqlInstance (carried flag). The three continues are inside foreach loops -
    // loop-bound. The system-object skip is a plain truthiness check (not a parameter).
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $ExcludeDatabase, $Type, $EnableException, $__boundSqlInstance, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [object[]]$Database, [object[]]$ExcludeDatabase, [string[]]$Type, $EnableException, $__boundSqlInstance, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        if ($__boundSqlInstance) {
            $InputObject = Get-DbaDatabase -SqlInstance $SqlInstance -SqlCredential $SqlCredential -Database $Database -ExcludeDatabase $ExcludeDatabase
        }

        foreach ($db in $InputObject) {
            if (!$db.IsAccessible) {
                Write-Message -Level Warning -Message "Database $db is not accessible. Skipping." -FunctionName Get-DbaDbUserDefinedTableType -ModuleName "dbatools"
                continue
            }
            if ($db.UserDefinedTableTypes.Count -eq 0) {
                Write-Message -Message "No User Defined Table Types exist in the $db database on $instance" -Target $db -Level Output -FunctionName Get-DbaDbUserDefinedTableType -ModuleName "dbatools"
                continue
            }

            if ($Type) {
                $userDefinedTableTypes = $db.UserDefinedTableTypes | Where-Object Name -in $Type
            } else {
                $userDefinedTableTypes = $db.UserDefinedTableTypes
            }

            foreach ($tabletype in $userDefinedTableTypes) {
                if ( $tabletype.IsSystemObject ) {
                    continue
                }

                Add-Member -Force -InputObject $tabletype -MemberType NoteProperty -Name ComputerName -value $tabletype.Parent.ComputerName
                Add-Member -Force -InputObject $tabletype -MemberType NoteProperty -Name InstanceName -value $tabletype.Parent.InstanceName
                Add-Member -Force -InputObject $tabletype -MemberType NoteProperty -Name SqlInstance -value $tabletype.Parent.SqlInstance
                Add-Member -Force -InputObject $tabletype -MemberType NoteProperty -Name Database -value $db.Name

                $defaults = ('ComputerName', 'InstanceName', 'SqlInstance' , 'Database' , 'ID', 'Name', 'Columns', 'Owner', 'CreateDate', 'IsSystemObject', 'Version')

                Select-DefaultView -InputObject $tabletype -Property $defaults
            }
        }
} $SqlInstance $SqlCredential $Database $ExcludeDatabase $Type $EnableException $__boundSqlInstance $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
