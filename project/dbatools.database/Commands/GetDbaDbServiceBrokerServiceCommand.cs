#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves Service Broker service objects and metadata from databases. Port of
/// public/Get-DbaDbServiceBrokerService.ps1; the workflow remains a module-scoped PowerShell compatibility hop.
///
/// A process-only port and the EXACT structural twin of Get-DbaDbServiceBrokerQueue (W2-105). TWO Test-Bound
/// become carried flags: the positional Test-Bound SqlInstance -> $__boundSqlInstance (gates the Get-DbaDatabase
/// gather into the LOCAL $InputObject - not a parameter, assigned only when SqlInstance is bound), and
/// Test-Bound -ParameterName ExcludeSystemService -> $__boundExcludeSystemService (gates the system-service skip;
/// boundness quirk preserved). ExcludeSystemService is consumed ONLY via Test-Bound, so it is an untyped carried
/// flag - NOT a value-passed [switch] in the inner param (no positional-binding hazard). The three continue
/// statements are all inside foreach loops - loop-bound. No Stop-Function, no accumulator, no interrupt, no
/// ShouldProcess. The only other edits are -FunctionName Get-DbaDbServiceBrokerService on the two Write-Message.
/// Surface pinned by migration/baselines/Get-DbaDbServiceBrokerService.json (positions 0-3, ExcludeSystemService switch non-positional, no ShouldProcess).
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbServiceBrokerService")]
public sealed class GetDbaDbServiceBrokerServiceCommand : DbaBaseCmdlet
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

    /// <summary>Exclude system Service Broker services from the results.</summary>
    [Parameter]
    public SwitchParameter ExcludeSystemService { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, SqlCredential, Database, ExcludeDatabase, EnableException.ToBool(),
            TestBound(nameof(SqlInstance)), TestBound(nameof(ExcludeSystemService)),
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
    // PS: the process block VERBATIM. Edits: -FunctionName Get-DbaDbServiceBrokerService on the two Write-Message;
    // Test-Bound SqlInstance -> $__boundSqlInstance and Test-Bound -ParameterName ExcludeSystemService ->
    // $__boundExcludeSystemService (carried flags). The three continues are inside foreach loops - loop-bound.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $ExcludeDatabase, $EnableException, $__boundSqlInstance, $__boundExcludeSystemService, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [object[]]$Database, [object[]]$ExcludeDatabase, $EnableException, $__boundSqlInstance, $__boundExcludeSystemService, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        if ($__boundSqlInstance) {
            $InputObject = Get-DbaDatabase -SqlInstance $SqlInstance -SqlCredential $SqlCredential -Database $Database -ExcludeDatabase $ExcludeDatabase
        }

        foreach ($db in $InputObject) {
            if (!$db.IsAccessible) {
                Write-Message -Level Warning -Message "Database $db is not accessible. Skipping." -FunctionName Get-DbaDbServiceBrokerService
                continue
            }
            if ($db.ServiceBroker.Services.Count -eq 0) {
                Write-Message -Message "No Service Broker Services exist in the $db database on $instance" -Target $db -Level Output -FunctionName Get-DbaDbServiceBrokerService
                continue
            }

            foreach ($service in $db.ServiceBroker.Services) {
                if ( ($__boundExcludeSystemService) -and $service.IsSystemObject ) {
                    continue
                }

                Add-Member -Force -InputObject $service -MemberType NoteProperty -Name ComputerName -value $service.Parent.Parent.ComputerName
                Add-Member -Force -InputObject $service -MemberType NoteProperty -Name InstanceName -value $service.Parent.Parent.InstanceName
                Add-Member -Force -InputObject $service -MemberType NoteProperty -Name SqlInstance -value $service.Parent.Parent.SqlInstance
                Add-Member -Force -InputObject $service -MemberType NoteProperty -Name Database -value $db.Name

                $defaults = 'ComputerName', 'InstanceName', 'SqlInstance', 'Database', 'Owner', 'ID as ServiceID', 'Name', 'QueueSchema', 'QueueName'
                Select-DefaultView -InputObject $service -Property $defaults
            }
        }
} $SqlInstance $SqlCredential $Database $ExcludeDatabase $EnableException $__boundSqlInstance $__boundExcludeSystemService $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}