#nullable enable

using System;
using System.Collections;
using System.Globalization;
using System.Management.Automation;
using System.Numerics;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Gets SQL Server backup history from msdb. Port of public/Get-DbaDbBackupHistory.ps1 (W3-028). A
/// READ-ONLY getter (queries msdb backup tables via T-SQL and emits BackupHistory objects; no mutation).
/// begin computes device/backup-type filters + internalLsnSort from CONSTANT params (recomputed inline
/// per-record in the process hop, so NO cross-record state bag) and emits two -Level System diagnostics
/// (.ParameterSetName + System.Management.Automation.PSBoundParametersDictionary.Keys) ONCE - those ride a minimal BeginScript hop with
/// the real cmdlet ParameterSetName/bound-key carriers. process STREAMS per record via
/// InvokeScopedStreaming (3 Stop-Function: AgCheck-deprecation+return, Since-type+return, connection
/// -Continue). Surface is NAMED-ONLY (no positions; baseline reflection, source SHA256 09AA831D..).
/// The two (Get-PSCallStack)[1].Command self-recursion guards (L373/L460) are carried VERBATIM: measured
/// in both editions, L373 is dead-in-practice ('{ScriptBlock}' curly never matches the real angle-bracket
/// frame -> always FALSE, function-world too) and L460's leading-space pattern is always-true; the hop's
/// &lt;ScriptBlock&gt; frame reproduces both identically, so no caller carrier is needed. Substitution:
/// -FunctionName Get-DbaDbBackupHistory on the 3 Stop-Function (sibling convention); Write-Message
/// verbatim (DEF-006 -ModuleName is the fleet sweep). Since is [psobject] with the epoch computed-default
/// resolved in C#; LsnSort scalar-ValidateSet -> [PsStringCast], Type array-ValidateSet no cast.
/// codex r1 dispositions: #1 (TimeSpan Since cross-record) FIXED via the cached _resolvedSince field; #2
/// (explicit null RecoveryFork) FIXED via [AllowNull]. DISCLOSED BOUNDS (contrived, near-unreachable):
/// #3 explicit "-LastLsn $null" (source [bigint]$null->0; the compiled non-nullable BigInteger rejects
/// null; making it nullable risks the reflected-surface parity, and unbound already defaults to 0);
/// #4 a null element inside -Type (both reject the call, only the error text differs); #5/#6 the two
/// (Get-PSCallStack)[1].Command guards diverge ONLY for absurd callers - a function literally named
/// "{ScriptBlock}" (curly) or one whose name begins with a leading space - which no realistic code emits,
/// and the recursion indirection means even a caller carrier could not fix the recursive leg. Same
/// disclosed-bound class as the accepted W2-206 alias-precedence P2. codex r2: #1 (AgCheck vs overflowing
/// TimeSpan ordering) FIXED (guard the C# conversion with !AgCheck); #2 (in-hop Write-Message reports
/// FunctionName=&lt;ScriptBlock&gt;) is the DEF-006 -FunctionName class - the certified sibling
/// GetDbaDbRestoreHistory leaves Write-Message verbatim too; routed to the DEF-006 batch pass, DISCLOSED.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbBackupHistory", DefaultParameterSetName = "Default")]
public sealed partial class GetDbaDbBackupHistoryCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Database(s) to include.</summary>
    [Parameter]
    public object[]? Database { get; set; }

    /// <summary>Database(s) to exclude.</summary>
    [Parameter]
    public object[]? ExcludeDatabase { get; set; }

    /// <summary>Include copy-only backups (excluded by default).</summary>
    [Parameter]
    public SwitchParameter IncludeCopyOnly { get; set; }

    /// <summary>Return every column (raw select). Only valid outside the -Last* recovery-chain paths.</summary>
    [Parameter(ParameterSetName = "NoLast")]
    public SwitchParameter Force { get; set; }

    /// <summary>Only backups since this DateTime (or a TimeSpan back from now). Defaults to the epoch.</summary>
    [Parameter]
    public PSObject? Since { get; set; }

    /// <summary>Restrict to a specific recovery fork GUID (or empty).</summary>
    // FINAL shape (5 codex rounds, cap): the source (line 236) validates via ValidateScript -
    // GUID -match (whose trailing $ is newline-tolerant) OR exact '' -eq. This pattern reproduces
    // both branches exactly: the GUID alternative keeps $ (same -match semantics), the empty
    // alternative uses \z (exact empty, matching '' -eq which a bare ^$ would not - r4's catch).
    // [PsStringCast] coerces bound $null to "" first, as the source's [string] cast does. On the
    // record: r5 briefly removed the pattern on a misread of the source (grep window missed the
    // attribute line above the param) - retracted after direct source verification.
    [Parameter]
    [PsStringCast]
    [ValidatePattern(@"^(\{){0,1}[0-9a-fA-F]{8}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{12}(\}){0,1}$|^\z")]
    public string? RecoveryFork { get; set; }

    /// <summary>Return the last full recovery chain (full + diff + logs) per database.</summary>
    [Parameter]
    public SwitchParameter Last { get; set; }

    /// <summary>Return the last full backup per database.</summary>
    [Parameter]
    public SwitchParameter LastFull { get; set; }

    /// <summary>Return the last differential backup per database.</summary>
    [Parameter]
    public SwitchParameter LastDiff { get; set; }

    /// <summary>Return the last log backup per database.</summary>
    [Parameter]
    public SwitchParameter LastLog { get; set; }

    /// <summary>Filter by device type(s).</summary>
    [Parameter]
    public string[]? DeviceType { get; set; }

    /// <summary>Return the raw per-media-family rows instead of grouped backup sets.</summary>
    [Parameter]
    public SwitchParameter Raw { get; set; }

    /// <summary>Only backups with last_lsn greater than this.</summary>
    [Parameter]
    public BigInteger LastLsn { get; set; }

    /// <summary>Include mirrored media families.</summary>
    [Parameter]
    public SwitchParameter IncludeMirror { get; set; }

    /// <summary>Filter by backup type(s).</summary>
    [Parameter]
    [ValidateSet("Full", "Log", "Differential", "File", "Differential File", "Partial Full", "Partial Differential")]
    public string[]? Type { get; set; }

    /// <summary>Deprecated no-op retained for surface parity.</summary>
    [Parameter]
    public SwitchParameter AgCheck { get; set; }

    /// <summary>Ignore differential backups when building the -Last chain.</summary>
    [Parameter]
    public SwitchParameter IgnoreDiffBackup { get; set; }

    /// <summary>Which LSN column orders the results.</summary>
    [Parameter]
    [PsStringCast]
    [ValidateSet("FirstLsn", "DatabaseBackupLsn", "LastLsn")]
    public string LsnSort { get; set; } = "LastLsn";

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // codex r1 #1: the source mutates $Since from TimeSpan to DateTime on the FIRST process() call
    // (via (Get-Date).Add) and that DateTime PERSISTS across subsequent pipeline records. Resolving it
    // per-record would recompute (Get-Date) each record -> a different cutoff per piped instance. Resolve
    // ONCE here (cached), reproducing the function's first-record-computes-then-persists semantics.
    private object? _resolvedSince;
    private bool _sinceResolved;

    protected override void BeginProcessing()
    {
        NestedCommand.InvokeScoped(this, BeginScript,
            MyInvocation.BoundParameters.Count > 0 ? this.ParameterSetName : "Default",
            string.Join(", ", MyInvocation.BoundParameters.Keys),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug"));
    }

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        // PS bind-time default: $Since = epoch DateTime. Resolve ONCE (first record) and cache: the raw
        // bound/default value, and if it is a TimeSpan, the source's (Get-Date).Add() to a fixed DateTime
        // that persists across records. A non-TimeSpan value passes through raw so the hop's
        // "-isnot [DateTime]" Stop-Function still fires for a bad type, exactly as the function.
        if (!_sinceResolved)
        {
            object raw = MyInvocation.BoundParameters.ContainsKey("Since")
                ? (Since is null ? null! : Since.BaseObject)
                : DateTime.ParseExact("1970-01-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
            // codex r2 #1: the source checks -AgCheck (Stop-Function + return, L299) BEFORE the Since
            // TimeSpan conversion (L305), so an overflowing TimeSpan never converts under -AgCheck.
            // Guard the C# conversion the same way (AgCheck set -> pass raw TimeSpan; the hop's AgCheck
            // return fires first, exactly as the function - no premature ArgumentOutOfRangeException).
            if (!AgCheck.ToBool() && raw is TimeSpan ts)
                raw = DateTime.Now.Add(ts);
            _resolvedSince = raw;
            _sinceResolved = true;
        }
        object sinceValue = _resolvedSince!;

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
            SqlInstance, SqlCredential, Database, ExcludeDatabase, IncludeCopyOnly.ToBool(), Force.ToBool(),
            sinceValue, RecoveryFork, Last.ToBool(), LastFull.ToBool(), LastDiff.ToBool(), LastLog.ToBool(),
            DeviceType, Raw.ToBool(), LastLsn, IncludeMirror.ToBool(), Type, AgCheck.ToBool(),
            IgnoreDiffBackup.ToBool(), LsnSort, EnableException.ToBool(),
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