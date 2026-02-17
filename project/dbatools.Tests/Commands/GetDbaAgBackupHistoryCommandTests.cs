using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class GetDbaAgBackupHistoryCommandTests
    {
        #region SetAvailabilityGroupName
        [TestMethod]
        public void SetAvailabilityGroupName_SetsPropertyOnNewObject()
        {
            PSObject obj = new PSObject();
            GetDbaAgBackupHistoryCommand.SetAvailabilityGroupName(obj, "AG01");

            Assert.AreEqual("AG01", obj.Properties["AvailabilityGroupName"].Value);
        }

        [TestMethod]
        public void SetAvailabilityGroupName_OverwritesExistingProperty()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("AvailabilityGroupName", "OldAG"));

            GetDbaAgBackupHistoryCommand.SetAvailabilityGroupName(obj, "NewAG");

            Assert.AreEqual("NewAG", obj.Properties["AvailabilityGroupName"].Value);
        }

        [TestMethod]
        public void SetAvailabilityGroupName_NullObjectDoesNotThrow()
        {
            // Should not throw
            GetDbaAgBackupHistoryCommand.SetAvailabilityGroupName(null, "AG01");
        }

        [TestMethod]
        public void SetAvailabilityGroupName_NullAgNameSetsNull()
        {
            PSObject obj = new PSObject();
            GetDbaAgBackupHistoryCommand.SetAvailabilityGroupName(obj, null);

            Assert.IsNull(obj.Properties["AvailabilityGroupName"].Value);
        }
        #endregion

        #region GetDatabaseName
        [TestMethod]
        public void GetDatabaseName_ReturnsDatabaseProperty()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Database", "TestDB"));

            string result = GetDbaAgBackupHistoryCommand.GetDatabaseName(obj);

            Assert.AreEqual("TestDB", result);
        }

        [TestMethod]
        public void GetDatabaseName_NullObjectReturnsNull()
        {
            string result = GetDbaAgBackupHistoryCommand.GetDatabaseName(null);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetDatabaseName_MissingPropertyReturnsNull()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("SqlInstance", "sql01"));

            string result = GetDbaAgBackupHistoryCommand.GetDatabaseName(obj);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetDatabaseName_NullValueReturnsNull()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Database", null));

            string result = GetDbaAgBackupHistoryCommand.GetDatabaseName(obj);
            Assert.IsNull(result);
        }
        #endregion

        #region GetComparableProperty
        [TestMethod]
        public void GetComparableProperty_ReturnsComparableValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("FirstLsn", 12345L));

            IComparable result = GetDbaAgBackupHistoryCommand.GetComparableProperty(obj, "FirstLsn");

            Assert.IsNotNull(result);
            Assert.AreEqual(12345L, result);
        }

        [TestMethod]
        public void GetComparableProperty_NullObjectReturnsNull()
        {
            IComparable result = GetDbaAgBackupHistoryCommand.GetComparableProperty(null, "FirstLsn");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetComparableProperty_MissingPropertyReturnsNull()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Database", "TestDB"));

            IComparable result = GetDbaAgBackupHistoryCommand.GetComparableProperty(obj, "FirstLsn");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetComparableProperty_NonComparableValueReturnsNull()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Data", new object()));

            IComparable result = GetDbaAgBackupHistoryCommand.GetComparableProperty(obj, "Data");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetComparableProperty_StringValueIsComparable()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("LastLsn", "99999"));

            IComparable result = GetDbaAgBackupHistoryCommand.GetComparableProperty(obj, "LastLsn");

            Assert.IsNotNull(result);
            Assert.AreEqual("99999", result);
        }
        #endregion

        #region ConvertToStringArray
        [TestMethod]
        public void ConvertToStringArray_ConvertsObjectArray()
        {
            object[] input = new object[] { "sql01", "sql02", "sqldr" };

            string[] result = GetDbaAgBackupHistoryCommand.ConvertToStringArray(input);

            Assert.AreEqual(3, result.Length);
            Assert.AreEqual("sql01", result[0]);
            Assert.AreEqual("sql02", result[1]);
            Assert.AreEqual("sqldr", result[2]);
        }

        [TestMethod]
        public void ConvertToStringArray_NullInputReturnsEmptyArray()
        {
            string[] result = GetDbaAgBackupHistoryCommand.ConvertToStringArray(null);

            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Length);
        }

        [TestMethod]
        public void ConvertToStringArray_NullElementBecomesEmptyString()
        {
            object[] input = new object[] { "sql01", null, "sql02" };

            string[] result = GetDbaAgBackupHistoryCommand.ConvertToStringArray(input);

            Assert.AreEqual(3, result.Length);
            Assert.AreEqual("sql01", result[0]);
            Assert.AreEqual(String.Empty, result[1]);
            Assert.AreEqual("sql02", result[2]);
        }

        [TestMethod]
        public void ConvertToStringArray_EmptyArrayReturnsEmptyArray()
        {
            object[] input = new object[0];

            string[] result = GetDbaAgBackupHistoryCommand.ConvertToStringArray(input);

            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Length);
        }

        [TestMethod]
        public void ConvertToStringArray_IntegersConvertToStrings()
        {
            object[] input = new object[] { 1, 2, 3 };

            string[] result = GetDbaAgBackupHistoryCommand.ConvertToStringArray(input);

            Assert.AreEqual("1", result[0]);
            Assert.AreEqual("2", result[1]);
            Assert.AreEqual("3", result[2]);
        }
        #endregion
    }
}
