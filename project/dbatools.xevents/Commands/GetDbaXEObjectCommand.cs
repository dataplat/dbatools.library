#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves the Extended Events objects (events, actions, targets, etc.) available on one or more
/// SQL Server instances.
/// </summary>
/// <remarks>
/// The query construction, the connection, the DMV query, and the Select-DefaultView projection all
/// run the original dbatools PowerShell body VERBATIM inside the dbatools module scope rather than
/// being reimplemented in C#, so the engine decides the observable details.
///
/// The function has a begin block and a process block. The begin block is a PURE string-building
/// computation (it has no side effects - no Stop-Function, Write-Message, connection, or output - and
/// is a function only of the non-pipeline $Type parameter), so it is FOLDED into the top of the
/// process hop: recomputing $sql per record is identical to computing it once in begin. (The source's
/// $where.Replace(...) calls do not assign back, so they are no-ops; preserved verbatim.)
///
/// The process block's second Stop-Function (query failure) has NO -Continue, so it SETS the
/// function-scope interrupt, but nothing in the function reads Test-FunctionInterrupt, so the interrupt
/// is inert and needs no carry (under -EnableException that Stop-Function throws terminating instead,
/// which the framework re-throws). The connect Stop-Function is -Continue. There is no ShouldProcess
/// and no Test-Bound.
///
/// Each instance's rows are emitted before a later instance may fail under -EnableException (DEF-001),
/// so the process hop uses InvokeScopedStreaming. Surface pinned by
/// migration/baselines/Get-DbaXEObject.json.
/// </remarks>
[Cmdlet(VerbsCommon.Get, "DbaXEObject")]
public sealed class GetDbaXEObjectCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    [PsDbaInstanceArrayCast]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Filters the Extended Events objects by component type.</summary>
    [Parameter(Position = 2)]
    [ValidateSet("Type", "Event", "Target", "Action", "Map", "Message", "PredicateComparator", "PredicateSource")]
    public string[]? Type { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - the source declares it bare (every set), which
    // the inherited [Parameter] (no ParameterSetName) already matches; no override needed.

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
            SqlInstance, SqlCredential, Type, EnableException.ToBool(),
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

    // PS: the begin block (pure $sql construction) folded to the top, then the process block VERBATIM
    // apart from -FunctionName Get-DbaXEObject on the two direct Stop-Function sites.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Type, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, $SqlCredential, [string[]]$Type, $EnableException)
    if ($Type) {
        $join = $Type -join "','"
        $where = "AND o.object_type in ('$join')"
        $where.Replace("PredicateComparator", "pred_compare")
        $where.Replace("PredicateSource", "pred_source")
    }
    $sql = "SELECT  SERVERPROPERTY('MachineName') AS ComputerName,
            ISNULL(SERVERPROPERTY('InstanceName'), 'MSSQLSERVER') AS InstanceName,
            SERVERPROPERTY('ServerName') AS SqlInstance,
            p.name AS PackageName,
            ObjectType =
                  CASE o.object_type
                     WHEN 'type' THEN 'Type'
                     WHEN 'event' THEN 'Event'
                     WHEN 'target' THEN 'Target'
                     WHEN 'pred_compare' THEN 'PredicateComparator'
                     WHEN 'pred_source' THEN 'PredicateSource'
                     WHEN 'action' THEN 'Action'
                     WHEN 'map' THEN 'Map'
                     WHEN 'message' THEN 'Message'
                     ELSE o.object_type
                  END,
            o.object_type AS ObjectTypeRaw,
            o.name AS TargetName,
            o.description AS Description
            FROM sys.dm_xe_packages AS p
            JOIN sys.dm_xe_objects AS o ON p.guid = o.package_guid
            WHERE (p.capabilities IS NULL OR p.capabilities & 1 = 0)
            $where
            AND (o.capabilities IS NULL OR o.capabilities & 1 = 0)
            ORDER BY o.object_type
            "
    foreach ($instance in $SqlInstance) {
        try {
            $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential -MinimumVersion 9
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaXEObject
        }

        try {
            $server.Query($sql) | Select-DefaultView -ExcludeProperty ComputerName, InstanceName, ObjectTypeRaw
        } catch {
            Stop-Function -Message "Issue collecting trace data on $server." -Target $server -ErrorRecord $_ -FunctionName Get-DbaXEObject
        }
    }
} $SqlInstance $SqlCredential $Type $EnableException @__commonParameters 3>&1 2>&1
""";
}
