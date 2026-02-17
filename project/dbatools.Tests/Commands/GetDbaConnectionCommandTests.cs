using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class GetDbaConnectionCommandTests
    {
        // Note: limited unit test coverage - command is primarily an SMO wrapper
        // that executes a SQL query and outputs DataRows directly.

        #region ConnectionQuery
        [TestMethod]
        public void ConnectionQuery_ContainsDmExecConnections()
        {
            Assert.IsTrue(
                GetDbaConnectionCommand.ConnectionQuery.Contains("sys.dm_exec_connections"),
                "Query should reference sys.dm_exec_connections DMV");
        }

        [TestMethod]
        public void ConnectionQuery_ContainsExpectedColumns()
        {
            string query = GetDbaConnectionCommand.ConnectionQuery;

            // Verify all expected column aliases are present
            Assert.IsTrue(query.Contains("AS ComputerName"), "Missing ComputerName alias");
            Assert.IsTrue(query.Contains("AS InstanceName"), "Missing InstanceName alias");
            Assert.IsTrue(query.Contains("AS SqlInstance"), "Missing SqlInstance alias");
            Assert.IsTrue(query.Contains("AS SessionId"), "Missing SessionId alias");
            Assert.IsTrue(query.Contains("AS MostRecentSessionId"), "Missing MostRecentSessionId alias");
            Assert.IsTrue(query.Contains("AS ConnectTime"), "Missing ConnectTime alias");
            Assert.IsTrue(query.Contains("AS Transport"), "Missing Transport alias");
            Assert.IsTrue(query.Contains("AS ProtocolType"), "Missing ProtocolType alias");
            Assert.IsTrue(query.Contains("AS ProtocolVersion"), "Missing ProtocolVersion alias");
            Assert.IsTrue(query.Contains("AS EndpointId"), "Missing EndpointId alias");
            Assert.IsTrue(query.Contains("AS EncryptOption"), "Missing EncryptOption alias");
            Assert.IsTrue(query.Contains("AS AuthScheme"), "Missing AuthScheme alias");
            Assert.IsTrue(query.Contains("AS NodeAffinity"), "Missing NodeAffinity alias");
            Assert.IsTrue(query.Contains("AS Reads"), "Missing Reads alias");
            Assert.IsTrue(query.Contains("AS Writes"), "Missing Writes alias");
            Assert.IsTrue(query.Contains("AS LastRead"), "Missing LastRead alias");
            Assert.IsTrue(query.Contains("AS LastWrite"), "Missing LastWrite alias");
            Assert.IsTrue(query.Contains("AS PacketSize"), "Missing PacketSize alias");
            Assert.IsTrue(query.Contains("AS ClientNetworkAddress"), "Missing ClientNetworkAddress alias");
            Assert.IsTrue(query.Contains("AS ClientTcpPort"), "Missing ClientTcpPort alias");
            Assert.IsTrue(query.Contains("AS ServerNetworkAddress"), "Missing ServerNetworkAddress alias");
            Assert.IsTrue(query.Contains("AS ServerTcpPort"), "Missing ServerTcpPort alias");
            Assert.IsTrue(query.Contains("AS ConnectionId"), "Missing ConnectionId alias");
            Assert.IsTrue(query.Contains("AS ParentConnectionId"), "Missing ParentConnectionId alias");
            Assert.IsTrue(query.Contains("AS MostRecentSqlHandle"), "Missing MostRecentSqlHandle alias");
        }

        [TestMethod]
        public void ConnectionQuery_UsesServerPropertyForMetadata()
        {
            string query = GetDbaConnectionCommand.ConnectionQuery;

            Assert.IsTrue(
                query.Contains("SERVERPROPERTY('MachineName')"),
                "Should use SERVERPROPERTY('MachineName') for ComputerName");
            Assert.IsTrue(
                query.Contains("SERVERPROPERTY('InstanceName')"),
                "Should use SERVERPROPERTY('InstanceName') for InstanceName");
            Assert.IsTrue(
                query.Contains("SERVERPROPERTY('ServerName')"),
                "Should use SERVERPROPERTY('ServerName') for SqlInstance");
        }

        [TestMethod]
        public void ConnectionQuery_DefaultsInstanceNameToMSSQLSERVER()
        {
            Assert.IsTrue(
                GetDbaConnectionCommand.ConnectionQuery.Contains("ISNULL(SERVERPROPERTY('InstanceName'), 'MSSQLSERVER')"),
                "Should default InstanceName to MSSQLSERVER when null");
        }

        [TestMethod]
        public void ConnectionQuery_IsNotNullOrEmpty()
        {
            Assert.IsFalse(
                String.IsNullOrWhiteSpace(GetDbaConnectionCommand.ConnectionQuery),
                "ConnectionQuery should not be null or empty");
        }
        #endregion
    }
}
