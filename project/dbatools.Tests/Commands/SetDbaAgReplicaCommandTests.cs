using System;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class SetDbaAgReplicaCommandTests
    {
        #region NormalizeSecondaryConnectionMode

        [TestMethod]
        public void NormalizeSecondaryConnectionMode_No_ReturnsAllowNoConnections()
        {
            string result = SetDbaAgReplicaCommand.NormalizeSecondaryConnectionMode("No");
            Assert.AreEqual("AllowNoConnections", result);
        }

        [TestMethod]
        public void NormalizeSecondaryConnectionMode_ReadIntentOnly_ReturnsAllowReadIntentConnectionsOnly()
        {
            string result = SetDbaAgReplicaCommand.NormalizeSecondaryConnectionMode("Read-intent only");
            Assert.AreEqual("AllowReadIntentConnectionsOnly", result);
        }

        [TestMethod]
        public void NormalizeSecondaryConnectionMode_Yes_ReturnsAllowAllConnections()
        {
            string result = SetDbaAgReplicaCommand.NormalizeSecondaryConnectionMode("Yes");
            Assert.AreEqual("AllowAllConnections", result);
        }

        [TestMethod]
        public void NormalizeSecondaryConnectionMode_AlreadyNormalized_ReturnsSameValue()
        {
            string result = SetDbaAgReplicaCommand.NormalizeSecondaryConnectionMode("AllowNoConnections");
            Assert.AreEqual("AllowNoConnections", result);
        }

        [TestMethod]
        public void NormalizeSecondaryConnectionMode_Null_ReturnsNull()
        {
            string result = SetDbaAgReplicaCommand.NormalizeSecondaryConnectionMode(null);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void NormalizeSecondaryConnectionMode_CaseInsensitive()
        {
            Assert.AreEqual("AllowNoConnections", SetDbaAgReplicaCommand.NormalizeSecondaryConnectionMode("no"));
            Assert.AreEqual("AllowAllConnections", SetDbaAgReplicaCommand.NormalizeSecondaryConnectionMode("yes"));
            Assert.AreEqual("AllowReadIntentConnectionsOnly", SetDbaAgReplicaCommand.NormalizeSecondaryConnectionMode("read-intent only"));
        }

        #endregion NormalizeSecondaryConnectionMode

        #region IsLoadBalancedRoutingList

        [TestMethod]
        public void IsLoadBalancedRoutingList_SimpleArray_ReturnsFalse()
        {
            object[] simpleList = new object[] { "Server1", "Server2" };
            bool result = SetDbaAgReplicaCommand.IsLoadBalancedRoutingList(simpleList);
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void IsLoadBalancedRoutingList_NestedArray_ReturnsTrue()
        {
            object[] nestedList = new object[] { new object[] { "Server1", "Server2" } };
            bool result = SetDbaAgReplicaCommand.IsLoadBalancedRoutingList(nestedList);
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void IsLoadBalancedRoutingList_Null_ReturnsFalse()
        {
            bool result = SetDbaAgReplicaCommand.IsLoadBalancedRoutingList(null);
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void IsLoadBalancedRoutingList_Empty_ReturnsFalse()
        {
            bool result = SetDbaAgReplicaCommand.IsLoadBalancedRoutingList(new object[0]);
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void IsLoadBalancedRoutingList_StringArray_ReturnsTrue()
        {
            // A string[] inside object[] is still an Array
            object[] list = new object[] { new string[] { "Server1", "Server2" } };
            bool result = SetDbaAgReplicaCommand.IsLoadBalancedRoutingList(list);
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void IsLoadBalancedRoutingList_PSObjectWrapped_ReturnsTrue()
        {
            // When PowerShell wraps arrays in PSObject
            object[] innerArray = new object[] { "Server1", "Server2" };
            PSObject wrapped = PSObject.AsPSObject(innerArray);
            object[] list = new object[] { wrapped };
            bool result = SetDbaAgReplicaCommand.IsLoadBalancedRoutingList(list);
            Assert.IsTrue(result);
        }

        #endregion IsLoadBalancedRoutingList

        #region GetPropertyString

        [TestMethod]
        public void GetPropertyString_ExistingProperty_ReturnsValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "TestReplica"));
            string result = SetDbaAgReplicaCommand.GetPropertyString(obj, "Name");
            Assert.AreEqual("TestReplica", result);
        }

        [TestMethod]
        public void GetPropertyString_MissingProperty_ReturnsNull()
        {
            PSObject obj = new PSObject();
            string result = SetDbaAgReplicaCommand.GetPropertyString(obj, "NonExistent");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyString_NullObject_ReturnsNull()
        {
            string result = SetDbaAgReplicaCommand.GetPropertyString(null, "Name");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyString_NullValue_ReturnsNull()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", null));
            string result = SetDbaAgReplicaCommand.GetPropertyString(obj, "Name");
            Assert.IsNull(result);
        }

        #endregion GetPropertyString

        #region GetParentAgName

        [TestMethod]
        public void GetParentAgName_NullReplica_ReturnsNull()
        {
            string result = SetDbaAgReplicaCommand.GetParentAgName(null);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetParentAgName_WithParent_ReturnsAgName()
        {
            PSObject parent = new PSObject();
            parent.Properties.Add(new PSNoteProperty("Name", "AG01"));

            PSObject replica = new PSObject();
            replica.Properties.Add(new PSNoteProperty("Parent", parent));

            string result = SetDbaAgReplicaCommand.GetParentAgName(replica);
            Assert.AreEqual("AG01", result);
        }

        [TestMethod]
        public void GetParentAgName_NoParent_ReturnsNull()
        {
            PSObject replica = new PSObject();
            string result = SetDbaAgReplicaCommand.GetParentAgName(replica);
            Assert.IsNull(result);
        }

        #endregion GetParentAgName
    }
}
