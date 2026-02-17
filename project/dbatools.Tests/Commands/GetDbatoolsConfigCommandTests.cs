using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Configuration;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class GetDbatoolsConfigCommandTests
    {
        #region CompareConfigByModuleThenName
        [TestMethod]
        public void CompareConfigByModuleThenName_DifferentModules_SortsByModule()
        {
            // Arrange
            var configA = new Config { Module = "alpha", Name = "setting1" };
            var configB = new Config { Module = "beta", Name = "setting1" };

            // Act
            int result = GetDbatoolsConfigCommand.CompareConfigByModuleThenName(configA, configB);

            // Assert
            Assert.IsTrue(result < 0, "alpha should sort before beta");
        }

        [TestMethod]
        public void CompareConfigByModuleThenName_SameModule_SortsByName()
        {
            // Arrange
            var configA = new Config { Module = "sql", Name = "alpha" };
            var configB = new Config { Module = "sql", Name = "beta" };

            // Act
            int result = GetDbatoolsConfigCommand.CompareConfigByModuleThenName(configA, configB);

            // Assert
            Assert.IsTrue(result < 0, "alpha should sort before beta within same module");
        }

        [TestMethod]
        public void CompareConfigByModuleThenName_IdenticalEntries_ReturnsZero()
        {
            // Arrange
            var configA = new Config { Module = "sql", Name = "timeout" };
            var configB = new Config { Module = "sql", Name = "timeout" };

            // Act
            int result = GetDbatoolsConfigCommand.CompareConfigByModuleThenName(configA, configB);

            // Assert
            Assert.AreEqual(0, result);
        }

        [TestMethod]
        public void CompareConfigByModuleThenName_NullFirst_ReturnsNegative()
        {
            // Arrange
            var config = new Config { Module = "sql", Name = "timeout" };

            // Act
            int result = GetDbatoolsConfigCommand.CompareConfigByModuleThenName(null, config);

            // Assert
            Assert.IsTrue(result < 0, "null should sort before non-null");
        }

        [TestMethod]
        public void CompareConfigByModuleThenName_NullSecond_ReturnsPositive()
        {
            // Arrange
            var config = new Config { Module = "sql", Name = "timeout" };

            // Act
            int result = GetDbatoolsConfigCommand.CompareConfigByModuleThenName(config, null);

            // Assert
            Assert.IsTrue(result > 0, "non-null should sort after null");
        }

        [TestMethod]
        public void CompareConfigByModuleThenName_BothNull_ReturnsZero()
        {
            // Act
            int result = GetDbatoolsConfigCommand.CompareConfigByModuleThenName(null, null);

            // Assert
            Assert.AreEqual(0, result);
        }

        [TestMethod]
        public void CompareConfigByModuleThenName_CaseInsensitive_ReturnsZero()
        {
            // Arrange
            var configA = new Config { Module = "SQL", Name = "Timeout" };
            var configB = new Config { Module = "sql", Name = "timeout" };

            // Act
            int result = GetDbatoolsConfigCommand.CompareConfigByModuleThenName(configA, configB);

            // Assert
            Assert.AreEqual(0, result, "Comparison should be case-insensitive");
        }
        #endregion

        #region ConfigHiddenProperty
        [TestMethod]
        public void Config_HiddenDefault_IsFalse()
        {
            // Arrange & Act
            var config = new Config();

            // Assert
            Assert.IsFalse(config.Hidden, "Hidden should default to false");
        }

        [TestMethod]
        public void Config_HiddenSetTrue_IsTrue()
        {
            // Arrange
            var config = new Config { Hidden = true };

            // Assert
            Assert.IsTrue(config.Hidden);
        }
        #endregion

        #region ConfigFullName
        [TestMethod]
        public void Config_FullName_CombinesModuleAndName()
        {
            // Arrange
            var config = new Config { Module = "sql", Name = "timeout" };

            // Act
            string fullName = config.FullName;

            // Assert
            Assert.AreEqual("sql.timeout", fullName);
        }

        [TestMethod]
        public void Config_FullName_HandlesCompoundNames()
        {
            // Arrange
            var config = new Config { Module = "sql.connection", Name = "timeout" };

            // Act
            string fullName = config.FullName;

            // Assert
            Assert.AreEqual("sql.connection.timeout", fullName);
        }
        #endregion
    }
}
