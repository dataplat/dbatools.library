using System;
using System.Management.Automation;
using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;
using Dataplat.Dbatools.Connection;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class GetDbaCmConnectionCommandTests
    {
        #region MatchesFilter
        [TestMethod]
        public void MatchesFilter_ExactComputerName_ReturnsTrue()
        {
            var conn = new ManagementConnection("server01");
            var computerPattern = new WildcardPattern("server01", WildcardOptions.IgnoreCase);
            var userPattern = new WildcardPattern("*", WildcardOptions.IgnoreCase);

            Assert.IsTrue(GetDbaCmConnectionCommand.MatchesFilter(conn, computerPattern, userPattern));
        }

        [TestMethod]
        public void MatchesFilter_WildcardComputerName_ReturnsTrue()
        {
            var conn = new ManagementConnection("sql-prod-01");
            var computerPattern = new WildcardPattern("sql*", WildcardOptions.IgnoreCase);
            var userPattern = new WildcardPattern("*", WildcardOptions.IgnoreCase);

            Assert.IsTrue(GetDbaCmConnectionCommand.MatchesFilter(conn, computerPattern, userPattern));
        }

        [TestMethod]
        public void MatchesFilter_NonMatchingComputerName_ReturnsFalse()
        {
            var conn = new ManagementConnection("webserver01");
            var computerPattern = new WildcardPattern("sql*", WildcardOptions.IgnoreCase);
            var userPattern = new WildcardPattern("*", WildcardOptions.IgnoreCase);

            Assert.IsFalse(GetDbaCmConnectionCommand.MatchesFilter(conn, computerPattern, userPattern));
        }

        [TestMethod]
        public void MatchesFilter_CaseInsensitiveComputerName_ReturnsTrue()
        {
            var conn = new ManagementConnection("SERVER01");
            var computerPattern = new WildcardPattern("server01", WildcardOptions.IgnoreCase);
            var userPattern = new WildcardPattern("*", WildcardOptions.IgnoreCase);

            // ManagementConnection constructor lowercases the name
            Assert.IsTrue(GetDbaCmConnectionCommand.MatchesFilter(conn, computerPattern, userPattern));
        }

        [TestMethod]
        public void MatchesFilter_NullConnection_ReturnsFalse()
        {
            var computerPattern = new WildcardPattern("*", WildcardOptions.IgnoreCase);
            var userPattern = new WildcardPattern("*", WildcardOptions.IgnoreCase);

            Assert.IsFalse(GetDbaCmConnectionCommand.MatchesFilter(null, computerPattern, userPattern));
        }

        [TestMethod]
        public void MatchesFilter_NoCredentials_WildcardUser_ReturnsTrue()
        {
            var conn = new ManagementConnection("server01");
            // No credentials set - Credentials is null
            var computerPattern = new WildcardPattern("*", WildcardOptions.IgnoreCase);
            var userPattern = new WildcardPattern("*", WildcardOptions.IgnoreCase);

            Assert.IsTrue(GetDbaCmConnectionCommand.MatchesFilter(conn, computerPattern, userPattern));
        }

        [TestMethod]
        public void MatchesFilter_NoCredentials_SpecificUser_ReturnsFalse()
        {
            var conn = new ManagementConnection("server01");
            // No credentials set
            var computerPattern = new WildcardPattern("*", WildcardOptions.IgnoreCase);
            var userPattern = new WildcardPattern("admin", WildcardOptions.IgnoreCase);

            Assert.IsFalse(GetDbaCmConnectionCommand.MatchesFilter(conn, computerPattern, userPattern));
        }

        [TestMethod]
        public void MatchesFilter_WithCredentials_MatchingUser_ReturnsTrue()
        {
            var conn = new ManagementConnection("server01");
            conn.Credentials = new PSCredential("DOMAIN\\admin", ConvertToSecureString("password"));
            var computerPattern = new WildcardPattern("*", WildcardOptions.IgnoreCase);
            var userPattern = new WildcardPattern("*admin*", WildcardOptions.IgnoreCase);

            Assert.IsTrue(GetDbaCmConnectionCommand.MatchesFilter(conn, computerPattern, userPattern));
        }

        [TestMethod]
        public void MatchesFilter_WithCredentials_NonMatchingUser_ReturnsFalse()
        {
            var conn = new ManagementConnection("server01");
            conn.Credentials = new PSCredential("DOMAIN\\admin", ConvertToSecureString("password"));
            var computerPattern = new WildcardPattern("*", WildcardOptions.IgnoreCase);
            var userPattern = new WildcardPattern("*charles*", WildcardOptions.IgnoreCase);

            Assert.IsFalse(GetDbaCmConnectionCommand.MatchesFilter(conn, computerPattern, userPattern));
        }

        [TestMethod]
        public void MatchesFilter_StarPatterns_MatchesAll()
        {
            var conn = new ManagementConnection("anyserver");
            var computerPattern = new WildcardPattern("*", WildcardOptions.IgnoreCase);
            var userPattern = new WildcardPattern("*", WildcardOptions.IgnoreCase);

            Assert.IsTrue(GetDbaCmConnectionCommand.MatchesFilter(conn, computerPattern, userPattern));
        }

        [TestMethod]
        public void MatchesFilter_BothFiltersApplied_RequiresBothMatch()
        {
            var conn = new ManagementConnection("sqlprod01");
            conn.Credentials = new PSCredential("DOMAIN\\sqladmin", ConvertToSecureString("password"));

            // Computer matches, user doesn't
            var computerPattern = new WildcardPattern("sql*", WildcardOptions.IgnoreCase);
            var userPattern = new WildcardPattern("*webadmin*", WildcardOptions.IgnoreCase);
            Assert.IsFalse(GetDbaCmConnectionCommand.MatchesFilter(conn, computerPattern, userPattern));

            // User matches, computer doesn't
            var computerPattern2 = new WildcardPattern("web*", WildcardOptions.IgnoreCase);
            var userPattern2 = new WildcardPattern("*sqladmin*", WildcardOptions.IgnoreCase);
            Assert.IsFalse(GetDbaCmConnectionCommand.MatchesFilter(conn, computerPattern2, userPattern2));

            // Both match
            var computerPattern3 = new WildcardPattern("sql*", WildcardOptions.IgnoreCase);
            var userPattern3 = new WildcardPattern("*sqladmin*", WildcardOptions.IgnoreCase);
            Assert.IsTrue(GetDbaCmConnectionCommand.MatchesFilter(conn, computerPattern3, userPattern3));
        }
        #endregion

        #region Helpers
        private static System.Security.SecureString ConvertToSecureString(string plainText)
        {
            var secure = new System.Security.SecureString();
            foreach (char c in plainText)
                secure.AppendChar(c);
            secure.MakeReadOnly();
            return secure;
        }
        #endregion
    }
}
