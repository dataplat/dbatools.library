using System;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class GetDbaAgentServerCommandTests
    {
        #region AddScriptProperty
        [TestMethod]
        public void AddScriptProperty_AddsPropertyToObject()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("MaximumHistoryRows", 1000));

            GetDbaAgentServerCommand.AddScriptProperty(obj, "TestProp", "$true");

            PSPropertyInfo prop = obj.Properties["TestProp"];
            Assert.IsNotNull(prop);
            Assert.IsTrue(prop is PSScriptProperty);
        }

        [TestMethod]
        public void AddScriptProperty_NullObjectDoesNotThrow()
        {
            GetDbaAgentServerCommand.AddScriptProperty(null, "TestProp", "$true");
        }

        [TestMethod]
        public void AddScriptProperty_ReplacesExistingProperty()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("TestProp", "oldvalue"));

            GetDbaAgentServerCommand.AddScriptProperty(obj, "TestProp", "$true");

            PSPropertyInfo prop = obj.Properties["TestProp"];
            Assert.IsNotNull(prop);
            Assert.IsTrue(prop is PSScriptProperty);
        }
        #endregion

        #region AddOrSetProperty
        [TestMethod]
        public void AddOrSetProperty_AddsNewProperty()
        {
            PSObject obj = new PSObject();
            GetDbaAgentServerCommand.AddOrSetProperty(obj, "ComputerName", "sql01");

            Assert.AreEqual("sql01", obj.Properties["ComputerName"].Value);
        }

        [TestMethod]
        public void AddOrSetProperty_UpdatesExistingProperty()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("ComputerName", "old"));

            GetDbaAgentServerCommand.AddOrSetProperty(obj, "ComputerName", "new");

            Assert.AreEqual("new", obj.Properties["ComputerName"].Value);
        }

        [TestMethod]
        public void AddOrSetProperty_NullObjectDoesNotThrow()
        {
            GetDbaAgentServerCommand.AddOrSetProperty(null, "Name", "value");
        }

        [TestMethod]
        public void AddOrSetProperty_SetsNullValue()
        {
            PSObject obj = new PSObject();
            GetDbaAgentServerCommand.AddOrSetProperty(obj, "Prop", null);

            Assert.IsNull(obj.Properties["Prop"].Value);
        }
        #endregion

        #region SetDefaultDisplayPropertySet
        [TestMethod]
        public void SetDefaultDisplayPropertySet_AddsStandardMembers()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "Test"));
            obj.Properties.Add(new PSNoteProperty("ID", 1));

            string[] props = new string[] { "Name", "ID" };
            GetDbaAgentServerCommand.SetDefaultDisplayPropertySet(obj, props);

            PSMemberInfo member = obj.Members["PSStandardMembers"];
            Assert.IsNotNull(member);
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_NullObjectDoesNotThrow()
        {
            GetDbaAgentServerCommand.SetDefaultDisplayPropertySet(null, new string[] { "Name" });
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_NullPropertiesDoesNotThrow()
        {
            PSObject obj = new PSObject();
            GetDbaAgentServerCommand.SetDefaultDisplayPropertySet(obj, null);
        }
        #endregion

        #region GetServerPropertySafe
        [TestMethod]
        public void GetServerPropertySafe_ReturnsPropertyValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("ComputerName", "sql01"));

            string result = GetDbaAgentServerCommand.GetServerPropertySafe(obj, "ComputerName");

            Assert.AreEqual("sql01", result);
        }

        [TestMethod]
        public void GetServerPropertySafe_NullObjectReturnsNull()
        {
            string result = GetDbaAgentServerCommand.GetServerPropertySafe(null, "ComputerName");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetServerPropertySafe_MissingPropertyReturnsNull()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "sql01"));

            string result = GetDbaAgentServerCommand.GetServerPropertySafe(obj, "ComputerName");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetServerPropertySafe_NullValueReturnsNull()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Edition", null));

            string result = GetDbaAgentServerCommand.GetServerPropertySafe(obj, "Edition");
            Assert.IsNull(result);
        }
        #endregion
    }
}
