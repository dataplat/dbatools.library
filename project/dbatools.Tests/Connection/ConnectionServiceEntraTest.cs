using System;
using System.Management.Automation;
using System.Reflection;
using System.Security;
using Dataplat.Dbatools.Commands;
using Dataplat.Dbatools.Connection;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Connection.Test
{
    /// <summary>
    /// Coverage for the Entra ID half of the auth matrix now that it lives in the service
    /// layer: the accepted flows, which of them cannot proceed without a credential, the
    /// tenant-plus-client-id service-principal trigger, and the per-framework split between
    /// minting an access token and building an Active Directory Service Principal connection
    /// string. All of it is connection-string construction, so none of it needs a cloud - the
    /// built string is asserted by parsing it with the driver's own builder.
    /// </summary>
    [TestClass]
    public class ConnectionServiceEntraTest
    {
        private const string ClientId = "21f5633f-6776-4bab-b878-bbd5e3e5ed72";
        private const string Secret = "s3cr3t";

        private static PSCredential MakeCredential(string userName, string password)
        {
            SecureString secure = new SecureString();
            foreach (char c in password)
                secure.AppendChar(c);
            secure.MakeReadOnly();
            return new PSCredential(userName, secure);
        }

        [TestMethod]
        public void EntraAuthentication_ListsTheSixFlowsInParameterOrder()
        {
            string[] expected = new string[]
            {
                "ActiveDirectoryIntegrated",
                "ActiveDirectoryInteractive",
                "ActiveDirectoryPassword",
                "ActiveDirectoryServicePrincipal",
                "ActiveDirectoryManagedIdentity",
                "ActiveDirectoryDeviceCodeFlow"
            };
            CollectionAssert.AreEqual(expected, ConnectionService.EntraAuthentication.All);
        }

        [TestMethod]
        public void AuthenticationTypeValidateSet_IsGeneratedFromTheServiceDefinition()
        {
            // The point of the consolidation: the parameter surface and the connection logic
            // cannot drift because they read the same constants.
            PropertyInfo property = typeof(ConnectDbaInstanceCommand).GetProperty("AuthenticationType");
            Assert.IsNotNull(property, "Connect-DbaInstance no longer exposes AuthenticationType");
            ValidateSetAttribute validateSet = (ValidateSetAttribute)Attribute.GetCustomAttribute(property, typeof(ValidateSetAttribute));
            Assert.IsNotNull(validateSet, "AuthenticationType carries no ValidateSet");

            string[] declared = new string[validateSet.ValidValues.Count];
            validateSet.ValidValues.CopyTo(declared, 0);
            CollectionAssert.AreEqual(ConnectionService.EntraAuthentication.All, declared);
        }

        [TestMethod]
        public void RequiresSqlCredential_CoversOnlyThePasswordAndServicePrincipalFlows()
        {
            Assert.IsTrue(ConnectionService.RequiresSqlCredential(ConnectionService.EntraAuthentication.Password));
            Assert.IsTrue(ConnectionService.RequiresSqlCredential(ConnectionService.EntraAuthentication.ServicePrincipal));

            Assert.IsFalse(ConnectionService.RequiresSqlCredential(ConnectionService.EntraAuthentication.Integrated));
            Assert.IsFalse(ConnectionService.RequiresSqlCredential(ConnectionService.EntraAuthentication.Interactive));
            Assert.IsFalse(ConnectionService.RequiresSqlCredential(ConnectionService.EntraAuthentication.ManagedIdentity));
            Assert.IsFalse(ConnectionService.RequiresSqlCredential(ConnectionService.EntraAuthentication.DeviceCodeFlow));
            Assert.IsFalse(ConnectionService.RequiresSqlCredential(null));
            Assert.IsFalse(ConnectionService.RequiresSqlCredential(""));
        }

        [TestMethod]
        public void RequiresSqlCredential_IgnoresCasingLikeTheParameterBinder()
        {
            // ValidateSet passes the caller's casing straight through, so the check that
            // rejects a missing credential has to be case-insensitive or it silently lets an
            // uncredentialed connection attempt through.
            Assert.IsTrue(ConnectionService.RequiresSqlCredential("activedirectorypassword"));
            Assert.IsTrue(ConnectionService.RequiresSqlCredential("ACTIVEDIRECTORYSERVICEPRINCIPAL"));
        }

        [TestMethod]
        public void IsClientIdUserName_AcceptsOnlyGuidShapedNames()
        {
            Assert.IsTrue(ConnectionService.IsClientIdUserName(ClientId));
            Assert.IsTrue(ConnectionService.IsClientIdUserName(ClientId.ToUpperInvariant()));
            Assert.IsFalse(ConnectionService.IsClientIdUserName("appuser@contoso.com"));
            Assert.IsFalse(ConnectionService.IsClientIdUserName(ClientId.Substring(1)));
            Assert.IsFalse(ConnectionService.IsClientIdUserName("prefix-" + ClientId));
            Assert.IsFalse(ConnectionService.IsClientIdUserName(""));
            Assert.IsFalse(ConnectionService.IsClientIdUserName(null));
        }

        [TestMethod]
        public void IsServicePrincipalTenantFlow_NeedsTenantClientIdAndNoToken()
        {
            PSCredential clientCredential = MakeCredential(ClientId, Secret);

            Assert.IsTrue(ConnectionService.IsServicePrincipalTenantFlow("contoso.onmicrosoft.com", null, clientCredential));

            Assert.IsFalse(ConnectionService.IsServicePrincipalTenantFlow(null, null, clientCredential), "no tenant");
            Assert.IsFalse(ConnectionService.IsServicePrincipalTenantFlow("", null, clientCredential), "empty tenant");
            Assert.IsFalse(ConnectionService.IsServicePrincipalTenantFlow("contoso.onmicrosoft.com", null, null), "no credential");
            Assert.IsFalse(ConnectionService.IsServicePrincipalTenantFlow("contoso.onmicrosoft.com", null, MakeCredential("appuser@contoso.com", Secret)), "not a client id");
            Assert.IsFalse(ConnectionService.IsServicePrincipalTenantFlow("contoso.onmicrosoft.com", "already-a-token", clientCredential), "token supplied");
        }

        [TestMethod]
        public void IsServicePrincipalTenantFlow_TreatsAnEmptyTokenAsAbsent()
        {
            // The source tested truthiness, not null: an empty-string token must NOT suppress
            // the service-principal flow.
            PSCredential clientCredential = MakeCredential(ClientId, Secret);
            Assert.IsTrue(ConnectionService.IsServicePrincipalTenantFlow("contoso.onmicrosoft.com", "", clientCredential));
            Assert.IsTrue(ConnectionService.IsServicePrincipalTenantFlow("contoso.onmicrosoft.com", false, clientCredential));
        }

        [TestMethod]
        public void PlanServicePrincipalConnection_ReturnsNullWhenTheFlowDoesNotApply()
        {
            Assert.IsNull(ConnectionService.PlanServicePrincipalConnection(null, null, MakeCredential(ClientId, Secret)));
            Assert.IsNull(ConnectionService.PlanServicePrincipalConnection("contoso.onmicrosoft.com", null, null));
        }

        [TestMethod]
        public void PlanServicePrincipalConnection_TakesTheStrategyThisFrameworkSupports()
        {
            ServicePrincipalPlan plan = ConnectionService.PlanServicePrincipalConnection(
                "contoso.onmicrosoft.com", null, MakeCredential(ClientId, Secret));
            Assert.IsNotNull(plan);
            Assert.AreEqual(ConnectionService.RuntimeServicePrincipalStrategy, plan.Strategy);

#if NETFRAMEWORK
            // .NET Framework can mint a renewable service-principal token.
            Assert.AreEqual(ServicePrincipalStrategy.AccessTokenAcquisition, plan.Strategy);
            Assert.AreEqual(ConnectionService.ServicePrincipalTokenMessage, plan.VerboseMessage);
            Assert.IsNotNull(plan.TokenScript);
            StringAssert.Contains(plan.TokenScript, "RenewableServicePrincipal");
            StringAssert.Contains(plan.TokenScript, "AzureSqlDb");
            StringAssert.Contains(plan.TokenScript, "GetAccessToken()");
#else
            // .NET cannot, and expresses the same intent as a connection string instead.
            Assert.AreEqual(ServicePrincipalStrategy.ConnectionStringRewrite, plan.Strategy);
            Assert.AreEqual(ConnectionService.ServicePrincipalRewriteMessage, plan.VerboseMessage);
            Assert.IsNull(plan.TokenScript, "the token script is unusable on this framework and must not be offered");
#endif
        }

        [TestMethod]
        public void BuildServicePrincipalConnectionString_CarriesTheAuthTokensThroughTheDriverBuilder()
        {
            string built = ConnectionService.BuildServicePrincipalConnectionString(
                "myserver.database.windows.net", "AdventureWorks", ClientId, Secret);

            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(built);
            Assert.AreEqual(SqlAuthenticationMethod.ActiveDirectoryServicePrincipal, builder.Authentication);
            Assert.AreEqual("myserver.database.windows.net", builder.DataSource);
            Assert.AreEqual("AdventureWorks", builder.InitialCatalog);
            Assert.AreEqual(ClientId, builder.UserID);
            Assert.AreEqual(Secret, builder.Password);
        }

        [TestMethod]
        public void BuildServicePrincipalConnectionString_OmitsTheDatabaseWhenNoneWasRequested()
        {
            string built = ConnectionService.BuildServicePrincipalConnectionString(
                "myserver.database.windows.net", null, ClientId, Secret);
            Assert.IsFalse(built.Contains("Database=") || built.Contains("Initial Catalog="),
                "a database segment appeared for a request that named no database");

            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(built);
            Assert.AreEqual(SqlAuthenticationMethod.ActiveDirectoryServicePrincipal, builder.Authentication);
            Assert.AreEqual("", builder.InitialCatalog);
            Assert.AreEqual(ClientId, builder.UserID);

            string emptyDatabase = ConnectionService.BuildServicePrincipalConnectionString(
                "myserver.database.windows.net", "", ClientId, Secret);
            Assert.AreEqual(built, emptyDatabase, "an empty database must be treated as none, like the request default");
        }

        /// <summary>
        /// A client secret is arbitrary text: it can hold the very characters a connection
        /// string uses as syntax. A semicolon is the one that used to be fatal - it ended the
        /// Password segment early and the whole string stopped parsing, with an error naming
        /// only a character index. Every one of these secrets must arrive at the driver
        /// byte-identical to what the caller supplied.
        /// </summary>
        [TestMethod]
        public void BuildServicePrincipalConnectionString_CarriesASecretHoldingConnectionStringSyntax()
        {
            string[] awkwardSecrets = new string[]
            {
                "ab;cd",            // the keyword separator - unparseable before this was escaped
                "ab=cd",            // the key/value separator
                "ab'cd",            // single quote
                "ab\"cd",           // double quote
                "a;b='c\"d;",       // all of them at once, with a trailing separator
                "Ab8Q~xY.z-K_1",    // the shape Azure actually mints
            };

            foreach (string secret in awkwardSecrets)
            {
                string built = ConnectionService.BuildServicePrincipalConnectionString(
                    "myserver.database.windows.net", "AdventureWorks", ClientId, secret);

                SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(built);
                Assert.AreEqual(secret, builder.Password, "the secret did not survive the round trip: " + secret);
                Assert.AreEqual(ClientId, builder.UserID, "the user id was disturbed by the secret: " + secret);
                Assert.AreEqual("AdventureWorks", builder.InitialCatalog, "the database was disturbed by the secret: " + secret);
                Assert.AreEqual("myserver.database.windows.net", builder.DataSource, "the server was disturbed by the secret: " + secret);
                Assert.AreEqual(SqlAuthenticationMethod.ActiveDirectoryServicePrincipal, builder.Authentication,
                    "the authentication method was disturbed by the secret: " + secret);
            }
        }

        /// <summary>
        /// The same escaping has to hold for the other three values. They are hostname-, GUID-
        /// and identifier-shaped in this flow, so this is a guard against the concatenation
        /// coming back rather than a bug seen in the field.
        /// </summary>
        [TestMethod]
        public void BuildServicePrincipalConnectionString_CarriesAServerDatabaseAndUserIdHoldingSeparators()
        {
            string built = ConnectionService.BuildServicePrincipalConnectionString(
                "my;server", "Adventure;Works", "client;id", Secret);

            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(built);
            Assert.AreEqual("my;server", builder.DataSource);
            Assert.AreEqual("Adventure;Works", builder.InitialCatalog);
            Assert.AreEqual("client;id", builder.UserID);
            Assert.AreEqual(Secret, builder.Password);
        }
    }
}
