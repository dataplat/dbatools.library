using System;
using System.Collections;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class ImportDbatoolsConfigCommandTests
    {
        #region IsExcluded
        [TestMethod]
        public void IsExcluded_NoFilters_ReturnsFalse()
        {
            var cmd = CreateCommand(null, null);
            Assert.IsFalse(cmd.IsExcluded("sql.connection.timeout"));
        }

        [TestMethod]
        public void IsExcluded_MatchingWildcard_ReturnsTrue()
        {
            var cmd = CreateCommand(null, new string[] { "sql.connection.*" });
            Assert.IsTrue(cmd.IsExcluded("sql.connection.timeout"));
        }

        [TestMethod]
        public void IsExcluded_NoMatch_ReturnsFalse()
        {
            var cmd = CreateCommand(null, new string[] { "logging.*" });
            Assert.IsFalse(cmd.IsExcluded("sql.connection.timeout"));
        }

        [TestMethod]
        public void IsExcluded_CaseInsensitive_ReturnsTrue()
        {
            var cmd = CreateCommand(null, new string[] { "SQL.CONNECTION.*" });
            Assert.IsTrue(cmd.IsExcluded("sql.connection.timeout"));
        }

        [TestMethod]
        public void IsExcluded_EmptyArray_ReturnsFalse()
        {
            var cmd = CreateCommand(null, new string[0]);
            Assert.IsFalse(cmd.IsExcluded("sql.connection.timeout"));
        }
        #endregion

        #region IsIncluded
        [TestMethod]
        public void IsIncluded_NoFilters_ReturnsTrue()
        {
            var cmd = CreateCommand(null, null);
            Assert.IsTrue(cmd.IsIncluded("sql.connection.timeout"));
        }

        [TestMethod]
        public void IsIncluded_MatchingWildcard_ReturnsTrue()
        {
            var cmd = CreateCommand(new string[] { "sql.*" }, null);
            Assert.IsTrue(cmd.IsIncluded("sql.connection.timeout"));
        }

        [TestMethod]
        public void IsIncluded_NoMatch_ReturnsFalse()
        {
            var cmd = CreateCommand(new string[] { "logging.*" }, null);
            Assert.IsFalse(cmd.IsIncluded("sql.connection.timeout"));
        }

        [TestMethod]
        public void IsIncluded_CaseInsensitive_ReturnsTrue()
        {
            var cmd = CreateCommand(new string[] { "SQL.CONNECTION.*" }, null);
            Assert.IsTrue(cmd.IsIncluded("sql.connection.timeout"));
        }

        [TestMethod]
        public void IsIncluded_EmptyArray_ReturnsTrue()
        {
            var cmd = CreateCommand(new string[0], null);
            Assert.IsTrue(cmd.IsIncluded("any.config.item"));
        }

        [TestMethod]
        public void IsIncluded_MultiplePatterns_MatchesAny()
        {
            var cmd = CreateCommand(new string[] { "logging.*", "sql.connection.*" }, null);
            Assert.IsTrue(cmd.IsIncluded("sql.connection.timeout"));
            Assert.IsTrue(cmd.IsIncluded("logging.maxerrors"));
            Assert.IsFalse(cmd.IsIncluded("other.setting"));
        }
        #endregion

        #region CreatePeekObject
        [TestMethod]
        public void CreatePeekObject_AllProperties_SetsCorrectly()
        {
            Hashtable element = new Hashtable();
            element["FullName"] = "sql.connection.timeout";
            element["Value"] = 30;
            element["Type"] = 3;
            element["KeepPersisted"] = false;
            element["Enforced"] = false;
            element["Policy"] = false;

            PSObject result = ImportDbatoolsConfigCommand.CreatePeekObject(element);

            Assert.AreEqual("sql.connection.timeout", result.Properties["FullName"].Value);
            Assert.AreEqual(30, result.Properties["Value"].Value);
            Assert.AreEqual(3, result.Properties["Type"].Value);
            Assert.AreEqual(false, result.Properties["KeepPersisted"].Value);
            Assert.AreEqual(false, result.Properties["Enforced"].Value);
            Assert.AreEqual(false, result.Properties["Policy"].Value);
        }

        [TestMethod]
        public void CreatePeekObject_MissingType_SetsNull()
        {
            Hashtable element = new Hashtable();
            element["FullName"] = "test.config";
            element["Value"] = "hello";
            element["KeepPersisted"] = false;

            PSObject result = ImportDbatoolsConfigCommand.CreatePeekObject(element);

            Assert.AreEqual("test.config", result.Properties["FullName"].Value);
            Assert.AreEqual("hello", result.Properties["Value"].Value);
            Assert.IsNull(result.Properties["Type"].Value);
            Assert.AreEqual(false, result.Properties["KeepPersisted"].Value);
        }

        [TestMethod]
        public void CreatePeekObject_KeepPersistedTrue_SetsTrue()
        {
            Hashtable element = new Hashtable();
            element["FullName"] = "test.persisted";
            element["Value"] = "some xml";
            element["Type"] = 12;
            element["KeepPersisted"] = true;

            PSObject result = ImportDbatoolsConfigCommand.CreatePeekObject(element);

            Assert.AreEqual(true, result.Properties["KeepPersisted"].Value);
        }

        [TestMethod]
        public void CreatePeekObject_EmptyHashtable_HandlesGracefully()
        {
            Hashtable element = new Hashtable();

            PSObject result = ImportDbatoolsConfigCommand.CreatePeekObject(element);

            Assert.IsNull(result.Properties["FullName"].Value);
            Assert.IsNull(result.Properties["Value"].Value);
            Assert.IsNull(result.Properties["Type"].Value);
            Assert.AreEqual(false, result.Properties["KeepPersisted"].Value);
            Assert.AreEqual(false, result.Properties["Enforced"].Value);
            Assert.AreEqual(false, result.Properties["Policy"].Value);
        }

        [TestMethod]
        public void CreatePeekObject_EnforcedAndPolicy_SetsCorrectly()
        {
            Hashtable element = new Hashtable();
            element["FullName"] = "enforced.setting";
            element["Value"] = "locked";
            element["KeepPersisted"] = true;
            element["Enforced"] = true;
            element["Policy"] = true;

            PSObject result = ImportDbatoolsConfigCommand.CreatePeekObject(element);

            Assert.AreEqual(true, result.Properties["Enforced"].Value);
            Assert.AreEqual(true, result.Properties["Policy"].Value);
        }
        #endregion

        #region GetKeepPersisted
        [TestMethod]
        public void GetKeepPersisted_NullHashtable_ReturnsFalse()
        {
            Assert.IsFalse(ImportDbatoolsConfigCommand.GetKeepPersisted(null));
        }

        [TestMethod]
        public void GetKeepPersisted_MissingKey_ReturnsFalse()
        {
            Hashtable ht = new Hashtable();
            ht["FullName"] = "test";
            Assert.IsFalse(ImportDbatoolsConfigCommand.GetKeepPersisted(ht));
        }

        [TestMethod]
        public void GetKeepPersisted_BoolTrue_ReturnsTrue()
        {
            Hashtable ht = new Hashtable();
            ht["KeepPersisted"] = true;
            Assert.IsTrue(ImportDbatoolsConfigCommand.GetKeepPersisted(ht));
        }

        [TestMethod]
        public void GetKeepPersisted_BoolFalse_ReturnsFalse()
        {
            Hashtable ht = new Hashtable();
            ht["KeepPersisted"] = false;
            Assert.IsFalse(ImportDbatoolsConfigCommand.GetKeepPersisted(ht));
        }

        [TestMethod]
        public void GetKeepPersisted_StringTrue_ReturnsTrue()
        {
            Hashtable ht = new Hashtable();
            ht["KeepPersisted"] = "True";
            Assert.IsTrue(ImportDbatoolsConfigCommand.GetKeepPersisted(ht));
        }
        #endregion

        #region GetBoolProperty
        [TestMethod]
        public void GetBoolProperty_NullHashtable_ReturnsFalse()
        {
            Assert.IsFalse(ImportDbatoolsConfigCommand.GetBoolProperty(null, "Enforced"));
        }

        [TestMethod]
        public void GetBoolProperty_MissingKey_ReturnsFalse()
        {
            Hashtable ht = new Hashtable();
            Assert.IsFalse(ImportDbatoolsConfigCommand.GetBoolProperty(ht, "Enforced"));
        }

        [TestMethod]
        public void GetBoolProperty_BoolTrue_ReturnsTrue()
        {
            Hashtable ht = new Hashtable();
            ht["Policy"] = true;
            Assert.IsTrue(ImportDbatoolsConfigCommand.GetBoolProperty(ht, "Policy"));
        }

        [TestMethod]
        public void GetBoolProperty_InvalidValue_ReturnsFalse()
        {
            Hashtable ht = new Hashtable();
            ht["Enforced"] = "not_a_bool";
            Assert.IsFalse(ImportDbatoolsConfigCommand.GetBoolProperty(ht, "Enforced"));
        }
        #endregion

        #region Filter Interaction
        [TestMethod]
        public void ExcludeAndInclude_FiltersAreIndependent()
        {
            // Both filters are tested independently in ProcessRecord
            // Exclude runs before Include - this tests they produce correct individual results
            var cmd = CreateCommand(new string[] { "sql.*" }, new string[] { "sql.connection.*" });

            string item1 = "sql.connection.timeout";
            string item2 = "sql.maxdop";

            Assert.IsTrue(cmd.IsExcluded(item1), "sql.connection.timeout should be excluded");
            Assert.IsTrue(cmd.IsIncluded(item1), "sql.connection.timeout matches include pattern");
            Assert.IsFalse(cmd.IsExcluded(item2), "sql.maxdop should not be excluded");
            Assert.IsTrue(cmd.IsIncluded(item2), "sql.maxdop should be included");
        }

        [TestMethod]
        public void ExcludeFilter_MultiplePatterns_ExcludesAll()
        {
            var cmd = CreateCommand(null, new string[] { "secret.*", "credential.*" });

            Assert.IsTrue(cmd.IsExcluded("secret.apikey"));
            Assert.IsTrue(cmd.IsExcluded("credential.password"));
            Assert.IsFalse(cmd.IsExcluded("sql.connection"));
        }
        #endregion

        #region Test Helper
        /// <summary>
        /// Creates an ImportDbatoolsConfigCommand with pre-compiled filter patterns
        /// for unit testing without requiring a PowerShell runspace.
        /// </summary>
        private static ImportDbatoolsConfigCommand CreateCommand(string[] includeFilter, string[] excludeFilter)
        {
            var cmd = new ImportDbatoolsConfigCommand();
            cmd.IncludeFilter = includeFilter;
            cmd.ExcludeFilter = excludeFilter;

            // Set pre-compiled patterns via reflection since BeginProcessing
            // requires a PowerShell runspace context
            var excludeField = typeof(ImportDbatoolsConfigCommand).GetField("_excludePatterns",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var includeField = typeof(ImportDbatoolsConfigCommand).GetField("_includePatterns",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            excludeField.SetValue(cmd, BuildPatterns(excludeFilter));
            includeField.SetValue(cmd, BuildPatterns(includeFilter));

            return cmd;
        }

        private static WildcardPattern[] BuildPatterns(string[] filters)
        {
            if (filters == null || filters.Length == 0)
                return new WildcardPattern[0];

            WildcardPattern[] patterns = new WildcardPattern[filters.Length];
            for (int i = 0; i < filters.Length; i++)
            {
                patterns[i] = new WildcardPattern(filters[i], WildcardOptions.IgnoreCase);
            }
            return patterns;
        }
        #endregion
    }
}
