using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class ConnectDbaInstanceCommandTests
    {
        #region ConvertConnectionString

        [TestMethod]
        public void ConvertConnectionString_ReplacesSynonyms_AllReplacements()
        {
            // Arrange
            string input = "Application Intent=ReadOnly;Connect Retry Count=3;Connect Retry Interval=10;Pool Blocking Period=Auto;Multiple Active Result Sets=True;Multiple Subnet Failover=True;Trust Server Certificate=True";

            // Act
            string result = ConnectDbaInstanceCommand.ConvertConnectionString(input);

            // Assert
            Assert.IsTrue(result.Contains("ApplicationIntent=ReadOnly"), "ApplicationIntent not replaced");
            Assert.IsTrue(result.Contains("ConnectRetryCount=3"), "ConnectRetryCount not replaced");
            Assert.IsTrue(result.Contains("ConnectRetryInterval=10"), "ConnectRetryInterval not replaced");
            Assert.IsTrue(result.Contains("PoolBlockingPeriod=Auto"), "PoolBlockingPeriod not replaced");
            Assert.IsTrue(result.Contains("MultipleActiveResultSets=True"), "MultipleActiveResultSets not replaced");
            Assert.IsTrue(result.Contains("MultiSubnetFailover=True"), "MultiSubnetFailover not replaced");
            Assert.IsTrue(result.Contains("TrustServerCertificate=True"), "TrustServerCertificate not replaced");
        }

        [TestMethod]
        public void ConvertConnectionString_NoSynonyms_ReturnsUnchanged()
        {
            // Arrange
            string input = "Data Source=server1;Initial Catalog=master;Integrated Security=True";

            // Act
            string result = ConnectDbaInstanceCommand.ConvertConnectionString(input);

            // Assert
            Assert.AreEqual(input, result);
        }

        [TestMethod]
        public void ConvertConnectionString_Null_ReturnsNull()
        {
            // Act
            string result = ConnectDbaInstanceCommand.ConvertConnectionString(null);

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void ConvertConnectionString_Empty_ReturnsEmpty()
        {
            // Act
            string result = ConnectDbaInstanceCommand.ConvertConnectionString("");

            // Assert
            Assert.AreEqual("", result);
        }

        [TestMethod]
        public void ConvertConnectionString_PartialMatch_OnlyReplacesMatches()
        {
            // Arrange - only has one synonym
            string input = "Trust Server Certificate=True;Data Source=srv1";

            // Act
            string result = ConnectDbaInstanceCommand.ConvertConnectionString(input);

            // Assert
            Assert.IsTrue(result.Contains("TrustServerCertificate=True"), "TrustServerCertificate not replaced");
            Assert.IsTrue(result.Contains("Data Source=srv1"), "Data Source should not be modified");
        }

        #endregion

        #region GetPropertyValue

        [TestMethod]
        public void GetPropertyValue_NullObject_ReturnsNull()
        {
            // Act
            object result = ConnectDbaInstanceCommand.GetPropertyValue(null, "Name");

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyValue_ValidProperty_ReturnsValue()
        {
            // Arrange
            var exception = new Exception("test message");

            // Act
            object result = ConnectDbaInstanceCommand.GetPropertyValue(exception, "Message");

            // Assert
            Assert.AreEqual("test message", result);
        }

        [TestMethod]
        public void GetPropertyValue_NonExistentProperty_ReturnsNull()
        {
            // Arrange
            var exception = new Exception("test");

            // Act
            object result = ConnectDbaInstanceCommand.GetPropertyValue(exception, "NonExistentProperty");

            // Assert
            Assert.IsNull(result);
        }

        #endregion

        #region Field Arrays

        [TestMethod]
        public void Fields2000Db_ContainsExpectedFields()
        {
            // The static field arrays should be properly initialized
            // We verify the key fields are present
            string[] fields = GetStaticField<string[]>("Fields2000Db");
            Assert.IsNotNull(fields);
            CollectionAssert.Contains(fields, "Name");
            CollectionAssert.Contains(fields, "Collation");
            CollectionAssert.Contains(fields, "CreateDate");
            CollectionAssert.Contains(fields, "ID");
            CollectionAssert.Contains(fields, "Owner");
            CollectionAssert.Contains(fields, "Status");
        }

        [TestMethod]
        public void Fields200xDb_IncludesBaseFieldsPlusExtras()
        {
            string[] fields200x = GetStaticField<string[]>("Fields200xDb");
            Assert.IsNotNull(fields200x);
            // Should include Fields2000Db items plus BrokerEnabled, etc.
            CollectionAssert.Contains(fields200x, "Name"); // from base
            CollectionAssert.Contains(fields200x, "BrokerEnabled"); // 200x extra
            CollectionAssert.Contains(fields200x, "Trustworthy"); // 200x extra
        }

        [TestMethod]
        public void Fields201xDb_IncludesAllFieldsPlusExtras()
        {
            string[] fields201x = GetStaticField<string[]>("Fields201xDb");
            Assert.IsNotNull(fields201x);
            // Should include all prior fields plus EncryptionEnabled, etc.
            CollectionAssert.Contains(fields201x, "Name"); // from base
            CollectionAssert.Contains(fields201x, "BrokerEnabled"); // from 200x
            CollectionAssert.Contains(fields201x, "ActiveConnections"); // 201x extra
            CollectionAssert.Contains(fields201x, "EncryptionEnabled"); // 201x extra
        }

        [TestMethod]
        public void FieldsJob_ContainsExpectedFields()
        {
            string[] fields = GetStaticField<string[]>("FieldsJob");
            Assert.IsNotNull(fields);
            CollectionAssert.Contains(fields, "LastRunOutcome");
            CollectionAssert.Contains(fields, "CurrentRunStatus");
            CollectionAssert.Contains(fields, "NextRunDate");
            CollectionAssert.Contains(fields, "Category");
        }

        #endregion

        #region Helpers

        private static T GetStaticField<T>(string fieldName) where T : class
        {
            var field = typeof(ConnectDbaInstanceCommand).GetField(fieldName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (field == null) return null;
            return field.GetValue(null) as T;
        }

        #endregion
    }
}
