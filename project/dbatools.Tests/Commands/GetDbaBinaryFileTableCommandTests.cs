using System;
using System.Collections.Generic;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class GetDbaBinaryFileTableCommandTests
    {
        #region GetPropertyValue
        [TestMethod]
        public void GetPropertyValue_ReturnsValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Columns", "testvalue"));

            object result = GetDbaBinaryFileTableCommand.GetPropertyValue(obj, "Columns");

            Assert.AreEqual("testvalue", result);
        }

        [TestMethod]
        public void GetPropertyValue_NullObjectReturnsNull()
        {
            object result = GetDbaBinaryFileTableCommand.GetPropertyValue(null, "Columns");

            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyValue_MissingPropertyReturnsNull()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "test"));

            object result = GetDbaBinaryFileTableCommand.GetPropertyValue(obj, "Columns");

            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyValue_NullPropertyValueReturnsNull()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Columns", null));

            object result = GetDbaBinaryFileTableCommand.GetPropertyValue(obj, "Columns");

            Assert.IsNull(result);
        }
        #endregion

        #region GetParentName
        [TestMethod]
        public void GetParentName_ReturnsParentName()
        {
            PSObject parent = new PSObject();
            parent.Properties.Add(new PSNoteProperty("Name", "TestDB"));

            PSObject tbl = new PSObject();
            tbl.Properties.Add(new PSNoteProperty("Parent", parent));

            string result = GetDbaBinaryFileTableCommand.GetParentName(tbl);

            Assert.AreEqual("TestDB", result);
        }

        [TestMethod]
        public void GetParentName_NoParentReturnsUnknown()
        {
            PSObject tbl = new PSObject();

            string result = GetDbaBinaryFileTableCommand.GetParentName(tbl);

            Assert.AreEqual("<Unknown>", result);
        }

        [TestMethod]
        public void GetParentName_ParentWithoutNameReturnsUnknown()
        {
            PSObject parent = new PSObject();

            PSObject tbl = new PSObject();
            tbl.Properties.Add(new PSNoteProperty("Parent", parent));

            string result = GetDbaBinaryFileTableCommand.GetParentName(tbl);

            Assert.AreEqual("<Unknown>", result);
        }
        #endregion

        #region GetGrandparentName
        [TestMethod]
        public void GetGrandparentName_ReturnsServerName()
        {
            PSObject server = new PSObject();
            server.Properties.Add(new PSNoteProperty("Name", "SQL01"));

            PSObject db = new PSObject();
            db.Properties.Add(new PSNoteProperty("Name", "TestDB"));
            db.Properties.Add(new PSNoteProperty("Parent", server));

            PSObject tbl = new PSObject();
            tbl.Properties.Add(new PSNoteProperty("Parent", db));

            string result = GetDbaBinaryFileTableCommand.GetGrandparentName(tbl);

            Assert.AreEqual("SQL01", result);
        }

        [TestMethod]
        public void GetGrandparentName_NoParentReturnsUnknown()
        {
            PSObject tbl = new PSObject();

            string result = GetDbaBinaryFileTableCommand.GetGrandparentName(tbl);

            Assert.AreEqual("<Unknown>", result);
        }

        [TestMethod]
        public void GetGrandparentName_NoGrandparentReturnsUnknown()
        {
            PSObject db = new PSObject();
            db.Properties.Add(new PSNoteProperty("Name", "TestDB"));

            PSObject tbl = new PSObject();
            tbl.Properties.Add(new PSNoteProperty("Parent", db));

            string result = GetDbaBinaryFileTableCommand.GetGrandparentName(tbl);

            Assert.AreEqual("<Unknown>", result);
        }
        #endregion

        #region AddOrSetProperty
        [TestMethod]
        public void AddOrSetProperty_AddsNewProperty()
        {
            PSObject obj = new PSObject();

            GetDbaBinaryFileTableCommand.AddOrSetProperty(obj, "BinaryColumn", "TheFile");

            Assert.AreEqual("TheFile", obj.Properties["BinaryColumn"].Value);
        }

        [TestMethod]
        public void AddOrSetProperty_UpdatesExistingProperty()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("BinaryColumn", "OldValue"));

            GetDbaBinaryFileTableCommand.AddOrSetProperty(obj, "BinaryColumn", "NewValue");

            Assert.AreEqual("NewValue", obj.Properties["BinaryColumn"].Value);
        }

        [TestMethod]
        public void AddOrSetProperty_NullObjectDoesNotThrow()
        {
            GetDbaBinaryFileTableCommand.AddOrSetProperty(null, "Name", "value");
            // Should not throw
        }

        [TestMethod]
        public void AddOrSetProperty_ArrayValueIsStored()
        {
            PSObject obj = new PSObject();
            string[] array = new string[] { "Col1", "Col2" };

            GetDbaBinaryFileTableCommand.AddOrSetProperty(obj, "BinaryColumn", array);

            Assert.IsInstanceOfType(obj.Properties["BinaryColumn"].Value, typeof(string[]));
            CollectionAssert.AreEqual(array, (string[])obj.Properties["BinaryColumn"].Value);
        }

        [TestMethod]
        public void AddOrSetProperty_NullValueIsStored()
        {
            PSObject obj = new PSObject();

            GetDbaBinaryFileTableCommand.AddOrSetProperty(obj, "FileNameColumn", null);

            Assert.IsNull(obj.Properties["FileNameColumn"].Value);
        }
        #endregion

        #region SetDefaultDisplayPropertySet
        [TestMethod]
        public void SetDefaultDisplayPropertySet_AddsStandardMembers()
        {
            PSObject obj = new PSObject();
            string[] props = new string[] { "Name", "BinaryColumn" };

            GetDbaBinaryFileTableCommand.SetDefaultDisplayPropertySet(obj, props);

            PSMemberInfo member = obj.Members["PSStandardMembers"];
            Assert.IsNotNull(member);
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_NullObjectDoesNotThrow()
        {
            GetDbaBinaryFileTableCommand.SetDefaultDisplayPropertySet(null, new string[] { "Name" });
            // Should not throw
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_NullPropertiesDoesNotThrow()
        {
            PSObject obj = new PSObject();
            GetDbaBinaryFileTableCommand.SetDefaultDisplayPropertySet(obj, null);
            // Should not throw
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_OverwritesExisting()
        {
            PSObject obj = new PSObject();
            string[] props1 = new string[] { "Name" };
            string[] props2 = new string[] { "Name", "BinaryColumn", "FileNameColumn" };

            GetDbaBinaryFileTableCommand.SetDefaultDisplayPropertySet(obj, props1);
            GetDbaBinaryFileTableCommand.SetDefaultDisplayPropertySet(obj, props2);

            PSMemberInfo member = obj.Members["PSStandardMembers"];
            Assert.IsNotNull(member);
        }
        #endregion

        #region IsBinaryDataType
        [TestMethod]
        public void IsBinaryDataType_Binary_ReturnsTrue()
        {
            Assert.IsTrue(GetDbaBinaryFileTableCommand.IsBinaryDataType("binary"));
        }

        [TestMethod]
        public void IsBinaryDataType_Varbinary_ReturnsTrue()
        {
            Assert.IsTrue(GetDbaBinaryFileTableCommand.IsBinaryDataType("varbinary"));
        }

        [TestMethod]
        public void IsBinaryDataType_Image_ReturnsTrue()
        {
            Assert.IsTrue(GetDbaBinaryFileTableCommand.IsBinaryDataType("image"));
        }

        [TestMethod]
        public void IsBinaryDataType_ImageCaseInsensitive_ReturnsTrue()
        {
            Assert.IsTrue(GetDbaBinaryFileTableCommand.IsBinaryDataType("IMAGE"));
        }

        [TestMethod]
        public void IsBinaryDataType_Nvarchar_ReturnsFalse()
        {
            Assert.IsFalse(GetDbaBinaryFileTableCommand.IsBinaryDataType("nvarchar"));
        }

        [TestMethod]
        public void IsBinaryDataType_Int_ReturnsFalse()
        {
            Assert.IsFalse(GetDbaBinaryFileTableCommand.IsBinaryDataType("int"));
        }

        [TestMethod]
        public void IsBinaryDataType_Null_ReturnsFalse()
        {
            Assert.IsFalse(GetDbaBinaryFileTableCommand.IsBinaryDataType(null));
        }

        [TestMethod]
        public void IsBinaryDataType_Empty_ReturnsFalse()
        {
            Assert.IsFalse(GetDbaBinaryFileTableCommand.IsBinaryDataType(""));
        }
        #endregion

        #region IsFileNameColumn
        [TestMethod]
        public void IsFileNameColumn_FileName_ReturnsTrue()
        {
            Assert.IsTrue(GetDbaBinaryFileTableCommand.IsFileNameColumn("FileName"));
        }

        [TestMethod]
        public void IsFileNameColumn_DocumentName_ReturnsTrue()
        {
            Assert.IsTrue(GetDbaBinaryFileTableCommand.IsFileNameColumn("DocumentName"));
        }

        [TestMethod]
        public void IsFileNameColumn_Name_ReturnsTrue()
        {
            Assert.IsTrue(GetDbaBinaryFileTableCommand.IsFileNameColumn("Name"));
        }

        [TestMethod]
        public void IsFileNameColumn_NAME_CaseInsensitive_ReturnsTrue()
        {
            Assert.IsTrue(GetDbaBinaryFileTableCommand.IsFileNameColumn("NAME"));
        }

        [TestMethod]
        public void IsFileNameColumn_Description_ReturnsFalse()
        {
            Assert.IsFalse(GetDbaBinaryFileTableCommand.IsFileNameColumn("Description"));
        }

        [TestMethod]
        public void IsFileNameColumn_ID_ReturnsFalse()
        {
            Assert.IsFalse(GetDbaBinaryFileTableCommand.IsFileNameColumn("ID"));
        }

        [TestMethod]
        public void IsFileNameColumn_Null_ReturnsFalse()
        {
            Assert.IsFalse(GetDbaBinaryFileTableCommand.IsFileNameColumn(null));
        }

        [TestMethod]
        public void IsFileNameColumn_Empty_ReturnsFalse()
        {
            Assert.IsFalse(GetDbaBinaryFileTableCommand.IsFileNameColumn(""));
        }
        #endregion

        #region BuildColumnValue
        [TestMethod]
        public void BuildColumnValue_NullList_ReturnsNull()
        {
            Assert.IsNull(GetDbaBinaryFileTableCommand.BuildColumnValue(null));
        }

        [TestMethod]
        public void BuildColumnValue_EmptyList_ReturnsNull()
        {
            var list = new List<string>();
            Assert.IsNull(GetDbaBinaryFileTableCommand.BuildColumnValue(list));
        }

        [TestMethod]
        public void BuildColumnValue_SingleItem_ReturnsString()
        {
            var list = new List<string> { "TheFile" };
            object result = GetDbaBinaryFileTableCommand.BuildColumnValue(list);

            Assert.IsInstanceOfType(result, typeof(string));
            Assert.AreEqual("TheFile", result);
        }

        [TestMethod]
        public void BuildColumnValue_MultipleItems_ReturnsStringArray()
        {
            var list = new List<string> { "Col1", "Col2", "Col3" };
            object result = GetDbaBinaryFileTableCommand.BuildColumnValue(list);

            Assert.IsInstanceOfType(result, typeof(string[]));
            CollectionAssert.AreEqual(new string[] { "Col1", "Col2", "Col3" }, (string[])result);
        }
        #endregion
    }
}
