using Dataplat.Dbatools.Parameter;
using System;
using System.IO;
using System.Management.Automation;
using System.Runtime.InteropServices;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Constructs file paths with correct separators for Windows and Linux SQL Server instances.
    /// Joins multiple path segments while automatically using the correct path separators
    /// (backslash for Windows, forward slash for Linux) based on the target SQL Server
    /// instance's operating system. Without specifying a SqlInstance, it defaults to the
    /// local machine's path separator conventions.
    /// </summary>
    [OutputType(typeof(string))]
    [Cmdlet("Join", "DbaPath")]
    public class JoinDbaPathCommand : DbaBaseCmdlet
    {
        /// <summary>
        /// Specifies the base directory path where backup files, scripts, or other SQL Server
        /// resources will be stored. This forms the root location for building complete file paths.
        /// </summary>
        [Parameter(Mandatory = true, Position = 0)]
        public string Path { get; set; }

        /// <summary>
        /// Optional SQL Server instance to determine the target operating system
        /// (Linux or Windows) for choosing the correct path separator.
        /// </summary>
        [Parameter()]
        public DbaInstanceParameter SqlInstance { get; set; }

        /// <summary>
        /// Credential to use for SQL Server authentication when detecting the remote OS.
        /// </summary>
        [Parameter()]
        public PSCredential SqlCredential { get; set; }

        /// <summary>
        /// Specifies additional path segments to append to the base path, such as
        /// subdirectories, filename prefixes, or date folders. Accepts multiple values
        /// to build nested directory structures.
        /// </summary>
        [Parameter(ValueFromRemainingArguments = true)]
        [Alias("ChildPath")]
        public string[] Child { get; set; }

        /// <summary>
        /// Processes the path joining logic.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (SqlInstance == null)
            {
                // No SqlInstance: use local OS separator
                WriteObject(JoinWithLocalSeparator(Path, Child));
                return;
            }

            // SqlInstance provided: detect remote OS
            bool isLinux = false;
            try
            {
                isLinux = TestHostOSLinux(SqlInstance, SqlCredential);
            }
            catch (Exception ex)
            {
                StopFunction(
                    String.Format("Failed to connect to {0} to detect OS type", SqlInstance),
                    exception: ex,
                    target: SqlInstance,
                    category: ErrorCategory.ConnectionError);
                return;
            }

            string resultingPath = Path;

            if (isLinux)
            {
                WriteMessageVerbose("Linux detected on remote server");
                resultingPath = resultingPath.Replace("\\", "/");

                if (Child != null && Child.Length > 0)
                {
                    string[] segments = new string[Child.Length + 1];
                    segments[0] = resultingPath;
                    Array.Copy(Child, 0, segments, 1, Child.Length);
                    resultingPath = String.Join("/", segments);
                }
            }
            else
            {
                // Windows target
#if NETFRAMEWORK
                // net472 always runs on Windows
                resultingPath = resultingPath.Replace("/", "\\");
#else
                // net8.0: check if the local machine is Windows or not
                // When running on Linux/macOS targeting a Windows SQL Server, the PS1
                // normalizes to forward slashes (matching the local host convention).
                // This matches the original PS1 behavior.
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    resultingPath = resultingPath.Replace("/", "\\");
                }
                else
                {
                    resultingPath = resultingPath.Replace("\\", "/");
                }
#endif

                if (Child != null && Child.Length > 0)
                {
                    foreach (string childItem in Child)
                    {
                        resultingPath = System.IO.Path.Combine(resultingPath, childItem);
                    }
                }
            }

            WriteObject(resultingPath);
        }

        #region Helpers

        /// <summary>
        /// Joins a base path with child segments using the local OS directory separator.
        /// Mirrors the PS1 behavior: @($path) + $Child -join [IO.Path]::DirectorySeparatorChar
        /// then -replace '\\|/', [IO.Path]::DirectorySeparatorChar
        /// Child segments containing separators are also normalized to the local separator.
        /// </summary>
        internal static string JoinWithLocalSeparator(string basePath, string[] children)
        {
            char sep = System.IO.Path.DirectorySeparatorChar;

            // Build the combined path: basePath + children joined by separator
            string combined = basePath;
            if (children != null && children.Length > 0)
            {
                combined = basePath + sep + String.Join(sep.ToString(), children);
            }

            // Normalize all separators to the local OS separator
            combined = combined.Replace('\\', sep).Replace('/', sep);

            return combined;
        }

        /// <summary>
        /// Tests if the SQL Server instance is running on Linux by executing SELECT @@VERSION
        /// and checking for "Linux" in the result. If the connection fails or @@VERSION
        /// does not contain "Linux", assumes Windows.
        /// </summary>
        private bool TestHostOSLinux(DbaInstanceParameter instance, PSCredential credential)
        {
            string script;
            object[] args;

            // SECURITY: instance and credential are passed as $args to prevent script injection.
            // Do NOT interpolate these values into the script string.
            if (credential != null)
            {
                script = "param($i, $c) $server = Connect-DbaInstance -SqlInstance $i -SqlCredential $c; $server.ConnectionContext.ExecuteScalar('SELECT @@VERSION') -match 'Linux'";
                args = new object[] { instance, credential };
            }
            else
            {
                script = "param($i) $server = Connect-DbaInstance -SqlInstance $i; $server.ConnectionContext.ExecuteScalar('SELECT @@VERSION') -match 'Linux'";
                args = new object[] { instance };
            }

            var results = InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, args);
            if (results != null && results.Count > 0)
            {
                if (results[0].BaseObject is bool boolResult)
                    return boolResult;
            }
            return false;
        }

        #endregion
    }
}