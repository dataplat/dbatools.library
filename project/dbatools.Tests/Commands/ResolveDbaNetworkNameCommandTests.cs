using System;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class ResolveDbaNetworkNameCommandTests
    {
        #region GetComputerDomainName

        [TestMethod]
        public void GetComputerDomainName_FqdnWithDot_ReturnsDomainPart()
        {
            // Arrange
            string fqdn = "server01.contoso.com";
            string computerName = "server01";

            // Act
            string result = Dataplat.Dbatools.Commands.ResolveDbaNetworkNameCommand.GetComputerDomainName(fqdn, computerName);

            // Assert
            Assert.AreEqual("contoso.com", result);
        }

        [TestMethod]
        public void GetComputerDomainName_FqdnNoDot_ComputerNameWithDot_ReturnsDomainFromComputerName()
        {
            // Arrange
            string fqdn = "server01";
            string computerName = "server01.contoso.com";

            // Act
            string result = Dataplat.Dbatools.Commands.ResolveDbaNetworkNameCommand.GetComputerDomainName(fqdn, computerName);

            // Assert
            Assert.AreEqual("contoso.com", result);
        }

        [TestMethod]
        public void GetComputerDomainName_FqdnNoDot_ComputerNameNoDot_ReturnsUserDnsDomainOrEmpty()
        {
            // Arrange
            string fqdn = "server01";
            string computerName = "server01";

            // Act
            string result = Dataplat.Dbatools.Commands.ResolveDbaNetworkNameCommand.GetComputerDomainName(fqdn, computerName);

            // Assert
            // On domain-joined machines, returns USERDNSDOMAIN; on non-domain, returns empty
            string expected = Environment.GetEnvironmentVariable("USERDNSDOMAIN");
            if (!String.IsNullOrEmpty(expected))
            {
                Assert.AreEqual(expected.ToLowerInvariant(), result);
            }
            else
            {
                Assert.AreEqual(String.Empty, result);
            }
        }

        [TestMethod]
        public void GetComputerDomainName_NullFqdn_ReturnsUserDnsDomainOrEmpty()
        {
            // Arrange & Act
            string result = Dataplat.Dbatools.Commands.ResolveDbaNetworkNameCommand.GetComputerDomainName(null, null);

            // Assert
            string expected = Environment.GetEnvironmentVariable("USERDNSDOMAIN");
            if (!String.IsNullOrEmpty(expected))
            {
                Assert.AreEqual(expected.ToLowerInvariant(), result);
            }
            else
            {
                Assert.AreEqual(String.Empty, result);
            }
        }

        [TestMethod]
        public void GetComputerDomainName_MultipleDots_ReturnsFullDomainSuffix()
        {
            // Arrange
            string fqdn = "server01.sub.contoso.com";
            string computerName = "server01";

            // Act
            string result = Dataplat.Dbatools.Commands.ResolveDbaNetworkNameCommand.GetComputerDomainName(fqdn, computerName);

            // Assert
            Assert.AreEqual("sub.contoso.com", result);
        }

        #endregion GetComputerDomainName

        #region GetPSProperty

        [TestMethod]
        public void GetPSProperty_ExistingProperty_ReturnsValue()
        {
            // Arrange
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("TestProp", "TestValue"));

            // Act
            object result = Dataplat.Dbatools.Commands.ResolveDbaNetworkNameCommand.GetPSProperty(obj, "TestProp");

            // Assert
            Assert.AreEqual("TestValue", result);
        }

        [TestMethod]
        public void GetPSProperty_NonExistingProperty_ReturnsNull()
        {
            // Arrange
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("TestProp", "TestValue"));

            // Act
            object result = Dataplat.Dbatools.Commands.ResolveDbaNetworkNameCommand.GetPSProperty(obj, "MissingProp");

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPSProperty_NullObject_ReturnsNull()
        {
            // Act
            object result = Dataplat.Dbatools.Commands.ResolveDbaNetworkNameCommand.GetPSProperty(null, "TestProp");

            // Assert
            Assert.IsNull(result);
        }

        #endregion GetPSProperty

        #region GetPSPropertyString

        [TestMethod]
        public void GetPSPropertyString_ExistingStringProperty_ReturnsString()
        {
            // Arrange
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "server01"));

            // Act
            string result = Dataplat.Dbatools.Commands.ResolveDbaNetworkNameCommand.GetPSPropertyString(obj, "Name");

            // Assert
            Assert.AreEqual("server01", result);
        }

        [TestMethod]
        public void GetPSPropertyString_IntProperty_ReturnsToString()
        {
            // Arrange
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Count", 42));

            // Act
            string result = Dataplat.Dbatools.Commands.ResolveDbaNetworkNameCommand.GetPSPropertyString(obj, "Count");

            // Assert
            Assert.AreEqual("42", result);
        }

        [TestMethod]
        public void GetPSPropertyString_NullObject_ReturnsNull()
        {
            // Act
            string result = Dataplat.Dbatools.Commands.ResolveDbaNetworkNameCommand.GetPSPropertyString(null, "Name");

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPSPropertyString_MissingProperty_ReturnsNull()
        {
            // Arrange
            PSObject obj = new PSObject();

            // Act
            string result = Dataplat.Dbatools.Commands.ResolveDbaNetworkNameCommand.GetPSPropertyString(obj, "Missing");

            // Assert
            Assert.IsNull(result);
        }

        #endregion GetPSPropertyString
    }
}