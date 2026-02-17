using System;
using System.Collections.Generic;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class GetDbaAvailabilityGroupCommandTests
    {
        #region BuildAgFilter

        [TestMethod]
        public void BuildAgFilter_WithNullInput_ReturnsNull()
        {
            var result = GetDbaAvailabilityGroupCommand.BuildAgFilter(null);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void BuildAgFilter_WithEmptyArray_ReturnsNull()
        {
            var result = GetDbaAvailabilityGroupCommand.BuildAgFilter(new string[0]);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void BuildAgFilter_WithNames_ReturnsCaseInsensitiveSet()
        {
            var result = GetDbaAvailabilityGroupCommand.BuildAgFilter(new string[] { "AG01", "AG02" });
            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Count);
            Assert.IsTrue(result.Contains("AG01"));
            Assert.IsTrue(result.Contains("ag01")); // case insensitive
            Assert.IsTrue(result.Contains("AG02"));
            Assert.IsFalse(result.Contains("AG03"));
        }

        [TestMethod]
        public void BuildAgFilter_WithSingleName_ReturnsSetWithOneElement()
        {
            var result = GetDbaAvailabilityGroupCommand.BuildAgFilter(new string[] { "MyAG" });
            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.Count);
            Assert.IsTrue(result.Contains("myag"));
        }

        #endregion BuildAgFilter

        #region GetPropertyString

        [TestMethod]
        public void GetPropertyString_WithNullObject_ReturnsNull()
        {
            var result = GetDbaAvailabilityGroupCommand.GetPropertyString(null, "Name");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyString_WithExistingProperty_ReturnsValue()
        {
            var obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "TestAG"));
            var result = GetDbaAvailabilityGroupCommand.GetPropertyString(obj, "Name");
            Assert.AreEqual("TestAG", result);
        }

        [TestMethod]
        public void GetPropertyString_WithMissingProperty_ReturnsNull()
        {
            var obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "TestAG"));
            var result = GetDbaAvailabilityGroupCommand.GetPropertyString(obj, "NonExistent");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyString_WithNullPropertyValue_ReturnsNull()
        {
            var obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", null));
            var result = GetDbaAvailabilityGroupCommand.GetPropertyString(obj, "Name");
            Assert.IsNull(result);
        }

        #endregion GetPropertyString

        #region GetPropertyValue

        [TestMethod]
        public void GetPropertyValue_WithNullObject_ReturnsNull()
        {
            var result = GetDbaAvailabilityGroupCommand.GetPropertyValue(null, "IsHadrEnabled");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyValue_WithBoolProperty_ReturnsBool()
        {
            var obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("IsHadrEnabled", true));
            var result = GetDbaAvailabilityGroupCommand.GetPropertyValue(obj, "IsHadrEnabled");
            Assert.IsInstanceOfType(result, typeof(bool));
            Assert.AreEqual(true, result);
        }

        [TestMethod]
        public void GetPropertyValue_WithFalseProperty_ReturnsFalse()
        {
            var obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("IsHadrEnabled", false));
            var result = GetDbaAvailabilityGroupCommand.GetPropertyValue(obj, "IsHadrEnabled");
            Assert.AreEqual(false, result);
        }

        #endregion GetPropertyValue

        #region AddOrSetProperty

        [TestMethod]
        public void AddOrSetProperty_WithNullObject_DoesNotThrow()
        {
            // Should not throw
            GetDbaAvailabilityGroupCommand.AddOrSetProperty(null, "Test", "value");
        }

        [TestMethod]
        public void AddOrSetProperty_AddsNewProperty()
        {
            var obj = new PSObject();
            GetDbaAvailabilityGroupCommand.AddOrSetProperty(obj, "ComputerName", "Server01");
            Assert.AreEqual("Server01", obj.Properties["ComputerName"].Value);
        }

        [TestMethod]
        public void AddOrSetProperty_OverwritesExistingProperty()
        {
            var obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("ComputerName", "OldValue"));
            GetDbaAvailabilityGroupCommand.AddOrSetProperty(obj, "ComputerName", "NewValue");
            Assert.AreEqual("NewValue", obj.Properties["ComputerName"].Value);
        }

        #endregion AddOrSetProperty

        #region AddAliasProperty

        [TestMethod]
        public void AddAliasProperty_WithNullObject_DoesNotThrow()
        {
            // Should not throw
            GetDbaAvailabilityGroupCommand.AddAliasProperty(null, "AvailabilityGroup", "Name");
        }

        [TestMethod]
        public void AddAliasProperty_CreatesAlias()
        {
            var obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "AG01"));
            GetDbaAvailabilityGroupCommand.AddAliasProperty(obj, "AvailabilityGroup", "Name");

            // The alias should be accessible via Members
            var member = obj.Members["AvailabilityGroup"];
            Assert.IsNotNull(member);
            Assert.IsInstanceOfType(member, typeof(PSAliasProperty));
        }

        #endregion AddAliasProperty

        #region SetDefaultDisplayPropertySet

        [TestMethod]
        public void SetDefaultDisplayPropertySet_WithNullObject_DoesNotThrow()
        {
            GetDbaAvailabilityGroupCommand.SetDefaultDisplayPropertySet(null, new string[] { "Name" });
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_WithNullProperties_DoesNotThrow()
        {
            var obj = new PSObject();
            GetDbaAvailabilityGroupCommand.SetDefaultDisplayPropertySet(obj, null);
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_SetsStandardMembers()
        {
            var obj = new PSObject();
            var props = new string[] { "ComputerName", "InstanceName", "SqlInstance" };
            GetDbaAvailabilityGroupCommand.SetDefaultDisplayPropertySet(obj, props);

            var member = obj.Members["PSStandardMembers"];
            Assert.IsNotNull(member);
        }

        #endregion SetDefaultDisplayPropertySet
    }
}
