#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;
using Dataplat.Dbatools.Connection;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves WMI/CIM management information from a computer, with protocol fallback. Port of
/// public/Get-DbaCmObject.ps1 (W3-024, WAVE-3 hard remnant); the workflow remains a module-scoped
/// PowerShell compatibility hop. READ-ONLY (no SupportsShouldProcess).
///
/// BEGIN+PROCESS. -ComputerName is ValueFromPipeline, so process fires per piped target. TWO
/// parameter sets: Class (default) and Query.
///
/// THREE begin -> process CARRIES, and NO cross-record carry (confirmed by the two-step triage, not
/// assumed):
///   $disable_cache (:108, ConnectionHost::DisableCache) - read throughout process for the
///     connection-cache writes; carried from begin.
///   $ParSet (:244, "$PSCmdlet.ParameterSetName") - CANNOT ride a hop: inside a hop $PSCmdlet is the
///     HOP scriptblock's own cmdlet, so it would report the hop's set, not the caller's. The real
///     cmdlet's ParameterSetName is passed in and the body uses the carried value (read at :304/:335/
///     :366 to branch Class vs Query). Same class handled on W2-153.
///   Resolve-CimError - a ~128-line utility FUNCTION defined in begin (:114-241) and CALLED in
///     process (:314/:345/...). Begin's scope dies before process, so it is recreated verbatim at
///     the top of the process hop (the New-DbaDacProfile / W3-016 / W3-047 helper-recreation pattern).
///   $excluded (accumulator detector hit) does NOT carry: it is reset to @() at :289 INSIDE the
///     ":main foreach ($connectionObject in $ComputerName)" loop, so it is per-connectionObject, not
///     cross-record. Recorded so a reviewer does not re-add it.
///
/// NESTED LABELED LOOPS RIDE VERBATIM. process is ":main foreach ($connectionObject ...)" containing
/// ":sub while ($true)" (the protocol fallback CimRM -> CimDCOM -> Wmi -> PowerShellRemoting). The
/// body uses "continue main", "continue sub" and "Stop-Function -Continue -ContinueLabel 'main'"
/// (whose effect is "continue main"). PowerShell's labeled continue is DYNAMICALLY scoped, and the
/// loops live in the dot-sourced body, so the labels resolve correctly inside the hop.
///
/// ONE Test-Bound ("Namespace" at :374/:384) becomes a carried boundness flag. ComputerName's source
/// default is $env:COMPUTERNAME (a DEF-007 runtime default); because it is ValueFromPipeline the
/// piped/passed value wins and the env default applies only when nothing was supplied, resolved in
/// the hop by "if (-not $ComputerName) { $ComputerName = $env:COMPUTERNAME }".
///
/// -ClassName carries Alias("Class"). The three switches (Force, SilentlyContinue) and inherited
/// EnableException cross as SwitchParameter OBJECTS received untyped. In-hop Stop-Function/
/// Write-Message calls carry -FunctionName. NO interrupt (no Test-FunctionInterrupt), NO
/// $PSBoundParameters iteration, NO .IsPresent. Streaming (DEF-001): emits per target as each
/// connects. Only -ClassName is positional (0), confirmed against the exported baseline. Surface
/// pinned by migration/baselines/Get-DbaCmObject.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaCmObject", DefaultParameterSetName = "Class")]
public sealed partial class GetDbaCmObjectCommand : DbaBaseCmdlet
{
    /// <summary>The WMI/CIM class to query (Class parameter set).</summary>
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = "Class")]
    [Alias("Class")]
    [PsStringCast]
    public string ClassName { get; set; } = null!;

    /// <summary>A WQL query to run (Query parameter set).</summary>
    [Parameter(Mandatory = true, ParameterSetName = "Query")]
    [PsStringCast]
    public string Query { get; set; } = null!;

    /// <summary>The target computer(s); defaults to the local machine.</summary>
    [Parameter(ValueFromPipeline = true, ParameterSetName = "Class")]
    [Parameter(ValueFromPipeline = true, ParameterSetName = "Query")]
    public DbaCmConnectionParameter[]? ComputerName { get; set; }

    /// <summary>Alternative credential for the target computer(s).</summary>
    [Parameter(ParameterSetName = "Class")]
    [Parameter(ParameterSetName = "Query")]
    public PSCredential? Credential { get; set; }

    /// <summary>The WMI namespace to query.</summary>
    [Parameter(ParameterSetName = "Class")]
    [Parameter(ParameterSetName = "Query")]
    [PsStringCast]
    public string Namespace { get; set; } = "root\\cimv2";

    /// <summary>Connection protocols to skip.</summary>
    [Parameter(ParameterSetName = "Class")]
    [Parameter(ParameterSetName = "Query")]
    public ManagementConnectionType[] DoNotUse { get; set; } = new[] { ManagementConnectionType.None };

    /// <summary>Force a fresh connection, bypassing the cache.</summary>
    [Parameter(ParameterSetName = "Class")]
    [Parameter(ParameterSetName = "Query")]
    public SwitchParameter Force { get; set; }

    /// <summary>Suppress non-terminating connection errors.</summary>
    [Parameter(ParameterSetName = "Class")]
    [Parameter(ParameterSetName = "Query")]
    public SwitchParameter SilentlyContinue { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // begin's $disable_cache and $ParSet; opaque to C#.
    private Hashtable? _beginState;

    protected override void BeginProcessing()
    {
        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BeginScript,
            EnableException, ParameterSetName,
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (item?.BaseObject is Hashtable sentinel && sentinel.ContainsKey("__getDbaCmObjectBegin"))
            {
                _beginState = sentinel["__getDbaCmObjectBegin"] as Hashtable;
                continue;
            }
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
                continue;
            }
            WriteObject(item);
        }
    }

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        // Streaming, not buffered (DEF-001): each target's objects are emitted as it connects, so a
        // buffered hop would discard results already produced when a later target failed.
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
            ClassName, Query, ComputerName, Credential, Namespace, DoNotUse, Force, SilentlyContinue,
            EnableException, _beginState,
            MyInvocation.BoundParameters.ContainsKey("Namespace"),
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
}