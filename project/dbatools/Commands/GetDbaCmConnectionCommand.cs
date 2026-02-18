using System;
using System.Collections.Generic;
using System.Management.Automation;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Message;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Retrieves cached Windows Management and CIM connections used by dbatools commands.
    /// Shows which remote computer connections are currently cached by dbatools for
    /// Windows Management and CIM operations.
    /// </summary>
    [Cmdlet("Get", "DbaCmConnection")]
    public class GetDbaCmConnectionCommand : DbaBaseCmdlet
    {
        /// <summary>
        /// Filters cached connections by computer name or server name. Supports wildcards for pattern matching.
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        [Alias("Filter")]
        public string[] ComputerName { get; set; } = new string[] { "*" };

        /// <summary>
        /// Filters cached connections by the username in the stored credentials. Supports wildcards for pattern matching.
        /// </summary>
        [Parameter()]
        public string UserName { get; set; } = "*";

        /// <summary>
        /// Initializes the cmdlet, logging the start of the operation.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();
            WriteMessageAtLevel("Starting", MessageLevel.InternalComment, null);
            WriteMessageAtLevel(
                String.Format("Bound parameters: {0}", String.Join(", ", MyInvocation.BoundParameters.Keys)),
                MessageLevel.Verbose,
                null);
        }

        /// <summary>
        /// Processes each ComputerName value and outputs matching cached connections.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (ComputerName == null)
                return;

            var userPattern = new WildcardPattern(UserName, WildcardOptions.IgnoreCase);

            // Snapshot the connections dictionary to avoid InvalidOperationException
            // if another thread modifies it while we are enumerating.
            ManagementConnection[] snapshot;
            lock (ConnectionHost.Connections)
            {
                snapshot = new ManagementConnection[ConnectionHost.Connections.Count];
                ConnectionHost.Connections.Values.CopyTo(snapshot, 0);
            }

            foreach (string name in ComputerName)
            {
                WriteMessageAtLevel(
                    String.Format("Processing search. ComputerName: '{0}' | Username: '{1}'", name, UserName),
                    MessageLevel.VeryVerbose,
                    null);

                var computerPattern = new WildcardPattern(name, WildcardOptions.IgnoreCase);

                foreach (ManagementConnection connection in snapshot)
                {
                    if (!MatchesFilter(connection, computerPattern, userPattern))
                        continue;

                    WriteObject(connection);
                }
            }
        }

        /// <summary>
        /// Logs the end of the operation.
        /// </summary>
        protected override void EndProcessing()
        {
            WriteMessageAtLevel("Ending", MessageLevel.InternalComment, null);
            base.EndProcessing();
        }

        /// <summary>
        /// Tests whether a ManagementConnection matches the given computer name and user name wildcard patterns.
        /// </summary>
        /// <param name="connection">The connection to test</param>
        /// <param name="computerPattern">Wildcard pattern for computer name</param>
        /// <param name="userPattern">Wildcard pattern for credential user name</param>
        /// <returns>True if the connection matches both patterns</returns>
        internal static bool MatchesFilter(ManagementConnection connection, WildcardPattern computerPattern, WildcardPattern userPattern)
        {
            if (connection == null)
                return false;

            if (!computerPattern.IsMatch(connection.ComputerName ?? String.Empty))
                return false;

            string credentialUserName = null;
            if (connection.Credentials != null)
                credentialUserName = connection.Credentials.UserName;

            if (!userPattern.IsMatch(credentialUserName ?? String.Empty))
                return false;

            return true;
        }
    }
}
