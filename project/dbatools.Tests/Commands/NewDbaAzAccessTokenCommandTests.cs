using System;
using System.Collections;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class NewDbaAzAccessTokenCommandTests
    {
        #region GetResourceUrl
        [TestMethod]
        public void GetResourceUrl_AzureSqlDb_ReturnsCorrectUrl()
        {
            string result = NewDbaAzAccessTokenCommand.GetResourceUrl("AzureSqlDb");
            Assert.AreEqual("https://database.windows.net/", result);
        }

        [TestMethod]
        public void GetResourceUrl_ResourceManager_ReturnsCorrectUrl()
        {
            string result = NewDbaAzAccessTokenCommand.GetResourceUrl("ResourceManager");
            Assert.AreEqual("https://management.azure.com/", result);
        }

        [TestMethod]
        public void GetResourceUrl_KeyVault_ReturnsCorrectUrl()
        {
            string result = NewDbaAzAccessTokenCommand.GetResourceUrl("KeyVault");
            Assert.AreEqual("https://vault.azure.net/", result);
        }

        [TestMethod]
        public void GetResourceUrl_DataLake_ReturnsCorrectUrl()
        {
            string result = NewDbaAzAccessTokenCommand.GetResourceUrl("DataLake");
            Assert.AreEqual("https://datalake.azure.net/", result);
        }

        [TestMethod]
        public void GetResourceUrl_EventHubs_ReturnsCorrectUrl()
        {
            string result = NewDbaAzAccessTokenCommand.GetResourceUrl("EventHubs");
            Assert.AreEqual("https://eventhubs.azure.net/", result);
        }

        [TestMethod]
        public void GetResourceUrl_ServiceBus_ReturnsCorrectUrl()
        {
            string result = NewDbaAzAccessTokenCommand.GetResourceUrl("ServiceBus");
            Assert.AreEqual("https://servicebus.azure.net/", result);
        }

        [TestMethod]
        public void GetResourceUrl_Storage_ReturnsCorrectUrl()
        {
            string result = NewDbaAzAccessTokenCommand.GetResourceUrl("Storage");
            Assert.AreEqual("https://storage.azure.com/", result);
        }

        [TestMethod]
        public void GetResourceUrl_Null_ReturnsDefault()
        {
            string result = NewDbaAzAccessTokenCommand.GetResourceUrl(null);
            Assert.AreEqual("https://database.windows.net/", result);
        }

        [TestMethod]
        public void GetResourceUrl_Empty_ReturnsDefault()
        {
            string result = NewDbaAzAccessTokenCommand.GetResourceUrl("");
            Assert.AreEqual("https://database.windows.net/", result);
        }

        [TestMethod]
        public void GetResourceUrl_UnknownSubtype_ReturnsDefault()
        {
            string result = NewDbaAzAccessTokenCommand.GetResourceUrl("SomethingElse");
            Assert.AreEqual("https://database.windows.net/", result);
        }
        #endregion

        #region GetResourceFromConfig
        [TestMethod]
        public void GetResourceFromConfig_Null_ReturnsNull()
        {
            string result = NewDbaAzAccessTokenCommand.GetResourceFromConfig(null);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetResourceFromConfig_Hashtable_ReturnsResource()
        {
            Hashtable ht = new Hashtable();
            ht["Resource"] = "https://custom.resource.net/";
            string result = NewDbaAzAccessTokenCommand.GetResourceFromConfig(ht);
            Assert.AreEqual("https://custom.resource.net/", result);
        }

        [TestMethod]
        public void GetResourceFromConfig_HashtableNoKey_ReturnsNull()
        {
            Hashtable ht = new Hashtable();
            ht["SomeOtherKey"] = "value";
            string result = NewDbaAzAccessTokenCommand.GetResourceFromConfig(ht);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetResourceFromConfig_PSObject_ReturnsResource()
        {
            PSObject pso = new PSObject();
            pso.Properties.Add(new PSNoteProperty("Resource", "https://custom.resource.net/"));
            string result = NewDbaAzAccessTokenCommand.GetResourceFromConfig(pso);
            Assert.AreEqual("https://custom.resource.net/", result);
        }

        [TestMethod]
        public void GetResourceFromConfig_PSObjectNoProperty_ReturnsNull()
        {
            PSObject pso = new PSObject();
            pso.Properties.Add(new PSNoteProperty("SomeOtherProp", "value"));
            string result = NewDbaAzAccessTokenCommand.GetResourceFromConfig(pso);
            Assert.IsNull(result);
        }
        #endregion

        #region GetVersionFromConfig
        [TestMethod]
        public void GetVersionFromConfig_Null_ReturnsNull()
        {
            string result = NewDbaAzAccessTokenCommand.GetVersionFromConfig(null);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetVersionFromConfig_Hashtable_ReturnsVersion()
        {
            Hashtable ht = new Hashtable();
            ht["Version"] = "2020-06-01";
            string result = NewDbaAzAccessTokenCommand.GetVersionFromConfig(ht);
            Assert.AreEqual("2020-06-01", result);
        }

        [TestMethod]
        public void GetVersionFromConfig_HashtableNoKey_ReturnsNull()
        {
            Hashtable ht = new Hashtable();
            ht["Resource"] = "https://test.net/";
            string result = NewDbaAzAccessTokenCommand.GetVersionFromConfig(ht);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetVersionFromConfig_PSObject_ReturnsVersion()
        {
            PSObject pso = new PSObject();
            pso.Properties.Add(new PSNoteProperty("Version", "2021-01-01"));
            string result = NewDbaAzAccessTokenCommand.GetVersionFromConfig(pso);
            Assert.AreEqual("2021-01-01", result);
        }
        #endregion

        #region AllSubtypesMap
        [TestMethod]
        public void GetResourceUrl_AllSubtypes_ReturnTrailingSlash()
        {
            // All resource URLs should end with a trailing slash per Azure convention
            string[] subtypes = { "AzureSqlDb", "ResourceManager", "KeyVault", "DataLake", "EventHubs", "ServiceBus", "Storage" };
            foreach (string subtype in subtypes)
            {
                string url = NewDbaAzAccessTokenCommand.GetResourceUrl(subtype);
                Assert.IsTrue(url.EndsWith("/"), String.Format("Resource URL for {0} should end with '/'", subtype));
                Assert.IsTrue(url.StartsWith("https://"), String.Format("Resource URL for {0} should use HTTPS", subtype));
            }
        }
        #endregion
    }
}
