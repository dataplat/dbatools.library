using System;
using System.Collections.Generic;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class NewDbaAgentJobCommandTests
    {
        #region ResolveNotificationLevel
        [TestMethod]
        public void ResolveNotificationLevel_Null_ReturnsZero()
        {
            Assert.AreEqual(0, NewDbaAgentJobCommand.ResolveNotificationLevel(null));
        }

        [TestMethod]
        public void ResolveNotificationLevel_IntZero_ReturnsZero()
        {
            Assert.AreEqual(0, NewDbaAgentJobCommand.ResolveNotificationLevel(0));
        }

        [TestMethod]
        public void ResolveNotificationLevel_IntOne_ReturnsOne()
        {
            Assert.AreEqual(1, NewDbaAgentJobCommand.ResolveNotificationLevel(1));
        }

        [TestMethod]
        public void ResolveNotificationLevel_IntTwo_ReturnsTwo()
        {
            Assert.AreEqual(2, NewDbaAgentJobCommand.ResolveNotificationLevel(2));
        }

        [TestMethod]
        public void ResolveNotificationLevel_IntThree_ReturnsThree()
        {
            Assert.AreEqual(3, NewDbaAgentJobCommand.ResolveNotificationLevel(3));
        }

        [TestMethod]
        public void ResolveNotificationLevel_StringNever_ReturnsZero()
        {
            Assert.AreEqual(0, NewDbaAgentJobCommand.ResolveNotificationLevel("Never"));
        }

        [TestMethod]
        public void ResolveNotificationLevel_StringOnSuccess_ReturnsOne()
        {
            Assert.AreEqual(1, NewDbaAgentJobCommand.ResolveNotificationLevel("OnSuccess"));
        }

        [TestMethod]
        public void ResolveNotificationLevel_StringOnFailure_ReturnsTwo()
        {
            Assert.AreEqual(2, NewDbaAgentJobCommand.ResolveNotificationLevel("OnFailure"));
        }

        [TestMethod]
        public void ResolveNotificationLevel_StringAlways_ReturnsThree()
        {
            Assert.AreEqual(3, NewDbaAgentJobCommand.ResolveNotificationLevel("Always"));
        }

        [TestMethod]
        public void ResolveNotificationLevel_CaseInsensitive_ReturnsCorrectValue()
        {
            Assert.AreEqual(1, NewDbaAgentJobCommand.ResolveNotificationLevel("onsuccess"));
            Assert.AreEqual(2, NewDbaAgentJobCommand.ResolveNotificationLevel("ONFAILURE"));
            Assert.AreEqual(3, NewDbaAgentJobCommand.ResolveNotificationLevel("always"));
        }

        [TestMethod]
        public void ResolveNotificationLevel_StringIntValue_ReturnsCorrectInt()
        {
            Assert.AreEqual(0, NewDbaAgentJobCommand.ResolveNotificationLevel("0"));
            Assert.AreEqual(1, NewDbaAgentJobCommand.ResolveNotificationLevel("1"));
            Assert.AreEqual(2, NewDbaAgentJobCommand.ResolveNotificationLevel("2"));
            Assert.AreEqual(3, NewDbaAgentJobCommand.ResolveNotificationLevel("3"));
        }

        [TestMethod]
        public void ResolveNotificationLevel_UnknownString_ReturnsZero()
        {
            Assert.AreEqual(0, NewDbaAgentJobCommand.ResolveNotificationLevel("invalid"));
        }
        #endregion

        #region IsContainedAgError
        [TestMethod]
        public void IsContainedAgError_WithNewParentMessage_ReturnsTrue()
        {
            var ex = new Exception("Parameter name: newParent");
            Assert.IsTrue(NewDbaAgentJobCommand.IsContainedAgError(ex));
        }

        [TestMethod]
        public void IsContainedAgError_CaseInsensitive_ReturnsTrue()
        {
            var ex = new Exception("Parameter name: NEWPARENT value is invalid");
            Assert.IsTrue(NewDbaAgentJobCommand.IsContainedAgError(ex));
        }

        [TestMethod]
        public void IsContainedAgError_InInnerException_ReturnsTrue()
        {
            var inner = new Exception("Value cannot be null. (Parameter 'newParent')");
            var outer = new Exception("Something went wrong", inner);
            Assert.IsTrue(NewDbaAgentJobCommand.IsContainedAgError(outer));
        }

        [TestMethod]
        public void IsContainedAgError_NullException_ReturnsFalse()
        {
            Assert.IsFalse(NewDbaAgentJobCommand.IsContainedAgError(null));
        }

        [TestMethod]
        public void IsContainedAgError_UnrelatedMessage_ReturnsFalse()
        {
            var ex = new Exception("Connection timeout expired");
            Assert.IsFalse(NewDbaAgentJobCommand.IsContainedAgError(ex));
        }
        #endregion

        #region CmdletAttribute
        [TestMethod]
        public void CmdletAttribute_SupportsShouldProcess_IsTrue()
        {
            var attrs = typeof(NewDbaAgentJobCommand).GetCustomAttributes(typeof(CmdletAttribute), false);
            Assert.AreEqual(1, attrs.Length);
            var cmdletAttr = (CmdletAttribute)attrs[0];
            Assert.IsTrue(cmdletAttr.SupportsShouldProcess);
        }

        [TestMethod]
        public void CmdletAttribute_VerbAndNoun_AreCorrect()
        {
            var attrs = typeof(NewDbaAgentJobCommand).GetCustomAttributes(typeof(CmdletAttribute), false);
            var cmdletAttr = (CmdletAttribute)attrs[0];
            Assert.AreEqual("New", cmdletAttr.VerbName);
            Assert.AreEqual("DbaAgentJob", cmdletAttr.NounName);
        }

        [TestMethod]
        public void CmdletAttribute_ConfirmImpact_IsLow()
        {
            var attrs = typeof(NewDbaAgentJobCommand).GetCustomAttributes(typeof(CmdletAttribute), false);
            var cmdletAttr = (CmdletAttribute)attrs[0];
            Assert.AreEqual(ConfirmImpact.Low, cmdletAttr.ConfirmImpact);
        }
        #endregion

        #region ParameterDefaults
        [TestMethod]
        public void ParameterDefaults_Job_IsNull()
        {
            var cmd = new NewDbaAgentJobCommand();
            Assert.IsNull(cmd.Job);
        }

        [TestMethod]
        public void ParameterDefaults_Schedule_IsNull()
        {
            var cmd = new NewDbaAgentJobCommand();
            Assert.IsNull(cmd.Schedule);
        }

        [TestMethod]
        public void ParameterDefaults_ScheduleId_IsNull()
        {
            var cmd = new NewDbaAgentJobCommand();
            Assert.IsNull(cmd.ScheduleId);
        }

        [TestMethod]
        public void ParameterDefaults_Disabled_IsFalse()
        {
            var cmd = new NewDbaAgentJobCommand();
            Assert.IsFalse(cmd.Disabled.IsPresent);
        }

        [TestMethod]
        public void ParameterDefaults_Force_IsFalse()
        {
            var cmd = new NewDbaAgentJobCommand();
            Assert.IsFalse(cmd.Force.IsPresent);
        }

        [TestMethod]
        public void ParameterDefaults_NotificationLevels_AreNull()
        {
            var cmd = new NewDbaAgentJobCommand();
            Assert.IsNull(cmd.EventLogLevel);
            Assert.IsNull(cmd.EmailLevel);
            Assert.IsNull(cmd.NetsendLevel);
            Assert.IsNull(cmd.PageLevel);
            Assert.IsNull(cmd.DeleteLevel);
        }
        #endregion

        #region ParameterValidation
        [TestMethod]
        public void JobParameter_HasMandatoryAttribute()
        {
            var prop = typeof(NewDbaAgentJobCommand).GetProperty("Job");
            Assert.IsNotNull(prop);
            var attrs = prop.GetCustomAttributes(typeof(ParameterAttribute), false);
            Assert.AreEqual(1, attrs.Length);
            var paramAttr = (ParameterAttribute)attrs[0];
            Assert.IsTrue(paramAttr.Mandatory);
        }

        [TestMethod]
        public void JobParameter_HasValidateNotNullOrEmpty()
        {
            var prop = typeof(NewDbaAgentJobCommand).GetProperty("Job");
            Assert.IsNotNull(prop);
            var attrs = prop.GetCustomAttributes(typeof(ValidateNotNullOrEmptyAttribute), false);
            Assert.AreEqual(1, attrs.Length);
        }

        [TestMethod]
        public void EventLogLevelParameter_HasValidateSet()
        {
            var prop = typeof(NewDbaAgentJobCommand).GetProperty("EventLogLevel");
            Assert.IsNotNull(prop);
            var attrs = prop.GetCustomAttributes(typeof(ValidateSetAttribute), false);
            Assert.AreEqual(1, attrs.Length);
            var validateSet = (ValidateSetAttribute)attrs[0];
            var validValues = new List<string>(validateSet.ValidValues);
            Assert.IsTrue(validValues.Contains("0"), "Missing '0'");
            Assert.IsTrue(validValues.Contains("Never"), "Missing 'Never'");
            Assert.IsTrue(validValues.Contains("1"), "Missing '1'");
            Assert.IsTrue(validValues.Contains("OnSuccess"), "Missing 'OnSuccess'");
            Assert.IsTrue(validValues.Contains("2"), "Missing '2'");
            Assert.IsTrue(validValues.Contains("OnFailure"), "Missing 'OnFailure'");
            Assert.IsTrue(validValues.Contains("3"), "Missing '3'");
            Assert.IsTrue(validValues.Contains("Always"), "Missing 'Always'");
        }

        [TestMethod]
        public void NetsendLevelParameter_Exists()
        {
            // NetsendLevel was missing from PS1 param block (bug fix)
            var prop = typeof(NewDbaAgentJobCommand).GetProperty("NetsendLevel");
            Assert.IsNotNull(prop, "NetsendLevel parameter should exist");
            var attrs = prop.GetCustomAttributes(typeof(ValidateSetAttribute), false);
            Assert.AreEqual(1, attrs.Length, "NetsendLevel should have ValidateSet");
        }
        #endregion

        #region InheritanceAndOutputType
        [TestMethod]
        public void Command_InheritsDbaInstanceCmdlet()
        {
            Assert.IsTrue(typeof(DbaInstanceCmdlet).IsAssignableFrom(typeof(NewDbaAgentJobCommand)));
        }

        [TestMethod]
        public void Command_HasOutputTypeAttribute()
        {
            var attrs = typeof(NewDbaAgentJobCommand).GetCustomAttributes(typeof(OutputTypeAttribute), false);
            Assert.AreEqual(1, attrs.Length);
        }
        #endregion
    }
}
