using System;
using System.Management.Automation;
using System.Text.RegularExpressions;

namespace Dataplat.Dbatools.Connection
{
    /// <summary>
    /// How the running framework satisfies an Entra ID service-principal connection that was
    /// expressed as a tenant plus a client-id credential. The two strategies are not a
    /// preference: only one of them is available per target framework.
    /// </summary>
    public enum ServicePrincipalStrategy
    {
        /// <summary>
        /// Acquire a renewable service-principal access token and connect with the token.
        /// Available on .NET Framework only.
        /// </summary>
        AccessTokenAcquisition,

        /// <summary>
        /// Rewrite the target into an "Authentication=Active Directory Service Principal"
        /// connection string and let the driver authenticate. The .NET (Core) path, where
        /// token generation is unavailable.
        /// </summary>
        ConnectionStringRewrite
    }

    /// <summary>
    /// The plan for one service-principal connection: which strategy applies on this
    /// framework, the verbose message that announces it, and - for the token strategy - the
    /// script that acquires the token. Produced by
    /// <see cref="ConnectionService.PlanServicePrincipalConnection"/>.
    /// </summary>
    public sealed class ServicePrincipalPlan
    {
        /// <summary>The strategy this framework supports.</summary>
        public ServicePrincipalStrategy Strategy;

        /// <summary>The verbose message announcing the chosen strategy.</summary>
        public string VerboseMessage;

        /// <summary>
        /// The token-acquisition script, parameterized as ($Tenant, $Credential). Null for
        /// <see cref="ServicePrincipalStrategy.ConnectionStringRewrite"/>. It is returned
        /// rather than executed because acquiring the token requires the caller's runspace.
        /// </summary>
        public string TokenScript;
    }

    public static partial class ConnectionService
    {
        #region Entra ID authentication

        /// <summary>
        /// The Entra ID authentication flows accepted by -AuthenticationType. Declared here so
        /// the parameter's ValidateSet and the connection logic share one definition; they are
        /// const so the attribute can reference them directly.
        /// </summary>
        public static class EntraAuthentication
        {
            /// <summary>Integrated Windows/Entra authentication.</summary>
            public const string Integrated = "ActiveDirectoryIntegrated";

            /// <summary>Interactive sign-in, including multi-factor prompts.</summary>
            public const string Interactive = "ActiveDirectoryInteractive";

            /// <summary>Entra ID username and password.</summary>
            public const string Password = "ActiveDirectoryPassword";

            /// <summary>Application (service principal) client id and secret.</summary>
            public const string ServicePrincipal = "ActiveDirectoryServicePrincipal";

            /// <summary>The managed identity assigned to the host running the connection.</summary>
            public const string ManagedIdentity = "ActiveDirectoryManagedIdentity";

            /// <summary>Device code flow, for hosts without an interactive browser.</summary>
            public const string DeviceCodeFlow = "ActiveDirectoryDeviceCodeFlow";

            /// <summary>Every accepted flow, in the order the parameter declares them.</summary>
            public static string[] All
            {
                get
                {
                    return new string[]
                    {
                        Integrated,
                        Interactive,
                        Password,
                        ServicePrincipal,
                        ManagedIdentity,
                        DeviceCodeFlow
                    };
                }
            }
        }

        /// <summary>The verbose message announcing the token-acquisition strategy.</summary>
        public const string ServicePrincipalTokenMessage = "Tenant detected, getting access token";

        /// <summary>The verbose message announcing the connection-string rewrite strategy.</summary>
        public const string ServicePrincipalRewriteMessage = "Generating access tokens is not supported on Core. Will try connection string with Active Directory Service Principal instead. See https://github.com/dataplat/dbatools/pull/7610 for more information.";

        /// <summary>
        /// The token-acquisition script, parameterized as ($Tenant, $Credential). The caller
        /// runs it in its own session state, which is where the token provider lives.
        /// </summary>
        public const string ServicePrincipalTokenScript = "param($Tenant, $Credential) (New-DbaAzAccessToken -Type RenewableServicePrincipal -Subtype AzureSqlDb -Tenant $Tenant -Credential $Credential -ErrorAction Stop).GetAccessToken()";

        /// <summary>
        /// The strategy available on the framework this assembly was built for. .NET Framework
        /// can mint a service-principal token; .NET (Core) cannot and must express the same
        /// intent as a connection string instead.
        /// </summary>
        public static ServicePrincipalStrategy RuntimeServicePrincipalStrategy
        {
            get
            {
#if NETFRAMEWORK
                return ServicePrincipalStrategy.AccessTokenAcquisition;
#else
                return ServicePrincipalStrategy.ConnectionStringRewrite;
#endif
            }
        }

        private const string ClientIdPattern = "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$";

        /// <summary>
        /// Whether the flow cannot authenticate without a SqlCredential, and so must be
        /// rejected before any connection is attempted.
        /// </summary>
        /// <param name="authenticationType">The requested authentication flow</param>
        /// <returns>Whether a SqlCredential is required</returns>
        public static bool RequiresSqlCredential(string authenticationType)
        {
            return String.Equals(authenticationType, EntraAuthentication.Password, StringComparison.OrdinalIgnoreCase)
                || String.Equals(authenticationType, EntraAuthentication.ServicePrincipal, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Whether a user name is an application client id rather than an account name.
        /// </summary>
        /// <param name="userName">The credential user name</param>
        /// <returns>Whether the name is a GUID-shaped client id</returns>
        public static bool IsClientIdUserName(string userName)
        {
            return !String.IsNullOrEmpty(userName) && Regex.IsMatch(userName, ClientIdPattern);
        }

        /// <summary>
        /// Whether a tenant plus a client-id credential, with no access token supplied,
        /// describes a service-principal connection this service has to build itself.
        /// </summary>
        /// <param name="tenant">The Entra ID tenant</param>
        /// <param name="accessToken">The access token the caller supplied, if any</param>
        /// <param name="credential">The credential the caller supplied, if any</param>
        /// <returns>Whether the service-principal flow applies</returns>
        public static bool IsServicePrincipalTenantFlow(string tenant, object accessToken, PSCredential credential)
        {
            // An empty-string token counts as absent: the source tested truthiness, not null.
            return !String.IsNullOrEmpty(tenant)
                && !LanguagePrimitives.IsTrue(accessToken)
                && credential != null
                && IsClientIdUserName(credential.UserName);
        }

        /// <summary>
        /// Plans the service-principal connection for a tenant plus client-id credential, or
        /// returns null when that combination does not apply and the request should be
        /// resolved as supplied.
        /// </summary>
        /// <param name="tenant">The Entra ID tenant</param>
        /// <param name="accessToken">The access token the caller supplied, if any</param>
        /// <param name="credential">The credential the caller supplied, if any</param>
        /// <returns>The plan, or null when the flow does not apply</returns>
        public static ServicePrincipalPlan PlanServicePrincipalConnection(string tenant, object accessToken, PSCredential credential)
        {
            if (!IsServicePrincipalTenantFlow(tenant, accessToken, credential))
                return null;

            ServicePrincipalPlan plan = new ServicePrincipalPlan();
            plan.Strategy = RuntimeServicePrincipalStrategy;
            if (plan.Strategy == ServicePrincipalStrategy.AccessTokenAcquisition)
            {
                plan.VerboseMessage = ServicePrincipalTokenMessage;
                plan.TokenScript = ServicePrincipalTokenScript;
            }
            else
            {
                plan.VerboseMessage = ServicePrincipalRewriteMessage;
            }
            return plan;
        }

        /// <summary>
        /// Builds the "Authentication=Active Directory Service Principal" connection string
        /// that carries a client id and secret to an Azure SQL target. The Database segment is
        /// emitted only when a database was requested.
        /// </summary>
        /// <param name="azureServer">The Azure SQL server name</param>
        /// <param name="database">The database to connect to, or null/empty for none</param>
        /// <param name="userId">The application client id</param>
        /// <param name="password">The client secret, in plain text</param>
        /// <returns>The connection string</returns>
        public static string BuildServicePrincipalConnectionString(string azureServer, string database, string userId, string password)
        {
            if (!String.IsNullOrEmpty(database))
                return String.Format("Server={0}; Authentication=Active Directory Service Principal; Database={1}; User Id={2}; Password={3}", azureServer, database, userId, password);
            return String.Format("Server={0}; Authentication=Active Directory Service Principal; User Id={1}; Password={2}", azureServer, userId, password);
        }

        #endregion Entra ID authentication
    }
}
