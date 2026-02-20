using System;
using System.Collections.Generic;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class NewDbaAgentJobStepCommandTests
    {
        #region ResolveJobName
        [TestMethod]
        public void ResolveJobName_StringInput_ReturnsString()
        {
            Assert.AreEqual("MyJob", NewDbaAgentJobStepCommand.ResolveJobName("MyJob"));
        }

        [TestMethod]
        public void ResolveJobName_NullInput_ReturnsNull()
        {
            Assert.IsNull(NewDbaAgentJobStepCommand.ResolveJobName(null));
        }

        [TestMethod]
        public void ResolveJobName_PSObjectWrappedString_ReturnsString()
        {
            var psObj = PSObject.AsPSObject("TestJob");
            Assert.AreEqual("TestJob", NewDbaAgentJobStepCommand.ResolveJobName(psObj));
        }

        [TestMethod]
        public void ResolveJobName_PSObjectWithNameProperty_ReturnsName()
        {
            var psObj = new PSObject();
            psObj.Properties.Add(new PSNoteProperty("Name", "JobFromSMO"));
            Assert.AreEqual("JobFromSMO", NewDbaAgentJobStepCommand.ResolveJobName(psObj));
        }

        [TestMethod]
        public void ResolveJobName_IntegerInput_ReturnsToString()
        {
            Assert.AreEqual("42", NewDbaAgentJobStepCommand.ResolveJobName(42));
        }

        [TestMethod]
        public void ResolveJobName_EmptyString_ReturnsEmpty()
        {
            Assert.AreEqual("", NewDbaAgentJobStepCommand.ResolveJobName(""));
        }
        #endregion

        #region IsContainedAgError
        [TestMethod]
        public void IsContainedAgError_WithNewParentMessage_ReturnsTrue()
        {
            var ex = new Exception("Parameter name: newParent");
            Assert.IsTrue(NewDbaAgentJobStepCommand.IsContainedAgError(ex));
        }

        [TestMethod]
        public void IsContainedAgError_CaseInsensitive_ReturnsTrue()
        {
            var ex = new Exception("Parameter name: NEWPARENT value is invalid");
            Assert.IsTrue(NewDbaAgentJobStepCommand.IsContainedAgError(ex));
        }

        [TestMethod]
        public void IsContainedAgError_InInnerException_ReturnsTrue()
        {
            var inner = new Exception("Value cannot be null. (Parameter 'newParent')");
            var outer = new Exception("Something went wrong", inner);
            Assert.IsTrue(NewDbaAgentJobStepCommand.IsContainedAgError(outer));
        }

        [TestMethod]
        public void IsContainedAgError_NullException_ReturnsFalse()
        {
            Assert.IsFalse(NewDbaAgentJobStepCommand.IsContainedAgError(null));
        }

        [TestMethod]
        public void IsContainedAgError_UnrelatedMessage_ReturnsFalse()
        {
            var ex = new Exception("Connection timeout expired");
            Assert.IsFalse(NewDbaAgentJobStepCommand.IsContainedAgError(ex));
        }
        #endregion

        #region CmdletAttribute
        [TestMethod]
        public void CmdletAttribute_SupportsShouldProcess_IsTrue()
        {
            var attrs = typeof(NewDbaAgentJobStepCommand).GetCustomAttributes(typeof(CmdletAttribute), false);
            Assert.AreEqual(1, attrs.Length);
            var cmdletAttr = (CmdletAttribute)attrs[0];
            Assert.IsTrue(cmdletAttr.SupportsShouldProcess);
        }

        [TestMethod]
        public void CmdletAttribute_VerbAndNoun_AreCorrect()
        {
            var attrs = typeof(NewDbaAgentJobStepCommand).GetCustomAttributes(typeof(CmdletAttribute), false);
            var cmdletAttr = (CmdletAttribute)attrs[0];
            Assert.AreEqual("New", cmdletAttr.VerbName);
            Assert.AreEqual("DbaAgentJobStep", cmdletAttr.NounName);
        }

        [TestMethod]
        public void CmdletAttribute_ConfirmImpact_IsLow()
        {
            var attrs = typeof(NewDbaAgentJobStepCommand).GetCustomAttributes(typeof(CmdletAttribute), false);
            var cmdletAttr = (CmdletAttribute)attrs[0];
            Assert.AreEqual(ConfirmImpact.Low, cmdletAttr.ConfirmImpact);
        }
        #endregion

        #region ParameterDefaults
        [TestMethod]
        public void ParameterDefaults_Job_IsNull()
        {
            var cmd = new NewDbaAgentJobStepCommand();
            Assert.IsNull(cmd.Job);
        }

        [TestMethod]
        public void ParameterDefaults_StepName_IsNull()
        {
            var cmd = new NewDbaAgentJobStepCommand();
            Assert.IsNull(cmd.StepName);
        }

        [TestMethod]
        public void ParameterDefaults_Subsystem_IsNull()
        {
            // Note: default "TransactSql" is applied in BeginProcessing, not at property level
            var cmd = new NewDbaAgentJobStepCommand();
            Assert.IsNull(cmd.Subsystem);
        }

        [TestMethod]
        public void ParameterDefaults_Force_IsFalse()
        {
            var cmd = new NewDbaAgentJobStepCommand();
            Assert.IsFalse(cmd.Force.IsPresent);
        }

        [TestMethod]
        public void ParameterDefaults_Insert_IsFalse()
        {
            var cmd = new NewDbaAgentJobStepCommand();
            Assert.IsFalse(cmd.Insert.IsPresent);
        }

        [TestMethod]
        public void ParameterDefaults_Flag_IsNull()
        {
            var cmd = new NewDbaAgentJobStepCommand();
            Assert.IsNull(cmd.Flag);
        }

        [TestMethod]
        public void ParameterDefaults_IntParams_AreZero()
        {
            var cmd = new NewDbaAgentJobStepCommand();
            Assert.AreEqual(0, cmd.StepId);
            Assert.AreEqual(0, cmd.CmdExecSuccessCode);
            Assert.AreEqual(0, cmd.OnSuccessStepId);
            Assert.AreEqual(0, cmd.OnFailStepId);
            Assert.AreEqual(0, cmd.RetryAttempts);
            Assert.AreEqual(0, cmd.RetryInterval);
        }
        #endregion

        #region ParameterValidation
        [TestMethod]
        public void JobParameter_HasMandatoryAttribute()
        {
            var prop = typeof(NewDbaAgentJobStepCommand).GetProperty("Job");
            Assert.IsNotNull(prop);
            var attrs = prop.GetCustomAttributes(typeof(ParameterAttribute), false);
            Assert.AreEqual(1, attrs.Length);
            var paramAttr = (ParameterAttribute)attrs[0];
            Assert.IsTrue(paramAttr.Mandatory);
        }

        [TestMethod]
        public void JobParameter_HasValidateNotNullOrEmpty()
        {
            var prop = typeof(NewDbaAgentJobStepCommand).GetProperty("Job");
            Assert.IsNotNull(prop);
            var attrs = prop.GetCustomAttributes(typeof(ValidateNotNullOrEmptyAttribute), false);
            Assert.AreEqual(1, attrs.Length);
        }

        [TestMethod]
        public void StepNameParameter_HasMandatoryAttribute()
        {
            var prop = typeof(NewDbaAgentJobStepCommand).GetProperty("StepName");
            Assert.IsNotNull(prop);
            var attrs = prop.GetCustomAttributes(typeof(ParameterAttribute), false);
            Assert.AreEqual(1, attrs.Length);
            var paramAttr = (ParameterAttribute)attrs[0];
            Assert.IsTrue(paramAttr.Mandatory);
        }

        [TestMethod]
        public void StepNameParameter_HasValidateNotNullOrEmpty()
        {
            var prop = typeof(NewDbaAgentJobStepCommand).GetProperty("StepName");
            Assert.IsNotNull(prop);
            var attrs = prop.GetCustomAttributes(typeof(ValidateNotNullOrEmptyAttribute), false);
            Assert.AreEqual(1, attrs.Length);
        }

        [TestMethod]
        public void SubsystemParameter_HasValidateSet()
        {
            var prop = typeof(NewDbaAgentJobStepCommand).GetProperty("Subsystem");
            Assert.IsNotNull(prop);
            var attrs = prop.GetCustomAttributes(typeof(ValidateSetAttribute), false);
            Assert.AreEqual(1, attrs.Length);
            var validateSet = (ValidateSetAttribute)attrs[0];
            var validValues = new List<string>(validateSet.ValidValues);
            Assert.IsTrue(validValues.Contains("TransactSql"), "Missing 'TransactSql'");
            Assert.IsTrue(validValues.Contains("CmdExec"), "Missing 'CmdExec'");
            Assert.IsTrue(validValues.Contains("PowerShell"), "Missing 'PowerShell'");
            Assert.IsTrue(validValues.Contains("Ssis"), "Missing 'Ssis'");
            Assert.IsTrue(validValues.Contains("AnalysisCommand"), "Missing 'AnalysisCommand'");
            Assert.IsTrue(validValues.Contains("AnalysisQuery"), "Missing 'AnalysisQuery'");
            Assert.IsTrue(validValues.Contains("ActiveScripting"), "Missing 'ActiveScripting'");
            Assert.IsTrue(validValues.Contains("Distribution"), "Missing 'Distribution'");
            Assert.IsTrue(validValues.Contains("LogReader"), "Missing 'LogReader'");
            Assert.IsTrue(validValues.Contains("Merge"), "Missing 'Merge'");
            Assert.IsTrue(validValues.Contains("QueueReader"), "Missing 'QueueReader'");
            Assert.IsTrue(validValues.Contains("Snapshot"), "Missing 'Snapshot'");
        }

        [TestMethod]
        public void OnSuccessActionParameter_HasValidateSet()
        {
            var prop = typeof(NewDbaAgentJobStepCommand).GetProperty("OnSuccessAction");
            Assert.IsNotNull(prop);
            var attrs = prop.GetCustomAttributes(typeof(ValidateSetAttribute), false);
            Assert.AreEqual(1, attrs.Length);
            var validateSet = (ValidateSetAttribute)attrs[0];
            var validValues = new List<string>(validateSet.ValidValues);
            Assert.IsTrue(validValues.Contains("QuitWithSuccess"), "Missing 'QuitWithSuccess'");
            Assert.IsTrue(validValues.Contains("QuitWithFailure"), "Missing 'QuitWithFailure'");
            Assert.IsTrue(validValues.Contains("GoToNextStep"), "Missing 'GoToNextStep'");
            Assert.IsTrue(validValues.Contains("GoToStep"), "Missing 'GoToStep'");
        }

        [TestMethod]
        public void OnFailActionParameter_HasValidateSet()
        {
            var prop = typeof(NewDbaAgentJobStepCommand).GetProperty("OnFailAction");
            Assert.IsNotNull(prop);
            var attrs = prop.GetCustomAttributes(typeof(ValidateSetAttribute), false);
            Assert.AreEqual(1, attrs.Length);
            var validateSet = (ValidateSetAttribute)attrs[0];
            var validValues = new List<string>(validateSet.ValidValues);
            Assert.IsTrue(validValues.Contains("QuitWithSuccess"), "Missing 'QuitWithSuccess'");
            Assert.IsTrue(validValues.Contains("QuitWithFailure"), "Missing 'QuitWithFailure'");
            Assert.IsTrue(validValues.Contains("GoToNextStep"), "Missing 'GoToNextStep'");
            Assert.IsTrue(validValues.Contains("GoToStep"), "Missing 'GoToStep'");
        }

        [TestMethod]
        public void FlagParameter_HasValidateSet()
        {
            var prop = typeof(NewDbaAgentJobStepCommand).GetProperty("Flag");
            Assert.IsNotNull(prop);
            var attrs = prop.GetCustomAttributes(typeof(ValidateSetAttribute), false);
            Assert.AreEqual(1, attrs.Length);
            var validateSet = (ValidateSetAttribute)attrs[0];
            var validValues = new List<string>(validateSet.ValidValues);
            Assert.IsTrue(validValues.Contains("AppendAllCmdExecOutputToJobHistory"), "Missing flag");
            Assert.IsTrue(validValues.Contains("AppendToJobHistory"), "Missing flag");
            Assert.IsTrue(validValues.Contains("AppendToLogFile"), "Missing flag");
            Assert.IsTrue(validValues.Contains("AppendToTableLog"), "Missing flag");
            Assert.IsTrue(validValues.Contains("LogToTableWithOverwrite"), "Missing flag");
            Assert.IsTrue(validValues.Contains("None"), "Missing flag");
            Assert.IsTrue(validValues.Contains("ProvideStopProcessEvent"), "Missing flag");
        }
        #endregion

        #region InheritanceAndOutputType
        [TestMethod]
        public void Command_InheritsDbaInstanceCmdlet()
        {
            Assert.IsTrue(typeof(DbaInstanceCmdlet).IsAssignableFrom(typeof(NewDbaAgentJobStepCommand)));
        }

        [TestMethod]
        public void Command_HasOutputTypeAttribute()
        {
            var attrs = typeof(NewDbaAgentJobStepCommand).GetCustomAttributes(typeof(OutputTypeAttribute), false);
            Assert.AreEqual(1, attrs.Length);
            var outputType = (OutputTypeAttribute)attrs[0];
            Assert.AreEqual(1, outputType.Type.Length);
            Assert.AreEqual("Microsoft.SqlServer.Management.Smo.Agent.JobStep", outputType.Type[0].Name);
        }
        #endregion

        #region ParameterTypes
        [TestMethod]
        public void JobParameter_IsObjectArray()
        {
            var prop = typeof(NewDbaAgentJobStepCommand).GetProperty("Job");
            Assert.IsNotNull(prop);
            Assert.AreEqual(typeof(object[]), prop.PropertyType);
        }

        [TestMethod]
        public void FlagParameter_IsStringArray()
        {
            var prop = typeof(NewDbaAgentJobStepCommand).GetProperty("Flag");
            Assert.IsNotNull(prop);
            Assert.AreEqual(typeof(string[]), prop.PropertyType);
        }

        [TestMethod]
        public void InsertParameter_IsSwitchParameter()
        {
            var prop = typeof(NewDbaAgentJobStepCommand).GetProperty("Insert");
            Assert.IsNotNull(prop);
            Assert.AreEqual(typeof(SwitchParameter), prop.PropertyType);
        }

        [TestMethod]
        public void ForceParameter_IsSwitchParameter()
        {
            var prop = typeof(NewDbaAgentJobStepCommand).GetProperty("Force");
            Assert.IsNotNull(prop);
            Assert.AreEqual(typeof(SwitchParameter), prop.PropertyType);
        }
        #endregion
    }
}