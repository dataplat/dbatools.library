#nullable enable

using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Returns database mirroring pairs and their witness state. Port of
/// public/Get-DbaDbMirror.ps1; surface pinned by
/// migration/baselines/Get-DbaDbMirror.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbMirror")]
public sealed class GetDbaDbMirrorCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instances using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Restricts results to these databases.</summary>
    [Parameter(Position = 2)]
    public string[]? Database { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        // Whole-record hop with ZERO substitutions: the source has no Stop-Function or
        // Write-Message sites at all - the body is the source process bytes unmodified
        // (CRLF-preserved). The catch's bare `continue` targets the enclosing source
        // foreach - loop-local in both worlds.
        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, SqlCredential, Database, EnableException.ToBool(),
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

    // PS: the source process block VERBATIM, CRLF-preserved, ZERO substitutions
    // (cmp-proven byte-exact against the raw source slice - no strip step needed).
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [string[]]$Database, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        foreach ($instance in $SqlInstance) {
            $dbs = Get-DbaDatabase -SqlInstance $instance -SqlCredential $SqlCredential -Database $Database
            $partners = $dbs | Where-Object MirroringPartner
            $partners | Select-DefaultView -Property ComputerName, InstanceName, SqlInstance, Name, MirroringSafetyLevel, MirroringStatus, MirroringPartner, MirroringPartnerInstance, MirroringFailoverLogSequenceNumber, MirroringID, MirroringRedoQueueMaxSize, MirroringRoleSequence, MirroringSafetySequence, MirroringTimeout, MirroringWitness, MirroringWitnessStatus

            # The witness is kinda hidden. Go get it manually.
            try {
                $witnesses = $dbs[0].Parent.Query("SELECT DISTINCT database_name, principal_server_name, safety_level, safety_level_desc, partner_sync_state FROM master.sys.database_mirroring_witnesses")
            } catch { continue }

            foreach ($witness in $witnesses) {
                $witnessdb = $dbs | Where-Object Name -eq $witness.database_name
                $status = switch ($witness.partner_sync_state) {
                    0 { "None" }
                    1 { "Suspended" }
                    2 { "Disconnected" }
                    3 { "Synchronizing" }
                    4 { "PendingFailover" }
                    5 { "Synchronized" }
                }

                foreach ($db in $witnessdb) {
                    Add-Member -InputObject $db -Force -MemberType NoteProperty -Name MirroringPartner -Value $witness.principal_server_name
                    Add-Member -InputObject $db -Force -MemberType NoteProperty -Name MirroringSafetyLevel -Value $witness.safety_level_desc
                    Add-Member -InputObject $db -Force -MemberType NoteProperty -Name MirroringWitnessStatus -Value $status
                    Select-DefaultView -InputObject $db -Property ComputerName, InstanceName, SqlInstance, Name, MirroringSafetyLevel, MirroringStatus, MirroringPartner, MirroringPartnerInstance, MirroringFailoverLogSequenceNumber, MirroringID, MirroringRedoQueueMaxSize, MirroringRoleSequence, MirroringSafetySequence, MirroringTimeout, MirroringWitness, MirroringWitnessStatus
                }
            }
        }
} $SqlInstance $SqlCredential $Database $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
