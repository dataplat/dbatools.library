#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Invokes other dbatools PUBLIC commands from inside a ported cmdlet through the caller's
/// own runspace, exactly the way the retired PS function invoked them: by command name, with
/// a splatted parameter table (null-valued keys BIND, matching PS splat semantics), streams
/// flowing through to the user. During the hybrid period the name resolves to whichever
/// implementation is live (PS function or flipped cmdlet) — byte-parity either way, and the
/// kill-switch keeps working. Pattern precedent: RemoteExecutionService (Wave 5) drives
/// engine cmdlets through nested pipelines.
/// Command names are FIXED LITERALS at every call site; no user input reaches the script text.
/// VERBATIM COPY of project/dbatools.core/Commands/Support/NestedCommand.cs (W5-022): the class is
/// internal per-assembly and the shared dbatools project is pinned to LangVersion 7.3, so promoting
/// it there means an owner LangVersion/style decision - OWNER HAND-BACK, keep the copies in sync.
/// </summary>
internal static class NestedCommand
{
    /// <summary>
    /// Replicates the module scope boundary the retired PS functions had around their
    /// internal calls: a PS function's nested *-Dba* invocations resolved
    /// $PSDefaultParameterValues through the dbatools module scope chain (module -> global),
    /// so a caller-LOCAL dictionary — like the test suite's file-scoped
    /// '*-Dba*:WarningVariable' = 'WarnVar' default — never applied to them. InvokeScript
    /// from a cmdlet chains to the caller's scope instead, so without this shield every
    /// nested *-Dba* command re-binds such a default and re-initializes the very variable
    /// the OUTER command is capturing warnings into, wiping it (lab-proven: outer-only
    /// pattern captures fine, nested-only pattern clobbers even an explicit outer
    /// -WarningVariable of the same name). Swaps the effective value for the global one for
    /// the duration of the nested invocation and restores it afterwards; no-op when
    /// effective and global already match (no caller-local dictionary — the common case).
    /// </summary>
    private static IDisposable? ShieldDefaultParameterValues(PSCmdlet host)
    {
        object? effective = host.SessionState.PSVariable.GetValue("PSDefaultParameterValues");
        if (effective is null)
            return null;
        object? globalValue = host.SessionState.PSVariable.GetValue("global:PSDefaultParameterValues");
        if (ReferenceEquals(effective, globalValue))
            return null;
        host.SessionState.PSVariable.Set("PSDefaultParameterValues", globalValue);
        return new DefaultParameterRestore(host, effective);
    }

    private sealed class DefaultParameterRestore : IDisposable
    {
        private readonly PSCmdlet _host;
        private readonly object _saved;

        internal DefaultParameterRestore(PSCmdlet host, object saved)
        {
            _host = host;
            _saved = saved;
        }

        public void Dispose()
        {
            _host.SessionState.PSVariable.Set("PSDefaultParameterValues", _saved);
        }
    }

    // Action-preference common parameters bound on the cmdlet must govern the hop body the way
    // they govern a function body: the engine translates -WarningAction into a function-local
    // $WarningPreference, but a compiled cmdlet gets engine-level handling only, so the nested
    // scriptblock inherited the AMBIENT preferences instead. Measured divergences (E's
    // W2-161/165/169 suite): -WarningAction SilentlyContinue suppressed a source-internal warning
    // in the function world but not in the hop, and -ErrorAction Stop terminated inside the
    // source's try/catch in the function world but outside it in the hop. Setting the preference
    // variables for the invocation window (same save/restore shape as the shield above) restores
    // function-scope semantics; only EXPLICITLY BOUND preferences propagate - unbound ones keep
    // inheriting the ambient values, exactly like a function call would.
    private static readonly (string ParameterName, string PreferenceVariable)[] ActionPreferenceMap =
    {
        ("ErrorAction", "ErrorActionPreference"),
        ("WarningAction", "WarningPreference"),
        ("InformationAction", "InformationPreference"),
        ("ProgressAction", "ProgressPreference"),
    };

    internal static IDisposable? PropagateActionPreferences(PSCmdlet host)
    {
        List<(string Name, object? Saved)>? saved = null;
        foreach ((string parameterName, string preferenceVariable) in ActionPreferenceMap)
        {
            if (!host.MyInvocation.BoundParameters.TryGetValue(parameterName, out object? bound) || bound is null)
                continue;
            saved ??= new List<(string, object?)>();
            saved.Add((preferenceVariable, host.SessionState.PSVariable.GetValue(preferenceVariable)));
            host.SessionState.PSVariable.Set(preferenceVariable, bound);
        }
        return saved is null ? null : new ActionPreferenceRestore(host, saved);
    }

    private sealed class ActionPreferenceRestore : IDisposable
    {
        private readonly PSCmdlet _host;
        private readonly List<(string Name, object? Saved)> _saved;

        internal ActionPreferenceRestore(PSCmdlet host, List<(string Name, object? Saved)> saved)
        {
            _host = host;
            _saved = saved;
        }

        public void Dispose()
        {
            foreach ((string name, object? value) in _saved)
            {
                _host.SessionState.PSVariable.Set(name, value);
            }
        }
    }

    /// <summary>
    /// Buffered invocation — for PS call sites that assigned the output to a variable or
    /// discarded it ($x = Get-DbaBackupInformation ... / $null = ... | Test-DbaBackupInformation).
    /// Nested warnings merge back (3&gt;&amp;1) and re-emit through the HOST cmdlet's own
    /// warning stream: in PS the inner function's warnings bubbled through the outer
    /// function's runtime, so the caller's -WarningVariable captured them alongside the
    /// outer command's own warnings ("Database X exists, so WithReplace must be specified"
    /// arrives next to "unable to be restored"). InvokeScript-invoked cmdlets bypass the
    /// host runtime, so without the merge those records never reach the caller's variable.
    /// </summary>
    internal static Collection<PSObject> Invoke(PSCmdlet host, string commandName, IDictionary parameters, object? pipelineInput = null)
    {
        using (ShieldDefaultParameterValues(host))
        using (PropagateActionPreferences(host))
        {
            Collection<PSObject> raw;
            if (pipelineInput is null)
            {
                ScriptBlock script = ScriptBlock.Create("param($__parameters) & " + commandName + " @__parameters 3>&1");
                raw = host.InvokeCommand.InvokeScript(false, script, null, parameters);
            }
            else
            {
                ScriptBlock piped = ScriptBlock.Create("param($__parameters, $__input) $__input | & " + commandName + " @__parameters 3>&1");
                raw = host.InvokeCommand.InvokeScript(false, piped, null, parameters, pipelineInput);
            }

            Collection<PSObject> output = new Collection<PSObject>();
            foreach (PSObject item in raw)
            {
                if (item?.BaseObject is WarningRecord warning)
                    host.WriteWarning(warning.Message);
                else if (item?.BaseObject is ErrorRecord nonTerminating)
                    // Re-emit through the cmdlet's own error channel so -ErrorVariable capture
                    // and caller-side preference handling see them, as the function world does.
                    host.WriteError(nonTerminating);
                else
                    output.Add(item!);
            }
            return output;
        }
    }

    /// <summary>
    /// Streaming invocation over a steppable pipeline — for PS call sites that piped input to
    /// the command at top level, where output must reach the user's pipeline as it is
    /// produced (restore progress objects), not after the command completes.
    /// </summary>
    internal static void InvokeStreamed(PSCmdlet host, string commandName, IDictionary parameters, IEnumerable pipelineInput)
    {
        using (ShieldDefaultParameterValues(host))
        using (PropagateActionPreferences(host))
        {
            ScriptBlock script = ScriptBlock.Create("param($__parameters) & " + commandName + " @__parameters 3>&1");
            SteppablePipeline pipeline = script.GetSteppablePipeline(CommandOrigin.Internal, new object[] { parameters });
            bool stopped = false;
            try
            {
                pipeline.Begin(true);
                foreach (object? item in pipelineInput)
                {
                    foreach (object output in pipeline.Process(item))
                        ForwardStreamedItem(host, output);
                }
                foreach (object output in pipeline.End())
                    ForwardStreamedItem(host, output);
            }
            catch
            {
                stopped = true;
                try { pipeline.Dispose(); }
                catch { /* the failed pipeline may already be dead; the original error wins */ }
                throw;
            }
            finally
            {
                if (!stopped)
                    pipeline.Dispose();
            }
        }
    }

    /// <summary>
    /// Streamed counterpart of the buffered warning re-emit: merged-back warning records
    /// route through the host's warning stream as they arrive, everything else stays
    /// pipeline output.
    /// </summary>
    private static void ForwardStreamedItem(PSCmdlet host, object? item)
    {
        object? unwrapped = item is PSObject psObject ? psObject.BaseObject : item;
        if (unwrapped is WarningRecord warning)
            host.WriteWarning(warning.Message);
        else if (unwrapped is ErrorRecord nonTerminating)
            // Same channel correction as the item-form branches above.
            host.WriteError(nonTerminating);
        else
            host.WriteObject(item);
    }
}
