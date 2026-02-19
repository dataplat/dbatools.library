using System;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class GetDbaAvailableCollationCommandTests
    {
        #region GetIntProperty
        [TestMethod]
        public void GetIntProperty_ReturnsIntValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("CodePage", 1252));

            int result = GetDbaAvailableCollationCommand.GetIntProperty(obj, "CodePage");

            Assert.AreEqual(1252, result);
        }

        [TestMethod]
        public void GetIntProperty_ParsesStringValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("CodePage", "1252"));

            int result = GetDbaAvailableCollationCommand.GetIntProperty(obj, "CodePage");

            Assert.AreEqual(1252, result);
        }

        [TestMethod]
        public void GetIntProperty_NullObjectReturnsZero()
        {
            int result = GetDbaAvailableCollationCommand.GetIntProperty(null, "CodePage");

            Assert.AreEqual(0, result);
        }

        [TestMethod]
        public void GetIntProperty_MissingPropertyReturnsZero()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "test"));

            int result = GetDbaAvailableCollationCommand.GetIntProperty(obj, "CodePage");

            Assert.AreEqual(0, result);
        }

        [TestMethod]
        public void GetIntProperty_NullValueReturnsZero()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("CodePage", null));

            int result = GetDbaAvailableCollationCommand.GetIntProperty(obj, "CodePage");

            Assert.AreEqual(0, result);
        }
        #endregion

        #region GetServerPropertySafe
        [TestMethod]
        public void GetServerPropertySafe_ReturnsStringProperty()
        {
            PSObject server = new PSObject();
            server.Properties.Add(new PSNoteProperty("ComputerName", "SERVER01"));

            string result = GetDbaAvailableCollationCommand.GetServerPropertySafe(server, "ComputerName");

            Assert.AreEqual("SERVER01", result);
        }

        [TestMethod]
        public void GetServerPropertySafe_NullObjectReturnsNull()
        {
            string result = GetDbaAvailableCollationCommand.GetServerPropertySafe(null, "ComputerName");

            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetServerPropertySafe_MissingPropertyReturnsNull()
        {
            PSObject server = new PSObject();
            server.Properties.Add(new PSNoteProperty("Name", "test"));

            string result = GetDbaAvailableCollationCommand.GetServerPropertySafe(server, "ComputerName");

            Assert.IsNull(result);
        }
        #endregion

        #region AddOrSetProperty
        [TestMethod]
        public void AddOrSetProperty_AddsNewProperty()
        {
            PSObject obj = new PSObject();

            GetDbaAvailableCollationCommand.AddOrSetProperty(obj, "ComputerName", "SERVER01");

            Assert.AreEqual("SERVER01", obj.Properties["ComputerName"].Value);
        }

        [TestMethod]
        public void AddOrSetProperty_UpdatesExistingProperty()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("ComputerName", "OLD"));

            GetDbaAvailableCollationCommand.AddOrSetProperty(obj, "ComputerName", "NEW");

            Assert.AreEqual("NEW", obj.Properties["ComputerName"].Value);
        }

        [TestMethod]
        public void AddOrSetProperty_NullObjectDoesNotThrow()
        {
            GetDbaAvailableCollationCommand.AddOrSetProperty(null, "Name", "value");
            // Should not throw
        }

        [TestMethod]
        public void AddOrSetProperty_NullValueIsStored()
        {
            PSObject obj = new PSObject();

            GetDbaAvailableCollationCommand.AddOrSetProperty(obj, "LocaleName", null);

            Assert.IsNull(obj.Properties["LocaleName"].Value);
        }
        #endregion

        #region SetDefaultDisplayPropertySet
        [TestMethod]
        public void SetDefaultDisplayPropertySet_AddsStandardMembers()
        {
            PSObject obj = new PSObject();
            string[] props = new string[] { "Name", "CodePage" };

            GetDbaAvailableCollationCommand.SetDefaultDisplayPropertySet(obj, props);

            PSMemberInfo member = obj.Members["PSStandardMembers"];
            Assert.IsNotNull(member);
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_NullObjectDoesNotThrow()
        {
            GetDbaAvailableCollationCommand.SetDefaultDisplayPropertySet(null, new string[] { "Name" });
            // Should not throw
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_NullPropertiesDoesNotThrow()
        {
            PSObject obj = new PSObject();
            GetDbaAvailableCollationCommand.SetDefaultDisplayPropertySet(obj, null);
            // Should not throw
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_OverwritesExisting()
        {
            PSObject obj = new PSObject();
            string[] props1 = new string[] { "Name" };
            string[] props2 = new string[] { "Name", "CodePage" };

            GetDbaAvailableCollationCommand.SetDefaultDisplayPropertySet(obj, props1);
            GetDbaAvailableCollationCommand.SetDefaultDisplayPropertySet(obj, props2);

            // Should not throw - second call overwrites first
            PSMemberInfo member = obj.Members["PSStandardMembers"];
            Assert.IsNotNull(member);
        }
        #endregion

        #region GetLocaleDescription
        [TestMethod]
        public void GetLocaleDescription_KnownLocaleReturnsDisplayName()
        {
            // Locale 1033 = English (United States)
            var cmd = new GetDbaAvailableCollationCommand();
            InitializeCaches(cmd);

            string result = cmd.GetLocaleDescription(1033);

            Assert.IsNotNull(result);
            Assert.IsTrue(result.Contains("English"), String.Format("Expected locale 1033 to contain 'English', got '{0}'", result));
        }

        [TestMethod]
        public void GetLocaleDescription_SpecialLocale66577_ReturnsJapaneseUnicode()
        {
            var cmd = new GetDbaAvailableCollationCommand();
            InitializeCaches(cmd);

            string result = cmd.GetLocaleDescription(66577);

            Assert.AreEqual("Japanese_Unicode", result);
        }

        [TestMethod]
        public void GetLocaleDescription_InvalidLocaleReturnsNull()
        {
            var cmd = new GetDbaAvailableCollationCommand();
            InitializeCaches(cmd);

            string result = cmd.GetLocaleDescription(999999);

            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetLocaleDescription_CachesResult()
        {
            var cmd = new GetDbaAvailableCollationCommand();
            InitializeCaches(cmd);

            string first = cmd.GetLocaleDescription(1033);
            string second = cmd.GetLocaleDescription(1033);

            Assert.AreEqual(first, second);
        }
        #endregion

        #region GetCodePageDescription
        [TestMethod]
        public void GetCodePageDescription_KnownCodePageReturnsName()
        {
            // Code page 65001 = UTF-8 (available on all .NET platforms)
            var cmd = new GetDbaAvailableCollationCommand();
            InitializeCaches(cmd);

            string result = cmd.GetCodePageDescription(65001);

            Assert.IsNotNull(result);
        }

        [TestMethod]
        public void GetCodePageDescription_InvalidCodePageReturnsNull()
        {
            var cmd = new GetDbaAvailableCollationCommand();
            InitializeCaches(cmd);

            string result = cmd.GetCodePageDescription(999999);

            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetCodePageDescription_CachesResult()
        {
            var cmd = new GetDbaAvailableCollationCommand();
            InitializeCaches(cmd);

            string first = cmd.GetCodePageDescription(65001);
            string second = cmd.GetCodePageDescription(65001);

            Assert.AreEqual(first, second);
        }
        #endregion

        #region Helpers
        /// <summary>
        /// Initializes the private cache fields using reflection since
        /// BeginProcessing requires a PowerShell runspace.
        /// </summary>
        private static void InitializeCaches(GetDbaAvailableCollationCommand cmd)
        {
            var localeCacheField = typeof(GetDbaAvailableCollationCommand)
                .GetField("_localeCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var codePageCacheField = typeof(GetDbaAvailableCollationCommand)
                .GetField("_codePageCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            var localeCache = new System.Collections.Generic.Dictionary<int, string>();
            localeCache[66577] = "Japanese_Unicode";
            localeCacheField.SetValue(cmd, localeCache);
            codePageCacheField.SetValue(cmd, new System.Collections.Generic.Dictionary<int, string>());
        }
        #endregion
    }
}
