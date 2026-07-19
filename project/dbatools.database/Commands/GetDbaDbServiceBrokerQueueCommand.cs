#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves Service Broker queue objects and metadata from databases. Port of
/// public/Get-DbaDbServiceBrokerQueue.ps1; the workflow remains a module-scoped PowerShell compatibility hop.
///
/// A process-only port (SqlInstance is ValueFromPipeline). TWO Test-Bound checks become carried flags: the
/// positional Test-Bound SqlInstance -> $__boundSqlInstance (gates the Get-DbaDatabase gather into the local
/// $InputObject), and Test-Bound -ParameterName ExcludeSystemQueue -> $__boundExcludeSystemQueue (gates the
/// system-queue skip; boundness quirk preserved - -ExcludeSystemQueue:$false still excludes). ExcludeSystemQueue
/// is consumed ONLY via Test-Bound, so it is an untyped carried flag - NOT a value-passed [switch] in the inner
/// param (no positional-binding risk). The three continue statements are all inside foreach loops - loop-bound.
/// No Stop-Function, no accumulator, no interrupt, no ShouldProcess. The only other edits are -FunctionName
/// Get-DbaDbServiceBrokerQueue on the two Write-Message. Surface pinned by
/// migration/baselines/Get-DbaDbServiceBrokerQueue.json (positions 0-3, ExcludeSystemQueue switch non-positional, no ShouldProcess).
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbServiceBrokerQueue")]
public sealed class GetDbaDbServiceBrokerQueueCommand : DbaBaseCmdlet
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

    /// <summary>Exclude system Service Broker queues from the results.</summary>
    [Parameter]
    public SwitchParameter ExcludeSystemQueue { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, SqlCredential, Database, ExcludeDatabase, EnableException.ToBool(),
            TestBound(nameof(SqlInstance)), TestBound(nameof(ExcludeSystemQueue)),
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
    // PS: the process block VERBATIM. Edits: -FunctionName Get-DbaDbServiceBrokerQueue on the two Write-Message; -ModuleName "dbatools"
    // Test-Bound SqlInstance -> $__boundSqlInstance and Test-Bound -ParameterName ExcludeSystemQueue ->
    // $__boundExcludeSystemQueue (carried flags). The three continues are inside foreach loops - loop-bound.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $ExcludeDatabase, $EnableException, $__boundSqlInstance, $__boundExcludeSystemQueue, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [object[]]$Database, [object[]]$ExcludeDatabase, $EnableException, $__boundSqlInstance, $__boundExcludeSystemQueue, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        if ($__boundSqlInstance) {
            $InputObject = Get-DbaDatabase -SqlInstance $SqlInstance -SqlCredential $SqlCredential -Database $Database -ExcludeDatabase $ExcludeDatabase
        }

        foreach ($db in $InputObject) {
            if (!$db.IsAccessible) {
                Write-Message -Level Warning -Message "Database $db is not accessible. Skipping." -FunctionName Get-DbaDbServiceBrokerQueue -ModuleName "dbatools"
                continue
            }
            if ($db.ServiceBroker.Queues.Count -eq 0) {
                Write-Message -Message "No Service Broker Queues exist in the $db database on $instance" -Target $db -Level Output -FunctionName Get-DbaDbServiceBrokerQueue -ModuleName "dbatools"
                continue
            }

            foreach ($queue in $db.ServiceBroker.Queues) {
                if ( ($__boundExcludeSystemQueue) -and $queue.IsSystemObject ) {
                    continue
                }

                Add-Member -Force -InputObject $queue -MemberType NoteProperty -Name ComputerName -value $queue.Parent.Parent.ComputerName
                Add-Member -Force -InputObject $queue -MemberType NoteProperty -Name InstanceName -value $queue.Parent.Parent.InstanceName
                Add-Member -Force -InputObject $queue -MemberType NoteProperty -Name SqlInstance -value $queue.Parent.Parent.SqlInstance
                Add-Member -Force -InputObject $queue -MemberType NoteProperty -Name Database -value $db.Name

                $defaults = 'ComputerName', 'InstanceName', 'SqlInstance', 'Database', 'Schema', 'ID as QueueID', 'CreateDate', 'DateLastModified', 'Name', 'ProcedureName', 'ProcedureSchema'
                Select-DefaultView -InputObject $queue -Property $defaults
            }
        }
} $SqlInstance $SqlCredential $Database $ExcludeDatabase $EnableException $__boundSqlInstance $__boundExcludeSystemQueue $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
