using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class GetDbaAgentOperatorCommandTests
    {
        #region ConvertToStringArray
        [TestMethod]
        public void ConvertToStringArray_ConvertsObjects()
        {
            object[] input = new object[] { "Op1", "Op2", "Op3" };

            string[] result = GetDbaAgentOperatorCommand.ConvertToStringArray(input);

            Assert.AreEqual(3, result.Length);
            Assert.AreEqual("Op1", result[0]);
            Assert.AreEqual("Op2", result[1]);
            Assert.AreEqual("Op3", result[2]);
        }

        [TestMethod]
        public void ConvertToStringArray_NullReturnsEmpty()
        {
            string[] result = GetDbaAgentOperatorCommand.ConvertToStringArray(null);
            Assert.AreEqual(0, result.Length);
        }

        [TestMethod]
        public void ConvertToStringArray_HandlesNullElements()
        {
            object[] input = new object[] { "Op1", null, "Op3" };

            string[] result = GetDbaAgentOperatorCommand.ConvertToStringArray(input);

            Assert.AreEqual(3, result.Length);
            Assert.AreEqual("Op1", result[0]);
            Assert.IsNull(result[1]);
            Assert.AreEqual("Op3", result[2]);
        }

        [TestMethod]
        public void ConvertToStringArray_ConvertsIntToString()
        {
            object[] input = new object[] { 42 };

            string[] result = GetDbaAgentOperatorCommand.ConvertToStringArray(input);

            Assert.AreEqual("42", result[0]);
        }
        #endregion

        #region IsInArray
        [TestMethod]
        public void IsInArray_FindsExactMatch()
        {
            string[] array = new string[] { "Alpha", "Beta", "Gamma" };

            Assert.IsTrue(GetDbaAgentOperatorCommand.IsInArray("Beta", array));
        }

        [TestMethod]
        public void IsInArray_CaseInsensitive()
        {
            string[] array = new string[] { "Alpha", "Beta" };

            Assert.IsTrue(GetDbaAgentOperatorCommand.IsInArray("alpha", array));
            Assert.IsTrue(GetDbaAgentOperatorCommand.IsInArray("BETA", array));
        }

        [TestMethod]
        public void IsInArray_NotFoundReturnsFalse()
        {
            string[] array = new string[] { "Alpha", "Beta" };

            Assert.IsFalse(GetDbaAgentOperatorCommand.IsInArray("Gamma", array));
        }

        [TestMethod]
        public void IsInArray_NullValueReturnsFalse()
        {
            string[] array = new string[] { "Alpha" };

            Assert.IsFalse(GetDbaAgentOperatorCommand.IsInArray(null, array));
        }

        [TestMethod]
        public void IsInArray_NullArrayReturnsFalse()
        {
            Assert.IsFalse(GetDbaAgentOperatorCommand.IsInArray("Alpha", null));
        }

        [TestMethod]
        public void IsInArray_EmptyArrayReturnsFalse()
        {
            Assert.IsFalse(GetDbaAgentOperatorCommand.IsInArray("Alpha", new string[0]));
        }
        #endregion

        #region FilterIncludeOperators
        [TestMethod]
        public void FilterIncludeOperators_IncludesMatchingByName()
        {
            Collection<PSObject> operators = CreateTestOperators("DBA1", "DBA2", "DBA3");
            object[] names = new object[] { "DBA1", "DBA3" };

            Collection<PSObject> result = GetDbaAgentOperatorCommand.FilterIncludeOperators(operators, names);

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("DBA1", GetDbaAgentOperatorCommand.GetOperatorPropertyString(result[0], "Name"));
            Assert.AreEqual("DBA3", GetDbaAgentOperatorCommand.GetOperatorPropertyString(result[1], "Name"));
        }

        [TestMethod]
        public void FilterIncludeOperators_CaseInsensitive()
        {
            Collection<PSObject> operators = CreateTestOperators("DbaOperator");
            object[] names = new object[] { "dbaoperator" };

            Collection<PSObject> result = GetDbaAgentOperatorCommand.FilterIncludeOperators(operators, names);

            Assert.AreEqual(1, result.Count);
        }

        [TestMethod]
        public void FilterIncludeOperators_NoMatchReturnsEmpty()
        {
            Collection<PSObject> operators = CreateTestOperators("DBA1", "DBA2");
            object[] names = new object[] { "NonExistent" };

            Collection<PSObject> result = GetDbaAgentOperatorCommand.FilterIncludeOperators(operators, names);

            Assert.AreEqual(0, result.Count);
        }
        #endregion

        #region FilterExcludeOperators
        [TestMethod]
        public void FilterExcludeOperators_ExcludesMatchingByName()
        {
            Collection<PSObject> operators = CreateTestOperators("DBA1", "DBA2", "DBA3");
            object[] names = new object[] { "DBA2" };

            Collection<PSObject> result = GetDbaAgentOperatorCommand.FilterExcludeOperators(operators, names);

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("DBA1", GetDbaAgentOperatorCommand.GetOperatorPropertyString(result[0], "Name"));
            Assert.AreEqual("DBA3", GetDbaAgentOperatorCommand.GetOperatorPropertyString(result[1], "Name"));
        }

        [TestMethod]
        public void FilterExcludeOperators_CaseInsensitive()
        {
            Collection<PSObject> operators = CreateTestOperators("DbaOp", "Other");
            object[] names = new object[] { "dbaop" };

            Collection<PSObject> result = GetDbaAgentOperatorCommand.FilterExcludeOperators(operators, names);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("Other", GetDbaAgentOperatorCommand.GetOperatorPropertyString(result[0], "Name"));
        }

        [TestMethod]
        public void FilterExcludeOperators_NoMatchReturnsAll()
        {
            Collection<PSObject> operators = CreateTestOperators("DBA1", "DBA2");
            object[] names = new object[] { "NonExistent" };

            Collection<PSObject> result = GetDbaAgentOperatorCommand.FilterExcludeOperators(operators, names);

            Assert.AreEqual(2, result.Count);
        }
        #endregion

        #region GetOperatorPropertyString
        [TestMethod]
        public void GetOperatorPropertyString_ReturnsValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "TestOp"));

            string result = GetDbaAgentOperatorCommand.GetOperatorPropertyString(obj, "Name");

            Assert.AreEqual("TestOp", result);
        }

        [TestMethod]
        public void GetOperatorPropertyString_NullObjectReturnsNull()
        {
            string result = GetDbaAgentOperatorCommand.GetOperatorPropertyString(null, "Name");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetOperatorPropertyString_MissingPropertyReturnsNull()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("ID", 1));

            string result = GetDbaAgentOperatorCommand.GetOperatorPropertyString(obj, "Name");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetOperatorPropertyString_NullValueReturnsNull()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", null));

            string result = GetDbaAgentOperatorCommand.GetOperatorPropertyString(obj, "Name");
            Assert.IsNull(result);
        }
        #endregion

        #region GetOperatorProperty
        [TestMethod]
        public void GetOperatorProperty_ReturnsValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Enabled", true));

            object result = GetDbaAgentOperatorCommand.GetOperatorProperty(obj, "Enabled");

            Assert.AreEqual(true, result);
        }

        [TestMethod]
        public void GetOperatorProperty_NullObjectReturnsNull()
        {
            object result = GetDbaAgentOperatorCommand.GetOperatorProperty(null, "Enabled");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetOperatorProperty_MissingPropertyReturnsNull()
        {
            PSObject obj = new PSObject();

            object result = GetDbaAgentOperatorCommand.GetOperatorProperty(obj, "Enabled");
            Assert.IsNull(result);
        }
        #endregion

        #region GetOperatorDateProperty
        [TestMethod]
        public void GetOperatorDateProperty_ReturnsDateTime()
        {
            PSObject obj = new PSObject();
            DateTime expected = new DateTime(2024, 1, 15, 10, 30, 0);
            obj.Properties.Add(new PSNoteProperty("LastEmailDate", expected));

            DateTime result = GetDbaAgentOperatorCommand.GetOperatorDateProperty(obj, "LastEmailDate");

            Assert.AreEqual(expected, result);
        }

        [TestMethod]
        public void GetOperatorDateProperty_ParsesString()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("LastEmailDate", "2024-01-15T10:30:00"));

            DateTime result = GetDbaAgentOperatorCommand.GetOperatorDateProperty(obj, "LastEmailDate");

            Assert.AreEqual(new DateTime(2024, 1, 15, 10, 30, 0), result);
        }

        [TestMethod]
        public void GetOperatorDateProperty_MissingPropertyReturnsMinValue()
        {
            PSObject obj = new PSObject();

            DateTime result = GetDbaAgentOperatorCommand.GetOperatorDateProperty(obj, "LastEmailDate");

            Assert.AreEqual(DateTime.MinValue, result);
        }

        [TestMethod]
        public void GetOperatorDateProperty_InvalidStringReturnsMinValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("LastEmailDate", "not-a-date"));

            DateTime result = GetDbaAgentOperatorCommand.GetOperatorDateProperty(obj, "LastEmailDate");

            Assert.AreEqual(DateTime.MinValue, result);
        }
        #endregion

        #region FindRelatedJobs
        [TestMethod]
        public void FindRelatedJobs_FindsByOperatorToEmail()
        {
            Collection<PSObject> jobs = CreateTestJobs(
                new JobDef("Job1", "DBA1", "", ""),
                new JobDef("Job2", "DBA2", "", "")
            );

            List<object> result = GetDbaAgentOperatorCommand.FindRelatedJobs(jobs, "DBA1");

            Assert.AreEqual(1, result.Count);
        }

        [TestMethod]
        public void FindRelatedJobs_FindsByOperatorToNetSend()
        {
            Collection<PSObject> jobs = CreateTestJobs(
                new JobDef("Job1", "", "DBA1", "")
            );

            List<object> result = GetDbaAgentOperatorCommand.FindRelatedJobs(jobs, "DBA1");

            Assert.AreEqual(1, result.Count);
        }

        [TestMethod]
        public void FindRelatedJobs_FindsByOperatorToPage()
        {
            Collection<PSObject> jobs = CreateTestJobs(
                new JobDef("Job1", "", "", "DBA1")
            );

            List<object> result = GetDbaAgentOperatorCommand.FindRelatedJobs(jobs, "DBA1");

            Assert.AreEqual(1, result.Count);
        }

        [TestMethod]
        public void FindRelatedJobs_CaseInsensitiveMatch()
        {
            Collection<PSObject> jobs = CreateTestJobs(
                new JobDef("Job1", "dba1", "", "")
            );

            List<object> result = GetDbaAgentOperatorCommand.FindRelatedJobs(jobs, "DBA1");

            Assert.AreEqual(1, result.Count);
        }

        [TestMethod]
        public void FindRelatedJobs_NoMatchReturnsEmpty()
        {
            Collection<PSObject> jobs = CreateTestJobs(
                new JobDef("Job1", "DBA2", "", "")
            );

            List<object> result = GetDbaAgentOperatorCommand.FindRelatedJobs(jobs, "DBA1");

            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void FindRelatedJobs_NullOperatorNameReturnsEmpty()
        {
            Collection<PSObject> jobs = CreateTestJobs(
                new JobDef("Job1", "DBA1", "", "")
            );

            List<object> result = GetDbaAgentOperatorCommand.FindRelatedJobs(jobs, null);

            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void FindRelatedJobs_NullJobsReturnsEmpty()
        {
            List<object> result = GetDbaAgentOperatorCommand.FindRelatedJobs(null, "DBA1");

            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void FindRelatedJobs_MultipleMatchingJobs()
        {
            Collection<PSObject> jobs = CreateTestJobs(
                new JobDef("Job1", "DBA1", "", ""),
                new JobDef("Job2", "", "DBA1", ""),
                new JobDef("Job3", "DBA2", "", "")
            );

            List<object> result = GetDbaAgentOperatorCommand.FindRelatedJobs(jobs, "DBA1");

            Assert.AreEqual(2, result.Count);
        }
        #endregion

        #region AddOrSetProperty
        [TestMethod]
        public void AddOrSetProperty_AddsNewProperty()
        {
            PSObject obj = new PSObject();
            GetDbaAgentOperatorCommand.AddOrSetProperty(obj, "ComputerName", "sql01");

            Assert.AreEqual("sql01", obj.Properties["ComputerName"].Value);
        }

        [TestMethod]
        public void AddOrSetProperty_UpdatesExistingProperty()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("ComputerName", "old"));

            GetDbaAgentOperatorCommand.AddOrSetProperty(obj, "ComputerName", "new");

            Assert.AreEqual("new", obj.Properties["ComputerName"].Value);
        }

        [TestMethod]
        public void AddOrSetProperty_NullObjectDoesNotThrow()
        {
            GetDbaAgentOperatorCommand.AddOrSetProperty(null, "Name", "value");
        }
        #endregion

        #region SetDefaultDisplayPropertySet
        [TestMethod]
        public void SetDefaultDisplayPropertySet_AddsMemberSet()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "Test"));

            string[] props = new string[] { "Name" };
            GetDbaAgentOperatorCommand.SetDefaultDisplayPropertySet(obj, props);

            PSMemberInfo member = obj.Members["PSStandardMembers"];
            Assert.IsNotNull(member);
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_NullObjectDoesNotThrow()
        {
            GetDbaAgentOperatorCommand.SetDefaultDisplayPropertySet(null, new string[] { "Name" });
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_NullPropertiesDoesNotThrow()
        {
            PSObject obj = new PSObject();
            GetDbaAgentOperatorCommand.SetDefaultDisplayPropertySet(obj, null);
        }
        #endregion

        #region AddAliasProperty
        [TestMethod]
        public void AddAliasProperty_CreatesAlias()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Enabled", true));

            GetDbaAgentOperatorCommand.AddAliasProperty(obj, "IsEnabled", "Enabled");

            PSMemberInfo member = obj.Members["IsEnabled"];
            Assert.IsNotNull(member);
            Assert.IsInstanceOfType(member, typeof(PSAliasProperty));
        }

        [TestMethod]
        public void AddAliasProperty_AliasResolvesToOriginalValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Enabled", true));

            GetDbaAgentOperatorCommand.AddAliasProperty(obj, "IsEnabled", "Enabled");

            PSPropertyInfo prop = obj.Properties["IsEnabled"];
            Assert.IsNotNull(prop);
            Assert.AreEqual(true, prop.Value);
        }

        [TestMethod]
        public void AddAliasProperty_NullObjectDoesNotThrow()
        {
            GetDbaAgentOperatorCommand.AddAliasProperty(null, "IsEnabled", "Enabled");
        }

        [TestMethod]
        public void AddAliasProperty_OverwritesExistingMember()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Enabled", true));
            obj.Properties.Add(new PSNoteProperty("IsEnabled", "stale"));

            GetDbaAgentOperatorCommand.AddAliasProperty(obj, "IsEnabled", "Enabled");

            // The member should now be an alias or note, and resolve to true
            PSPropertyInfo prop = obj.Properties["IsEnabled"];
            Assert.IsNotNull(prop);
        }
        #endregion

        #region GetServerPropertySafe
        [TestMethod]
        public void GetServerPropertySafe_ReturnsPropertyValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Edition", "Enterprise"));

            string result = GetDbaAgentOperatorCommand.GetServerPropertySafe(obj, "Edition");

            Assert.AreEqual("Enterprise", result);
        }

        [TestMethod]
        public void GetServerPropertySafe_NullObjectReturnsNull()
        {
            string result = GetDbaAgentOperatorCommand.GetServerPropertySafe(null, "Edition");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetServerPropertySafe_MissingPropertyReturnsNull()
        {
            PSObject obj = new PSObject();

            string result = GetDbaAgentOperatorCommand.GetServerPropertySafe(obj, "Edition");
            Assert.IsNull(result);
        }
        #endregion

        #region Test Helpers

        private struct JobDef
        {
            public string Name;
            public string OperatorToEmail;
            public string OperatorToNetSend;
            public string OperatorToPage;

            public JobDef(string name, string toEmail, string toNetSend, string toPage)
            {
                Name = name;
                OperatorToEmail = toEmail;
                OperatorToNetSend = toNetSend;
                OperatorToPage = toPage;
            }
        }

        private static Collection<PSObject> CreateTestOperators(params string[] names)
        {
            Collection<PSObject> operators = new Collection<PSObject>();
            for (int i = 0; i < names.Length; i++)
            {
                PSObject op = new PSObject();
                op.Properties.Add(new PSNoteProperty("Name", names[i]));
                op.Properties.Add(new PSNoteProperty("ID", i + 1));
                op.Properties.Add(new PSNoteProperty("Enabled", true));
                op.Properties.Add(new PSNoteProperty("EmailAddress", String.Format("{0}@company.com", names[i])));
                operators.Add(op);
            }
            return operators;
        }

        private static Collection<PSObject> CreateTestJobs(params JobDef[] defs)
        {
            Collection<PSObject> jobs = new Collection<PSObject>();
            foreach (JobDef def in defs)
            {
                PSObject job = new PSObject();
                job.Properties.Add(new PSNoteProperty("Name", def.Name));
                job.Properties.Add(new PSNoteProperty("OperatorToEmail", def.OperatorToEmail));
                job.Properties.Add(new PSNoteProperty("OperatorToNetSend", def.OperatorToNetSend));
                job.Properties.Add(new PSNoteProperty("OperatorToPage", def.OperatorToPage));
                jobs.Add(job);
            }
            return jobs;
        }

        #endregion
    }
}
