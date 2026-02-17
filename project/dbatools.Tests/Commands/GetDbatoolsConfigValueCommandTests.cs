using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;
using Dataplat.Dbatools.Configuration;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class GetDbatoolsConfigValueCommandTests
    {
        #region ConvertSwitchSafetyValue
        [TestMethod]
        public void ConvertSwitchSafetyValue_Mandatory_ReturnsTrue()
        {
            // Act
            bool? result = GetDbatoolsConfigValueCommand.ConvertSwitchSafetyValue("Mandatory");

            // Assert
            Assert.IsTrue(result.HasValue, "Should return a value for Mandatory");
            Assert.IsTrue(result.Value, "Mandatory should convert to true");
        }

        [TestMethod]
        public void ConvertSwitchSafetyValue_Optional_ReturnsFalse()
        {
            // Act
            bool? result = GetDbatoolsConfigValueCommand.ConvertSwitchSafetyValue("Optional");

            // Assert
            Assert.IsTrue(result.HasValue, "Should return a value for Optional");
            Assert.IsFalse(result.Value, "Optional should convert to false");
        }

        [TestMethod]
        public void ConvertSwitchSafetyValue_RegularString_ReturnsNull()
        {
            // Act
            bool? result = GetDbatoolsConfigValueCommand.ConvertSwitchSafetyValue("SomeValue");

            // Assert
            Assert.IsFalse(result.HasValue, "Regular strings should return null");
        }

        [TestMethod]
        public void ConvertSwitchSafetyValue_EmptyString_ReturnsNull()
        {
            // Act
            bool? result = GetDbatoolsConfigValueCommand.ConvertSwitchSafetyValue("");

            // Assert
            Assert.IsFalse(result.HasValue, "Empty string should return null");
        }

        [TestMethod]
        public void ConvertSwitchSafetyValue_CaseSensitive_MandatoryLowercase_ReturnsNull()
        {
            // Act
            bool? result = GetDbatoolsConfigValueCommand.ConvertSwitchSafetyValue("mandatory");

            // Assert
            Assert.IsFalse(result.HasValue, "Lowercase 'mandatory' should not match (case-sensitive)");
        }

        [TestMethod]
        public void ConvertSwitchSafetyValue_CaseSensitive_OptionalLowercase_ReturnsNull()
        {
            // Act
            bool? result = GetDbatoolsConfigValueCommand.ConvertSwitchSafetyValue("optional");

            // Assert
            Assert.IsFalse(result.HasValue, "Lowercase 'optional' should not match (case-sensitive)");
        }
        #endregion

        #region LookupConfigValue
        [TestMethod]
        public void LookupConfigValue_KeyExists_ReturnsValue()
        {
            // Arrange
            string key = "test.lookupexists";
            var config = new Config { Module = "test", Name = "lookupexists" };
            config.Value = 42;
            ConfigurationHost.Configurations[key] = config;

            try
            {
                // Act
                object result = GetDbatoolsConfigValueCommand.LookupConfigValue(key, null);

                // Assert
                Assert.AreEqual(42, result);
            }
            finally
            {
                Config removed;
                ConfigurationHost.Configurations.TryRemove(key, out removed);
            }
        }

        [TestMethod]
        public void LookupConfigValue_KeyNotFound_ReturnsFallback()
        {
            // Arrange
            string key = "test.nonexistentkey12345";

            // Act
            object result = GetDbatoolsConfigValueCommand.LookupConfigValue(key, "default-value");

            // Assert
            Assert.AreEqual("default-value", result);
        }

        [TestMethod]
        public void LookupConfigValue_KeyNotFound_NoFallback_ReturnsNull()
        {
            // Arrange
            string key = "test.nonexistentkey67890";

            // Act
            object result = GetDbatoolsConfigValueCommand.LookupConfigValue(key, null);

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void LookupConfigValue_ValueIsMandatory_ReturnsTrue()
        {
            // Arrange
            string key = "test.mandatoryval";
            var config = new Config { Module = "test", Name = "mandatoryval" };
            config.Value = "Mandatory";
            ConfigurationHost.Configurations[key] = config;

            try
            {
                // Act
                object result = GetDbatoolsConfigValueCommand.LookupConfigValue(key, null);

                // Assert
                Assert.IsInstanceOfType(result, typeof(bool));
                Assert.IsTrue((bool)result, "Mandatory should be converted to true");
            }
            finally
            {
                Config removed;
                ConfigurationHost.Configurations.TryRemove(key, out removed);
            }
        }

        [TestMethod]
        public void LookupConfigValue_ValueIsOptional_ReturnsFalse()
        {
            // Arrange
            string key = "test.optionalval";
            var config = new Config { Module = "test", Name = "optionalval" };
            config.Value = "Optional";
            ConfigurationHost.Configurations[key] = config;

            try
            {
                // Act
                object result = GetDbatoolsConfigValueCommand.LookupConfigValue(key, null);

                // Assert
                Assert.IsInstanceOfType(result, typeof(bool));
                Assert.IsFalse((bool)result, "Optional should be converted to false");
            }
            finally
            {
                Config removed;
                ConfigurationHost.Configurations.TryRemove(key, out removed);
            }
        }

        [TestMethod]
        public void LookupConfigValue_CaseInsensitiveLookup_FindsKey()
        {
            // Arrange - key stored with mixed case, looked up with different case
            string storedKey = "Test.CaseCheck";
            var config = new Config { Module = "Test", Name = "CaseCheck" };
            config.Value = "found-it";
            ConfigurationHost.Configurations[storedKey] = config;

            try
            {
                // Act - lookup uses ToLowerInvariant internally
                object result = GetDbatoolsConfigValueCommand.LookupConfigValue("TEST.CASECHECK", null);

                // Assert
                Assert.AreEqual("found-it", result);
            }
            finally
            {
                Config removed;
                ConfigurationHost.Configurations.TryRemove(storedKey, out removed);
            }
        }

        [TestMethod]
        public void LookupConfigValue_NullValue_ReturnsFallback()
        {
            // Arrange - config exists but value is null
            string key = "test.nullval";
            var config = new Config { Module = "test", Name = "nullval" };
            // Value defaults to null (no assignment)
            ConfigurationHost.Configurations[key] = config;

            try
            {
                // Act
                object result = GetDbatoolsConfigValueCommand.LookupConfigValue(key, "fallback-used");

                // Assert
                Assert.AreEqual("fallback-used", result);
            }
            finally
            {
                Config removed;
                ConfigurationHost.Configurations.TryRemove(key, out removed);
            }
        }

        [TestMethod]
        public void LookupConfigValue_RegularStringValue_ReturnsUnchanged()
        {
            // Arrange
            string key = "test.regularstring";
            var config = new Config { Module = "test", Name = "regularstring" };
            config.Value = "just-a-string";
            ConfigurationHost.Configurations[key] = config;

            try
            {
                // Act
                object result = GetDbatoolsConfigValueCommand.LookupConfigValue(key, null);

                // Assert
                Assert.AreEqual("just-a-string", result);
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
