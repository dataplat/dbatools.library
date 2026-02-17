using System;
using System.Collections.Generic;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class GetDbaAgReplicaCommandTests
    {
        #region BuildReplicaFilter
        [TestMethod]
        public void BuildReplicaFilter_WithValidNames_ReturnsHashSet()
        {
            // Arrange
            string[] replicas = new string[] { "replica1", "replica2" };

            // Act
            HashSet<string> result = GetDbaAgReplicaCommand.BuildReplicaFilter(replicas);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Count);
            Assert.IsTrue(result.Contains("replica1"));
            Assert.IsTrue(result.Contains("replica2"));
        }

        [TestMethod]
        public void BuildReplicaFilter_IsCaseInsensitive()
        {
            // Arrange
            string[] replicas = new string[] { "Replica1" };

            // Act
            HashSet<string> result = GetDbaAgReplicaCommand.BuildReplicaFilter(replicas);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Contains("replica1"));
            Assert.IsTrue(result.Contains("REPLICA1"));
            Assert.IsTrue(result.Contains("Replica1"));
        }

        [TestMethod]
        public void BuildReplicaFilter_NullInput_ReturnsNull()
        {
            // Act
            HashSet<string> result = GetDbaAgReplicaCommand.BuildReplicaFilter(null);

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void BuildReplicaFilter_EmptyArray_ReturnsNull()
        {
            // Act
            HashSet<string> result = GetDbaAgReplicaCommand.BuildReplicaFilter(new string[0]);

            // Assert
            Assert.IsNull(result);
        }
        #endregion

        #region GetPropertyString
        [TestMethod]
        public void GetPropertyString_ExistingProperty_ReturnsValue()
        {
            // Arrange
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "TestReplica"));

            // Act
            string result = GetDbaAgReplicaCommand.GetPropertyString(obj, "Name");

            // Assert
            Assert.AreEqual("TestReplica", result);
        }

        [TestMethod]
        public void GetPropertyString_NonExistentProperty_ReturnsNull()
        {
            // Arrange
            PSObject obj = new PSObject();

            // Act
            string result = GetDbaAgReplicaCommand.GetPropertyString(obj, "DoesNotExist");

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyString_NullObject_ReturnsNull()
        {
            // Act
            string result = GetDbaAgReplicaCommand.GetPropertyString(null, "Name");

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyString_NullPropertyValue_ReturnsNull()
        {
            // Arrange
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", null));

            // Act
            string result = GetDbaAgReplicaCommand.GetPropertyString(obj, "Name");

            // Assert
            Assert.IsNull(result);
        }
        #endregion

        #region GetPropertyObject
        [TestMethod]
        public void GetPropertyObject_ExistingProperty_ReturnsPSObject()
        {
            // Arrange
            PSObject child = new PSObject();
            child.Properties.Add(new PSNoteProperty("ChildProp", "ChildValue"));

            PSObject parent = new PSObject();
            parent.Properties.Add(new PSNoteProperty("Child", child));

            // Act
            PSObject result = GetDbaAgReplicaCommand.GetPropertyObject(parent, "Child");

            // Assert
            Assert.IsNotNull(result);
        }

        [TestMethod]
        public void GetPropertyObject_NullObject_ReturnsNull()
        {
            // Act
            PSObject result = GetDbaAgReplicaCommand.GetPropertyObject(null, "Prop");

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyObject_NonExistentProperty_ReturnsNull()
        {
            // Arrange
            PSObject obj = new PSObject();

            // Act
            PSObject result = GetDbaAgReplicaCommand.GetPropertyObject(obj, "DoesNotExist");

            // Assert
            Assert.IsNull(result);
        }
        #endregion

        #region AddOrSetProperty
        [TestMethod]
        public void AddOrSetProperty_NewProperty_AddsIt()
        {
            // Arrange
            PSObject obj = new PSObject();

            // Act
            GetDbaAgReplicaCommand.AddOrSetProperty(obj, "TestProp", "TestValue");

            // Assert
            Assert.AreEqual("TestValue", obj.Properties["TestProp"].Value);
        }

        [TestMethod]
        public void AddOrSetProperty_ExistingProperty_UpdatesIt()
        {
            // Arrange
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("TestProp", "OldValue"));

            // Act
            GetDbaAgReplicaCommand.AddOrSetProperty(obj, "TestProp", "NewValue");

            // Assert
            Assert.AreEqual("NewValue", obj.Properties["TestProp"].Value);
        }

        [TestMethod]
        public void AddOrSetProperty_NullObject_DoesNotThrow()
        {
            // Act - should not throw
            GetDbaAgReplicaCommand.AddOrSetProperty(null, "Prop", "Value");
        }
        #endregion

        #region SetDefaultDisplayPropertySet
        [TestMethod]
        public void SetDefaultDisplayPropertySet_ValidInput_AddsPSStandardMembers()
        {
            // Arrange
            PSObject obj = new PSObject();
            string[] props = new string[] { "Name", "Role", "ConnectionState" };

            // Act
            GetDbaAgReplicaCommand.SetDefaultDisplayPropertySet(obj, props);

            // Assert
            PSMemberInfo member = obj.Members["PSStandardMembers"];
            Assert.IsNotNull(member);
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_NullObject_DoesNotThrow()
        {
            // Act - should not throw
            GetDbaAgReplicaCommand.SetDefaultDisplayPropertySet(null, new string[] { "Name" });
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_NullProperties_DoesNotThrow()
        {
            // Arrange
            PSObject obj = new PSObject();

            // Act - should not throw
            GetDbaAgReplicaCommand.SetDefaultDisplayPropertySet(obj, null);
        }
        #endregion
    }
}
