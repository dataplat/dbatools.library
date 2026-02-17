using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class GetDbaAgDatabaseReplicaStateCommandTests
    {
        #region BuildDatabaseFilter
        [TestMethod]
        public void BuildDatabaseFilter_NullReturnsNull()
        {
            HashSet<string> result = GetDbaAgDatabaseReplicaStateCommand.BuildDatabaseFilter(null);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void BuildDatabaseFilter_EmptyArrayReturnsNull()
        {
            HashSet<string> result = GetDbaAgDatabaseReplicaStateCommand.BuildDatabaseFilter(new string[0]);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void BuildDatabaseFilter_PopulatesFilter()
        {
            HashSet<string> result = GetDbaAgDatabaseReplicaStateCommand.BuildDatabaseFilter(new string[] { "DB1", "DB2" });

            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Count);
            Assert.IsTrue(result.Contains("DB1"));
            Assert.IsTrue(result.Contains("DB2"));
        }

        [TestMethod]
        public void BuildDatabaseFilter_CaseInsensitive()
        {
            HashSet<string> result = GetDbaAgDatabaseReplicaStateCommand.BuildDatabaseFilter(new string[] { "MyDatabase" });

            Assert.IsNotNull(result);
            Assert.IsTrue(result.Contains("mydatabase"));
            Assert.IsTrue(result.Contains("MYDATABASE"));
            Assert.IsTrue(result.Contains("MyDatabase"));
        }

        [TestMethod]
        public void BuildDatabaseFilter_DeduplicatesDifferentCase()
        {
            HashSet<string> result = GetDbaAgDatabaseReplicaStateCommand.BuildDatabaseFilter(new string[] { "db1", "DB1" });

            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.Count);
        }
        #endregion

        #region GetPropertyString
        [TestMethod]
        public void GetPropertyString_ReturnsStringValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "TestDatabase"));

            string result = GetDbaAgDatabaseReplicaStateCommand.GetPropertyString(obj, "Name");

            Assert.AreEqual("TestDatabase", result);
        }

        [TestMethod]
        public void GetPropertyString_NullObjectReturnsNull()
        {
            string result = GetDbaAgDatabaseReplicaStateCommand.GetPropertyString(null, "Name");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyString_MissingPropertyReturnsNull()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("ID", 1));

            string result = GetDbaAgDatabaseReplicaStateCommand.GetPropertyString(obj, "Name");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyString_NullValueReturnsNull()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", null));

            string result = GetDbaAgDatabaseReplicaStateCommand.GetPropertyString(obj, "Name");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyString_NonStringValueConvertsToString()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Count", 42));

            string result = GetDbaAgDatabaseReplicaStateCommand.GetPropertyString(obj, "Count");

            Assert.AreEqual("42", result);
        }
        #endregion

        #region GetPropertyValue
        [TestMethod]
        public void GetPropertyValue_ReturnsValuePreservingType()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("IsJoined", true));

            object result = GetDbaAgDatabaseReplicaStateCommand.GetPropertyValue(obj, "IsJoined");

            Assert.IsInstanceOfType(result, typeof(bool));
            Assert.AreEqual(true, result);
        }

        [TestMethod]
        public void GetPropertyValue_NullObjectReturnsNull()
        {
            object result = GetDbaAgDatabaseReplicaStateCommand.GetPropertyValue(null, "Name");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyValue_MissingPropertyReturnsNull()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("ID", 1));

            object result = GetDbaAgDatabaseReplicaStateCommand.GetPropertyValue(obj, "Name");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyValue_ReturnsNullForNullValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", null));

            object result = GetDbaAgDatabaseReplicaStateCommand.GetPropertyValue(obj, "Name");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyValue_PreservesIntType()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("LogSendQueueSize", 1024L));

            object result = GetDbaAgDatabaseReplicaStateCommand.GetPropertyValue(obj, "LogSendQueueSize");

            Assert.IsInstanceOfType(result, typeof(long));
            Assert.AreEqual(1024L, result);
        }

        [TestMethod]
        public void GetPropertyValue_PreservesDateTimeType()
        {
            DateTime now = DateTime.Now;
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("LastCommitTime", now));

            object result = GetDbaAgDatabaseReplicaStateCommand.GetPropertyValue(obj, "LastCommitTime");

            Assert.IsInstanceOfType(result, typeof(DateTime));
            Assert.AreEqual(now, result);
        }
        #endregion

        #region FilterByReplicaId
        [TestMethod]
        public void FilterByReplicaId_ReturnsMatchingStates()
        {
            string replicaId = Guid.NewGuid().ToString();
            string otherId = Guid.NewGuid().ToString();

            Collection<PSObject> allStates = new Collection<PSObject>();

            PSObject state1 = new PSObject();
            state1.Properties.Add(new PSNoteProperty("AvailabilityReplicaId", replicaId));
            state1.Properties.Add(new PSNoteProperty("DatabaseName", "DB1"));
            allStates.Add(state1);

            PSObject state2 = new PSObject();
            state2.Properties.Add(new PSNoteProperty("AvailabilityReplicaId", otherId));
            state2.Properties.Add(new PSNoteProperty("DatabaseName", "DB2"));
            allStates.Add(state2);

            PSObject state3 = new PSObject();
            state3.Properties.Add(new PSNoteProperty("AvailabilityReplicaId", replicaId));
            state3.Properties.Add(new PSNoteProperty("DatabaseName", "DB3"));
            allStates.Add(state3);

            List<PSObject> result = GetDbaAgDatabaseReplicaStateCommand.FilterByReplicaId(allStates, replicaId);

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("DB1", GetDbaAgDatabaseReplicaStateCommand.GetPropertyString(result[0], "DatabaseName"));
            Assert.AreEqual("DB3", GetDbaAgDatabaseReplicaStateCommand.GetPropertyString(result[1], "DatabaseName"));
        }

        [TestMethod]
        public void FilterByReplicaId_NullCollectionReturnsEmpty()
        {
            List<PSObject> result = GetDbaAgDatabaseReplicaStateCommand.FilterByReplicaId(null, "some-id");
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void FilterByReplicaId_NullReplicaIdReturnsEmpty()
        {
            Collection<PSObject> allStates = new Collection<PSObject>();
            PSObject state = new PSObject();
            state.Properties.Add(new PSNoteProperty("AvailabilityReplicaId", "some-id"));
            allStates.Add(state);

            List<PSObject> result = GetDbaAgDatabaseReplicaStateCommand.FilterByReplicaId(allStates, null);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void FilterByReplicaId_CaseInsensitiveMatch()
        {
            string replicaId = "abc-def-123";

            Collection<PSObject> allStates = new Collection<PSObject>();
            PSObject state = new PSObject();
            state.Properties.Add(new PSNoteProperty("AvailabilityReplicaId", "ABC-DEF-123"));
            allStates.Add(state);

            List<PSObject> result = GetDbaAgDatabaseReplicaStateCommand.FilterByReplicaId(allStates, replicaId);
            Assert.AreEqual(1, result.Count);
        }

        [TestMethod]
        public void FilterByReplicaId_NoMatchReturnsEmpty()
        {
            Collection<PSObject> allStates = new Collection<PSObject>();
            PSObject state = new PSObject();
            state.Properties.Add(new PSNoteProperty("AvailabilityReplicaId", "some-id"));
            allStates.Add(state);

            List<PSObject> result = GetDbaAgDatabaseReplicaStateCommand.FilterByReplicaId(allStates, "different-id");
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void FilterByReplicaId_SkipsNullEntries()
        {
            string replicaId = "test-id";
            Collection<PSObject> allStates = new Collection<PSObject>();
            allStates.Add(null);

            PSObject state = new PSObject();
            state.Properties.Add(new PSNoteProperty("AvailabilityReplicaId", replicaId));
            allStates.Add(state);

            allStates.Add(null);

            List<PSObject> result = GetDbaAgDatabaseReplicaStateCommand.FilterByReplicaId(allStates, replicaId);
            Assert.AreEqual(1, result.Count);
        }
        #endregion

        #region FindByDatabaseId
        [TestMethod]
        public void FindByDatabaseId_ReturnsMatchingState()
        {
            string dbId = Guid.NewGuid().ToString();

            List<PSObject> states = new List<PSObject>();
            PSObject state1 = new PSObject();
            state1.Properties.Add(new PSNoteProperty("AvailabilityDateabaseId", Guid.NewGuid().ToString()));
            states.Add(state1);

            PSObject state2 = new PSObject();
            state2.Properties.Add(new PSNoteProperty("AvailabilityDateabaseId", dbId));
            state2.Properties.Add(new PSNoteProperty("DatabaseName", "TargetDB"));
            states.Add(state2);

            PSObject result = GetDbaAgDatabaseReplicaStateCommand.FindByDatabaseId(states, dbId);

            Assert.IsNotNull(result);
            Assert.AreEqual("TargetDB", GetDbaAgDatabaseReplicaStateCommand.GetPropertyString(result, "DatabaseName"));
        }

        [TestMethod]
        public void FindByDatabaseId_NullListReturnsNull()
        {
            PSObject result = GetDbaAgDatabaseReplicaStateCommand.FindByDatabaseId(null, "some-id");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void FindByDatabaseId_NullDatabaseIdReturnsNull()
        {
            List<PSObject> states = new List<PSObject>();
            PSObject state = new PSObject();
            state.Properties.Add(new PSNoteProperty("AvailabilityDateabaseId", "some-id"));
            states.Add(state);

            PSObject result = GetDbaAgDatabaseReplicaStateCommand.FindByDatabaseId(states, null);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void FindByDatabaseId_CaseInsensitiveMatch()
        {
            string dbId = "abc-def-123";

            List<PSObject> states = new List<PSObject>();
            PSObject state = new PSObject();
            state.Properties.Add(new PSNoteProperty("AvailabilityDateabaseId", "ABC-DEF-123"));
            state.Properties.Add(new PSNoteProperty("DatabaseName", "Found"));
            states.Add(state);

            PSObject result = GetDbaAgDatabaseReplicaStateCommand.FindByDatabaseId(states, dbId);
            Assert.IsNotNull(result);
            Assert.AreEqual("Found", GetDbaAgDatabaseReplicaStateCommand.GetPropertyString(result, "DatabaseName"));
        }

        [TestMethod]
        public void FindByDatabaseId_NoMatchReturnsNull()
        {
            List<PSObject> states = new List<PSObject>();
            PSObject state = new PSObject();
            state.Properties.Add(new PSNoteProperty("AvailabilityDateabaseId", "some-id"));
            states.Add(state);

            PSObject result = GetDbaAgDatabaseReplicaStateCommand.FindByDatabaseId(states, "different-id");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void FindByDatabaseId_SkipsNullEntries()
        {
            string dbId = "target-id";
            List<PSObject> states = new List<PSObject>();
            states.Add(null);

            PSObject state = new PSObject();
            state.Properties.Add(new PSNoteProperty("AvailabilityDateabaseId", dbId));
            state.Properties.Add(new PSNoteProperty("DatabaseName", "Found"));
            states.Add(state);

            PSObject result = GetDbaAgDatabaseReplicaStateCommand.FindByDatabaseId(states, dbId);
            Assert.IsNotNull(result);
        }

        [TestMethod]
        public void FindByDatabaseId_ReturnsFirstMatch()
        {
            string dbId = "target-id";
            List<PSObject> states = new List<PSObject>();

            PSObject state1 = new PSObject();
            state1.Properties.Add(new PSNoteProperty("AvailabilityDateabaseId", dbId));
            state1.Properties.Add(new PSNoteProperty("DatabaseName", "First"));
            states.Add(state1);

            PSObject state2 = new PSObject();
            state2.Properties.Add(new PSNoteProperty("AvailabilityDateabaseId", dbId));
            state2.Properties.Add(new PSNoteProperty("DatabaseName", "Second"));
            states.Add(state2);

            PSObject result = GetDbaAgDatabaseReplicaStateCommand.FindByDatabaseId(states, dbId);
            Assert.AreEqual("First", GetDbaAgDatabaseReplicaStateCommand.GetPropertyString(result, "DatabaseName"));
        }
        #endregion
    }
}
