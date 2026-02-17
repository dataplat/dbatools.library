using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;
using Dataplat.Dbatools.Configuration;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class SetDbatoolsPathCommandTests
    {
        #region BuildConfigKey
        [TestMethod]
        public void BuildConfigKey_SimpleName_ReturnsPrefixedKey()
        {
            // Arrange
            string name = "temp";

            // Act
            string result = SetDbatoolsPathCommand.BuildConfigKey(name);

            // Assert
            Assert.AreEqual("Path.Managed.temp", result);
        }

        [TestMethod]
        public void BuildConfigKey_MixedCaseName_PreservesCase()
        {
            // Arrange
            string name = "LocalAppData";

            // Act
            string result = SetDbatoolsPathCommand.BuildConfigKey(name);

            // Assert
            Assert.AreEqual("Path.Managed.LocalAppData", result);
        }

        [TestMethod]
        public void BuildConfigKey_NameWithDots_ReturnsValidKey()
        {
            // Arrange
            string name = "custom.path";

            // Act
            string result = SetDbatoolsPathCommand.BuildConfigKey(name);

            // Assert
            Assert.AreEqual("Path.Managed.custom.path", result);
        }

        [TestMethod]
        public void BuildConfigKey_EmptyName_ReturnsPrefix()
        {
            // Arrange
            string name = "";

            // Act
            string result = SetDbatoolsPathCommand.BuildConfigKey(name);

            // Assert
            Assert.AreEqual("Path.Managed.", result);
        }
        #endregion

        #region DefaultScope
        [TestMethod]
        public void DefaultScope_IsUserDefault()
        {
            // Arrange & Act
            var cmd = new SetDbatoolsPathCommand();

            // Assert
            Assert.AreEqual(ConfigScope.UserDefault, cmd.Scope);
        }
        #endregion

        #region ConfigKeyRoundTrip
        [TestMethod]
        public void BuildConfigKey_MatchesGetDbatoolsPathLookup()
        {
            // Verify that the key Set-DbatoolsPath builds is the same key
            // that Get-DbatoolsPath uses to retrieve the value.
            // Get-DbatoolsPath uses String.Format("Path.Managed.{0}", Name)
            // and then LookupConfigValue lowercases the key.
            string name = "BackupFolder";
            string setKey = SetDbatoolsPathCommand.BuildConfigKey(name);
            string getKey = String.Format("Path.Managed.{0}", name);

            Assert.AreEqual(setKey, getKey, "Set and Get must use the same config key format");
        }

        [TestMethod]
        public void BuildConfigKey_UncPath_HandlesSpecialChars()
        {
            // The Name parameter is the alias, not the path itself.
            // But verify special characters in name don't break the key.
            string name = "network_share_2";
            string result = SetDbatoolsPathCommand.BuildConfigKey(name);

            Assert.AreEqual("Path.Managed.network_share_2", result);
        }
        #endregion
    }
}
