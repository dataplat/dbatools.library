#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Enables SQL Server startup stored procedures. Port of
/// public/Enable-DbaStartupProcedure.ps1 (W3-012). Sibling of W3-008 Disable-DbaStartupProcedure
/// but structurally distinct: no InputObject parameter and no Test-Bound guards, StartupProcedure
/// is [object[]], and the emit (Select-DefaultView) is INSIDE the nested foreach so it fires once
/// PER procedure. The begin block only sets the $action ('Enable') and $startup ($true) constants,
/// so there is NO cross-record accumulator and NO end hop - the begin constants inline into the
/// process script. DEF-001 cond1+cond2: the process foreach EMITS per procedure AND has reachable
/// Stop-Function -Continue at Connect-DbaInstance / the procedure-parse checks, so the hop STREAMS
/// via InvokeScopedStreaming - a buffered hop would lose an earlier procedure's emit when a later
/// one throws under -EnableException. Substitutions only: $Pscmdlet.ShouldProcess ->
/// $__realCmdlet.ShouldProcess (ConfirmImpact HIGH mirrored) and explicit -FunctionName
/// Enable-DbaStartupProcedure on every Stop-Function (W1-090); the body is otherwise verbatim.
/// Surface pinned by migration/baselines/Enable-DbaStartupProcedure.json.
/// </summary>
[Cmdlet(VerbsLifecycle.Enable, "DbaStartupProcedure", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.High, DefaultParameterSetName = "Default")]
public sealed class EnableDbaStartupProcedureCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The startup procedure(s) to enable (schema.name).</summary>
    [Parameter(Position = 2)]
    public object[]? StartupProcedure { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                NestedCommand.RemoveDuplicateError(this, nestedError);
                WriteError(nestedError);
                return;
            }
            WriteObject(item);
        }, ProcessScript,
            SqlInstance, SqlCredential, StartupProcedure, EnableException.ToBool(), this,
            NestedCommand.BoundCommonParameter(this, "WhatIf"), NestedCommand.BoundCommonParameter(this, "Confirm"),
            NestedCommand.BoundCommonParameter(this, "Verbose"), NestedCommand.BoundCommonParameter(this, "Debug"));
    }

    // PS: the begin constants ($action / $startup) inline ahead of the process body, which is
    // VERBATIM per record. Substitutions only: $Pscmdlet -> $__realCmdlet, explicit -FunctionName
    // Enable-DbaStartupProcedure on Stop-Function (W1-090).
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $StartupProcedure, $EnableException, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = "High")]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [object[]]$StartupProcedure, $EnableException, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    $action = 'Enable'
    $startup = $true

    foreach ($instance in $SqlInstance) {
        try {
            $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Enable-DbaStartupProcedure
        }
        Write-Message -Level Verbose -Message "Getting startup procedures for $instance" -FunctionName Enable-DbaStartupProcedure -ModuleName "dbatools"

        $db = $server.Databases['master']

        foreach ($proc in $StartupProcedure) {
            Write-Message -Level Verbose -Message "Preparing to get object parts for $proc" -FunctionName Enable-DbaStartupProcedure -ModuleName "dbatools"
            $procParts = Get-ObjectNameParts $proc

            if ($procParts.Parsed) {
                $sp = $db.StoredProcedures.Item($procParts.Name, $procParts.Schema)

                if ($null -eq $sp) {
                    Stop-Function -Message "Requested procedure $proc does not exist." -Continue -Target $server -Category InvalidData -FunctionName Enable-DbaStartupProcedure
                } else {
                    try {
                        if ($sp.Startup -eq $startup) {
                            Write-Message -Level Verbose -Message "No work being performed. Startup property already $startup" -FunctionName Enable-DbaStartupProcedure -ModuleName "dbatools"
                            $status = $false
                            $note = "Action $action already performed"
                        } else {
                            if ($__realCmdlet.ShouldProcess("$instance", "Setting Startup status of $proc to $startup")) {
                                $sp.Startup = $startup
                                $sp.Alter()
                                $status = $true
                                $note = "$action succeded"
                            } else {
                                $status = $false
                                $note = "$action skipped"
                            }
                        }

                    } catch {
                        $status = $false
                        $note = "$action failed"
                    }
                }

            } else {
                Stop-Function -Message "Requested procedure $proc could not be parsed." -Continue -Target $server -Category InvalidData -FunctionName Enable-DbaStartupProcedure
            }

            Add-Member -Force -InputObject $sp -MemberType NoteProperty -Name ComputerName -value $server.ComputerName
            Add-Member -Force -InputObject $sp -MemberType NoteProperty -Name InstanceName -value $server.ServiceName
            Add-Member -Force -InputObject $sp -MemberType NoteProperty -Name SqlInstance -value $server.DomainInstanceName
            Add-Member -Force -InputObject $sp -MemberType NoteProperty -Name Database -value $db.Name
            Add-Member -Force -InputObject $sp -MemberType NoteProperty -Name Action -value $action
            Add-Member -Force -InputObject $sp -MemberType NoteProperty -Name Status -value $status
            Add-Member -Force -InputObject $sp -MemberType NoteProperty -Name Note -value $note

            $defaults = 'ComputerName', 'InstanceName', 'SqlInstance', 'Database', 'Schema', 'Name', 'Startup', 'Action', 'Status', 'Note'
            Select-DefaultView -InputObject $sp -Property $defaults
        }
    }
} $SqlInstance $SqlCredential $StartupProcedure $EnableException $__realCmdlet $__boundWhatIf $__boundConfirm $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
