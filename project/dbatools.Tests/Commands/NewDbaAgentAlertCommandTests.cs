using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class NewDbaAgentAlertCommandTests
    {
        // Note: limited unit test coverage - command is primarily an SMO wrapper.
        // Core logic tested: error detection, parameter defaults, display properties.

        #region IsContainedAgError
        [TestMethod]
        public void IsContainedAgError_WithNewParentMessage_ReturnsTrue()
        {
            var ex = new Exception("Parameter name: newParent");
            Assert.IsTrue(NewDbaAgentAlertCommand.IsContainedAgError(ex));
        }

        [TestMethod]
        public void IsContainedAgError_CaseInsensitive_ReturnsTrue()
        {
            var ex = new Exception("Parameter name: NEWPARENT value is invalid");
            Assert.IsTrue(NewDbaAgentAlertCommand.IsContainedAgError(ex));
        }

        [TestMethod]
        public void IsContainedAgError_UnrelatedMessage_ReturnsFalse()
        {
            var ex = new Exception("Connection timeout expired");
            Assert.IsFalse(NewDbaAgentAlertCommand.IsContainedAgError(ex));
        }

        [TestMethod]
        public void IsContainedAgError_NullException_ReturnsFalse()
        {
            Assert.IsFalse(NewDbaAgentAlertCommand.IsContainedAgError(null));
        }

        [TestMethod]
        public void IsContainedAgError_EmptyMessage_ReturnsFalse()
        {
            var ex = new Exception("");
            Assert.IsFalse(NewDbaAgentAlertCommand.IsContainedAgError(ex));
        }
        #endregion

        #region ParameterDefaults
        [TestMethod]
        public void ParameterDefaults_DelayBetweenResponses_Is60()
        {
            var cmd = new NewDbaAgentAlertCommand();
            Assert.AreEqual(60, cmd.DelayBetweenResponses);
        }

        [TestMethod]
        public void ParameterDefaults_JobId_IsEmptyGuid()
        {
            var cmd = new NewDbaAgentAlertCommand();
            Assert.AreEqual("00000000-0000-0000-0000-000000000000", cmd.JobId);
        }

        [TestMethod]
        public void ParameterDefaults_NotifyMethod_IsNotifyAll()
        {
            var cmd = new NewDbaAgentAlertCommand();
            Assert.AreEqual("NotifyAll", cmd.NotifyMethod);
        }

        [TestMethod]
        public void ParameterDefaults_Severity_IsZero()
        {
            var cmd = new NewDbaAgentAlertCommand();
            Assert.AreEqual(0, cmd.Severity);
        }

        [TestMethod]
        public void ParameterDefaults_MessageId_IsZero()
        {
            var cmd = new NewDbaAgentAlertCommand();
            Assert.AreEqual(0, cmd.MessageId);
        }

        [TestMethod]
        public void ParameterDefaults_Operator_IsNull()
        {
            var cmd = new NewDbaAgentAlertCommand();
            Assert.IsNull(cmd.Operator);
        }
        #endregion

        #region CmdletAttribute
        [TestMethod]
        public void CmdletAttribute_SupportsShouldProcess_IsTrue()
        {
            var attrs = typeof(NewDbaAgentAlertCommand).GetCustomAttributes(typeof(CmdletAttribute), false);
            Assert.AreEqual(1, attrs.Length);
            var cmdletAttr = (CmdletAttribute)attrs[0];
            Assert.IsTrue(cmdletAttr.SupportsShouldProcess);
        }

        [TestMethod]
        public void CmdletAttribute_VerbAndNoun_AreCorrect()
        {
            var attrs = typeof(NewDbaAgentAlertCommand).GetCustomAttributes(typeof(CmdletAttribute), false);
            var cmdletAttr = (CmdletAttribute)attrs[0];
            Assert.AreEqual("New", cmdletAttr.VerbName);
            Assert.AreEqual("DbaAgentAlert", cmdletAttr.NounName);
        }

        [TestMethod]
        public void CmdletAttribute_ConfirmImpact_IsLow()
        {
            var attrs = typeof(NewDbaAgentAlertCommand).GetCustomAttributes(typeof(CmdletAttribute), false);
            var cmdletAttr = (CmdletAttribute)attrs[0];
            Assert.AreEqual(ConfirmImpact.Low, cmdletAttr.ConfirmImpact);
        }
        #endregion

        #region AlertParameterValidation
        [TestMethod]
        public void AlertParameter_HasMandatoryAttribute()
        {
            var prop = typeof(NewDbaAgentAlertCommand).GetProperty("Alert");
            Assert.IsNotNull(prop);
            var attrs = prop.GetCustomAttributes(typeof(ParameterAttribute), false);
            Assert.AreEqual(1, attrs.Length);
            var paramAttr = (ParameterAttribute)attrs[0];
            Assert.IsTrue(paramAttr.Mandatory);
        }

        [TestMethod]
        public void AlertParameter_HasValidateNotNullOrEmpty()
        {
            var prop = typeof(NewDbaAgentAlertCommand).GetProperty("Alert");
            Assert.IsNotNull(prop);
            var attrs = prop.GetCustomAttributes(typeof(ValidateNotNullOrEmptyAttribute), false);
            Assert.AreEqual(1, attrs.Length);
        }
        #endregion

        #region NotifyMethodValidation
        [TestMethod]
        public void NotifyMethodParameter_HasValidateSet()
        {
            var prop = typeof(NewDbaAgentAlertCommand).GetProperty("NotifyMethod");
            Assert.IsNotNull(prop);
            var attrs = prop.GetCustomAttributes(typeof(ValidateSetAttribute), false);
            Assert.AreEqual(1, attrs.Length);
            var validateSet = (ValidateSetAttribute)attrs[0];
            var validValues = new List<string>(validateSet.ValidValues);
            Assert.IsTrue(validValues.Contains("None"), "Missing 'None'");
            Assert.IsTrue(validValues.Contains("NotifyEmail"), "Missing 'NotifyEmail'");
            Assert.IsTrue(validValues.Contains("Pager"), "Missing 'Pager'");
            Assert.IsTrue(validValues.Contains("NetSend"), "Missing 'NetSend'");
            Assert.IsTrue(validValues.Contains("NotifyAll"), "Missing 'NotifyAll'");
        }
        #endregion
    }
}
