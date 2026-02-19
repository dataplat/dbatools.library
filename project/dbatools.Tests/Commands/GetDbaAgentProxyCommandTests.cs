using System;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class GetDbaAgentProxyCommandTests
    {
        #region FilterIncludeWildcard

        [TestMethod]
        public void FilterIncludeWildcard_ExactMatch_ReturnsMatchingItem()
        {
            var items = CreateProxyCollection("ProxyA", "ProxyB", "ProxyC");
            var result = Dbatools.Commands.GetDbaAgentProxyCommand.FilterIncludeWildcard(items, new string[] { "ProxyB" });
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("ProxyB", GetName(result[0]));
        }

        [TestMethod]
        public void FilterIncludeWildcard_WildcardPattern_ReturnsMatchingItems()
        {
            var items = CreateProxyCollection("TestProxy1", "TestProxy2", "OtherProxy");
            var result = Dbatools.Commands.GetDbaAgentProxyCommand.FilterIncludeWildcard(items, new string[] { "Test*" });
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("TestProxy1", GetName(result[0]));
            Assert.AreEqual("TestProxy2", GetName(result[1]));
        }

        [TestMethod]
        public void FilterIncludeWildcard_MultiplePatterns_ReturnsAllMatches()
        {
            var items = CreateProxyCollection("Alpha", "Beta", "Gamma");
            var result = Dbatools.Commands.GetDbaAgentProxyCommand.FilterIncludeWildcard(items, new string[] { "Alpha", "Gamma" });
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("Alpha", GetName(result[0]));
            Assert.AreEqual("Gamma", GetName(result[1]));
        }

        [TestMethod]
        public void FilterIncludeWildcard_CaseInsensitive_MatchesRegardlessOfCase()
        {
            var items = CreateProxyCollection("MyProxy");
            var result = Dbatools.Commands.GetDbaAgentProxyCommand.FilterIncludeWildcard(items, new string[] { "myproxy" });
            Assert.AreEqual(1, result.Count);
        }

        [TestMethod]
        public void FilterIncludeWildcard_NoMatch_ReturnsEmpty()
        {
            var items = CreateProxyCollection("ProxyA", "ProxyB");
            var result = Dbatools.Commands.GetDbaAgentProxyCommand.FilterIncludeWildcard(items, new string[] { "NonExistent" });
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void FilterIncludeWildcard_NullPatterns_ReturnsAll()
        {
            var items = CreateProxyCollection("ProxyA", "ProxyB");
            var result = Dbatools.Commands.GetDbaAgentProxyCommand.FilterIncludeWildcard(items, null);
            Assert.AreEqual(2, result.Count);
        }

        [TestMethod]
        public void FilterIncludeWildcard_EmptyPatterns_ReturnsAll()
        {
            var items = CreateProxyCollection("ProxyA", "ProxyB");
            var result = Dbatools.Commands.GetDbaAgentProxyCommand.FilterIncludeWildcard(items, new string[0]);
            Assert.AreEqual(2, result.Count);
        }

        #endregion FilterIncludeWildcard

        #region FilterExcludeWildcard

        [TestMethod]
        public void FilterExcludeWildcard_ExactMatch_ExcludesMatchingItem()
        {
            var items = CreateProxyCollection("ProxyA", "ProxyB", "ProxyC");
            var result = Dbatools.Commands.GetDbaAgentProxyCommand.FilterExcludeWildcard(items, new string[] { "ProxyB" });
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("ProxyA", GetName(result[0]));
            Assert.AreEqual("ProxyC", GetName(result[1]));
        }

        [TestMethod]
        public void FilterExcludeWildcard_WildcardPattern_ExcludesMatchingItems()
        {
            var items = CreateProxyCollection("TestProxy1", "TestProxy2", "OtherProxy");
            var result = Dbatools.Commands.GetDbaAgentProxyCommand.FilterExcludeWildcard(items, new string[] { "Test*" });
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("OtherProxy", GetName(result[0]));
        }

        [TestMethod]
        public void FilterExcludeWildcard_CaseInsensitive_ExcludesRegardlessOfCase()
        {
            var items = CreateProxyCollection("MyProxy", "OtherProxy");
            var result = Dbatools.Commands.GetDbaAgentProxyCommand.FilterExcludeWildcard(items, new string[] { "MYPROXY" });
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("OtherProxy", GetName(result[0]));
        }

        [TestMethod]
        public void FilterExcludeWildcard_NoMatch_ReturnsAll()
        {
            var items = CreateProxyCollection("ProxyA", "ProxyB");
            var result = Dbatools.Commands.GetDbaAgentProxyCommand.FilterExcludeWildcard(items, new string[] { "NonExistent" });
            Assert.AreEqual(2, result.Count);
        }

        [TestMethod]
        public void FilterExcludeWildcard_NullPatterns_ReturnsAll()
        {
            var items = CreateProxyCollection("ProxyA", "ProxyB");
            var result = Dbatools.Commands.GetDbaAgentProxyCommand.FilterExcludeWildcard(items, null);
            Assert.AreEqual(2, result.Count);
        }

        #endregion FilterExcludeWildcard

        #region GetPropertyString

        [TestMethod]
        public void GetPropertyString_ExistingProperty_ReturnsValue()
        {
            var obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "TestProxy"));
            var result = Dbatools.Commands.DbaBaseCmdlet.GetPropertyString(obj, "Name");
            Assert.AreEqual("TestProxy", result);
        }

        [TestMethod]
        public void GetPropertyString_MissingProperty_ReturnsNull()
        {
            var obj = new PSObject();
            var result = Dbatools.Commands.DbaBaseCmdlet.GetPropertyString(obj, "NonExistent");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyString_NullObject_ReturnsNull()
        {
            var result = Dbatools.Commands.DbaBaseCmdlet.GetPropertyString(null, "Name");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyString_NullValue_ReturnsNull()
        {
            var obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", null));
            var result = Dbatools.Commands.DbaBaseCmdlet.GetPropertyString(obj, "Name");
            Assert.IsNull(result);
        }

        #endregion GetPropertyString

        #region AddOrSetProperty

        [TestMethod]
        public void AddOrSetProperty_NewProperty_AddsSuccessfully()
        {
            var obj = new PSObject();
            Dbatools.Commands.GetDbaAgentProxyCommand.AddOrSetProperty(obj, "TestProp", "TestValue");
            Assert.AreEqual("TestValue", obj.Properties["TestProp"].Value);
        }

        [TestMethod]
        public void AddOrSetProperty_ExistingProperty_UpdatesValue()
        {
            var obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("TestProp", "OldValue"));
            Dbatools.Commands.GetDbaAgentProxyCommand.AddOrSetProperty(obj, "TestProp", "NewValue");
            Assert.AreEqual("NewValue", obj.Properties["TestProp"].Value);
        }

        [TestMethod]
        public void AddOrSetProperty_NullObject_DoesNotThrow()
        {
            Dbatools.Commands.GetDbaAgentProxyCommand.AddOrSetProperty(null, "Name", "Value");
            // Should not throw
        }

        #endregion AddOrSetProperty

        #region SetDefaultDisplayPropertySet

        [TestMethod]
        public void SetDefaultDisplayPropertySet_SetsProperties()
        {
            var obj = new PSObject();
            string[] props = new string[] { "Name", "ID", "IsEnabled" };
            Dbatools.Commands.GetDbaAgentProxyCommand.SetDefaultDisplayPropertySet(obj, props);

            var memberSet = obj.Members["PSStandardMembers"] as PSMemberSet;
            Assert.IsNotNull(memberSet);
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_NullObject_DoesNotThrow()
        {
            Dbatools.Commands.GetDbaAgentProxyCommand.SetDefaultDisplayPropertySet(null, new string[] { "Name" });
            // Should not throw
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_NullProperties_DoesNotThrow()
        {
            var obj = new PSObject();
            Dbatools.Commands.GetDbaAgentProxyCommand.SetDefaultDisplayPropertySet(obj, null);
            // Should not throw
        }

        #endregion SetDefaultDisplayPropertySet

        #region GetPropertyString_ServerEdition

        [TestMethod]
        public void GetPropertyString_ServerEdition_ReturnsValue()
        {
            var obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Edition", "Enterprise"));
            var result = Dbatools.Commands.DbaBaseCmdlet.GetPropertyString(obj, "Edition");
            Assert.AreEqual("Enterprise", result);
        }

        [TestMethod]
        public void GetPropertyString_NullServer_ReturnsNull()
        {
            var result = Dbatools.Commands.DbaBaseCmdlet.GetPropertyString(null, "Edition");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyString_MissingEdition_ReturnsNull()
        {
            var obj = new PSObject();
            var result = Dbatools.Commands.DbaBaseCmdlet.GetPropertyString(obj, "NonExistent");
            Assert.IsNull(result);
        }

        #endregion GetPropertyString_ServerEdition

        #region Filter Combination

        [TestMethod]
        public void FilterIncludeAndExclude_CombinedFiltering_WorksCorrectly()
        {
            // Simulates Proxy=Test* then ExcludeProxy=TestProxy2
            var items = CreateProxyCollection("TestProxy1", "TestProxy2", "TestProxy3", "OtherProxy");
            var included = Dbatools.Commands.GetDbaAgentProxyCommand.FilterIncludeWildcard(items, new string[] { "Test*" });
            Assert.AreEqual(3, included.Count);
            var result = Dbatools.Commands.GetDbaAgentProxyCommand.FilterExcludeWildcard(included, new string[] { "TestProxy2" });
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("TestProxy1", GetName(result[0]));
            Assert.AreEqual("TestProxy3", GetName(result[1]));
        }

        #endregion Filter Combination

        #region Test Helpers

        private static Collection<PSObject> CreateProxyCollection(params string[] names)
        {
            var collection = new Collection<PSObject>();
            foreach (string name in names)
            {
                var obj = new PSObject();
                obj.Properties.Add(new PSNoteProperty("Name", name));
                collection.Add(obj);
            }
            return collection;
        }

        private static string GetName(PSObject obj)
        {
            return Dbatools.Commands.DbaBaseCmdlet.GetPropertyString(obj, "Name");
        }

        #endregion Test Helpers
    }
}
