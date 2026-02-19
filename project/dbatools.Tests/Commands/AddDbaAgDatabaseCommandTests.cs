using System;
using System.Collections;
using System.Collections.Generic;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class AddDbaAgDatabaseCommandTests
    {
        #region GetPSPropertyString

        [TestMethod]
        public void GetPSPropertyString_ExistingProperty_ReturnsValue()
        {
            // Arrange
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "TestDB"));

            // Act
            string result = AddDbaAgDatabaseCommand.GetPSPropertyString(obj, "Name");

            // Assert
            Assert.AreEqual("TestDB", result);
        }

        [TestMethod]
        public void GetPSPropertyString_NullObject_ReturnsNull()
        {
            // Act
            string result = AddDbaAgDatabaseCommand.GetPSPropertyString(null, "Name");

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPSPropertyString_MissingProperty_ReturnsNull()
        {
            // Arrange
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "TestDB"));

            // Act
            string result = AddDbaAgDatabaseCommand.GetPSPropertyString(obj, "DoesNotExist");

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
            string result = AddDbaAgDatabaseCommand.GetPSPropertyString(obj, "Name");

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPSPropertyString_IntProperty_ReturnsStringRepresentation()
        {
            // Arrange
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Version", 42));

            // Act
            string result = AddDbaAgDatabaseCommand.GetPSPropertyString(obj, "Version");

            // Assert
            Assert.AreEqual("42", result);
        }

        #endregion GetPSPropertyString

        #region GetPSPropertyValue

        [TestMethod]
        public void GetPSPropertyValue_ExistingProperty_ReturnsRawValue()
        {
            // Arrange
            PSObject obj = new PSObject();
            Hashtable ht = new Hashtable();
            ht["key1"] = "value1";
            obj.Properties.Add(new PSNoteProperty("Data", ht));

            // Act
            object result = AddDbaAgDatabaseCommand.GetPSPropertyValue(obj, "Data");

            // Assert
            Assert.IsNotNull(result);
            Assert.IsInstanceOfType(result, typeof(Hashtable));
        }

        [TestMethod]
        public void GetPSPropertyValue_NullObject_ReturnsNull()
        {
            // Act
            object result = AddDbaAgDatabaseCommand.GetPSPropertyValue(null, "Name");

            // Assert
            Assert.IsNull(result);
        }

        #endregion GetPSPropertyValue

        #region GetBoolProperty

        [TestMethod]
        public void GetBoolProperty_TrueValue_ReturnsTrue()
        {
            // Arrange
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("IsJoined", true));

            // Act
            bool result = AddDbaAgDatabaseCommand.GetBoolProperty(obj, "IsJoined");

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void GetBoolProperty_FalseValue_ReturnsFalse()
        {
            // Arrange
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("IsJoined", false));

            // Act
            bool result = AddDbaAgDatabaseCommand.GetBoolProperty(obj, "IsJoined");

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void GetBoolProperty_StringTrueValue_ReturnsTrue()
        {
            // Arrange
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("IsJoined", "True"));

            // Act
            bool result = AddDbaAgDatabaseCommand.GetBoolProperty(obj, "IsJoined");

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void GetBoolProperty_MissingProperty_ReturnsFalse()
        {
            // Arrange
            PSObject obj = new PSObject();

            // Act
            bool result = AddDbaAgDatabaseCommand.GetBoolProperty(obj, "IsJoined");

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void GetBoolProperty_NullObject_ReturnsFalse()
        {
            // Act
            bool result = AddDbaAgDatabaseCommand.GetBoolProperty(null, "IsJoined");

            // Assert
            Assert.IsFalse(result);
        }

        #endregion GetBoolProperty

        #region GetPSPropertyLong

        [TestMethod]
        public void GetPSPropertyLong_LongValue_ReturnsValue()
        {
            // Arrange
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("transferred_size_bytes", 1073741824L));

            // Act
            long result = AddDbaAgDatabaseCommand.GetPSPropertyLong(obj, "transferred_size_bytes");

            // Assert
            Assert.AreEqual(1073741824L, result);
        }

        [TestMethod]
        public void GetPSPropertyLong_IntValue_ReturnsValue()
        {
            // Arrange
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("count", 42));

            // Act
            long result = AddDbaAgDatabaseCommand.GetPSPropertyLong(obj, "count");

            // Assert
            Assert.AreEqual(42L, result);
        }

        [TestMethod]
        public void GetPSPropertyLong_MissingProperty_ReturnsZero()
        {
            // Arrange
            PSObject obj = new PSObject();

            // Act
            long result = AddDbaAgDatabaseCommand.GetPSPropertyLong(obj, "missing");

            // Assert
            Assert.AreEqual(0L, result);
        }

        [TestMethod]
        public void GetPSPropertyLong_StringValue_ParsesCorrectly()
        {
            // Arrange
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("size", "999999"));

            // Act
            long result = AddDbaAgDatabaseCommand.GetPSPropertyLong(obj, "size");

            // Assert
            Assert.AreEqual(999999L, result);
        }

        #endregion GetPSPropertyLong

        #region UnwrapHashtable

        [TestMethod]
        public void UnwrapHashtable_DirectHashtable_ReturnsSameObject()
        {
            // Arrange
            Hashtable ht = new Hashtable();
            ht["key"] = "value";

            // Act
            Hashtable result = AddDbaAgDatabaseCommand.UnwrapHashtable(ht);

            // Assert
            Assert.AreSame(ht, result);
        }

        [TestMethod]
        public void UnwrapHashtable_PSObjectWrapped_ReturnsInnerHashtable()
        {
            // Arrange
            Hashtable ht = new Hashtable();
            ht["key"] = "value";
            PSObject psObj = PSObject.AsPSObject(ht);

            // Act
            Hashtable result = AddDbaAgDatabaseCommand.UnwrapHashtable(psObj);

            // Assert
            Assert.AreSame(ht, result);
        }

        [TestMethod]
        public void UnwrapHashtable_Null_ReturnsNull()
        {
            // Act
            Hashtable result = AddDbaAgDatabaseCommand.UnwrapHashtable(null);

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void UnwrapHashtable_NonHashtable_ReturnsNull()
        {
            // Act
            Hashtable result = AddDbaAgDatabaseCommand.UnwrapHashtable("not a hashtable");

            // Assert
            Assert.IsNull(result);
        }

        #endregion UnwrapHashtable

        #region UnwrapObjectArray

        [TestMethod]
        public void UnwrapObjectArray_NonEmptyArray_ReturnsArray()
        {
            // Arrange
            object[] arr = new object[] { "item1", "item2" };

            // Act
            object[] result = AddDbaAgDatabaseCommand.UnwrapObjectArray(arr);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Length);
        }

        [TestMethod]
        public void UnwrapObjectArray_EmptyArray_ReturnsNull()
        {
            // Arrange
            object[] arr = new object[0];

            // Act - empty array is treated as "no backups" (matches PS1 falsy check)
            object[] result = AddDbaAgDatabaseCommand.UnwrapObjectArray(arr);

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void UnwrapObjectArray_Null_ReturnsNull()
        {
            // Act
            object[] result = AddDbaAgDatabaseCommand.UnwrapObjectArray(null);

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void UnwrapObjectArray_PSObjectWrapped_ReturnsInnerArray()
        {
            // Arrange
            object[] arr = new object[] { "backup1" };
            PSObject psObj = PSObject.AsPSObject(arr);

            // Act
            object[] result = AddDbaAgDatabaseCommand.UnwrapObjectArray(psObj);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.Length);
        }

        #endregion UnwrapObjectArray

        #region GetHashtableKeys

        [TestMethod]
        public void GetHashtableKeys_WithKeys_ReturnsList()
        {
            // Arrange
            Hashtable ht = new Hashtable();
            ht["replica1"] = "server1";
            ht["replica2"] = "server2";

            // Act
            List<string> keys = AddDbaAgDatabaseCommand.GetHashtableKeys(ht);

            // Assert
            Assert.AreEqual(2, keys.Count);
            CollectionAssert.Contains(keys, "replica1");
            CollectionAssert.Contains(keys, "replica2");
        }

        [TestMethod]
        public void GetHashtableKeys_Empty_ReturnsEmptyList()
        {
            // Arrange
            Hashtable ht = new Hashtable();

            // Act
            List<string> keys = AddDbaAgDatabaseCommand.GetHashtableKeys(ht);

            // Assert
            Assert.AreEqual(0, keys.Count);
        }

        [TestMethod]
        public void GetHashtableKeys_Null_ReturnsEmptyList()
        {
            // Act
            List<string> keys = AddDbaAgDatabaseCommand.GetHashtableKeys(null);

            // Assert
            Assert.AreEqual(0, keys.Count);
        }

        #endregion GetHashtableKeys

        #region GetHashtableString

        [TestMethod]
        public void GetHashtableString_ExistingKey_ReturnsValue()
        {
            // Arrange
            Hashtable ht = new Hashtable();
            ht["SeedingMode"] = "Automatic";

            // Act
            string result = AddDbaAgDatabaseCommand.GetHashtableString(ht, "SeedingMode");

            // Assert
            Assert.AreEqual("Automatic", result);
        }

        [TestMethod]
        public void GetHashtableString_MissingKey_ReturnsNull()
        {
            // Arrange
            Hashtable ht = new Hashtable();

            // Act
            string result = AddDbaAgDatabaseCommand.GetHashtableString(ht, "DoesNotExist");

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetHashtableString_NullHashtable_ReturnsNull()
        {
            // Act
            string result = AddDbaAgDatabaseCommand.GetHashtableString(null, "key");

            // Assert
            Assert.IsNull(result);
        }

        #endregion GetHashtableString

        #region IsNonNullNonDbNull

        [TestMethod]
        public void IsNonNullNonDbNull_ValidString_ReturnsTrue()
        {
            // Act
            bool result = AddDbaAgDatabaseCommand.IsNonNullNonDbNull("some error message");

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void IsNonNullNonDbNull_NullObject_ReturnsFalse()
        {
            // Act
            bool result = AddDbaAgDatabaseCommand.IsNonNullNonDbNull(null);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void IsNonNullNonDbNull_EmptyString_ReturnsFalse()
        {
            // Act
            bool result = AddDbaAgDatabaseCommand.IsNonNullNonDbNull("");

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void IsNonNullNonDbNull_DbNull_ReturnsFalse()
        {
            // Act
            bool result = AddDbaAgDatabaseCommand.IsNonNullNonDbNull(DBNull.Value);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void IsNonNullNonDbNull_IntegerValue_ReturnsTrue()
        {
            // Act
            bool result = AddDbaAgDatabaseCommand.IsNonNullNonDbNull(42);

            // Assert
            Assert.IsTrue(result);
        }

        #endregion IsNonNullNonDbNull
    }
}
