using System;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class GetDbaAgentAlertCommandTests
    {
        #region GetAlertName
        [TestMethod]
        public void GetAlertName_ReturnsNameProperty()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "Test Alert"));

            string result = GetDbaAgentAlertCommand.GetAlertName(obj);

            Assert.AreEqual("Test Alert", result);
        }

        [TestMethod]
        public void GetAlertName_NullObjectReturnsNull()
        {
            string result = GetDbaAgentAlertCommand.GetAlertName(null);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetAlertName_MissingPropertyReturnsNull()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("ID", 1));

            string result = GetDbaAgentAlertCommand.GetAlertName(obj);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetAlertName_NullValueReturnsNull()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", null));

            string result = GetDbaAgentAlertCommand.GetAlertName(obj);
            Assert.IsNull(result);
        }
        #endregion

        #region FilterIncludeAlerts
        [TestMethod]
        public void FilterIncludeAlerts_ExactMatch()
        {
            Collection<PSObject> alerts = CreateTestAlerts("Alert1", "Alert2", "Alert3");
            string[] patterns = new string[] { "Alert1" };

            Collection<PSObject> result = GetDbaAgentAlertCommand.FilterIncludeAlerts(alerts, patterns);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("Alert1", GetDbaAgentAlertCommand.GetAlertName(result[0]));
        }

        [TestMethod]
        public void FilterIncludeAlerts_WildcardMatch()
        {
            Collection<PSObject> alerts = CreateTestAlerts("Severity 016", "Severity 017", "DB Mail Alert");
            string[] patterns = new string[] { "Severity*" };

            Collection<PSObject> result = GetDbaAgentAlertCommand.FilterIncludeAlerts(alerts, patterns);

            Assert.AreEqual(2, result.Count);
        }

        [TestMethod]
        public void FilterIncludeAlerts_MultiplePatterns()
        {
            Collection<PSObject> alerts = CreateTestAlerts("Severity 016", "DB Mail Alert", "Custom Alert");
            string[] patterns = new string[] { "Severity*", "Custom*" };

            Collection<PSObject> result = GetDbaAgentAlertCommand.FilterIncludeAlerts(alerts, patterns);

            Assert.AreEqual(2, result.Count);
        }

        [TestMethod]
        public void FilterIncludeAlerts_NoDuplicates()
        {
            Collection<PSObject> alerts = CreateTestAlerts("Alert1", "Alert2");
            string[] patterns = new string[] { "Alert*", "Alert1" };

            Collection<PSObject> result = GetDbaAgentAlertCommand.FilterIncludeAlerts(alerts, patterns);

            Assert.AreEqual(2, result.Count);
        }

        [TestMethod]
        public void FilterIncludeAlerts_CaseInsensitive()
        {
            Collection<PSObject> alerts = CreateTestAlerts("MyAlert");
            string[] patterns = new string[] { "myalert" };

            Collection<PSObject> result = GetDbaAgentAlertCommand.FilterIncludeAlerts(alerts, patterns);

            Assert.AreEqual(1, result.Count);
        }

        [TestMethod]
        public void FilterIncludeAlerts_NoMatchReturnsEmpty()
        {
            Collection<PSObject> alerts = CreateTestAlerts("Alert1", "Alert2");
            string[] patterns = new string[] { "NonExistent*" };

            Collection<PSObject> result = GetDbaAgentAlertCommand.FilterIncludeAlerts(alerts, patterns);

            Assert.AreEqual(0, result.Count);
        }
        #endregion

        #region FilterExcludeAlerts
        [TestMethod]
        public void FilterExcludeAlerts_ExcludesMatchingAlerts()
        {
            Collection<PSObject> alerts = CreateTestAlerts("Alert1", "Alert2", "Alert3");
            string[] patterns = new string[] { "Alert2" };

            Collection<PSObject> result = GetDbaAgentAlertCommand.FilterExcludeAlerts(alerts, patterns);

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("Alert1", GetDbaAgentAlertCommand.GetAlertName(result[0]));
            Assert.AreEqual("Alert3", GetDbaAgentAlertCommand.GetAlertName(result[1]));
        }

        [TestMethod]
        public void FilterExcludeAlerts_WildcardExclude()
        {
            Collection<PSObject> alerts = CreateTestAlerts("Severity 016", "Severity 017", "DB Mail Alert");
            string[] patterns = new string[] { "Severity*" };

            Collection<PSObject> result = GetDbaAgentAlertCommand.FilterExcludeAlerts(alerts, patterns);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("DB Mail Alert", GetDbaAgentAlertCommand.GetAlertName(result[0]));
        }

        [TestMethod]
        public void FilterExcludeAlerts_NoMatchReturnsAll()
        {
            Collection<PSObject> alerts = CreateTestAlerts("Alert1", "Alert2");
            string[] patterns = new string[] { "NonExistent*" };

            Collection<PSObject> result = GetDbaAgentAlertCommand.FilterExcludeAlerts(alerts, patterns);

            Assert.AreEqual(2, result.Count);
        }

        [TestMethod]
        public void FilterExcludeAlerts_CaseInsensitive()
        {
            Collection<PSObject> alerts = CreateTestAlerts("MyAlert", "Other");
            string[] patterns = new string[] { "myalert" };

            Collection<PSObject> result = GetDbaAgentAlertCommand.FilterExcludeAlerts(alerts, patterns);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("Other", GetDbaAgentAlertCommand.GetAlertName(result[0]));
        }
        #endregion

        #region AddOrSetProperty
        [TestMethod]
        public void AddOrSetProperty_AddsNewProperty()
        {
            PSObject obj = new PSObject();
            GetDbaAgentAlertCommand.AddOrSetProperty(obj, "ComputerName", "sql01");

            Assert.AreEqual("sql01", obj.Properties["ComputerName"].Value);
        }

        [TestMethod]
        public void AddOrSetProperty_UpdatesExistingProperty()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("ComputerName", "old"));

            GetDbaAgentAlertCommand.AddOrSetProperty(obj, "ComputerName", "new");

            Assert.AreEqual("new", obj.Properties["ComputerName"].Value);
        }

        [TestMethod]
        public void AddOrSetProperty_NullObjectDoesNotThrow()
        {
            GetDbaAgentAlertCommand.AddOrSetProperty(null, "Name", "value");
        }

        [TestMethod]
        public void AddOrSetProperty_SetsNullValue()
        {
            PSObject obj = new PSObject();
            GetDbaAgentAlertCommand.AddOrSetProperty(obj, "Prop", null);

            Assert.IsNull(obj.Properties["Prop"].Value);
        }
        #endregion

        #region SetDefaultDisplayPropertySet
        [TestMethod]
        public void SetDefaultDisplayPropertySet_AddsStandardMembers()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "Test"));
            obj.Properties.Add(new PSNoteProperty("ID", 1));

            string[] props = new string[] { "Name", "ID" };
            GetDbaAgentAlertCommand.SetDefaultDisplayPropertySet(obj, props);

            PSMemberInfo member = obj.Members["PSStandardMembers"];
            Assert.IsNotNull(member);
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_NullObjectDoesNotThrow()
        {
            GetDbaAgentAlertCommand.SetDefaultDisplayPropertySet(null, new string[] { "Name" });
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_NullPropertiesDoesNotThrow()
        {
            PSObject obj = new PSObject();
            GetDbaAgentAlertCommand.SetDefaultDisplayPropertySet(obj, null);
        }
        #endregion

        #region GetAlertDateProperty
        [TestMethod]
        public void GetAlertDateProperty_ReturnsDateTimeValue()
        {
            PSObject obj = new PSObject();
            DateTime expected = new DateTime(2024, 6, 15, 10, 30, 0);
            obj.Properties.Add(new PSNoteProperty("LastOccurrenceDate", expected));

            DateTime result = GetDbaAgentAlertCommand.GetAlertDateProperty(obj, "LastOccurrenceDate");

            Assert.AreEqual(expected, result);
        }

        [TestMethod]
        public void GetAlertDateProperty_ParsesStringDateTime()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("LastOccurrenceDate", "2024-06-15T10:30:00"));

            DateTime result = GetDbaAgentAlertCommand.GetAlertDateProperty(obj, "LastOccurrenceDate");

            Assert.AreEqual(new DateTime(2024, 6, 15, 10, 30, 0), result);
        }

        [TestMethod]
        public void GetAlertDateProperty_MissingPropertyReturnsMinValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "Test"));

            DateTime result = GetDbaAgentAlertCommand.GetAlertDateProperty(obj, "LastOccurrenceDate");

            Assert.AreEqual(DateTime.MinValue, result);
        }

        [TestMethod]
        public void GetAlertDateProperty_NullValueReturnsMinValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("LastOccurrenceDate", null));

            DateTime result = GetDbaAgentAlertCommand.GetAlertDateProperty(obj, "LastOccurrenceDate");

            Assert.AreEqual(DateTime.MinValue, result);
        }

        [TestMethod]
        public void GetAlertDateProperty_InvalidStringReturnsMinValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("LastOccurrenceDate", "not-a-date"));

            DateTime result = GetDbaAgentAlertCommand.GetAlertDateProperty(obj, "LastOccurrenceDate");

            Assert.AreEqual(DateTime.MinValue, result);
        }
        #endregion

        #region GetServerPropertySafe
        [TestMethod]
        public void GetServerPropertySafe_ReturnsPropertyValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("ComputerName", "sql01"));

            string result = GetDbaAgentAlertCommand.GetServerPropertySafe(obj, "ComputerName");

            Assert.AreEqual("sql01", result);
        }

        [TestMethod]
        public void GetServerPropertySafe_NullObjectReturnsNull()
        {
            string result = GetDbaAgentAlertCommand.GetServerPropertySafe(null, "ComputerName");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetServerPropertySafe_MissingPropertyReturnsNull()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "sql01"));

            string result = GetDbaAgentAlertCommand.GetServerPropertySafe(obj, "ComputerName");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetServerPropertySafe_NullValueReturnsNull()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Edition", null));

            string result = GetDbaAgentAlertCommand.GetServerPropertySafe(obj, "Edition");
            Assert.IsNull(result);
        }
        #endregion

        #region Test Helpers
        private static Collection<PSObject> CreateTestAlerts(params string[] names)
        {
            Collection<PSObject> alerts = new Collection<PSObject>();
            for (int i = 0; i < names.Length; i++)
            {
                PSObject alert = new PSObject();
                alert.Properties.Add(new PSNoteProperty("Name", names[i]));
                alert.Properties.Add(new PSNoteProperty("ID", i + 1));
                alerts.Add(alert);
            }
            return alerts;
        }
        #endregion
    }
}
