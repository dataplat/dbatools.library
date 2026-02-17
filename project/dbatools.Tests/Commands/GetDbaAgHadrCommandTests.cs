using System;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class GetDbaAgHadrCommandTests
    {
        #region GetPropertyString
        [TestMethod]
        public void GetPropertyString_ValidProperty_ReturnsValue()
        {
            var obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("TestProp", "TestValue"));
            string result = GetDbaAgHadrCommand.GetPropertyString(obj, "TestProp");
            Assert.AreEqual("TestValue", result);
        }

        [TestMethod]
        public void GetPropertyString_MissingProperty_ReturnsNull()
        {
            var obj = new PSObject();
            string result = GetDbaAgHadrCommand.GetPropertyString(obj, "NonExistent");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyString_NullObject_ReturnsNull()
        {
            string result = GetDbaAgHadrCommand.GetPropertyString(null, "TestProp");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyString_NullValue_ReturnsNull()
        {
            var obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("TestProp", null));
            string result = GetDbaAgHadrCommand.GetPropertyString(obj, "TestProp");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyString_IntegerProperty_ReturnsStringRepresentation()
        {
            var obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Port", 1433));
            string result = GetDbaAgHadrCommand.GetPropertyString(obj, "Port");
            Assert.AreEqual("1433", result);
        }
        #endregion

        #region AddOrSetProperty
        [TestMethod]
        public void AddOrSetProperty_NewProperty_AddsProperty()
        {
            var obj = new PSObject();
            GetDbaAgHadrCommand.AddOrSetProperty(obj, "ComputerName", "SERVER01");
            Assert.AreEqual("SERVER01", obj.Properties["ComputerName"].Value);
        }

        [TestMethod]
        public void AddOrSetProperty_ExistingProperty_UpdatesValue()
        {
            var obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("ComputerName", "OLD"));
            GetDbaAgHadrCommand.AddOrSetProperty(obj, "ComputerName", "NEW");
            Assert.AreEqual("NEW", obj.Properties["ComputerName"].Value);
        }

        [TestMethod]
        public void AddOrSetProperty_NullObject_DoesNotThrow()
        {
            GetDbaAgHadrCommand.AddOrSetProperty(null, "ComputerName", "SERVER01");
            // Should not throw
        }

        [TestMethod]
        public void AddOrSetProperty_NullValue_SetsNullProperty()
        {
            var obj = new PSObject();
            GetDbaAgHadrCommand.AddOrSetProperty(obj, "ComputerName", null);
            Assert.IsNull(obj.Properties["ComputerName"].Value);
        }
        #endregion

        #region SetDefaultDisplayPropertySet
        [TestMethod]
        public void SetDefaultDisplayPropertySet_ValidInput_SetsDisplayProperties()
        {
            var obj = new PSObject();
            string[] props = new string[] { "ComputerName", "InstanceName", "SqlInstance", "IsHadrEnabled" };
            GetDbaAgHadrCommand.SetDefaultDisplayPropertySet(obj, props);

            var standardMembers = obj.Members["PSStandardMembers"];
            Assert.IsNotNull(standardMembers);
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_NullObject_DoesNotThrow()
        {
            GetDbaAgHadrCommand.SetDefaultDisplayPropertySet(null, new string[] { "A" });
            // Should not throw
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_NullProperties_DoesNotThrow()
        {
            var obj = new PSObject();
            GetDbaAgHadrCommand.SetDefaultDisplayPropertySet(obj, null);
            // Should not throw
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_CalledTwice_ReplacesExisting()
        {
            var obj = new PSObject();
            string[] props1 = new string[] { "A", "B" };
            string[] props2 = new string[] { "C", "D" };
            GetDbaAgHadrCommand.SetDefaultDisplayPropertySet(obj, props1);
            GetDbaAgHadrCommand.SetDefaultDisplayPropertySet(obj, props2);

            var standardMembers = obj.Members["PSStandardMembers"];
            Assert.IsNotNull(standardMembers);
        }
        #endregion

        #region OutputObjectShape
        [TestMethod]
        public void OutputObject_HasExpectedProperties()
        {
            // Simulate building an output object like ProcessRecord does
            var obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("IsHadrEnabled", true));

            GetDbaAgHadrCommand.AddOrSetProperty(obj, "ComputerName", "SERVER01");
            GetDbaAgHadrCommand.AddOrSetProperty(obj, "InstanceName", "MSSQLSERVER");
            GetDbaAgHadrCommand.AddOrSetProperty(obj, "SqlInstance", "SERVER01");

            string[] expectedProps = new string[] { "ComputerName", "InstanceName", "SqlInstance", "IsHadrEnabled" };
            GetDbaAgHadrCommand.SetDefaultDisplayPropertySet(obj, expectedProps);

            // Verify all properties exist
            Assert.AreEqual("SERVER01", obj.Properties["ComputerName"].Value);
            Assert.AreEqual("MSSQLSERVER", obj.Properties["InstanceName"].Value);
            Assert.AreEqual("SERVER01", obj.Properties["SqlInstance"].Value);
            Assert.AreEqual(true, obj.Properties["IsHadrEnabled"].Value);
        }
        #endregion
    }
}
