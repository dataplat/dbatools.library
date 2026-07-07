#nullable enable

using System.Collections;
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
/// </summary>
internal static class NestedCommand
{
    /// <summary>
    /// Buffered invocation — for PS call sites that assigned the output to a variable or
    /// discarded it ($x = Get-DbaBackupInformation ... / $null = ... | Test-DbaBackupInformation).
    /// </summary>
    internal static Collection<PSObject> Invoke(PSCmdlet host, string commandName, IDictionary parameters, object? pipelineInput = null)
    {
        if (pipelineInput is null)
        {
            ScriptBlock script = ScriptBlock.Create("param($__parameters) & " + commandName + " @__parameters");
            return host.InvokeCommand.InvokeScript(false, script, null, parameters);
        }

        ScriptBlock piped = ScriptBlock.Create("param($__parameters, $__input) $__input | & " + commandName + " @__parameters");
        return host.InvokeCommand.InvokeScript(false, piped, null, parameters, pipelineInput);
    }

    /// <summary>
    /// Streaming invocation over a steppable pipeline — for PS call sites that piped input to
    /// the command at top level, where output must reach the user's pipeline as it is
    /// produced (restore progress objects), not after the command completes.
    /// </summary>
    internal static void InvokeStreamed(PSCmdlet host, string commandName, IDictionary parameters, IEnumerable pipelineInput)
    {
        ScriptBlock script = ScriptBlock.Create("param($__parameters) & " + commandName + " @__parameters");
        SteppablePipeline pipeline = script.GetSteppablePipeline(CommandOrigin.Internal, new object[] { parameters });
        bool stopped = false;
        try
        {
            pipeline.Begin(true);
            foreach (object? item in pipelineInput)
            {
                foreach (object output in pipeline.Process(item))
                    host.WriteObject(output);
            }
            foreach (object output in pipeline.End())
                host.WriteObject(output);
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
