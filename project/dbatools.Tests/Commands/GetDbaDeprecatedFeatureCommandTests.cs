using System;
using System.Data;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class GetDbaDeprecatedFeatureCommandTests
    {
        #region GetServerProperty
        [TestMethod]
        public void GetServerProperty_NullServer_ReturnsEmpty()
        {
            string result = GetDbaDeprecatedFeatureCommand.GetServerProperty(null, "ComputerName");
            Assert.AreEqual(String.Empty, result);
        }

        [TestMethod]
        public void GetServerProperty_ValidProperty_ReturnsValue()
        {
            PSObject server = new PSObject();
            server.Properties.Add(new PSNoteProperty("ComputerName", "SQLSERVER01"));
            string result = GetDbaDeprecatedFeatureCommand.GetServerProperty(server, "ComputerName");
            Assert.AreEqual("SQLSERVER01", result);
        }

        [TestMethod]
        public void GetServerProperty_MissingProperty_ReturnsEmpty()
        {
            PSObject server = new PSObject();
            server.Properties.Add(new PSNoteProperty("ComputerName", "SQLSERVER01"));
            string result = GetDbaDeprecatedFeatureCommand.GetServerProperty(server, "NonExistent");
            Assert.AreEqual(String.Empty, result);
        }

        [TestMethod]
        public void GetServerProperty_NullPropertyValue_ReturnsEmpty()
        {
            PSObject server = new PSObject();
            server.Properties.Add(new PSNoteProperty("ComputerName", null));
            string result = GetDbaDeprecatedFeatureCommand.GetServerProperty(server, "ComputerName");
            Assert.AreEqual(String.Empty, result);
        }
        #endregion GetServerProperty

        #region GetRowValue
        [TestMethod]
        public void GetRowValue_NullRow_ReturnsNull()
        {
            object result = GetDbaDeprecatedFeatureCommand.GetRowValue(null, "DeprecatedFeature");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetRowValue_PSObjectWithProperty_ReturnsValue()
        {
            PSObject row = new PSObject();
            row.Properties.Add(new PSNoteProperty("DeprecatedFeature", "sysdatabases"));
            object result = GetDbaDeprecatedFeatureCommand.GetRowValue(row, "DeprecatedFeature");
            Assert.AreEqual("sysdatabases", result);
        }

        [TestMethod]
        public void GetRowValue_PSObjectMissingProperty_ReturnsNull()
        {
            PSObject row = new PSObject();
            row.Properties.Add(new PSNoteProperty("DeprecatedFeature", "sysdatabases"));
            object result = GetDbaDeprecatedFeatureCommand.GetRowValue(row, "NonExistent");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetRowValue_DataRow_ReturnsValue()
        {
            DataTable table = new DataTable();
            table.Columns.Add("DeprecatedFeature", typeof(string));
            table.Columns.Add("UsageCount", typeof(long));
            DataRow dataRow = table.NewRow();
            dataRow["DeprecatedFeature"] = "sysdatabases";
            dataRow["UsageCount"] = 42L;
            table.Rows.Add(dataRow);

            PSObject row = PSObject.AsPSObject(dataRow);
            object featureResult = GetDbaDeprecatedFeatureCommand.GetRowValue(row, "DeprecatedFeature");
            Assert.AreEqual("sysdatabases", featureResult);

            object countResult = GetDbaDeprecatedFeatureCommand.GetRowValue(row, "UsageCount");
            Assert.AreEqual(42L, countResult);
        }

        [TestMethod]
        public void GetRowValue_DataRowWithDBNull_ReturnsNull()
        {
            DataTable table = new DataTable();
            table.Columns.Add("DeprecatedFeature", typeof(string));
            DataRow dataRow = table.NewRow();
            dataRow["DeprecatedFeature"] = DBNull.Value;
            table.Rows.Add(dataRow);

            PSObject row = PSObject.AsPSObject(dataRow);
            object result = GetDbaDeprecatedFeatureCommand.GetRowValue(row, "DeprecatedFeature");
            Assert.IsNull(result);
        }
        #endregion GetRowValue

        #region DeprecatedFeatureQuery
        [TestMethod]
        public void DeprecatedFeatureQuery_ContainsExpectedSqlElements()
        {
            string query = GetDbaDeprecatedFeatureCommand.DeprecatedFeatureQuery;
            Assert.IsTrue(query.Contains("sys.dm_os_performance_counters"), "Query should reference sys.dm_os_performance_counters");
            Assert.IsTrue(query.Contains("Deprecated Features"), "Query should filter for Deprecated Features");
            Assert.IsTrue(query.Contains("cntr_value > 0"), "Query should filter for non-zero usage counts");
            Assert.IsTrue(query.Contains("LTRIM(RTRIM(instance_name))"), "Query should trim instance_name");
            Assert.IsTrue(query.Contains("AS DeprecatedFeature"), "Query should alias as DeprecatedFeature");
            Assert.IsTrue(query.Contains("AS UsageCount"), "Query should alias as UsageCount");
        }
        #endregion DeprecatedFeatureQuery
    }
}
