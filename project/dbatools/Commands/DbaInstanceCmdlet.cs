using Dataplat.Dbatools.Parameter;
using System.Management.Automation;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Base class for dbatools cmdlets that operate against SQL Server instances.
    /// Provides SqlInstance and SqlCredential parameters automatically.
    /// </summary>
    public abstract class DbaInstanceCmdlet : DbaBaseCmdlet
    {
        /// <summary>
        /// The SQL Server instance(s) to target. Accepts pipeline input.
        /// </summary>
        [Parameter(Mandatory = true, ValueFromPipeline = true)]
        public DbaInstanceParameter[] SqlInstance { get; set; }

        /// <summary>
        /// Credential to use for SQL Server authentication.
        /// </summary>
        [Parameter()]
        public PSCredential SqlCredential { get; set; }
    }
}
