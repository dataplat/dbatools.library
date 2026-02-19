using System;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class GetDbaRunningJobCommandTests
    {
        #region IsRunning

        [TestMethod]
        public void IsRunning_IdleJob_ReturnsFalse()
        {
            // Arrange
            PSObject jobObj = new PSObject();
            jobObj.Properties.Add(new PSNoteProperty("CurrentRunStatus", "Idle"));

            // Act
            bool result = Dataplat.Dbatools.Commands.GetDbaRunningJobCommand.IsRunning(jobObj);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void IsRunning_ExecutingJob_ReturnsTrue()
        {
            // Arrange
            PSObject jobObj = new PSObject();
            jobObj.Properties.Add(new PSNoteProperty("CurrentRunStatus", "Executing"));

            // Act
            bool result = Dataplat.Dbatools.Commands.GetDbaRunningJobCommand.IsRunning(jobObj);

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void IsRunning_SuspendedJob_ReturnsTrue()
        {
            // Arrange
            PSObject jobObj = new PSObject();
            jobObj.Properties.Add(new PSNoteProperty("CurrentRunStatus", "Suspended"));

            // Act
            bool result = Dataplat.Dbatools.Commands.GetDbaRunningJobCommand.IsRunning(jobObj);

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void IsRunning_IdleCaseInsensitive_ReturnsFalse()
        {
            // Arrange
            PSObject jobObj = new PSObject();
            jobObj.Properties.Add(new PSNoteProperty("CurrentRunStatus", "IDLE"));

            // Act
            bool result = Dataplat.Dbatools.Commands.GetDbaRunningJobCommand.IsRunning(jobObj);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void IsRunning_NullObject_ReturnsFalse()
        {
            // Act
            bool result = Dataplat.Dbatools.Commands.GetDbaRunningJobCommand.IsRunning(null);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void IsRunning_MissingProperty_ReturnsFalse()
        {
            // Arrange
            PSObject jobObj = new PSObject();
            jobObj.Properties.Add(new PSNoteProperty("Name", "TestJob"));

            // Act
            bool result = Dataplat.Dbatools.Commands.GetDbaRunningJobCommand.IsRunning(jobObj);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void IsRunning_NullPropertyValue_ReturnsFalse()
        {
            // Arrange
            PSObject jobObj = new PSObject();
            jobObj.Properties.Add(new PSNoteProperty("CurrentRunStatus", null));

            // Act
            bool result = Dataplat.Dbatools.Commands.GetDbaRunningJobCommand.IsRunning(jobObj);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void IsRunning_WaitingForWorkerThread_ReturnsTrue()
        {
            // Arrange - tests another non-idle status
            PSObject jobObj = new PSObject();
            jobObj.Properties.Add(new PSNoteProperty("CurrentRunStatus", "WaitingForWorkerThread"));

            // Act
            bool result = Dataplat.Dbatools.Commands.GetDbaRunningJobCommand.IsRunning(jobObj);

            // Assert
            Assert.IsTrue(result);
        }

        #endregion IsRunning
    }
}
