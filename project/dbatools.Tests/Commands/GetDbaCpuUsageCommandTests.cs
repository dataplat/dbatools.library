using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Management.Automation;
using Dataplat.Dbatools.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class GetDbaCpuUsageCommandTests
    {
        #region GetThreadStateDescription

        [TestMethod]
        public void GetThreadStateDescription_ValidState0_ReturnsInitialized()
        {
            string result = GetDbaCpuUsageCommand.GetThreadStateDescription(0);
            Assert.IsTrue(result.StartsWith("Initialized"));
        }

        [TestMethod]
        public void GetThreadStateDescription_ValidState2_ReturnsRunning()
        {
            string result = GetDbaCpuUsageCommand.GetThreadStateDescription(2);
            Assert.IsTrue(result.StartsWith("Running"));
        }

        [TestMethod]
        public void GetThreadStateDescription_ValidState5_ReturnsWaiting()
        {
            string result = GetDbaCpuUsageCommand.GetThreadStateDescription(5);
            Assert.IsTrue(result.StartsWith("Waiting"));
        }

        [TestMethod]
        public void GetThreadStateDescription_ValidState7_ReturnsUnknown()
        {
            string result = GetDbaCpuUsageCommand.GetThreadStateDescription(7);
            Assert.IsTrue(result.StartsWith("Unknown"));
        }

        [TestMethod]
        public void GetThreadStateDescription_InvalidState99_ReturnsNull()
        {
            string result = GetDbaCpuUsageCommand.GetThreadStateDescription(99);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetThreadStateDescription_NullInput_ReturnsNull()
        {
            string result = GetDbaCpuUsageCommand.GetThreadStateDescription(null);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetThreadStateDescription_StringInput_ParsesCorrectly()
        {
            string result = GetDbaCpuUsageCommand.GetThreadStateDescription("3");
            Assert.IsTrue(result.StartsWith("Standby"));
        }

        [TestMethod]
        public void GetThreadStateDescription_LongInput_ParsesCorrectly()
        {
            string result = GetDbaCpuUsageCommand.GetThreadStateDescription((long)4);
            Assert.IsTrue(result.StartsWith("Terminated"));
        }

        [TestMethod]
        public void GetThreadStateDescription_AllStatesPresent()
        {
            for (int i = 0; i <= 7; i++)
            {
                string result = GetDbaCpuUsageCommand.GetThreadStateDescription(i);
                Assert.IsNotNull(result, String.Format("State {0} should have a description", i));
            }
        }

        #endregion GetThreadStateDescription

        #region GetThreadWaitReasonDescription

        [TestMethod]
        public void GetThreadWaitReasonDescription_ValidReason0_ReturnsExecutive()
        {
            string result = GetDbaCpuUsageCommand.GetThreadWaitReasonDescription(0);
            Assert.AreEqual("Executive", result);
        }

        [TestMethod]
        public void GetThreadWaitReasonDescription_ValidReason1_ReturnsFreePage()
        {
            string result = GetDbaCpuUsageCommand.GetThreadWaitReasonDescription(1);
            Assert.AreEqual("FreePage", result);
        }

        [TestMethod]
        public void GetThreadWaitReasonDescription_ValidReason20_ReturnsUnknown()
        {
            string result = GetDbaCpuUsageCommand.GetThreadWaitReasonDescription(20);
            Assert.AreEqual("Unknown", result);
        }

        [TestMethod]
        public void GetThreadWaitReasonDescription_InvalidReason99_ReturnsNull()
        {
            string result = GetDbaCpuUsageCommand.GetThreadWaitReasonDescription(99);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetThreadWaitReasonDescription_NullInput_ReturnsNull()
        {
            string result = GetDbaCpuUsageCommand.GetThreadWaitReasonDescription(null);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetThreadWaitReasonDescription_StringInput_ParsesCorrectly()
        {
            string result = GetDbaCpuUsageCommand.GetThreadWaitReasonDescription("14");
            Assert.AreEqual("EventPairHigh", result);
        }

        [TestMethod]
        public void GetThreadWaitReasonDescription_AllReasonsPresent()
        {
            for (int i = 0; i <= 20; i++)
            {
                string result = GetDbaCpuUsageCommand.GetThreadWaitReasonDescription(i);
                Assert.IsNotNull(result, String.Format("Reason {0} should have a description", i));
            }
        }

        #endregion GetThreadWaitReasonDescription

        #region FindSpid

        [TestMethod]
        public void FindSpid_NullCollection_ReturnsNull()
        {
            object result = GetDbaCpuUsageCommand.FindSpid(null, "123");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void FindSpid_NullIdThread_ReturnsNull()
        {
            Collection<PSObject> collection = new Collection<PSObject>();
            object result = GetDbaCpuUsageCommand.FindSpid(collection, null);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void FindSpid_EmptyCollection_ReturnsNull()
        {
            Collection<PSObject> collection = new Collection<PSObject>();
            object result = GetDbaCpuUsageCommand.FindSpid(collection, "123");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void FindSpid_MatchingRow_ReturnsSpid()
        {
            // Create a DataTable to simulate query results
            DataTable dt = new DataTable();
            dt.Columns.Add("kpid", typeof(int));
            dt.Columns.Add("spid", typeof(int));
            DataRow row = dt.NewRow();
            row["kpid"] = 100;
            row["spid"] = 55;
            dt.Rows.Add(row);

            Collection<PSObject> collection = new Collection<PSObject>();
            collection.Add(PSObject.AsPSObject(row));

            object result = GetDbaCpuUsageCommand.FindSpid(collection, "100");
            Assert.IsNotNull(result);
            Assert.AreEqual(55, Convert.ToInt32(result));
        }

        [TestMethod]
        public void FindSpid_NoMatchingRow_ReturnsNull()
        {
            DataTable dt = new DataTable();
            dt.Columns.Add("kpid", typeof(int));
            dt.Columns.Add("spid", typeof(int));
            DataRow row = dt.NewRow();
            row["kpid"] = 100;
            row["spid"] = 55;
            dt.Rows.Add(row);

            Collection<PSObject> collection = new Collection<PSObject>();
            collection.Add(PSObject.AsPSObject(row));

            object result = GetDbaCpuUsageCommand.FindSpid(collection, "999");
            Assert.IsNull(result);
        }

        #endregion FindSpid

        #region FindProcess

        [TestMethod]
        public void FindProcess_NullProcesses_ReturnsNull()
        {
            PSObject result = GetDbaCpuUsageCommand.FindProcess(null, 55);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void FindProcess_NullSpid_ReturnsNull()
        {
            Collection<PSObject> processes = new Collection<PSObject>();
            PSObject result = GetDbaCpuUsageCommand.FindProcess(processes, null);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void FindProcess_MatchingSpid_ReturnsProcess()
        {
            PSObject proc = new PSObject();
            proc.Properties.Add(new PSNoteProperty("Spid", 55));
            proc.Properties.Add(new PSNoteProperty("LastQuery", "SELECT 1"));

            Collection<PSObject> processes = new Collection<PSObject>();
            processes.Add(proc);

            PSObject result = GetDbaCpuUsageCommand.FindProcess(processes, 55);
            Assert.IsNotNull(result);
        }

        [TestMethod]
        public void FindProcess_NoMatchingSpid_ReturnsNull()
        {
            PSObject proc = new PSObject();
            proc.Properties.Add(new PSNoteProperty("Spid", 55));

            Collection<PSObject> processes = new Collection<PSObject>();
            processes.Add(proc);

            PSObject result = GetDbaCpuUsageCommand.FindProcess(processes, 99);
            Assert.IsNull(result);
        }

        #endregion FindProcess

        #region FindProcessesByHostProcessId

        [TestMethod]
        public void FindProcessesByHostProcessId_NullProcesses_ReturnsEmptyList()
        {
            List<PSObject> result = GetDbaCpuUsageCommand.FindProcessesByHostProcessId(null, 123);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void FindProcessesByHostProcessId_NullIdProcess_ReturnsEmptyList()
        {
            Collection<PSObject> processes = new Collection<PSObject>();
            List<PSObject> result = GetDbaCpuUsageCommand.FindProcessesByHostProcessId(processes, null);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void FindProcessesByHostProcessId_MatchingPid_ReturnsProcesses()
        {
            PSObject proc1 = new PSObject();
            proc1.Properties.Add(new PSNoteProperty("HostProcessID", 1234));
            PSObject proc2 = new PSObject();
            proc2.Properties.Add(new PSNoteProperty("HostProcessID", 1234));
            PSObject proc3 = new PSObject();
            proc3.Properties.Add(new PSNoteProperty("HostProcessID", 5678));

            Collection<PSObject> processes = new Collection<PSObject>();
            processes.Add(proc1);
            processes.Add(proc2);
            processes.Add(proc3);

            List<PSObject> result = GetDbaCpuUsageCommand.FindProcessesByHostProcessId(processes, 1234);
            Assert.AreEqual(2, result.Count);
        }

        #endregion FindProcessesByHostProcessId

        #region FilterThreads

        [TestMethod]
        public void FilterThreads_NullCollection_ReturnsEmptyList()
        {
            List<PSObject> result = GetDbaCpuUsageCommand.FilterThreads(null, 0);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void FilterThreads_NoSqlThreads_ReturnsEmptyList()
        {
            Collection<PSObject> threads = new Collection<PSObject>();
            PSObject thread = new PSObject();
            thread.Properties.Add(new PSNoteProperty("Name", "csrss_1234_5"));
            thread.Properties.Add(new PSNoteProperty("PercentProcessorTime", 50));
            threads.Add(thread);

            List<PSObject> result = GetDbaCpuUsageCommand.FilterThreads(threads, 0);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void FilterThreads_SqlThreadAboveThreshold_Included()
        {
            Collection<PSObject> threads = new Collection<PSObject>();
            PSObject thread = new PSObject();
            thread.Properties.Add(new PSNoteProperty("Name", "sqlservr_1234_5"));
            thread.Properties.Add(new PSNoteProperty("PercentProcessorTime", 50));
            threads.Add(thread);

            List<PSObject> result = GetDbaCpuUsageCommand.FilterThreads(threads, 10);
            Assert.AreEqual(1, result.Count);
        }

        [TestMethod]
        public void FilterThreads_SqlThreadBelowThreshold_Excluded()
        {
            Collection<PSObject> threads = new Collection<PSObject>();
            PSObject thread = new PSObject();
            thread.Properties.Add(new PSNoteProperty("Name", "sqlservr_1234_5"));
            thread.Properties.Add(new PSNoteProperty("PercentProcessorTime", 5));
            threads.Add(thread);

            List<PSObject> result = GetDbaCpuUsageCommand.FilterThreads(threads, 10);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void FilterThreads_SqlThreadAtThreshold_Included()
        {
            Collection<PSObject> threads = new Collection<PSObject>();
            PSObject thread = new PSObject();
            thread.Properties.Add(new PSNoteProperty("Name", "sqlservr_1234_5"));
            thread.Properties.Add(new PSNoteProperty("PercentProcessorTime", 10));
            threads.Add(thread);

            List<PSObject> result = GetDbaCpuUsageCommand.FilterThreads(threads, 10);
            Assert.AreEqual(1, result.Count);
        }

        [TestMethod]
        public void FilterThreads_CaseInsensitiveName_Included()
        {
            Collection<PSObject> threads = new Collection<PSObject>();
            PSObject thread = new PSObject();
            thread.Properties.Add(new PSNoteProperty("Name", "SQLSERVR_1234_5"));
            thread.Properties.Add(new PSNoteProperty("PercentProcessorTime", 50));
            threads.Add(thread);

            List<PSObject> result = GetDbaCpuUsageCommand.FilterThreads(threads, 0);
            Assert.AreEqual(1, result.Count);
        }

        [TestMethod]
        public void FilterThreads_NullNameProperty_Skipped()
        {
            Collection<PSObject> threads = new Collection<PSObject>();
            PSObject thread = new PSObject();
            thread.Properties.Add(new PSNoteProperty("PercentProcessorTime", 50));
            threads.Add(thread);

            List<PSObject> result = GetDbaCpuUsageCommand.FilterThreads(threads, 0);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void FilterThreads_MixedThreads_FiltersCorrectly()
        {
            Collection<PSObject> threads = new Collection<PSObject>();

            PSObject sqlThread = new PSObject();
            sqlThread.Properties.Add(new PSNoteProperty("Name", "sqlservr_1234_5"));
            sqlThread.Properties.Add(new PSNoteProperty("PercentProcessorTime", 50));
            threads.Add(sqlThread);

            PSObject nonSqlThread = new PSObject();
            nonSqlThread.Properties.Add(new PSNoteProperty("Name", "csrss_5678_1"));
            nonSqlThread.Properties.Add(new PSNoteProperty("PercentProcessorTime", 90));
            threads.Add(nonSqlThread);

            PSObject sqlAgentThread = new PSObject();
            sqlAgentThread.Properties.Add(new PSNoteProperty("Name", "sqlagent_1234_2"));
            sqlAgentThread.Properties.Add(new PSNoteProperty("PercentProcessorTime", 10));
            threads.Add(sqlAgentThread);

            List<PSObject> result = GetDbaCpuUsageCommand.FilterThreads(threads, 0);
            Assert.AreEqual(2, result.Count);
        }

        #endregion FilterThreads

        #region GetVersionMajor

        [TestMethod]
        public void GetVersionMajor_NullServer_ReturnsZero()
        {
            int result = GetDbaCpuUsageCommand.GetVersionMajor(null);
            Assert.AreEqual(0, result);
        }

        [TestMethod]
        public void GetVersionMajor_PSObjectWithIntProperty_ReturnsValue()
        {
            PSObject server = new PSObject();
            server.Properties.Add(new PSNoteProperty("VersionMajor", 15));
            int result = GetDbaCpuUsageCommand.GetVersionMajor(server);
            Assert.AreEqual(15, result);
        }

        [TestMethod]
        public void GetVersionMajor_PSObjectWithStringProperty_ParsesValue()
        {
            PSObject server = new PSObject();
            server.Properties.Add(new PSNoteProperty("VersionMajor", "14"));
            int result = GetDbaCpuUsageCommand.GetVersionMajor(server);
            Assert.AreEqual(14, result);
        }

        [TestMethod]
        public void GetVersionMajor_PSObjectWithoutProperty_ReturnsZero()
        {
            PSObject server = new PSObject();
            int result = GetDbaCpuUsageCommand.GetVersionMajor(server);
            Assert.AreEqual(0, result);
        }

        #endregion GetVersionMajor

        #region GetServerProperty

        [TestMethod]
        public void GetServerProperty_NullServer_ReturnsEmpty()
        {
            string result = GetDbaCpuUsageCommand.GetServerProperty(null, "ComputerName");
            Assert.AreEqual(String.Empty, result);
        }

        [TestMethod]
        public void GetServerProperty_ValidProperty_ReturnsValue()
        {
            PSObject server = new PSObject();
            server.Properties.Add(new PSNoteProperty("ComputerName", "SQL01"));
            string result = GetDbaCpuUsageCommand.GetServerProperty(server, "ComputerName");
            Assert.AreEqual("SQL01", result);
        }

        [TestMethod]
        public void GetServerProperty_MissingProperty_ReturnsEmpty()
        {
            PSObject server = new PSObject();
            string result = GetDbaCpuUsageCommand.GetServerProperty(server, "NonExistent");
            Assert.AreEqual(String.Empty, result);
        }

        #endregion GetServerProperty

        #region GetPSObjectProperty

        [TestMethod]
        public void GetPSObjectProperty_NullObject_ReturnsNull()
        {
            object result = GetDbaCpuUsageCommand.GetPSObjectProperty(null, "Name");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPSObjectProperty_ValidProperty_ReturnsValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "sqlservr_1234_5"));
            object result = GetDbaCpuUsageCommand.GetPSObjectProperty(obj, "Name");
            Assert.AreEqual("sqlservr_1234_5", result);
        }

        [TestMethod]
        public void GetPSObjectProperty_MissingProperty_ReturnsNull()
        {
            PSObject obj = new PSObject();
            object result = GetDbaCpuUsageCommand.GetPSObjectProperty(obj, "Missing");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPSObjectProperty_DBNullValue_ReturnsNull()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Value", DBNull.Value));
            object result = GetDbaCpuUsageCommand.GetPSObjectProperty(obj, "Value");
            Assert.IsNull(result);
        }

        #endregion GetPSObjectProperty

        #region GetRowColumnValue

        [TestMethod]
        public void GetRowColumnValue_NullRow_ReturnsNull()
        {
            object result = GetDbaCpuUsageCommand.GetRowColumnValue(null, "col");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetRowColumnValue_DataRowWithValue_ReturnsValue()
        {
            DataTable dt = new DataTable();
            dt.Columns.Add("kpid", typeof(int));
            DataRow row = dt.NewRow();
            row["kpid"] = 42;
            dt.Rows.Add(row);

            PSObject pso = PSObject.AsPSObject(row);
            object result = GetDbaCpuUsageCommand.GetRowColumnValue(pso, "kpid");
            Assert.AreEqual(42, result);
        }

        [TestMethod]
        public void GetRowColumnValue_DataRowWithDBNull_ReturnsNull()
        {
            DataTable dt = new DataTable();
            dt.Columns.Add("kpid", typeof(int));
            DataRow row = dt.NewRow();
            // kpid is DBNull by default
            dt.Rows.Add(row);

            PSObject pso = PSObject.AsPSObject(row);
            object result = GetDbaCpuUsageCommand.GetRowColumnValue(pso, "kpid");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetRowColumnValue_PSObjectWithProperty_ReturnsValue()
        {
            PSObject pso = new PSObject();
            pso.Properties.Add(new PSNoteProperty("spid", 55));
            object result = GetDbaCpuUsageCommand.GetRowColumnValue(pso, "spid");
            Assert.AreEqual(55, result);
        }

        #endregion GetRowColumnValue
    }
}
