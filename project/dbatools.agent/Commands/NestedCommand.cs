#nullable enable

using System;
using System.Collections.ObjectModel;
using System.Management.Automation;

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

    internal static Collection<PSObject> InvokeScoped(
        PSCmdlet host,
        string scriptText,
        params object?[] scriptArgs)
    {
        using (ShieldDefaultParameterValues(host))
        {
            ScriptBlock script = ScriptBlock.Create(
                "param($__nestedCommandArguments)\n& {\n" + scriptText +
                "\n} @__nestedCommandArguments");
            Collection<PSObject> raw = host.InvokeCommand.InvokeScript(
                false,
                script,
                null,
                new object?[] { scriptArgs });
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
}
