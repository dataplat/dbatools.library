#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Gets active processes/sessions on SQL Server instances. Port of public/Get-DbaProcess.ps1 (W3-046).
/// Pure per-record process command, no begin/end blocks, no ShouldProcess. DEF-001 cond1+cond2: the
/// process foreach EMITS a decorated session per match (Select-DefaultView) AND has a reachable
/// Stop-Function -Continue at Connect-DbaInstance, so the hop STREAMS via InvokeScopedStreaming. No
/// cross-record state, no carriers beyond the parameters. Positions match the retired function
/// (SqlInstance=0..Program=7; switches unpositioned). Substitutions in the body: explicit
/// -FunctionName Get-DbaProcess on Stop-Function, and the multi-name Test-Bound -not rewritten to the
/// six carried bound flags (Login/Spid/ExcludeSpid/Hostname/Program/Database) - both reversed for the
/// byte-exact fidelity check. The source references an undefined $Exclude (the parameter is
/// $ExcludeSpid), which resolves to $null in both worlds and is preserved bug-for-bug. Surface pinned
/// by migration/baselines/Get-DbaProcess.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaProcess")]
public sealed class GetDbaProcessCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Include only these session process IDs (SPIDs).</summary>
    [Parameter(Position = 2)]
    public int[]? Spid { get; set; }

    /// <summary>Exclude these session process IDs (SPIDs).</summary>
    [Parameter(Position = 3)]
    public int[]? ExcludeSpid { get; set; }

    /// <summary>Include only sessions connected to these databases.</summary>
    [Parameter(Position = 4)]
    public string[]? Database { get; set; }

    /// <summary>Include only sessions for these logins.</summary>
    [Parameter(Position = 5)]
    public string[]? Login { get; set; }

    /// <summary>Include only sessions from these client hosts.</summary>
    [Parameter(Position = 6)]
    public string[]? Hostname { get; set; }

    /// <summary>Include only sessions from these client programs.</summary>
    [Parameter(Position = 7)]
    public string[]? Program { get; set; }

    /// <summary>Exclude system SPIDs (Spid greater than 50 only).</summary>
    [Parameter]
    public SwitchParameter ExcludeSystemSpids { get; set; }

    /// <summary>Return only sessions matching ALL supplied filters rather than the union.</summary>
    [Parameter]
    public SwitchParameter Intersect { get; set; }

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
            SqlInstance, SqlCredential, Spid, ExcludeSpid, Database, Login, Hostname, Program,
            ExcludeSystemSpids.ToBool(), Intersect.ToBool(), EnableException.ToBool(),
            TestBound(nameof(Login)), TestBound(nameof(Spid)), TestBound(nameof(ExcludeSpid)),
            TestBound(nameof(Hostname)), TestBound(nameof(Program)), TestBound(nameof(Database)),
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

    // PS: the process body VERBATIM per record (no begin/end blocks). Substitutions: explicit
    // -FunctionName Get-DbaProcess on Stop-Function, and the multi-name Test-Bound -not rewritten to
    // the six carried bound flags. The undefined $Exclude reference rides untouched (bug-for-bug).
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Spid, $ExcludeSpid, $Database, $Login, $Hostname, $Program, $ExcludeSystemSpids, $Intersect, $EnableException, $__boundLogin, $__boundSpid, $__boundExcludeSpid, $__boundHostname, $__boundProgram, $__boundDatabase, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [int[]]$Spid, [int[]]$ExcludeSpid, [string[]]$Database, [string[]]$Login, [string[]]$Hostname, [string[]]$Program, [switch]$ExcludeSystemSpids, [switch]$Intersect, $EnableException, $__boundLogin, $__boundSpid, $__boundExcludeSpid, $__boundHostname, $__boundProgram, $__boundDatabase, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }
        foreach ($instance in $SqlInstance) {
            try {
                $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
            } catch {
                Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaProcess
            }

            $sql = "SELECT DATEDIFF(MINUTE, s.last_request_end_time, GETDATE()) AS MinutesAsleep,
                s.session_id AS spid,
                s.host_process_id AS HostProcessId,
                t.text AS Query,
                s.login_time AS LoginTime,
                s.client_version AS ClientVersion,
                s.last_request_start_time AS LastRequestStartTime,
                s.last_request_end_time AS LastRequestEndTime,
                c.net_transport AS NetTransport,
                c.encrypt_option AS EncryptOption,
                c.auth_scheme AS AuthScheme,
                c.net_packet_size AS NetPacketSize,
                c.client_net_address AS ClientNetAddress,
                e.name AS EndpointName,
                e.is_admin_endpoint AS IsDac
            FROM sys.dm_exec_connections c
            JOIN sys.dm_exec_sessions s
                ON c.session_id = s.session_id
            JOIN sys.endpoints e
                ON c.endpoint_id = e.endpoint_id
            OUTER APPLY sys.dm_exec_sql_text(c.most_recent_sql_handle) t"

            if ($server.VersionMajor -gt 8) {
                $results = $server.Query($sql)
            } else {
                $results = $null
            }

            $allSessions = @()

            $processes = $server.EnumProcesses()

            if ($Intersect -eq $true) {
                $allSessions = $processes

                if ($Login) {
                    $allSessions = $allSessions | Where-Object { $_.Login -in $Login }
                }

                if ($Spid) {
                    $allSessions = $allSessions | Where-Object { $_.Spid -in $Spid -or $_.BlockingSpid -in $Spid }
                }

                if ($Hostname) {
                    $allSessions = $allSessions | Where-Object { $_.Host -in $Hostname }
                }

                if ($Program) {
                    $allSessions = $allSessions | Where-Object { $_.Program -in $Program }
                }

                if ($Database) {
                    $allSessions = $allSessions | Where-Object { $Database -contains $_.Database }
                }
            } else {
                if ($Login) {
                    $allSessions += $processes | Where-Object { $_.Login -in $Login -and $_.Spid -notin $allSessions.Spid }
                }

                if ($Spid) {
                    $allSessions += $processes | Where-Object { ($_.Spid -in $Spid -or $_.BlockingSpid -in $Spid) -and $_.Spid -notin $allSessions.Spid }
                }

                if ($Hostname) {
                    $allSessions += $processes | Where-Object { $_.Host -in $Hostname -and $_.Spid -notin $allSessions.Spid }
                }

                if ($Program) {
                    $allSessions += $processes | Where-Object { $_.Program -in $Program -and $_.Spid -notin $allSessions.Spid }
                }

                if ($Database) {
                    $allSessions += $processes | Where-Object { $Database -contains $_.Database -and $_.Spid -notin $allSessions.Spid }
                }
            }

            if (-not ($__boundLogin -or $__boundSpid -or $__boundExcludeSpid -or $__boundHostname -or $__boundProgram -or $__boundDatabase)) { # SOURCE: if (Test-Bound -not 'Login', 'Spid', 'ExcludeSpid', 'Hostname', 'Program', 'Database') {
                $allSessions = $processes
            }

            if ($ExcludeSystemSpids -eq $true) {
                $allSessions = $allSessions | Where-Object { $_.Spid -gt 50 }
            }

            if ($Exclude) {
                $allSessions = $allSessions | Where-Object { $Exclude -notcontains $_.SPID -and $_.Spid -notin $allSessions.Spid }
            }

            foreach ($session in $allSessions) {

                if ($session.Status -eq "") {
                    $status = "sleeping"
                } else {
                    $status = $session.Status
                }

                if ($session.Command -eq "") {
                    $command = "AWAITING COMMAND"
                } else {
                    $command = $session.Command
                }

                $row = $results | Where-Object { $_.Spid -eq $session.Spid }

                Add-Member -Force -InputObject $session -MemberType NoteProperty -Name Parent -value $server
                Add-Member -Force -InputObject $session -MemberType NoteProperty -Name ComputerName -value $server.ComputerName
                Add-Member -Force -InputObject $session -MemberType NoteProperty -Name InstanceName -value $server.ServiceName
                Add-Member -Force -InputObject $session -MemberType NoteProperty -Name SqlInstance -value $server.DomainInstanceName
                Add-Member -Force -InputObject $session -MemberType NoteProperty -Name Status -value $status
                Add-Member -Force -InputObject $session -MemberType NoteProperty -Name Command -value $command
                Add-Member -Force -InputObject $session -MemberType NoteProperty -Name HostProcessId -value $row.HostProcessId
                Add-Member -Force -InputObject $session -MemberType NoteProperty -Name MinutesAsleep -value $row.MinutesAsleep
                Add-Member -Force -InputObject $session -MemberType NoteProperty -Name LoginTime -value $row.LoginTime
                Add-Member -Force -InputObject $session -MemberType NoteProperty -Name ClientVersion -value $row.ClientVersion
                Add-Member -Force -InputObject $session -MemberType NoteProperty -Name LastRequestStartTime -value $row.LastRequestStartTime
                Add-Member -Force -InputObject $session -MemberType NoteProperty -Name LastRequestEndTime -value $row.LastRequestEndTime
                Add-Member -Force -InputObject $session -MemberType NoteProperty -Name NetTransport -value $row.NetTransport
                Add-Member -Force -InputObject $session -MemberType NoteProperty -Name EncryptOption -value $row.EncryptOption
                Add-Member -Force -InputObject $session -MemberType NoteProperty -Name AuthScheme -value $row.AuthScheme
                Add-Member -Force -InputObject $session -MemberType NoteProperty -Name NetPacketSize -value $row.NetPacketSize
                Add-Member -Force -InputObject $session -MemberType NoteProperty -Name ClientNetAddress -value $row.ClientNetAddress
                Add-Member -Force -InputObject $session -MemberType NoteProperty -Name LastQuery -value $row.Query
                Add-Member -Force -InputObject $session -MemberType NoteProperty -Name EndpointName -value $row.EndpointName
                Add-Member -Force -InputObject $session -MemberType NoteProperty -Name IsDac -value $row.IsDac

                Select-DefaultView -InputObject $session -Property ComputerName, InstanceName, SqlInstance, Spid, Login, LoginTime, Host, Database, BlockingSpid, Program, Status, Command, Cpu, MemUsage, LastRequestStartTime, LastRequestEndTime, MinutesAsleep, ClientNetAddress, NetTransport, EncryptOption, AuthScheme, NetPacketSize, ClientVersion, HostProcessId, IsSystem, EndpointName, IsDac, LastQuery
            }
        }
} $SqlInstance $SqlCredential $Spid $ExcludeSpid $Database $Login $Hostname $Program $ExcludeSystemSpids $Intersect $EnableException $__boundLogin $__boundSpid $__boundExcludeSpid $__boundHostname $__boundProgram $__boundDatabase $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}