using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Net;

namespace Dataplat.Dbatools.Connection
{
    [TestClass]
    public class SqlClientCompatibilityTest
    {
        [TestMethod]
        public void SqlConnectionExposesPluggableSspiProvider()
        {
            Assert.IsNotNull(typeof(SqlConnection).GetProperty("SspiContextProvider"));
        }

        [TestMethod]
        public void ActiveDirectoryAuthenticationProviderIsRegistered()
        {
            Assert.IsNotNull(SqlAuthenticationProvider.GetProvider(SqlAuthenticationMethod.ActiveDirectoryIntegrated));
        }

#if NET8_0_OR_GREATER
        [TestMethod]
        public void NetworkCredentialSspiProviderCanBeAssignedToSqlConnection()
        {
            using (NetworkCredentialSspiContextProvider provider = new NetworkCredentialSspiContextProvider(
                new NetworkCredential("user", "password", "domain")))
            using (SqlConnection connection = new SqlConnection())
            {
                connection.SspiContextProvider = provider;

                Assert.AreSame(provider, connection.SspiContextProvider);
            }
        }

        [TestMethod]
        public void NetworkCredentialSspiProviderRejectsNullCredential()
        {
            Assert.ThrowsExactly<ArgumentNullException>(() => new NetworkCredentialSspiContextProvider(null));
        }

        [TestMethod]
        public void NetworkCredentialSspiProviderHasStablePoolIdentityPerCredential()
        {
            using (NetworkCredentialSspiContextProvider first = new NetworkCredentialSspiContextProvider(
                new NetworkCredential("user", "password", "domain")))
            using (NetworkCredentialSspiContextProvider second = new NetworkCredentialSspiContextProvider(
                new NetworkCredential("USER", "password", "DOMAIN")))
            using (NetworkCredentialSspiContextProvider different = new NetworkCredentialSspiContextProvider(
                new NetworkCredential("other-user", "password", "domain")))
            using (NetworkCredentialSspiContextProvider differentPassword = new NetworkCredentialSspiContextProvider(
                new NetworkCredential("user", "different-password", "domain")))
            {
                Assert.AreEqual(first, second);
                Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
                Assert.AreNotEqual(first, different);
                Assert.AreNotEqual(first, differentPassword);
            }
        }
#endif
    }
}
