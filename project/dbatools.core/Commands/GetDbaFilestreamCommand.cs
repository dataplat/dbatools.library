#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Gets the FileStream configuration status for SQL Server instances. Port of
/// public/Get-DbaFilestream.ps1 (W3-033) - the read command that W3-006/W3-010
/// (Disable/Enable-DbaFilestream) call to report status. The begin block only sets the
/// $idServiceFS / $idInstanceFS lookup hashtables consumed inside the same process body, so there
/// is NO cross-record accumulator and NO end hop - the begin constants inline into the process
/// script. DEF-001 cond1+cond2: the process foreach EMITS a decorated [PSCustomObject] per instance
/// (Select-DefaultView) AND has reachable Stop-Function -Continue at the service-level WMI collection
/// / Connect-DbaInstance / the instance-level Get-DbaSpConfigure, so the hop STREAMS via
/// InvokeScopedStreaming. SqlInstance is ValueFromPipeline but NOT mandatory and has no default. No
/// ShouldProcess. Positions match the retired function (SqlInstance=0, SqlCredential=1, Credential=2;
/// EnableException=switch/null). Substitution only: explicit -FunctionName Get-DbaFilestream on
/// Stop-Function (W1-090); the body is otherwise verbatim (including its original indentation).
/// Surface pinned by migration/baselines/Get-DbaFilestream.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaFilestream")]
public sealed class GetDbaFilestreamCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Position = 0, ValueFromPipeline = true)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Alternative Windows credential for the WMI service-level query.</summary>
    [Parameter(Position = 2)]
    public PSCredential? Credential { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // Cross-record carrier for $serviceFS (W3-033 DEF-012): the source's process-block $serviceFS
    // persists across piped records AND across instances within a record, so an instance whose WMI
    // namespace is absent (the else-warn path, no -Continue, no assignment) reads the PRIOR $serviceFS.
    // Each record runs in a fresh hop scope that loses that, so the final $serviceFS is captured OUT via
    // the sentinel and seeded back INTO the next record, reproducing (not sanitizing) the legacy
    // stale-carry. PLAIN VALUE carry; no assigned-flag (per plan / W2-075).
    private object? _carriedServiceFS;

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is Hashtable sentinel && sentinel.ContainsKey("__getFilestreamCarry"))
            {
                if (sentinel["__getFilestreamCarry"] is Hashtable state)
                {
                    _carriedServiceFS = state["ServiceFS"];
                }
                return;
            }
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                NestedCommand.RemoveDuplicateError(this, nestedError);
                WriteError(nestedError);
                return;
            }
            WriteObject(item);
        }, ProcessScript,
            SqlInstance, SqlCredential, Credential, EnableException.ToBool(),
            _carriedServiceFS,
            NestedCommand.BoundCommonParameter(this, "Verbose"), NestedCommand.BoundCommonParameter(this, "Debug"));
    }

    // PS: the begin constants ($idServiceFS / $idInstanceFS lookups) inline ahead of the process
    // body, which is VERBATIM per record (original indentation preserved). Substitution only:
    // explicit -FunctionName Get-DbaFilestream on Stop-Function (W1-090).
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Credential, $EnableException, $__carriedServiceFS, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [PSCredential]$Credential, $EnableException, $__carriedServiceFS, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    $idServiceFS = [ordered]@{
        0 = 'Disabled'
        1 = 'FileStream enabled for T-Sql access'
        2 = 'FileStream enabled for T-Sql and IO streaming access'
        3 = 'FileStream enabled for T-Sql, IO streaming, and remote clients'
    }

    $idInstanceFS = [ordered]@{
        0 = 'Disabled'
        1 = 'T-SQL access enabled'
        2 = 'Full access enabled'
    }

    $serviceFS = $__carriedServiceFS

    foreach ($instance in $SqlInstance) {
        $computer = $instance.ComputerName
        $instanceName = $instance.InstanceName

        <# Get Service-Level information #>
        if ($instance.IsLocalHost) {
            $computerName = $computer
        } else {
            $computerName = (Resolve-DbaNetworkName -ComputerName $computer -Credential $Credential).FullComputerName
        }

        Write-Message -Level Verbose -Message "Attempting to connect to $computer" -FunctionName Get-DbaFilestream -ModuleName "dbatools"
        try {
            $ognamespace = Get-DbaCmObject -EnableException -ComputerName $computerName -Namespace root\Microsoft\SQLServer -Query "SELECT NAME FROM __NAMESPACE WHERE NAME LIKE 'ComputerManagement%'"
            $namespace = $ognamespace | Where-Object {
                (Get-DbaCmObject -EnableException -ComputerName $computerName -Namespace $("root\Microsoft\SQLServer\" + $_.Name) -ClassName FilestreamSettings).Count -gt 0
            } |
            Sort-Object Name -Descending | Select-Object -First 1

        if (-not $namespace) {
            $namespace = $ognamespace
        }

        if ($namespace.Name) {
            $serviceFS = Get-DbaCmObject -EnableException -ComputerName $computerName -Namespace $("root\Microsoft\SQLServer\" + $namespace.Name) -ClassName FilestreamSettings | Where-Object InstanceName -eq $instanceName | Select-Object -First 1
        } else {
            Write-Message -Level Warning -Message "No ComputerManagement was found on $computer. Service level information may not be collected." -Target $computer -FunctionName Get-DbaFilestream -ModuleName "dbatools"
        }
    } catch {
        Stop-Function -Message "Issue collecting service-level information on $computer for $instanceName" -Target $computer -ErrorRecord $_ -Continue -FunctionName Get-DbaFilestream
    }

    <# Get Instance-Level information #>
    try {
        $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential -MinimumVersion 10
    } catch {
        Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaFilestream
    }

    try {
        $instanceFS = Get-DbaSpConfigure -SqlInstance $server -Name FilestreamAccessLevel | Select-Object ConfiguredValue, RunningValue
    } catch {
        Stop-Function -Message "Issue collection instance-level configuration on $instanceName" -Target $server -ErrorRecord $_ -Exception $_.Exception -Continue -FunctionName Get-DbaFilestream
    }

    $pendingRestart = $instanceFS.ConfiguredValue -ne $instanceFS.RunningValue

    if (($serviceFS.AccessLevel -ne 0) -and ($instanceFS.RunningValue -ne 0)) {
        if (($serviceFS.AccessLevel -eq $instanceFS.RunningValue) -and $pendingRestart) {
            Write-Message -Level Verbose -Message "A restart of the instance is pending before Filestream is configured." -FunctionName Get-DbaFilestream -ModuleName "dbatools"
        }
    }

    $runvalue = (Get-DbaSpConfigure -SqlInstance $server -Name FilestreamAccessLevel | Select-Object RunningValue).RunningValue
    $servicelevel = [int]$serviceFS.AccessLevel

    [PSCustomObject]@{
        ComputerName        = $server.ComputerName
        InstanceName        = $server.ServiceName
        SqlInstance         = $server.DomainInstanceName
        InstanceAccess      = $idInstanceFS[$runvalue]
        ServiceAccess       = $idServiceFS[$servicelevel]
        ServiceShareName    = $serviceFS.ShareName
        InstanceAccessLevel = $instanceFS.RunningValue
        ServiceAccessLevel  = $serviceFS.AccessLevel
        Credential          = $Credential
        SqlCredential       = $SqlCredential
    } | Select-DefaultView -Property ComputerName, InstanceName, SqlInstance, InstanceAccess, ServiceAccess, ServiceShareName
    }

    @{ __getFilestreamCarry = @{ ServiceFS = $serviceFS } }
} $SqlInstance $SqlCredential $Credential $EnableException $__carriedServiceFS $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
