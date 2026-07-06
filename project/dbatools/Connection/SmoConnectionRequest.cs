using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Connection
{
    /// <summary>
    /// The input to ConnectionService.GetServer: one property per Connect-DbaInstance concept
    /// the service currently resolves. Grows toward the full parameter surface as the
    /// migration proceeds (P0-010b); unset properties fall back to ConfigurationHost defaults
    /// exactly like the PS parameter defaults do.
    /// </summary>
    public class SmoConnectionRequest
    {
        /// <summary>The target instance in any of the DbaInstanceParameter input shapes.</summary>
        public DbaInstanceParameter Instance;

        /// <summary>Credential for SQL login or Windows credential authentication.</summary>
        public PSCredential SqlCredential;

        /// <summary>Initial catalog override; empty means the config/default database.</summary>
        public string Database;

        /// <summary>Reject instances below this major version with the verbatim PS message.</summary>
        public int MinimumVersion;

        /// <summary>Reject Azure SQL Database targets with the verbatim PS message.</summary>
        public bool AzureUnsupported;

        /// <summary>ReadOnly/ReadWrite application intent; part of the cache key.</summary>
        public string ApplicationIntent;

        /// <summary>Bypass and do not populate the SMO server cache for this request.</summary>
        public bool NonPooledConnection;
    }
}
