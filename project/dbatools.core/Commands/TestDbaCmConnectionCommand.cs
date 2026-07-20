#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Tests management connectivity (CIM/WMI/PSRemoting) to a computer. Port of
/// public/Test-DbaCmConnection.ps1 (W3-107). Begin + per-record hops; the source's end
/// block is EMPTY (no EndProcessing override). CLASSIFICATION TABLE (ComputerName is
/// the VFP; promoted question answered): begin state = the $disable_cache config
/// snapshot (read ONCE, rides the __w3107State sentinel - a mid-pipeline config change
/// must not affect later records, matching the source's begin snapshot) and the four
/// Test-Connection* helper functions (function-scope in the source, so RE-DECLARED
/// inside the process hop per the W3-100 relocation; they contain no
/// Stop-Function/Write-Message - no helper-frame concern); everything else is
/// per-iteration ($con mutations land on the LIVE connection object and the
/// ConnectionHost cache, which are process-global in both worlds). NO
/// ShouldProcess/gates (plain CmdletBinding - no WhatIf/Confirm plumbing); no
/// Test-FunctionInterrupt in the source and both Stop-Function sites use -Continue
/// INSIDE the per-record foreach (no latch, no relay - the W3-102/W3-103 classes do
/// not apply). The :types labeled loop and break types ride verbatim inside the hop.
/// Checklist greps done: New-DbaCimSessionOptionWithTimeout is a scope-walk-free,
/// callstack-free PS function; Get-DbatoolsConfigValue is compiled; hop-frame
/// Stop-Function/Write-Message carry -FunctionName (W1-090). No bind-time casts
/// (typed, non-mandatory, unvalidated params). NO WarningAction carrier (codex W3-005
/// r3). Surface pinned by migration/baselines/Test-DbaCmConnection.json (no sets,
/// implicit positions: ComputerName DbaCmConnectionParameter[] pos0 VFP with the
/// $env:COMPUTERNAME default applied in BeginProcessing per DEF-007, Credential pos1, Type ManagementConnectionType[] pos2
/// with the four-type default).
/// </summary>
[Cmdlet(VerbsDiagnostic.Test, "DbaCmConnection")]
public sealed partial class TestDbaCmConnectionCommand : DbaBaseCmdlet
{
    /// <summary>The computer(s) to test against; defaults to this computer.</summary>
    // DEF-007: NO property initializer here. A C# initializer runs at CONSTRUCTION, on every
    // invocation, before binding - and DbaCmConnectionParameter's ctor REGISTERS into the live
    // ConnectionHost cache (Parameter/DbaCmConnectionParameter.cs:69-70), so the default's
    // converter mutated shared state even when -ComputerName was explicitly bound, where the PS
    // bind-time default evaluates ZERO times when the parameter is EXPLICITLY bound. Applied
    // in BeginProcessing instead - see the measured case table on that gate.
    [Parameter(ValueFromPipeline = true, Position = 0)]
    public DbaCmConnectionParameter[]? ComputerName { get; set; }

    /// <summary>Credential for the remote tests.</summary>
    [Parameter(Position = 1)]
    public PSCredential? Credential { get; set; }

    /// <summary>Connection protocol(s) to test.</summary>
    [Parameter(Position = 2)]
    public ManagementConnectionType[] Type { get; set; } = new[]
    {
        ManagementConnectionType.CimRM,
        ManagementConnectionType.CimDCOM,
        ManagementConnectionType.Wmi,
        ManagementConnectionType.PowerShellRemoting
    };

    /// <summary>Overrides the known-bad-credentials skip.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // begin-snapshotted configuration value.
    private Hashtable? _state;

    // DEF-007: the invocation-scoped $env:COMPUTERNAME default (see BeginProcessing).
    private DbaCmConnectionParameter[]? _defaultComputerName;

    protected override void BeginProcessing()
    {
        // DEF-007: evaluate the $env:COMPUTERNAME default HERE, once per invocation, gated on
        // EXPLICIT boundness - this is the shape MEASURED against the source, and it is NOT the
        // ProcessRecord gate I first wrote (see the retraction in the commit message). A PS param
        // default is applied at invocation start, BEFORE begin{}, and for a ValueFromPipeline
        // parameter it is applied even when pipeline input will later overwrite it per record.
        // Measured on the source shape ([SideEffecting[]]$ComputerName = $env:COMPUTERNAME, ctor
        // logging): `-ComputerName sql01` -> ctor(sql01) only; `"sql01" | cmd` -> ctor(LOCALBOX)
        // THEN ctor(sql01); no args -> ctor(LOCALBOX); `@() | cmd` -> ctor(LOCALBOX). So the piped
        // and empty-pipeline paths DO register localhost in the source, and only the explicitly
        // bound path does not. BoundParameters at begin time contains ComputerName ONLY when it was
        // bound on the command line, which is exactly the discriminator needed. Held in a field,
        // never written back to the property, so pipeline binding stays authoritative per record.
        if (!MyInvocation.BoundParameters.ContainsKey(nameof(ComputerName)))
        {
            _defaultComputerName = (DbaCmConnectionParameter[])LanguagePrimitives.ConvertTo(
                Environment.GetEnvironmentVariable("COMPUTERNAME"),
                typeof(DbaCmConnectionParameter[]), System.Globalization.CultureInfo.InvariantCulture);
        }

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BeginScript,
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            Hashtable? sentinel = item?.BaseObject as Hashtable;
            if (sentinel is not null && sentinel.ContainsKey("__w3107State"))
            {
                _state = sentinel["__w3107State"] as Hashtable;
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

        // DEF-007: pipeline binding wins per record; otherwise the invocation default.
        DbaCmConnectionParameter[]? computers =
            MyInvocation.BoundParameters.ContainsKey(nameof(ComputerName))
                ? ComputerName
                : _defaultComputerName;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            computers, Credential, Type, Force.ToBool(), EnableException.ToBool(),
            _state,
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
                continue;
            }
            WriteObject(item);
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

}
