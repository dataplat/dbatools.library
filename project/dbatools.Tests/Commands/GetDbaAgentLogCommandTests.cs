using System;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class GetDbaAgentLogCommandTests
    {
        // Note: limited unit test coverage - command is primarily an SMO wrapper
        // that delegates to JobServer.ReadErrorLog(). Testable logic covers
        // the property decoration and default display helpers.

        #region GetServerPropertySafe
        [TestMethod]
        public void GetServerPropertySafe_ReturnsPropertyValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("ComputerName", "sql01"));

            string result = GetDbaAgentLogCommand.GetServerPropertySafe(obj, "ComputerName");

            Assert.AreEqual("sql01", result);
        }

        [TestMethod]
        public void GetServerPropertySafe_NullObjectReturnsNull()
        {
            string result = GetDbaAgentLogCommand.GetServerPropertySafe(null, "ComputerName");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetServerPropertySafe_MissingPropertyReturnsNull()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "sql01"));

            string result = GetDbaAgentLogCommand.GetServerPropertySafe(obj, "ComputerName");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetServerPropertySafe_NullValueReturnsNull()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("ServiceName", null));

            string result = GetDbaAgentLogCommand.GetServerPropertySafe(obj, "ServiceName");
            Assert.IsNull(result);
        }
        #endregion

        #region AddOrSetProperty
        [TestMethod]
        public void AddOrSetProperty_AddsNewProperty()
        {
            PSObject obj = new PSObject();
            GetDbaAgentLogCommand.AddOrSetProperty(obj, "ComputerName", "sql01");

            Assert.AreEqual("sql01", obj.Properties["ComputerName"].Value);
        }

        [TestMethod]
        public void AddOrSetProperty_UpdatesExistingProperty()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("ComputerName", "old-value"));

            GetDbaAgentLogCommand.AddOrSetProperty(obj, "ComputerName", "new-value");

            Assert.AreEqual("new-value", obj.Properties["ComputerName"].Value);
        }

        [TestMethod]
        public void AddOrSetProperty_NullObjectDoesNotThrow()
        {
            GetDbaAgentLogCommand.AddOrSetProperty(null, "Name", "value");
        }

        [TestMethod]
        public void AddOrSetProperty_SetsNullValue()
        {
            PSObject obj = new PSObject();
            GetDbaAgentLogCommand.AddOrSetProperty(obj, "InstanceName", null);

            Assert.IsNull(obj.Properties["InstanceName"].Value);
        }

        [TestMethod]
        public void AddOrSetProperty_MultiplePropertiesAdded()
        {
            PSObject obj = new PSObject();
            GetDbaAgentLogCommand.AddOrSetProperty(obj, "ComputerName", "sql01");
            GetDbaAgentLogCommand.AddOrSetProperty(obj, "InstanceName", "MSSQLSERVER");
            GetDbaAgentLogCommand.AddOrSetProperty(obj, "SqlInstance", "sql01");

            Assert.AreEqual("sql01", obj.Properties["ComputerName"].Value);
            Assert.AreEqual("MSSQLSERVER", obj.Properties["InstanceName"].Value);
            Assert.AreEqual("sql01", obj.Properties["SqlInstance"].Value);
        }
        #endregion

        #region SetDefaultDisplayPropertySet
        [TestMethod]
        public void SetDefaultDisplayPropertySet_AddsPSStandardMembers()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("LogDate", DateTime.Now));
            obj.Properties.Add(new PSNoteProperty("Text", "Test entry"));

            string[] props = new string[] { "LogDate", "Text" };
            GetDbaAgentLogCommand.SetDefaultDisplayPropertySet(obj, props);

            PSMemberInfo member = obj.Members["PSStandardMembers"];
            Assert.IsNotNull(member);
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_NullObjectDoesNotThrow()
        {
            GetDbaAgentLogCommand.SetDefaultDisplayPropertySet(null, new string[] { "Name" });
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_NullPropertiesDoesNotThrow()
        {
            PSObject obj = new PSObject();
            GetDbaAgentLogCommand.SetDefaultDisplayPropertySet(obj, null);
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_IdempotentReplacesPreviousSet()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "Test"));

            string[] props1 = new string[] { "Name" };
            string[] props2 = new string[] { "Name" };

            GetDbaAgentLogCommand.SetDefaultDisplayPropertySet(obj, props1);
            GetDbaAgentLogCommand.SetDefaultDisplayPropertySet(obj, props2);

            PSMemberInfo member = obj.Members["PSStandardMembers"];
            Assert.IsNotNull(member);
        }
        #endregion
    }
}
