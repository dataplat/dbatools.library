#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// The buffered whole-body hop entry point plus the $error-dedup and bound-common-parameter
/// helpers a hop needs. This satellite standardized on RemoteExecutionService + NestedCommand.Invoke
/// for its OS/WMI/cert commands, so these lived only in the SQL-touching satellites; a command whose
/// unit tests mock a private helper (Invoke-Command2) and a nested cmdlet with -ModuleName dbatools
/// needs the body to run in the real module scope, which is what InvokeScoped provides.
///
/// InvokeScoped and the three helpers are copied verbatim from the canonical
/// dbatools.core copy (the internal class is per-assembly, so each satellite keeps its own). They
/// reuse this satellite's existing ShieldDefaultParameterValues, PropagateActionPreferences,
/// ErrorVariableBridge, InterruptBeacon, ModuleRootSeed* and PreserveErrorIdentity - none of which
/// the existing NestedCommand.Invoke / InvokeStreamed callers reference these new members, so adding
/// them changes no shipped behavior.
/// </summary>
internal static partial class NestedCommand
{
    /// <summary>
    /// Runs a script - typically a module-scoped `&amp; (Get-Module dbatools) { ... }` hop
    /// that reaches PRIVATE functions - with the empty-table PSDPV shield, re-emitting
    /// 3&gt;&amp;1-merged WarningRecords through the host cmdlet's warning stream (caller
    /// -WarningVariable parity, matching how a function-internal call's warnings bubbled)
    /// and returning the remaining output. Engine flow control (a Stop-Function-style
    /// helper's continue/break - PS try/catch cannot intercept those and neither does
    /// this) and terminating errors propagate to the caller, but NOTHING THIS INVOCATION
    /// ALREADY PRODUCED SURVIVES THAT UNWIND: InvokeScript hands back its Collection only on
    /// normal completion, so a guard that warns and then unwinds loses its warning and the
    /// caller's statement dies with no diagnostic at all. A guard at hop TOP LEVEL
    /// (outside any loop) is the shape that hits this; inside a loop the continue binds to
    /// that loop and never reaches the boundary. Contrast InvokeScopedStreaming, which keeps
    /// the warning and loses the flow control instead.
    /// The script is wrapped in an `&amp; {{ ... }} @args` block: InvokeScript's
    /// useLocalScope:false DOT-SOURCES the text, so a bare script's param() would BIND IN
    /// THE CALLER'S SCOPE and die "Cannot overwrite variable X because the variable has
    /// been optimized" whenever the calling function/scriptblock has an optimized local of
    /// the same name. The wrapper block creates a real scope for the param binding while dynamic
    /// READS (preference variables) still resolve through the caller.
    /// </summary>
    internal static Collection<PSObject> InvokeScoped(PSCmdlet host, string scriptText, params object?[] scriptArgs)
    {
        using (ShieldDefaultParameterValues(host))
        using (PropagateActionPreferences(host))
        {
            using ErrorVariableBridge bridge = new ErrorVariableBridge(host);
            // The carrier param is the ONLY name bound in the caller's scope; the args
            // array travels as a single element so null elements survive the InvokeScript
            // object[]-unpacking, then splats positionally into the real scope.
            using InterruptBeacon interruptBeacon = new InterruptBeacon(host);
            string __seedToken = Guid.NewGuid().ToString("N");
            ScriptBlock script = ScriptBlock.Create(
                "param($__nestedCommandArguments, $__nestedInterruptBeacon)\n" + ModuleRootSeedProlog(host, scriptText, __seedToken) + InterruptBeaconSeedProlog(__seedToken) + "& {\n" + scriptText + "\n} @__nestedCommandArguments" + InterruptBeaconSeedEpilog(__seedToken) + ModuleRootSeedEpilog(__seedToken));
            Collection<PSObject> raw = host.InvokeCommand.InvokeScript(false, script, null, new object?[] { scriptArgs, interruptBeacon.State });
            Collection<PSObject> output = new Collection<PSObject>();
            foreach (PSObject item in raw)
            {
                if (item?.BaseObject is WarningRecord warning)
                    host.WriteWarning(warning.Message);
                else if (item?.BaseObject is ErrorRecord nonTerminating)
                {
                    // Re-emit through the cmdlet's own error channel so -ErrorVariable capture
                    // and caller-side preference handling see them, as the function world does.
                    RemoveHopEraDuplicateError(host, nonTerminating, bridge.HopEraBaselineHead);
                    host.WriteError(PreserveErrorIdentity(nonTerminating));
                }
                else
                    output.Add(item!);
            }
            return output;
        }
    }

    // Streams hop output to the host as produced instead of buffering, with the downstream
    // pipeline-stop guard. Prefer this over InvokeScoped for a multi-record emit loop whose later
    // records can terminate (a Stop-Function -Continue under -EnableException throws): buffered
    // InvokeScoped hands its Collection back only on normal completion, so a later throw discards
    // the output already produced for earlier records, whereas the function world had already
    // emitted them. Streaming keeps them (and a top-level guard's warning), losing only the engine
    // flow-control unwind, which the caller absorbs at the nested-pipeline boundary.
    internal static void InvokeScopedStreaming(
        PSCmdlet host,
        Action<PSObject> onOutput,
        string scriptText,
        params object?[] scriptArgs)
    {
        using (ShieldDefaultParameterValues(host))
        using (PropagateActionPreferences(host))
        {
            using ErrorVariableBridge bridge = new ErrorVariableBridge(host);
            Hashtable termination = new Hashtable { ["ErrorRecord"] = null };
            string terminationMarker = "__dbatoolsNestedTermination_" + Guid.NewGuid().ToString("N");
            using InterruptBeacon interruptBeacon = new InterruptBeacon(host);
            string __seedToken = Guid.NewGuid().ToString("N");
            string wrapper =
                "param($__nestedCommandArguments, $__nestedTermination, $__nestedTerminationMarker, $__nestedInterruptBeacon)\n" + ModuleRootSeedProlog(host, scriptText, __seedToken) + InterruptBeaconSeedProlog(__seedToken) + "try { & {\n" + scriptText +
                "\n} @__nestedCommandArguments 6>&1 5>&1 4>&1 3>&1 2>&1 } catch { " +
                "$__nestedTermination.ErrorRecord = $PSItem; " +
                "Write-Output $__nestedTerminationMarker }" + InterruptBeaconSeedEpilog(__seedToken) + ModuleRootSeedEpilog(__seedToken);

            ErrorRecord? terminatingError = null;
            // Pipeline-stop parity: a downstream early stop - e.g. `<cmdlet> | Select-Object -First N`
            // - makes the host's WriteObject throw PipelineStoppedException the instant it has enough.
            // Escaping that from the DataAdded handler killed the child process outright (function
            // world survives). Catch it, BeginStop the nested pipeline (non-blocking - a blocking
            // Stop() from inside the pipeline's own output handler deadlocks) so upstream side effects
            // halt exactly like the function world, then re-throw to the host after Invoke.
            bool downstreamStopped = false;
            using PowerShell nested = PowerShell.Create(RunspaceMode.CurrentRunspace);
            using PSDataCollection<PSObject> output = new PSDataCollection<PSObject>();
            output.DataAdded += (_, eventArgs) =>
            {
                if (downstreamStopped)
                {
                    return;
                }
                PSObject item = output[eventArgs.Index];
                try
                {
                    if (string.Equals(item?.BaseObject as string, terminationMarker, StringComparison.Ordinal))
                    {
                        terminatingError = termination["ErrorRecord"] as ErrorRecord ??
                            throw new InvalidOperationException("Nested command terminated without an ErrorRecord.");
                    }
                    else if (item?.BaseObject is WarningRecord warning)
                    {
                        host.WriteWarning(warning.Message);
                    }
                    else if (item?.BaseObject is VerboseRecord verbose)
                    {
                        host.WriteVerbose(verbose.Message);
                    }
                    else if (item?.BaseObject is DebugRecord debug)
                    {
                        host.WriteDebug(debug.Message);
                    }
                    else if (IsInformationStreamRecord(item?.BaseObject))
                    {
                        RelayInformationStreamRecord(host, item!.BaseObject);
                    }
                    else if (item?.BaseObject is ErrorRecord nonTerminating)
                    {
                        // Same channel correction as the item-form branches above.
                        host.WriteError(PreserveErrorIdentity(nonTerminating));
                    }
                    else
                    {
                        onOutput(item!);
                    }
                }
                catch (PipelineStoppedException)
                {
                    downstreamStopped = true;
                    try
                    {
                        nested.BeginStop(null, null);
                    }
                    catch (PSInvalidOperationException)
                    {
                        // The pipeline may already be past the stoppable state; the re-throw below still unwinds the host.
                    }
                }
            };

            nested.AddScript(wrapper, useLocalScope: false)
                .AddArgument(scriptArgs)
                .AddArgument(termination)
                .AddArgument(terminationMarker)
                .AddArgument(interruptBeacon.State);
            try
            {
                nested.Invoke<PSObject>(null, output, null);
            }
            catch (PipelineStoppedException)
            {
                downstreamStopped = true;
            }

            if (downstreamStopped)
            {
                // Honor the downstream stop on the HOST pipeline - unwinds ProcessRecord
                // exactly like the function world's StopUpstreamCommands.
                throw new PipelineStoppedException();
            }

            if (terminatingError is not null)
            {
                bridge.Complete(terminatingError);
                RemoveCapturedErrorBookkeeping(host, terminatingError);
                host.InvokeCommand.InvokeScript(
                    false,
                    ScriptBlock.Create("param($__record) throw $__record"),
                    null,
                    new object?[] { terminatingError });
                throw new InvalidOperationException("Nested terminating ErrorRecord unexpectedly returned.");
            }
        }
    }

    // The information stream is probed by type NAME and relayed by reflection, never by a
    // typed reference. System.Management.Automation.InformationRecord and Cmdlet.WriteInformation
    // both arrived in PowerShell 5.0 and are absent from the System.Management.Automation 3.0.0.0
    // surface this net472 assembly is compiled against. A typed reference would sit inside the
    // DataAdded handler above, and the CLR jits a method body whole: on a Windows PowerShell 3.0
    // or 4.0 host the FIRST output record of ANY hopped command would fail to jit the handler and
    // throw, whether or not an information record ever appeared. Probing by name keeps every
    // token in this file resolvable on the floor while preserving the PS 5+ relay exactly.
    private static bool IsInformationStreamRecord(object? value)
    {
        for (Type? candidate = value?.GetType(); candidate is not null; candidate = candidate.BaseType)
        {
            if (string.Equals(candidate.FullName, "System.Management.Automation.InformationRecord", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static void RelayInformationStreamRecord(PSCmdlet host, object record)
    {
        MethodInfo? writeInformation = typeof(Cmdlet).GetMethod(
            "WriteInformation",
            new Type[] { typeof(object), typeof(string[]) });
        if (writeInformation is null)
        {
            // No information stream on this engine, so the nested pipeline cannot have merged a
            // record into 6>&1 in the first place. Drop it rather than fail the whole relay.
            return;
        }

        Type recordType = record.GetType();
        object? messageData = recordType.GetProperty("MessageData")?.GetValue(record, null);
        List<string> tags = new List<string>();
        if (recordType.GetProperty("Tags")?.GetValue(record, null) is IEnumerable rawTags)
        {
            foreach (object? tag in rawTags)
            {
                if (tag is string text)
                    tags.Add(text);
            }
        }

        try
        {
            writeInformation.Invoke(host, new object?[] { messageData, tags.ToArray() });
        }
        catch (TargetInvocationException invocation) when (invocation.InnerException is not null)
        {
            // Reflection wraps whatever the real call throws. The caller's stop guard matches
            // PipelineStoppedException by type, so the original has to come back out unwrapped.
            ExceptionDispatchInfo.Capture(invocation.InnerException).Throw();
        }
    }

    private static void RemoveCapturedErrorBookkeeping(PSCmdlet host, ErrorRecord record)
    {
        // ThrowTerminatingError will add the final outer record; de-dup is best effort only.
        // The match rule is shared with every hop cmdlet's RemoveHopErrorBookkeeping.
        RemoveDuplicateError(host, record);
    }

    // An InnerException chain longer than this is a cycle or a pathological wrap; a
    // bookkeeping helper must never be the thing that hangs the pipeline.
    private const int MaximumExceptionChainDepth = 32;

    /// <summary>
    /// Drops $error[0] when it is the hop's own copy of the supplied record. A hop runs its
    /// body in a nested pipeline with 2&gt;&amp;1 merged, so a non-terminating failure lands in
    /// the runspace's $error AND arrives at the host as an output item; re-emitting it with
    /// WriteError would push a second copy and leave the caller with one more record than the
    /// retired function produced. Best effort: a runspace that denies $error access keeps the
    /// duplicate rather than failing the command.
    /// </summary>
    /// <param name="host">The cmdlet whose session state owns $error</param>
    /// <param name="record">The record about to be re-emitted through the host's error stream</param>
    internal static void RemoveDuplicateError(PSCmdlet host, ErrorRecord record)
    {
        if (host is null || record is null)
        {
            return;
        }

        try
        {
            if (host.SessionState.PSVariable.GetValue("Error") is not ArrayList errorList || errorList.Count == 0)
            {
                return;
            }
            if (errorList[0] is not ErrorRecord first)
            {
                return;
            }
            if (IsSameFailure(first, record))
            {
                errorList.RemoveAt(0);
            }
        }
        catch
        {
            // Best-effort bookkeeping only.
        }
    }

    /// <summary>
    /// Removes one matching record from the hop-era portion of $error. The boundary is the same
    /// pre-hop head used by ErrorVariableBridge; scanning past it could delete session history
    /// and make the bridge misclassify that history as hop output.
    /// </summary>
    internal static void RemoveHopEraDuplicateError(PSCmdlet host, ErrorRecord record, object? hopEraBaselineHead)
    {
        if (host is null || record is null)
        {
            return;
        }

        try
        {
            if (host.SessionState.PSVariable.GetValue("Error") is not ArrayList errorList || errorList.Count == 0)
            {
                return;
            }

            int limit = HopEraLimit(errorList, hopEraBaselineHead);
            for (int i = 0; i < limit; i++)
            {
                if (errorList[i] is ErrorRecord candidate && IsSameFailure(candidate, record))
                {
                    errorList.RemoveAt(i);
                    return;
                }
            }
        }
        catch
        {
            // Best-effort bookkeeping only.
        }
    }

    private static int HopEraLimit(ArrayList errorList, object? hopEraBaselineHead)
    {
        if (hopEraBaselineHead is null)
        {
            return errorList.Count;
        }
        for (int i = 0; i < errorList.Count; i++)
        {
            if (ReferenceEquals(errorList[i], hopEraBaselineHead))
            {
                return i;
            }
        }
        return errorList.Count;
    }

    /// <summary>
    /// Whether the record sitting on top of $error is the hop's own copy of the record the host
    /// is about to re-emit. Every arm is anchored to OBJECT IDENTITY.
    ///
    /// The rule used to fall back to comparing exception message TEXT, and that arm could
    /// dequeue a completely unrelated record: two instances both failing with dbatools' bare
    /// "Failure" message is the everyday case, and nothing in a text comparison ties the
    /// candidate to the failure this hop produced. The hop's own duplicate then stayed in
    /// $error while somebody else's diagnostic disappeared from it.
    /// </summary>
    /// <param name="first">The record currently at $error[0]</param>
    /// <param name="record">The record the host is about to re-emit</param>
    /// <returns>True when they are the same failure</returns>
    internal static bool IsSameFailure(ErrorRecord? first, ErrorRecord? record)
    {
        if (first is null || record is null)
        {
            return false;
        }
        if (ReferenceEquals(first, record))
        {
            return true;
        }

        Exception? firstException = first.Exception;
        Exception? recordException = record.Exception;
        if (firstException is null || recordException is null)
        {
            return false;
        }
        if (ReferenceEquals(firstException, recordException))
        {
            return true;
        }

        // A re-wrapped record still carries the original exception OBJECT in its chain -
        // Stop-Function builds new Exception(message, record.Exception) - so walking the chain
        // recognizes the duplicate without ever comparing text.
        return ChainContains(firstException, recordException)
            || ChainContains(recordException, firstException);
    }

    private static bool ChainContains(Exception outer, Exception target)
    {
        Exception? current = outer.InnerException;
        for (int depth = 0; current is not null && depth < MaximumExceptionChainDepth; depth++)
        {
            if (ReferenceEquals(current, target))
            {
                return true;
            }
            current = current.InnerException;
        }
        return false;
    }

    /// <summary>
    /// The bound truthiness of a common parameter (-Verbose/-Debug) for forwarding into a hop
    /// body: true or false when the caller bound it, null when they did not, so the hop can tell
    /// "explicitly off" from "not specified" - a distinction a plain bool loses.
    /// </summary>
    /// <param name="host">The cmdlet whose invocation carries the bound parameters</param>
    /// <param name="name">The common parameter name</param>
    /// <returns>The bound value's truthiness, or null when unbound</returns>
    internal static object? BoundCommonParameter(PSCmdlet host, string name)
    {
        if (host.MyInvocation.BoundParameters.TryGetValue(name, out object? value))
        {
            return LanguagePrimitives.IsTrue(value);
        }
        return null;
    }
}
