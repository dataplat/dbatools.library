using System;
using System.Collections.ObjectModel;
using System.Management.Automation;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Validates and resolves file system paths with enhanced error handling and provider verification.
    /// Ensures paths exist and are accessible before performing database operations like backups,
    /// restores, or log file management. Provides provider validation, single-item enforcement,
    /// and NewChild support for creating new files in existing parent directories.
    /// </summary>
    [OutputType(typeof(string))]
    [Cmdlet("Resolve", "DbaPath")]
    public class ResolveDbaPathCommand : DbaBaseCmdlet
    {
        /// <summary>
        /// Specifies the file system path to validate and resolve, supporting both absolute and relative paths.
        /// Accepts wildcards for pattern matching and can validate multiple paths when passed as an array.
        /// </summary>
        [Parameter(Mandatory = true, ValueFromPipeline = true)]
        public string[] Path { get; set; }

        /// <summary>
        /// Validates that the resolved path belongs to the specified PowerShell provider type.
        /// Use 'FileSystem' to ensure paths are on disk storage, not registry or other providers.
        /// </summary>
        [Parameter()]
        public string Provider { get; set; }

        /// <summary>
        /// Requires the path to resolve to exactly one location, preventing wildcard expansion.
        /// Will throw an error if wildcards or patterns resolve to multiple paths.
        /// </summary>
        [Parameter()]
        public SwitchParameter SingleItem { get; set; }

        /// <summary>
        /// Validates the parent directory exists for creating new files, without requiring the target file to exist.
        /// The parent folder must be accessible, but the final filename can be new.
        /// </summary>
        [Parameter()]
        public SwitchParameter NewChild { get; set; }

        /// <summary>
        /// Initializes processing. Forces EnableException to true to match the original PS1 behavior
        /// where all errors are always terminating.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            // The original PS1 hardcodes -EnableException $true on all Stop-Function calls,
            // meaning errors always terminate. Preserve this behavior unless the caller
            // explicitly passed -EnableException:$false.
            if (!MyInvocation.BoundParameters.ContainsKey("EnableException"))
            {
                EnableException = new SwitchParameter(true);
            }
        }

        /// <summary>
        /// Processes each input path, resolving it through the PowerShell provider system.
        /// </summary>
        protected override void ProcessRecord()
        {
            foreach (string inputPath in Path)
            {
                string workingPath = inputPath;

                // Handle "." specially - replace with current location
                if (IsDotPath(workingPath))
                {
                    workingPath = SessionState.Path.CurrentLocation.Path;
                }

                if (NewChild.IsPresent)
                {
                    ProcessNewChildPath(workingPath);
                }
                else
                {
                    ProcessExistingPath(workingPath);
                }

                if (TestFunctionInterrupt()) return;
            }
        }

        /// <summary>
        /// Processes a path in NewChild mode: resolves the parent directory and joins with the child name.
        /// Uses PowerShell Split-Path for PS provider compatibility.
        /// </summary>
        private void ProcessNewChildPath(string inputPath)
        {
            // Use PowerShell Split-Path for provider-aware path splitting
            string parent = InvokeSplitPath(inputPath, false);
            string child = InvokeSplitPath(inputPath, true);

            Collection<PathInfo> parentPaths;

            try
            {
                if (String.IsNullOrEmpty(parent))
                {
                    // No parent component - use current location
                    PathInfo currentLocation = SessionState.Path.CurrentLocation;
                    parentPaths = new Collection<PathInfo>();
                    parentPaths.Add(currentLocation);
                }
                else
                {
                    parentPaths = SessionState.Path.GetResolvedPSPathFromPSPath(parent);
                }
            }
            catch (Exception ex)
            {
                StopFunction(
                    String.Format("Failed to resolve path: {0}", inputPath),
                    exception: ex,
                    target: inputPath,
                    category: ErrorCategory.ObjectNotFound);
                TestFunctionInterrupt();
                return;
            }

            if (SingleItem.IsPresent && parentPaths.Count > 1)
            {
                StopFunction(
                    "Could not resolve to a single parent path.",
                    target: inputPath,
                    category: ErrorCategory.InvalidArgument);
                TestFunctionInterrupt();
                return;
            }

            if (!String.IsNullOrEmpty(Provider))
            {
                foreach (PathInfo pp in parentPaths)
                {
                    string resolvedProvider = pp.Provider.Name;
                    if (!IsProviderMatch(resolvedProvider, Provider))
                    {
                        StopFunction(
                            FormatProviderMismatchMessage(resolvedProvider, Provider),
                            target: inputPath,
                            category: ErrorCategory.InvalidArgument);
                        TestFunctionInterrupt();
                        return;
                    }
                }
            }

            foreach (PathInfo parentItem in parentPaths)
            {
                string combined = InvokeJoinPath(parentItem.ProviderPath, child);
                WriteObject(combined);
            }
        }

        /// <summary>
        /// Processes a path in normal (existing) mode: resolves the full path and validates constraints.
        /// </summary>
        private void ProcessExistingPath(string inputPath)
        {
            Collection<PathInfo> resolvedPaths;

            try
            {
                resolvedPaths = SessionState.Path.GetResolvedPSPathFromPSPath(inputPath);
            }
            catch (Exception ex)
            {
                StopFunction(
                    String.Format("Failed to resolve path: {0}", inputPath),
                    exception: ex,
                    target: inputPath,
                    category: ErrorCategory.ObjectNotFound);
                TestFunctionInterrupt();
                return;
            }

            if (SingleItem.IsPresent && resolvedPaths.Count > 1)
            {
                StopFunction(
                    "Could not resolve to a single parent path.",
                    target: inputPath,
                    category: ErrorCategory.InvalidArgument);
                TestFunctionInterrupt();
                return;
            }

            if (!String.IsNullOrEmpty(Provider))
            {
                foreach (PathInfo rp in resolvedPaths)
                {
                    string resolvedProvider = rp.Provider.Name;
                    if (!IsProviderMatch(resolvedProvider, Provider))
                    {
                        StopFunction(
                            FormatProviderMismatchMessage(resolvedProvider, Provider),
                            target: inputPath,
                            category: ErrorCategory.InvalidArgument);
                        TestFunctionInterrupt();
                        return;
                    }
                }
            }

            foreach (PathInfo resolvedPath in resolvedPaths)
            {
                WriteObject(resolvedPath.ProviderPath);
            }
        }

        #region Helpers

        /// <summary>
        /// Returns true if the path is a dot reference that should be replaced with the current location.
        /// </summary>
        internal static bool IsDotPath(string path)
        {
            return path == ".";
        }

        /// <summary>
        /// Checks whether a resolved provider name matches the expected provider.
        /// Comparison is case-insensitive to match PowerShell's -ne behavior.
        /// </summary>
        internal static bool IsProviderMatch(string resolvedProviderName, string expectedProvider)
        {
            return String.Equals(resolvedProviderName, expectedProvider, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Builds the provider mismatch error message.
        /// </summary>
        internal static string FormatProviderMismatchMessage(string resolvedProviderName, string expectedProvider)
        {
            return String.Format("Resolved provider is {0} when it should be {1}", resolvedProviderName, expectedProvider);
        }

        /// <summary>
        /// Invokes PowerShell Split-Path to split a path in a provider-aware manner.
        /// </summary>
        /// <param name="path">The path to split</param>
        /// <param name="leaf">If true, returns the leaf (child) component; otherwise returns the parent</param>
        /// <returns>The parent or leaf portion of the path</returns>
        private string InvokeSplitPath(string path, bool leaf)
        {
            string script = leaf
                ? "param($p) Split-Path -Path $p -Leaf"
                : "param($p) Split-Path -Path $p";

            var results = InvokeCommand.InvokeScript(
                false,
                ScriptBlock.Create(script),
                null,
                path);

            if (results != null && results.Count > 0 && results[0] != null)
            {
                return results[0].BaseObject as string ?? results[0].ToString();
            }
            return String.Empty;
        }

        /// <summary>
        /// Invokes PowerShell Join-Path to combine path components in a provider-aware manner.
        /// </summary>
        private string InvokeJoinPath(string parent, string child)
        {
            string script = "param($p, $c) Join-Path $p $c";
            var results = InvokeCommand.InvokeScript(
                false,
                ScriptBlock.Create(script),
                null,
                parent,
                child);

            if (results != null && results.Count > 0 && results[0] != null)
            {
                return results[0].BaseObject as string ?? results[0].ToString();
            }
            return String.Empty;
        }

        #endregion
    }
}
