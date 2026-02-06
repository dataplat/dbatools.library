using Dataplat.Dbatools.TabExpansion;
using System;
using System.Management.Automation;

namespace Dataplat.Dbatools.Internal
{
    /// <summary>
    /// Static tab completion helpers mirroring PS1 functions like
    /// Register-DbaTeppScriptBlock, Register-DbaTeppInstanceCacheBuilder,
    /// New-DbaTeppCompletionResult.
    /// </summary>
    public static class TabCompletionHelpers
    {
        /// <summary>
        /// Registers a tab expansion scriptblock.
        /// Mirrors Register-DbaTeppScriptBlock.ps1.
        /// </summary>
        /// <param name="name">Script identifier (converted to lowercase invariant)</param>
        /// <param name="scriptBlock">The completion scriptblock</param>
        public static void RegisterScriptBlock(string name, ScriptBlock scriptBlock)
        {
            if (String.IsNullOrEmpty(name) || scriptBlock == null)
                return;

            string normalizedName = name.ToLowerInvariant();

            ScriptContainer container = new ScriptContainer();
            container.Name = normalizedName;
            container.ScriptBlock = scriptBlock;
            container.LastDuration = new TimeSpan(0, 0, -1);

            TabExpansionHost.Scripts[normalizedName] = container;
        }

        /// <summary>
        /// Registers a tab expansion argument completer for specific commands and parameters.
        /// Mirrors Register-DbaTeppArgumentCompleter.ps1.
        /// </summary>
        /// <param name="commands">Command names to register</param>
        /// <param name="parameters">Parameter names to complete</param>
        /// <param name="name">Optional completion script name (defaults to parameter name)</param>
        /// <param name="all">If true, apply to all commands with this parameter</param>
        public static void RegisterCompleter(string[] commands, string[] parameters, string name = null, bool all = false)
        {
            if (parameters == null || parameters.Length == 0)
                return;

            foreach (string parameter in parameters)
            {
                string scriptName = String.IsNullOrEmpty(name) ? parameter.ToLowerInvariant() : name.ToLowerInvariant();

                if (all)
                {
                    TabExpansionHost.AddTabCompletionSet("*", parameter, scriptName);
                }
                else if (commands != null)
                {
                    foreach (string command in commands)
                    {
                        TabExpansionHost.AddTabCompletionSet(command, parameter, scriptName);
                    }
                }
            }
        }

        /// <summary>
        /// Registers an instance cache builder scriptblock.
        /// Mirrors Register-DbaTeppInstanceCacheBuilder.ps1.
        /// </summary>
        /// <param name="scriptBlock">The cache builder scriptblock</param>
        /// <param name="slow">If true, added to slow (async) list; otherwise fast (sync) list</param>
        public static void RegisterCacheBuilder(ScriptBlock scriptBlock, bool slow = false)
        {
            if (scriptBlock == null)
                return;

            if (slow)
            {
                if (!TabExpansionHost.TeppGatherScriptsSlow.Contains(scriptBlock))
                    TabExpansionHost.TeppGatherScriptsSlow.Add(scriptBlock);
            }
            else
            {
                if (!TabExpansionHost.TeppGatherScriptsFast.Contains(scriptBlock))
                    TabExpansionHost.TeppGatherScriptsFast.Add(scriptBlock);
            }
        }

        /// <summary>
        /// Creates a new CompletionResult with optional quoting.
        /// Mirrors New-DbaTeppCompletionResult.ps1.
        /// </summary>
        /// <param name="completionText">Text to propose</param>
        /// <param name="toolTip">Optional tooltip text</param>
        /// <param name="listItemText">Optional list display text</param>
        /// <param name="resultType">Completion result type (default: ParameterValue)</param>
        /// <param name="noQuotes">If true, suppress automatic quoting</param>
        /// <returns>A CompletionResult object</returns>
        public static CompletionResult NewCompletionResult(
            string completionText,
            string toolTip = null,
            string listItemText = null,
            CompletionResultType resultType = CompletionResultType.ParameterValue,
            bool noQuotes = false)
        {
            if (String.IsNullOrEmpty(completionText))
                return null;

            string tip = String.IsNullOrEmpty(toolTip) ? completionText : toolTip;
            string listText = String.IsNullOrEmpty(listItemText) ? completionText : listItemText;
            string text = completionText;

            // Auto-quote if needed for ParameterValue type
            if (!noQuotes && resultType == CompletionResultType.ParameterValue)
            {
                if (text.Contains(" ") || text.Contains("'") || text.Contains("\"") ||
                    text.Contains("$") || text.Contains("(") || text.Contains(")") ||
                    text.Contains("{") || text.Contains("}") || text.Contains("|") ||
                    text.Contains("&") || text.Contains(";"))
                {
                    text = "'" + text.Replace("'", "''") + "'";
                }
            }

            return new CompletionResult(text, listText, resultType, tip);
        }
    }
}
