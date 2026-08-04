#nullable enable

using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Copies server-level triggers between instances. Port of public/Copy-DbaInstanceTrigger.ps1. The
/// workflow rides one module-scoped PowerShell hop: it leans on the private Get-ErrorMessage and
/// Select-DefaultView, and on SMO Trigger.Script() plus Server.Query, so the engine semantics stay
/// where they were. The compiled cmdlet supplies the real ShouldProcess runtime. Surface pinned by
/// migration/baselines/Copy-DbaInstanceTrigger.json.
/// </summary>
[Cmdlet(VerbsCommon.Copy, "DbaInstanceTrigger", DefaultParameterSetName = "Default",
    SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Medium)]
public sealed class CopyDbaInstanceTriggerCommand : DbaBaseCmdlet
{
    /// <summary>The source SQL Server instance. Requires sysadmin and SQL Server 2005 or higher.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    public DbaInstanceParameter Source { get; set; } = null!;

    /// <summary>Alternative credential for the source instance.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SourceSqlCredential { get; set; }

    /// <summary>The destination SQL Server instances.</summary>
    [Parameter(Mandatory = true, Position = 2)]
    public DbaInstanceParameter[] Destination { get; set; } = null!;

    /// <summary>Alternative credential for destination instances.</summary>
    [Parameter(Position = 3)]
    public PSCredential? DestinationSqlCredential { get; set; }

    /// <summary>Only copy the server triggers with these names.</summary>
    [Parameter(Position = 4)]
    public object[]? ServerTrigger { get; set; }

    /// <summary>Skip the server triggers with these names.</summary>
    [Parameter(Position = 5)]
    public object[]? ExcludeServerTrigger { get; set; }

    /// <summary>Drop and recreate server triggers that already exist on the destination.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // Begin and process stay in one hop, and there are no carried locals: no parameter takes
    // pipeline input, so ProcessRecord runs exactly once and nothing can survive between records.
    // That merge is also what keeps $eol working - it is assigned where begin used to be and read
    // in the destination loop, the way $ConfirmPreference is. Test-FunctionInterrupt still earns
    // its line inside the script: the begin half's source connect has to stop the destination loop
    // that follows it in the same invocation.
    protected override void ProcessRecord()
    {
        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                NestedCommand.RemoveDuplicateError(this, nestedError);
                WriteError(NestedCommand.PreserveErrorIdentity(nestedError));
            }
            else
            {
                WriteObject(item);
            }
        }, BodyScript,
            Source, SourceSqlCredential, Destination, DestinationSqlCredential,
            ServerTrigger, ExcludeServerTrigger, Force.ToBool(),
            EnableException.ToBool(), this, NestedCommand.BoundCommonParameter(this, "Verbose"),
            NestedCommand.BoundCommonParameter(this, "Debug"));
    }

    private const string BodyScript = """
param($Source, $SourceSqlCredential, $Destination, $DestinationSqlCredential, $ServerTrigger, $ExcludeServerTrigger, $Force, $EnableException, $__realCmdlet, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    # $Force is deliberately untyped: PowerShell excludes [switch] parameters from positional
    # binding, so one typed flag would shift every argument after it. It arrives as a real boolean,
    # which the -eq $false test below reads the way a switch reads.
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter]$Source, $SourceSqlCredential, [Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$Destination, $DestinationSqlCredential, [object[]]$ServerTrigger, [object[]]$ExcludeServerTrigger, $Force, $EnableException, $__realCmdlet, $__boundVerbose, $__boundDebug)

    try {
        $sourceServer = Connect-DbaInstance -SqlInstance $Source -SqlCredential $SourceSqlCredential -MinimumVersion 9
    } catch {
        Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $Source -FunctionName Copy-DbaInstanceTrigger
        return
    }

    # Plural, and one letter away from the $ServerTrigger parameter that filters it below.
    $serverTriggers = $sourceServer.Triggers

    if ($Force) { $ConfirmPreference = 'none' }

    $eol = [System.Environment]::NewLine

    if (Test-FunctionInterrupt) { return }

    foreach ($destinstance in $Destination) {
        try {
            $destServer = Connect-DbaInstance -SqlInstance $destinstance -SqlCredential $DestinationSqlCredential -MinimumVersion 9
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $destinstance -Continue -FunctionName Copy-DbaInstanceTrigger
        }

        if ($destServer.VersionMajor -lt $sourceServer.VersionMajor) {
            Stop-Function -Message "Migration from version $($destServer.VersionMajor) to version $($sourceServer.VersionMajor) is not supported." -FunctionName Copy-DbaInstanceTrigger
            return
        }
        $destTriggers = $destServer.Triggers

        foreach ($trigger in $serverTriggers) {
            $triggerName = $trigger.Name

            $copyTriggerStatus = [PSCustomObject]@{
                SourceServer      = $sourceServer.Name
                DestinationServer = $destServer.Name
                Name              = $triggerName
                Type              = "Server Trigger"
                Status            = $null
                Notes             = $null
                DateTime          = [Dataplat.Dbatools.Utility.DbaDateTime](Get-Date)
            }

            if ($ServerTrigger -and $triggerName -notin $ServerTrigger -or $triggerName -in $ExcludeServerTrigger) {
                continue
            }

            if ($destTriggers.Name -contains $triggerName) {
                if ($force -eq $false) {
                    If ($__realCmdlet.ShouldProcess($destinstance, "Server trigger $triggerName exists at destination. Use -Force to drop and migrate")) {
                        Write-Message -Level Verbose -Message "Server trigger $triggerName exists at destination. Use -Force to drop and migrate." -FunctionName Copy-DbaInstanceTrigger -ModuleName "dbatools"
                        $copyTriggerStatus.Status = "Skipped"
                        $copyTriggerStatus.Notes = "Already exists on destination"
                        $copyTriggerStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                    }
                    continue
                } else {
                    if ($__realCmdlet.ShouldProcess($destinstance, "Dropping server trigger $triggerName and recreating")) {
                        try {
                            Write-Message -Level Verbose -Message "Dropping server trigger $triggerName" -FunctionName Copy-DbaInstanceTrigger -ModuleName "dbatools"
                            $destServer.Triggers[$triggerName].Drop()
                        } catch {
                            $copyTriggerStatus.Status = "Failed"
                            $copyTriggerStatus.Notes = (Get-ErrorMessage -Record $_)
                            $copyTriggerStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                            Write-Message -Level Verbose -Message "Issue dropping trigger $triggerName on $destinstance | $PSItem" -FunctionName Copy-DbaInstanceTrigger -ModuleName "dbatools"
                            continue
                        }
                    }
                }
            }

            if ($__realCmdlet.ShouldProcess($destinstance, "Creating server trigger $triggerName")) {
                try {
                    Write-Message -Level Verbose -Message "Copying server trigger $triggerName" -FunctionName Copy-DbaInstanceTrigger -ModuleName "dbatools"
                    $sql = $trigger.Script() | Out-String
                    # Unanchored on purpose, both of them: the rewrite is what turns the scripted
                    # definition into separate batches, and CREATE TRIGGER has to be first in its
                    # own batch or the copy cannot run at all. It also cuts on the same words
                    # wherever they appear inside a trigger body, which is carried over as-is.
                    $sql = $sql -replace "CREATE ", "$($eol)GO$($eol)CREATE "
                    $sql = $sql -replace "ENABLE TRIGGER", "$($eol)GO$($eol)ENABLE TRIGGER"
                    Write-Message -Level Debug -Message $sql -FunctionName Copy-DbaInstanceTrigger -ModuleName "dbatools"
                    foreach ($query in ($sql -split '\nGO\b')) {
                        $destServer.Query($query) | Out-Null
                    }
                    $copyTriggerStatus.Status = "Successful"
                    $copyTriggerStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                } catch {
                    $copyTriggerStatus.Status = "Failed"
                    $copyTriggerStatus.Notes = (Get-ErrorMessage -Record $_)
                    $copyTriggerStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                    Write-Message -Level Verbose -Message "Issue creating trigger $triggerName on $destinstance | $PSItem" -FunctionName Copy-DbaInstanceTrigger -ModuleName "dbatools"
                    continue
                }
            }
        }
    }
} $Source $SourceSqlCredential $Destination $DestinationSqlCredential $ServerTrigger $ExcludeServerTrigger $Force $EnableException $__realCmdlet $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
