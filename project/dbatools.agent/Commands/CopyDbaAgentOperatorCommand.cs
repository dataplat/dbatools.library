#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Copies SQL Agent operators while protecting the destination failsafe operator. Port of
/// public/Copy-DbaAgentOperator.ps1 (W2-005). The complete workflow remains a module-scoped
/// PowerShell compatibility hop so source filtering, SMO scripting, output ordering, and legacy
/// quirks retain engine semantics. The compiled cmdlet supplies the real ShouldProcess runtime.
/// Surface pinned by migration/baselines/Copy-DbaAgentOperator.json.
/// </summary>
[Cmdlet(VerbsCommon.Copy, "DbaAgentOperator", DefaultParameterSetName = "Default",
    SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Medium)]
public sealed class CopyDbaAgentOperatorCommand : DbaBaseCmdlet
{
    /// <summary>Source SQL Server instance.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    public DbaInstanceParameter Source { get; set; } = null!;

    /// <summary>Alternative credential for the source instance.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SourceSqlCredential { get; set; }

    /// <summary>Destination SQL Server instances.</summary>
    [Parameter(Mandatory = true, Position = 2)]
    public DbaInstanceParameter[] Destination { get; set; } = null!;

    /// <summary>Alternative credential for destination instances.</summary>
    [Parameter(Position = 3)]
    public PSCredential? DestinationSqlCredential { get; set; }

    /// <summary>Only copy operators with these names.</summary>
    [Parameter(Position = 4)]
    public object[]? Operator { get; set; }

    /// <summary>Exclude operators with these names.</summary>
    [Parameter(Position = 5)]
    public object[]? ExcludeOperator { get; set; }

    /// <summary>Drop and recreate existing operators except the failsafe operator.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BodyScript,
            Source, SourceSqlCredential, Destination, DestinationSqlCredential,
            Operator, ExcludeOperator, Force.ToBool(), EnableException.ToBool(), this,
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
            }
            else
            {
                WriteObject(item);
            }
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

    private const string BodyScript = """
param($Source, $SourceSqlCredential, $Destination, $DestinationSqlCredential, $Operator, $ExcludeOperator, $Force, $EnableException, $__realCmdlet, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter]$Source, $SourceSqlCredential, [Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$Destination, $DestinationSqlCredential, [object[]]$Operator, [object[]]$ExcludeOperator, $Force, $EnableException, $__realCmdlet, $__boundVerbose, $__boundDebug)

    try {
        $sourceServer = Connect-DbaInstance -SqlInstance $Source -SqlCredential $SourceSqlCredential
    } catch {
        Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $Source -FunctionName Copy-DbaAgentOperator
        return
    }
    $serverOperator = $sourceServer.JobServer.Operators
    if ($Force) { $ConfirmPreference = 'none' }

    if (Test-FunctionInterrupt) { return }
    foreach ($destinstance in $Destination) {
        try {
            $destServer = Connect-DbaInstance -SqlInstance $destinstance -SqlCredential $DestinationSqlCredential
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $destinstance -Continue -FunctionName Copy-DbaAgentOperator
        }

        $destOperator = $destServer.JobServer.Operators
        $failsafe = $destServer.JobServer.AlertSystem | Select-Object FailSafeOperator
        foreach ($sOperator in $serverOperator) {
            $operatorName = $sOperator.Name
            $copyOperatorStatus = [PSCustomObject]@{
                SourceServer      = $sourceServer.Name
                DestinationServer = $destServer.Name
                Name              = $operatorName
                Type              = "Agent Operator"
                Status            = $null
                Notes             = $null
                DateTime          = [Dataplat.Dbatools.Utility.DbaDateTime](Get-Date)
            }

            if ($Operator -and $Operator -notcontains $operatorName -or $ExcludeOperator -in $operatorName) {
                continue
            }

            if ($destOperator.Name -contains $sOperator.Name) {
                if ($force -eq $false) {
                    if ($__realCmdlet.ShouldProcess($destinstance, "Operator $operatorName exists at destination. Use -Force to drop and migrate.")) {
                        $copyOperatorStatus.Status = "Skipped"
                        $copyOperatorStatus.Notes = "Already exists on destination"
                        $copyOperatorStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                        Write-Message -Level Verbose -Message "Operator $operatorName exists at destination. Use -Force to drop and migrate." -FunctionName Copy-DbaAgentOperator
                    }
                    continue
                } else {
                    if ($failsafe.FailSafeOperator -eq $operatorName) {
                        Write-Message -Level Verbose -Message "$operatorName is the failsafe operator. Skipping drop." -FunctionName Copy-DbaAgentOperator
                        continue
                    }

                    if ($__realCmdlet.ShouldProcess($destinstance, "Dropping operator $operatorName and recreating")) {
                        try {
                            Write-Message -Level Verbose -Message "Dropping Operator $operatorName" -FunctionName Copy-DbaAgentOperator
                            $destServer.JobServer.Operators[$operatorName].Drop()
                        } catch {
                            $copyOperatorStatus.Status = "Failed"
                            $copyOperatorStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                            Write-Message -Level Verbose -Message "Issue dropping operator $operatorName on $destinstance | $PSItem" -FunctionName Copy-DbaAgentOperator
                            continue
                        }
                    }
                }
            }

            if ($__realCmdlet.ShouldProcess($destinstance, "Creating Operator $operatorName")) {
                try {
                    Write-Message -Level Verbose -Message "Copying Operator $operatorName" -FunctionName Copy-DbaAgentOperator
                    $sql = $sOperator.Script() | Out-String
                    Write-Message -Level Debug -Message $sql -FunctionName Copy-DbaAgentOperator
                    $destServer.Query($sql)
                    $destServer.JobServer.Operators.Refresh()

                    $copyOperatorStatus.Status = "Successful"
                    $copyOperatorStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                } catch {
                    $copyOperatorStatus.Status = "Failed"
                    $copyOperatorStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                    Write-Message -Level Verbose -Message "Issue creating operator $operatorName on $destinstance | $PSItem" -FunctionName Copy-DbaAgentOperator
                    continue
                }
            }
        }
    }
} $Source $SourceSqlCredential $Destination $DestinationSqlCredential $Operator $ExcludeOperator $Force $EnableException $__realCmdlet $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
