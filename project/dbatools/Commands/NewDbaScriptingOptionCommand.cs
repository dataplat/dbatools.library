using System.Management.Automation;
using Microsoft.SqlServer.Management.Smo;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Creates a customizable SMO ScriptingOptions object for controlling T-SQL script generation.
    /// The returned object can be used with Export-DbaScript and other dbatools scripting commands
    /// to customize what gets included when scripting SQL Server objects.
    /// </summary>
    [OutputType(typeof(ScriptingOptions))]
    [Cmdlet("New", "DbaScriptingOption")]
    public class NewDbaScriptingOptionCommand : DbaBaseCmdlet
    {
        /// <summary>
        /// Creates and outputs a new ScriptingOptions object.
        /// </summary>
        protected override void ProcessRecord()
        {
            WriteObject(new ScriptingOptions());
        }
    }
}
