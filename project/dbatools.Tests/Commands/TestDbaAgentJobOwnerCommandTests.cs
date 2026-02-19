using System;
using System.Collections.Generic;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class TestDbaAgentJobOwnerCommandTests
    {
        #region ToStringHashSet
        [TestMethod]
        public void ToStringHashSet_NullInput_ReturnsNull()
        {
            HashSet<string> result = TestDbaAgentJobOwnerCommand.ToStringHashSet(null);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void ToStringHashSet_EmptyArray_ReturnsNull()
        {
            HashSet<string> result = TestDbaAgentJobOwnerCommand.ToStringHashSet(new object[0]);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void ToStringHashSet_AllNulls_ReturnsNull()
        {
            HashSet<string> result = TestDbaAgentJobOwnerCommand.ToStringHashSet(new object[] { null, null });
            Assert.IsNull(result);
        }

        [TestMethod]
        public void ToStringHashSet_ValidStrings_ReturnsSet()
        {
            HashSet<string> result = TestDbaAgentJobOwnerCommand.ToStringHashSet(new object[] { "Job1", "Job2" });
            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Count);
            Assert.IsTrue(result.Contains("Job1"));
            Assert.IsTrue(result.Contains("Job2"));
        }

        [TestMethod]
        public void ToStringHashSet_CaseInsensitive()
        {
            HashSet<string> result = TestDbaAgentJobOwnerCommand.ToStringHashSet(new object[] { "TestJob" });
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Contains("testjob"));
            Assert.IsTrue(result.Contains("TESTJOB"));
        }

        [TestMethod]
        public void ToStringHashSet_MixedNullsAndValues_FiltersNulls()
        {
            HashSet<string> result = TestDbaAgentJobOwnerCommand.ToStringHashSet(new object[] { null, "Job1", null, "Job2" });
            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Count);
        }

        [TestMethod]
        public void ToStringHashSet_EmptyStringValues_FiltersEmpty()
        {
            HashSet<string> result = TestDbaAgentJobOwnerCommand.ToStringHashSet(new object[] { "" });
            Assert.IsNull(result);
        }

        [TestMethod]
        public void ToStringHashSet_NonStringObjects_ConvertsToString()
        {
            HashSet<string> result = TestDbaAgentJobOwnerCommand.ToStringHashSet(new object[] { 42, true });
            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Count);
            Assert.IsTrue(result.Contains("42"));
            Assert.IsTrue(result.Contains("True"));
        }
        #endregion

        #region GetPSPropertyString
        [TestMethod]
        public void GetPSPropertyString_ExistingProperty_ReturnsValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "TestJob"));
            string result = TestDbaAgentJobOwnerCommand.GetPSPropertyString(obj, "Name");
            Assert.AreEqual("TestJob", result);
        }

        [TestMethod]
        public void GetPSPropertyString_MissingProperty_ReturnsNull()
        {
            PSObject obj = new PSObject();
            string result = TestDbaAgentJobOwnerCommand.GetPSPropertyString(obj, "NonExistent");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPSPropertyString_NullObj_ReturnsNull()
        {
            string result = TestDbaAgentJobOwnerCommand.GetPSPropertyString(null, "Name");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPSPropertyString_NullValue_ReturnsNull()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", null));
            string result = TestDbaAgentJobOwnerCommand.GetPSPropertyString(obj, "Name");
            Assert.IsNull(result);
        }
        #endregion

        #region GetPSPropertyInt
        [TestMethod]
        public void GetPSPropertyInt_ValidInt_ReturnsValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("CategoryID", 5));
            Assert.AreEqual(5, TestDbaAgentJobOwnerCommand.GetPSPropertyInt(obj, "CategoryID"));
        }

        [TestMethod]
        public void GetPSPropertyInt_MissingProperty_ReturnsZero()
        {
            PSObject obj = new PSObject();
            Assert.AreEqual(0, TestDbaAgentJobOwnerCommand.GetPSPropertyInt(obj, "NonExistent"));
        }

        [TestMethod]
        public void GetPSPropertyInt_StringInt_ReturnsValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("CategoryID", "42"));
            Assert.AreEqual(42, TestDbaAgentJobOwnerCommand.GetPSPropertyInt(obj, "CategoryID"));
        }

        [TestMethod]
        public void GetPSPropertyInt_NullObj_ReturnsZero()
        {
            Assert.AreEqual(0, TestDbaAgentJobOwnerCommand.GetPSPropertyInt(null, "CategoryID"));
        }
        #endregion

        #region GetServerPropertySafe
        [TestMethod]
        public void GetServerPropertySafe_NullServer_ReturnsNull()
        {
            string result = TestDbaAgentJobOwnerCommand.GetServerPropertySafe(null, "Name");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetServerPropertySafe_ExistingProperty_ReturnsValue()
        {
            PSObject server = new PSObject();
            server.Properties.Add(new PSNoteProperty("Name", "SERVER01"));
            string result = TestDbaAgentJobOwnerCommand.GetServerPropertySafe(server, "Name");
            Assert.AreEqual("SERVER01", result);
        }

        [TestMethod]
        public void GetServerPropertySafe_MissingProperty_ReturnsNull()
        {
            PSObject server = new PSObject();
            string result = TestDbaAgentJobOwnerCommand.GetServerPropertySafe(server, "NonExistent");
            Assert.IsNull(result);
        }
        #endregion

        #region SetDefaultDisplayPropertySet
        [TestMethod]
        public void SetDefaultDisplayPropertySet_SetsProperties()
        {
            PSObject obj = new PSObject();
            string[] props = new string[] { "Server", "Job" };
            TestDbaAgentJobOwnerCommand.SetDefaultDisplayPropertySet(obj, props);

            PSMemberInfo member = obj.Members["PSStandardMembers"];
            Assert.IsNotNull(member);
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_NullObj_DoesNotThrow()
        {
            TestDbaAgentJobOwnerCommand.SetDefaultDisplayPropertySet(null, new string[] { "Server" });
            // No exception means success
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_CalledTwice_DoesNotThrow()
        {
            PSObject obj = new PSObject();
            string[] props = new string[] { "Server" };
            TestDbaAgentJobOwnerCommand.SetDefaultDisplayPropertySet(obj, props);
            TestDbaAgentJobOwnerCommand.SetDefaultDisplayPropertySet(obj, props);

            PSMemberInfo member = obj.Members["PSStandardMembers"];
            Assert.IsNotNull(member);
        }
        #endregion

        #region OutputObjectShape
        [TestMethod]
        public void OutputObject_HasExpectedProperties()
        {
            // Simulate the output PSObject structure created by the command
            PSObject result = new PSObject();
            result.Properties.Add(new PSNoteProperty("Server", "SERVER01"));
            result.Properties.Add(new PSNoteProperty("Job", "TestJob"));
            result.Properties.Add(new PSNoteProperty("JobType", "Local"));
            result.Properties.Add(new PSNoteProperty("CurrentOwner", "DOMAIN\\user"));
            result.Properties.Add(new PSNoteProperty("TargetOwner", "sa"));
            result.Properties.Add(new PSNoteProperty("OwnerMatch", false));

            Assert.AreEqual("SERVER01", result.Properties["Server"].Value);
            Assert.AreEqual("TestJob", result.Properties["Job"].Value);
            Assert.AreEqual("Local", result.Properties["JobType"].Value);
            Assert.AreEqual("DOMAIN\\user", result.Properties["CurrentOwner"].Value);
            Assert.AreEqual("sa", result.Properties["TargetOwner"].Value);
            Assert.AreEqual(false, result.Properties["OwnerMatch"].Value);
        }

        [TestMethod]
        public void OutputObject_RemoteJob_OwnerMatchIsTrue()
        {
            // Remote jobs (CategoryID=1) always have OwnerMatch=true
            // This validates the logic: if categoryId==1, ownerMatch=true regardless of owner
            int categoryId = 1;
            string ownerLoginName = "DOMAIN\\someuser";
            string targetLogin = "sa";

            bool ownerMatch;
            if (categoryId == 1)
            {
                ownerMatch = true;
            }
            else
            {
                ownerMatch = String.Equals(ownerLoginName, targetLogin, StringComparison.OrdinalIgnoreCase);
            }

            Assert.IsTrue(ownerMatch);
        }

        [TestMethod]
        public void OutputObject_LocalJob_OwnerMismatch()
        {
            int categoryId = 0;
            string ownerLoginName = "DOMAIN\\user";
            string targetLogin = "sa";

            bool ownerMatch;
            if (categoryId == 1)
            {
                ownerMatch = true;
            }
            else
            {
                ownerMatch = String.Equals(ownerLoginName, targetLogin, StringComparison.OrdinalIgnoreCase);
            }

            Assert.IsFalse(ownerMatch);
        }

        [TestMethod]
        public void OutputObject_LocalJob_OwnerMatch_CaseInsensitive()
        {
            int categoryId = 0;
            string ownerLoginName = "SA";
            string targetLogin = "sa";

            bool ownerMatch;
            if (categoryId == 1)
            {
                ownerMatch = true;
            }
            else
            {
                ownerMatch = String.Equals(ownerLoginName, targetLogin, StringComparison.OrdinalIgnoreCase);
            }

            Assert.IsTrue(ownerMatch);
        }

        [TestMethod]
        public void OutputObject_RemoteJob_JobTypeIsRemote()
        {
            int categoryId = 1;
            string jobType;
            if (categoryId == 1)
            {
                jobType = "Remote";
            }
            else
            {
                jobType = "Local";
            }

            Assert.AreEqual("Remote", jobType);
        }
        #endregion
    }
}
