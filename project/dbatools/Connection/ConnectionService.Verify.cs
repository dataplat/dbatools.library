using System;
using System.Collections.Generic;
using System.Management.Automation;
using System.Text.RegularExpressions;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;
using Dataplat.Dbatools.TabExpansion;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;

namespace Dataplat.Dbatools.Connection
{
    public static partial class ConnectionService
    {
        private static ConnectionResolution VerifyAndComplete(ResolveState state)
        {
            SmoConnectionRequest request = state.Request;
            DbaInstanceParameter instance = state.Instance;
            Server server = state.Server;

            string maskedConnString = HideConnectionString(server.ConnectionContext.ConnectionString);
            Msg(request, MessageLevel.Debug, String.Format("The masked server.ConnectionContext.ConnectionString is {0}", maskedConnString));

            // It doesn't matter which input we have, we pass this line and have a server SMO in $server to work with
            // It might be a brand new one or an already used one.
            // "Pooled connections are always closed directly after an operation" (so .IsOpen does not tell us anything):
            // https://docs.microsoft.com/en-us/dotnet/api/microsoft.sqlserver.management.common.connectionmanager.isopen?view=sql-smo-160#Microsoft_SqlServer_Management_Common_ConnectionManager_IsOpen
            // We could use .ConnectionContext.SqlConnectionObject.Open(), but we would have to check ConnectionContext.IsOpen first because it is not allowed on open connections
            // But ConnectionContext.IsOpen does not tell the truth if the instance was just shut down
            // And we don't use $server.ConnectionContext.Connect() as this would create a non pooled connection
            // Instead we run a real T-SQL command and just SELECT something to be sure we have a valid connection and let the SMO handle the connection
            bool connectionSucceeded = false;
            Exception connectionError = null;
            try
            {
                Msg(request, MessageLevel.Debug, "We connect to the instance by running SELECT 'dbatools is opening a new connection'");
                server.ConnectionContext.ExecuteWithResults("SELECT 'dbatools is opening a new connection'");
                Msg(request, MessageLevel.Debug, "We have a connected server object");
                connectionSucceeded = true;
            }
            catch (Exception initialFailure)
            {
                connectionError = initialFailure;
                string errorMessage = CollectMessages(initialFailure);

                // Check if AllowTrustServerCertificate is enabled and this is a new connection attempt and not already using TrustServerCertificate
                // Also verify this is a certificate validation error
                bool isCertError = Regex.IsMatch(errorMessage, "certificate", RegexOptions.IgnoreCase) || Regex.IsMatch(errorMessage, "SSL", RegexOptions.IgnoreCase) || Regex.IsMatch(errorMessage, "TLS", RegexOptions.IgnoreCase) || Regex.IsMatch(errorMessage, "trust", RegexOptions.IgnoreCase);
                if (request.AllowTrustServerCertificate && state.IsNewConnection && !request.TrustServerCertificate && isCertError && state.InputType == ResolveInputType.String)
                {
                    Msg(request, MessageLevel.Verbose, "Initial connection failed due to certificate validation. Retrying with TrustServerCertificate enabled.");
                    Msg(request, MessageLevel.Debug, String.Format("Original error: {0}", errorMessage));

                    // Recreate the connection with TrustServerCertificate enabled
                    try
                    {
                        // Update the SqlConnectionInfo to trust the server certificate
                        server.ConnectionContext.SqlConnectionObject.ConnectionString = ApplyTrustServerCertificate(server.ConnectionContext.SqlConnectionObject.ConnectionString);

                        // Retry the connection
                        Msg(request, MessageLevel.Debug, "Retrying connection with TrustServerCertificate enabled");
                        server.ConnectionContext.ExecuteWithResults("SELECT 'dbatools is opening a new connection with TrustServerCertificate'");
                        Msg(request, MessageLevel.Verbose, "Connection succeeded with TrustServerCertificate enabled");
                        connectionSucceeded = true;
                    }
                    catch (Exception trustRetryFailure)
                    {
                        Msg(request, MessageLevel.Debug, String.Format("Retry with TrustServerCertificate also failed: {0}", trustRetryFailure.Message));
                        // Keep the latest error details available for any follow-up retry and final reporting
                        connectionError = trustRetryFailure;
                        errorMessage = CollectMessages(trustRetryFailure);
                    }
                }

                // Check if the error is about Failover Partner requiring Initial Catalog.
                // This happens when connecting to a SQL Server instance configured for database mirroring.
                // The .NET SqlClient sends Failover Partner info from the server's TDS handshake to the
                // connection pool, and the pool then requires Initial Catalog to be set in the connection string.
                bool isFailoverPartnerError = Regex.IsMatch(errorMessage, "Failover Partner", RegexOptions.IgnoreCase) && Regex.IsMatch(errorMessage, "Initial Catalog", RegexOptions.IgnoreCase);
                if (state.IsNewConnection && isFailoverPartnerError && !connectionSucceeded && (state.InputType == ResolveInputType.String || state.InputType == ResolveInputType.ConnectionString || state.InputType == ResolveInputType.RegisteredServer))
                {
                    Msg(request, MessageLevel.Verbose, "Connection failed because the server is configured for database mirroring (Failover Partner requires Initial Catalog). Retrying with Initial Catalog=master.");
                    Msg(request, MessageLevel.Debug, String.Format("Original error: {0}", errorMessage));

                    try
                    {
                        // Add Initial Catalog=master to satisfy the Failover Partner connection string requirement
                        string retryConnectionString = NormalizeFailoverPartnerKey(server.ConnectionContext.SqlConnectionObject.ConnectionString);
                        retryConnectionString = EnsureInitialCatalogMaster(retryConnectionString);
                        server.ConnectionContext.SqlConnectionObject.ConnectionString = retryConnectionString;

                        // Retry the connection
                        Msg(request, MessageLevel.Debug, "Retrying connection with Initial Catalog=master for server with database mirroring");
                        server.ConnectionContext.ExecuteWithResults("SELECT 'dbatools is opening a new connection with Initial Catalog'");
                        Msg(request, MessageLevel.Verbose, "Connection succeeded with Initial Catalog=master for server with database mirroring");
                        connectionSucceeded = true;
                    }
                    catch (Exception failoverRetryFailure)
                    {
                        Msg(request, MessageLevel.Debug, String.Format("Retry with Initial Catalog=master also failed: {0}", failoverRetryFailure.Message));
                        // Keep the original error for reporting
                        connectionError = failoverRetryFailure;
                    }
                }

                if (!connectionSucceeded)
                {
                    // PS: Stop-Function -Target $instance -Message "Failure" -Category ConnectionError -ErrorRecord $connectionError -Continue
                    throw new ConnectionResolutionException(ConnectionResolutionFailure.ConnectFailure, "Failure", connectionError);
                }
            }

            if (request.AzureUnsupported && server.DatabaseEngineType == DatabaseEngineType.SqlAzureDatabase)
            {
                if (state.IsNewConnection)
                    server.ConnectionContext.Disconnect();
                throw new ConnectionResolutionException(ConnectionResolutionFailure.AzureUnsupported, "Azure SQL Database not supported", null);
            }

            if (request.MinimumVersion != 0 && server.VersionMajor != 0)
            {
                if (server.VersionMajor < request.MinimumVersion)
                {
                    if (state.IsNewConnection)
                        server.ConnectionContext.Disconnect();
                    throw new ConnectionResolutionException(ConnectionResolutionFailure.MinimumVersion, String.Format("SQL Server version {0} required - {1} not supported.", request.MinimumVersion, server), null);
                }
            }

            ConnectionResolution resolution = new ConnectionResolution();
            resolution.Instance = instance;
            resolution.IsNewConnection = state.IsNewConnection;
            resolution.IsAzure = state.IsAzure;
            resolution.UsesCredentialSspiProvider = state.UsesCredentialSspiProvider;

            if (request.SqlConnectionOnly)
            {
                RegisterConnection(server.ConnectionContext.ConnectionString, server.ConnectionContext.SqlConnectionObject, request.MessageCallback);
                Msg(request, MessageLevel.Debug, "We return only SqlConnection in server.ConnectionContext.SqlConnectionObject");
                resolution.SqlConnection = server.ConnectionContext.SqlConnectionObject;
                return resolution;
            }

            Decorate(state);
            Msg(request, MessageLevel.Debug, "We return the server object");
            resolution.Server = server;
            return resolution;
        }

        private static void Decorate(ResolveState state)
        {
            SmoConnectionRequest request = state.Request;
            DbaInstanceParameter instance = state.Instance;
            Server server = state.Server;

            object existingComputerName = SmoServerExtensions.GetPSProperty(server, "ComputerName");
            bool hasComputerName = existingComputerName != null && !String.IsNullOrEmpty(existingComputerName.ToString());
            if (!hasComputerName)
            {
                // To set the source of ComputerName to something else than the default use this config parameter:
                // Set-DbatoolsConfig -FullName commands.connect-dbainstance.smo.computername.source -Value 'server.ComputerNamePhysicalNetBIOS'
                // Set-DbatoolsConfig -FullName commands.connect-dbainstance.smo.computername.source -Value 'instance.ComputerName'
                // If the config parameter is not used, then there a different ways to handle the new property ComputerName
                // Rules in legacy code: Use $server.NetName, but if $server.NetName is empty or we are on Azure or Linux, use $instance.ComputerName
                string computerName = null;
                string computerNameSource = GetConfigString("commands.connect-dbainstance.smo.computername.source", null);
                if (!String.IsNullOrEmpty(computerNameSource))
                {
                    Msg(request, MessageLevel.Debug, String.Format("Setting ComputerName based on {0}", computerNameSource));
                    // PS: $object, $property = $computerNameSource -split '\.'
                    //     $value = (Get-Variable -Name $object).Value.$property
                    string[] sourceParts = computerNameSource.Split('.');
                    object sourceObject = null;
                    if (sourceParts.Length == 2)
                    {
                        if (String.Equals(sourceParts[0], "server", StringComparison.OrdinalIgnoreCase))
                            sourceObject = server;
                        else if (String.Equals(sourceParts[0], "instance", StringComparison.OrdinalIgnoreCase))
                            sourceObject = instance;
                    }
                    object sourceValue = sourceObject == null ? null : SmoServerExtensions.GetPSProperty(sourceObject, sourceParts[1]);
                    if (sourceValue != null && !String.IsNullOrEmpty(sourceValue.ToString()))
                    {
                        computerName = sourceValue.ToString();
                        Msg(request, MessageLevel.Debug, String.Format("ComputerName will be set to {0}", computerName));
                    }
                    else
                    {
                        Msg(request, MessageLevel.Debug, "No value found for ComputerName, so will use the default");
                    }
                }
                if (String.IsNullOrEmpty(computerName))
                {
                    if (server.DatabaseEngineType == DatabaseEngineType.SqlAzureDatabase)
                    {
                        Msg(request, MessageLevel.Debug, "We are on Azure, so server.ComputerName will be set to instance.ComputerName");
                        computerName = instance.ComputerName;
                    }
                    else if (String.Equals(server.HostPlatform, "Linux", StringComparison.OrdinalIgnoreCase))
                    {
                        Msg(request, MessageLevel.Debug, "We are on Linux what is often on docker and the internal name is not useful, so server.ComputerName will be set to instance.ComputerName");
                        computerName = instance.ComputerName;
                    }
                    else if (!String.IsNullOrEmpty(server.NetName))
                    {
                        Msg(request, MessageLevel.Debug, "We will set server.ComputerName to server.NetName");
                        computerName = server.NetName;
                    }
                    else
                    {
                        Msg(request, MessageLevel.Debug, "We will set server.ComputerName to instance.ComputerName as server.NetName is empty");
                        computerName = instance.ComputerName;
                    }
                    Msg(request, MessageLevel.Debug, String.Format("ComputerName will be set to {0}", computerName));
                }
                AddNoteProperty(PSObject.AsPSObject(server), "ComputerName", computerName);
            }

            // PS: if (-not $server.IsAzure) - decoration is skipped only when IsAzure is
            // already present AND true; a false value is refreshed every time (quirk preserved).
            object existingIsAzure = SmoServerExtensions.GetPSProperty(server, "IsAzure");
            bool isAzureAlreadyTrue = existingIsAzure is bool && (bool)existingIsAzure;
            if (!isAzureAlreadyTrue)
            {
                PSObject wrapped = PSObject.AsPSObject(server);
                AddNoteProperty(wrapped, "IsAzure", state.IsAzure);
                AddNoteProperty(wrapped, "DbaInstanceName", instance.InstanceName);
                // Server.DomainInstanceName is adapter-only in SqlManagementObjects 181.x, so
                // it is read through the PSObject view with a composed fallback.
                AddNoteProperty(wrapped, "SqlInstance", SmoServerExtensions.GetDomainInstanceName(server));
                AddNoteProperty(wrapped, "NetPort", instance.Port);
                AddNoteProperty(wrapped, "ConnectedAs", server.ConnectionContext.TrueLogin);
                Msg(request, MessageLevel.Debug, String.Format("We added IsAzure = '{0}', DbaInstanceName = instance.InstanceName = '{1}', SqlInstance = server.DomainInstanceName = '{2}', NetPort = instance.Port = '{3}', ConnectedAs = server.ConnectionContext.TrueLogin = '{4}'", SmoServerExtensions.GetPSProperty(server, "IsAzure"), SmoServerExtensions.GetPSProperty(server, "DbaInstanceName"), SmoServerExtensions.GetPSProperty(server, "SqlInstance"), SmoServerExtensions.GetPSProperty(server, "NetPort"), SmoServerExtensions.GetPSProperty(server, "ConnectedAs")));
            }
        }

        /// <summary>
        /// The post-emission registrations of a resolved connection for callers that do not
        /// interleave TEPP script execution (the GetServer facade and the row-3 re-entry):
        /// TEPP instance seeding and SetDefaultInitFields for new non-DAC connections, then
        /// the connection-hash registration that the PS source ran for every instance.
        /// </summary>
        /// <param name="resolution">The resolution to finalize</param>
        /// <param name="request">The request it was resolved from</param>
        public static void FinalizeConnection(ConnectionResolution resolution, SmoConnectionRequest request)
        {
            if (resolution == null || resolution.Server == null)
                return;
            if (resolution.IsNewConnection && !request.DedicatedAdminConnection)
            {
                if (!resolution.UsesCredentialSspiProvider)
                    RegisterInstanceForTepp(resolution.Instance, resolution.Server);
                ApplyDefaultInitFields(resolution.Server, resolution.IsAzure, request.MessageCallback);
            }
            RegisterConnection(resolution.Server.ConnectionContext.ConnectionString, resolution.Server, request.MessageCallback);
        }

        /// <summary>
        /// Registers a new connection with the TEPP updater, exactly like the PS source:
        /// SetInstance with a copied ConnectionContext and the SysAdmin flag, plus the
        /// sqlinstance name-cache append.
        /// </summary>
        /// <param name="instance">The instance that was connected</param>
        /// <param name="server">The connected server</param>
        public static void RegisterInstanceForTepp(DbaInstanceParameter instance, Server server)
        {
            // Register the connected instance, so that the TEPP updater knows it's been connected to and starts building the cache
            TabExpansionHost.SetInstance(instance.FullSmoName.ToLowerInvariant(), server.ConnectionContext.Copy(), Regex.IsMatch(server.ConnectionContext.FixedServerRoles.ToString(), "SysAdmin", RegexOptions.IgnoreCase));

            // Update cache for instance names
            string lowerName = instance.FullSmoName.ToLowerInvariant();
            lock (TabExpansionHost.Cache)
            {
                object cacheValue = TabExpansionHost.Cache["sqlinstance"];
                List<object> names = new List<object>();
                System.Collections.IEnumerable enumerable = cacheValue as System.Collections.IEnumerable;
                bool found = false;
                if (enumerable != null && !(cacheValue is string))
                {
                    foreach (object item in enumerable)
                    {
                        names.Add(item);
                        if (item != null && String.Equals(item.ToString(), lowerName, StringComparison.OrdinalIgnoreCase))
                            found = true;
                    }
                }
                else if (cacheValue != null)
                {
                    names.Add(cacheValue);
                    if (String.Equals(cacheValue.ToString(), lowerName, StringComparison.OrdinalIgnoreCase))
                        found = true;
                }
                if (!found)
                {
                    names.Add(lowerName);
                    // PS += on an array produces a new object[]; keep that shape for the TEPP scripts.
                    TabExpansionHost.Cache["sqlinstance"] = names.ToArray();
                }
            }
        }

    }
}
