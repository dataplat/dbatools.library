using System;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class SetDbaAgentJobCommandTests
    {
        #region ConvertCompletionLevel

        [TestMethod]
        public void ConvertCompletionLevel_IntValue_ReturnsAsIs()
        {
            Assert.AreEqual(0, SetDbaAgentJobCommand.ConvertCompletionLevel(0));
            Assert.AreEqual(1, SetDbaAgentJobCommand.ConvertCompletionLevel(1));
            Assert.AreEqual(2, SetDbaAgentJobCommand.ConvertCompletionLevel(2));
            Assert.AreEqual(3, SetDbaAgentJobCommand.ConvertCompletionLevel(3));
        }

        [TestMethod]
        public void ConvertCompletionLevel_StringName_ReturnsCorrectInt()
        {
            Assert.AreEqual(0, SetDbaAgentJobCommand.ConvertCompletionLevel("Never"));
            Assert.AreEqual(1, SetDbaAgentJobCommand.ConvertCompletionLevel("OnSuccess"));
            Assert.AreEqual(2, SetDbaAgentJobCommand.ConvertCompletionLevel("OnFailure"));
            Assert.AreEqual(3, SetDbaAgentJobCommand.ConvertCompletionLevel("Always"));
        }

        [TestMethod]
        public void ConvertCompletionLevel_NumericString_ReturnsCorrectInt()
        {
            Assert.AreEqual(0, SetDbaAgentJobCommand.ConvertCompletionLevel("0"));
            Assert.AreEqual(1, SetDbaAgentJobCommand.ConvertCompletionLevel("1"));
            Assert.AreEqual(2, SetDbaAgentJobCommand.ConvertCompletionLevel("2"));
            Assert.AreEqual(3, SetDbaAgentJobCommand.ConvertCompletionLevel("3"));
        }

        [TestMethod]
        public void ConvertCompletionLevel_Null_ReturnsZero()
        {
            Assert.AreEqual(0, SetDbaAgentJobCommand.ConvertCompletionLevel(null));
        }

        [TestMethod]
        public void ConvertCompletionLevel_UnknownString_ReturnsZero()
        {
            Assert.AreEqual(0, SetDbaAgentJobCommand.ConvertCompletionLevel("Invalid"));
        }

        #endregion ConvertCompletionLevel

        #region GetJobName

        [TestMethod]
        public void GetJobName_WithNameProperty_ReturnsName()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "TestJob1"));
            Assert.AreEqual("TestJob1", SetDbaAgentJobCommand.GetJobName(obj));
        }

        [TestMethod]
        public void GetJobName_Null_ReturnsNull()
        {
            Assert.IsNull(SetDbaAgentJobCommand.GetJobName(null));
        }

        [TestMethod]
        public void GetJobName_NoNameProperty_ReturnsNull()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Title", "Something"));
            Assert.IsNull(SetDbaAgentJobCommand.GetJobName(obj));
        }

        #endregion GetJobName

        #region GetJobPropertyString

        [TestMethod]
        public void GetJobPropertyString_ExistingProperty_ReturnsValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("OperatorToEmail", "DBATeam"));
            Assert.AreEqual("DBATeam", SetDbaAgentJobCommand.GetJobPropertyString(obj, "OperatorToEmail"));
        }

        [TestMethod]
        public void GetJobPropertyString_MissingProperty_ReturnsNull()
        {
            PSObject obj = new PSObject();
            Assert.IsNull(SetDbaAgentJobCommand.GetJobPropertyString(obj, "NonExistent"));
        }

        [TestMethod]
        public void GetJobPropertyString_NullObject_ReturnsNull()
        {
            Assert.IsNull(SetDbaAgentJobCommand.GetJobPropertyString(null, "Name"));
        }

        [TestMethod]
        public void GetJobPropertyString_NullValue_ReturnsNull()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("OperatorToEmail", null));
            Assert.IsNull(SetDbaAgentJobCommand.GetJobPropertyString(obj, "OperatorToEmail"));
        }

        #endregion GetJobPropertyString
    }
}
