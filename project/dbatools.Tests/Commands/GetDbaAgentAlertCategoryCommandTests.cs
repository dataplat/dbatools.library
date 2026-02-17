using System;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class GetDbaAgentAlertCategoryCommandTests
    {
        #region FilterByCategory

        [TestMethod]
        public void FilterByCategory_MatchingNames_ReturnsMatches()
        {
            // Arrange
            Collection<PSObject> categories = new Collection<PSObject>();
            PSObject cat1 = new PSObject();
            cat1.Properties.Add(new PSNoteProperty("Name", "Severity Alert"));
            PSObject cat2 = new PSObject();
            cat2.Properties.Add(new PSNoteProperty("Name", "Database Maintenance"));
            PSObject cat3 = new PSObject();
            cat3.Properties.Add(new PSNoteProperty("Name", "Custom Alert"));
            categories.Add(cat1);
            categories.Add(cat2);
            categories.Add(cat3);

            string[] filter = new string[] { "Severity Alert", "Custom Alert" };

            // Act
            Collection<PSObject> result = GetDbaAgentAlertCategoryCommand.FilterByCategory(categories, filter);

            // Assert
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("Severity Alert", result[0].Properties["Name"].Value.ToString());
            Assert.AreEqual("Custom Alert", result[1].Properties["Name"].Value.ToString());
        }

        [TestMethod]
        public void FilterByCategory_CaseInsensitive_ReturnsMatches()
        {
            // Arrange
            Collection<PSObject> categories = new Collection<PSObject>();
            PSObject cat1 = new PSObject();
            cat1.Properties.Add(new PSNoteProperty("Name", "Severity Alert"));
            categories.Add(cat1);

            string[] filter = new string[] { "severity alert" };

            // Act
            Collection<PSObject> result = GetDbaAgentAlertCategoryCommand.FilterByCategory(categories, filter);

            // Assert
            Assert.AreEqual(1, result.Count);
        }

        [TestMethod]
        public void FilterByCategory_NoMatch_ReturnsEmpty()
        {
            // Arrange
            Collection<PSObject> categories = new Collection<PSObject>();
            PSObject cat1 = new PSObject();
            cat1.Properties.Add(new PSNoteProperty("Name", "Severity Alert"));
            categories.Add(cat1);

            string[] filter = new string[] { "NonExistent" };

            // Act
            Collection<PSObject> result = GetDbaAgentAlertCategoryCommand.FilterByCategory(categories, filter);

            // Assert
            Assert.AreEqual(0, result.Count);
        }

        #endregion FilterByCategory

        #region CountAlertsForCategory

        [TestMethod]
        public void CountAlertsForCategory_MatchingAlerts_ReturnsCorrectCount()
        {
            // Arrange
            Collection<PSObject> alerts = new Collection<PSObject>();
            PSObject alert1 = new PSObject();
            alert1.Properties.Add(new PSNoteProperty("CategoryName", "Severity Alert"));
            PSObject alert2 = new PSObject();
            alert2.Properties.Add(new PSNoteProperty("CategoryName", "Severity Alert"));
            PSObject alert3 = new PSObject();
            alert3.Properties.Add(new PSNoteProperty("CategoryName", "Other Category"));
            alerts.Add(alert1);
            alerts.Add(alert2);
            alerts.Add(alert3);

            // Act
            int count = GetDbaAgentAlertCategoryCommand.CountAlertsForCategory(alerts, "Severity Alert");

            // Assert
            Assert.AreEqual(2, count);
        }

        [TestMethod]
        public void CountAlertsForCategory_NullAlerts_ReturnsZero()
        {
            // Act
            int count = GetDbaAgentAlertCategoryCommand.CountAlertsForCategory(null, "Test");

            // Assert
            Assert.AreEqual(0, count);
        }

        [TestMethod]
        public void CountAlertsForCategory_EmptyAlerts_ReturnsZero()
        {
            // Arrange
            Collection<PSObject> alerts = new Collection<PSObject>();

            // Act
            int count = GetDbaAgentAlertCategoryCommand.CountAlertsForCategory(alerts, "Test");

            // Assert
            Assert.AreEqual(0, count);
        }

        [TestMethod]
        public void CountAlertsForCategory_NullCategoryName_ReturnsZero()
        {
            // Arrange
            Collection<PSObject> alerts = new Collection<PSObject>();
            PSObject alert1 = new PSObject();
            alert1.Properties.Add(new PSNoteProperty("CategoryName", "Test"));
            alerts.Add(alert1);

            // Act
            int count = GetDbaAgentAlertCategoryCommand.CountAlertsForCategory(alerts, null);

            // Assert
            Assert.AreEqual(0, count);
        }

        [TestMethod]
        public void CountAlertsForCategory_CaseInsensitive_ReturnsCorrectCount()
        {
            // Arrange
            Collection<PSObject> alerts = new Collection<PSObject>();
            PSObject alert1 = new PSObject();
            alert1.Properties.Add(new PSNoteProperty("CategoryName", "severity alert"));
            alerts.Add(alert1);

            // Act
            int count = GetDbaAgentAlertCategoryCommand.CountAlertsForCategory(alerts, "Severity Alert");

            // Assert
            Assert.AreEqual(1, count);
        }

        [TestMethod]
        public void CountAlertsForCategory_NullAlertInCollection_SkipsNull()
        {
            // Arrange
            Collection<PSObject> alerts = new Collection<PSObject>();
            alerts.Add(null);
            PSObject alert1 = new PSObject();
            alert1.Properties.Add(new PSNoteProperty("CategoryName", "Test"));
            alerts.Add(alert1);

            // Act
            int count = GetDbaAgentAlertCategoryCommand.CountAlertsForCategory(alerts, "Test");

            // Assert
            Assert.AreEqual(1, count);
        }

        #endregion CountAlertsForCategory

        #region GetPropertySafe

        [TestMethod]
        public void GetPropertySafe_ExistingProperty_ReturnsValue()
        {
            // Arrange
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "TestCategory"));

            // Act
            string result = GetDbaAgentAlertCategoryCommand.GetPropertySafe(obj, "Name");

            // Assert
            Assert.AreEqual("TestCategory", result);
        }

        [TestMethod]
        public void GetPropertySafe_MissingProperty_ReturnsNull()
        {
            // Arrange
            PSObject obj = new PSObject();

            // Act
            string result = GetDbaAgentAlertCategoryCommand.GetPropertySafe(obj, "Name");

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertySafe_NullObject_ReturnsNull()
        {
            // Act
            string result = GetDbaAgentAlertCategoryCommand.GetPropertySafe(null, "Name");

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertySafe_NullPropertyValue_ReturnsNull()
        {
            // Arrange
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", null));

            // Act
            string result = GetDbaAgentAlertCategoryCommand.GetPropertySafe(obj, "Name");

            // Assert
            Assert.IsNull(result);
        }

        #endregion GetPropertySafe

        #region AddOrSetProperty

        [TestMethod]
        public void AddOrSetProperty_NewProperty_AddsProperty()
        {
            // Arrange
            PSObject obj = new PSObject();

            // Act
            GetDbaAgentAlertCategoryCommand.AddOrSetProperty(obj, "AlertCount", 5);

            // Assert
            Assert.AreEqual(5, obj.Properties["AlertCount"].Value);
        }

        [TestMethod]
        public void AddOrSetProperty_ExistingProperty_UpdatesValue()
        {
            // Arrange
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("AlertCount", 3));

            // Act
            GetDbaAgentAlertCategoryCommand.AddOrSetProperty(obj, "AlertCount", 7);

            // Assert
            Assert.AreEqual(7, obj.Properties["AlertCount"].Value);
        }

        [TestMethod]
        public void AddOrSetProperty_NullObject_DoesNotThrow()
        {
            // Act - should not throw
            GetDbaAgentAlertCategoryCommand.AddOrSetProperty(null, "Test", "Value");
        }

        #endregion AddOrSetProperty

        #region SetDefaultDisplayPropertySet

        [TestMethod]
        public void SetDefaultDisplayPropertySet_ValidInput_AddsPSStandardMembers()
        {
            // Arrange
            PSObject obj = new PSObject();
            string[] props = new string[] { "ComputerName", "Name", "AlertCount" };

            // Act
            GetDbaAgentAlertCategoryCommand.SetDefaultDisplayPropertySet(obj, props);

            // Assert
            PSMemberInfo member = obj.Members["PSStandardMembers"];
            Assert.IsNotNull(member);
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_NullObject_DoesNotThrow()
        {
            // Act - should not throw
            GetDbaAgentAlertCategoryCommand.SetDefaultDisplayPropertySet(null, new string[] { "Name" });
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_NullProperties_DoesNotThrow()
        {
            // Arrange
            PSObject obj = new PSObject();

            // Act - should not throw
            GetDbaAgentAlertCategoryCommand.SetDefaultDisplayPropertySet(obj, null);
        }

        #endregion SetDefaultDisplayPropertySet

        #region GetServerPropertySafe

        [TestMethod]
        public void GetServerPropertySafe_ExistingProperty_ReturnsValue()
        {
            // Arrange - use a PSObject as the server stand-in
            PSObject server = new PSObject();
            server.Properties.Add(new PSNoteProperty("ComputerName", "SQL01"));

            // Act
            string result = GetDbaAgentAlertCategoryCommand.GetServerPropertySafe(server, "ComputerName");

            // Assert
            Assert.AreEqual("SQL01", result);
        }

        [TestMethod]
        public void GetServerPropertySafe_MissingProperty_ReturnsNull()
        {
            // Arrange
            PSObject server = new PSObject();

            // Act
            string result = GetDbaAgentAlertCategoryCommand.GetServerPropertySafe(server, "ComputerName");

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetServerPropertySafe_NullObject_ReturnsNull()
        {
            // Act
            string result = GetDbaAgentAlertCategoryCommand.GetServerPropertySafe(null, "ComputerName");

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetServerPropertySafe_NullPropertyValue_ReturnsNull()
        {
            // Arrange
            PSObject server = new PSObject();
            server.Properties.Add(new PSNoteProperty("ComputerName", null));

            // Act
            string result = GetDbaAgentAlertCategoryCommand.GetServerPropertySafe(server, "ComputerName");

            // Assert
            Assert.IsNull(result);
        }

        #endregion GetServerPropertySafe
    }
}
