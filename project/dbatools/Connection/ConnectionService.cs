using System;
using System.Collections.Generic;
using System.Management.Automation;
using System.Text.RegularExpressions;
using Dataplat.Dbatools.Configuration;
using Dataplat.Dbatools.Parameter;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;

namespace Dataplat.Dbatools.Connection
{
    /// <summary>
    /// The C# resolution of Connect-DbaInstance for ported cmdlets, written fresh from
    /// public/Connect-DbaInstance.ps1 (migration/specs/architecture.md section 4). This is
    /// the Phase 0 skeleton (P0-010a): live SMO Server passthrough, SqlConnection wrapping,
    /// connection strings, and the string path with the integrated/SQL-login/Windows-credential
    /// auth rows, config-driven defaults, the verify query, the TrustServerCertificate retry,
    /// MinimumVersion/AzureUnsupported rejection and the SMO cache. Azure Entra flows, DAC and
    /// AccessToken land with P0-010b.
    /// </summary>
    public static class ConnectionService
    {
        /// <summary>The Azure SQL Database domain suffix used for Azure detection.</summary>
        public static string AzureDomain = "database.windows.net";

        /// <summary>
        /// Resolves a connection request to a connected SMO Server, per the input resolution
        /// and auth matrices. Throws on failure; DbaInstanceCmdlet.ConnectInstance converts
        /// failures into the canonical Stop-Function shape.
        /// </summary>
        /// <param name="request">The connection request</param>
        /// <returns>A connected, decorated SMO Server</returns>
        public static Server GetServer(SmoConnectionRequest request)
        {
            if (request == null)
                throw new ArgumentNullException("request");
            if (request.Instance == null)
                throw new ArgumentException("The request carries no target instance", "request");

            DbaInstanceParameter instance = request.Instance;
            object inputObject = UnwrapInput(instance.InputObject);

            // Row 1: a live SMO Server binds through unchanged - no reconnect, no re-auth.
            Server liveServer = inputObject as Server;
            if (liveServer != null)
            {
                Server contextResult = ResolveLiveServer(liveServer, request);
                if (contextResult != null)
                    return contextResult;
            }

            // Row 4: a raw SqlConnection is wrapped in place.
            SqlConnection sqlConnection = inputObject as SqlConnection;
            if (sqlConnection != null)
            {
                Server wrapped = new Server(new ServerConnection(sqlConnection));
                VerifyAndDecorate(wrapped, request, true);
                return wrapped;
            }

            // Row 6: connection strings flow through ServerConnection.ConnectionString.
            if (instance.IsConnectionString)
            {
                ServerConnection stringConnection = new ServerConnection();
                stringConnection.ConnectionString = instance.InputObject.ToString();
                Server fromConnectionString = new Server(stringConnection);
                VerifyAndDecorate(fromConnectionString, request, true);
                return fromConnectionString;
            }

            // Row 7: the string path with the auth matrix and the SMO cache.
            string cacheKey = BuildCacheKey(instance, request);
            if (!request.NonPooledConnection && ConnectionHost.SmoServerCache.ContainsKey(cacheKey))
            {
                Server cached = ConnectionHost.SmoServerCache[cacheKey];
                ApplyGateChecks(cached, request, false);
                return cached;
            }

            Server server = BuildServer(instance, request);
            VerifyAndDecorate(server, request, true);

            if (!request.NonPooledConnection)
                ConnectionHost.SmoServerCache[cacheKey] = server;

            return server;
        }

        /// <summary>
        /// Builds the cache key per the frozen shape:
        /// lower(FullSmoName) + "|" + database + "|" + authority + "|" + applicationIntent.
        /// </summary>
        /// <param name="instance">The target instance</param>
        /// <param name="request">The connection request</param>
        /// <returns>The cache key</returns>
        public static string BuildCacheKey(DbaInstanceParameter instance, SmoConnectionRequest request)
        {
            string database = request.Database;
            if (String.IsNullOrEmpty(database))
                database = "";

            string authority = "integrated";
            if (request.SqlCredential != null)
            {
                string userName = NormalizeUserName(request.SqlCredential.UserName);
                if (userName.Contains("\\") || userName.Contains("@"))
                    authority = String.Format("ad|{0}", userName);
                else
                    authority = String.Format("sql|{0}", userName);
            }

            string intent = request.ApplicationIntent;
            if (String.IsNullOrEmpty(intent))
                intent = "";

            return String.Format("{0}|{1}|{2}|{3}", instance.FullSmoName.ToLowerInvariant(), database.ToLowerInvariant(), authority.ToLowerInvariant(), intent.ToLowerInvariant());
        }

        private static Server ResolveLiveServer(Server liveServer, SmoConnectionRequest request)
        {
            // Context-changing parameters (differing Database) would require a
            // ConnectionContext.Copy per matrix row 2; none of the Phase 0 consumers pass
            // one, so the live object flows through with the gate checks applied.
            ApplyGateChecks(liveServer, request, false);
            return liveServer;
        }

        private static Server BuildServer(DbaInstanceParameter instance, SmoConnectionRequest request)
        {
            ServerConnection connection = new ServerConnection();
            connection.ServerInstance = instance.FullSmoName;

            int connectTimeout = GetConfigInt("sql.connection.timeout", 15);
            connection.ConnectTimeout = connectTimeout;

            string clientName = GetConfigString("sql.connection.clientname", null);
            if (!String.IsNullOrEmpty(clientName))
                connection.ApplicationName = clientName;

            string database = request.Database;
            if (String.IsNullOrEmpty(database))
                database = GetConfigString("sql.connection.database", null);
            if (!String.IsNullOrEmpty(database))
                connection.DatabaseName = database;

            // Encryption defaults follow the config system exactly (BP-103); the PS source
            // skips EncryptConnection for (localdb) instances verbatim.
            bool isLocalDb = instance.FullSmoName.IndexOf("(localdb)", StringComparison.OrdinalIgnoreCase) >= 0;
            object encryptRaw = GetConfigRaw("sql.connection.encrypt");
            if (encryptRaw != null && !isLocalDb)
                connection.EncryptConnection = ToBool(encryptRaw);
            object trustRaw = GetConfigRaw("sql.connection.trustcert");
            if (trustRaw != null)
                connection.TrustServerCertificate = ToBool(trustRaw);

            int statementTimeout = GetConfigInt("sql.execution.timeout", -1);
            if (statementTimeout >= 0)
                connection.StatementTimeout = statementTimeout;

            if (request.SqlCredential == null)
            {
                connection.LoginSecure = true;
            }
            else
            {
                string userName = NormalizeUserName(request.SqlCredential.UserName);
                if (userName.Contains("\\") || userName.Contains("@"))
                {
                    if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                        throw new InvalidOperationException("Cannot use Windows credentials to connect when host is Linux or OS X. Use kinit instead. See https://github.com/dataplat/dbatools/issues/7602 for more info.");
                    connection.LoginSecure = true;
                    connection.ConnectAsUser = true;
                    connection.ConnectAsUserName = userName;
                    connection.ConnectAsUserPassword = request.SqlCredential.GetNetworkCredential().Password;
                }
                else
                {
                    connection.LoginSecure = false;
                    connection.Login = userName;
                    connection.SecurePassword = request.SqlCredential.Password;
                }
            }

            return new Server(connection);
        }

        private static void VerifyAndDecorate(Server server, SmoConnectionRequest request, bool isNewConnection)
        {
            try
            {
                // Verify with a real query - never ConnectionContext.Connect() (it forces a
                // non-pooled connection) and never trust IsOpen.
                server.ConnectionContext.ExecuteWithResults("SELECT 'dbatools is opening a new connection'");
            }
            catch (Exception initialFailure)
            {
                // AllowTrustServerCertificate single retry on certificate trust failures.
                bool allowTrustRetry = ToBool(GetConfigRaw("sql.connection.allowtrustcert"));
                bool alreadyTrusting = false;
                try { alreadyTrusting = server.ConnectionContext.TrustServerCertificate; }
                catch { /* older connection shapes may not expose the setting */ }
                string failureText = CollectMessages(initialFailure);
                if (allowTrustRetry && isNewConnection && !alreadyTrusting && Regex.IsMatch(failureText, "certificate|SSL|TLS|trust", RegexOptions.IgnoreCase))
                {
                    server.ConnectionContext.TrustServerCertificate = true;
                    server.ConnectionContext.ExecuteWithResults("SELECT 'dbatools is opening a new connection'");
                }
                else
                {
                    throw;
                }
            }

            ApplyGateChecks(server, request, isNewConnection);
            Decorate(server, request.Instance);
            RegisterActiveConnection(server);
        }

        private static void ApplyGateChecks(Server server, SmoConnectionRequest request, bool isNewConnection)
        {
            bool isAzure = IsAzureTarget(request.Instance);

            if (request.AzureUnsupported && (isAzure || server.DatabaseEngineType == DatabaseEngineType.SqlAzureDatabase))
            {
                if (isNewConnection)
                    TryDisconnect(server);
                throw new InvalidOperationException("Azure SQL Database not supported");
            }

            if (request.MinimumVersion > 0 && server.VersionMajor < request.MinimumVersion)
            {
                if (isNewConnection)
                    TryDisconnect(server);
                throw new InvalidOperationException(String.Format("SQL Server version {0} required - {1} not supported.", request.MinimumVersion, server));
            }
        }

        private static void Decorate(Server server, DbaInstanceParameter instance)
        {
            PSObject wrapped = PSObject.AsPSObject(server);
            bool isAzure = IsAzureTarget(instance);

            string computerName;
            if (isAzure)
                computerName = instance.ComputerName;
            else
            {
                string netName = null;
                try { netName = server.NetName; }
                catch { /* NetName is not available on all targets */ }
                if (!String.IsNullOrEmpty(netName))
                    computerName = netName;
                else
                    computerName = instance.ComputerName;
            }

            AddNoteProperty(wrapped, "ComputerName", computerName);
            AddNoteProperty(wrapped, "IsAzure", isAzure);
            AddNoteProperty(wrapped, "DbaInstanceName", instance.InstanceName);
            AddNoteProperty(wrapped, "SqlInstance", SmoServerExtensions.GetDomainInstanceName(server));
            AddNoteProperty(wrapped, "NetPort", instance.Port);
            string trueLogin = null;
            try { trueLogin = server.ConnectionContext.TrueLogin; }
            catch { /* TrueLogin needs an open connection; decoration stays best-effort */ }
            AddNoteProperty(wrapped, "ConnectedAs", trueLogin);
        }

        private static void RegisterActiveConnection(Server server)
        {
            string connectionString = null;
            try { connectionString = server.ConnectionContext.ConnectionString; }
            catch { /* no connection string means nothing to register */ }
            if (String.IsNullOrEmpty(connectionString))
                return;
            lock (ConnectionHost.ActiveConnections)
            {
                if (!ConnectionHost.ActiveConnections.ContainsKey(connectionString))
                    ConnectionHost.ActiveConnections[connectionString] = new List<object>();
                if (!ConnectionHost.ActiveConnections[connectionString].Contains(server))
                    ConnectionHost.ActiveConnections[connectionString].Add(server);
            }
        }

        private static bool IsAzureTarget(DbaInstanceParameter instance)
        {
            if (instance == null || String.IsNullOrEmpty(instance.ComputerName))
                return false;
            return Regex.IsMatch(instance.ComputerName, Regex.Escape(AzureDomain), RegexOptions.IgnoreCase);
        }

        private static string NormalizeUserName(string userName)
        {
            if (String.IsNullOrEmpty(userName))
                return userName;
            string normalized = userName;
            // A leading backslash is stripped; .\user stays local per the PS normalization.
            if (normalized.StartsWith("\\", StringComparison.Ordinal))
                normalized = normalized.Substring(1);
            return normalized;
        }

        private static void AddNoteProperty(PSObject wrapped, string name, object value)
        {
            if (wrapped.Properties[name] != null)
                wrapped.Properties.Remove(name);
            wrapped.Properties.Add(new PSNoteProperty(name, value));
        }

        private static void TryDisconnect(Server server)
        {
            try { server.ConnectionContext.Disconnect(); }
            catch { /* teardown of a failed gate check is best-effort */ }
        }

        private static string CollectMessages(Exception exception)
        {
            List<string> messages = new List<string>();
            Exception current = exception;
            while (current != null && messages.Count < 10)
            {
                messages.Add(current.Message);
                current = current.InnerException;
            }
            return String.Join(" | ", messages);
        }

        private static object GetConfigRaw(string key)
        {
            Config config;
            if (ConfigurationHost.Configurations.TryGetValue(key, out config) && config != null)
                return config.Value;
            return null;
        }

        private static string GetConfigString(string key, string fallback)
        {
            object raw = GetConfigRaw(key);
            if (raw == null)
                return fallback;
            return raw.ToString();
        }

        private static int GetConfigInt(string key, int fallback)
        {
            object raw = GetConfigRaw(key);
            if (raw == null)
                return fallback;
            try { return Convert.ToInt32(raw); }
            catch { return fallback; }
        }

        private static bool ToBool(object raw)
        {
            if (raw == null)
                return false;
            if (raw is bool)
                return (bool)raw;
            try { return Convert.ToBoolean(raw); }
            catch { return false; }
        }

        private static object UnwrapInput(object inputObject)
        {
            PSObject wrapped = inputObject as PSObject;
            if (wrapped != null)
                return wrapped.BaseObject;
            return inputObject;
        }
    }
}
