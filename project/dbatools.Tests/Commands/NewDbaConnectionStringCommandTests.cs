using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class NewDbaConnectionStringCommandTests
    {
        #region TestAzure
        [TestMethod]
        public void TestAzure_AzureDomain_ReturnsTrue()
        {
            var instance = new DbaInstanceParameter("myserver.database.windows.net");
            Assert.IsTrue(NewDbaConnectionStringCommand.TestAzure(instance));
        }

        [TestMethod]
        public void TestAzure_RegularServer_ReturnsFalse()
        {
            var instance = new DbaInstanceParameter("sql01");
            Assert.IsFalse(NewDbaConnectionStringCommand.TestAzure(instance));
        }

        [TestMethod]
        public void TestAzure_NullInstance_ReturnsFalse()
        {
            Assert.IsFalse(NewDbaConnectionStringCommand.TestAzure(null));
        }

        [TestMethod]
        public void TestAzure_AzureDomainWithSubdomain_ReturnsTrue()
        {
            var instance = new DbaInstanceParameter("mydb.database.windows.net,1433");
            Assert.IsTrue(NewDbaConnectionStringCommand.TestAzure(instance));
        }

        [TestMethod]
        public void TestAzure_CaseInsensitive_ReturnsTrue()
        {
            var instance = new DbaInstanceParameter("myserver.DATABASE.WINDOWS.NET");
            Assert.IsTrue(NewDbaConnectionStringCommand.TestAzure(instance));
        }
        #endregion

        #region TransformUsername
        [TestMethod]
        public void TransformUsername_DomainBackslashUser_ReturnsUserAtDomain()
        {
            string result = NewDbaConnectionStringCommand.TransformUsername(@"DOMAIN\user");
            Assert.AreEqual("user@DOMAIN", result);
        }

        [TestMethod]
        public void TransformUsername_SimpleUser_ReturnsUnchanged()
        {
            string result = NewDbaConnectionStringCommand.TransformUsername("sqladmin");
            Assert.AreEqual("sqladmin", result);
        }

        [TestMethod]
        public void TransformUsername_UserAtDomain_ReturnsUnchanged()
        {
            string result = NewDbaConnectionStringCommand.TransformUsername("user@domain.com");
            Assert.AreEqual("user@domain.com", result);
        }

        [TestMethod]
        public void TransformUsername_LeadingBackslash_TrimsIt()
        {
            string result = NewDbaConnectionStringCommand.TransformUsername(@"\username");
            Assert.AreEqual("username", result);
        }

        [TestMethod]
        public void TransformUsername_NullInput_ReturnsNull()
        {
            string result = NewDbaConnectionStringCommand.TransformUsername(null);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void TransformUsername_EmptyInput_ReturnsEmpty()
        {
            string result = NewDbaConnectionStringCommand.TransformUsername("");
            Assert.AreEqual("", result);
        }
        #endregion
    }
}
