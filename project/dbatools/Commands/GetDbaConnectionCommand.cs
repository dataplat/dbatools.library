using System;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Returns connection information from sys.dm_exec_connections for each SQL Server instance.
    /// </summary>
    [Cmdlet("Get", "DbaConnection")]
    public class GetDbaConnectionCommand : DbaInstanceCmdlet
    {
        // internal for unit test access
        internal static readonly string ConnectionQuery =
            "SELECT SERVERPROPERTY('MachineName') AS ComputerName, " +
            "ISNULL(SERVERPROPERTY('InstanceName'), 'MSSQLSERVER') AS InstanceName, " +
            "SERVERPROPERTY('ServerName') AS SqlInstance, " +
            "session_id AS SessionId, most_recent_session_id AS MostRecentSessionId, connect_time AS ConnectTime, " +
            "net_transport AS Transport, protocol_type AS ProtocolType, protocol_version AS ProtocolVersion, " +
            "endpoint_id AS EndpointId, encrypt_option AS EncryptOption, auth_scheme AS AuthScheme, node_affinity AS NodeAffinity, " +
            "num_reads AS Reads, num_writes AS Writes, last_read AS LastRead, last_write AS LastWrite, " +
            "net_packet_size AS PacketSize, client_net_address AS ClientNetworkAddress, client_tcp_port AS ClientTcpPort, " +
            "local_net_address AS ServerNetworkAddress, local_tcp_port AS ServerTcpPort, connection_id AS ConnectionId, " +
            "parent_connection_id AS ParentConnectionId, most_recent_sql_handle AS MostRecentSqlHandle " +
            "FROM sys.dm_exec_connections";

        /// <summary>
        /// Processes each SQL Server instance, querying dm_exec_connections.
        /// </summary>
        protected override void ProcessRecord()
        {
            foreach (DbaInstanceParameter instance in SqlInstance)
            {
                object server = null;
                try
                {
                    server = ConnectInstance(instance);
                }
                catch (Exception ex)
                {
                    StopFunction(
                        "Failure",
                        errorRecord: new ErrorRecord(ex, "GetDbaConnection_ConnectionError", ErrorCategory.ConnectionError, instance),
                        target: instance,
                        isContinue: true,
                        category: ErrorCategory.ConnectionError);
                    TestFunctionInterrupt();
                    continue;
                }

                if (server == null)
                {
                    StopFunction(
                        "Failure",
                        target: instance,
                        isContinue: true,
                        category: ErrorCategory.ConnectionError);
                    TestFunctionInterrupt();
                    continue;
                }

                WriteMessageAtLevel(
                    String.Format("Getting results for the following query: {0}.", ConnectionQuery),
                    Message.MessageLevel.Debug,
                    new string[0]);

                try
                {
                    Collection<PSObject> rows = ServerQuery(server, ConnectionQuery);
                    if (rows != null)
                    {
                        foreach (PSObject row in rows)
                        {
                            WriteObject(row);
                        }
                    }
                }
                catch (Exception ex)
                {
                    StopFunction(
                        "Failure",
                        errorRecord: new ErrorRecord(ex, "GetDbaConnection_QueryError", ErrorCategory.InvalidOperation, server),
                        target: server,
                        isContinue: true);
                    TestFunctionInterrupt();
                    continue;
                }
            }
        }

        /// <summary>
        /// Connects to a SQL Server instance by invoking Connect-DbaInstance.
        /// Requires minimum SQL Server 2005 (version 9).
        /// </summary>
        private object ConnectInstance(DbaInstanceParameter instance)
        {
            string script;
            object[] args;
            if (SqlCredential != null)
            {
                script = "param($i, $c) Connect-DbaInstance -SqlInstance $i -SqlCredential $c -MinimumVersion 9";
                args = new object[] { instance, SqlCredential };
            }
            else
            {
                script = "param($i) Connect-DbaInstance -SqlInstance $i -MinimumVersion 9";
                args = new object[] { instance };
            }

            Collection<PSObject> results = InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, args);
            if (results != null && results.Count > 0)
                return results[0].BaseObject;
            return null;
        }

        /// <summary>
        /// Executes a SQL query via $server.Query() to match dbatools PS1 behavior.
        /// Returns PSObjects with named properties (same as PS1 Query() output).
        /// </summary>
        private Collection<PSObject> ServerQuery(object server, string sql)
        {
            string script = "param($s, $q) $s.Query($q)";
            return InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { server, sql });
        }
    }
}
