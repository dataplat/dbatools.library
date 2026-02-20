using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class NewDbaAgentProxyCommandTests
    {
        #region ContainsSubSystem
        [TestMethod]
        public void ContainsSubSystem_MatchFound_ReturnsTrue()
        {
            string[] subSystems = new string[] { "CmdExec", "PowerShell", "Ssis" };
            Assert.IsTrue(Dbatools.Commands.NewDbaAgentProxyCommand.ContainsSubSystem(subSystems, "PowerShell"));
        }

        [TestMethod]
        public void ContainsSubSystem_CaseInsensitive_ReturnsTrue()
        {
            string[] subSystems = new string[] { "CmdExec", "PowerShell" };
            Assert.IsTrue(Dbatools.Commands.NewDbaAgentProxyCommand.ContainsSubSystem(subSystems, "cmdexec"));
        }

        [TestMethod]
        public void ContainsSubSystem_NoMatch_ReturnsFalse()
        {
            string[] subSystems = new string[] { "CmdExec", "PowerShell" };
            Assert.IsFalse(Dbatools.Commands.NewDbaAgentProxyCommand.ContainsSubSystem(subSystems, "Ssis"));
        }

        [TestMethod]
        public void ContainsSubSystem_NullArray_ReturnsFalse()
        {
            Assert.IsFalse(Dbatools.Commands.NewDbaAgentProxyCommand.ContainsSubSystem(null, "CmdExec"));
        }

        [TestMethod]
        public void ContainsSubSystem_EmptyArray_ReturnsFalse()
        {
            Assert.IsFalse(Dbatools.Commands.NewDbaAgentProxyCommand.ContainsSubSystem(new string[0], "CmdExec"));
        }
        #endregion

        #region JoinArray
        [TestMethod]
        public void JoinArray_MultipleValues_JoinsWithComma()
        {
            string[] values = new string[] { "CmdExec", "PowerShell", "Ssis" };
            Assert.AreEqual("CmdExec, PowerShell, Ssis", Dbatools.Commands.NewDbaAgentProxyCommand.JoinArray(values));
        }

        [TestMethod]
        public void JoinArray_SingleValue_ReturnsSingleValue()
        {
            string[] values = new string[] { "CmdExec" };
            Assert.AreEqual("CmdExec", Dbatools.Commands.NewDbaAgentProxyCommand.JoinArray(values));
        }

        [TestMethod]
        public void JoinArray_NullArray_ReturnsEmpty()
        {
            Assert.AreEqual(String.Empty, Dbatools.Commands.NewDbaAgentProxyCommand.JoinArray(null));
        }

        [TestMethod]
        public void JoinArray_EmptyArray_ReturnsEmpty()
        {
            Assert.AreEqual(String.Empty, Dbatools.Commands.NewDbaAgentProxyCommand.JoinArray(new string[0]));
        }
        #endregion

        #region IsContainedAgError
        [TestMethod]
        public void IsContainedAgError_MessageContainsNewParent_ReturnsTrue()
        {
            var ex = new Exception("Value cannot be null. Parameter name: newParent");
            Assert.IsTrue(Dbatools.Commands.NewDbaAgentProxyCommand.IsContainedAgError(ex));
        }

        [TestMethod]
        public void IsContainedAgError_InnerExceptionContainsNewParent_ReturnsTrue()
        {
            var inner = new Exception("Value cannot be null. Parameter name: newParent");
            var outer = new Exception("Some wrapper error", inner);
            Assert.IsTrue(Dbatools.Commands.NewDbaAgentProxyCommand.IsContainedAgError(outer));
        }

        [TestMethod]
        public void IsContainedAgError_NoNewParent_ReturnsFalse()
        {
            var ex = new Exception("Some other error");
            Assert.IsFalse(Dbatools.Commands.NewDbaAgentProxyCommand.IsContainedAgError(ex));
        }

        [TestMethod]
        public void IsContainedAgError_NullException_ReturnsFalse()
        {
            Assert.IsFalse(Dbatools.Commands.NewDbaAgentProxyCommand.IsContainedAgError(null));
        }

        [TestMethod]
        public void IsContainedAgError_CaseInsensitive_ReturnsTrue()
        {
            var ex = new Exception("Parameter name: NEWPARENT");
            Assert.IsTrue(Dbatools.Commands.NewDbaAgentProxyCommand.IsContainedAgError(ex));
        }
        #endregion

        #region AddOrSetProperty
        [TestMethod]
        public void AddOrSetProperty_AddsNewProperty()
        {
            var obj = new System.Management.Automation.PSObject();
            Dbatools.Commands.NewDbaAgentProxyCommand.AddOrSetProperty(obj, "TestProp", "TestValue");
            Assert.AreEqual("TestValue", obj.Properties["TestProp"].Value);
        }

        [TestMethod]
        public void AddOrSetProperty_UpdatesExistingProperty()
        {
            var obj = new System.Management.Automation.PSObject();
            obj.Properties.Add(new System.Management.Automation.PSNoteProperty("TestProp", "OldValue"));
            Dbatools.Commands.NewDbaAgentProxyCommand.AddOrSetProperty(obj, "TestProp", "NewValue");
            Assert.AreEqual("NewValue", obj.Properties["TestProp"].Value);
        }

        [TestMethod]
        public void AddOrSetProperty_NullObject_DoesNotThrow()
        {
            // Should not throw
            Dbatools.Commands.NewDbaAgentProxyCommand.AddOrSetProperty(null, "TestProp", "TestValue");
        }
        #endregion

        #region SetDefaultDisplayPropertySet
        [TestMethod]
        public void SetDefaultDisplayPropertySet_SetsProperties()
        {
            var obj = new System.Management.Automation.PSObject();
            string[] props = new string[] { "Name", "Value" };
            Dbatools.Commands.NewDbaAgentProxyCommand.SetDefaultDisplayPropertySet(obj, props);
            Assert.IsNotNull(obj.Members["PSStandardMembers"]);
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_NullObject_DoesNotThrow()
        {
            Dbatools.Commands.NewDbaAgentProxyCommand.SetDefaultDisplayPropertySet(null, new string[] { "Name" });
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_NullProperties_DoesNotThrow()
        {
            var obj = new System.Management.Automation.PSObject();
            Dbatools.Commands.NewDbaAgentProxyCommand.SetDefaultDisplayPropertySet(obj, null);
        }
        #endregion
    }
}
