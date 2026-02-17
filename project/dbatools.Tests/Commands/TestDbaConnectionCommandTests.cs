using System;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class TestDbaConnectionCommandTests
    {
        #region TestPing
        [TestMethod]
        public void TestPing_LocalhostShouldBeReachable()
        {
            bool result = TestDbaConnectionCommand.TestPing("127.0.0.1");
            Assert.IsTrue(result, "Localhost (127.0.0.1) should be pingable");
        }

        [TestMethod]
        public void TestPing_InvalidHostShouldReturnFalse()
        {
            bool result = TestDbaConnectionCommand.TestPing("host.that.definitely.does.not.exist.invalid");
            Assert.IsFalse(result, "Invalid host should not be pingable");
        }

        [TestMethod]
        public void TestPing_EmptyStringShouldReturnFalse()
        {
            bool result = TestDbaConnectionCommand.TestPing("");
            Assert.IsFalse(result, "Empty string should return false");
        }

        [TestMethod]
        public void TestPing_NullShouldReturnFalse()
        {
            bool result = TestDbaConnectionCommand.TestPing(null);
            Assert.IsFalse(result, "Null should return false");
        }
        #endregion

        #region GetResolvedProperty
        [TestMethod]
        public void GetResolvedProperty_NullObjectReturnsNull()
        {
            object result = TestDbaConnectionCommand.GetResolvedProperty(null, "ComputerName");
            Assert.IsNull(result, "Null object should return null");
        }

        [TestMethod]
        public void GetResolvedProperty_ExistingPropertyReturnsValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("ComputerName", "sql01"));

            object result = TestDbaConnectionCommand.GetResolvedProperty(obj, "ComputerName");
            Assert.AreEqual("sql01", result);
        }

        [TestMethod]
        public void GetResolvedProperty_MissingPropertyReturnsNull()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("ComputerName", "sql01"));

            object result = TestDbaConnectionCommand.GetResolvedProperty(obj, "NonExistent");
            Assert.IsNull(result, "Missing property should return null");
        }
        #endregion

        #region GetPSObjectProperty
        [TestMethod]
        public void GetPSObjectProperty_NullObjectReturnsNull()
        {
            object result = TestDbaConnectionCommand.GetPSObjectProperty(null, "Windows");
            Assert.IsNull(result, "Null object should return null");
        }

        [TestMethod]
        public void GetPSObjectProperty_ExistingPropertyReturnsValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Windows", "10.0.19041.0"));
            obj.Properties.Add(new PSNoteProperty("Edition", "Core"));

            object result = TestDbaConnectionCommand.GetPSObjectProperty(obj, "Windows");
            Assert.AreEqual("10.0.19041.0", result);
        }

        [TestMethod]
        public void GetPSObjectProperty_MissingPropertyReturnsNull()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Windows", "10.0.19041.0"));

            object result = TestDbaConnectionCommand.GetPSObjectProperty(obj, "NonExistent");
            Assert.IsNull(result, "Missing property should return null");
        }

        [TestMethod]
        public void GetPSObjectProperty_BoolPropertyReturnsBool()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("DomainUser", true));

            object result = TestDbaConnectionCommand.GetPSObjectProperty(obj, "DomainUser");
            Assert.IsInstanceOfType(result, typeof(bool));
            Assert.AreEqual(true, result);
        }
        #endregion

        #region GetServerProperty
        [TestMethod]
        public void GetServerProperty_NullServerReturnsNull()
        {
            TestDbaConnectionCommand cmd = new TestDbaConnectionCommand();
            object result = cmd.GetServerProperty(null, "Version");
            Assert.IsNull(result, "Null server should return null");
        }

        [TestMethod]
        public void GetServerProperty_ExistingClrPropertyReturnsValue()
        {
            TestDbaConnectionCommand cmd = new TestDbaConnectionCommand();
            // Use a simple .NET object with a known property
            string testString = "hello";
            object result = cmd.GetServerProperty(testString, "Length");
            Assert.AreEqual(5, result, "Should read Length property from string");
        }

        [TestMethod]
        public void GetServerProperty_MissingPropertyReturnsNull()
        {
            TestDbaConnectionCommand cmd = new TestDbaConnectionCommand();
            string testString = "hello";
            object result = cmd.GetServerProperty(testString, "NonExistentProperty");
            Assert.IsNull(result, "Missing property should return null");
        }

        [TestMethod]
        public void GetServerProperty_PSObjectWrappedObjectIsUnwrapped()
        {
            TestDbaConnectionCommand cmd = new TestDbaConnectionCommand();
            PSObject wrapped = new PSObject("hello");
            object result = cmd.GetServerProperty(wrapped, "Length");
            Assert.AreEqual(5, result, "Should unwrap PSObject and read base object property");
        }
        #endregion

        #region GetTrueLogin
        [TestMethod]
        public void GetTrueLogin_NullServerReturnsNull()
        {
            TestDbaConnectionCommand cmd = new TestDbaConnectionCommand();
            string result = cmd.GetTrueLogin(null);
            Assert.IsNull(result, "Null server should return null");
        }

        [TestMethod]
        public void GetTrueLogin_ObjectWithoutConnectionContextReturnsNull()
        {
            TestDbaConnectionCommand cmd = new TestDbaConnectionCommand();
            // A plain string has no ConnectionContext property
            string result = cmd.GetTrueLogin("not a server");
            Assert.IsNull(result, "Object without ConnectionContext should return null");
        }
        #endregion
    }
}
