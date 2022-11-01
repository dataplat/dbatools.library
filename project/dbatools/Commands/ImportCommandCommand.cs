using System.Management.Automation;
using System.IO;

namespace Sqlcollaborative.Dbatools.Commands
{
    /// <summary>
    /// Implements the <c>Import-Command</c> internal command
    /// </summary>
    [Cmdlet("Import", "Command", DefaultParameterSetName = "DefaultParameter", RemotingCapability = RemotingCapability.None)]
    public class ImportCommand : PSCmdlet
    {
        #region Parameters
        /// <summary>
        /// The actual input object that is being processed
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        public string Path;
        #endregion Parameters

        #region Command Implementation
        /// <summary>
        /// Implements the begin action of the command
        /// </summary>
        protected override void BeginProcessing()
        {

        }

        /// <summary>
        /// Implements the process action of the command
        /// </summary>
        protected override void ProcessRecord()
        {
            /*
             *  SessionState.InvokeCommand.InvokeScript(thecode.Resource.dbatools,
                SessionState.InvokeCommand.InvokeScript(File.ReadAllText(Path),
            */
            SessionState.InvokeCommand.InvokeScript(File.ReadAllText(Path),
            false, System.Management.Automation.Runspaces.PipelineResultTypes.None, null, null);
        }

        /// <summary>
        /// Implements the end action of the command
        /// </summary>
        protected override void EndProcessing()
        {
        }
        #endregion Command Implementation
    }
}
