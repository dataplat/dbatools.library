#nullable enable

using System;
using System.Collections.Generic;
using System.Management.Automation;

namespace Dataplat.Dbatools.Commands;

internal static partial class NestedCommand
{
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
}
