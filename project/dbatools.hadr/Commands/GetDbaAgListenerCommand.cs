#nullable enable

using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Returns availability group listeners with port and cluster IP context, from
/// instances or piped availability groups. Port of public/Get-DbaAgListener.ps1; surface pinned by
/// migration/baselines/Get-DbaAgListener.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaAgListener")]
public sealed class GetDbaAgListenerCommand : DbaBaseCmdlet
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

    /// <summary>Restricts results to these listeners.</summary>
    [Parameter(Position = 3)]
    public string[]? Listener { get; set; }

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
            SqlInstance, SqlCredential, AvailabilityGroup, Listener,
            InputObject, EnableException.ToBool(),
            TestBound(nameof(SqlInstance)), TestBound(nameof(InputObject)), TestBound(nameof(Listener)),
            NestedCommand.BoundCommonParameter(this, "Verbose"), NestedCommand.BoundCommonParameter(this, "Debug")))
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                NestedCommand.RemoveDuplicateError(this, nestedError);
                WriteError(nestedError);
                continue;
            }
            WriteObject(item);
        }
    }

    // PS: the source process block VERBATIM (EOL-normalized like every sliced body).
    // Substitutions: one -FunctionName append on the loop-less Stop-Function plus TWO
    // Test-Bound rewrites to carried bound flags (the multi-name validation and the
    // Listener filter gate, SOURCE comments per site); the source's `return` after
    // the validation exits the record identically in both worlds. The dot-block
    // preserves that early return without skipping the hop frame.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $AvailabilityGroup, $Listener, $InputObject, $EnableException, $__boundSqlInstance, $__boundInputObject, $__boundListener, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [string[]]$AvailabilityGroup, [string[]]$Listener, [Microsoft.SqlServer.Management.Smo.AvailabilityGroup[]]$InputObject, $EnableException, $__boundSqlInstance, $__boundInputObject, $__boundListener, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    . {
        if (-not ($__boundSqlInstance -or $__boundInputObject)) { # SOURCE: if (Test-Bound -Not SqlInstance, InputObject) {
            Stop-Function -Message "You must supply either -SqlInstance or an Input Object" -FunctionName Get-DbaAgListener
            return
        }

        if ($SqlInstance) {
            $InputObject += Get-DbaAvailabilityGroup -SqlInstance $SqlInstance -SqlCredential $SqlCredential -AvailabilityGroup $AvailabilityGroup -EnableException:$EnableException
        }

        $agListeners = $InputObject.AvailabilityGroupListeners
        if ($__boundListener) { # SOURCE: if (Test-Bound -ParameterName Listener) {
            $agListeners = $agListeners | Where-Object { $Listener -contains $_.Name }
        }

        $defaults = 'ComputerName', 'InstanceName', 'SqlInstance', 'AvailabilityGroup', 'Name', 'PortNumber', 'ClusterIPConfiguration'

        foreach ($agListener in $agListeners) {
            $server = $agListener.Parent.Parent
            Add-Member -Force -InputObject $agListener -MemberType NoteProperty -Name ComputerName -value $server.ComputerName
            Add-Member -Force -InputObject $agListener -MemberType NoteProperty -Name InstanceName -value $server.ServiceName
            Add-Member -Force -InputObject $agListener -MemberType NoteProperty -Name SqlInstance -value $server.DomainInstanceName
            Add-Member -Force -InputObject $agListener -MemberType NoteProperty -Name AvailabilityGroup -value $agListener.Parent.Name
            Select-DefaultView -InputObject $agListener -Property $defaults
        }
    }
} $SqlInstance $SqlCredential $AvailabilityGroup $Listener $InputObject $EnableException $__boundSqlInstance $__boundInputObject $__boundListener $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
