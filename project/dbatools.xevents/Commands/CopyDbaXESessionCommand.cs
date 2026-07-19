#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Copies Extended Events sessions from a source SQL Server instance to one or more destinations.
/// </summary>
/// <remarks>
/// The source/destination connections, the session filtering, the ScriptCreate().GetScript() migration,
/// the drop-and-recreate under -Force, and the MigrationObject result projection all run the original
/// dbatools PowerShell body VERBATIM inside the dbatools module scope rather than being reimplemented in
/// C#, so the engine decides the observable details.
///
/// The command has no pipeline-bound parameter, so ProcessRecord runs exactly once; the begin body (connect
/// the source, build the filtered session list, and set $ConfirmPreference under -Force) and the process
/// body run together in a SINGLE hop, so the cross-block $sourceServer/$sourceStore/$storeSessions locals
/// persist naturally. The source-connect failure Stop-Function (no -Continue) returns from that hop, exactly
/// as the source's begin return + the process Test-FunctionInterrupt guard leave nothing to do.
///
/// SHOULDPROCESS: the three gates use the source function's own $Pscmdlet, kept VERBATIM. The hop
/// scriptblock is itself [CmdletBinding(SupportsShouldProcess, ConfirmImpact = "Medium")] and the forwarded
/// -WhatIf/-Confirm bind to it, so its $Pscmdlet evaluates ConfirmImpact Medium and honours the
/// "if ($Force) { $ConfirmPreference = 'none' }" line - a single invocation, so there is no cross-record
/// "Yes to All" concern that would require routing to the outer cmdlet. The outer cmdlet's ConfirmImpact is
/// a surface facet only. -Force is passed as a bool and the inner param is UNTYPED (a [switch] inner param
/// skips positional binding - the switch-shift class). Each session's MigrationObject emits as it is
/// produced, so the hop uses InvokeScopedStreaming. Surface pinned by migration/baselines/Copy-DbaXESession.json.
/// </remarks>
[Cmdlet(VerbsCommon.Copy, "DbaXESession", DefaultParameterSetName = "Default", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Medium)]
public sealed class CopyDbaXESessionCommand : DbaBaseCmdlet
{
    /// <summary>The source SQL Server instance.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    public DbaInstanceParameter Source { get; set; } = null!;

    /// <summary>The destination SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, Position = 1)]
    [PsDbaInstanceArrayCast]
    public DbaInstanceParameter[] Destination { get; set; } = null!;

    /// <summary>Login to the source instance using alternative credentials.</summary>
    [Parameter(Position = 2)]
    public PSCredential? SourceSqlCredential { get; set; }

    /// <summary>Login to the destination instance(s) using alternative credentials.</summary>
    [Parameter(Position = 3)]
    public PSCredential? DestinationSqlCredential { get; set; }

    /// <summary>Only copy these named Extended Events sessions.</summary>
    [Parameter(Position = 4)]
    public object[]? XeSession { get; set; }

    /// <summary>Exclude these named Extended Events sessions from the copy.</summary>
    [Parameter(Position = 5)]
    public object[]? ExcludeXeSession { get; set; }

    /// <summary>Drop and recreate sessions that already exist on the destination.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - the source declares it bare, which the inherited
    // [Parameter] already matches; no override needed.

    protected override void ProcessRecord()
    {
        NestedCommand.InvokeScopedStreaming(this, item =>
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
        }, ProcessScript,
            Source, Destination, SourceSqlCredential, DestinationSqlCredential, XeSession, ExcludeXeSession,
            Force.ToBool(), EnableException.ToBool(),
            BoundCommonParameter("WhatIf"), BoundCommonParameter("Confirm"),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug"));
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
            {
                return;
            }
            if (errorList[0] is not ErrorRecord first)
            {
                return;
            }
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

    // PS: the begin body then the process body VERBATIM in ONE scope, apart from -FunctionName
    // Copy-DbaXESession on the direct Stop-Function/Write-Message sites. The three $Pscmdlet.ShouldProcess
    // gates are the hop scriptblock's own $Pscmdlet (SupportsShouldProcess, ConfirmImpact Medium);
    // -WhatIf/-Confirm are forwarded to it and the -Force $ConfirmPreference line is kept verbatim.
    // EnableException is bound so Stop-Function's scope-walking default inherits the caller's value.
    private const string ProcessScript = """
param($Source, $Destination, $SourceSqlCredential, $DestinationSqlCredential, $XeSession, $ExcludeXeSession, $Force, $EnableException, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = "Medium")]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter]$Source, [Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$Destination, $SourceSqlCredential, $DestinationSqlCredential, [object[]]$XeSession, [object[]]$ExcludeXeSession, $Force, $EnableException)
    try {
        $sourceServer = Connect-DbaInstance -SqlInstance $Source -SqlCredential $SourceSqlCredential -MinimumVersion 11

        $sourceSqlConn = $sourceServer.ConnectionContext.SqlConnectionObject
        $sourceSqlStoreConnection = New-Object Microsoft.SqlServer.Management.Sdk.Sfc.SqlStoreConnection $sourceSqlConn
        $sourceStore = New-Object  Microsoft.SqlServer.Management.XEvent.XEStore $sourceSqlStoreConnection
    } catch {
        Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $Source -FunctionName Copy-DbaXESession
        return
    }

    $storeSessions = $sourceStore.Sessions | Where-Object { $_.Name -notin 'AlwaysOn_health', 'system_health' }
    if ($XeSession) {
        $storeSessions = $storeSessions | Where-Object Name -In $XeSession
    }
    if ($ExcludeXeSession) {
        $storeSessions = $storeSessions | Where-Object Name -NotIn $ExcludeXeSession
    }

    if ($Force) { $ConfirmPreference = 'none' }

    if (Test-FunctionInterrupt) { return }
    foreach ($destinstance in $Destination) {
        try {
            $destServer = Connect-DbaInstance -SqlInstance $destinstance -SqlCredential $DestinationSqlCredential -MinimumVersion 11
            $destSqlConn = $destServer.ConnectionContext.SqlConnectionObject
            $destSqlStoreConnection = New-Object Microsoft.SqlServer.Management.Sdk.Sfc.SqlStoreConnection $destSqlConn
            $destStore = New-Object  Microsoft.SqlServer.Management.XEvent.XEStore $destSqlStoreConnection
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $destinstance -Continue -FunctionName Copy-DbaXESession
        }

        Write-Message -Level Verbose -Message "Migrating sessions." -FunctionName Copy-DbaXESession -ModuleName "dbatools"
        foreach ($session in $storeSessions) {
            $sessionName = $session.Name

            $copyXeSessionStatus = [PSCustomObject]@{
                SourceServer      = $sourceServer.Name
                DestinationServer = $destServer.Name
                Name              = $sessionName
                Type              = "Extended Event"
                Status            = $null
                Notes             = $null
                DateTime          = [DbaDateTime](Get-Date)
            }

            if ($null -ne $destStore.Sessions[$sessionName]) {
                if ($force -eq $false) {
                    if ($Pscmdlet.ShouldProcess($destinstance, "Extended Event Session '$sessionName' was skipped because it already exists on $destinstance.")) {
                        $copyXeSessionStatus.Status = "Skipped"
                        $copyXeSessionStatus.Notes = "Already exists on destination"
                        $copyXeSessionStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject

                        Write-Message -Level Verbose -Message "Extended Event Session '$sessionName' was skipped because it already exists on $destinstance." -FunctionName Copy-DbaXESession -ModuleName "dbatools"
                        Write-Message -Level Verbose -Message "Use -Force to drop and recreate." -FunctionName Copy-DbaXESession -ModuleName "dbatools"
                    }
                    continue
                } else {
                    if ($Pscmdlet.ShouldProcess($destinstance, "Attempting to drop $sessionName")) {
                        Write-Message -Level Verbose -Message "Extended Event Session '$sessionName' exists on $destinstance." -FunctionName Copy-DbaXESession -ModuleName "dbatools"
                        Write-Message -Level Verbose -Message "Force specified. Dropping $sessionName." -FunctionName Copy-DbaXESession -ModuleName "dbatools"

                        try {
                            $destStore.Sessions[$sessionName].Drop()
                        } catch {
                            $copyXeSessionStatus.Status = "Failed"
                            $copyXeSessionStatus.Notes = (Get-ErrorMessage -Record $_)
                            $copyXeSessionStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                            Write-Message -Level Verbose -Message "Issue dropping Extended Event session $sessionName on $destinstance | $PSItem" -FunctionName Copy-DbaXESession -ModuleName "dbatools"
                            continue
                        }
                    }
                }
            }

            if ($Pscmdlet.ShouldProcess($destinstance, "Migrating session $sessionName")) {
                try {
                    $sql = $session.ScriptCreate().GetScript() | Out-String

                    Write-Message -Level Debug -Message $sql -FunctionName Copy-DbaXESession -ModuleName "dbatools"
                    Write-Message -Level Verbose -Message "Migrating session $sessionName." -FunctionName Copy-DbaXESession -ModuleName "dbatools"
                    $null = $destServer.Query($sql)

                    if ($session.IsRunning -eq $true) {
                        $destStore.Sessions.Refresh()
                        $destStore.Sessions[$sessionName].Start()
                    }

                    $copyXeSessionStatus.Status = "Successful"
                    $copyXeSessionStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                } catch {
                    $copyXeSessionStatus.Status = "Failed"
                    $copyXeSessionStatus.Notes = (Get-ErrorMessage -Record $_)
                    $copyXeSessionStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                    Write-Message -Level Verbose -Message "Issue creating Extended Event session $sessionName on $destinstance | $PSItem" -FunctionName Copy-DbaXESession -ModuleName "dbatools"
                    continue
                }
            }
        }
    }
} $Source $Destination $SourceSqlCredential $DestinationSqlCredential $XeSession $ExcludeXeSession $Force $EnableException @__commonParameters 3>&1 2>&1
""";
}
