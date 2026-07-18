#nullable enable

using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Returns availability group replicas with role and synchronization context, from
/// instances or piped availability groups. Port of public/Get-DbaAgReplica.ps1; surface pinned by
/// migration/baselines/Get-DbaAgReplica.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaAgReplica")]
public sealed class GetDbaAgReplicaCommand : DbaBaseCmdlet
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

    /// <summary>Restricts results to these replicas.</summary>
    [Parameter(Position = 3)]
    public string[]? Replica { get; set; }

    /// <summary>Availability group objects piped from Get-DbaAvailabilityGroup.</summary>
    [Parameter(ValueFromPipeline = true, Position = 4)]
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
            SqlInstance, SqlCredential, AvailabilityGroup, Replica,
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
    // Substitutions: two -FunctionName appends (the validation Stop-Function and the
    // accumulation catch Stop-Function - both loop-less, each followed by a `return`
    // that exits the record identically in both worlds) plus the multi-name
    // Test-Bound rewrite to carried bound flags (SOURCE comment); the Replica filter
    // is a plain value check in the source and rides verbatim. The dot-block
    // preserves that early return without skipping the hop frame.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $AvailabilityGroup, $Replica, $InputObject, $EnableException, $__boundSqlInstance, $__boundInputObject, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [string[]]$AvailabilityGroup, [string[]]$Replica, [Microsoft.SqlServer.Management.Smo.AvailabilityGroup[]]$InputObject, $EnableException, $__boundSqlInstance, $__boundInputObject, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    . {
        if (-not ($__boundSqlInstance -or $__boundInputObject)) { # SOURCE: if (Test-Bound -Not SqlInstance, InputObject) {
            Stop-Function -Message "You must supply either -SqlInstance or an Input Object" -FunctionName Get-DbaAgReplica
            return
        }

        if ($SqlInstance) {
            try {
                $InputObject += Get-DbaAvailabilityGroup -SqlInstance $SqlInstance -SqlCredential $SqlCredential -AvailabilityGroup $AvailabilityGroup -EnableException
            } catch {
                Stop-Function -Message "Failure on $SqlInstance to obtain the availability group $AvailabilityGroup" -ErrorRecord $_ -FunctionName Get-DbaAgReplica
                return
            }
        }

        $availabilityReplicas = $InputObject.AvailabilityReplicas
        if ($Replica) {
            $availabilityReplicas = $InputObject.AvailabilityReplicas | Where-Object { $_.Name -in $Replica }
        }

        $defaults = 'ComputerName', 'InstanceName', 'SqlInstance', 'AvailabilityGroup', 'Name', 'Role', 'ConnectionState', 'RollupSynchronizationState', 'AvailabilityMode', 'BackupPriority', 'EndpointUrl', 'SessionTimeout', 'FailoverMode', 'ReadonlyRoutingList'

        foreach ($agreplica in $availabilityReplicas) {
            Add-Member -Force -InputObject $agreplica -MemberType NoteProperty -Name ComputerName -value $agreplica.Parent.ComputerName
            Add-Member -Force -InputObject $agreplica -MemberType NoteProperty -Name InstanceName -value $agreplica.Parent.InstanceName
            Add-Member -Force -InputObject $agreplica -MemberType NoteProperty -Name SqlInstance -value $agreplica.Parent.SqlInstance
            Add-Member -Force -InputObject $agreplica -MemberType NoteProperty -Name AvailabilityGroup -value $agreplica.Parent.Name
            Add-Member -Force -InputObject $agreplica -MemberType NoteProperty -Name Replica -value $agreplica.Name # backwards compat

            Select-DefaultView -InputObject $agreplica -Property $defaults
        }
    }
} $SqlInstance $SqlCredential $AvailabilityGroup $Replica $InputObject $EnableException $__boundSqlInstance $__boundInputObject $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
