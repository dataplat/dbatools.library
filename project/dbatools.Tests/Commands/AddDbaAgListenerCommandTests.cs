using System.Net;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class AddDbaAgListenerCommandTests
    {
        #region CalculateSubnet Tests

        [TestMethod]
        public void CalculateSubnet_StandardClassC_ReturnsNetworkAddress()
        {
            IPAddress ip = IPAddress.Parse("10.0.20.20");
            IPAddress mask = IPAddress.Parse("255.255.255.0");

            IPAddress result = AddDbaAgListenerCommand.CalculateSubnet(ip, mask);

            Assert.AreEqual("10.0.20.0", result.ToString());
        }

        [TestMethod]
        public void CalculateSubnet_ClassBMask_ReturnsNetworkAddress()
        {
            IPAddress ip = IPAddress.Parse("172.16.5.100");
            IPAddress mask = IPAddress.Parse("255.255.0.0");

            IPAddress result = AddDbaAgListenerCommand.CalculateSubnet(ip, mask);

            Assert.AreEqual("172.16.0.0", result.ToString());
        }

        [TestMethod]
        public void CalculateSubnet_Slash22Mask_ReturnsNetworkAddress()
        {
            IPAddress ip = IPAddress.Parse("10.0.20.20");
            IPAddress mask = IPAddress.Parse("255.255.252.0");

            IPAddress result = AddDbaAgListenerCommand.CalculateSubnet(ip, mask);

            Assert.AreEqual("10.0.20.0", result.ToString());
        }

        [TestMethod]
        public void CalculateSubnet_Slash22Mask_HigherOctet_ReturnsNetworkAddress()
        {
            IPAddress ip = IPAddress.Parse("10.1.77.77");
            IPAddress mask = IPAddress.Parse("255.255.252.0");

            IPAddress result = AddDbaAgListenerCommand.CalculateSubnet(ip, mask);

            Assert.AreEqual("10.1.76.0", result.ToString());
        }

        [TestMethod]
        public void CalculateSubnet_HostBitsAllOnes_ReturnsNetworkAddress()
        {
            IPAddress ip = IPAddress.Parse("192.168.1.255");
            IPAddress mask = IPAddress.Parse("255.255.255.0");

            IPAddress result = AddDbaAgListenerCommand.CalculateSubnet(ip, mask);

            Assert.AreEqual("192.168.1.0", result.ToString());
        }

        [TestMethod]
        public void CalculateSubnet_AllZerosMask_ReturnsAllZeros()
        {
            IPAddress ip = IPAddress.Parse("10.20.30.40");
            IPAddress mask = IPAddress.Parse("0.0.0.0");

            IPAddress result = AddDbaAgListenerCommand.CalculateSubnet(ip, mask);

            Assert.AreEqual("0.0.0.0", result.ToString());
        }

        [TestMethod]
        public void CalculateSubnet_AllOnesMask_ReturnsOriginalIP()
        {
            IPAddress ip = IPAddress.Parse("10.20.30.40");
            IPAddress mask = IPAddress.Parse("255.255.255.255");

            IPAddress result = AddDbaAgListenerCommand.CalculateSubnet(ip, mask);

            Assert.AreEqual("10.20.30.40", result.ToString());
        }

        #endregion CalculateSubnet Tests

        #region ExpandArray Tests

        [TestMethod]
        public void ExpandArray_SingleToThree_ReturnsRepeatedElement()
        {
            IPAddress[] source = new IPAddress[] { IPAddress.Parse("255.255.255.0") };

            IPAddress[] result = AddDbaAgListenerCommand.ExpandArray(source, 3);

            Assert.AreEqual(3, result.Length);
            Assert.AreEqual("255.255.255.0", result[0].ToString());
            Assert.AreEqual("255.255.255.0", result[1].ToString());
            Assert.AreEqual("255.255.255.0", result[2].ToString());
        }

        [TestMethod]
        public void ExpandArray_SingleToOne_ReturnsSingleElement()
        {
            IPAddress[] source = new IPAddress[] { IPAddress.Parse("10.0.0.0") };

            IPAddress[] result = AddDbaAgListenerCommand.ExpandArray(source, 1);

            Assert.AreEqual(1, result.Length);
            Assert.AreEqual("10.0.0.0", result[0].ToString());
        }

        [TestMethod]
        public void ExpandArray_NullInput_ReturnsNull()
        {
            IPAddress[] result = AddDbaAgListenerCommand.ExpandArray(null, 3);

            Assert.IsNull(result);
        }

        [TestMethod]
        public void ExpandArray_EmptyInput_ReturnsEmpty()
        {
            IPAddress[] result = AddDbaAgListenerCommand.ExpandArray(new IPAddress[0], 3);

            Assert.AreEqual(0, result.Length);
        }

        #endregion ExpandArray Tests

        #region GetIPAddressStrings Tests

        [TestMethod]
        public void GetIPAddressStrings_MultipleIPs_ReturnsStringArray()
        {
            IPAddress[] addresses = new IPAddress[]
            {
                IPAddress.Parse("10.0.20.20"),
                IPAddress.Parse("10.1.77.77")
            };

            string[] result = AddDbaAgListenerCommand.GetIPAddressStrings(addresses);

            Assert.AreEqual(2, result.Length);
            Assert.AreEqual("10.0.20.20", result[0]);
            Assert.AreEqual("10.1.77.77", result[1]);
        }

        [TestMethod]
        public void GetIPAddressStrings_SingleIP_ReturnsSingleString()
        {
            IPAddress[] addresses = new IPAddress[] { IPAddress.Parse("192.168.1.1") };

            string[] result = AddDbaAgListenerCommand.GetIPAddressStrings(addresses);

            Assert.AreEqual(1, result.Length);
            Assert.AreEqual("192.168.1.1", result[0]);
        }

        [TestMethod]
        public void GetIPAddressStrings_NullInput_ReturnsEmptyArray()
        {
            string[] result = AddDbaAgListenerCommand.GetIPAddressStrings(null);

            Assert.AreEqual(0, result.Length);
        }

        #endregion GetIPAddressStrings Tests

        #region GetPropertyString Tests

        [TestMethod]
        public void GetPropertyString_ExistingProperty_ReturnsValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "TestListener"));

            string result = AddDbaAgListenerCommand.GetPropertyString(obj, "Name");

            Assert.AreEqual("TestListener", result);
        }

        [TestMethod]
        public void GetPropertyString_MissingProperty_ReturnsNull()
        {
            PSObject obj = new PSObject();

            string result = AddDbaAgListenerCommand.GetPropertyString(obj, "NonExistent");

            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyString_NullObject_ReturnsNull()
        {
            string result = AddDbaAgListenerCommand.GetPropertyString(null, "Name");

            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyString_NullPropertyValue_ReturnsNull()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", null));

            string result = AddDbaAgListenerCommand.GetPropertyString(obj, "Name");

            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyString_IntegerProperty_ReturnsString()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Port", 1433));

            string result = AddDbaAgListenerCommand.GetPropertyString(obj, "Port");

            Assert.AreEqual("1433", result);
        }

        #endregion GetPropertyString Tests

        #region GetPropertyObject Tests

        [TestMethod]
        public void GetPropertyObject_ExistingProperty_ReturnsPSObject()
        {
            PSObject parent = new PSObject();
            parent.Properties.Add(new PSNoteProperty("Name", "ServerName"));

            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Parent", parent));

            PSObject result = AddDbaAgListenerCommand.GetPropertyObject(obj, "Parent");

            Assert.IsNotNull(result);
        }

        [TestMethod]
        public void GetPropertyObject_MissingProperty_ReturnsNull()
        {
            PSObject obj = new PSObject();

            PSObject result = AddDbaAgListenerCommand.GetPropertyObject(obj, "Parent");

            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyObject_NullObject_ReturnsNull()
        {
            PSObject result = AddDbaAgListenerCommand.GetPropertyObject(null, "Parent");

            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPropertyObject_NullPropertyValue_ReturnsNull()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Parent", null));

            PSObject result = AddDbaAgListenerCommand.GetPropertyObject(obj, "Parent");

            Assert.IsNull(result);
        }

        #endregion GetPropertyObject Tests

        #region CalculateSubnet Edge Cases

        [TestMethod]
        public void CalculateSubnet_MultiSubnetExample_FirstIP()
        {
            // From PS1 example: Add-DbaAgListener -IPAddress 10.0.20.20,10.1.77.77 -SubnetMask 255.255.252.0
            IPAddress ip = IPAddress.Parse("10.0.20.20");
            IPAddress mask = IPAddress.Parse("255.255.252.0");

            IPAddress result = AddDbaAgListenerCommand.CalculateSubnet(ip, mask);

            Assert.AreEqual("10.0.20.0", result.ToString());
        }

        [TestMethod]
        public void CalculateSubnet_MultiSubnetExample_SecondIP()
        {
            // From PS1 example: second IP in multi-subnet listener
            IPAddress ip = IPAddress.Parse("10.1.77.77");
            IPAddress mask = IPAddress.Parse("255.255.252.0");

            IPAddress result = AddDbaAgListenerCommand.CalculateSubnet(ip, mask);

            Assert.AreEqual("10.1.76.0", result.ToString());
        }

        #endregion CalculateSubnet Edge Cases
    }
}
