using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class NewDbaAvailabilityGroupCommandTests
    {
        #region IsValidEndpointUrl

        [TestMethod]
        public void IsValidEndpointUrl_ValidTcp_ReturnsTrue()
        {
            Assert.IsTrue(Dbatools.Commands.NewDbaAvailabilityGroupCommand.IsValidEndpointUrl("TCP://sql01.lab.local:5022"));
        }

        [TestMethod]
        public void IsValidEndpointUrl_ValidLowercase_ReturnsTrue()
        {
            Assert.IsTrue(Dbatools.Commands.NewDbaAvailabilityGroupCommand.IsValidEndpointUrl("tcp://sql01:5022"));
        }

        [TestMethod]
        public void IsValidEndpointUrl_MissingPort_ReturnsFalse()
        {
            Assert.IsFalse(Dbatools.Commands.NewDbaAvailabilityGroupCommand.IsValidEndpointUrl("TCP://sql01"));
        }

        [TestMethod]
        public void IsValidEndpointUrl_EmptyString_ReturnsFalse()
        {
            Assert.IsFalse(Dbatools.Commands.NewDbaAvailabilityGroupCommand.IsValidEndpointUrl(""));
        }

        [TestMethod]
        public void IsValidEndpointUrl_NullString_ReturnsFalse()
        {
            Assert.IsFalse(Dbatools.Commands.NewDbaAvailabilityGroupCommand.IsValidEndpointUrl(null));
        }

        [TestMethod]
        public void IsValidEndpointUrl_InvalidProtocol_ReturnsFalse()
        {
            Assert.IsFalse(Dbatools.Commands.NewDbaAvailabilityGroupCommand.IsValidEndpointUrl("HTTP://sql01:5022"));
        }

        [TestMethod]
        public void IsValidEndpointUrl_MixedCase_ReturnsTrue()
        {
            Assert.IsTrue(Dbatools.Commands.NewDbaAvailabilityGroupCommand.IsValidEndpointUrl("Tcp://SQL01.DOMAIN.COM:5022"));
        }

        #endregion IsValidEndpointUrl

        #region NormalizeConnectionModeInSecondaryRole

        [TestMethod]
        public void NormalizeConnectionMode_No_ReturnsAllowNoConnections()
        {
            Assert.AreEqual("AllowNoConnections",
                Dbatools.Commands.NewDbaAvailabilityGroupCommand.NormalizeConnectionModeInSecondaryRole("No"));
        }

        [TestMethod]
        public void NormalizeConnectionMode_ReadIntentOnly_ReturnsAllowReadIntentConnectionsOnly()
        {
            Assert.AreEqual("AllowReadIntentConnectionsOnly",
                Dbatools.Commands.NewDbaAvailabilityGroupCommand.NormalizeConnectionModeInSecondaryRole("Read-intent only"));
        }

        [TestMethod]
        public void NormalizeConnectionMode_Yes_ReturnsAllowAllConnections()
        {
            Assert.AreEqual("AllowAllConnections",
                Dbatools.Commands.NewDbaAvailabilityGroupCommand.NormalizeConnectionModeInSecondaryRole("Yes"));
        }

        [TestMethod]
        public void NormalizeConnectionMode_AlreadyNormalized_ReturnsUnchanged()
        {
            Assert.AreEqual("AllowNoConnections",
                Dbatools.Commands.NewDbaAvailabilityGroupCommand.NormalizeConnectionModeInSecondaryRole("AllowNoConnections"));
        }

        [TestMethod]
        public void NormalizeConnectionMode_CaseInsensitive_No_Works()
        {
            Assert.AreEqual("AllowNoConnections",
                Dbatools.Commands.NewDbaAvailabilityGroupCommand.NormalizeConnectionModeInSecondaryRole("no"));
        }

        [TestMethod]
        public void NormalizeConnectionMode_CaseInsensitive_Yes_Works()
        {
            Assert.AreEqual("AllowAllConnections",
                Dbatools.Commands.NewDbaAvailabilityGroupCommand.NormalizeConnectionModeInSecondaryRole("yes"));
        }

        [TestMethod]
        public void NormalizeConnectionMode_Null_ReturnsNull()
        {
            Assert.IsNull(
                Dbatools.Commands.NewDbaAvailabilityGroupCommand.NormalizeConnectionModeInSecondaryRole(null));
        }

        [TestMethod]
        public void NormalizeConnectionMode_Empty_ReturnsEmpty()
        {
            Assert.AreEqual("",
                Dbatools.Commands.NewDbaAvailabilityGroupCommand.NormalizeConnectionModeInSecondaryRole(""));
        }

        #endregion NormalizeConnectionModeInSecondaryRole

        #region GetInnerExceptionMessage

        [TestMethod]
        public void GetInnerExceptionMessage_TwoLevelsDeep_ReturnsInnerInner()
        {
            var inner2 = new Exception("deepest error");
            var inner1 = new Exception("middle", inner2);
            var outer = new Exception("outer", inner1);

            Assert.AreEqual("deepest error",
                Dbatools.Commands.NewDbaAvailabilityGroupCommand.GetInnerExceptionMessage(outer));
        }

        [TestMethod]
        public void GetInnerExceptionMessage_OneLevelDeep_ReturnsInner()
        {
            var inner = new Exception("inner error");
            var outer = new Exception("outer", inner);

            Assert.AreEqual("inner error",
                Dbatools.Commands.NewDbaAvailabilityGroupCommand.GetInnerExceptionMessage(outer));
        }

        [TestMethod]
        public void GetInnerExceptionMessage_NoInner_ReturnsNull()
        {
            var ex = new Exception("just one level");

            Assert.IsNull(
                Dbatools.Commands.NewDbaAvailabilityGroupCommand.GetInnerExceptionMessage(ex));
        }

        [TestMethod]
        public void GetInnerExceptionMessage_Null_ReturnsNull()
        {
            Assert.IsNull(
                Dbatools.Commands.NewDbaAvailabilityGroupCommand.GetInnerExceptionMessage(null));
        }

        [TestMethod]
        public void GetInnerExceptionMessage_ThreeLevelsDeep_ReturnsSecondLevel()
        {
            // PS1 only goes 2 levels deep ($_.Exception.InnerException.InnerException.Message)
            var inner3 = new Exception("level 3");
            var inner2 = new Exception("level 2", inner3);
            var inner1 = new Exception("level 1", inner2);
            var outer = new Exception("outer", inner1);

            // Should return level 2 (InnerException.InnerException) per PS1 behavior
            Assert.AreEqual("level 2",
                Dbatools.Commands.NewDbaAvailabilityGroupCommand.GetInnerExceptionMessage(outer));
        }

        #endregion GetInnerExceptionMessage
    }
}
