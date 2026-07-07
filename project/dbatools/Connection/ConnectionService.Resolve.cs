using System;
using System.Management.Automation;
using System.Security;
using System.Text.RegularExpressions;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;

namespace Dataplat.Dbatools.Connection
{
    public static partial class ConnectionService
    {
        // Which shape the input resolution matrix classified the instance as; mirrors the
        // $inputObjectType strings of the PS source.
        internal enum ResolveInputType
        {
            Server = 0,
            SqlConnection = 1,
            RegisteredServer = 2,
            ConnectionString = 3,
            String = 4
        }

        // Per-instance working state, so the build/verify phases stay readable without
        // threading a dozen locals through every method.
        private class ResolveState
        {
            public SmoConnectionRequest Request;
            public DbaInstanceParameter Instance;
            public ResolveInputType InputType;
            public bool IsNewConnection;
            public object InputObject;
            public string ServerName;
            public string ConnectionString;
            public bool IsAzure;
            public Server Server;
        }

        /// <summary>
        /// Resolves ONE instance through the full Connect-DbaInstance flow: input analysis,
        /// server construction per the input resolution and auth matrices, the verify query
        /// with its sanctioned retries, the AzureUnsupported/MinimumVersion gates, the
        /// SqlConnectionOnly early return, and the PS-exact decoration. Failures throw
        /// ConnectionResolutionException so the cmdlet can replay the exact Stop-Function
        /// call-site semantics; the caller owns emission and the post-emission registrations
        /// (FinalizeConnection or the granular equivalents).
        /// </summary>
        /// <param name="request">The connection request; Instance must be set</param>
        /// <returns>The resolution outcome</returns>
        public static ConnectionResolution ResolveInstance(SmoConnectionRequest request)
        {
            if (request == null)
                throw new ArgumentNullException("request");
            if (request.Instance == null)
                throw new ArgumentException("The request carries no target instance", "request");

            ResolveState state = new ResolveState();
            state.Request = request;
            state.Instance = request.Instance;

            DbaInstanceParameter instance = state.Instance;
            Msg(request, MessageLevel.Verbose, String.Format("Starting loop for '{0}': ComputerName = '{1}', InstanceName = '{2}', IsLocalHost = '{3}', Type = '{4}'", instance, instance.ComputerName, instance.InstanceName, instance.IsLocalHost, instance.Type));

            Msg(request, MessageLevel.Debug, "Immediately checking for Azure");
            // PS begin block: if ($AzureDomain) { $AzureDomain = [regex]::escape($AzureDomain) }
            string azureDomain = request.AzureDomain;
            string azurePattern = String.IsNullOrEmpty(azureDomain) ? "" : Regex.Escape(azureDomain);
            state.IsAzure = IsAzureInstance(instance, azurePattern);
            if (state.IsAzure)
                Msg(request, MessageLevel.Verbose, "Azure detected");

            /*
            Best practice:
            * Create a smo server object by submitting the name of the instance as a string to SqlInstance and additional parameters to configure the connection
            * Reuse the smo server object in all following calls as SqlInstance
            * When reusing the smo server object, only the following additional parameters are allowed with Connect-DbaInstance:
                - Database, ApplicationIntent, NonPooledConnection, StatementTimeout (command clones ConnectionContext and returns new smo server object)
                - AzureUnsupported (command fails if target is Azure)
                - MinimumVersion (command fails if target version is too old)
                - SqlConnectionOnly (command returns only the ConnectionContext.SqlConnectionObject)
            Commands that use these parameters:
            * ApplicationIntent
                - Invoke-DbaQuery
            * NonPooledConnection
                - Install-DbaFirstResponderKit
            * StatementTimeout (sometimes not as a parameter, they should changed to do so)
                - Backup-DbaDatabase
                - Restore-DbaDatabase
                - Get-DbaTopResourceUsage
                - Import-DbaCsv
                - Invoke-DbaDbLogShipping
                - Invoke-DbaDbShrink
                - Invoke-DbaDbUpgrade
                - Set-DbaDbCompression
                - Test-DbaDbCompression
                - Start-DbccCheck
            * AzureUnsupported
                - Backup-DbaDatabase
                - Copy-DbaLogin
                - Get-DbaLogin
                - Set-DbaLogin
                - Get-DbaDefaultPath
                - Get-DbaUserPermission
                - Get-DbaXESession
                - New-DbaCustomError
                - Remove-DbaCustomError
            Additional possibilities as input to SqlInstance:
            * A smo connection object [Microsoft.Data.SqlClient.SqlConnection] (InputObject is used to build smo server object)
            * A smo registered server object [Microsoft.SqlServer.Management.RegisteredServers.RegisteredServer] (FullSmoName und InputObject.ConnectionString are used to build smo server object)
            * A connections string [String] (FullSmoName und InputObject are used to build smo server object)
            Limitations of these additional possibilities:
            * All additional parameters are ignored, a warning is displayed if they are used
            * Currently, connection pooling does not work with connections that are build from connection strings
            * All parameters that configure the connection and where they can be set (here just for documentation and future development):
                - AppendConnectionString      SqlConnectionInfo.AdditionalParameters
                - ApplicationIntent           SqlConnectionInfo.ApplicationIntent          SqlConnectionStringBuilder['ApplicationIntent']
                - AuthenticationType          SqlConnectionInfo.Authentication             SqlConnectionStringBuilder['Authentication']
                - BatchSeparator                                                                                                                     ConnectionContext.BatchSeparator
                - ClientName                  SqlConnectionInfo.ApplicationName            SqlConnectionStringBuilder['Application Name']
                - ConnectTimeout              SqlConnectionInfo.ConnectionTimeout          SqlConnectionStringBuilder['Connect Timeout']
                - Database                    SqlConnectionInfo.DatabaseName               SqlConnectionStringBuilder['Initial Catalog']
                - EncryptConnection           SqlConnectionInfo.EncryptConnection          SqlConnectionStringBuilder['Encrypt']
                - FailoverPartner             SqlConnectionInfo.AdditionalParameters       SqlConnectionStringBuilder['Failover Partner']
                - LockTimeout                                                                                                                        ConnectionContext.LockTimeout
                - MaxPoolSize                 SqlConnectionInfo.MaxPoolSize                SqlConnectionStringBuilder['Max Pool Size']
                - MinPoolSize                 SqlConnectionInfo.MinPoolSize                SqlConnectionStringBuilder['Min Pool Size']
                - MultipleActiveResultSets                                                 SqlConnectionStringBuilder['MultipleActiveResultSets']    ConnectionContext.MultipleActiveResultSets
                - MultiSubnetFailover         SqlConnectionInfo.AdditionalParameters       SqlConnectionStringBuilder['MultiSubnetFailover']
                - NetworkProtocol             SqlConnectionInfo.ConnectionProtocol
                - NonPooledConnection         SqlConnectionInfo.Pooled                     SqlConnectionStringBuilder['Pooling']
                - PacketSize                  SqlConnectionInfo.PacketSize                 SqlConnectionStringBuilder['Packet Size']
                - PooledConnectionLifetime    SqlConnectionInfo.PoolConnectionLifeTime     SqlConnectionStringBuilder['Load Balance Timeout']
                - SqlInstance                 SqlConnectionInfo.ServerName                 SqlConnectionStringBuilder['Data Source']
                - SqlCredential               SqlConnectionInfo.SecurePassword             SqlConnectionStringBuilder['Password']
                                            SqlConnectionInfo.UserName                   SqlConnectionStringBuilder['User ID']
                                            SqlConnectionInfo.UseIntegratedSecurity      SqlConnectionStringBuilder['Integrated Security']
                - SqlExecutionModes                                                                                                                  ConnectionContext.SqlExecutionModes
                - StatementTimeout            (SqlConnectionInfo.QueryTimeout?)                                                                      ConnectionContext.StatementTimeout
                - TrustServerCertificate      SqlConnectionInfo.TrustServerCertificate     SqlConnectionStringBuilder['TrustServerCertificate']
                - WorkstationId               SqlConnectionInfo.WorkstationId              SqlConnectionStringBuilder['Workstation Id']

            Some additional tests:
            * Is $AzureUnsupported set? Test for Azure.
            * Is $MinimumVersion set? Test for that.
            * Is $SqlConnectionOnly set? Then return $server.ConnectionContext.SqlConnectionObject.
            * Does the server object have the additional properties? Add them when necessary.

            Some general decisions:
            * We try to treat connections to Azure as normal connections.
            * Not every edge case will be covered at the beginning.
            * We copy as less code from the existing code paths as possible.
            */

            AnalyzeInput(state);
            WarnIgnoredParameters(state);
            ApplyDedicatedAdminRewrite(state);
            BuildServerObject(state);
            return VerifyAndComplete(state);
        }

        // Analyse input object and extract necessary parts
        private static void AnalyzeInput(ResolveState state)
        {
            SmoConnectionRequest request = state.Request;
            DbaInstanceParameter instance = state.Instance;

            if (instance.Type == DbaInstanceInputType.Server)
            {
                Msg(request, MessageLevel.Verbose, "Server object passed in, will do some checks and then return the original object");
                state.InputType = ResolveInputType.Server;
                state.IsNewConnection = false;
                state.InputObject = UnwrapInput(instance.InputObject);
            }
            else if (instance.Type == DbaInstanceInputType.SqlConnection)
            {
                Msg(request, MessageLevel.Verbose, "SqlConnection object passed in, will build server object from instance.InputObject, do some checks and then return the server object");
                state.InputType = ResolveInputType.SqlConnection;
                state.IsNewConnection = false;
                state.InputObject = UnwrapInput(instance.InputObject);
            }
            else if (instance.Type == DbaInstanceInputType.RegisteredServer)
            {
                Msg(request, MessageLevel.Verbose, "RegisteredServer object passed in, will build empty server object, set connection string from instance.InputObject.ConnectionString, do some checks and then return the server object");
                state.InputType = ResolveInputType.RegisteredServer;
                state.IsNewConnection = true;
                state.InputObject = UnwrapInput(instance.InputObject);
                state.ServerName = instance.FullSmoName;
                object registeredConnectionString = SmoServerExtensions.GetPSProperty(instance.InputObject, "ConnectionString");
                state.ConnectionString = registeredConnectionString == null ? null : registeredConnectionString.ToString();
            }
            else if (instance.IsConnectionString)
            {
                Msg(request, MessageLevel.Verbose, "Connection string is passed in, will build empty server object, set connection string from instance.InputObject, do some checks and then return the server object");
                state.InputType = ResolveInputType.ConnectionString;
                state.IsNewConnection = true;
                state.ServerName = instance.FullSmoName;
                state.ConnectionString = ConvertConnectionString(instance.InputObject.ToString());
            }
            else
            {
                Msg(request, MessageLevel.Verbose, "String is passed in, will build server object from instance object and other parameters, do some checks and then return the server object");
                state.InputType = ResolveInputType.String;
                state.IsNewConnection = true;
                state.ServerName = instance.FullSmoName;
            }
        }

        // Check for ignored parameters
        // We do not check for SqlCredential as this parameter is widely used even if a server SMO is passed in and we don't want to output a message for that
        private static readonly string[] IgnoredParameters = new string[] {
            "BatchSeparator", "ClientName", "ConnectTimeout", "EncryptConnection", "LockTimeout", "MaxPoolSize", "MinPoolSize", "NetworkProtocol", "PacketSize", "PooledConnectionLifetime", "SqlExecutionModes", "TrustServerCertificate", "AllowTrustServerCertificate", "WorkstationId", "FailoverPartner", "MultipleActiveResultSets", "MultiSubnetFailover", "AppendConnectionString", "AccessToken", "AuthenticationType"
        };

        private static void WarnIgnoredParameters(ResolveState state)
        {
            SmoConnectionRequest request = state.Request;

            if (state.InputType == ResolveInputType.Server)
            {
                if (request.IsAnyBound(IgnoredParameters))
                    Msg(request, MessageLevel.Warning, "Additional parameters are passed in, but they will be ignored");
            }
            else if (state.InputType == ResolveInputType.RegisteredServer || state.InputType == ResolveInputType.ConnectionString)
            {
                // Parameter TrustServerCertificate changes the connection string be allow connections to instances with the default self-signed certificate
                if (request.IsBound("TrustServerCertificate"))
                    Msg(request, MessageLevel.Verbose, "Additional parameter TrustServerCertificate is passed in and will override other settings");
                else if (request.IsAnyBound(IgnoredParameters) || request.IsAnyBound("ApplicationIntent", "StatementTimeout"))
                    Msg(request, MessageLevel.Warning, "Additional parameters are passed in, but they will be ignored");
            }
            else if (state.InputType == ResolveInputType.SqlConnection)
            {
                if (request.IsAnyBound(IgnoredParameters) || request.IsAnyBound("ApplicationIntent", "StatementTimeout", "DedicatedAdminConnection"))
                    Msg(request, MessageLevel.Warning, "Additional parameters are passed in, but they will be ignored");
            }
        }

        private static void ApplyDedicatedAdminRewrite(ResolveState state)
        {
            SmoConnectionRequest request = state.Request;
            DbaInstanceParameter instance = state.Instance;

            if (request.DedicatedAdminConnection && !String.IsNullOrEmpty(state.ServerName))
            {
                Msg(request, MessageLevel.Debug, "Parameter DedicatedAdminConnection is used, so serverName will be changed and NonPooledConnection will be set.");
                if (instance.IsLocalHost)
                {
                    // Use localhost to avoid multiple IP resolution on multi-homed servers (issue #10151)
                    if (!String.Equals(instance.InstanceName, "MSSQLSERVER", StringComparison.OrdinalIgnoreCase))
                        state.ServerName = String.Format("ADMIN:localhost\\{0}", instance.InstanceName);
                    else
                        state.ServerName = "ADMIN:localhost";
                    Msg(request, MessageLevel.Debug, String.Format("IsLocalHost is true, using '{0}' for DAC to avoid multi-IP resolution.", state.ServerName));
                    // Trust the server certificate because 'localhost' may not match the certificate CN (e.g., FQDN), issue #10254
                    // NOTE: the PS source mutates the function-scope $TrustServerCertificate here, so the
                    // setting deliberately persists to the remaining instances of the same call.
                    request.TrustServerCertificate = true;
                }
                else
                {
                    state.ServerName = "ADMIN:" + state.ServerName;
                }
                // Same function-scope persistence as $NonPooledConnection = $true in the PS source.
                request.NonPooledConnection = true;
            }
        }

        // Create smo server object
        private static void BuildServerObject(ResolveState state)
        {
            switch (state.InputType)
            {
                case ResolveInputType.Server:
                    ResolveServerInput(state);
                    break;
                case ResolveInputType.SqlConnection:
                    state.Server = new Server(new ServerConnection((SqlConnection)state.InputObject));
                    break;
                case ResolveInputType.RegisteredServer:
                case ResolveInputType.ConnectionString:
                    BuildFromConnectionString(state);
                    break;
                case ResolveInputType.String:
                    BuildFromString(state);
                    break;
            }
        }

        private static void ResolveServerInput(ResolveState state)
        {
            SmoConnectionRequest request = state.Request;
            DbaInstanceParameter instance = state.Instance;
            Server inputServer = (Server)state.InputObject;

            // Test if we have to copy the connection context
            // Currently only if we have a different Database or have to switch to a NonPooledConnection or using a specific StatementTimeout or using ApplicationIntent
            // We do not test for SqlCredential as this would change the behavior compared to the legacy code path
            bool copyContext = false;
            bool createNewConnection = false;
            if (!String.IsNullOrEmpty(request.Database))
            {
                Msg(request, MessageLevel.Debug, String.Format("Database [{0}] provided.", request.Database));
                if (String.IsNullOrEmpty(inputServer.ConnectionContext.CurrentDatabase))
                {
                    Msg(request, MessageLevel.Debug, "ConnectionContext.CurrentDatabase is empty, so connection will be opened to get the value");
                    inputServer.ConnectionContext.Connect();
                    Msg(request, MessageLevel.Debug, String.Format("ConnectionContext.CurrentDatabase is now [{0}]", inputServer.ConnectionContext.CurrentDatabase));
                }
                if (!String.Equals(inputServer.ConnectionContext.CurrentDatabase, request.Database, StringComparison.OrdinalIgnoreCase))
                {
                    Msg(request, MessageLevel.Verbose, String.Format("Database [{0}] provided. Does not match ConnectionContext.CurrentDatabase [{1}], copying ConnectionContext and setting the CurrentDatabase", request.Database, inputServer.ConnectionContext.CurrentDatabase));
                    copyContext = true;
                    if (!String.IsNullOrEmpty(inputServer.ConnectionContext.ConnectAsUserName))
                    {
                        Msg(request, MessageLevel.Debug, String.Format("Using ConnectAsUserName [{0}], so changing database context is not possible without loosing this information. We will create a new connection targeting database [{1}]", inputServer.ConnectionContext.ConnectAsUserName, request.Database));
                        createNewConnection = true;
                    }
                }
            }
            if (!String.IsNullOrEmpty(request.ApplicationIntent) && !String.Equals(inputServer.ConnectionContext.ApplicationIntent, request.ApplicationIntent, StringComparison.OrdinalIgnoreCase))
            {
                Msg(request, MessageLevel.Verbose, "ApplicationIntent provided. Does not match ConnectionContext.ApplicationIntent, copying ConnectionContext and setting the ApplicationIntent");
                copyContext = true;
            }
            if (request.NonPooledConnection && !inputServer.ConnectionContext.NonPooledConnection)
            {
                Msg(request, MessageLevel.Verbose, "NonPooledConnection provided. Does not match ConnectionContext.NonPooledConnection, copying ConnectionContext and setting NonPooledConnection");
                copyContext = true;
            }
            if (request.IsBound("StatementTimeout") && inputServer.ConnectionContext.StatementTimeout != request.StatementTimeout)
            {
                Msg(request, MessageLevel.Verbose, "StatementTimeout provided. Does not match ConnectionContext.StatementTimeout, copying ConnectionContext and setting the StatementTimeout");
                copyContext = true;
            }
            if (request.DedicatedAdminConnection && !Regex.IsMatch(inputServer.ConnectionContext.ServerInstance ?? "", "^ADMIN:", RegexOptions.IgnoreCase))
            {
                Msg(request, MessageLevel.Verbose, "DedicatedAdminConnection provided. Does not match ConnectionContext.ServerInstance, copying ConnectionContext and setting the ServerInstance");
                copyContext = true;
            }

            if (createNewConnection)
            {
                state.IsNewConnection = true;
                // PS: reconstruct the Windows credential from the ConnectionContext and re-invoke
                // Connect-DbaInstance with all bound parameters plus the replaced
                // SqlInstance/SqlCredential (input resolution matrix row 3).
                SecureString secStringPassword = new SecureString();
                string connectAsUserPassword = inputServer.ConnectionContext.ConnectAsUserPassword;
                if (connectAsUserPassword != null)
                {
                    foreach (char passwordChar in connectAsUserPassword)
                        secStringPassword.AppendChar(passwordChar);
                }
                PSCredential serverCredentialFromSMO = new PSCredential(inputServer.ConnectionContext.ConnectAsUserName, secStringPassword);

                SmoConnectionRequest innerRequest = state.Request.Clone();
                innerRequest.Instance = new DbaInstanceParameter(inputServer.Name);
                innerRequest.SqlCredential = serverCredentialFromSMO;
                if (innerRequest.BoundParameters != null)
                    innerRequest.BoundParameters.Add("SqlCredential");

                ConnectionResolution inner = ResolveInstance(innerRequest);
                // The PS recursion ran the inner call's TEPP/InitFields/hash registrations
                // before returning (its emitted server was captured, not written to the
                // pipeline). The TeppGatherScriptsFast execution is cmdlet-owned and is
                // skipped here; TabExpansionHost.TeppSyncDisabled defaults to true, so the
                // default path loses nothing.
                FinalizeConnection(inner, innerRequest);

                if (inner.Server != null)
                {
                    state.Server = inner.Server;
                }
                else
                {
                    // PS parity: under -SqlConnectionOnly the recursive call emitted a bare
                    // SqlConnection, and the outer flow then died on the next
                    // ConnectionContext access ("You cannot call a method on a null-valued
                    // expression"), surfacing as the generic connect failure.
                    throw new ConnectionResolutionException(ConnectionResolutionFailure.ConnectFailure, "Failure",
                        new InvalidOperationException("You cannot call a method on a null-valued expression."));
                }
            }
            else if (copyContext)
            {
                state.IsNewConnection = true;
                ServerConnection connContext = inputServer.ConnectionContext.Copy();
                if (!String.IsNullOrEmpty(request.ApplicationIntent))
                    connContext.ApplicationIntent = request.ApplicationIntent;
                if (request.NonPooledConnection)
                    connContext.NonPooledConnection = true;
                if (request.IsBound("StatementTimeout"))
                    connContext.StatementTimeout = request.StatementTimeout;
                if (request.DedicatedAdminConnection && !Regex.IsMatch(inputServer.ConnectionContext.ServerInstance ?? "", "^ADMIN:", RegexOptions.IgnoreCase))
                {
                    if (instance.IsLocalHost)
                    {
                        // Use localhost to avoid multiple IP resolution on multi-homed servers (issue #10151)
                        if (!String.Equals(instance.InstanceName, "MSSQLSERVER", StringComparison.OrdinalIgnoreCase))
                            connContext.ServerInstance = String.Format("ADMIN:localhost\\{0}", instance.InstanceName);
                        else
                            connContext.ServerInstance = "ADMIN:localhost";
                        // Trust the server certificate because 'localhost' may not match the certificate CN (e.g., FQDN), issue #10254
                        connContext.TrustServerCertificate = true;
                    }
                    else
                    {
                        connContext.ServerInstance = "ADMIN:" + connContext.ServerInstance;
                    }
                    connContext.NonPooledConnection = true;
                }
                if (!String.IsNullOrEmpty(request.Database))
                {
                    // Save StatementTimeout because it might be reset on GetDatabaseConnection
                    int savedStatementTimeout = connContext.StatementTimeout;
                    connContext = connContext.GetDatabaseConnection(request.Database, false);
                    connContext.StatementTimeout = savedStatementTimeout;
                }
                state.Server = new Server(connContext);
                if (!String.IsNullOrEmpty(request.Database) && !String.Equals(state.Server.ConnectionContext.CurrentDatabase, request.Database, StringComparison.OrdinalIgnoreCase))
                    Msg(request, MessageLevel.Warning, String.Format("Changing connection context to database {0} was not successful. Current database is {1}. Please open an issue on https://github.com/dataplat/dbatools/issues.", request.Database, state.Server.ConnectionContext.CurrentDatabase));
            }
            else
            {
                state.Server = inputServer;
            }
        }

        private static void BuildFromConnectionString(ResolveState state)
        {
            SmoConnectionRequest request = state.Request;

            // Create the server SMO in the same way as when passing a string (see #8962 for details).
            // Best way to get connection pooling to work is to use SqlConnectionInfo -> ServerConnection -> Server
            SqlConnectionInfo sqlConnectionInfo = new SqlConnectionInfo();

            // Set properties of SqlConnectionInfo based on the used properties of the connection string.
            string connectionString = NormalizeFailoverPartnerKey(state.ConnectionString);
            connectionString = EnsureInitialCatalogForFailoverPartner(connectionString);
            SqlConnectionStringBuilder csb = new SqlConnectionStringBuilder(connectionString);
            if (csb.ShouldSerialize("Data Source"))
            {
                Msg(request, MessageLevel.Debug, String.Format("ServerName will be set to '{0}'", csb.DataSource));
                sqlConnectionInfo.ServerName = csb.DataSource;
                csb.Remove("Data Source");
            }
            if (csb.ShouldSerialize("User ID"))
            {
                Msg(request, MessageLevel.Debug, String.Format("UserName will be set to '{0}'", csb.UserID));
                sqlConnectionInfo.UserName = csb.UserID;
                csb.Remove("User ID");
            }
            if (csb.ShouldSerialize("Password"))
            {
                Msg(request, MessageLevel.Debug, "Password will be set");
                sqlConnectionInfo.Password = csb.Password;
                csb.Remove("Password");
            }
            // look for 'Initial Catalog' and 'Database' in the connection string
            string specifiedDatabase = Convert.ToString(csb["Database"]);
            if (specifiedDatabase == "")
                specifiedDatabase = Convert.ToString(csb["Initial Catalog"]);
            if (!String.IsNullOrEmpty(request.Database) && !String.Equals(request.Database, specifiedDatabase, StringComparison.OrdinalIgnoreCase))
            {
                Msg(request, MessageLevel.Debug, String.Format("Database specified in connection string '{0}' does not match Database parameter '{1}'. Database parameter will be used.", specifiedDatabase, request.Database));
                // clear both, in order to not be overridden later by setting all AddtionalParameters
                if (csb.ShouldSerialize("Database"))
                    csb.Remove("Database");
                if (csb.ShouldSerialize("Initial Catalog"))
                    csb.Remove("Initial Catalog");
                sqlConnectionInfo.DatabaseName = request.Database;
            }

            // Add all remaining parts of the connection string as additional parameters.
            sqlConnectionInfo.AdditionalParameters = csb.ConnectionString;

            // Set properties based on used parameters.
            if (request.TrustServerCertificate)
            {
                Msg(request, MessageLevel.Debug, String.Format("TrustServerCertificate will be set to '{0}'", true));
                sqlConnectionInfo.TrustServerCertificate = true;
            }

            ServerConnection serverConnection = new ServerConnection(sqlConnectionInfo);
            state.Server = new Server(serverConnection);
        }

        private static void BuildFromString(ResolveState state)
        {
            SmoConnectionRequest request = state.Request;
            DbaInstanceParameter instance = state.Instance;

            // Identify authentication method
            string authType = state.IsAzure ? "azure " : "local ";
            string username = null;
            if (request.SqlCredential != null)
            {
                username = NormalizeConnectUserName(request.SqlCredential.UserName, Environment.GetEnvironmentVariable("USERDOMAIN"), Environment.GetEnvironmentVariable("COMPUTERNAME"));
                if (username.Contains("@") || username.Contains("\\"))
                    authType += "ad";
                else
                    authType += "sql";
            }
            else if (LanguagePrimitives.IsTrue(request.AccessToken))
            {
                // PS: elseif ($AccessToken) - truthiness, so an empty-string token counts as
                // absent (cross-model review 2026-07-07 finding 3).
                authType += "token";
            }
            else
            {
                authType += "integrated";
            }
            Msg(request, MessageLevel.Verbose, String.Format("authentication method is '{0}'", authType));
            // PS: -in is case-insensitive and ValidateSet passes the user's casing through
            // (cross-model review 2026-07-07 finding 1).
            bool authenticationTypeUsesSqlCredential = String.Equals(request.AuthenticationType, "ActiveDirectoryPassword", StringComparison.OrdinalIgnoreCase) || String.Equals(request.AuthenticationType, "ActiveDirectoryServicePrincipal", StringComparison.OrdinalIgnoreCase);

            // Best way to get connection pooling to work is to use SqlConnectionInfo -> ServerConnection -> Server
            SqlConnectionInfo sqlConnectionInfo = new SqlConnectionInfo(state.ServerName);

            // But if we have an AccessToken, we need ConnectionString -> SqlConnection -> ServerConnection -> Server
            // We will get the ConnectionString from the SqlConnectionInfo, so let's move on

            // I will list all properties of SqlConnectionInfo and set them if value is provided

            //AccessToken            Property   Microsoft.SqlServer.Management.Common.IRenewableToken AccessToken {get;set;}
            // This parameter needs an IRenewableToken and we currently support only a non renewable token

            //AdditionalParameters   Property   string AdditionalParameters {get;set;}
            if (!String.IsNullOrEmpty(request.AppendConnectionString))
            {
                Msg(request, MessageLevel.Debug, String.Format("AdditionalParameters will be appended by '{0};'", request.AppendConnectionString));
                sqlConnectionInfo.AdditionalParameters = (sqlConnectionInfo.AdditionalParameters ?? "") + request.AppendConnectionString + ";";
            }
            if (!String.IsNullOrEmpty(request.FailoverPartner))
            {
                Msg(request, MessageLevel.Debug, String.Format("AdditionalParameters will be appended by 'FailoverPartner={0};'", request.FailoverPartner));
                sqlConnectionInfo.AdditionalParameters = (sqlConnectionInfo.AdditionalParameters ?? "") + "FailoverPartner=" + request.FailoverPartner + ";";
            }
            if (request.MultiSubnetFailover)
            {
                Msg(request, MessageLevel.Debug, "AdditionalParameters will be appended by 'MultiSubnetFailover=True;'");
                sqlConnectionInfo.AdditionalParameters = (sqlConnectionInfo.AdditionalParameters ?? "") + "MultiSubnetFailover=True;";
            }
            if (request.AlwaysEncrypted)
            {
                Msg(request, MessageLevel.Debug, "AdditionalParameters will be appended by 'Column Encryption Setting=enabled;'");
                sqlConnectionInfo.AdditionalParameters = (sqlConnectionInfo.AdditionalParameters ?? "") + "Column Encryption Setting=enabled;";
            }

            //ApplicationIntent      Property   string ApplicationIntent {get;set;}
            if (!String.IsNullOrEmpty(request.ApplicationIntent))
            {
                Msg(request, MessageLevel.Debug, String.Format("ApplicationIntent will be set to '{0}'", request.ApplicationIntent));
                sqlConnectionInfo.ApplicationIntent = request.ApplicationIntent;
            }

            //ApplicationName        Property   string ApplicationName {get;set;}
            if (!String.IsNullOrEmpty(request.ClientName))
            {
                Msg(request, MessageLevel.Debug, String.Format("ApplicationName will be set to '{0}'", request.ClientName));
                sqlConnectionInfo.ApplicationName = request.ClientName;
            }

            //Authentication         Property   Microsoft.SqlServer.Management.Common.SqlConnectionInfo+AuthenticationMethod Authentication {get;set;}
            //[Microsoft.SqlServer.Management.Common.SqlConnectionInfo+AuthenticationMethod]::ActiveDirectoryIntegrated
            //[Microsoft.SqlServer.Management.Common.SqlConnectionInfo+AuthenticationMethod]::ActiveDirectoryInteractive
            //[Microsoft.SqlServer.Management.Common.SqlConnectionInfo+AuthenticationMethod]::ActiveDirectoryPassword
            //[Microsoft.SqlServer.Management.Common.SqlConnectionInfo+AuthenticationMethod]::NotSpecified
            //[Microsoft.SqlServer.Management.Common.SqlConnectionInfo+AuthenticationMethod]::SqlPassword
            if (!String.IsNullOrEmpty(request.AuthenticationType))
            {
                Msg(request, MessageLevel.Debug, String.Format("Authentication will be set to '{0}' (from AuthenticationType parameter)", request.AuthenticationType));
                // PS: [AuthenticationMethod]::$AuthenticationType - static member access by
                // string is case-insensitive (cross-model review 2026-07-07 finding 1).
                sqlConnectionInfo.Authentication = (SqlConnectionInfo.AuthenticationMethod)Enum.Parse(typeof(SqlConnectionInfo.AuthenticationMethod), request.AuthenticationType, true);
                Msg(request, MessageLevel.Debug, String.Format("UseIntegratedSecurity will be set to '{0}' for {1}", false, request.AuthenticationType));
                sqlConnectionInfo.UseIntegratedSecurity = false;
            }
            else if (String.Equals(authType, "azure integrated", StringComparison.Ordinal))
            {
                // Azure AD integrated security
                // TODO: This is not tested / How can we test that?
                Msg(request, MessageLevel.Debug, "Authentication will be set to 'ActiveDirectoryIntegrated'");
                sqlConnectionInfo.Authentication = SqlConnectionInfo.AuthenticationMethod.ActiveDirectoryIntegrated;
            }
            else if (String.Equals(authType, "azure ad", StringComparison.Ordinal))
            {
                // Azure AD account with password
                Msg(request, MessageLevel.Debug, "Authentication will be set to 'ActiveDirectoryPassword'");
                sqlConnectionInfo.Authentication = SqlConnectionInfo.AuthenticationMethod.ActiveDirectoryPassword;
            }

            //ConnectionProtocol     Property   Microsoft.SqlServer.Management.Common.NetworkProtocol ConnectionProtocol {get;set;}
            if (!String.IsNullOrEmpty(request.NetworkProtocol))
            {
                Msg(request, MessageLevel.Debug, String.Format("ConnectionProtocol will be set to '{0}'", request.NetworkProtocol));
                // PS enum conversion on assignment is case-insensitive (finding 1).
                sqlConnectionInfo.ConnectionProtocol = (NetworkProtocol)Enum.Parse(typeof(NetworkProtocol), request.NetworkProtocol, true);
            }

            //ConnectionString       Property   string ConnectionString {get;}
            // Only a getter, not a setter - so don't touch

            //ConnectionTimeout      Property   int ConnectionTimeout {get;set;}
            if (request.ConnectTimeout != 0)
            {
                Msg(request, MessageLevel.Debug, String.Format("ConnectionTimeout will be set to '{0}'", request.ConnectTimeout));
                sqlConnectionInfo.ConnectionTimeout = request.ConnectTimeout;
            }

            //DatabaseName           Property   string DatabaseName {get;set;}
            if (!String.IsNullOrEmpty(request.Database))
            {
                Msg(request, MessageLevel.Debug, String.Format("Database will be set to '{0}'", request.Database));
                sqlConnectionInfo.DatabaseName = request.Database;
            }

            if (!Regex.IsMatch(instance.ToString(), "localdb", RegexOptions.IgnoreCase))
            {
                //EncryptConnection      Property   bool EncryptConnection {get;set;}
                Msg(request, MessageLevel.Debug, String.Format("EncryptConnection will be set to '{0}'", request.EncryptConnection));
                sqlConnectionInfo.EncryptConnection = request.EncryptConnection;
            }
            else
            {
                Msg(request, MessageLevel.Verbose, "localdb detected, skipping unsupported keyword 'Encryption'");
            }

            //MaxPoolSize            Property   int MaxPoolSize {get;set;}
            if (request.MaxPoolSize != 0)
            {
                Msg(request, MessageLevel.Debug, String.Format("MaxPoolSize will be set to '{0}'", request.MaxPoolSize));
                sqlConnectionInfo.MaxPoolSize = request.MaxPoolSize;
            }

            //MinPoolSize            Property   int MinPoolSize {get;set;}
            if (request.MinPoolSize != 0)
            {
                Msg(request, MessageLevel.Debug, String.Format("MinPoolSize will be set to '{0}'", request.MinPoolSize));
                sqlConnectionInfo.MinPoolSize = request.MinPoolSize;
            }

            //PacketSize             Property   int PacketSize {get;set;}
            if (request.PacketSize != 0)
            {
                Msg(request, MessageLevel.Debug, String.Format("PacketSize will be set to '{0}'", request.PacketSize));
                sqlConnectionInfo.PacketSize = request.PacketSize;
            }

            //Password               Property   string Password {get;set;}
            // We will use SecurePassword

            //PoolConnectionLifeTime Property   int PoolConnectionLifeTime {get;set;}
            if (request.PooledConnectionLifetime != 0)
            {
                Msg(request, MessageLevel.Debug, String.Format("PoolConnectionLifeTime will be set to '{0}'", request.PooledConnectionLifetime));
                sqlConnectionInfo.PoolConnectionLifeTime = request.PooledConnectionLifetime;
            }

            //Pooled                 Property   System.Data.SqlTypes.SqlBoolean Pooled {get;set;}
            // TODO: Do we need or want the else path or is it the default and we better don't touch it?
            if (request.NonPooledConnection)
            {
                Msg(request, MessageLevel.Debug, String.Format("Pooled will be set to '{0}'", false));
                sqlConnectionInfo.Pooled = false;
            }
            else
            {
                Msg(request, MessageLevel.Debug, String.Format("Pooled will be set to '{0}'", true));
                sqlConnectionInfo.Pooled = true;
            }

            //QueryTimeout           Property   int QueryTimeout {get;set;}
            // We use ConnectionContext.StatementTimeout instead

            //SecurePassword         Property   securestring SecurePassword {get;set;}
            if (authenticationTypeUsesSqlCredential || authType == "azure ad" || authType == "azure sql" || authType == "local sql")
            {
                Msg(request, MessageLevel.Debug, "SecurePassword will be set");
                // PS: $SqlCredential.Password on a $null credential yields $null (the Desktop
                // Tenant branch clears SqlCredential after token acquisition) - never throw
                // (cross-model review 2026-07-07 finding 2).
                sqlConnectionInfo.SecurePassword = request.SqlCredential == null ? null : request.SqlCredential.Password;
            }

            //ServerCaseSensitivity  Property   Microsoft.SqlServer.Management.Common.ServerCaseSensitivity ServerCaseSensitivity {get;set;}

            //ServerName             Property   string ServerName {get;set;}
            // Was already set by the constructor.

            //ServerType             Property   Microsoft.SqlServer.Management.Common.ConnectionType ServerType {get;}
            // Only a getter, not a setter - so don't touch

            //ServerVersion          Property   Microsoft.SqlServer.Management.Common.ServerVersion ServerVersion {get;set;}
            // We can set that? No, we don't want to...

            //TrustServerCertificate Property   bool TrustServerCertificate {get;set;}
            Msg(request, MessageLevel.Debug, String.Format("TrustServerCertificate will be set to '{0}'", request.TrustServerCertificate));
            sqlConnectionInfo.TrustServerCertificate = request.TrustServerCertificate;

            //UseIntegratedSecurity  Property   bool UseIntegratedSecurity {get;set;}
            // We rely on the default unless AuthenticationType already set it above or UserName changes it automatically.

            //UserName               Property   string UserName {get;set;}
            if (authenticationTypeUsesSqlCredential || authType == "azure ad" || authType == "azure sql" || authType == "local sql")
            {
                Msg(request, MessageLevel.Debug, String.Format("UserName will be set to '{0}'", username));
                sqlConnectionInfo.UserName = username;
            }

            //WorkstationId          Property   string WorkstationId {get;set;}
            if (!String.IsNullOrEmpty(request.WorkstationId))
            {
                Msg(request, MessageLevel.Debug, String.Format("WorkstationId will be set to '{0}'", request.WorkstationId));
                sqlConnectionInfo.WorkstationId = request.WorkstationId;
            }

            ServerConnection serverConnection;
            // If we have an AccessToken, we will build a SqlConnection
            // PS: if ($AccessToken) - truthiness (finding 3)
            if (LanguagePrimitives.IsTrue(request.AccessToken))
            {
                string tokenText = UnwrapAccessToken(state);
                Msg(request, MessageLevel.Debug, "We have an AccessToken and build a SqlConnection with that token");
                Msg(request, MessageLevel.Debug, "But we remove 'Integrated Security=True;'");
                // TODO: How do we get a ConnectionString without this?
                Msg(request, MessageLevel.Debug, "Building SqlConnection from SqlConnectionInfo.ConnectionString");
                string tokenConnectionString = RemoveIntegratedSecurity(sqlConnectionInfo.ConnectionString);
                SqlConnection sqlConnection = new SqlConnection(tokenConnectionString);
                Msg(request, MessageLevel.Debug, "SqlConnection was built");
                sqlConnection.AccessToken = tokenText;
                Msg(request, MessageLevel.Debug, "Building ServerConnection from SqlConnection");
                serverConnection = new ServerConnection(sqlConnection);
                Msg(request, MessageLevel.Debug, "ServerConnection was built");
            }
            else
            {
                Msg(request, MessageLevel.Debug, "Building ServerConnection from SqlConnectionInfo");
                serverConnection = new ServerConnection(sqlConnectionInfo);
                Msg(request, MessageLevel.Debug, "ServerConnection was built");
            }

            if (authType == "local ad" && String.IsNullOrEmpty(request.AuthenticationType))
            {
                if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                {
                    // PS: Stop-Function (no -Continue) followed by return - the cmdlet stops
                    // processing the remaining instances, exactly like the PS source did.
                    throw new ConnectionResolutionException(ConnectionResolutionFailure.WindowsCredentialOnUnix,
                        "Cannot use Windows credentials to connect when host is Linux or OS X. Use kinit instead. See https://github.com/dataplat/dbatools/issues/7602 for more info.", null);
                }
                Msg(request, MessageLevel.Debug, String.Format("ConnectAsUser will be set to '{0}'", true));
                serverConnection.ConnectAsUser = true;

                Msg(request, MessageLevel.Debug, String.Format("ConnectAsUserName will be set to '{0}'", username));
                serverConnection.ConnectAsUserName = username;

                Msg(request, MessageLevel.Debug, "ConnectAsUserPassword will be set");
                // ServerConnection.ConnectAsUserPassword is string-typed; the PS source made the
                // same GetNetworkCredential() unwrap at this boundary (BP-401 exception).
                serverConnection.ConnectAsUserPassword = request.SqlCredential.GetNetworkCredential().Password;
            }

            Msg(request, MessageLevel.Debug, "Building Server from ServerConnection");
            state.Server = new Server(serverConnection);
            Msg(request, MessageLevel.Debug, "Server was built");

            // Set properties of ConnectionContext that are not part of SqlConnectionInfo
            if (request.IsBound("BatchSeparator"))
            {
                Msg(request, MessageLevel.Debug, String.Format("Setting ConnectionContext.BatchSeparator to '{0}'", request.BatchSeparator));
                state.Server.ConnectionContext.BatchSeparator = request.BatchSeparator;
            }
            if (request.IsBound("LockTimeout"))
            {
                Msg(request, MessageLevel.Debug, String.Format("Setting ConnectionContext.LockTimeout to '{0}'", request.LockTimeout));
                state.Server.ConnectionContext.LockTimeout = request.LockTimeout;
            }
            if (request.MultipleActiveResultSets)
            {
                Msg(request, MessageLevel.Debug, "Setting ConnectionContext.MultipleActiveResultSets to 'True'");
                state.Server.ConnectionContext.MultipleActiveResultSets = true;
            }
            if (request.IsBound("SqlExecutionModes"))
            {
                Msg(request, MessageLevel.Debug, String.Format("Setting ConnectionContext.SqlExecutionModes to '{0}'", request.SqlExecutionModes));
                state.Server.ConnectionContext.SqlExecutionModes = (SqlExecutionModes)Enum.Parse(typeof(SqlExecutionModes), request.SqlExecutionModes);
            }
            Msg(request, MessageLevel.Debug, String.Format("Setting ConnectionContext.StatementTimeout to '{0}'", request.StatementTimeout));
            state.Server.ConnectionContext.StatementTimeout = request.StatementTimeout;
            if (request.NonPooledConnection && !state.Server.ConnectionContext.NonPooledConnection)
            {
                Msg(request, MessageLevel.Debug, "Setting ConnectionContext.NonPooledConnection to 'True'");
                state.Server.ConnectionContext.NonPooledConnection = true;
            }
        }

        private static string UnwrapAccessToken(ResolveState state)
        {
            SmoConnectionRequest request = state.Request;

            // Check if token was created by New-DbaAzAccessToken or Get-AzAccessToken
            Msg(request, MessageLevel.Debug, "AccessToken detected, checking for string, SecureString, or PsObjectIRenewableToken");
            object current = request.AccessToken;
            PSObject wrapped = PSObject.AsPSObject(current);
            if (wrapped.Members["GetAccessToken"] != null)
            {
                Msg(request, MessageLevel.Debug, "Token was generated using New-DbaAzAccessToken, executing GetAccessToken()");
                current = wrapped.Methods["GetAccessToken"].Invoke();
                wrapped = PSObject.AsPSObject(current);
            }
            if (wrapped.Members["Token"] != null)
            {
                Msg(request, MessageLevel.Debug, "Token was generated using Get-AzAccessToken, getting .Token");
                object tokenValue = wrapped.Properties["Token"].Value;
                // Check if the Token property is a SecureString (Azure PowerShell v14+)
                SecureString secureToken = UnwrapInput(tokenValue) as SecureString;
                if (secureToken != null)
                {
                    Msg(request, MessageLevel.Debug, "Token is SecureString (Azure PowerShell v14+), converting to plain text");
                    try
                    {
                        current = ConvertFromSecurePass(secureToken);
                        Msg(request, MessageLevel.Debug, "Successfully converted SecureString token to plain text");
                    }
                    catch (Exception ex)
                    {
                        throw new ConnectionResolutionException(ConnectionResolutionFailure.AccessTokenConversion,
                            String.Format("Failed to convert SecureString AccessToken to plain text: {0}", ex.Message), ex);
                    }
                }
                else
                {
                    Msg(request, MessageLevel.Debug, "Token is plain text string (Azure PowerShell v13 and earlier)");
                    current = tokenValue;
                }
            }
            else
            {
                SecureString directSecure = UnwrapInput(current) as SecureString;
                if (directSecure != null)
                {
                    // Handle direct SecureString AccessToken input
                    Msg(request, MessageLevel.Debug, "AccessToken is directly provided as SecureString, converting to plain text");
                    try
                    {
                        current = ConvertFromSecurePass(directSecure);
                        Msg(request, MessageLevel.Debug, "Successfully converted direct SecureString AccessToken to plain text");
                    }
                    catch (Exception ex)
                    {
                        throw new ConnectionResolutionException(ConnectionResolutionFailure.AccessTokenConversion,
                            String.Format("Failed to convert SecureString AccessToken to plain text: {0}", ex.Message), ex);
                    }
                }
            }
            // The PS source reassigned $AccessToken, so the unwrapped token persists for the
            // remaining instances of the same call.
            request.AccessToken = current;
            return current == null ? null : current.ToString();
        }

        private static string ConvertFromSecurePass(SecureString inputObject)
        {
            // private/functions/ConvertFrom-SecurePass.ps1: decrypt on Linux, Windows and OSX
            // via (New-Object PSCredential("fake", $InputObject)).GetNetworkCredential().Password
            return new System.Net.NetworkCredential("fake", inputObject).Password;
        }
    }
}
