#nullable enable

using System;
using System.Collections.Generic;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Pure parsing helpers for GetDbaStartupParameterCommand.
/// Extracted so unit tests can cover the logic without requiring WMI infrastructure.
/// </summary>
internal static class StartupParameterParser
{
    /// <summary>
    /// Splits the raw startup parameter string on semicolons.
    /// </summary>
    internal static string[] Split(string raw) => raw.Split(';');

    /// <summary>
    /// Collects all parameters that start with the given prefix (ordinal, case-sensitive).
    /// </summary>
    internal static List<string> Collect(string[] startupParams, string prefix)
    {
        List<string> result = new();
        foreach (string p in startupParams)
        {
            if (p.StartsWith(prefix, StringComparison.Ordinal))
                result.Add(p);
        }
        return result;
    }

    /// <summary>
    /// Returns "None" when the list is empty, or an int[] of the numeric portion (after the 2-char prefix).
    /// Replicates: if ($flags.length -eq 0) { "None" } else { [int[]]$flags.substring(2) }
    /// </summary>
    internal static object FlagsOrNone(List<string> flags)
    {
        if (flags.Count == 0)
        {
            return "None";
        }
        int[] result = new int[flags.Count];
        for (int i = 0; i < flags.Count; i++)
        {
            result[i] = int.Parse(flags[i].Substring(2));
        }
        return result;
    }
}
