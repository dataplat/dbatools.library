using System;
using System.Data;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class GetDbaCpuRingBufferCommandTests
    {
        #region BuildRingBufferQuery
        [TestMethod]
        public void BuildRingBufferQuery_DefaultMinutes_ContainsCorrectTimestamp()
        {
            // Arrange
            long timestamp = 123456789L;
            int minutes = 60;

            // Act
            string sql = GetDbaCpuRingBufferCommand.BuildRingBufferQuery(timestamp, minutes);

            // Assert
            Assert.IsTrue(sql.Contains("123456789"), "SQL should contain the timestamp value");
            Assert.IsTrue(sql.Contains("-60"), "SQL should contain the negative collection minutes");
        }

        [TestMethod]
        public void BuildRingBufferQuery_CustomMinutes_ContainsCorrectMinutes()
        {
            // Arrange
            long timestamp = 999999999L;
            int minutes = 240;

            // Act
            string sql = GetDbaCpuRingBufferCommand.BuildRingBufferQuery(timestamp, minutes);

            // Assert
            Assert.IsTrue(sql.Contains("-240"), "SQL should contain the negative custom minutes value");
            Assert.IsTrue(sql.Contains("999999999"), "SQL should contain the timestamp value");
        }

        [TestMethod]
        public void BuildRingBufferQuery_ContainsRequiredSqlElements()
        {
            // Arrange & Act
            string sql = GetDbaCpuRingBufferCommand.BuildRingBufferQuery(100000, 60);

            // Assert
            Assert.IsTrue(sql.Contains("sys.dm_os_ring_buffers"), "SQL should query dm_os_ring_buffers");
            Assert.IsTrue(sql.Contains("RING_BUFFER_SCHEDULER_MONITOR"), "SQL should filter for RING_BUFFER_SCHEDULER_MONITOR");
            Assert.IsTrue(sql.Contains("SQLProcessUtilization"), "SQL should select SQLProcessUtilization");
            Assert.IsTrue(sql.Contains("SystemIdle"), "SQL should select SystemIdle");
            Assert.IsTrue(sql.Contains("OtherProcessUtilization"), "SQL should calculate OtherProcessUtilization");
            Assert.IsTrue(sql.Contains("record_id"), "SQL should select record_id");
            Assert.IsTrue(sql.Contains("EventTime"), "SQL should select EventTime");
            Assert.IsTrue(sql.Contains("DATEADD"), "SQL should use DATEADD for time calculation");
        }

        [TestMethod]
        public void BuildRingBufferQuery_ZeroMinutes_ProducesValidSql()
        {
            // Arrange & Act
            string sql = GetDbaCpuRingBufferCommand.BuildRingBufferQuery(100000, 0);

            // Assert - should not throw and should produce valid SQL
            Assert.IsNotNull(sql);
            Assert.IsTrue(sql.Contains("-0"), "SQL should contain -0 for zero minutes");
        }
        #endregion

        #region GetRowValue
        [TestMethod]
        public void GetRowValue_NullRow_ReturnsNull()
        {
            // Act
            object result = GetDbaCpuRingBufferCommand.GetRowValue(null, "record_id");

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetRowValue_DataRowWithValidColumn_ReturnsValue()
        {
            // Arrange
            DataTable dt = new DataTable();
            dt.Columns.Add("record_id", typeof(int));
            DataRow dr = dt.NewRow();
            dr["record_id"] = 42;
            dt.Rows.Add(dr);

            PSObject psObj = new PSObject(dr);

            // Act
            object result = GetDbaCpuRingBufferCommand.GetRowValue(psObj, "record_id");

            // Assert
            Assert.AreEqual(42, result);
        }

        [TestMethod]
        public void GetRowValue_DataRowWithDbNull_ReturnsNull()
        {
            // Arrange
            DataTable dt = new DataTable();
            dt.Columns.Add("EventTime", typeof(DateTime));
            DataRow dr = dt.NewRow();
            // Default value for unset column is DBNull
            dt.Rows.Add(dr);

            PSObject psObj = new PSObject(dr);

            // Act
            object result = GetDbaCpuRingBufferCommand.GetRowValue(psObj, "EventTime");

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetRowValue_DataRowWithInvalidColumn_ReturnsNull()
        {
            // Arrange
            DataTable dt = new DataTable();
            dt.Columns.Add("record_id", typeof(int));
            DataRow dr = dt.NewRow();
            dr["record_id"] = 1;
            dt.Rows.Add(dr);

            PSObject psObj = new PSObject(dr);

            // Act
            object result = GetDbaCpuRingBufferCommand.GetRowValue(psObj, "NonExistentColumn");

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetRowValue_PSObjectWithNoteProperty_ReturnsValue()
        {
            // Arrange
            PSObject psObj = new PSObject();
            psObj.Properties.Add(new PSNoteProperty("SQLProcessUtilization", 75));

            // Act
            object result = GetDbaCpuRingBufferCommand.GetRowValue(psObj, "SQLProcessUtilization");

            // Assert
            Assert.AreEqual(75, result);
        }

        [TestMethod]
        public void GetRowValue_DataRowWithDateTimeColumn_ReturnsDateTime()
        {
            // Arrange
            DataTable dt = new DataTable();
            dt.Columns.Add("EventTime", typeof(DateTime));
            DataRow dr = dt.NewRow();
            DateTime expected = new DateTime(2026, 2, 18, 10, 30, 0);
            dr["EventTime"] = expected;
            dt.Rows.Add(dr);

            PSObject psObj = new PSObject(dr);

            // Act
            object result = GetDbaCpuRingBufferCommand.GetRowValue(psObj, "EventTime");

            // Assert
            Assert.AreEqual(expected, result);
        }
        #endregion

        #region GetVersionMajor
        [TestMethod]
        public void GetVersionMajor_NullServer_ReturnsZero()
        {
            // Act
            int result = GetDbaCpuRingBufferCommand.GetVersionMajor(null);

            // Assert
            Assert.AreEqual(0, result);
        }

        [TestMethod]
        public void GetVersionMajor_PSObjectWithIntProperty_ReturnsValue()
        {
            // Arrange
            PSObject pso = new PSObject();
            pso.Properties.Add(new PSNoteProperty("VersionMajor", 16));

            // Act
            int result = GetDbaCpuRingBufferCommand.GetVersionMajor(pso);

            // Assert
            Assert.AreEqual(16, result);
        }

        [TestMethod]
        public void GetVersionMajor_PSObjectWithStringProperty_ReturnsParsedValue()
        {
            // Arrange
            PSObject pso = new PSObject();
            pso.Properties.Add(new PSNoteProperty("VersionMajor", "15"));

            // Act
            int result = GetDbaCpuRingBufferCommand.GetVersionMajor(pso);

            // Assert
            Assert.AreEqual(15, result);
        }

        [TestMethod]
        public void GetVersionMajor_PSObjectWithoutProperty_ReturnsZero()
        {
            // Arrange
            PSObject pso = new PSObject();
            pso.Properties.Add(new PSNoteProperty("SomeOtherProperty", 42));

            // Act
            int result = GetDbaCpuRingBufferCommand.GetVersionMajor(pso);

            // Assert
            Assert.AreEqual(0, result);
        }

        [TestMethod]
        public void GetVersionMajor_PSObjectWithNonNumericString_ReturnsZero()
        {
            // Arrange
            PSObject pso = new PSObject();
            pso.Properties.Add(new PSNoteProperty("VersionMajor", "notanumber"));

            // Act
            int result = GetDbaCpuRingBufferCommand.GetVersionMajor(pso);

            // Assert
            Assert.AreEqual(0, result);
        }
        #endregion

        #region GetServerProperty
        [TestMethod]
        public void GetServerProperty_NullServer_ReturnsEmptyString()
        {
            // Act
            string result = GetDbaCpuRingBufferCommand.GetServerProperty(null, "ComputerName");

            // Assert
            Assert.AreEqual(String.Empty, result);
        }

        [TestMethod]
        public void GetServerProperty_ValidProperty_ReturnsValue()
        {
            // Arrange
            PSObject pso = new PSObject();
            pso.Properties.Add(new PSNoteProperty("ComputerName", "SQL01"));

            // Act
            string result = GetDbaCpuRingBufferCommand.GetServerProperty(pso, "ComputerName");

            // Assert
            Assert.AreEqual("SQL01", result);
        }

        [TestMethod]
        public void GetServerProperty_MissingProperty_ReturnsEmptyString()
        {
            // Arrange
            PSObject pso = new PSObject();
            pso.Properties.Add(new PSNoteProperty("SomeProperty", "value"));

            // Act
            string result = GetDbaCpuRingBufferCommand.GetServerProperty(pso, "ComputerName");

            // Assert
            Assert.AreEqual(String.Empty, result);
        }

        [TestMethod]
        public void GetServerProperty_NullPropertyValue_ReturnsEmptyString()
        {
            // Arrange
            PSObject pso = new PSObject();
            pso.Properties.Add(new PSNoteProperty("ComputerName", null));

            // Act
            string result = GetDbaCpuRingBufferCommand.GetServerProperty(pso, "ComputerName");

            // Assert
            Assert.AreEqual(String.Empty, result);
        }
        #endregion
    }
}
