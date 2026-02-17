using System;
using System.Management.Automation;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Retrieves configured file paths used by dbatools functions for storing temporary files, logs, and output data.
    /// </summary>
    [Cmdlet("Get", "DbatoolsPath")]
    [OutputType(typeof(string))]
    public class GetDbatoolsPathCommand : DbaBaseCmdlet
    {
        /// <summary>
        /// Specifies the name of the configured path to retrieve. Common predefined paths include
        /// 'Temp', 'LocalAppData', 'AppData', and 'ProgramData'. Custom path names can be defined
        /// using Set-DbatoolsPath.
        /// </summary>
        [Parameter(Mandatory = true, Position = 0)]
        public string Name { get; set; }

        /// <summary>
        /// Looks up the configured path by constructing the config key "Path.Managed.{Name}"
        /// and returning the stored value.
        /// </summary>
        protected override void ProcessRecord()
        {
            string fullName = String.Format("Path.Managed.{0}", Name);
            object value = GetDbatoolsConfigValueCommand.LookupConfigValue(fullName, null);
            WriteObject(value);
        }
    }
}
