using System;
using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Exceptions;

namespace Dataplat.Dbatools.Parameter
{
    [TestClass]
    public class DbaInstanceParamaterTest
    {
        [TestMethod]
        public void TestStringConstructor()
        {
            var dbaInstanceParamater = new DbaInstanceParameter("someMachine");

            Assert.AreEqual("someMachine", dbaInstanceParamater.FullName);
            Assert.AreEqual("someMachine", dbaInstanceParamater.FullSmoName);
            Assert.AreEqual(SqlConnectionProtocol.Any, dbaInstanceParamater.NetworkProtocol);
            Assert.IsFalse(dbaInstanceParamater.IsLocalHost);
            Assert.IsFalse(dbaInstanceParamater.IsConnectionString);
        }

        [DataRow(null)]
        [DataRow("")]
        [DataRow(" ")]
        [DataRow("\n")]
        [DataRow(" \n \t")]
        [DataRow(" \v\t\t ")]
        [DataRow(null)]
        [ExpectedException(typeof(BloodyHellGiveMeSomethingToWorkWithException), "Bloody hell! Don't give me an empty string for an instance name")]
        [TestMethod]
        public void TestEmptyString(string whitespace)
        {
            try
            {
                new DbaInstanceParameter(whitespace);
            }
            catch (BloodyHellGiveMeSomethingToWorkWithException ex)
            {
                Assert.AreEqual("DbaInstanceParameter", ex.ParameterClass);
                throw;
            }
        }

        [TestMethod]
        public void TestConnectionString()
        {
            var dbaInstanceParamater = new DbaInstanceParameter("Server=tcp:server.database.windows.net;Database=myDataBase;User ID =[LoginForDb]@[serverName]; Password = myPassword; Trusted_Connection = False;Encrypt = True; ");
            Assert.IsTrue(dbaInstanceParamater.IsConnectionString);
        }

        [ExpectedException(typeof(ArgumentException))]
        [TestMethod]
        public void TestConnectionStringBadKey()
        {
            new DbaInstanceParameter("Server=tcp:server.database.windows.net;Database=myDataBase;Trusted_Connection = True;Wrong=true");
        }

        [ExpectedException(typeof(FormatException))]
        [TestMethod]
        public void TestConnectionStringBadValue()
        {
            new DbaInstanceParameter("Server=tcp:server.database.windows.net;Database=myDataBase;Trusted_Connection=weird");
        }

        /// <summary>
        /// Checks that localhost\instancename is treated as a localhost connection
        /// </summary>
        [TestMethod]
        public void TestLocalhostNamedInstance()
        {
            var dbaInstanceParamater = new DbaInstanceParameter("localhost\\sql2008r2sp2");

            Assert.AreEqual("localhost\\sql2008r2sp2", dbaInstanceParamater.FullName);
            Assert.IsTrue(dbaInstanceParamater.IsLocalHost);
            Assert.AreEqual("localhost\\sql2008r2sp2", dbaInstanceParamater.FullSmoName);
            Assert.AreEqual("[localhost\\sql2008r2sp2]", dbaInstanceParamater.SqlFullName);
            Assert.AreEqual(SqlConnectionProtocol.Any, dbaInstanceParamater.NetworkProtocol);
            Assert.IsTrue(dbaInstanceParamater.IsLocalHost);
            Assert.IsFalse(dbaInstanceParamater.IsConnectionString);
        }

        /// <summary>
        /// Checks that . is treated as a localhost connection
        /// </summary>
        [TestMethod]
        public void TestDotHostname()
        {
            var dbaInstanceParamater = new DbaInstanceParameter(".");

            Assert.AreEqual(".", dbaInstanceParamater.ComputerName);
            Assert.AreEqual("[.]", dbaInstanceParamater.SqlComputerName);
            Assert.AreEqual(".", dbaInstanceParamater.FullName);
            Assert.IsTrue(dbaInstanceParamater.IsLocalHost);
            Assert.AreEqual("NP:.", dbaInstanceParamater.FullSmoName);
            Assert.AreEqual("NP_.", dbaInstanceParamater.FileNameFriendly);
            Assert.AreEqual(@"MSSQLSERVER", dbaInstanceParamater.InstanceName);
            Assert.AreEqual(@"[MSSQLSERVER]", dbaInstanceParamater.SqlInstanceName);
            Assert.AreEqual(@"[.]", dbaInstanceParamater.SqlFullName);
            Assert.AreEqual(SqlConnectionProtocol.NP, dbaInstanceParamater.NetworkProtocol);
            Assert.IsTrue(dbaInstanceParamater.IsLocalHost);
            Assert.IsFalse(dbaInstanceParamater.IsConnectionString);
        }

        /// <summary>
        /// Checks that . is treated as a localhost connection
        /// </summary>
        [TestMethod]
        public void TestDotHostnameWithInstance()
        {
            var dbaInstanceParamater = new DbaInstanceParameter(@".\instancename");

            Assert.AreEqual(".", dbaInstanceParamater.ComputerName);
            Assert.AreEqual("[.]", dbaInstanceParamater.SqlComputerName);
            Assert.AreEqual(@".\instancename", dbaInstanceParamater.FullName);
            Assert.IsTrue(dbaInstanceParamater.IsLocalHost);
            Assert.AreEqual(@"NP:.\instancename", dbaInstanceParamater.FullSmoName);
            Assert.AreEqual(@"NP_._instancename", dbaInstanceParamater.FileNameFriendly);
            Assert.AreEqual(@"instancename", dbaInstanceParamater.InstanceName);
            Assert.AreEqual(@"[instancename]", dbaInstanceParamater.SqlInstanceName);
            Assert.AreEqual(@"[.\instancename]", dbaInstanceParamater.SqlFullName);
            Assert.AreEqual(SqlConnectionProtocol.NP, dbaInstanceParamater.NetworkProtocol);
            Assert.IsTrue(dbaInstanceParamater.IsLocalHost);
            Assert.IsFalse(dbaInstanceParamater.IsConnectionString);
        }

        /// <summary>
        /// Checks that localdb named instances
        /// </summary>
        [TestMethod]
        //[Ignore()]
        public void TestLocalDb()
        {
            var dbaInstanceParamater = new DbaInstanceParameter(@"(LocalDb)\MSSQLLocalDB");

            Assert.AreEqual("localhost", dbaInstanceParamater.ComputerName);
            Assert.AreEqual("[localhost]", dbaInstanceParamater.SqlComputerName);
            Assert.AreEqual(@"(localdb)\MSSQLLocalDB", dbaInstanceParamater.FullName);
            Assert.AreEqual(@"(localdb)\MSSQLLocalDB", dbaInstanceParamater.FullSmoName);
            Assert.AreEqual(@"MSSQLLocalDB", dbaInstanceParamater.InstanceName);
            Assert.AreEqual(@"[MSSQLLocalDB]", dbaInstanceParamater.SqlInstanceName);
            Assert.AreEqual(SqlConnectionProtocol.Any, dbaInstanceParamater.NetworkProtocol);
            Assert.IsTrue(dbaInstanceParamater.IsLocalHost);
            Assert.IsFalse(dbaInstanceParamater.IsConnectionString);
        }

        /// <summary>
        /// Checks parsing of a localdb connectionstring
        /// </summary>
        [TestMethod]
        public void TestLocalDbConnectionString()
        {
            var dbaInstanceParamater = new DbaInstanceParameter(@"Data Source=(LocalDb)\MSSQLLocalDB;Initial Catalog=aspnet-MvcMovie;Integrated Security=SSPI;AttachDBFilename=|DataDirectory|\Movies.mdf");

            Assert.AreEqual("localhost", dbaInstanceParamater.ComputerName);
            Assert.AreEqual("[localhost]", dbaInstanceParamater.SqlComputerName);
            Assert.AreEqual(@"localhost\MSSQLLocalDB", dbaInstanceParamater.FullName);
            Assert.AreEqual(@"localhost\MSSQLLocalDB", dbaInstanceParamater.FullSmoName);
            Assert.AreEqual(@"localhost\MSSQLLocalDB", dbaInstanceParamater.ToString());
            Assert.AreEqual(@"MSSQLLocalDB", dbaInstanceParamater.InstanceName);
            Assert.AreEqual(SqlConnectionProtocol.Any, dbaInstanceParamater.NetworkProtocol);
            Assert.IsTrue(dbaInstanceParamater.IsLocalHost);
            Assert.IsTrue(dbaInstanceParamater.IsConnectionString);
        }

        /// <summary>
        /// Checks that 127.0.0.1 is treated as a localhost connection
        /// </summary>
        [DataRow("127.0.0.1")]
        [DataRow("::1")]
        [DataRow("0.0.0.0")]
        [DataRow("192.168.1.1")]
        [DataTestMethod]
        [TestMethod]
        public void TestIpAddressConstructor(string ipStr)
        {
            var ip = IPAddress.Parse(ipStr);
            var dbaInstanceParamater = new DbaInstanceParameter(ip);

            Assert.AreEqual(ip.ToString(), dbaInstanceParamater.FullName);
            Assert.AreEqual('[' + ip.ToString() + ']', dbaInstanceParamater.SqlFullName);
            Assert.AreEqual(ip.ToString(), dbaInstanceParamater.FullSmoName);
            Assert.AreEqual(ip.ToString(), dbaInstanceParamater.ToString());
            Assert.AreEqual(SqlConnectionProtocol.Any, dbaInstanceParamater.NetworkProtocol);
        }

        /// <summary>
        /// Checks that 127.0.0.1 is treated as a localhost connection
        /// </summary>
        [DataRow("127.0.0.1")]
        [DataRow("::1")]
        [DataRow("localhost")]
        [DataTestMethod]
        [TestMethod]
        public void TestLocalhost(string localhost)
        {
            var dbaInstanceParamater = new DbaInstanceParameter(localhost);

            Assert.AreEqual(localhost, dbaInstanceParamater.FullName);
            Assert.AreEqual('[' + localhost + ']', dbaInstanceParamater.SqlFullName);
            Assert.AreEqual(localhost, dbaInstanceParamater.FullSmoName);
            Assert.AreEqual(localhost, dbaInstanceParamater.ToString());
            Assert.AreEqual(SqlConnectionProtocol.Any, dbaInstanceParamater.NetworkProtocol);
            Assert.IsTrue(dbaInstanceParamater.IsLocalHost);
        }

        /// <summary>
        /// Checks that instance names with dashes are properly parsed
        /// </summary>
        [TestMethod]
        public void TestInstanceNameWithDash()
        {
            var dbaInstanceParamater = new DbaInstanceParameter("My-Instance.domain.local\\My-TestInstance");

            Assert.AreEqual("My-Instance.domain.local", dbaInstanceParamater.ComputerName);
            Assert.AreEqual("My-TestInstance", dbaInstanceParamater.InstanceName);
            Assert.AreEqual("My-Instance.domain.local\\My-TestInstance", dbaInstanceParamater.FullName);
            Assert.AreEqual("My-Instance.domain.local\\My-TestInstance", dbaInstanceParamater.FullSmoName);
            Assert.AreEqual("[My-Instance.domain.local]", dbaInstanceParamater.SqlComputerName);
            Assert.AreEqual("[My-TestInstance]", dbaInstanceParamater.SqlInstanceName);
            Assert.AreEqual("[My-Instance.domain.local\\My-TestInstance]", dbaInstanceParamater.SqlFullName);
            Assert.IsFalse(dbaInstanceParamater.IsConnectionString);
        }

        /// <summary>
        /// Checks that server names with dashes work correctly (regression test)
        /// </summary>
        [TestMethod]
        public void TestServerNameWithDash()
        {
            var dbaInstanceParamater = new DbaInstanceParameter("My-Instance.domain.local\\MyTestInstance");

            Assert.AreEqual("My-Instance.domain.local", dbaInstanceParamater.ComputerName);
            Assert.AreEqual("MyTestInstance", dbaInstanceParamater.InstanceName);
            Assert.AreEqual("My-Instance.domain.local\\MyTestInstance", dbaInstanceParamater.FullName);
            Assert.AreEqual("My-Instance.domain.local\\MyTestInstance", dbaInstanceParamater.FullSmoName);
            Assert.AreEqual("[My-Instance.domain.local]", dbaInstanceParamater.SqlComputerName);
            Assert.AreEqual("[MyTestInstance]", dbaInstanceParamater.SqlInstanceName);
            Assert.AreEqual("[My-Instance.domain.local\\MyTestInstance]", dbaInstanceParamater.SqlFullName);
            Assert.IsFalse(dbaInstanceParamater.IsConnectionString);
        }

        /// <summary>
        /// Tests that FileNameFriendly returns valid filenames for various connection types
        /// </summary>
        [TestMethod]
        public void TestFileNameFriendly()
        {
            // Test with named pipes using dot notation
            var npDot = new DbaInstanceParameter(".");
            Assert.AreEqual("NP_.", npDot.FileNameFriendly);
            Assert.IsFalse(npDot.FileNameFriendly.Contains(":"));

            // Test with named pipes using dot notation with instance
            var npDotInstance = new DbaInstanceParameter(@".\SQLSERVER");
            Assert.AreEqual("NP_._SQLSERVER", npDotInstance.FileNameFriendly);
            Assert.IsFalse(npDotInstance.FileNameFriendly.Contains(":"));
            Assert.IsFalse(npDotInstance.FileNameFriendly.Contains("\\"));

            // Test with TCP protocol
            var tcpInstance = new DbaInstanceParameter("TCP:server\\instance");
            Assert.AreEqual("TCP_server_instance", tcpInstance.FileNameFriendly);
            Assert.IsFalse(tcpInstance.FileNameFriendly.Contains(":"));
            Assert.IsFalse(tcpInstance.FileNameFriendly.Contains("\\"));

            // Test with port number
            var withPort = new DbaInstanceParameter("server,1433");
            Assert.AreEqual("server_1433", withPort.FileNameFriendly);
            Assert.IsFalse(withPort.FileNameFriendly.Contains(","));

            // Test with regular instance name (no protocol)
            var regular = new DbaInstanceParameter("server\\instance");
            Assert.AreEqual("server_instance", regular.FileNameFriendly);
            Assert.IsFalse(regular.FileNameFriendly.Contains("\\"));

            // Test that all results contain no invalid filename characters
            var invalidChars = System.IO.Path.GetInvalidFileNameChars();
            Assert.IsFalse(npDot.FileNameFriendly.IndexOfAny(invalidChars) >= 0);
            Assert.IsFalse(npDotInstance.FileNameFriendly.IndexOfAny(invalidChars) >= 0);
            Assert.IsFalse(tcpInstance.FileNameFriendly.IndexOfAny(invalidChars) >= 0);
            Assert.IsFalse(withPort.FileNameFriendly.IndexOfAny(invalidChars) >= 0);
            Assert.IsFalse(regular.FileNameFriendly.IndexOfAny(invalidChars) >= 0);
        }

        /// <summary>
        /// Tests that FileNameFriendly handles edge cases including IPv6 addresses
        /// </summary>
        [TestMethod]
        public void TestFileNameFriendlyEdgeCases()
        {
            // Test with IPv6 address (contains colons and brackets)
            var ipv6 = new DbaInstanceParameter("::1");
            var invalidChars = System.IO.Path.GetInvalidFileNameChars();
            Assert.IsFalse(ipv6.FileNameFriendly.IndexOfAny(invalidChars) >= 0,
                "IPv6 address FileNameFriendly should not contain invalid filename characters");

            // Test with IPv6 address and port
            var ipv6Port = new DbaInstanceParameter("[::1]:1433");
            Assert.IsFalse(ipv6Port.FileNameFriendly.IndexOfAny(invalidChars) >= 0,
                "IPv6 with port FileNameFriendly should not contain invalid filename characters");

            // Test with regular IPv6 address
            var ipv6Full = new DbaInstanceParameter("2001:0db8:85a3:0000:0000:8a2e:0370:7334");
            Assert.IsFalse(ipv6Full.FileNameFriendly.IndexOfAny(invalidChars) >= 0,
                "Full IPv6 address FileNameFriendly should not contain invalid filename characters");

            // Test with IPv4 address and port (contains colon)
            var ipv4Port = new DbaInstanceParameter("192.168.1.1:1433");
            Assert.IsFalse(ipv4Port.FileNameFriendly.IndexOfAny(invalidChars) >= 0,
                "IPv4 with port FileNameFriendly should not contain invalid filename characters");

            // Test that the results are not empty
            Assert.IsFalse(string.IsNullOrWhiteSpace(ipv6.FileNameFriendly));
            Assert.IsFalse(string.IsNullOrWhiteSpace(ipv6Port.FileNameFriendly));
            Assert.IsFalse(string.IsNullOrWhiteSpace(ipv6Full.FileNameFriendly));
            Assert.IsFalse(string.IsNullOrWhiteSpace(ipv4Port.FileNameFriendly));
        }
    }
}
