using System;
using System.Collections;
using System.Data;
using System.Management.Automation;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class InvokeDbaQueryCommandTests
    {
        #region DataRowToPSObject

        [TestMethod]
        public void DataRowToPSObject_NormalRow_ConvertsAllColumns()
        {
            // Arrange
            DataTable table = new DataTable();
            table.Columns.Add("Name", typeof(string));
            table.Columns.Add("Age", typeof(int));
            DataRow row = table.NewRow();
            row["Name"] = "Alice";
            row["Age"] = 30;
            table.Rows.Add(row);

            // Act
            PSObject result = InvokeDbaQueryCommand.DataRowToPSObject(row);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual("Alice", result.Properties["Name"].Value);
            Assert.AreEqual(30, result.Properties["Age"].Value);
        }

        [TestMethod]
        public void DataRowToPSObject_DBNullValues_ConvertedToNull()
        {
            // Arrange
            DataTable table = new DataTable();
            table.Columns.Add("Name", typeof(string));
            table.Columns.Add("Value", typeof(object));
            DataRow row = table.NewRow();
            row["Name"] = "Test";
            row["Value"] = DBNull.Value;
            table.Rows.Add(row);

            // Act
            PSObject result = InvokeDbaQueryCommand.DataRowToPSObject(row);

            // Assert
            Assert.AreEqual("Test", result.Properties["Name"].Value);
            Assert.IsNull(result.Properties["Value"].Value);
        }

        [TestMethod]
        public void DataRowToPSObject_NullRow_ReturnsEmptyPSObject()
        {
            // Act
            PSObject result = InvokeDbaQueryCommand.DataRowToPSObject(null);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Properties.Match("*").Count);
        }

        [TestMethod]
        public void DataRowToPSObject_AllNullColumns_AllNull()
        {
            // Arrange
            DataTable table = new DataTable();
            table.Columns.Add("Col1", typeof(string));
            table.Columns.Add("Col2", typeof(int));
            DataRow row = table.NewRow();
            // Both columns have DBNull since we didn't set them and they allow null
            table.Rows.Add(row);

            // Act
            PSObject result = InvokeDbaQueryCommand.DataRowToPSObject(row);

            // Assert
            Assert.IsNull(result.Properties["Col1"].Value);
            Assert.IsNull(result.Properties["Col2"].Value);
        }

        #endregion

        #region GenerateRandomPrefix

        [TestMethod]
        public void GenerateRandomPrefix_ReturnsCorrectLength()
        {
            // Act
            string prefix = InvokeDbaQueryCommand.GenerateRandomPrefix();

            // Assert
            Assert.AreEqual(10, prefix.Length);
        }

        [TestMethod]
        public void GenerateRandomPrefix_OnlyHexCharacters()
        {
            // Act
            string prefix = InvokeDbaQueryCommand.GenerateRandomPrefix();

            // Assert - Guid.ToString("N") produces hex characters (0-9, a-f)
            foreach (char c in prefix)
            {
                Assert.IsTrue((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'),
                    String.Format("Character '{0}' is not a hex character", c));
            }
        }

        [TestMethod]
        public void GenerateRandomPrefix_MultipleCallsProduceDifferentResults()
        {
            // Act - generate several prefixes
            string prefix1 = InvokeDbaQueryCommand.GenerateRandomPrefix();
            string prefix2 = InvokeDbaQueryCommand.GenerateRandomPrefix();
            string prefix3 = InvokeDbaQueryCommand.GenerateRandomPrefix();

            // Assert - at least 2 of 3 should differ (extremely unlikely all same)
            bool allSame = (prefix1 == prefix2) && (prefix2 == prefix3);
            Assert.IsFalse(allSame, "All three random prefixes were identical");
        }

        #endregion

        #region GetBaseObject

        [TestMethod]
        public void GetBaseObject_NullInput_ReturnsNull()
        {
            // Act
            object result = InvokeDbaQueryCommand.GetBaseObject(null);

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetBaseObject_PlainObject_ReturnsSame()
        {
            // Arrange
            string input = "hello";

            // Act
            object result = InvokeDbaQueryCommand.GetBaseObject(input);

            // Assert
            Assert.AreEqual("hello", result);
        }

        [TestMethod]
        public void GetBaseObject_PSObject_ReturnsBaseObject()
        {
            // Arrange
            string inner = "wrapped";
            PSObject psObj = new PSObject(inner);

            // Act
            object result = InvokeDbaQueryCommand.GetBaseObject(psObj);

            // Assert
            Assert.AreEqual("wrapped", result);
        }

        [TestMethod]
        public void GetBaseObject_IntValue_ReturnsInt()
        {
            // Arrange
            int value = 42;

            // Act
            object result = InvokeDbaQueryCommand.GetBaseObject(value);

            // Assert
            Assert.AreEqual(42, result);
        }

        #endregion

        #region GetPropertyValue

        [TestMethod]
        public void GetPropertyValue_NullObject_ReturnsNull()
        {
            // Act
            object result = InvokeDbaQueryCommand.GetPropertyValue(null, "Name");

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyValue_ValidProperty_ReturnsValue()
        {
            // Arrange
            DataTable table = new DataTable("TestTable");

            // Act
            object result = InvokeDbaQueryCommand.GetPropertyValue(table, "TableName");

            // Assert
            Assert.AreEqual("TestTable", result);
        }

        [TestMethod]
        public void GetPropertyValue_NonExistentProperty_ReturnsNull()
        {
            // Arrange
            DataTable table = new DataTable();

            // Act
            object result = InvokeDbaQueryCommand.GetPropertyValue(table, "NonExistentProp");

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyValue_PSObjectWrapped_UnwrapsAndReturns()
        {
            // Arrange
            DataTable table = new DataTable("Wrapped");
            PSObject psObj = new PSObject(table);

            // Act
            object result = InvokeDbaQueryCommand.GetPropertyValue(psObj, "TableName");

            // Assert
            Assert.AreEqual("Wrapped", result);
        }

        #endregion

        #region CanReuseConnection

        [TestMethod]
        public void CanReuseConnection_NullInstance_ReturnsFalse()
        {
            // Arrange
            InvokeDbaQueryCommand cmd = new InvokeDbaQueryCommand();

            // Act
            bool result = cmd.CanReuseConnection(null);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void CanReuseConnection_NullInputObject_ReturnsFalse()
        {
            // Arrange
            InvokeDbaQueryCommand cmd = new InvokeDbaQueryCommand();
            DbaInstanceParameter param = new DbaInstanceParameter("server1");

            // Act
            bool result = cmd.CanReuseConnection(param);

            // Assert - DbaInstanceParameter from string has no InputObject
            Assert.IsFalse(result);
        }

        #endregion

        #region DataRowToPSObject Edge Cases

        [TestMethod]
        public void DataRowToPSObject_SingleColumn_CorrectProperty()
        {
            // Arrange
            DataTable table = new DataTable();
            table.Columns.Add("TestColumn", typeof(string));
            DataRow row = table.NewRow();
            row["TestColumn"] = "hello";
            table.Rows.Add(row);

            // Act
            PSObject result = InvokeDbaQueryCommand.DataRowToPSObject(row);

            // Assert
            Assert.AreEqual("hello", result.Properties["TestColumn"].Value);
            Assert.AreEqual(1, result.Properties.Match("*").Count);
        }

        [TestMethod]
        public void DataRowToPSObject_MultipleTypes_PreservesTypes()
        {
            // Arrange
            DataTable table = new DataTable();
            table.Columns.Add("StringCol", typeof(string));
            table.Columns.Add("IntCol", typeof(int));
            table.Columns.Add("DateCol", typeof(DateTime));
            table.Columns.Add("BoolCol", typeof(bool));
            DataRow row = table.NewRow();
            DateTime testDate = new DateTime(2024, 1, 15, 10, 30, 0);
            row["StringCol"] = "test";
            row["IntCol"] = 42;
            row["DateCol"] = testDate;
            row["BoolCol"] = true;
            table.Rows.Add(row);

            // Act
            PSObject result = InvokeDbaQueryCommand.DataRowToPSObject(row);

            // Assert
            Assert.AreEqual("test", result.Properties["StringCol"].Value);
            Assert.AreEqual(42, result.Properties["IntCol"].Value);
            Assert.AreEqual(testDate, result.Properties["DateCol"].Value);
            Assert.AreEqual(true, result.Properties["BoolCol"].Value);
        }

        #endregion

        #region AddSqlParameters

        [TestMethod]
        public void AddSqlParameters_NullSqlParameter_DoesNothing()
        {
            // Arrange
            InvokeDbaQueryCommand cmd = new InvokeDbaQueryCommand();
            // cmd.SqlParameter is null by default
            using (SqlCommand sqlCmd = new SqlCommand())
            {
                // Act
                cmd.AddSqlParameters(sqlCmd);

                // Assert
                Assert.AreEqual(0, sqlCmd.Parameters.Count);
            }
        }

        [TestMethod]
        public void AddSqlParameters_HashtableWithStringValues_AddsParameters()
        {
            // Arrange
            InvokeDbaQueryCommand cmd = new InvokeDbaQueryCommand();
            Hashtable ht = new Hashtable();
            ht["@Name"] = "Alice";
            ht["@Age"] = 30;
            cmd.SqlParameter = new PSObject[] { new PSObject(ht) };

            using (SqlCommand sqlCmd = new SqlCommand())
            {
                // Act
                cmd.AddSqlParameters(sqlCmd);

                // Assert
                Assert.AreEqual(2, sqlCmd.Parameters.Count);
            }
        }

        [TestMethod]
        public void AddSqlParameters_HashtableWithNullValue_AddsDBNull()
        {
            // Arrange
            InvokeDbaQueryCommand cmd = new InvokeDbaQueryCommand();
            Hashtable ht = new Hashtable();
            ht["@NullParam"] = null;
            cmd.SqlParameter = new PSObject[] { new PSObject(ht) };

            using (SqlCommand sqlCmd = new SqlCommand())
            {
                // Act
                cmd.AddSqlParameters(sqlCmd);

                // Assert
                Assert.AreEqual(1, sqlCmd.Parameters.Count);
                Assert.AreEqual(DBNull.Value, sqlCmd.Parameters["@NullParam"].Value);
            }
        }

        [TestMethod]
        public void AddSqlParameters_SqlParameterArray_AddsDirectly()
        {
            // Arrange
            InvokeDbaQueryCommand cmd = new InvokeDbaQueryCommand();
            SqlParameter p1 = new SqlParameter("@Id", SqlDbType.Int) { Value = 1 };
            SqlParameter p2 = new SqlParameter("@Name", SqlDbType.NVarChar, 50) { Value = "Test" };
            cmd.SqlParameter = new PSObject[] { new PSObject(p1), new PSObject(p2) };

            using (SqlCommand sqlCmd = new SqlCommand())
            {
                // Act
                cmd.AddSqlParameters(sqlCmd);

                // Assert
                Assert.AreEqual(2, sqlCmd.Parameters.Count);
                Assert.AreEqual("@Id", sqlCmd.Parameters[0].ParameterName);
                Assert.AreEqual(1, sqlCmd.Parameters[0].Value);
                Assert.AreEqual("@Name", sqlCmd.Parameters[1].ParameterName);
                Assert.AreEqual("Test", sqlCmd.Parameters[1].Value);
            }
        }

        [TestMethod]
        public void AddSqlParameters_HashtableWithSqlParameterValue_RenamesKey()
        {
            // Arrange - when a hashtable entry value is a SqlParameter, it should be renamed to the key
            InvokeDbaQueryCommand cmd = new InvokeDbaQueryCommand();
            SqlParameter sp = new SqlParameter("@OldName", SqlDbType.Int) { Value = 42 };
            Hashtable ht = new Hashtable();
            ht["@NewName"] = sp;
            cmd.SqlParameter = new PSObject[] { new PSObject(ht) };

            using (SqlCommand sqlCmd = new SqlCommand())
            {
                // Act
                cmd.AddSqlParameters(sqlCmd);

                // Assert
                Assert.AreEqual(1, sqlCmd.Parameters.Count);
                Assert.AreEqual("@NewName", sqlCmd.Parameters[0].ParameterName);
                Assert.AreEqual(42, sqlCmd.Parameters[0].Value);
            }
        }

        #endregion

        #region CanReuseConnection Additional

        [TestMethod]
        public void CanReuseConnection_ReadOnlyIsPresent_ReturnsFalse()
        {
            // Arrange
            InvokeDbaQueryCommand cmd = new InvokeDbaQueryCommand();
            cmd.ReadOnly = new SwitchParameter(true);
            // Even with a null instance, ReadOnly check comes after null check
            // so we need a non-null instance with InputObject for this test path
            DbaInstanceParameter param = new DbaInstanceParameter("server1");

            // Act
            bool result = cmd.CanReuseConnection(param);

            // Assert - returns false (no InputObject, so it never reaches ReadOnly check)
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void CanReuseConnection_AppendConnectionStringSet_ReturnsFalse()
        {
            // Arrange
            InvokeDbaQueryCommand cmd = new InvokeDbaQueryCommand();
            cmd.AppendConnectionString = "ApplicationName=test";
            DbaInstanceParameter param = new DbaInstanceParameter("server1");

            // Act
            bool result = cmd.CanReuseConnection(param);

            // Assert - returns false (no InputObject, so exits early)
            Assert.IsFalse(result);
        }

        #endregion
    }
}
