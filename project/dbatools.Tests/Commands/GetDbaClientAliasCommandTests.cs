using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class GetDbaClientAliasCommandTests
    {
        // Note: limited unit test coverage — command is primarily a remote registry
        // reader via Invoke-Command2. The testable pure logic is the script block content.

        #region GetRegistryScriptBlock
        [TestMethod]
        public void GetRegistryScriptBlock_ReturnsNonEmptyString()
        {
            string script = GetDbaClientAliasCommand.GetRegistryScriptBlock();
            Assert.IsFalse(String.IsNullOrWhiteSpace(script));
        }

        [TestMethod]
        public void GetRegistryScriptBlock_ContainsBothRegistryBaseKeys()
        {
            string script = GetDbaClientAliasCommand.GetRegistryScriptBlock();
            Assert.IsTrue(script.Contains("WOW6432Node"));
            Assert.IsTrue(script.Contains("HKLM:\\SOFTWARE\\Microsoft\\MSSQLServer"));
        }

        [TestMethod]
        public void GetRegistryScriptBlock_ContainsConnectToSubkey()
        {
            string script = GetDbaClientAliasCommand.GetRegistryScriptBlock();
            Assert.IsTrue(script.Contains("ConnectTo"));
            Assert.IsTrue(script.Contains("Client"));
        }

        [TestMethod]
        public void GetRegistryScriptBlock_ContainsProtocolConstants()
        {
            string script = GetDbaClientAliasCommand.GetRegistryScriptBlock();
            Assert.IsTrue(script.Contains("DBMSSOCN"));
            Assert.IsTrue(script.Contains("DBNMPNTW"));
            Assert.IsTrue(script.Contains("TCP/IP"));
            Assert.IsTrue(script.Contains("Named Pipes"));
        }

        [TestMethod]
        public void GetRegistryScriptBlock_ContainsAllOutputProperties()
        {
            string script = GetDbaClientAliasCommand.GetRegistryScriptBlock();
            Assert.IsTrue(script.Contains("ComputerName"));
            Assert.IsTrue(script.Contains("NetworkLibrary"));
            Assert.IsTrue(script.Contains("ServerName"));
            Assert.IsTrue(script.Contains("AliasName"));
            Assert.IsTrue(script.Contains("AliasString"));
            Assert.IsTrue(script.Contains("Architecture"));
        }

        [TestMethod]
        public void GetRegistryScriptBlock_ContainsArchitectureDetection()
        {
            string script = GetDbaClientAliasCommand.GetRegistryScriptBlock();
            Assert.IsTrue(script.Contains("32-bit"));
            Assert.IsTrue(script.Contains("64-bit"));
            Assert.IsTrue(script.Contains("WOW64"));
        }

        [TestMethod]
        public void GetRegistryScriptBlock_ContainsGetItemPropertyValueHelper()
        {
            string script = GetDbaClientAliasCommand.GetRegistryScriptBlock();
            Assert.IsTrue(script.Contains("function Get-ItemPropertyValue"));
            Assert.IsTrue(script.Contains("Get-ItemProperty -LiteralPath"));
        }

        [TestMethod]
        public void GetRegistryScriptBlock_ContainsTestPathGuards()
        {
            string script = GetDbaClientAliasCommand.GetRegistryScriptBlock();
            Assert.IsTrue(script.Contains("Test-Path"));
            Assert.IsTrue(script.Contains("continue"));
        }
        #endregion
    }
}
