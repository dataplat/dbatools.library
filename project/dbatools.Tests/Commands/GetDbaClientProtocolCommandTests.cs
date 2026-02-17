using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class GetDbaClientProtocolCommandTests
    {
        // Note: limited unit test coverage — command is primarily a CIM/WMI wrapper
        // via Get-DbaCmObject. The testable pure logic is the script block content.

        #region GetProcessComputerScript
        [TestMethod]
        public void GetProcessComputerScript_ReturnsNonEmptyString()
        {
            string script = GetDbaClientProtocolCommand.GetProcessComputerScript();
            Assert.IsFalse(String.IsNullOrWhiteSpace(script));
        }

        [TestMethod]
        public void GetProcessComputerScript_AcceptsComputerNameAndCredentialParams()
        {
            string script = GetDbaClientProtocolCommand.GetProcessComputerScript();
            Assert.IsTrue(script.Contains("param($computerName, $credential)"));
        }

        [TestMethod]
        public void GetProcessComputerScript_ContainsResolveDbaNetworkName()
        {
            string script = GetDbaClientProtocolCommand.GetProcessComputerScript();
            Assert.IsTrue(script.Contains("Resolve-DbaNetworkName"));
            Assert.IsTrue(script.Contains("FullComputerName"));
        }

        [TestMethod]
        public void GetProcessComputerScript_ContainsNamespaceDiscovery()
        {
            string script = GetDbaClientProtocolCommand.GetProcessComputerScript();
            Assert.IsTrue(script.Contains("root\\Microsoft\\SQLServer"));
            Assert.IsTrue(script.Contains("ComputerManagement%"));
            Assert.IsTrue(script.Contains("__NAMESPACE"));
        }

        [TestMethod]
        public void GetProcessComputerScript_ContainsClientNetworkProtocolQuery()
        {
            string script = GetDbaClientProtocolCommand.GetProcessComputerScript();
            Assert.IsTrue(script.Contains("ClientNetworkProtocol"));
            Assert.IsTrue(script.Contains("Get-DbaCmObject"));
        }

        [TestMethod]
        public void GetProcessComputerScript_AddsIsEnabledScriptProperty()
        {
            string script = GetDbaClientProtocolCommand.GetProcessComputerScript();
            Assert.IsTrue(script.Contains("IsEnabled"));
            Assert.IsTrue(script.Contains("ProtocolOrder"));
            Assert.IsTrue(script.Contains("ScriptProperty"));
        }

        [TestMethod]
        public void GetProcessComputerScript_AddsEnableAndDisableMethods()
        {
            string script = GetDbaClientProtocolCommand.GetProcessComputerScript();
            Assert.IsTrue(script.Contains("ScriptMethod"));
            Assert.IsTrue(script.Contains("SetEnable"));
            Assert.IsTrue(script.Contains("SetDisable"));
            Assert.IsTrue(script.Contains("Invoke-CimMethod"));
        }

        [TestMethod]
        public void GetProcessComputerScript_ContainsDefaultViewProperties()
        {
            string script = GetDbaClientProtocolCommand.GetProcessComputerScript();
            Assert.IsTrue(script.Contains("Select-DefaultView"));
            Assert.IsTrue(script.Contains("PSComputerName as ComputerName"));
            Assert.IsTrue(script.Contains("ProtocolDisplayName as DisplayName"));
            Assert.IsTrue(script.Contains("ProtocolDll as DLL"));
            Assert.IsTrue(script.Contains("ProtocolOrder as Order"));
            Assert.IsTrue(script.Contains("IsEnabled"));
        }

        [TestMethod]
        public void GetProcessComputerScript_ContainsWarningMessages()
        {
            string script = GetDbaClientProtocolCommand.GetProcessComputerScript();
            Assert.IsTrue(script.Contains("No Sql ClientNetworkProtocol found on"));
            Assert.IsTrue(script.Contains("No ComputerManagement Namespace on"));
            Assert.IsTrue(script.Contains("SQL 2005"));
            Assert.IsTrue(script.Contains("Failed to connect to"));
        }

        [TestMethod]
        public void GetProcessComputerScript_PassesCredentialToResolveDbaNetworkName()
        {
            string script = GetDbaClientProtocolCommand.GetProcessComputerScript();
            Assert.IsTrue(script.Contains("Resolve-DbaNetworkName -ComputerName $computerName -Credential $credential"));
        }

        [TestMethod]
        public void GetProcessComputerScript_SortsNamespaceDescending()
        {
            string script = GetDbaClientProtocolCommand.GetProcessComputerScript();
            Assert.IsTrue(script.Contains("Sort-Object Name -Descending"));
            Assert.IsTrue(script.Contains("Select-Object -First 1"));
        }
        #endregion
    }
}
