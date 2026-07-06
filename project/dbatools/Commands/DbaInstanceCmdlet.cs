using System;
using System.Management.Automation;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Parameter;
using Microsoft.SqlServer.Management.Smo;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Base class for every ported cmdlet whose baseline carries SqlInstance. The properties
    /// are abstract and attribute-free ON PURPOSE: each concrete cmdlet declares mandatory-ness,
    /// position, pipeline attributes and aliases itself, because the baseline JSON is law and
    /// base-level attributes could never be tightened or loosened per command
    /// (migration/specs/architecture.md section 3).
    /// </summary>
    public abstract class DbaInstanceCmdlet : DbaBaseCmdlet
    {
        /// <summary>The target SQL Server instance or instances.</summary>
        public abstract DbaInstanceParameter[] SqlInstance { get; set; }

        /// <summary>Login to the target instance using alternative credentials.</summary>
        public abstract PSCredential SqlCredential { get; set; }

        /// <summary>
        /// Wraps ConnectionService.GetServer with the canonical failure shape: on any connect
        /// failure (including MinimumVersion and AzureUnsupported rejections) it runs the
        /// Stop-Function -Continue equivalent and returns null; the call site continues its
        /// loop. Under -EnableException the StopFunction call throws out of the cmdlet.
        /// </summary>
        /// <param name="instance">The instance being connected</param>
        /// <param name="failureMessage">The verbatim failure message from the PS source</param>
        /// <param name="minimumVersion">The -MinimumVersion value the PS source passed, when any</param>
        /// <param name="azureUnsupported">Whether the PS source passed -AzureUnsupported</param>
        /// <returns>The connected server, or null on failure</returns>
        protected Server ConnectInstance(DbaInstanceParameter instance,
            string failureMessage = "Failure",
            int minimumVersion = 0,
            bool azureUnsupported = false)
        {
            try
            {
                SmoConnectionRequest request = new SmoConnectionRequest();
                request.Instance = instance;
                request.SqlCredential = SqlCredential;
                request.MinimumVersion = minimumVersion;
                request.AzureUnsupported = azureUnsupported;

                Server server = ConnectionService.GetServer(request);
                SetActiveConnection(server.ConnectionContext);
                return server;
            }
            catch (Exception ex)
            {
                ErrorRecord record = new ErrorRecord(ex, String.Format("dbatools_{0}", MyInvocation.MyCommand.Name), ErrorCategory.ConnectionError, instance);
                StopFunction(failureMessage, target: instance, errorRecord: record, category: ErrorCategory.ConnectionError, continueLoop: true);
                return null;
            }
        }
    }
}
