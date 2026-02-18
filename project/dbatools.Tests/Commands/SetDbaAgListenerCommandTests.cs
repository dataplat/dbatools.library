using System;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class SetDbaAgListenerCommandTests
    {
        #region GetPropertyString
        [TestMethod]
        public void GetPropertyString_ValidProperty_ReturnsValue()
        {
            var pso = new PSObject();
            pso.Properties.Add(new PSNoteProperty("Name", "MyListener"));

            string result = Dataplat.Dbatools.Commands.SetDbaAgListenerCommand.GetPropertyString(pso, "Name");

            Assert.AreEqual("MyListener", result);
        }

        [TestMethod]
        public void GetPropertyString_MissingProperty_ReturnsNull()
        {
            var pso = new PSObject();
            pso.Properties.Add(new PSNoteProperty("Name", "MyListener"));

            string result = Dataplat.Dbatools.Commands.SetDbaAgListenerCommand.GetPropertyString(pso, "NonExistent");

            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyString_NullObject_ReturnsNull()
        {
            string result = Dataplat.Dbatools.Commands.SetDbaAgListenerCommand.GetPropertyString(null, "Name");

            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyString_NullPropertyValue_ReturnsNull()
        {
            var pso = new PSObject();
            pso.Properties.Add(new PSNoteProperty("Name", null));

            string result = Dataplat.Dbatools.Commands.SetDbaAgListenerCommand.GetPropertyString(pso, "Name");

            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyString_IntProperty_ReturnsStringRepresentation()
        {
            var pso = new PSObject();
            pso.Properties.Add(new PSNoteProperty("PortNumber", 1433));

            string result = Dataplat.Dbatools.Commands.SetDbaAgListenerCommand.GetPropertyString(pso, "PortNumber");

            Assert.AreEqual("1433", result);
        }
        #endregion

        #region GetPropertyObject
        [TestMethod]
        public void GetPropertyObject_ValidProperty_ReturnsPSObject()
        {
            var child = new PSObject();
            child.Properties.Add(new PSNoteProperty("Name", "AG01"));

            var parent = new PSObject();
            parent.Properties.Add(new PSNoteProperty("Parent", child));

            PSObject result = Dataplat.Dbatools.Commands.SetDbaAgListenerCommand.GetPropertyObject(parent, "Parent");

            Assert.IsNotNull(result);
            Assert.AreEqual("AG01", result.Properties["Name"].Value.ToString());
        }

        [TestMethod]
        public void GetPropertyObject_NullObject_ReturnsNull()
        {
            PSObject result = Dataplat.Dbatools.Commands.SetDbaAgListenerCommand.GetPropertyObject(null, "Parent");

            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyObject_MissingProperty_ReturnsNull()
        {
            var pso = new PSObject();

            PSObject result = Dataplat.Dbatools.Commands.SetDbaAgListenerCommand.GetPropertyObject(pso, "Parent");

            Assert.IsNull(result);
        }
        #endregion

        #region GetParentAgName
        [TestMethod]
        public void GetParentAgName_WithParent_ReturnsAgName()
        {
            var ag = new PSObject();
            ag.Properties.Add(new PSNoteProperty("Name", "AG01"));

            var listener = new PSObject();
            listener.Properties.Add(new PSNoteProperty("Parent", ag));

            string result = Dataplat.Dbatools.Commands.SetDbaAgListenerCommand.GetParentAgName(listener);

            Assert.AreEqual("AG01", result);
        }

        [TestMethod]
        public void GetParentAgName_NoParent_ReturnsNull()
        {
            var listener = new PSObject();
            listener.Properties.Add(new PSNoteProperty("Name", "Listener1"));

            string result = Dataplat.Dbatools.Commands.SetDbaAgListenerCommand.GetParentAgName(listener);

            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetParentAgName_NullInput_ReturnsNull()
        {
            string result = Dataplat.Dbatools.Commands.SetDbaAgListenerCommand.GetParentAgName(null);

            Assert.IsNull(result);
        }
        #endregion
    }
}
