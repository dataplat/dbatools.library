using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class GetDbaAgentJobCategoryCommandTests
    {
        #region BuildJobCountLookup

        [TestMethod]
        public void BuildJobCountLookup_MultipleJobsSameCategory_CountsCorrectly()
        {
            // Arrange
            Collection<PSObject> jobs = new Collection<PSObject>();
            PSObject job1 = new PSObject();
            job1.Properties.Add(new PSNoteProperty("CategoryID", 1));
            PSObject job2 = new PSObject();
            job2.Properties.Add(new PSNoteProperty("CategoryID", 1));
            PSObject job3 = new PSObject();
            job3.Properties.Add(new PSNoteProperty("CategoryID", 2));
            jobs.Add(job1);
            jobs.Add(job2);
            jobs.Add(job3);

            // Act
            Dictionary<int, int> result = GetDbaAgentJobCategoryCommand.BuildJobCountLookup(jobs);

            // Assert
            Assert.AreEqual(2, result[1]);
            Assert.AreEqual(1, result[2]);
        }

        [TestMethod]
        public void BuildJobCountLookup_NullJobs_ReturnsEmptyDictionary()
        {
            // Act
            Dictionary<int, int> result = GetDbaAgentJobCategoryCommand.BuildJobCountLookup(null);

            // Assert
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void BuildJobCountLookup_EmptyJobs_ReturnsEmptyDictionary()
        {
            // Arrange
            Collection<PSObject> jobs = new Collection<PSObject>();

            // Act
            Dictionary<int, int> result = GetDbaAgentJobCategoryCommand.BuildJobCountLookup(jobs);

            // Assert
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void BuildJobCountLookup_NullJobInCollection_SkipsNull()
        {
            // Arrange
            Collection<PSObject> jobs = new Collection<PSObject>();
            jobs.Add(null);
            PSObject job1 = new PSObject();
            job1.Properties.Add(new PSNoteProperty("CategoryID", 5));
            jobs.Add(job1);

            // Act
            Dictionary<int, int> result = GetDbaAgentJobCategoryCommand.BuildJobCountLookup(jobs);

            // Assert
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(1, result[5]);
        }

        [TestMethod]
        public void BuildJobCountLookup_MissingCategoryID_DefaultsToZero()
        {
            // Arrange - PSObject without CategoryID property
            Collection<PSObject> jobs = new Collection<PSObject>();
            PSObject job1 = new PSObject();
            job1.Properties.Add(new PSNoteProperty("Name", "TestJob"));
            jobs.Add(job1);

            // Act
            Dictionary<int, int> result = GetDbaAgentJobCategoryCommand.BuildJobCountLookup(jobs);

            // Assert - GetPSPropertyInt returns 0 for missing property
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(1, result[0]);
        }

        #endregion BuildJobCountLookup

        #region GetPSPropertyString

        [TestMethod]
        public void GetPSPropertyString_ExistingProperty_ReturnsValue()
        {
            // Arrange
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "Database Maintenance"));

            // Act
            string result = GetDbaAgentJobCategoryCommand.GetPSPropertyString(obj, "Name");

            // Assert
            Assert.AreEqual("Database Maintenance", result);
        }

        [TestMethod]
        public void GetPSPropertyString_NullObject_ReturnsNull()
        {
            // Act
            string result = GetDbaAgentJobCategoryCommand.GetPSPropertyString(null, "Name");

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPSPropertyString_MissingProperty_ReturnsNull()
        {
            // Arrange
            PSObject obj = new PSObject();

            // Act
            string result = GetDbaAgentJobCategoryCommand.GetPSPropertyString(obj, "NonExistent");

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPSPropertyString_NullPropertyValue_ReturnsNull()
        {
            // Arrange
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", null));

            // Act
            string result = GetDbaAgentJobCategoryCommand.GetPSPropertyString(obj, "Name");

            // Assert
            Assert.IsNull(result);
        }

        #endregion GetPSPropertyString

        #region GetPSPropertyInt

        [TestMethod]
        public void GetPSPropertyInt_ExistingIntProperty_ReturnsValue()
        {
            // Arrange
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("ID", 42));

            // Act
            int result = GetDbaAgentJobCategoryCommand.GetPSPropertyInt(obj, "ID");

            // Assert
            Assert.AreEqual(42, result);
        }

        [TestMethod]
        public void GetPSPropertyInt_StringIntProperty_ParsesCorrectly()
        {
            // Arrange
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("ID", "99"));

            // Act
            int result = GetDbaAgentJobCategoryCommand.GetPSPropertyInt(obj, "ID");

            // Assert
            Assert.AreEqual(99, result);
        }

        [TestMethod]
        public void GetPSPropertyInt_NullObject_ReturnsZero()
        {
            // Act
            int result = GetDbaAgentJobCategoryCommand.GetPSPropertyInt(null, "ID");

            // Assert
            Assert.AreEqual(0, result);
        }

        [TestMethod]
        public void GetPSPropertyInt_MissingProperty_ReturnsZero()
        {
            // Arrange
            PSObject obj = new PSObject();

            // Act
            int result = GetDbaAgentJobCategoryCommand.GetPSPropertyInt(obj, "NonExistent");

            // Assert
            Assert.AreEqual(0, result);
        }

        #endregion GetPSPropertyInt

        #region AddOrSetProperty

        [TestMethod]
        public void AddOrSetProperty_NewProperty_AddsProperty()
        {
            // Arrange
            PSObject obj = new PSObject();

            // Act
            GetDbaAgentJobCategoryCommand.AddOrSetProperty(obj, "JobCount", 5);

            // Assert
            Assert.AreEqual(5, obj.Properties["JobCount"].Value);
        }

        [TestMethod]
        public void AddOrSetProperty_ExistingProperty_UpdatesValue()
        {
            // Arrange
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("JobCount", 3));

            // Act
            GetDbaAgentJobCategoryCommand.AddOrSetProperty(obj, "JobCount", 7);

            // Assert
            Assert.AreEqual(7, obj.Properties["JobCount"].Value);
        }

        [TestMethod]
        public void AddOrSetProperty_NullObject_DoesNotThrow()
        {
            // Act - should not throw
            GetDbaAgentJobCategoryCommand.AddOrSetProperty(null, "Test", "Value");
        }

        #endregion AddOrSetProperty

        #region SetDefaultDisplayPropertySet

        [TestMethod]
        public void SetDefaultDisplayPropertySet_ValidInput_AddsPSStandardMembers()
        {
            // Arrange
            PSObject obj = new PSObject();
            string[] props = new string[] { "ComputerName", "Name", "JobCount" };

            // Act
            GetDbaAgentJobCategoryCommand.SetDefaultDisplayPropertySet(obj, props);

            // Assert
            PSMemberInfo member = obj.Members["PSStandardMembers"];
            Assert.IsNotNull(member);
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_NullObject_DoesNotThrow()
        {
            // Act - should not throw
            GetDbaAgentJobCategoryCommand.SetDefaultDisplayPropertySet(null, new string[] { "Name" });
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_NullProperties_DoesNotThrow()
        {
            // Arrange
            PSObject obj = new PSObject();

            // Act - should not throw
            GetDbaAgentJobCategoryCommand.SetDefaultDisplayPropertySet(obj, null);
        }

        #endregion SetDefaultDisplayPropertySet

        #region GetServerPropertySafe

        [TestMethod]
        public void GetServerPropertySafe_ExistingProperty_ReturnsValue()
        {
            // Arrange
            PSObject server = new PSObject();
            server.Properties.Add(new PSNoteProperty("ComputerName", "SQL01"));

            // Act
            string result = GetDbaAgentJobCategoryCommand.GetServerPropertySafe(server, "ComputerName");

            // Assert
            Assert.AreEqual("SQL01", result);
        }

        [TestMethod]
        public void GetServerPropertySafe_NullObject_ReturnsNull()
        {
            // Act
            string result = GetDbaAgentJobCategoryCommand.GetServerPropertySafe(null, "ComputerName");

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetServerPropertySafe_MissingProperty_ReturnsNull()
        {
            // Arrange
            PSObject server = new PSObject();

            // Act
            string result = GetDbaAgentJobCategoryCommand.GetServerPropertySafe(server, "NonExistent");

            // Assert
            Assert.IsNull(result);
        }

        #endregion GetServerPropertySafe
    }
}
