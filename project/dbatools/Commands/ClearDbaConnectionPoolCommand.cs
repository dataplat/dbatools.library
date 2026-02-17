using System;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Clears all SQL Server connection pools on the specified computer to resolve connection issues.
    /// Connection pools can retain stale or problematic connections that cause intermittent connectivity
    /// issues, authentication failures, or performance problems. This command forces all pooled
    /// connections to be discarded and recreated on the next connection attempt.
    /// </summary>
    [Cmdlet("Clear", "DbaConnectionPool")]
    public class ClearDbaConnectionPoolCommand : DbaBaseCmdlet
    {
        /// <summary>
        /// Specifies the computer(s) where SQL Server connection pools should be cleared.
        /// Defaults to the local computer if not specified.
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        [Alias("cn", "host", "Server")]
        public DbaInstanceParameter[] ComputerName { get; set; }

        /// <summary>
        /// Alternate credential object to use for accessing the target computer(s).
        /// </summary>
        [Parameter()]
        public PSCredential Credential { get; set; }

        /// <summary>
        /// Script for clearing connection pools on a remote computer with credential.
        /// </summary>
        private static readonly string RemoteWithCredScript = @"
param($computer, $credential)
Invoke-Command2 -ComputerName $computer -Credential $credential -ScriptBlock { [Microsoft.Data.SqlClient.SqlConnection]::ClearAllPools() }
";

        /// <summary>
        /// Script for clearing connection pools on a remote computer without credential.
        /// </summary>
        private static readonly string RemoteNoCredScript = @"
param($computer)
Invoke-Command2 -ComputerName $computer -ScriptBlock { [Microsoft.Data.SqlClient.SqlConnection]::ClearAllPools() }
";

        /// <summary>
        /// Script for clearing connection pools on the local computer with credential.
        /// </summary>
        private static readonly string LocalWithCredScript = @"
param($credential)
Invoke-Command2 -Credential $credential -ScriptBlock { [Microsoft.Data.SqlClient.SqlConnection]::ClearAllPools() }
";

        /// <summary>
        /// Pre-compiled ScriptBlock for remote execution with credential.
        /// </summary>
        private ScriptBlock _remoteWithCredScriptBlock;

        /// <summary>
        /// Pre-compiled ScriptBlock for remote execution without credential.
        /// </summary>
        private ScriptBlock _remoteNoCredScriptBlock;

        /// <summary>
        /// Pre-compiled ScriptBlock for local execution with credential.
        /// </summary>
        private ScriptBlock _localWithCredScriptBlock;

        /// <summary>
        /// Pre-compiles script blocks for reuse across pipeline items.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            _remoteWithCredScriptBlock = ScriptBlock.Create(RemoteWithCredScript);
            _remoteNoCredScriptBlock = ScriptBlock.Create(RemoteNoCredScript);
            _localWithCredScriptBlock = ScriptBlock.Create(LocalWithCredScript);
        }

        /// <summary>
        /// Clears connection pools for each specified computer.
        /// </summary>
        protected override void ProcessRecord()
        {
            DbaInstanceParameter[] targets = (ComputerName != null && ComputerName.Length > 0)
                ? ComputerName
                : new DbaInstanceParameter[] { new DbaInstanceParameter(Environment.MachineName) };

            foreach (var computer in targets)
            {
                try
                {
                    if (!computer.IsLocalHost)
                    {
                        WriteMessageVerbose(String.Format("Clearing all pools on remote computer {0}", computer));

                        if (TestBound("Credential"))
                        {
                            InvokeCommand.InvokeScript(
                                false,
                                _remoteWithCredScriptBlock,
                                null,
                                computer,
                                Credential);
                        }
                        else
                        {
                            InvokeCommand.InvokeScript(
                                false,
                                _remoteNoCredScriptBlock,
                                null,
                                computer);
                        }
                    }
                    else
                    {
                        WriteMessageVerbose("Clearing all local pools");

                        if (TestBound("Credential"))
                        {
                            InvokeCommand.InvokeScript(
                                false,
                                _localWithCredScriptBlock,
                                null,
                                Credential);
                        }
                        else
                        {
                            Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
                        }
                    }
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("Failed to clear connection pools on {0}", computer),
                        exception: ex,
                        target: computer,
                        isContinue: true);
                    TestFunctionInterrupt();
                    continue;
                }
            }
        }
    }
}
