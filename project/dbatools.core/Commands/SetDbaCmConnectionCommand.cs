#nullable enable

using System;
using System.Collections;
using System.Globalization;
using System.Management.Automation;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Configures cached CIM/WMI management connections. Port of
/// public/Set-DbaCmConnection.ps1 (W3-087), third CmConnection sibling (W3-063/W3-071
/// shapes): begin hop emits the InternalComment/keys lines and computes $disable_cache
/// (sentinel); PER-ELEMENT process hops (25a09f3 - the source loop over $ComputerName
/// has no cross-element state); end hop emits the closing InternalComment. The
/// per-computer body rides VERBATIM inside the $__realCmdlet.ShouldProcess gate
/// (W1-085 - no ConfirmPreference override; default ConfirmImpact Medium), including
/// the Success check's Stop-Function -Continue INSIDE the gate (source ordering,
/// unlike the Remove sibling), the Reset*/Clear* branches, the bad-credential loops,
/// ELEVEN Test-Bound property gates carried as flags - including the source's
/// EnableCredentialFailover -> DisableCredentialAutoRegister assignment bug preserved
/// verbatim (the W3-063 line-199 sibling bug) - and the CimOptions ELSEIF-null defaults
/// (unlike the New sibling's plain else). $env:COMPUTERNAME default applied in BeginProcessing,
/// NOT at construction (DEF-007). NO WarningAction carrier (codex W3-005 r3). Surface pinned by
/// migration/baselines/Set-DbaCmConnection.json (sets Credential {Credential,
/// WindowsCredentialsAreBad} + Windows {UseWindowsCredentials}, default Credential,
/// NO positions, ComputerName VFP all-sets).
/// </summary>
[Cmdlet(VerbsCommon.Set, "DbaCmConnection", SupportsShouldProcess = true, DefaultParameterSetName = "Credential")]
public sealed partial class SetDbaCmConnectionCommand : DbaBaseCmdlet
{
    /// <summary>The computer(s) whose cached connections should be configured; defaults to this computer.</summary>
    // DEF-007: NO property initializer here. A C# initializer runs at CONSTRUCTION, on every
    // invocation, before binding - and DbaCmConnectionParameter's ctor REGISTERS into the live
    // ConnectionHost cache (Parameter/DbaCmConnectionParameter.cs:69-70), so the default's
    // converter mutated shared state even when -ComputerName was explicitly bound, where the PS
    // bind-time default evaluates ZERO times. This command is DEF-007's headline repro
    // (`Set-DbaCmConnection -ComputerName sql01 -ResetConfiguration` on a cold cache registered
    // the LOCAL box). Applied in BeginProcessing instead - see the measured case table there.
    [Parameter(ValueFromPipeline = true)]
    public DbaCmConnectionParameter[]? ComputerName { get; set; }

    /// <summary>Credential to register for the connection.</summary>
    [Parameter(ParameterSetName = "Credential")]
    public PSCredential? Credential { get; set; }

    /// <summary>Marks the current Windows credentials as valid for the connection.</summary>
    [Parameter(ParameterSetName = "Windows")]
    public SwitchParameter UseWindowsCredentials { get; set; }

    /// <summary>Forces use of cached credentials over explicit ones.</summary>
    [Parameter]
    public SwitchParameter OverrideExplicitCredential { get; set; }

    /// <summary>Allows the connection to override the global connection policy.</summary>
    [Parameter]
    public SwitchParameter OverrideConnectionPolicy { get; set; }

    /// <summary>Connection protocols to disable for this connection.</summary>
    [Parameter]
    public ManagementConnectionType DisabledConnectionTypes { get; set; } = ManagementConnectionType.None;

    /// <summary>Prevents storing credentials known to fail.</summary>
    [Parameter]
    public SwitchParameter DisableBadCredentialCache { get; set; }

    /// <summary>Forces new CIM sessions instead of reusing existing ones.</summary>
    [Parameter]
    public SwitchParameter DisableCimPersistence { get; set; }

    /// <summary>Prevents auto-storing successful credentials.</summary>
    [Parameter]
    public SwitchParameter DisableCredentialAutoRegister { get; set; }

    /// <summary>Enables credential failover (source assigns DisableCredentialAutoRegister - preserved bug).</summary>
    [Parameter]
    public SwitchParameter EnableCredentialFailover { get; set; }

    /// <summary>Marks the current Windows credentials as invalid for the connection.</summary>
    [Parameter(ParameterSetName = "Credential")]
    public SwitchParameter WindowsCredentialsAreBad { get; set; }

    /// <summary>WSMan session options for CIM over WinRM.</summary>
    [Parameter]
    public Microsoft.Management.Infrastructure.Options.WSManSessionOptions? CimWinRMOptions { get; set; }

    /// <summary>DCOM session options for CIM over DCOM.</summary>
    [Parameter]
    public Microsoft.Management.Infrastructure.Options.DComSessionOptions? CimDCOMOptions { get; set; }

    /// <summary>Credentials to add to the known-bad list.</summary>
    [Parameter]
    public PSCredential[]? AddBadCredential { get; set; }

    /// <summary>Credentials to remove from the known-bad list.</summary>
    [Parameter]
    public PSCredential[]? RemoveBadCredential { get; set; }

    /// <summary>Clears the known-bad credential list.</summary>
    [Parameter]
    public SwitchParameter ClearBadCredential { get; set; }

    /// <summary>Clears the stored credential.</summary>
    [Parameter]
    public SwitchParameter ClearCredential { get; set; }

    /// <summary>Resets all credential state.</summary>
    [Parameter]
    public SwitchParameter ResetCredential { get; set; }

    /// <summary>Resets the cached connection-protocol status.</summary>
    [Parameter]
    public SwitchParameter ResetConnectionStatus { get; set; }

    /// <summary>Restores the default configuration.</summary>
    [Parameter]
    public SwitchParameter ResetConfiguration { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // begin-scope $disable_cache, computed once and read by every process record.
    private object? _disableCache;

    // DEF-007: the $env:COMPUTERNAME default. MEASURED source semantics (ctor-logging probe on
    // the source shape [SideEffecting[]]$ComputerName = $env:COMPUTERNAME):
    //   -ComputerName sql01     -> sql01                                   (default NOT evaluated)
    //   "sql01" | cmd           -> LOCALBOX, sql01
    //   "sql01","sql02" | cmd   -> LOCALBOX, sql01, LOCALBOX, sql02
    //   <no args>               -> LOCALBOX
    //   @() | cmd               -> LOCALBOX                                (process never runs)
    // i.e. PS applies the typed default at invocation start AND RE-APPLIES it before every
    // subsequent pipeline record, so the number of default conversions is max(1, recordCount) -
    // and it is skipped entirely only when the parameter was bound on the COMMAND LINE.
    // That is why _boundOnCommandLine is snapshotted in BeginProcessing: by ProcessRecord,
    // BoundParameters also carries pipeline-bound values and can no longer tell the two apart.
    // RESIDUAL, deliberate and NOT fully benign: for records after the first, PS converts the
    // default BEFORE binding the record's own value; a compiled cmdlet binds before
    // ProcessRecord runs, so the port converts in the opposite order within such a record.
    // Scope of the difference, stated precisely rather than waved off (codex r3 corrected an
    // earlier "unobservable" claim here): the resulting KEY SET and the object each key maps to
    // are identical either way, because the ctor's effect is
    // ConnectionHost.Connections[name] = connection keyed BY NAME - for two different names
    // order cannot change the mapping, and for the same name one call registers while the other
    // finds the existing entry. What CAN differ is Dictionary INSERTION ORDER, which leaks
    // through ConnectionHost.Connections.Keys/.Values and hence through Get-DbaCmConnection's
    // output ordering - reachable only if something removes the local-box key mid-pipeline
    // between records. Closing it would require converting the default ahead of the binder,
    // which a compiled cmdlet cannot do. Flagged for the re-gate; not asserted as a non-issue.
    private DbaCmConnectionParameter[]? _defaultComputerName;
    private bool _boundOnCommandLine;
    private bool _seenFirstRecord;

    private static DbaCmConnectionParameter[]? ConvertComputerNameDefault() =>
        (DbaCmConnectionParameter[])LanguagePrimitives.ConvertTo(
            Environment.GetEnvironmentVariable("COMPUTERNAME"),
            typeof(DbaCmConnectionParameter[]), CultureInfo.InvariantCulture);

    protected override void BeginProcessing()
    {
        // DEF-007: snapshot command-line boundness and apply the default once for the
        // invocation (this is also the ONLY conversion an empty pipeline gets, matching the
        // source, whose process block never runs but whose default has already been applied).
        // Reset per invocation, not just assigned: a reused cmdlet instance would otherwise
        // carry _seenFirstRecord=true into the next invocation and convert the default twice
        // for its first record (codex r3).
        _seenFirstRecord = false;
        _defaultComputerName = null;
        _boundOnCommandLine = MyInvocation.BoundParameters.ContainsKey(nameof(ComputerName));
        if (!_boundOnCommandLine)
        {
            _defaultComputerName = ConvertComputerNameDefault();
        }

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BeginScript,
            string.Join(", ", MyInvocation.BoundParameters.Keys), EnableException.ToBool(),
            NestedCommand.BoundCommonParameter(this, "Verbose"), NestedCommand.BoundCommonParameter(this, "Debug")))
        {
            Hashtable? sentinel = item?.BaseObject as Hashtable;
            if (sentinel is not null && sentinel.ContainsKey("__w3087DisableCache"))
            {
                _disableCache = sentinel["__w3087DisableCache"];
                continue;
            }
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                NestedCommand.RemoveDuplicateError(this, nestedError);
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

        // DEF-007: PS re-applies the typed default before every record after the first.
        if (!_boundOnCommandLine && _seenFirstRecord)
        {
            _defaultComputerName = ConvertComputerNameDefault();
        }
        _seenFirstRecord = true;

        // Pipeline binding wins for this record; otherwise the freshly-applied default.
        DbaCmConnectionParameter[]? computers =
            MyInvocation.BoundParameters.ContainsKey(nameof(ComputerName))
                ? ComputerName
                : _defaultComputerName;

        // Stream one hop PER COMPUTER: a whole-array hop batches every element's live
        // Debug/Verbose ahead of all buffered output, where the source's foreach
        // interleaves them per element (W2-010 P2A; coordinator 25a09f3 ruling). The
        // source loop body has no cross-element state.
        //
        foreach (DbaCmConnectionParameter computer in computers ?? Array.Empty<DbaCmConnectionParameter>())
        {
            if (Interrupted)
                return;

            NestedCommand.InvokeScopedStreaming(this, item =>
            {
                if (item?.BaseObject is ErrorRecord nestedError)
                {
                    NestedCommand.RemoveDuplicateError(this, nestedError);
                    WriteError(nestedError);
                    return;
                }
                WriteObject(item);
            }, ProcessScript,
            new[] { computer }, Credential, UseWindowsCredentials.ToBool(),
                OverrideExplicitCredential.ToBool(), OverrideConnectionPolicy.ToBool(),
                DisabledConnectionTypes, DisableBadCredentialCache.ToBool(),
                DisableCimPersistence.ToBool(), DisableCredentialAutoRegister.ToBool(),
                EnableCredentialFailover.ToBool(), WindowsCredentialsAreBad.ToBool(),
                CimWinRMOptions, CimDCOMOptions, AddBadCredential, RemoveBadCredential,
                ClearBadCredential.ToBool(), ClearCredential.ToBool(), ResetCredential.ToBool(),
                ResetConnectionStatus.ToBool(), ResetConfiguration.ToBool(),
                EnableException.ToBool(), _disableCache,
                TestBound(nameof(Credential)), TestBound(nameof(OverrideExplicitCredential)),
                TestBound(nameof(DisabledConnectionTypes)), TestBound(nameof(DisableBadCredentialCache)),
                TestBound(nameof(DisableCimPersistence)), TestBound(nameof(DisableCredentialAutoRegister)),
                TestBound(nameof(EnableCredentialFailover)), TestBound(nameof(WindowsCredentialsAreBad)),
                TestBound(nameof(CimWinRMOptions)), TestBound(nameof(CimDCOMOptions)),
                TestBound(nameof(OverrideConnectionPolicy)), this,
                NestedCommand.BoundCommonParameter(this, "WhatIf"), NestedCommand.BoundCommonParameter(this, "Confirm"),
                NestedCommand.BoundCommonParameter(this, "Verbose"), NestedCommand.BoundCommonParameter(this, "Debug"));
        }
    }

    protected override void EndProcessing()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, EndScript,
            EnableException.ToBool(),
            NestedCommand.BoundCommonParameter(this, "Verbose"), NestedCommand.BoundCommonParameter(this, "Debug")))
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                NestedCommand.RemoveDuplicateError(this, nestedError);
                WriteError(nestedError);
                continue;
            }
            WriteObject(item);
        }
    }
}
