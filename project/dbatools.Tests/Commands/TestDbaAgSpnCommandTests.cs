using System;
using System.Collections.Generic;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class TestDbaAgSpnCommandTests
    {
        #region BuildDnsDomain

        [TestMethod]
        public void BuildDnsDomain_WithFqdn_ReturnsDomainPart()
        {
            string result = TestDbaAgSpnCommand.BuildDnsDomain("server01.domain.local");
            Assert.AreEqual("domain.local", result);
        }

        [TestMethod]
        public void BuildDnsDomain_WithMultiLevelDomain_ReturnsFullDomain()
        {
            string result = TestDbaAgSpnCommand.BuildDnsDomain("server01.sub.domain.local");
            Assert.AreEqual("sub.domain.local", result);
        }

        [TestMethod]
        public void BuildDnsDomain_WithSingleName_ReturnsEmpty()
        {
            string result = TestDbaAgSpnCommand.BuildDnsDomain("server01");
            Assert.AreEqual("", result);
        }

        [TestMethod]
        public void BuildDnsDomain_WithNull_ReturnsEmpty()
        {
            string result = TestDbaAgSpnCommand.BuildDnsDomain(null);
            Assert.AreEqual("", result);
        }

        [TestMethod]
        public void BuildDnsDomain_WithEmpty_ReturnsEmpty()
        {
            string result = TestDbaAgSpnCommand.BuildDnsDomain("");
            Assert.AreEqual("", result);
        }

        #endregion BuildDnsDomain

        #region GetPlatformSuffix

        [TestMethod]
        public void GetPlatformSuffix_WithMultiWordPlatform_ReturnsLast()
        {
            var server = new PSObject();
            server.Properties.Add(new PSNoteProperty("Platform", "NT x64"));
            string result = TestDbaAgSpnCommand.GetPlatformSuffix(server);
            Assert.AreEqual("x64", result);
        }

        [TestMethod]
        public void GetPlatformSuffix_WithSingleWord_ReturnsSame()
        {
            var server = new PSObject();
            server.Properties.Add(new PSNoteProperty("Platform", "Linux"));
            string result = TestDbaAgSpnCommand.GetPlatformSuffix(server);
            Assert.AreEqual("Linux", result);
        }

        [TestMethod]
        public void GetPlatformSuffix_WithNullPlatform_ReturnsEmpty()
        {
            var server = new PSObject();
            string result = TestDbaAgSpnCommand.GetPlatformSuffix(server);
            Assert.AreEqual("", result);
        }

        #endregion GetPlatformSuffix

        #region BuildSpnObject

        [TestMethod]
        public void BuildSpnObject_CreatesCorrectProperties()
        {
            PSObject spn = TestDbaAgSpnCommand.BuildSpnObject(
                "server01.domain.local",
                "sql01",
                "MSSQLSERVER",
                "16.0.1000 Enterprise Edition x64",
                "LAB\\svc.mssql",
                "MSSQLSvc/listener.domain.local",
                false,
                1433,
                null);

            Assert.AreEqual("server01.domain.local", spn.Properties["ComputerName"].Value);
            Assert.AreEqual("sql01", spn.Properties["SqlInstance"].Value);
            Assert.AreEqual("MSSQLSERVER", spn.Properties["InstanceName"].Value);
            Assert.AreEqual("16.0.1000 Enterprise Edition x64", spn.Properties["SqlProduct"].Value);
            Assert.AreEqual("LAB\\svc.mssql", spn.Properties["InstanceServiceAccount"].Value);
            Assert.AreEqual("MSSQLSvc/listener.domain.local", spn.Properties["RequiredSPN"].Value);
            Assert.AreEqual(false, spn.Properties["IsSet"].Value);
            Assert.AreEqual(false, spn.Properties["Cluster"].Value);
            Assert.AreEqual(true, spn.Properties["TcpEnabled"].Value);
            Assert.AreEqual(1433, spn.Properties["Port"].Value);
            Assert.AreEqual(false, spn.Properties["DynamicPort"].Value);
            Assert.AreEqual("None", spn.Properties["Warning"].Value);
            Assert.AreEqual("None", spn.Properties["Error"].Value);
            Assert.IsNull(spn.Properties["Credential"].Value);
        }

        [TestMethod]
        public void BuildSpnObject_WithClustered_SetsClusterTrue()
        {
            PSObject spn = TestDbaAgSpnCommand.BuildSpnObject(
                "server01.domain.local",
                "sql01",
                "MSSQLSERVER",
                "version",
                "svc",
                "MSSQLSvc/test",
                true,
                1433,
                null);

            Assert.AreEqual(true, spn.Properties["Cluster"].Value);
        }

        [TestMethod]
        public void BuildSpnObject_HasAllExpectedProperties()
        {
            PSObject spn = TestDbaAgSpnCommand.BuildSpnObject(
                "comp", "inst", "name", "prod", "svc", "spn", false, 1433, null);

            string[] expectedProps = new string[]
            {
                "ComputerName", "SqlInstance", "InstanceName", "SqlProduct",
                "InstanceServiceAccount", "RequiredSPN", "IsSet", "Cluster",
                "TcpEnabled", "Port", "DynamicPort", "Warning", "Error", "Credential"
            };

            foreach (string prop in expectedProps)
            {
                Assert.IsNotNull(spn.Properties[prop],
                    String.Format("Missing property: {0}", prop));
            }
        }

        #endregion BuildSpnObject

        #region ContainsSPN

        [TestMethod]
        public void ContainsSPN_WithMatchingString_ReturnsTrue()
        {
            var list = new List<string> { "MSSQLSvc/listener.domain.local", "MSSQLSvc/listener.domain.local:1433" };
            bool result = TestDbaAgSpnCommand.ContainsSPN(list, "MSSQLSvc/listener.domain.local");
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void ContainsSPN_WithNoMatch_ReturnsFalse()
        {
            var list = new List<string> { "MSSQLSvc/other.domain.local" };
            bool result = TestDbaAgSpnCommand.ContainsSPN(list, "MSSQLSvc/listener.domain.local");
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void ContainsSPN_CaseInsensitive_ReturnsTrue()
        {
            var list = new List<string> { "MSSQLSvc/LISTENER.DOMAIN.LOCAL" };
            bool result = TestDbaAgSpnCommand.ContainsSPN(list, "MSSQLSvc/listener.domain.local");
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void ContainsSPN_WithNull_ReturnsFalse()
        {
            bool result = TestDbaAgSpnCommand.ContainsSPN(null, "MSSQLSvc/test");
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void ContainsSPN_WithNullSpn_ReturnsFalse()
        {
            var list = new List<string> { "MSSQLSvc/test" };
            bool result = TestDbaAgSpnCommand.ContainsSPN(list, null);
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void ContainsSPN_WithSingleValue_ReturnsTrue()
        {
            bool result = TestDbaAgSpnCommand.ContainsSPN("MSSQLSvc/listener.domain.local", "MSSQLSvc/listener.domain.local");
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void ContainsSPN_WithEmptyList_ReturnsFalse()
        {
            var list = new List<string>();
            bool result = TestDbaAgSpnCommand.ContainsSPN(list, "MSSQLSvc/test");
            Assert.IsFalse(result);
        }

        #endregion ContainsSPN

        #region SetProperty

        [TestMethod]
        public void SetProperty_UpdatesExistingValue()
        {
            var obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("IsSet", false));
            TestDbaAgSpnCommand.SetProperty(obj, "IsSet", true);
            Assert.AreEqual(true, obj.Properties["IsSet"].Value);
        }

        [TestMethod]
        public void SetProperty_AddsNewProperty()
        {
            var obj = new PSObject();
            TestDbaAgSpnCommand.SetProperty(obj, "NewProp", "value");
            Assert.AreEqual("value", obj.Properties["NewProp"].Value);
        }

        [TestMethod]
        public void SetProperty_WithNullObject_DoesNotThrow()
        {
            TestDbaAgSpnCommand.SetProperty(null, "Prop", "value");
        }

        #endregion SetProperty

        #region GetPropertyString

        [TestMethod]
        public void GetPropertyString_WithNullObject_ReturnsNull()
        {
            string result = TestDbaAgSpnCommand.GetPropertyString(null, "Name");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyString_WithExistingProperty_ReturnsValue()
        {
            var obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "TestValue"));
            string result = TestDbaAgSpnCommand.GetPropertyString(obj, "Name");
            Assert.AreEqual("TestValue", result);
        }

        [TestMethod]
        public void GetPropertyString_WithMissingProperty_ReturnsNull()
        {
            var obj = new PSObject();
            string result = TestDbaAgSpnCommand.GetPropertyString(obj, "NoSuchProp");
            Assert.IsNull(result);
        }

        #endregion GetPropertyString

        #region GetNestedPropertyString

        [TestMethod]
        public void GetNestedPropertyString_WithNestedObject_ReturnsValue()
        {
            var inner = new PSObject();
            inner.Properties.Add(new PSNoteProperty("Name", "AG01"));
            var outer = new PSObject();
            outer.Properties.Add(new PSNoteProperty("Parent", inner));

            string result = TestDbaAgSpnCommand.GetNestedPropertyString(outer, "Parent", "Name");
            Assert.AreEqual("AG01", result);
        }

        [TestMethod]
        public void GetNestedPropertyString_WithNullOuter_ReturnsNull()
        {
            string result = TestDbaAgSpnCommand.GetNestedPropertyString(null, "Parent", "Name");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetNestedPropertyString_WithMissingInnerProp_ReturnsNull()
        {
            var inner = new PSObject();
            var outer = new PSObject();
            outer.Properties.Add(new PSNoteProperty("Parent", inner));

            string result = TestDbaAgSpnCommand.GetNestedPropertyString(outer, "Parent", "Missing");
            Assert.IsNull(result);
        }

        #endregion GetNestedPropertyString
    }
}
