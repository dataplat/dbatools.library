#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Gets the SQL Server (and optionally Windows) install date for instances. Port of
/// public/Get-DbaInstanceInstallDate.ps1 (W3-035). Pure per-record process command with no begin/end
/// blocks. DEF-001 cond1+cond2: the process foreach EMITS a [PSCustomObject] per instance
/// (Select-DefaultView) AND has reachable Stop-Function -Continue at Connect-DbaInstance and the
/// -IncludeWindows Get-DbaCmObject, so the hop STREAMS via InvokeScopedStreaming. Cross-record-state
/// check: $windowsInstallDate is conditionally set (only under -IncludeWindows), but -IncludeWindows is
/// call-constant and on its failure path Stop-Function -Continues before the emit, so it is never
/// stale-read; $sqlInstallDate is always set (both version branches + the retry) before the emit - so
/// no carrier is needed. Positions match the retired function (SqlInstance=0, SqlCredential=1,
/// Credential=2; IncludeWindows/EnableException=switch/null). Substitution only: explicit -FunctionName
/// Get-DbaInstanceInstallDate on Stop-Function (W1-090); the body is otherwise verbatim. Surface pinned
/// by migration/baselines/Get-DbaInstanceInstallDate.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaInstanceInstallDate")]
public sealed class GetDbaInstanceInstallDateCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Alternative Windows credential for the -IncludeWindows WMI query.</summary>
    [Parameter(Position = 2)]
    public PSCredential? Credential { get; set; }

    /// <summary>Also returns the Windows OS install date.</summary>
    [Parameter]
    public SwitchParameter IncludeWindows { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
                return;
            }
            WriteObject(item);
        }, ProcessScript,
            SqlInstance, SqlCredential, Credential, IncludeWindows.ToBool(), EnableException.ToBool(),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug"));
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

    // PS: the process body VERBATIM per record (no begin/end blocks). Substitution only: explicit
    // -FunctionName Get-DbaInstanceInstallDate on Stop-Function (W1-090).
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Credential, $IncludeWindows, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [PSCredential]$Credential, $IncludeWindows, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    foreach ($instance in $SqlInstance) {
        try {
            $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaInstanceInstallDate
        }

        if ($server.VersionMajor -ge 9) {
            Write-Message -Level Verbose -Message "Getting Install Date for: $instance"
            $sql = "SELECT create_date FROM sys.server_principals WHERE sid = 0x010100000000000512000000"
            [DbaDateTime]$sqlInstallDate = $server.Query($sql, 'master', $true).create_date
        } else {
            Write-Message -Level Verbose -Message "Getting Install Date for: $instance"
            $sql = "SELECT schemadate FROM dbo.sysservers"
            [DbaDateTime]$sqlInstallDate = $server.Query($sql, 'master', $true).schemadate
        }

        if (-not $sqlInstallDate -or $sqlInstallDate -is [System.DBNull]) {
            Write-Message -Level Verbose -Message "Trying again to get Install Date for: $instance"
            $sql = "SELECT schemadate FROM dbo.sysservers"
            [DbaDateTime]$sqlInstallDate = $server.Query($sql, 'master', $true).schemadate
        }

        $WindowsServerName = $server.ComputerNamePhysicalNetBIOS

        if ($IncludeWindows) {
            try {
                [DbaDateTime]$windowsInstallDate = (Get-DbaCmObject -ClassName win32_OperatingSystem -ComputerName $WindowsServerName -Credential $Credential -EnableException).InstallDate
            } catch {
                Stop-Function -Message "Failed to connect to: $WindowsServerName" -Continue -Target $instance -ErrorRecord $_ -FunctionName Get-DbaInstanceInstallDate
            }
        }

        $object = [PSCustomObject]@{
            ComputerName       = $server.ComputerName
            InstanceName       = $server.ServiceName
            SqlInstance        = $server.DomainInstanceName
            SqlInstallDate     = $sqlInstallDate
            WindowsInstallDate = $windowsInstallDate
        }

        if ($IncludeWindows) {
            Select-DefaultView -InputObject $object -Property ComputerName, InstanceName, SqlInstance, SqlInstallDate, WindowsInstallDate
        } else {
            Select-DefaultView -InputObject $object -Property ComputerName, InstanceName, SqlInstance, SqlInstallDate
        }

    }
} $SqlInstance $SqlCredential $Credential $IncludeWindows $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
