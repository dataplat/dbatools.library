using System.Management.Automation;
using System.IO;

namespace Sqlcollaborative.Dbatools.Commands
{
    /// <summary>
    /// Implements the <c>Import-LocalModule</c> internal command
    /// </summary>
    [Cmdlet("Import", "LocalModule", DefaultParameterSetName = "DefaultParameter", RemotingCapability = RemotingCapability.None)]
    public class ImportLocalModule : PSCmdlet
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

        }

        /// <summary>
        /// Implements the end action of the command
        /// </summary>
        protected override void EndProcessing()
        {
            //string txt = File.ReadAllText(Path);
            SessionState.InvokeCommand.InvokeScript(File.ReadAllText(Path),
                false,
                System.Management.Automation.Runspaces.PipelineResultTypes.None,
                null,
                null);
        }
        #endregion Command Implementation
    }
}
