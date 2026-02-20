using System;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class SetDbaAgentAlertCommandTests
    {
        #region GetAlertName

        [TestMethod]
        public void GetAlertName_ValidPSObject_ReturnsName()
        {
            var pso = new PSObject();
            pso.Properties.Add(new PSNoteProperty("Name", "Test Alert"));
            string result = Dataplat.Dbatools.Commands.SetDbaAgentAlertCommand.GetAlertName(pso);
            Assert.AreEqual("Test Alert", result);
        }

        [TestMethod]
        public void GetAlertName_NullInput_ReturnsNull()
        {
            string result = Dataplat.Dbatools.Commands.SetDbaAgentAlertCommand.GetAlertName(null);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetAlertName_MissingNameProperty_ReturnsNull()
        {
            var pso = new PSObject();
            pso.Properties.Add(new PSNoteProperty("ID", 42));
            string result = Dataplat.Dbatools.Commands.SetDbaAgentAlertCommand.GetAlertName(pso);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetAlertName_NullNameValue_ReturnsNull()
        {
            var pso = new PSObject();
            pso.Properties.Add(new PSNoteProperty("Name", null));
            string result = Dataplat.Dbatools.Commands.SetDbaAgentAlertCommand.GetAlertName(pso);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetAlertName_IntegerNameValue_ReturnsString()
        {
            var pso = new PSObject();
            pso.Properties.Add(new PSNoteProperty("Name", 123));
            string result = Dataplat.Dbatools.Commands.SetDbaAgentAlertCommand.GetAlertName(pso);
            Assert.AreEqual("123", result);
        }

        #endregion

        #region GetAlertName_SpecialCharacters

        [TestMethod]
        public void GetAlertName_EmptyString_ReturnsEmptyString()
        {
            var pso = new PSObject();
            pso.Properties.Add(new PSNoteProperty("Name", ""));
            string result = Dataplat.Dbatools.Commands.SetDbaAgentAlertCommand.GetAlertName(pso);
            Assert.AreEqual("", result);
        }

        [TestMethod]
        public void GetAlertName_SpecialCharacters_ReturnsExact()
        {
            var pso = new PSObject();
            pso.Properties.Add(new PSNoteProperty("Name", "Severity 025: Fatal Error"));
            string result = Dataplat.Dbatools.Commands.SetDbaAgentAlertCommand.GetAlertName(pso);
            Assert.AreEqual("Severity 025: Fatal Error", result);
        }

        #endregion
    }
}
