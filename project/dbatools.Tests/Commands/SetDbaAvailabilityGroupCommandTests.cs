using System;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class SetDbaAvailabilityGroupCommandTests
    {
        #region GetPropertyString

        [TestMethod]
        public void GetPropertyString_WithNullObject_ReturnsNull()
        {
            var result = SetDbaAvailabilityGroupCommand.GetPropertyString(null, "Name");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyString_WithExistingProperty_ReturnsValue()
        {
            var obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "TestAG"));
            var result = SetDbaAvailabilityGroupCommand.GetPropertyString(obj, "Name");
            Assert.AreEqual("TestAG", result);
        }

        [TestMethod]
        public void GetPropertyString_WithMissingProperty_ReturnsNull()
        {
            var obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "TestAG"));
            var result = SetDbaAvailabilityGroupCommand.GetPropertyString(obj, "NonExistent");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyString_WithNullPropertyValue_ReturnsNull()
        {
            var obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", null));
            var result = SetDbaAvailabilityGroupCommand.GetPropertyString(obj, "Name");
            Assert.IsNull(result);
        }

        #endregion GetPropertyString

        #region GetPropertyValue

        [TestMethod]
        public void GetPropertyValue_WithNullObject_ReturnsNull()
        {
            var result = SetDbaAvailabilityGroupCommand.GetPropertyValue(null, "VersionMajor");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyValue_WithIntProperty_ReturnsInt()
        {
            var obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("VersionMajor", 16));
            var result = SetDbaAvailabilityGroupCommand.GetPropertyValue(obj, "VersionMajor");
            Assert.IsInstanceOfType(result, typeof(int));
            Assert.AreEqual(16, result);
        }

        [TestMethod]
        public void GetPropertyValue_WithBoolProperty_ReturnsBool()
        {
            var obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("DtcSupportEnabled", true));
            var result = SetDbaAvailabilityGroupCommand.GetPropertyValue(obj, "DtcSupportEnabled");
            Assert.AreEqual(true, result);
        }

        [TestMethod]
        public void GetPropertyValue_WithMissingProperty_ReturnsNull()
        {
            var obj = new PSObject();
            var result = SetDbaAvailabilityGroupCommand.GetPropertyValue(obj, "DoesNotExist");
            Assert.IsNull(result);
        }

        #endregion GetPropertyValue

        #region GetPropertyObject

        [TestMethod]
        public void GetPropertyObject_WithNullObject_ReturnsNull()
        {
            var result = SetDbaAvailabilityGroupCommand.GetPropertyObject(null, "Parent");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyObject_WithExistingProperty_ReturnsPSObject()
        {
            var parent = new PSObject();
            parent.Properties.Add(new PSNoteProperty("Name", "ServerName"));

            var obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Parent", parent));

            var result = SetDbaAvailabilityGroupCommand.GetPropertyObject(obj, "Parent");
            Assert.IsNotNull(result);
        }

        [TestMethod]
        public void GetPropertyObject_WithNullPropertyValue_ReturnsNull()
        {
            var obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Parent", null));
            var result = SetDbaAvailabilityGroupCommand.GetPropertyObject(obj, "Parent");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyObject_WithMissingProperty_ReturnsNull()
        {
            var obj = new PSObject();
            var result = SetDbaAvailabilityGroupCommand.GetPropertyObject(obj, "Parent");
            Assert.IsNull(result);
        }

        #endregion GetPropertyObject

        #region GetVersionMajor

        [TestMethod]
        public void GetVersionMajor_WithNullObject_ReturnsZero()
        {
            var result = SetDbaAvailabilityGroupCommand.GetVersionMajor(null);
            Assert.AreEqual(0, result);
        }

        [TestMethod]
        public void GetVersionMajor_WithParentAndVersion_ReturnsVersion()
        {
            var parent = new PSObject();
            parent.Properties.Add(new PSNoteProperty("VersionMajor", 16));

            var ag = new PSObject();
            ag.Properties.Add(new PSNoteProperty("Parent", parent));

            var result = SetDbaAvailabilityGroupCommand.GetVersionMajor(ag);
            Assert.AreEqual(16, result);
        }

        [TestMethod]
        public void GetVersionMajor_WithSql2025Version_Returns17()
        {
            var parent = new PSObject();
            parent.Properties.Add(new PSNoteProperty("VersionMajor", 17));

            var ag = new PSObject();
            ag.Properties.Add(new PSNoteProperty("Parent", parent));

            var result = SetDbaAvailabilityGroupCommand.GetVersionMajor(ag);
            Assert.AreEqual(17, result);
        }

        [TestMethod]
        public void GetVersionMajor_WithStringVersion_ParsesCorrectly()
        {
            var parent = new PSObject();
            parent.Properties.Add(new PSNoteProperty("VersionMajor", "15"));

            var ag = new PSObject();
            ag.Properties.Add(new PSNoteProperty("Parent", parent));

            var result = SetDbaAvailabilityGroupCommand.GetVersionMajor(ag);
            Assert.AreEqual(15, result);
        }

        [TestMethod]
        public void GetVersionMajor_WithNoParent_ReturnsZero()
        {
            var ag = new PSObject();
            var result = SetDbaAvailabilityGroupCommand.GetVersionMajor(ag);
            Assert.AreEqual(0, result);
        }

        [TestMethod]
        public void GetVersionMajor_WithParentNoVersion_ReturnsZero()
        {
            var parent = new PSObject();

            var ag = new PSObject();
            ag.Properties.Add(new PSNoteProperty("Parent", parent));

            var result = SetDbaAvailabilityGroupCommand.GetVersionMajor(ag);
            Assert.AreEqual(0, result);
        }

        #endregion GetVersionMajor

        #region GetServerName

        [TestMethod]
        public void GetServerName_WithNullObject_ReturnsNull()
        {
            var result = SetDbaAvailabilityGroupCommand.GetServerName(null);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetServerName_WithParentAndName_ReturnsServerName()
        {
            var parent = new PSObject();
            parent.Properties.Add(new PSNoteProperty("Name", "sql01"));

            var ag = new PSObject();
            ag.Properties.Add(new PSNoteProperty("Parent", parent));

            var result = SetDbaAvailabilityGroupCommand.GetServerName(ag);
            Assert.AreEqual("sql01", result);
        }

        [TestMethod]
        public void GetServerName_WithNoParent_ReturnsNull()
        {
            var ag = new PSObject();
            var result = SetDbaAvailabilityGroupCommand.GetServerName(ag);
            Assert.IsNull(result);
        }

        #endregion GetServerName
    }
}
