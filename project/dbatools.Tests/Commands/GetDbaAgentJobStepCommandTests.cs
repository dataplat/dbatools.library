using System;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class GetDbaAgentJobStepCommandTests
    {
        #region GetPSPropertyString
        [TestMethod]
        public void GetPSPropertyString_ExistingProperty_ReturnsValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "TestJob"));

            string result = GetDbaAgentJobStepCommand.GetPSPropertyString(obj, "Name");

            Assert.AreEqual("TestJob", result);
        }

        [TestMethod]
        public void GetPSPropertyString_MissingProperty_ReturnsNull()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "TestJob"));

            string result = GetDbaAgentJobStepCommand.GetPSPropertyString(obj, "NonExistent");

            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPSPropertyString_NullObject_ReturnsNull()
        {
            string result = GetDbaAgentJobStepCommand.GetPSPropertyString(null, "Name");

            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPSPropertyString_NullValue_ReturnsNull()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", null));

            string result = GetDbaAgentJobStepCommand.GetPSPropertyString(obj, "Name");

            Assert.IsNull(result);
        }
        #endregion

        #region GetPSPropertyBool
        [TestMethod]
        public void GetPSPropertyBool_TrueValue_ReturnsTrue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("IsEnabled", true));

            bool result = GetDbaAgentJobStepCommand.GetPSPropertyBool(obj, "IsEnabled");

            Assert.IsTrue(result);
        }

        [TestMethod]
        public void GetPSPropertyBool_FalseValue_ReturnsFalse()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("IsEnabled", false));

            bool result = GetDbaAgentJobStepCommand.GetPSPropertyBool(obj, "IsEnabled");

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void GetPSPropertyBool_StringTrue_ReturnsTrue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("IsEnabled", "True"));

            bool result = GetDbaAgentJobStepCommand.GetPSPropertyBool(obj, "IsEnabled");

            Assert.IsTrue(result);
        }

        [TestMethod]
        public void GetPSPropertyBool_MissingProperty_ReturnsFalse()
        {
            PSObject obj = new PSObject();

            bool result = GetDbaAgentJobStepCommand.GetPSPropertyBool(obj, "IsEnabled");

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void GetPSPropertyBool_NullObject_ReturnsFalse()
        {
            bool result = GetDbaAgentJobStepCommand.GetPSPropertyBool(null, "IsEnabled");

            Assert.IsFalse(result);
        }
        #endregion

        #region AddOrSetProperty
        [TestMethod]
        public void AddOrSetProperty_AddsNewProperty()
        {
            PSObject obj = new PSObject();

            GetDbaAgentJobStepCommand.AddOrSetProperty(obj, "ComputerName", "Server1");

            Assert.AreEqual("Server1", obj.Properties["ComputerName"].Value);
        }

        [TestMethod]
        public void AddOrSetProperty_UpdatesExistingProperty()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("ComputerName", "OldServer"));

            GetDbaAgentJobStepCommand.AddOrSetProperty(obj, "ComputerName", "NewServer");

            Assert.AreEqual("NewServer", obj.Properties["ComputerName"].Value);
        }

        [TestMethod]
        public void AddOrSetProperty_NullObject_DoesNotThrow()
        {
            // Should not throw
            GetDbaAgentJobStepCommand.AddOrSetProperty(null, "Name", "Value");
        }

        [TestMethod]
        public void AddOrSetProperty_NullValue_SetsNull()
        {
            PSObject obj = new PSObject();

            GetDbaAgentJobStepCommand.AddOrSetProperty(obj, "ComputerName", null);

            Assert.IsNull(obj.Properties["ComputerName"].Value);
        }
        #endregion

        #region SetDefaultDisplayPropertySet
        [TestMethod]
        public void SetDefaultDisplayPropertySet_SetsProperties()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "Step1"));
            obj.Properties.Add(new PSNoteProperty("SubSystem", "TransactSql"));

            string[] props = new string[] { "Name", "SubSystem" };
            GetDbaAgentJobStepCommand.SetDefaultDisplayPropertySet(obj, props);

            PSMemberInfo standardMembers = obj.Members["PSStandardMembers"];
            Assert.IsNotNull(standardMembers);
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_NullObject_DoesNotThrow()
        {
            GetDbaAgentJobStepCommand.SetDefaultDisplayPropertySet(null, new string[] { "Name" });
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_NullProperties_DoesNotThrow()
        {
            PSObject obj = new PSObject();
            GetDbaAgentJobStepCommand.SetDefaultDisplayPropertySet(obj, null);
        }
        #endregion

        #region GetParentJobName
        [TestMethod]
        public void GetParentJobName_WithParent_ReturnsName()
        {
            PSObject parentJob = new PSObject();
            parentJob.Properties.Add(new PSNoteProperty("Name", "BackupJob"));

            PSObject step = new PSObject();
            step.Properties.Add(new PSNoteProperty("Parent", parentJob));

            string result = GetDbaAgentJobStepCommand.GetParentJobName(step);

            Assert.AreEqual("BackupJob", result);
        }

        [TestMethod]
        public void GetParentJobName_NoParent_ReturnsNull()
        {
            PSObject step = new PSObject();

            string result = GetDbaAgentJobStepCommand.GetParentJobName(step);

            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetParentJobName_NullStep_ReturnsNull()
        {
            string result = GetDbaAgentJobStepCommand.GetParentJobName(null);

            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetParentJobName_NullParentValue_ReturnsNull()
        {
            PSObject step = new PSObject();
            step.Properties.Add(new PSNoteProperty("Parent", null));

            string result = GetDbaAgentJobStepCommand.GetParentJobName(step);

            Assert.IsNull(result);
        }
        #endregion

        #region GetParentServerProperty
        [TestMethod]
        public void GetParentServerProperty_FullChain_ReturnsValue()
        {
            // Build the chain: step -> Job -> JobServer -> Server
            PSObject server = new PSObject();
            server.Properties.Add(new PSNoteProperty("ComputerName", "SQLBOX01"));

            PSObject jobServer = new PSObject();
            jobServer.Properties.Add(new PSNoteProperty("Parent", server));

            PSObject job = new PSObject();
            job.Properties.Add(new PSNoteProperty("Parent", jobServer));

            PSObject step = new PSObject();
            step.Properties.Add(new PSNoteProperty("Parent", job));

            string result = GetDbaAgentJobStepCommand.GetParentServerProperty(step, "ComputerName");

            Assert.AreEqual("SQLBOX01", result);
        }

        [TestMethod]
        public void GetParentServerProperty_BrokenChain_ReturnsNull()
        {
            // Only one level of parent
            PSObject job = new PSObject();
            // No Parent on job

            PSObject step = new PSObject();
            step.Properties.Add(new PSNoteProperty("Parent", job));

            string result = GetDbaAgentJobStepCommand.GetParentServerProperty(step, "ComputerName");

            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetParentServerProperty_NullStep_ReturnsNull()
        {
            string result = GetDbaAgentJobStepCommand.GetParentServerProperty(null, "ComputerName");

            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetParentServerProperty_NullPropertyName_ReturnsNull()
        {
            PSObject step = new PSObject();

            string result = GetDbaAgentJobStepCommand.GetParentServerProperty(step, null);

            Assert.IsNull(result);
        }
        #endregion
    }
}
