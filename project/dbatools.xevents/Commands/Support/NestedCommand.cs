#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Runs module-scoped PowerShell compatibility hops through the invoking cmdlet's runspace.
/// The empty PSDefaultParameterValues shield preserves the scope boundary of retired dbatools
/// functions, and merged warnings are re-emitted through the compiled cmdlet's own channel.
/// </summary>
internal static class NestedCommand
{
    internal static IDisposable? ShieldDefaultParameterValues(PSCmdlet host)
    {
        object? effective = host.SessionState.PSVariable.GetValue("PSDefaultParameterValues");
        if (effective is null)
            return null;

        host.SessionState.PSVariable.Set(
            "PSDefaultParameterValues",
            new DefaultParameterDictionary());
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

    internal static IEnumerable<PSObject> InvokeScoped(
        PSCmdlet host,
        string scriptText,
        params object?[] scriptArgs)
    {
        using (ShieldDefaultParameterValues(host))
        {
            Hashtable termination = new Hashtable { ["ErrorRecord"] = null };
            string terminationMarker = "__dbatoolsNestedTermination_" + Guid.NewGuid().ToString("N");
            ScriptBlock script = ScriptBlock.Create(
                "param($__nestedCommandArguments, $__nestedTermination, $__nestedTerminationMarker)\ntry { & {\n" + scriptText +
                "\n} @__nestedCommandArguments } catch { " +
                "$__nestedTermination.ErrorRecord = $PSItem; " +
                "Write-Output $__nestedTerminationMarker }");
            Collection<PSObject> raw = host.InvokeCommand.InvokeScript(
                false,
                script,
                null,
                new object?[] { scriptArgs, termination, terminationMarker });
            foreach (PSObject item in raw)
            {
                if (string.Equals(item?.BaseObject as string, terminationMarker, StringComparison.Ordinal))
                {
                    if (termination["ErrorRecord"] is not ErrorRecord terminatingError)
                        throw new InvalidOperationException("Nested command terminated without an ErrorRecord.");
                    RemoveCapturedErrorBookkeeping(host, terminatingError);
                    host.InvokeCommand.InvokeScript(
                        false,
                        ScriptBlock.Create("param($__record) throw $__record"),
                        null,
                        new object?[] { terminatingError });
                    throw new InvalidOperationException("Nested terminating ErrorRecord unexpectedly returned.");
                }
                else if (item?.BaseObject is WarningRecord warning)
                    host.WriteWarning(warning.Message);
                else
                    yield return item!;
            }
        }
    }

    internal static void InvokeScopedStreaming(
        PSCmdlet host,
        Action<PSObject> onOutput,
        string scriptText,
        params object?[] scriptArgs)
    {
        using (ShieldDefaultParameterValues(host))
        {
            Hashtable termination = new Hashtable { ["ErrorRecord"] = null };
            string terminationMarker = "__dbatoolsNestedTermination_" + Guid.NewGuid().ToString("N");
            string wrapper =
                "param($__nestedCommandArguments, $__nestedTermination, $__nestedTerminationMarker)\ntry { & {\n" + scriptText +
                "\n} @__nestedCommandArguments 6>&1 5>&1 4>&1 3>&1 2>&1 } catch { " +
                "$__nestedTermination.ErrorRecord = $PSItem; " +
                "Write-Output $__nestedTerminationMarker }";

            ErrorRecord? terminatingError = null;
            using PowerShell nested = PowerShell.Create(RunspaceMode.CurrentRunspace);
            using PSDataCollection<PSObject> output = new PSDataCollection<PSObject>();
            output.DataAdded += (_, eventArgs) =>
            {
                PSObject item = output[eventArgs.Index];
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
                else if (item?.BaseObject is InformationRecord information)
                {
                    host.WriteInformation(
                        information.MessageData,
                        new List<string>(information.Tags).ToArray());
                }
                else
                {
                    onOutput(item!);
                }
            };

            nested.AddScript(wrapper, useLocalScope: false)
                .AddArgument(scriptArgs)
                .AddArgument(termination)
                .AddArgument(terminationMarker);
            nested.Invoke<PSObject>(null, output, null);

            if (terminatingError is not null)
            {
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

    private static void RemoveCapturedErrorBookkeeping(PSCmdlet host, ErrorRecord record)
    {
        try
        {
            if (host.SessionState.PSVariable.GetValue("Error") is not ArrayList errorList || errorList.Count == 0)
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
            // ThrowTerminatingError will add the final outer record; de-dup is best effort only.
        }
    }
}
