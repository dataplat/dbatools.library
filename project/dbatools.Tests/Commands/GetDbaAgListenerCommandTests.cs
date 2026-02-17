using System;
using System.Collections.Generic;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class GetDbaAgListenerCommandTests
    {
        #region BuildListenerFilter
        [TestMethod]
        public void BuildListenerFilter_NullReturnsNull()
        {
            HashSet<string> result = GetDbaAgListenerCommand.BuildListenerFilter(null);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void BuildListenerFilter_EmptyArrayReturnsNull()
        {
            HashSet<string> result = GetDbaAgListenerCommand.BuildListenerFilter(new string[0]);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void BuildListenerFilter_PopulatesFilter()
        {
            HashSet<string> result = GetDbaAgListenerCommand.BuildListenerFilter(new string[] { "Listener1", "Listener2" });

            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Count);
            Assert.IsTrue(result.Contains("Listener1"));
            Assert.IsTrue(result.Contains("Listener2"));
        }

        [TestMethod]
        public void BuildListenerFilter_CaseInsensitive()
        {
            HashSet<string> result = GetDbaAgListenerCommand.BuildListenerFilter(new string[] { "MyListener" });

            Assert.IsNotNull(result);
            Assert.IsTrue(result.Contains("mylistener"));
            Assert.IsTrue(result.Contains("MYLISTENER"));
            Assert.IsTrue(result.Contains("MyListener"));
        }

        [TestMethod]
        public void BuildListenerFilter_DeduplicatesDifferentCase()
        {
            HashSet<string> result = GetDbaAgListenerCommand.BuildListenerFilter(new string[] { "listener1", "LISTENER1" });

            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.Count);
        }
        #endregion

        #region GetPropertyString
        [TestMethod]
        public void GetPropertyString_ReturnsStringValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "AG01-Listener"));

            string result = GetDbaAgListenerCommand.GetPropertyString(obj, "Name");

            Assert.AreEqual("AG01-Listener", result);
        }

        [TestMethod]
        public void GetPropertyString_NullObjectReturnsNull()
        {
            string result = GetDbaAgListenerCommand.GetPropertyString(null, "Name");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyString_MissingPropertyReturnsNull()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("ID", 1));

            string result = GetDbaAgListenerCommand.GetPropertyString(obj, "Name");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyString_NullValueReturnsNull()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", null));

            string result = GetDbaAgListenerCommand.GetPropertyString(obj, "Name");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyString_NonStringConvertsToString()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("PortNumber", 1433));

            string result = GetDbaAgListenerCommand.GetPropertyString(obj, "PortNumber");

            Assert.AreEqual("1433", result);
        }
        #endregion

        #region GetPropertyObject
        [TestMethod]
        public void GetPropertyObject_ReturnsWrappedPSObject()
        {
            PSObject inner = new PSObject();
            inner.Properties.Add(new PSNoteProperty("Name", "AG01"));

            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Parent", inner));

            PSObject result = GetDbaAgListenerCommand.GetPropertyObject(obj, "Parent");

            Assert.IsNotNull(result);
            Assert.AreEqual("AG01", result.Properties["Name"].Value);
        }

        [TestMethod]
        public void GetPropertyObject_NullObjectReturnsNull()
        {
            PSObject result = GetDbaAgListenerCommand.GetPropertyObject(null, "Parent");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyObject_MissingPropertyReturnsNull()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "Test"));

            PSObject result = GetDbaAgListenerCommand.GetPropertyObject(obj, "Parent");
            Assert.IsNull(result);
        }
        #endregion

        #region AddOrSetProperty
        [TestMethod]
        public void AddOrSetProperty_AddsNewProperty()
        {
            PSObject obj = new PSObject();
            GetDbaAgListenerCommand.AddOrSetProperty(obj, "ComputerName", "sql01");

            Assert.AreEqual("sql01", obj.Properties["ComputerName"].Value);
        }

        [TestMethod]
        public void AddOrSetProperty_UpdatesExistingProperty()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("ComputerName", "old"));

            GetDbaAgListenerCommand.AddOrSetProperty(obj, "ComputerName", "new");

            Assert.AreEqual("new", obj.Properties["ComputerName"].Value);
        }

        [TestMethod]
        public void AddOrSetProperty_NullObjectDoesNotThrow()
        {
            GetDbaAgListenerCommand.AddOrSetProperty(null, "Name", "value");
        }

        [TestMethod]
        public void AddOrSetProperty_AddsAllExpectedProperties()
        {
            PSObject obj = new PSObject();
            GetDbaAgListenerCommand.AddOrSetProperty(obj, "ComputerName", "sql01");
            GetDbaAgListenerCommand.AddOrSetProperty(obj, "InstanceName", "MSSQLSERVER");
            GetDbaAgListenerCommand.AddOrSetProperty(obj, "SqlInstance", "sql01");
            GetDbaAgListenerCommand.AddOrSetProperty(obj, "AvailabilityGroup", "AG01");

            Assert.AreEqual("sql01", obj.Properties["ComputerName"].Value);
            Assert.AreEqual("MSSQLSERVER", obj.Properties["InstanceName"].Value);
            Assert.AreEqual("sql01", obj.Properties["SqlInstance"].Value);
            Assert.AreEqual("AG01", obj.Properties["AvailabilityGroup"].Value);
        }
        #endregion

        #region SetDefaultDisplayPropertySet
        [TestMethod]
        public void SetDefaultDisplayPropertySet_AddsStandardMembers()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "Listener1"));
            obj.Properties.Add(new PSNoteProperty("PortNumber", 1433));

            string[] props = new string[] { "Name", "PortNumber" };
            GetDbaAgListenerCommand.SetDefaultDisplayPropertySet(obj, props);

            PSMemberInfo member = obj.Members["PSStandardMembers"];
            Assert.IsNotNull(member);
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_NullObjectDoesNotThrow()
        {
            GetDbaAgListenerCommand.SetDefaultDisplayPropertySet(null, new string[] { "Name" });
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_NullPropertiesDoesNotThrow()
        {
            PSObject obj = new PSObject();
            GetDbaAgListenerCommand.SetDefaultDisplayPropertySet(obj, null);
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_ReplacesExistingMembers()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "Listener1"));

            string[] props1 = new string[] { "Name" };
            GetDbaAgListenerCommand.SetDefaultDisplayPropertySet(obj, props1);

            string[] props2 = new string[] { "Name", "PortNumber" };
            GetDbaAgListenerCommand.SetDefaultDisplayPropertySet(obj, props2);

            PSMemberInfo member = obj.Members["PSStandardMembers"];
            Assert.IsNotNull(member);
        }
        #endregion
    }
}
