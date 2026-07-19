#nullable enable

using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Returns recent HADR ring-buffer diagnostic records from sys.dm_os_ring_buffers.
/// Port of public/Get-DbaAgRingBuffer.ps1; surface pinned by
/// migration/baselines/Get-DbaAgRingBuffer.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaAgRingBuffer")]
public sealed class GetDbaAgRingBufferCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instances using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Restricts results to these HADR ring-buffer types.</summary>
    [Parameter(Position = 2)]
    [ValidateSet("RING_BUFFER_HADRDBMGR_API", "RING_BUFFER_HADRDBMGR_STATE", "RING_BUFFER_HADRDBMGR_COMMIT", "RING_BUFFER_HADR_TRANSPORT_STATE")]
    public string[]? RingBufferType { get; set; }

    /// <summary>How many minutes of history to return.</summary>
    [Parameter(Position = 3)]
    public int CollectionMinutes { get; set; } = 60;

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        // Whole-record hop: the source's process-top Test-FunctionInterrupt rides
        // verbatim and is provably inert in this source - both Stop-Function sites use
        // -Continue, whose flow path in Stop-Function fires `continue` BEFORE the
        // interrupt variable is ever set, so the flag can never latch in either world.
        // [DEF-001] closed via InvokeScopedStreaming (ab7492c). Streaming changes -WhatIf transcript
        // capture (documented observability change, not behaviour); the parity runner strips the
        // transcript gate-message. Fleet-confirmed non-blocker (C's streamed ShouldProcess wave, MSTest 487/487).
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
            SqlInstance, SqlCredential, RingBufferType, CollectionMinutes,
            EnableException.ToBool(),
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

    // PS: the source process block VERBATIM (EOL-normalized like every sliced body),
    // including the inert Test-FunctionInterrupt guard whose `return` the dot-block
    // frame preserves. Substitutions: four -FunctionName appends (two Stop-Function
    // -Continue sites, two Write-Message sites) - no gates, no Test-Bound. The
    // interpolated T-SQL ($typeList from the ValidateSet-constrained RingBufferType,
    // [int] CollectionMinutes, server-derived timestamp) is the SOURCE's own
    // construction, verbatim.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $RingBufferType, $CollectionMinutes, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [string[]]$RingBufferType, [int]$CollectionMinutes, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    . {
        if (Test-FunctionInterrupt) { return }
        foreach ($instance in $SqlInstance) {
            try {
                $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential -MinimumVersion 11
            } catch {
                Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaAgRingBuffer
            }

            try {
                [long]$currentTimestamp = ($server.Query("SELECT cpu_ticks / CONVERT(FLOAT, (cpu_ticks / ms_ticks)) AS TimeStamp FROM sys.dm_os_sys_info")).TimeStamp
                Write-Message -Level Verbose -Message "Using current timestamp of $currentTimestamp" -FunctionName Get-DbaAgRingBuffer

                if ($RingBufferType) {
                    $typeList = ($RingBufferType | ForEach-Object { "N'$_'" }) -join ", "
                } else {
                    $typeList = "N'RING_BUFFER_HADRDBMGR_API', N'RING_BUFFER_HADRDBMGR_STATE', N'RING_BUFFER_HADRDBMGR_COMMIT', N'RING_BUFFER_HADR_TRANSPORT_STATE'"
                }

                $sql = "WITH HadrRingBuffer AS
                    (
                        SELECT
                            ring_buffer_type,
                            timestamp,
                            CONVERT(XML, record) AS record
                        FROM sys.dm_os_ring_buffers
                        WHERE ring_buffer_type IN ($typeList)
                    )
                    SELECT
                        SERVERPROPERTY('ServerName') AS ServerName,
                        ring_buffer_type,
                        record.value('(./Record/@id)[1]', 'int') AS record_id,
                        DATEADD(ms, -1 * ($currentTimestamp - [timestamp]), GETDATE()) AS EventTime,
                        record
                    FROM HadrRingBuffer
                    WHERE DATEADD(ms, -1 * ($currentTimestamp - [timestamp]), GETDATE()) > DATEADD(MINUTE, -$CollectionMinutes, GETDATE())
                    ORDER BY EventTime DESC;"

                Write-Message -Level Verbose -Message "Executing SQL Statement: $sql" -FunctionName Get-DbaAgRingBuffer
                foreach ($row in $server.Query($sql)) {
                    [PSCustomObject]@{
                        ComputerName   = $server.ComputerName
                        InstanceName   = $server.ServiceName
                        SqlInstance    = $server.DomainInstanceName
                        RingBufferType = $row.ring_buffer_type
                        RecordId       = $row.record_id
                        EventTime      = $row.EventTime
                        Record         = $row.record
                    }
                }
            } catch {
                Stop-Function -Message "Failed to query HADR ring buffer data." -Category InvalidOperation -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaAgRingBuffer
            }
        }
    }
} $SqlInstance $SqlCredential $RingBufferType $CollectionMinutes $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
