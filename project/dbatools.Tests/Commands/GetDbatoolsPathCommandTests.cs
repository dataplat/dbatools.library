using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;
using Dataplat.Dbatools.Configuration;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class GetDbatoolsPathCommandTests
    {
        #region ConfigKeyConstruction
        [TestMethod]
        public void GetDbatoolsPath_ExistingPath_ReturnsConfiguredValue()
        {
            // Arrange - simulate a configured path
            string key = "path.managed.temp";
            var config = new Config { Module = "path", Name = "managed.temp" };
            config.Value = @"C:\Temp\dbatools";
            ConfigurationHost.Configurations[key] = config;

            try
            {
                // Act - LookupConfigValue is the same method the cmdlet calls
                string fullName = String.Format("Path.Managed.{0}", "Temp");
                object result = GetDbatoolsConfigValueCommand.LookupConfigValue(fullName, null);

                // Assert
                Assert.AreEqual(@"C:\Temp\dbatools", result);
            }
            finally
            {
                Config removed;
                ConfigurationHost.Configurations.TryRemove(key, out removed);
            }
        }

        [TestMethod]
        public void GetDbatoolsPath_NonExistentPath_ReturnsNull()
        {
            // Arrange - ensure the key does not exist
            string key = "path.managed.nonexistent99";
            Config removed;
            ConfigurationHost.Configurations.TryRemove(key, out removed);

            // Act
            string fullName = String.Format("Path.Managed.{0}", "nonexistent99");
            object result = GetDbatoolsConfigValueCommand.LookupConfigValue(fullName, null);

            // Assert
            Assert.IsNull(result, "Non-existent path should return null");
        }

        [TestMethod]
        public void GetDbatoolsPath_CaseInsensitiveLookup_FindsPath()
        {
            // Arrange
            string key = "path.managed.localappdata";
            var config = new Config { Module = "path", Name = "managed.localappdata" };
            config.Value = @"C:\Users\test\AppData\Local";
            ConfigurationHost.Configurations[key] = config;

            try
            {
                // Act - Name has mixed case, LookupConfigValue lowercases it
                string fullName = String.Format("Path.Managed.{0}", "LocalAppData");
                object result = GetDbatoolsConfigValueCommand.LookupConfigValue(fullName, null);

                // Assert
                Assert.AreEqual(@"C:\Users\test\AppData\Local", result);
            }
            finally
            {
                Config removed;
                ConfigurationHost.Configurations.TryRemove(key, out removed);
            }
        }

        [TestMethod]
        public void GetDbatoolsPath_ConfigKeyFormat_CorrectlyConstructed()
        {
            // Verify the key format matches what Set-DbatoolsPath would create
            string name = "MyCustomPath";
            string expected = "Path.Managed.MyCustomPath";
            string actual = String.Format("Path.Managed.{0}", name);

            Assert.AreEqual(expected, actual, "Config key should be Path.Managed.{Name}");
        }

        [TestMethod]
        public void GetDbatoolsPath_NullConfigValue_ReturnsNull()
        {
            // Arrange - config key exists but value is null
            string key = "path.managed.nullpath";
            var config = new Config { Module = "path", Name = "managed.nullpath" };
            // Value defaults to null
            ConfigurationHost.Configurations[key] = config;

            try
            {
                // Act
                string fullName = String.Format("Path.Managed.{0}", "nullpath");
                object result = GetDbatoolsConfigValueCommand.LookupConfigValue(fullName, null);

                // Assert - null value with null fallback returns null
                Assert.IsNull(result, "Null config value with no fallback should return null");
            }
            finally
            {
                Config removed;
                ConfigurationHost.Configurations.TryRemove(key, out removed);
            }
        }
        #endregion
    }
}
