using System;
using System.Collections.Generic;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class GetDbaAgDatabaseCommandTests
    {
        #region GetPropertyString
        [TestMethod]
        public void GetPropertyString_ReturnsStringValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "TestDatabase"));

            string result = GetDbaAgDatabaseCommand.GetPropertyString(obj, "Name");

            Assert.AreEqual("TestDatabase", result);
        }

        [TestMethod]
        public void GetPropertyString_NullObjectReturnsNull()
        {
            string result = GetDbaAgDatabaseCommand.GetPropertyString(null, "Name");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyString_MissingPropertyReturnsNull()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("ID", 1));

            string result = GetDbaAgDatabaseCommand.GetPropertyString(obj, "Name");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyString_NullValueReturnsNull()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", null));

            string result = GetDbaAgDatabaseCommand.GetPropertyString(obj, "Name");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyString_NonStringValueConvertsToString()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Count", 42));

            string result = GetDbaAgDatabaseCommand.GetPropertyString(obj, "Count");

            Assert.AreEqual("42", result);
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

            PSObject result = GetDbaAgDatabaseCommand.GetPropertyObject(obj, "Parent");

            Assert.IsNotNull(result);
            Assert.AreEqual("AG01", result.Properties["Name"].Value);
        }

        [TestMethod]
        public void GetPropertyObject_NullObjectReturnsNull()
        {
            PSObject result = GetDbaAgDatabaseCommand.GetPropertyObject(null, "Parent");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyObject_MissingPropertyReturnsNull()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "Test"));

            PSObject result = GetDbaAgDatabaseCommand.GetPropertyObject(obj, "Parent");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyObject_NullValueReturnsNull()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Parent", null));

            PSObject result = GetDbaAgDatabaseCommand.GetPropertyObject(obj, "Parent");
            Assert.IsNull(result);
        }
        #endregion

        #region AddOrSetProperty
        [TestMethod]
        public void AddOrSetProperty_AddsNewProperty()
        {
            PSObject obj = new PSObject();
            GetDbaAgDatabaseCommand.AddOrSetProperty(obj, "ComputerName", "sql01");

            Assert.AreEqual("sql01", obj.Properties["ComputerName"].Value);
        }

        [TestMethod]
        public void AddOrSetProperty_UpdatesExistingProperty()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("ComputerName", "old"));

            GetDbaAgDatabaseCommand.AddOrSetProperty(obj, "ComputerName", "new");

            Assert.AreEqual("new", obj.Properties["ComputerName"].Value);
        }

        [TestMethod]
        public void AddOrSetProperty_NullObjectDoesNotThrow()
        {
            GetDbaAgDatabaseCommand.AddOrSetProperty(null, "Name", "value");
        }

        [TestMethod]
        public void AddOrSetProperty_SetsNullValue()
        {
            PSObject obj = new PSObject();
            GetDbaAgDatabaseCommand.AddOrSetProperty(obj, "Prop", null);

            Assert.IsNull(obj.Properties["Prop"].Value);
        }

        [TestMethod]
        public void AddOrSetProperty_AddsMultipleProperties()
        {
            PSObject obj = new PSObject();
            GetDbaAgDatabaseCommand.AddOrSetProperty(obj, "ComputerName", "sql01");
            GetDbaAgDatabaseCommand.AddOrSetProperty(obj, "InstanceName", "MSSQLSERVER");
            GetDbaAgDatabaseCommand.AddOrSetProperty(obj, "SqlInstance", "sql01");

            Assert.AreEqual("sql01", obj.Properties["ComputerName"].Value);
            Assert.AreEqual("MSSQLSERVER", obj.Properties["InstanceName"].Value);
            Assert.AreEqual("sql01", obj.Properties["SqlInstance"].Value);
        }
        #endregion

        #region SetDefaultDisplayPropertySet
        [TestMethod]
        public void SetDefaultDisplayPropertySet_AddsStandardMembers()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "TestDB"));
            obj.Properties.Add(new PSNoteProperty("ComputerName", "sql01"));

            string[] props = new string[] { "Name", "ComputerName" };
            GetDbaAgDatabaseCommand.SetDefaultDisplayPropertySet(obj, props);

            PSMemberInfo member = obj.Members["PSStandardMembers"];
            Assert.IsNotNull(member);
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_NullObjectDoesNotThrow()
        {
            GetDbaAgDatabaseCommand.SetDefaultDisplayPropertySet(null, new string[] { "Name" });
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_NullPropertiesDoesNotThrow()
        {
            PSObject obj = new PSObject();
            GetDbaAgDatabaseCommand.SetDefaultDisplayPropertySet(obj, null);
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_ReplacesExistingMembers()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "TestDB"));

            string[] props1 = new string[] { "Name" };
            GetDbaAgDatabaseCommand.SetDefaultDisplayPropertySet(obj, props1);

            string[] props2 = new string[] { "Name", "ComputerName" };
            GetDbaAgDatabaseCommand.SetDefaultDisplayPropertySet(obj, props2);

            PSMemberInfo member = obj.Members["PSStandardMembers"];
            Assert.IsNotNull(member);
        }
        #endregion

        #region BuildDatabaseFilter
        [TestMethod]
        public void BuildDatabaseFilter_NullReturnsNull()
        {
            HashSet<string> result = GetDbaAgDatabaseCommand.BuildDatabaseFilter(null);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void BuildDatabaseFilter_EmptyArrayReturnsNull()
        {
            HashSet<string> result = GetDbaAgDatabaseCommand.BuildDatabaseFilter(new string[0]);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void BuildDatabaseFilter_PopulatesFilter()
        {
            HashSet<string> result = GetDbaAgDatabaseCommand.BuildDatabaseFilter(new string[] { "DB1", "DB2" });

            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Count);
            Assert.IsTrue(result.Contains("DB1"));
            Assert.IsTrue(result.Contains("DB2"));
        }

        [TestMethod]
        public void BuildDatabaseFilter_CaseInsensitive()
        {
            HashSet<string> result = GetDbaAgDatabaseCommand.BuildDatabaseFilter(new string[] { "MyDatabase" });

            Assert.IsNotNull(result);
            Assert.IsTrue(result.Contains("mydatabase"));
            Assert.IsTrue(result.Contains("MYDATABASE"));
            Assert.IsTrue(result.Contains("MyDatabase"));
        }

        [TestMethod]
        public void BuildDatabaseFilter_DeduplicatesSameCase()
        {
            HashSet<string> result = GetDbaAgDatabaseCommand.BuildDatabaseFilter(new string[] { "DB1", "DB1" });

            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.Count);
        }

        [TestMethod]
        public void BuildDatabaseFilter_DeduplicatesDifferentCase()
        {
            HashSet<string> result = GetDbaAgDatabaseCommand.BuildDatabaseFilter(new string[] { "db1", "DB1" });

            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.Count);
        }
        #endregion

        #region GetPropertyString_EdgeCases
        [TestMethod]
        public void GetPropertyString_EmptyStringReturnsEmptyString()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", ""));

            string result = GetDbaAgDatabaseCommand.GetPropertyString(obj, "Name");

            // Empty string converts to "" via ToString(), but our check requires non-null
            // so empty string is returned (not null)
            Assert.AreEqual("", result);
        }
        #endregion
    }
}
