using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class TestDbaAvailabilityGroupCommandTests
    {
        #region BackupsContainLog

        [TestMethod]
        public void BackupsContainLog_WithLogType_ReturnsTrue()
        {
            // Arrange
            Collection<PSObject> backups = new Collection<PSObject>();
            PSObject backup = new PSObject();
            backup.Properties.Add(new PSNoteProperty("Type", "Log"));
            backups.Add(backup);

            // Act
            bool result = TestDbaAvailabilityGroupCommand.BackupsContainLog(backups);

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void BackupsContainLog_WithFullTypeOnly_ReturnsFalse()
        {
            // Arrange
            Collection<PSObject> backups = new Collection<PSObject>();
            PSObject backup = new PSObject();
            backup.Properties.Add(new PSNoteProperty("Type", "Full"));
            backups.Add(backup);

            // Act
            bool result = TestDbaAvailabilityGroupCommand.BackupsContainLog(backups);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void BackupsContainLog_EmptyCollection_ReturnsFalse()
        {
            // Arrange
            Collection<PSObject> backups = new Collection<PSObject>();

            // Act
            bool result = TestDbaAvailabilityGroupCommand.BackupsContainLog(backups);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void BackupsContainLog_NullCollection_ReturnsFalse()
        {
            // Act
            bool result = TestDbaAvailabilityGroupCommand.BackupsContainLog(null);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void BackupsContainLog_MixedTypes_ReturnsTrue()
        {
            // Arrange
            Collection<PSObject> backups = new Collection<PSObject>();
            PSObject full = new PSObject();
            full.Properties.Add(new PSNoteProperty("Type", "Full"));
            backups.Add(full);

            PSObject diff = new PSObject();
            diff.Properties.Add(new PSNoteProperty("Type", "Differential"));
            backups.Add(diff);

            PSObject log = new PSObject();
            log.Properties.Add(new PSNoteProperty("Type", "Log"));
            backups.Add(log);

            // Act
            bool result = TestDbaAvailabilityGroupCommand.BackupsContainLog(backups);

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void BackupsContainLog_CaseInsensitive_ReturnsTrue()
        {
            // Arrange
            Collection<PSObject> backups = new Collection<PSObject>();
            PSObject backup = new PSObject();
            backup.Properties.Add(new PSNoteProperty("Type", "LOG"));
            backups.Add(backup);

            // Act
            bool result = TestDbaAvailabilityGroupCommand.BackupsContainLog(backups);

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void BackupsContainLog_NullEntries_HandledGracefully()
        {
            // Arrange
            Collection<PSObject> backups = new Collection<PSObject>();
            backups.Add(null);
            PSObject backup = new PSObject();
            backup.Properties.Add(new PSNoteProperty("Type", "Log"));
            backups.Add(backup);

            // Act
            bool result = TestDbaAvailabilityGroupCommand.BackupsContainLog(backups);

            // Assert
            Assert.IsTrue(result);
        }

        #endregion BackupsContainLog

        #region GetPropertyStringStatic

        [TestMethod]
        public void GetPropertyStringStatic_ExistingProperty_ReturnsValue()
        {
            // Arrange
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "TestDB"));

            // Act
            string result = TestDbaAvailabilityGroupCommand.GetPropertyStringStatic(obj, "Name");

            // Assert
            Assert.AreEqual("TestDB", result);
        }

        [TestMethod]
        public void GetPropertyStringStatic_NonExistentProperty_ReturnsNull()
        {
            // Arrange
            PSObject obj = new PSObject();

            // Act
            string result = TestDbaAvailabilityGroupCommand.GetPropertyStringStatic(obj, "DoesNotExist");

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyStringStatic_NullObject_ReturnsNull()
        {
            // Act
            string result = TestDbaAvailabilityGroupCommand.GetPropertyStringStatic(null, "Name");

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyStringStatic_NullPropertyValue_ReturnsNull()
        {
            // Arrange
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", null));

            // Act
            string result = TestDbaAvailabilityGroupCommand.GetPropertyStringStatic(obj, "Name");

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyStringStatic_IntProperty_ReturnsString()
        {
            // Arrange
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Version", 13));

            // Act
            string result = TestDbaAvailabilityGroupCommand.GetPropertyStringStatic(obj, "Version");

            // Assert
            Assert.AreEqual("13", result);
        }

        #endregion GetPropertyStringStatic

        #region BackupsContainLog_NoTypeProperty

        [TestMethod]
        public void BackupsContainLog_NoTypeProperty_ReturnsFalse()
        {
            // Arrange - object without Type property
            Collection<PSObject> backups = new Collection<PSObject>();
            PSObject backup = new PSObject();
            backup.Properties.Add(new PSNoteProperty("Name", "backup1"));
            backups.Add(backup);

            // Act
            bool result = TestDbaAvailabilityGroupCommand.BackupsContainLog(backups);

            // Assert
            Assert.IsFalse(result);
        }

        #endregion BackupsContainLog_NoTypeProperty
    }
}
