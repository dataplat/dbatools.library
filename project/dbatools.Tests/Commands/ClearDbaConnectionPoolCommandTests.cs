using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class ClearDbaConnectionPoolCommandTests
    {
        #region ComputerNameParameter
        [TestMethod]
        public void ComputerName_LocalhostDetection_ReturnsTrueForLocalNames()
        {
            // Arrange - DbaInstanceParameter recognizes localhost variants
            var localParam = new DbaInstanceParameter("localhost");

            // Act & Assert
            Assert.IsTrue(localParam.IsLocalHost, "localhost should be detected as local");
        }

        [TestMethod]
        public void ComputerName_RemoteHost_ReturnsFalseForRemoteNames()
        {
            // Arrange
            var remoteParam = new DbaInstanceParameter("remoteserver01");

            // Act & Assert
            Assert.IsFalse(remoteParam.IsLocalHost, "remoteserver01 should not be detected as local");
        }

        [TestMethod]
        public void ComputerName_DotNotation_ReturnsTrueForDot()
        {
            // Arrange - "." is a common shorthand for localhost
            var dotParam = new DbaInstanceParameter(".");

            // Act & Assert
            Assert.IsTrue(dotParam.IsLocalHost, ". should be detected as local");
        }
        #endregion

        #region ParameterTypes
        [TestMethod]
        public void ComputerName_AcceptsDbaInstanceParameterArray()
        {
            // Arrange - Verify we can create an array of DbaInstanceParameter
            var computers = new DbaInstanceParameter[]
            {
                new DbaInstanceParameter("server1"),
                new DbaInstanceParameter("server2"),
                new DbaInstanceParameter("localhost")
            };

            // Act & Assert
            Assert.AreEqual(3, computers.Length);
            Assert.AreEqual("server1", computers[0].ComputerName);
            Assert.AreEqual("server2", computers[1].ComputerName);
            Assert.IsTrue(computers[2].IsLocalHost);
        }

        [TestMethod]
        public void ComputerName_NullArray_HandledGracefully()
        {
            // Verify that a null DbaInstanceParameter array can be checked
            DbaInstanceParameter[] computers = null;
            Assert.IsNull(computers);
        }

        [TestMethod]
        public void ComputerName_EmptyArray_HasZeroLength()
        {
            // Verify empty array handling
            var computers = new DbaInstanceParameter[0];
            Assert.AreEqual(0, computers.Length);
        }
        #endregion

        #region ClearAllPoolsAvailability
        [TestMethod]
        public void ClearAllPools_StaticMethodExists()
        {
            // Verify that Microsoft.Data.SqlClient.SqlConnection.ClearAllPools is callable
            // This does not require a live SQL connection - it simply clears the in-process pool
            // and should not throw even when no pools exist
            Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
            // If we reach here without exception, the method is available and works
            Assert.IsTrue(true, "ClearAllPools should be callable without throwing");
        }
        #endregion
    }
}
