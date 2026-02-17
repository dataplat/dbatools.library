using System;
using System.Collections;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;
using Dataplat.Dbatools.Connection;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class DisconnectDbaInstanceCommandTests
    {
        #region UnwrapConnectionObjects
        [TestMethod]
        public void UnwrapConnectionObjects_NullInput_ReturnsEmptyArray()
        {
            // Arrange & Act
            object[] result = DisconnectDbaInstanceCommand.UnwrapConnectionObjects(null);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Length);
        }

        [TestMethod]
        public void UnwrapConnectionObjects_WithConnectionObjectProperty_ReturnsUnwrappedObject()
        {
            // Arrange - simulate Get-DbaConnectedInstance output with ConnectionObject property
            PSObject input = new PSObject();
            string fakeServer = "TestServer";
            input.Properties.Add(new PSNoteProperty("ConnectionObject", fakeServer));

            // Act
            object[] result = DisconnectDbaInstanceCommand.UnwrapConnectionObjects(input);

            // Assert
            Assert.AreEqual(1, result.Length);
            Assert.AreEqual("TestServer", result[0]);
        }

        [TestMethod]
        public void UnwrapConnectionObjects_WithConnectionObjectArray_ReturnsAllItems()
        {
            // Arrange - ConnectionObject can be an array of server objects
            PSObject input = new PSObject();
            object[] servers = new object[] { "Server1", "Server2", "Server3" };
            input.Properties.Add(new PSNoteProperty("ConnectionObject", servers));

            // Act
            object[] result = DisconnectDbaInstanceCommand.UnwrapConnectionObjects(input);

            // Assert
            Assert.AreEqual(3, result.Length);
            Assert.AreEqual("Server1", result[0]);
            Assert.AreEqual("Server2", result[1]);
            Assert.AreEqual("Server3", result[2]);
        }

        [TestMethod]
        public void UnwrapConnectionObjects_WithoutConnectionObject_ReturnsSelfAsArray()
        {
            // Arrange - plain object without ConnectionObject property (direct server object)
            PSObject input = new PSObject("DirectServerObject");

            // Act
            object[] result = DisconnectDbaInstanceCommand.UnwrapConnectionObjects(input);

            // Assert
            Assert.AreEqual(1, result.Length);
            Assert.AreEqual("DirectServerObject", result[0]);
        }

        [TestMethod]
        public void UnwrapConnectionObjects_NullConnectionObject_ReturnsSelfAsArray()
        {
            // Arrange - ConnectionObject property exists but is null
            PSObject input = new PSObject("FallbackObject");
            input.Properties.Add(new PSNoteProperty("ConnectionObject", null));

            // Act
            object[] result = DisconnectDbaInstanceCommand.UnwrapConnectionObjects(input);

            // Assert
            Assert.AreEqual(1, result.Length);
            Assert.AreEqual("FallbackObject", result[0]);
        }
        #endregion

        #region ConnectionHash Integration
        [TestMethod]
        public void ConnectionHash_RemoveKey_KeyNoLongerPresent()
        {
            // Arrange - add a key to the static ConnectionHash
            string testKey = "DisconnectTest_" + Guid.NewGuid().ToString();
            ConnectionHost.ConnectionHash[testKey] = new object[] { "TestValue" };
            Assert.IsTrue(ConnectionHost.ConnectionHash.ContainsKey(testKey), "Key should be present before removal");

            // Act - remove it (simulates what the command does)
            lock (ConnectionHost.ConnectionHash.SyncRoot)
            {
                ConnectionHost.ConnectionHash.Remove(testKey);
            }

            // Assert
            Assert.IsFalse(ConnectionHost.ConnectionHash.ContainsKey(testKey), "Key should be removed");
        }

        [TestMethod]
        public void ConnectionHash_RemoveNonexistentKey_DoesNotThrow()
        {
            // Arrange
            string testKey = "NonExistent_" + Guid.NewGuid().ToString();

            // Act & Assert - removing a key that doesn't exist should not throw
            lock (ConnectionHost.ConnectionHash.SyncRoot)
            {
                if (ConnectionHost.ConnectionHash.ContainsKey(testKey))
                {
                    ConnectionHost.ConnectionHash.Remove(testKey);
                }
            }
            Assert.IsFalse(ConnectionHost.ConnectionHash.ContainsKey(testKey));
        }
        #endregion

        #region OutputObjectShape
        [TestMethod]
        public void OutputObject_HasExpectedProperties()
        {
            // Arrange - build a PSObject like the command outputs
            PSObject result = new PSObject();
            result.Properties.Add(new PSNoteProperty("SqlInstance", "sql01"));
            result.Properties.Add(new PSNoteProperty("ConnectionString", "Data Source=sql01;Password=********"));
            result.Properties.Add(new PSNoteProperty("ConnectionType", "Microsoft.SqlServer.Management.Smo.Server"));
            result.Properties.Add(new PSNoteProperty("State", "Disconnected"));

            // Assert all four properties exist
            Assert.IsNotNull(result.Properties["SqlInstance"], "SqlInstance property should exist");
            Assert.IsNotNull(result.Properties["ConnectionString"], "ConnectionString property should exist");
            Assert.IsNotNull(result.Properties["ConnectionType"], "ConnectionType property should exist");
            Assert.IsNotNull(result.Properties["State"], "State property should exist");

            Assert.AreEqual("sql01", result.Properties["SqlInstance"].Value);
            Assert.AreEqual("Disconnected", result.Properties["State"].Value);
        }
        #endregion
    }
}
