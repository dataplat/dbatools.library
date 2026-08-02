using System;
using System.Collections.Generic;
using Dataplat.Dbatools.Message;
#if NET8_0_OR_GREATER
using Microsoft.Data.SqlClient;
#endif
using Microsoft.SqlServer.Management.Smo;

namespace Dataplat.Dbatools.Connection
{
    public static partial class ConnectionService
    {
        /// <summary>
        /// private/functions/Add-ConnectionHashValue.ps1 parity over
        /// ConnectionHost.ActiveConnections: non-pooled connections append to the entry's
        /// list, pooled connections replace it. The dictionary key is GetRegistryKey's
        /// result, not the raw connection string, so explicit-Windows-credential
        /// connections do not collide with each other.
        /// </summary>
        /// <param name="key">The connection string key</param>
        /// <param name="value">The Server or SqlConnection to register</param>
        /// <param name="messageCallback">Optional verbatim-message sink</param>
        public static void RegisterConnection(string key, object value, Action<MessageLevel, string> messageCallback)
        {
            if (messageCallback != null)
                messageCallback(MessageLevel.Debug, "Adding to connection hash");
            if (String.IsNullOrEmpty(key) || value == null)
                return;

            string registryKey = GetRegistryKey(key, value);

            // PS: if ($Value.ConnectionContext.NonPooledConnection -or $Value.NonPooledConnection)
            // A Server exposes it through ConnectionContext; a bare SqlConnection has neither,
            // which lands in the pooled/replace branch exactly like the PS member miss did.
            bool nonPooled = false;
            Server serverValue = value as Server;
            if (serverValue != null)
            {
                try { nonPooled = serverValue.ConnectionContext.NonPooledConnection; }
                catch { /* unreadable on some connection shapes; treat as pooled like the PS member miss */ }
            }

            lock (ConnectionHost.ActiveConnections)
            {
                if (nonPooled)
                {
                    List<object> entries;
                    if (!ConnectionHost.ActiveConnections.TryGetValue(registryKey, out entries) || entries == null)
                    {
                        entries = new List<object>();
                        ConnectionHost.ActiveConnections[registryKey] = entries;
                    }
                    entries.Add(value);
                }
                else
                {
                    List<object> single = new List<object>();
                    single.Add(value);
                    ConnectionHost.ActiveConnections[registryKey] = single;
                }
            }
        }

        /// <summary>
        /// Builds the ConnectionHost.ActiveConnections key for a connection: the raw
        /// connection string, unless the connection authenticates through a
        /// NetworkCredentialSspiContextProvider, in which case the Windows principal is
        /// appended. An explicit Windows credential lives out-of-band on
        /// SqlConnection.SspiContextProvider rather than in the connection string, so two
        /// different identities against the same server/database would otherwise collide on
        /// the same dictionary key and silently clobber each other's registration.
        /// Get-DbaConnectedInstance and Disconnect-DbaInstance call this to look up and
        /// remove entries with the same key a registration used.
        /// </summary>
        /// <param name="connectionString">The raw connection string</param>
        /// <param name="value">The Server or SqlConnection being registered or looked up</param>
        public static string GetRegistryKey(string connectionString, object value)
        {
#if NET8_0_OR_GREATER
            NetworkCredentialSspiContextProvider provider = GetSspiProvider(value);
            if (provider != null)
                return connectionString + "|sspi:" + provider.Principal;
#endif
            return connectionString;
        }

#if NET8_0_OR_GREATER
        private static NetworkCredentialSspiContextProvider GetSspiProvider(object value)
        {
            SqlConnection sqlConnection = value as SqlConnection;
            if (sqlConnection == null)
            {
                Server server = value as Server;
                if (server == null)
                    return null;
                try { sqlConnection = server.ConnectionContext.SqlConnectionObject; }
                catch { return null; }
            }
            return sqlConnection == null ? null : sqlConnection.SspiContextProvider as NetworkCredentialSspiContextProvider;
        }
#endif
    }
}
