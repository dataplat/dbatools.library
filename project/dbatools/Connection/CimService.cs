using System;
using System.Collections.Generic;
using System.Management.Automation;
using Microsoft.Management.Infrastructure;
using Microsoft.Management.Infrastructure.Options;
using Dataplat.Dbatools.Configuration;

namespace Dataplat.Dbatools.Connection
{
    /// <summary>
    /// Compiled equivalent of public/Get-DbaCmObject.ps1 for ported computer-management
    /// commands, scoped to the CimRM -> CimDCOM rungs of its protocol chain. The Wmi and
    /// PowerShellRemoting rungs stay with the PS implementation until the
    /// RemoteExecutionService wave lands. Drives the same ManagementConnection /
    /// ConnectionHost machinery the PS command uses, so protocol state, credential caches
    /// and CIM session reuse remain shared between the PS and compiled halves during the
    /// hybrid period.
    /// </summary>
    public static class CimService
    {
        /// <summary>
        /// Enumerates all CIM instances of a class on the target computer, trying CIM over
        /// WinRM first and CIM over DCOM second (integrated auth first when no credential is
        /// given), with Get-DbaCmObject's cache discipline: successes and failures are
        /// reported onto the cached ManagementConnection, good/bad credentials are recorded,
        /// and the connection is written back to ConnectionHost unless caching is disabled.
        /// </summary>
        /// <param name="computerName">The target computer name.</param>
        /// <param name="credential">Optional explicit credential; null = integrated auth.</param>
        /// <param name="className">The CIM class to enumerate.</param>
        /// <param name="cimNamespace">The CIM namespace (Get-DbaCmObject defaults to root\cimv2).</param>
        /// <returns>The materialized instances; enumeration happens inside the protocol loop so lazy CIM faults are triaged.</returns>
        public static List<CimInstance> EnumerateInstances(string computerName, PSCredential credential, string className, string cimNamespace = @"root\cimv2")
        {
            if (String.IsNullOrWhiteSpace(computerName))
                throw new ArgumentNullException("computerName");

            // PS: all connection caching runs using lower-case strings.
            string computer = computerName.ToLowerInvariant();

            ManagementConnection connection;
            if (!ConnectionHost.Connections.TryGetValue(computer, out connection) || connection == null)
                connection = new ManagementConnection(computer);

            // PS: New-DbaCimSessionOptionWithTimeout - session options carry the configured
            // operation timeout (ComputerManagement.CimOperationTimeout, fallback 60s).
            TimeSpan operationTimeout = GetConfigTimeSpan("computermanagement.cimoperationtimeout", TimeSpan.FromSeconds(60));
            if (connection.CimWinRMOptions == null)
            {
                WSManSessionOptions wsmanOptions = new WSManSessionOptions();
                wsmanOptions.Timeout = operationTimeout;
                connection.CimWinRMOptions = wsmanOptions;
            }
            if (connection.CimDComOptions == null)
            {
                // Mirror GetDefaultCimDcomOptions/New-CimSessionOption -Protocol Dcom: a raw
                // DComSessionOptions without impersonation/packet settings makes local DCOM
                // enumerations fail with NotFound (verified live 2026-07-06).
                DComSessionOptions dcomOptions = new DComSessionOptions();
                dcomOptions.PacketPrivacy = true;
                dcomOptions.PacketIntegrity = true;
                dcomOptions.Impersonation = ImpersonationType.Impersonate;
                dcomOptions.Timeout = operationTimeout;
                connection.CimDComOptions = dcomOptions;
            }

            PSCredential cred;
            try
            {
                cred = connection.GetCredential(credential);
            }
            catch (Exception e)
            {
                // Message text mirrors Get-DbaCmObject's bad-credential composition; ported
                // callers triage on the "known to not work" phrase.
                string message = "Bad credentials. ";
                if (credential != null)
                    message += "The credentials for " + credential.UserName + " are known to not work. ";
                else
                    message += "The windows credentials are known to not work. ";
                if (connection.EnableCredentialFailover || connection.OverrideExplicitCredential)
                    message += "The connection is configured to use credentials that are known to be good, but none have been registered yet. ";
                else if (connection.Credentials != null)
                    message += "Working credentials are known for " + connection.Credentials.UserName + ", however the connection is not configured to automatically use them. This can be done using 'Set-DbaCmConnection -ComputerName " + connection + " -OverrideExplicitCredential' ";
                else if (connection.UseWindowsCredentials)
                    message += "The windows credentials are known to work, however the connection is not configured to automatically use them. This can be done using 'Set-DbaCmConnection -ComputerName " + connection + " -OverrideExplicitCredential' ";
                message += e.Message;
                throw new InvalidOperationException(message, e);
            }

            // The service drives only the two CIM rungs; the others are excluded up front so
            // GetConnectionType sequences CimRM -> CimDCOM exactly like the PS loop does when
            // both are viable.
            ManagementConnectionType excluded = ManagementConnectionType.Wmi | ManagementConnectionType.PowerShellRemoting;

            while (true)
            {
                ManagementConnectionType conType;
                try
                {
                    conType = connection.GetConnectionType(excluded, false);
                }
                catch (Exception e)
                {
                    if (!ConnectionHost.DisableCache)
                        ConnectionHost.Connections[computer] = connection;
                    throw new PSInvalidOperationException("[" + computer + "] Unable to find a connection to the target system. Ensure the name is typed correctly, and the server allows any of the following protocols: CimRM, CimDCOM", e);
                }

                try
                {
                    object raw;
                    if (conType == ManagementConnectionType.CimRM)
                        raw = connection.GetCimRMInstance(cred, className, cimNamespace);
                    else
                        raw = connection.GetCimDComInstance(cred, className, cimNamespace);

                    // Materialize inside the try: MMI enumerables fault lazily, and the PS
                    // pipeline likewise enumerates the method result inside its try block.
                    List<CimInstance> instances = new List<CimInstance>();
                    IEnumerable<CimInstance> sequence = raw as IEnumerable<CimInstance>;
                    if (sequence != null)
                    {
                        foreach (CimInstance instance in sequence)
                            instances.Add(instance);
                    }

                    connection.ReportSuccess(conType);
                    connection.AddGoodCredential(cred);
                    if (!ConnectionHost.DisableCache)
                        ConnectionHost.Connections[computer] = connection;
                    return instances;
                }
                catch (Exception e)
                {
                    CimErrorVerdict verdict = ResolveCimError(e, computer, className, cimNamespace);

                    if (verdict.BadCredentials)
                    {
                        connection.AddBadCredential(cred);
                        if (!ConnectionHost.DisableCache)
                            ConnectionHost.Connections[computer] = connection;
                        throw new UnauthorizedAccessException("[" + computer + "] Invalid connection credentials", e);
                    }
                    if (verdict.BadConnection)
                    {
                        connection.ReportFailure(conType);
                        excluded = excluded | conType;
                        continue;
                    }
                    throw new InvalidOperationException(verdict.Message, e);
                }
            }
        }

        private sealed class CimErrorVerdict
        {
            public string Message;
            public bool BadConnection;
            public bool BadCredentials;
        }

        private static CimErrorVerdict ResolveCimError(Exception error, string computer, string className, string cimNamespace)
        {
            // Port of Get-DbaCmObject's Resolve-CimError utility: decide the user-facing
            // message and whether the failure means "this protocol is not viable" (fall to
            // the next rung) or "the operation itself is invalid" (stop with the message).
            CimErrorVerdict verdict = new CimErrorVerdict();

            // ManagementConnection translates session-create credential failures into
            // UnauthorizedAccessException before they ever reach the triage table.
            if (error is UnauthorizedAccessException)
            {
                verdict.BadCredentials = true;
                verdict.Message = "[" + computer + "] Invalid connection credentials";
                return verdict;
            }

            CimException cimError = error as CimException;
            if (cimError == null)
                cimError = error.InnerException as CimException;
            if (cimError == null)
            {
                verdict.BadConnection = true;
                verdict.Message = "[" + computer + "] An otherwise unexpected error happened.";
                return verdict;
            }

            int code = (int)cimError.NativeErrorCode;
            string messageId = cimError.MessageId;

            // PS region "1 = Generic runtime error": specific HRESULTs re-map before the table.
            if (code == 1)
            {
                if (messageId == "HRESULT 0x8007052e" || messageId == "HRESULT 0x80070005")
                {
                    verdict.BadCredentials = true;
                    verdict.Message = "[" + computer + "] Invalid connection credentials";
                    return verdict;
                }
                if (messageId == "HRESULT 0x80041013")
                {
                    verdict.Message = "[" + computer + "] Failed to access " + className + " in namespace " + cimNamespace;
                    return verdict;
                }
                if (messageId == "HRESULT 0x8004100e")
                {
                    verdict.Message = "[" + computer + "] Invalid namespace: " + cimNamespace;
                    return verdict;
                }
                if (messageId == "HRESULT 0x80041010")
                {
                    verdict.Message = "[" + computer + "] Invalid class name (" + className + "), not found in current namespace (" + cimNamespace + ")";
                    return verdict;
                }
                verdict.BadConnection = true;
                verdict.Message = "[" + computer + "] An otherwise unexpected error happened.";
                return verdict;
            }

            // The messages table, verbatim from Resolve-CimError.
            switch (code)
            {
                case 2:
                    verdict.Message = "[" + computer + "] Access to computer granted, but access to " + cimNamespace + "\\" + className + " denied";
                    return verdict;
                case 3:
                    verdict.Message = "[" + computer + "] Invalid namespace: " + cimNamespace;
                    return verdict;
                case 4:
                    verdict.Message = "[" + computer + "] Invalid parameters were specified";
                    return verdict;
                case 5:
                    verdict.Message = "[" + computer + "] Invalid class name (" + className + "), not found in current namespace (" + cimNamespace + ")";
                    return verdict;
                case 6:
                    verdict.Message = "[" + computer + "] The requested object of class " + className + " could not be found";
                    return verdict;
                case 7:
                    verdict.Message = "[" + computer + "] The operation against class " + className + " was not supported. This generally is a serverside WMI Provider issue (That is: It is specific to the application being managed via WMI)";
                    return verdict;
                case 8:
                case 9:
                    verdict.Message = "[" + computer + "] The operation against class " + className + " is refused as long as it contains instances (data)";
                    return verdict;
                case 10:
                    verdict.Message = "[" + computer + "] The operation against class " + className + " cannot be carried out since the specified superclass does not exist.";
                    return verdict;
                case 11:
                    verdict.Message = "[" + computer + "] The specified object in " + className + " already exists.";
                    return verdict;
                case 12:
                    verdict.Message = "[" + computer + "] The specified property does not exist on " + className + ".";
                    return verdict;
                case 13:
                    verdict.Message = "[" + computer + "] The input type is invalid.";
                    return verdict;
                case 14:
                    verdict.Message = "[" + computer + "] Invalid query language. Please check your query string.";
                    return verdict;
                case 15:
                    verdict.Message = "[" + computer + "] Invalid query string. Please check your syntax.";
                    return verdict;
                case 16:
                    verdict.Message = "[" + computer + "] The specified method on " + className + " is not available.";
                    return verdict;
                case 17:
                    verdict.Message = "[" + computer + "] The specified method on " + className + " does not exist.";
                    return verdict;
                case 18:
                    verdict.Message = "[" + computer + "] An unexpected response has happened in this request";
                    return verdict;
                case 19:
                    verdict.Message = "[" + computer + "] The specified destination for this request is invalid.";
                    return verdict;
                case 20:
                    verdict.Message = "[" + computer + "] The specified namespace " + cimNamespace + " is not empty.";
                    return verdict;
                default:
                    // PS region "0 = Non-CIM Issue not covered by the framework": anything
                    // outside the table reads as a connection-level failure.
                    verdict.BadConnection = true;
                    verdict.Message = "[" + computer + "] An otherwise unexpected error happened.";
                    return verdict;
            }
        }

        private static TimeSpan GetConfigTimeSpan(string key, TimeSpan fallback)
        {
            Config config;
            if (ConfigurationHost.Configurations.TryGetValue(key, out config) && config != null && config.Value != null)
            {
                if (config.Value is TimeSpan)
                    return (TimeSpan)config.Value;
                try
                {
                    return TimeSpan.Parse(config.Value.ToString(), System.Globalization.CultureInfo.InvariantCulture);
                }
                catch
                {
                    // fall through to the fallback on any malformed configuration value
                }
            }
            return fallback;
        }
    }
}
