using System.Management.Automation;
using System.IO;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Management.Automation.Language;
using System;

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

            //Token[] token = null;
            //ParseError[] errors = null;

            //Path = "C:\\gallery\\dbatools\\segments";
            //Path = "C:\\gallery\\dbatools\\single";
            //Console.WriteLine(Path);
            Parallel.ForEach(Directory.GetFiles(Path), file =>
            {
                //Console.WriteLine(file);
                //ast = (ScriptBlockAst)Parser.ParseFile(file, out token, out errors);
                //ScriptBlock sb = ScriptBlock.Create(File.ReadAllText(file));
                SessionState.InvokeCommand.InvokeScript(false, ScriptBlock.Create(File.ReadAllText(file)), null, null);
            });
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
