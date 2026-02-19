using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class AddDbaAgReplicaCommandTests
    {
        #region NormalizeSecondaryConnectionMode Tests

        [TestMethod]
        public void NormalizeSecondaryConnectionMode_No_ReturnsAllowNoConnections()
        {
            string result = AddDbaAgReplicaCommand.NormalizeSecondaryConnectionMode("No");
            Assert.AreEqual("AllowNoConnections", result);
        }

        [TestMethod]
        public void NormalizeSecondaryConnectionMode_ReadIntentOnly_ReturnsAllowReadIntentConnectionsOnly()
        {
            string result = AddDbaAgReplicaCommand.NormalizeSecondaryConnectionMode("Read-intent only");
            Assert.AreEqual("AllowReadIntentConnectionsOnly", result);
        }

        [TestMethod]
        public void NormalizeSecondaryConnectionMode_Yes_ReturnsAllowAllConnections()
        {
            string result = AddDbaAgReplicaCommand.NormalizeSecondaryConnectionMode("Yes");
            Assert.AreEqual("AllowAllConnections", result);
        }

        [TestMethod]
        public void NormalizeSecondaryConnectionMode_AllowNoConnections_ReturnsSame()
        {
            string result = AddDbaAgReplicaCommand.NormalizeSecondaryConnectionMode("AllowNoConnections");
            Assert.AreEqual("AllowNoConnections", result);
        }

        [TestMethod]
        public void NormalizeSecondaryConnectionMode_AllowAllConnections_ReturnsSame()
        {
            string result = AddDbaAgReplicaCommand.NormalizeSecondaryConnectionMode("AllowAllConnections");
            Assert.AreEqual("AllowAllConnections", result);
        }

        [TestMethod]
        public void NormalizeSecondaryConnectionMode_AllowReadIntentConnectionsOnly_ReturnsSame()
        {
            string result = AddDbaAgReplicaCommand.NormalizeSecondaryConnectionMode("AllowReadIntentConnectionsOnly");
            Assert.AreEqual("AllowReadIntentConnectionsOnly", result);
        }

        [TestMethod]
        public void NormalizeSecondaryConnectionMode_Null_ReturnsNull()
        {
            string result = AddDbaAgReplicaCommand.NormalizeSecondaryConnectionMode(null);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void NormalizeSecondaryConnectionMode_CaseInsensitive_No()
        {
            string result = AddDbaAgReplicaCommand.NormalizeSecondaryConnectionMode("no");
            Assert.AreEqual("AllowNoConnections", result);
        }

        [TestMethod]
        public void NormalizeSecondaryConnectionMode_CaseInsensitive_Yes()
        {
            string result = AddDbaAgReplicaCommand.NormalizeSecondaryConnectionMode("YES");
            Assert.AreEqual("AllowAllConnections", result);
        }

        #endregion NormalizeSecondaryConnectionMode Tests

        #region IsValidEndpointUrl Tests

        [TestMethod]
        public void IsValidEndpointUrl_ValidTCP_ReturnsTrue()
        {
            Assert.IsTrue(AddDbaAgReplicaCommand.IsValidEndpointUrl("TCP://sql2017b.domain.local:5022"));
        }

        [TestMethod]
        public void IsValidEndpointUrl_ValidTCPIPv4_ReturnsTrue()
        {
            Assert.IsTrue(AddDbaAgReplicaCommand.IsValidEndpointUrl("TCP://10.0.1.31:5022"));
        }

        [TestMethod]
        public void IsValidEndpointUrl_LowercaseTcp_ReturnsTrue()
        {
            Assert.IsTrue(AddDbaAgReplicaCommand.IsValidEndpointUrl("tcp://server:5022"));
        }

        [TestMethod]
        public void IsValidEndpointUrl_InvalidNoPort_ReturnsFalse()
        {
            Assert.IsFalse(AddDbaAgReplicaCommand.IsValidEndpointUrl("TCP://server"));
        }

        [TestMethod]
        public void IsValidEndpointUrl_InvalidNoProtocol_ReturnsFalse()
        {
            Assert.IsFalse(AddDbaAgReplicaCommand.IsValidEndpointUrl("server:5022"));
        }

        [TestMethod]
        public void IsValidEndpointUrl_Null_ReturnsFalse()
        {
            Assert.IsFalse(AddDbaAgReplicaCommand.IsValidEndpointUrl(null));
        }

        [TestMethod]
        public void IsValidEndpointUrl_Empty_ReturnsFalse()
        {
            Assert.IsFalse(AddDbaAgReplicaCommand.IsValidEndpointUrl(""));
        }

        [TestMethod]
        public void IsValidEndpointUrl_InvalidFormat_ReturnsFalse()
        {
            Assert.IsFalse(AddDbaAgReplicaCommand.IsValidEndpointUrl("HTTP://server:5022"));
        }

        [TestMethod]
        public void IsValidEndpointUrl_ExcessivelyLong_ReturnsFalse()
        {
            string longUrl = "TCP://" + new string('A', 510) + ":5022";
            Assert.IsFalse(AddDbaAgReplicaCommand.IsValidEndpointUrl(longUrl));
        }

        [TestMethod]
        public void IsValidEndpointUrl_TrailingText_ReturnsFalse()
        {
            Assert.IsFalse(AddDbaAgReplicaCommand.IsValidEndpointUrl("TCP://server:5022/extra"));
        }

        [TestMethod]
        public void IsValidEndpointUrl_LeadingText_ReturnsFalse()
        {
            Assert.IsFalse(AddDbaAgReplicaCommand.IsValidEndpointUrl("prefix TCP://server:5022"));
        }

        #endregion IsValidEndpointUrl Tests

        #region GetInnerExceptionMessage Tests

        [TestMethod]
        public void GetInnerExceptionMessage_TwoLevelsDeep_ReturnsInnermostMessage()
        {
            var inner2 = new Exception("Root cause");
            var inner1 = new Exception("Middle level", inner2);
            var outer = new Exception("Outer error", inner1);

            string result = AddDbaAgReplicaCommand.GetInnerExceptionMessage(outer);

            Assert.AreEqual("Root cause", result);
        }

        [TestMethod]
        public void GetInnerExceptionMessage_OneLevel_ReturnsInnerMessage()
        {
            var inner = new Exception("Inner cause");
            var outer = new Exception("Outer error", inner);

            string result = AddDbaAgReplicaCommand.GetInnerExceptionMessage(outer);

            Assert.AreEqual("Inner cause", result);
        }

        [TestMethod]
        public void GetInnerExceptionMessage_NoInner_ReturnsNull()
        {
            var ex = new Exception("Simple error");

            string result = AddDbaAgReplicaCommand.GetInnerExceptionMessage(ex);

            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetInnerExceptionMessage_Null_ReturnsNull()
        {
            string result = AddDbaAgReplicaCommand.GetInnerExceptionMessage(null);

            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetInnerExceptionMessage_ThreeLevelsDeep_ReturnsSecondLevel()
        {
            var inner3 = new Exception("Deepest");
            var inner2 = new Exception("Second level", inner3);
            var inner1 = new Exception("First level", inner2);
            var outer = new Exception("Outer", inner1);

            // PS1 pattern: $_.Exception.InnerException.InnerException.Message
            // This gets exactly 2 levels deep from the exception
            string result = AddDbaAgReplicaCommand.GetInnerExceptionMessage(outer);

            Assert.AreEqual("Second level", result);
        }

        #endregion GetInnerExceptionMessage Tests
    }
}
