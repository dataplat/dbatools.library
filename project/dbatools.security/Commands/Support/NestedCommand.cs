#nullable enable

using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Management.Automation;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Invokes dbatools PowerShell commands and module-scoped script bodies from inside a compiled
/// cmdlet, reproducing the scope and stream behavior a PowerShell function had around its own
/// internal calls.
/// </summary>
/// <remarks>
/// Commands are named by fixed literals at every call site; no caller input reaches the script
/// text. Name resolution picks up whichever implementation is live, so a command that exists as
/// either a script function or a compiled cmdlet behaves identically through this entry point.
/// </remarks>
internal static class NestedCommand
{
    /// <summary>
    /// Replaces the effective $PSDefaultParameterValues with an empty table for the duration of a
    /// nested invocation, returning a handle that restores the original on disposal.
    /// </summary>
    /// <param name="host">The cmdlet whose session state supplies the variable.</param>
    /// <returns>A disposable that restores the caller's table, or null when none is defined.</returns>
    /// <remarks>
    /// A PowerShell function's nested command calls resolved $PSDefaultParameterValues through the
    /// module scope chain, where none is defined - so neither a caller-local nor a global default
    /// ever reached them. Invoking a script from a cmdlet chains to the CALLER's scope instead, so
    /// without this shield a caller-local default such as '*-Dba*:WarningVariable' would re-bind on
    /// every nested call and re-initialize the very variable the outer command is capturing into,
    /// discarding it. An empty table, not the global one, is the faithful reproduction.
    /// </remarks>
    internal static IDisposable? ShieldDefaultParameterValues(PSCmdlet host)
    {
        object? effective = host.SessionState.PSVariable.GetValue("PSDefaultParameterValues");
        if (effective is null)
            return null;
        host.SessionState.PSVariable.Set("PSDefaultParameterValues", new System.Management.Automation.DefaultParameterDictionary());
        return new DefaultParameterRestore(host, effective);
    }

    /// <summary>Restores a saved $PSDefaultParameterValues table when disposed.</summary>
    private sealed class DefaultParameterRestore : IDisposable
    {
        private readonly PSCmdlet _host;
        private readonly object _saved;

        /// <summary>Captures the table to restore.</summary>
        /// <param name="host">The cmdlet whose session state holds the variable.</param>
        /// <param name="saved">The table to put back on disposal.</param>
        internal DefaultParameterRestore(PSCmdlet host, object saved)
        {
            _host = host;
            _saved = saved;
        }

        /// <summary>Puts the caller's original table back.</summary>
        public void Dispose()
        {
            _host.SessionState.PSVariable.Set("PSDefaultParameterValues", _saved);
        }
    }

    /// <summary>
    /// Invokes a named command and returns its output once the command has completed.
    /// </summary>
    /// <param name="host">The calling cmdlet.</param>
    /// <param name="commandName">A fixed literal command name.</param>
    /// <param name="parameters">Parameters to splat; null-valued keys bind, matching splat semantics.</param>
    /// <param name="pipelineInput">Optional input to pipe into the command.</param>
    /// <returns>The command's output with warning records removed.</returns>
    /// <remarks>
    /// For call sites that assigned the output to a variable or discarded it, where nothing depends
    /// on output arriving before the command finishes. Warnings are merged back and re-emitted
    /// through the HOST cmdlet's warning stream: an inner function's warnings bubbled through the
    /// outer function's runtime, so a caller's -WarningVariable captured them alongside the outer
    /// command's own. A script invoked from a cmdlet bypasses the host runtime, so without this
    /// merge those records would never reach the caller's variable.
    /// </remarks>
    internal static Collection<PSObject> Invoke(PSCmdlet host, string commandName, IDictionary parameters, object? pipelineInput = null)
    {
        using (ShieldDefaultParameterValues(host))
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
                else
                    output.Add(item!);
            }
            return output;
        }
    }

    /// <summary>
    /// Runs a script - typically a module-scoped hop that reaches private functions - and returns
    /// its output once the script has completed.
    /// </summary>
    /// <param name="host">The calling cmdlet.</param>
    /// <param name="scriptText">The script to run.</param>
    /// <param name="scriptArgs">Arguments splatted positionally into the script.</param>
    /// <returns>The script's output with warning records removed.</returns>
    /// <remarks>
    /// Merged warning records re-emit through the host's warning stream for -WarningVariable
    /// parity. Engine flow control raised by a helper (a continue or break that PowerShell
    /// try/catch cannot intercept) and terminating errors propagate to the caller.
    /// The script is wrapped in an "&amp; { ... } @args" block because the invocation dot-sources
    /// the text: a bare script's param() would bind IN THE CALLER'S SCOPE and fail with "Cannot
    /// overwrite variable X because the variable has been optimized" whenever the calling function
    /// has an optimized local of the same name. The wrapper block creates a real scope for the
    /// param binding while dynamic reads such as preference variables still resolve through the
    /// caller. The argument array travels as a SINGLE element so that null elements survive the
    /// object[] unpacking the invocation performs, then splats positionally into the real scope.
    /// </remarks>
    internal static Collection<PSObject> InvokeScoped(PSCmdlet host, string scriptText, params object?[] scriptArgs)
    {
        using (ShieldDefaultParameterValues(host))
        {
            ScriptBlock script = ScriptBlock.Create(
                "param($__nestedCommandArguments)\n& {\n" + scriptText + "\n} @__nestedCommandArguments");
            Collection<PSObject> raw = host.InvokeCommand.InvokeScript(false, script, null, new object?[] { scriptArgs });
            Collection<PSObject> output = new Collection<PSObject>();
            foreach (PSObject item in raw)
            {
                if (item?.BaseObject is WarningRecord warning)
                    host.WriteWarning(warning.Message);
                else
                    output.Add(item!);
            }
            return output;
        }
    }

    /// <summary>
    /// Invokes a named command over a steppable pipeline, forwarding output as it is produced.
    /// </summary>
    /// <param name="host">The calling cmdlet.</param>
    /// <param name="commandName">A fixed literal command name.</param>
    /// <param name="parameters">Parameters to splat into the command.</param>
    /// <param name="pipelineInput">Input fed to the command one item at a time.</param>
    /// <remarks>
    /// For call sites that piped input to the command at top level, where output must reach the
    /// user's pipeline as it is produced - progress objects, for instance - rather than after the
    /// command completes.
    /// </remarks>
    internal static void InvokeStreamed(PSCmdlet host, string commandName, IDictionary parameters, IEnumerable pipelineInput)
    {
        using (ShieldDefaultParameterValues(host))
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
    /// Routes one streamed item: warning records go to the host's warning stream, everything else
    /// stays pipeline output.
    /// </summary>
    /// <param name="host">The calling cmdlet.</param>
    /// <param name="item">The item produced by the pipeline.</param>
    private static void ForwardStreamedItem(PSCmdlet host, object? item)
    {
        object? unwrapped = item is PSObject psObject ? psObject.BaseObject : item;
        if (unwrapped is WarningRecord warning)
            host.WriteWarning(warning.Message);
        else
            host.WriteObject(item);
    }
}
