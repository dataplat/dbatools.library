#nullable enable

using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Returns availability group databases with replica-role context, from instances or
/// piped availability groups. Port of public/Get-DbaAgDatabase.ps1; surface pinned by
/// migration/baselines/Get-DbaAgDatabase.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaAgDatabase")]
public sealed class GetDbaAgDatabaseCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instances using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Restricts results to these availability groups.</summary>
    [Parameter(Position = 2)]
    public string[]? AvailabilityGroup { get; set; }

    /// <summary>Restricts results to these databases.</summary>
    [Parameter(Position = 3)]
    public string[]? Database { get; set; }

    /// <summary>Excludes these databases from the results.</summary>
    [Parameter(Position = 4)]
    public string[]? ExcludeDatabase { get; set; }

    /// <summary>Availability group objects piped from Get-DbaAvailabilityGroup.</summary>
    [Parameter(ValueFromPipeline = true, Position = 5)]
    public Microsoft.SqlServer.Management.Smo.AvailabilityGroup[]? InputObject { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        // Whole-record hop: the source process has no per-instance foreach at top level
        // (the $InputObject += accumulation and database loop share record scope), and
        // the loop-less Stop-Function + return exits the record in both worlds. The
        // Test-Bound flags are computed per record - pipeline binding adds InputObject
        // to BoundParameters only on records that actually bound it.
        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, SqlCredential, AvailabilityGroup, Database, ExcludeDatabase,
            InputObject, EnableException.ToBool(),
            TestBound(nameof(SqlInstance)), TestBound(nameof(InputObject)),
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
        {
            return LanguagePrimitives.IsTrue(value);
        }
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
                string.Equals(first.Exception?.Message, record.Exception?.Message, System.StringComparison.Ordinal))
            {
                errorList.RemoveAt(0);
            }
        }
        catch
        {
            // Best-effort bookkeeping only.
        }
    }

    // PS: the source process block VERBATIM (EOL-normalized like every sliced body).
    // Substitutions: one -FunctionName append on the loop-less Stop-Function and the
    // multi-name Test-Bound -> carried bound flags (SOURCE comment); the source's
    // `return` after it exits the record identically in both worlds. The dot-block
    // preserves that early return without skipping the hop frame.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $AvailabilityGroup, $Database, $ExcludeDatabase, $InputObject, $EnableException, $__boundSqlInstance, $__boundInputObject, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [string[]]$AvailabilityGroup, [string[]]$Database, [string[]]$ExcludeDatabase, [Microsoft.SqlServer.Management.Smo.AvailabilityGroup[]]$InputObject, $EnableException, $__boundSqlInstance, $__boundInputObject, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    . {
        if (-not ($__boundSqlInstance -or $__boundInputObject)) { # SOURCE: if (Test-Bound -Not SqlInstance, InputObject) {
            Stop-Function -Message "You must supply either -SqlInstance or an Input Object" -FunctionName Get-DbaAgDatabase
            return
        }

        if ($SqlInstance) {
            $InputObject += Get-DbaAvailabilityGroup -SqlInstance $SqlInstance -SqlCredential $SqlCredential -AvailabilityGroup $AvailabilityGroup
        }

        foreach ($db in $InputObject.AvailabilityDatabases) {
            if ($Database -and $db.Name -notin $Database) { continue }
            if ($ExcludeDatabase -and $db.Name -in $ExcludeDatabase) { continue }
            $ag = $db.Parent
            $server = $db.Parent.Parent
            Add-Member -Force -InputObject $db -MemberType NoteProperty -Name ComputerName -Value $server.ComputerName
            Add-Member -Force -InputObject $db -MemberType NoteProperty -Name InstanceName -Value $server.ServiceName
            Add-Member -Force -InputObject $db -MemberType NoteProperty -Name SqlInstance -Value $server.DomainInstanceName
            Add-Member -Force -InputObject $db -MemberType NoteProperty -Name AvailabilityGroup -Value $ag.Name
            Add-Member -Force -InputObject $db -MemberType NoteProperty -Name LocalReplicaRole -Value $ag.LocalReplicaRole

            $defaults = 'ComputerName', 'InstanceName', 'SqlInstance', 'AvailabilityGroup', 'LocalReplicaRole', 'Name', 'SynchronizationState', 'IsFailoverReady', 'IsJoined', 'IsSuspended'
            Select-DefaultView -InputObject $db -Property $defaults
        }
    }
} $SqlInstance $SqlCredential $AvailabilityGroup $Database $ExcludeDatabase $InputObject $EnableException $__boundSqlInstance $__boundInputObject $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
