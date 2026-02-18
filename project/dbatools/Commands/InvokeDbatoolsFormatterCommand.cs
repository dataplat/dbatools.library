using Dataplat.Dbatools.Message;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Management.Automation;
using System.Text;
using System.Text.RegularExpressions;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Formats PowerShell function files to dbatools coding standards using PSScriptAnalyzer's
    /// Invoke-Formatter with OTBS (One True Brace Style) settings. Standardizes indentation,
    /// brace placement, and whitespace handling. Files are saved without BOM encoding and with
    /// proper line ending handling for cross-platform compatibility.
    /// </summary>
    [Cmdlet("Invoke", "DbatoolsFormatter")]
    public class InvokeDbatoolsFormatterCommand : DbaBaseCmdlet
    {
        /// <summary>
        /// Specifies the path to one or more PowerShell (.ps1) files that need to be formatted
        /// to dbatools coding standards. Accepts pipeline input from Get-ChildItem or other
        /// file listing commands for batch processing multiple files.
        /// </summary>
        [Parameter(Mandatory = true, ValueFromPipeline = true)]
        [Alias("FullName)")]
        public object[] Path { get; set; }

        /// <summary>
        /// The required version of PSScriptAnalyzer.
        /// </summary>
        internal const string ScriptAnalyzerCorrectVersion = "1.18.2";

        /// <summary>
        /// Regex to match the entire comment-based help block.
        /// </summary>
        private static readonly Regex CBHRex = new Regex(@"(?smi)\s+\<\#[^#]*\#\>", RegexOptions.Compiled);

        /// <summary>
        /// Regex to capture the leading spaces before the opening CBH tag.
        /// </summary>
        private static readonly Regex CBHStartRex = new Regex(@"(?<spaces>[ ]+)\<\#", RegexOptions.Compiled);

        /// <summary>
        /// Regex to match the closing CBH tag and its leading whitespace.
        /// </summary>
        private static readonly Regex CBHEndRex = new Regex(@"(?<spaces>[ ]*)\#\>", RegexOptions.Compiled);

        /// <summary>
        /// UTF-8 encoding without BOM, shared across all file writes.
        /// </summary>
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private bool _hasInvokeFormatter;
        private string _osEol;

        /// <summary>
        /// Validates that PSScriptAnalyzer with Invoke-Formatter is available at the correct version.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            // Determine OS-appropriate line ending
            _osEol = "\n";
#if NETFRAMEWORK
            _osEol = "\r\n";
#else
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows))
            {
                _osEol = "\r\n";
            }
#endif

            // Check for Invoke-Formatter availability
            Version formatterVersion = GetInvokeFormatterVersion();
            _hasInvokeFormatter = formatterVersion != null;

            if (!_hasInvokeFormatter)
            {
                StopFunction(
                    String.Format("You need PSScriptAnalyzer version {0} installed", ScriptAnalyzerCorrectVersion));
                WriteMessageAtLevel(
                    String.Format("     Install-Module -Name PSScriptAnalyzer -RequiredVersion '{0}'", ScriptAnalyzerCorrectVersion),
                    MessageLevel.Warning, null);
                return;
            }

            // Compare major.minor.build only, since PSGallery may install 4-part versions
            Version requiredVersion = new Version(ScriptAnalyzerCorrectVersion);
            if (formatterVersion.Major != requiredVersion.Major
                || formatterVersion.Minor != requiredVersion.Minor
                || formatterVersion.Build != requiredVersion.Build)
            {
                // Try to reload the correct version
                try
                {
                    InvokeCommand.InvokeScript(
                        false,
                        ScriptBlock.Create("Remove-Module PSScriptAnalyzer"),
                        null);

                    InvokeCommand.InvokeScript(
                        false,
                        ScriptBlock.Create(
                            "param($ver) Import-Module PSScriptAnalyzer -RequiredVersion $ver -ErrorAction Stop"),
                        null,
                        ScriptAnalyzerCorrectVersion);
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("Please install PSScriptAnalyzer {0}", ScriptAnalyzerCorrectVersion),
                        exception: ex);
                    WriteMessageAtLevel(
                        String.Format("     Install-Module -Name PSScriptAnalyzer -RequiredVersion '{0}'", ScriptAnalyzerCorrectVersion),
                        MessageLevel.Warning, null);
                    return;
                }
            }
        }

        /// <summary>
        /// Processes each input path, formatting the file content to dbatools standards.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (TestFunctionInterrupt()) return;

            foreach (object p in Path)
            {
                string pathStr = p is PSObject pso ? (pso.BaseObject as string ?? pso.ToString()) : (p as string ?? p.ToString());

                // Resolve the path
                string realPath;
                try
                {
                    realPath = ResolveSinglePath(pathStr);
                }
                catch (Exception)
                {
                    StopFunction(
                        String.Format("Cannot find or resolve {0}", pathStr),
                        target: pathStr,
                        isContinue: true);
                    TestFunctionInterrupt();
                    continue;
                }

                // Read file content
                string content = File.ReadAllText(realPath, Encoding.UTF8);

                // Use a per-file copy of _osEol so that detecting Unix endings in one file
                // does not affect subsequent files in this invocation (PS1 bug fix).
                string fileEol = _osEol;
                if (fileEol == "\r\n")
                {
                    // See #5830: check if the file actually contains \r
                    bool containsCR = content.IndexOf('\r') >= 0;
                    if (!containsCR)
                    {
                        // File uses Unix-style endings even on Windows
                        fileEol = "\n";
                    }
                }

                // Strip trailing empty lines
                content = StripTrailingWhitespace(content, fileEol);

                // Invoke PSScriptAnalyzer formatter
                try
                {
                    string formatted = InvokeFormatter(content);
                    if (formatted != null)
                    {
                        content = formatted;
                    }
                }
                catch (Exception)
                {
                    WriteMessageAtLevel(
                        String.Format("Unable to format {0}", pathStr),
                        MessageLevel.Warning, null);
                }

                // Fix CBH end indentation to match start indentation (see #4373)
                content = FixCbhIndentation(content);

                // Trim whitespace lines, replace tabs with spaces
                string[] lines = content.Split('\n');
                List<string> realContent = new List<string>(lines.Length);
                foreach (string line in lines)
                {
                    realContent.Add(line.Replace("\t", "    ").TrimEnd());
                }

                // Write file without BOM
                string joinedContent = String.Join(fileEol, realContent.ToArray());
                File.WriteAllText(realPath, joinedContent, Utf8NoBom);
            }
        }

        #region Helper Methods

        /// <summary>
        /// Gets the version of Invoke-Formatter if available.
        /// </summary>
        private Version GetInvokeFormatterVersion()
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false,
                    ScriptBlock.Create("(Get-Command Invoke-Formatter -ErrorAction SilentlyContinue).Version"),
                    null);

                if (results != null && results.Count > 0 && results[0] != null)
                {
                    object baseObj = results[0].BaseObject;
                    if (baseObj is Version v)
                    {
                        return v;
                    }
                    // Try parsing as string
                    string vStr = baseObj.ToString();
                    if (!String.IsNullOrEmpty(vStr))
                    {
                        Version parsed;
                        if (Version.TryParse(vStr, out parsed))
                        {
                            return parsed;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Invoke-Formatter not available
            }
            return null;
        }

        /// <summary>
        /// Resolves a path to a single filesystem path using PowerShell Resolve-Path.
        /// </summary>
        private string ResolveSinglePath(string path)
        {
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false,
                ScriptBlock.Create("param($p) (Resolve-Path -Path $p -ErrorAction Stop).Path"),
                null,
                path);

            if (results != null && results.Count > 0 && results[0] != null)
            {
                object baseObj = results[0].BaseObject;
                return baseObj as string ?? baseObj.ToString();
            }

            throw new ItemNotFoundException(String.Format("Cannot resolve path: {0}", path));
        }

        /// <summary>
        /// Invokes PSScriptAnalyzer's Invoke-Formatter with OTBS settings.
        /// </summary>
        private string InvokeFormatter(string scriptContent)
        {
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false,
                ScriptBlock.Create(
                    "param($def) Invoke-Formatter -ScriptDefinition $def -Settings CodeFormattingOTBS -ErrorAction Stop"),
                null,
                scriptContent);

            if (results != null && results.Count > 0 && results[0] != null)
            {
                object baseObj = results[0].BaseObject;
                return baseObj as string ?? baseObj.ToString();
            }
            return null;
        }

        /// <summary>
        /// Strips trailing whitespace and empty lines from content.
        /// Equivalent to PS: $content -replace "(?s)$OSEOL\s*$"
        /// </summary>
        internal static string StripTrailingWhitespace(string content, string eol)
        {
            string pattern = String.Format("(?s){0}\\s*$", Regex.Escape(eol));
            return Regex.Replace(content, pattern, String.Empty);
        }

        /// <summary>
        /// Fixes comment-based help block indentation so the closing #> matches
        /// the indentation of the opening block. See dbatools issue #4373.
        /// </summary>
        internal static string FixCbhIndentation(string content)
        {
            Match cbhMatch = CBHRex.Match(content);
            string cbh = cbhMatch.Value;
            if (String.IsNullOrEmpty(cbh))
            {
                return content;
            }

            // Get starting spaces
            Match startMatch = CBHStartRex.Match(cbh);
            Group startSpaces = startMatch.Groups["spaces"];
            if (startSpaces == null || !startMatch.Success)
            {
                return content;
            }

            // Replace the end indentation to match the start
            string newCBH = CBHEndRex.Replace(cbh, startSpaces.Value + "#>");
            if (!String.IsNullOrEmpty(newCBH))
            {
                content = content.Replace(cbh, newCBH);
            }

            return content;
        }

        #endregion Helper Methods
    }
}
