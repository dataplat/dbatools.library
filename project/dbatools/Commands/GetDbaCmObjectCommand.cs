using System;
using System.Collections;
using System.Collections.Generic;
using System.Management.Automation;
using System.Text.RegularExpressions;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;
using Microsoft.Management.Infrastructure;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Retrieves Windows system information from SQL Server hosts using WMI/CIM with intelligent connection fallback.
    /// Queries WMI or CIM classes on SQL Server hosts to gather system-level information. Automatically tries
    /// multiple connection protocols in order of preference and remembers which methods work for each server.
    /// </summary>
    [Cmdlet("Get", "DbaCmObject", DefaultParameterSetName = "Class")]
    public class GetDbaCmObjectCommand : DbaBaseCmdlet
    {
        /// <summary>
        /// Specifies the WMI or CIM class name to query from the target servers.
        /// </summary>
        [Parameter(Mandatory = true, ParameterSetName = "Class", Position = 0)]
        [Alias("Class")]
        public string ClassName { get; set; }

        /// <summary>
        /// Specifies a custom WQL query to execute against the target servers.
        /// </summary>
        [Parameter(Mandatory = true, ParameterSetName = "Query")]
        public string Query { get; set; }

        /// <summary>
        /// Specifies the target computer names or SQL Server host names to query.
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        public DbaCmConnectionParameter[] ComputerName { get; set; }

        /// <summary>
        /// Credentials to use. Invalid credentials will be stored in a credentials cache and not be reused.
        /// </summary>
        [Parameter()]
        public PSCredential Credential { get; set; }

        /// <summary>
        /// Specifies the WMI namespace path where the target class or query should be executed.
        /// </summary>
        [Parameter()]
        public string Namespace { get; set; } = @"root\cimv2";

        /// <summary>
        /// Excludes specific connection protocols from the automatic fallback sequence.
        /// </summary>
        [Parameter()]
        public ManagementConnectionType[] DoNotUse { get; set; } = new ManagementConnectionType[] { ManagementConnectionType.None };

        /// <summary>
        /// Bypasses timeout protections on connections that have previously failed.
        /// </summary>
        [Parameter()]
        public SwitchParameter Force { get; set; }

        /// <summary>
        /// Converts terminating connection failures into non-terminating errors.
        /// </summary>
        [Parameter()]
        public SwitchParameter SilentlyContinue { get; set; }

        /// <summary>
        /// Whether caching is disabled globally.
        /// </summary>
        private bool _disableCache;

        /// <summary>
        /// The parameter set name, captured in BeginProcessing.
        /// </summary>
        private string _parameterSetName;

        /// <summary>
        /// Computes the effective isContinue value for StopFunction calls that pass -SilentlyContinue.
        /// In PS1, when EnableException is true and SilentlyContinue is false, Stop-Function throws
        /// (ignoring -Continue). When SilentlyContinue is true, it writes a non-terminating error
        /// and continues. When EnableException is false, -Continue always controls the flow.
        /// </summary>
        private bool IsSilentContinue
        {
            get { return !EnableException.ToBool() || SilentlyContinue.IsPresent; }
        }

        /// <summary>
        /// Initializes the cmdlet, reads configuration values.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();
            _disableCache = ConnectionHost.DisableCache;
            WriteMessageAtLevel(
                String.Format("Configuration loaded | Cache disabled: {0}", _disableCache),
                MessageLevel.Verbose,
                null);
            _parameterSetName = ParameterSetName;
        }

        /// <summary>
        /// Processes each computer, attempting connection protocols with fallback.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (ComputerName == null)
            {
                // Default to local computer when not specified
                string localName = Environment.MachineName;
                ComputerName = new DbaCmConnectionParameter[] { new DbaCmConnectionParameter(localName) };
            }

            foreach (DbaCmConnectionParameter connectionObject in ComputerName)
            {
                if (!connectionObject.Success)
                {
                    StopFunction(
                        String.Format("Failed to interpret input: {0}", connectionObject.InputObject),
                        category: ErrorCategory.InvalidArgument,
                        target: connectionObject.InputObject,
                        isContinue: IsSilentContinue);
                    TestFunctionInterrupt();
                    continue;
                }

                ManagementConnection connection = connectionObject.Connection;
                string computer = connection.ComputerName.ToLowerInvariant();

                WriteMessageAtLevel(
                    String.Format("[{0}] Retrieving Management Information", computer),
                    MessageLevel.VeryVerbose,
                    null);

                // Ensure using the right credentials
                PSCredential cred;
                try
                {
                    cred = connection.GetCredential(Credential);
                }
                catch (Exception ex)
                {
                    string message = "Bad credentials. ";
                    if (Credential != null)
                    {
                        message += String.Format("The credentials for {0} are known to not work. ", Credential.UserName);
                    }
                    else
                    {
                        message += "The windows credentials are known to not work. ";
                    }

                    if (connection.EnableCredentialFailover || connection.OverrideExplicitCredential)
                    {
                        message += "The connection is configured to use credentials that are known to be good, but none have been registered yet. ";
                    }
                    else if (connection.Credentials != null)
                    {
                        message += String.Format(
                            "Working credentials are known for {0}, however the connection is not configured to automatically use them. This can be done using 'Set-DbaCmConnection -ComputerName {1} -OverrideExplicitCredential' ",
                            connection.Credentials.UserName,
                            connection.ComputerName);
                    }
                    else if (connection.UseWindowsCredentials)
                    {
                        message += String.Format(
                            "The windows credentials are known to work, however the connection is not configured to automatically use them. This can be done using 'Set-DbaCmConnection -ComputerName {0} -OverrideExplicitCredential' ",
                            connection.ComputerName);
                    }

                    message += ex.Message;
                    StopFunction(
                        message,
                        errorRecord: new ErrorRecord(ex, "BadCredentials", ErrorCategory.InvalidArgument, connection),
                        target: connection,
                        isContinue: true,
                        overrideExceptionMessage: true);
                    TestFunctionInterrupt();
                    continue;
                }

                // Build the enabled protocols description
                string enabledProtocols = "None";
                if (connection.CimRM != ManagementConnectionProtocolState.Disabled)
                    enabledProtocols += ", CimRM";
                if (connection.CimDCOM != ManagementConnectionProtocolState.Disabled)
                    enabledProtocols += ", CimDCOM";
                if (connection.Wmi != ManagementConnectionProtocolState.Disabled)
                    enabledProtocols += ", Wmi";
                if (connection.PowerShellRemoting != ManagementConnectionProtocolState.Disabled)
                    enabledProtocols += ", PowerShellRemoting";

                // Create list of excluded connection types
                ManagementConnectionType excludedTypes = ManagementConnectionType.None;
                if (DoNotUse != null)
                {
                    foreach (ManagementConnectionType item in DoNotUse)
                    {
                        excludedTypes = excludedTypes | item;
                    }
                }

                // Protocol fallback loop
                bool connectionSuccess = false;
                while (!connectionSuccess)
                {
                    ManagementConnectionType conType;
                    try
                    {
                        conType = connection.GetConnectionType(excludedTypes, Force);
                    }
                    catch (Exception ex)
                    {
                        if (!_disableCache)
                            ConnectionHost.Connections[computer] = connection;
                        StopFunction(
                            String.Format("[{0}] Unable to find a connection to the target system. Ensure the name is typed correctly, and the server allows any of the following protocols: {1}",
                                computer, enabledProtocols),
                            errorRecord: new ErrorRecord(ex, "NoConnection", ErrorCategory.OpenError, computer),
                            target: computer,
                            isContinue: IsSilentContinue);
                        TestFunctionInterrupt();
                        break;
                    }

                    switch (conType)
                    {
                        case ManagementConnectionType.CimRM:
                            connectionSuccess = ProcessCimRM(connection, computer, cred, ref excludedTypes, enabledProtocols);
                            break;
                        case ManagementConnectionType.CimDCOM:
                            connectionSuccess = ProcessCimDCOM(connection, computer, cred, ref excludedTypes, enabledProtocols);
                            break;
                        case ManagementConnectionType.Wmi:
                            connectionSuccess = ProcessWmi(connection, computer, cred, ref excludedTypes, enabledProtocols);
                            break;
                        case ManagementConnectionType.PowerShellRemoting:
                            connectionSuccess = ProcessPSRemoting(connection, computer, cred, ref excludedTypes);
                            break;
                    }
                }
            }
        }

        #region Protocol Handlers

        /// <summary>
        /// Attempts to retrieve CIM data over WinRM.
        /// </summary>
        /// <returns>True if successful, false if should try next protocol, or breaks out of computer loop on terminal error</returns>
        private bool ProcessCimRM(ManagementConnection connection, string computer, PSCredential cred, ref ManagementConnectionType excludedTypes, string enabledProtocols)
        {
            WriteMessageAtLevel(
                String.Format("[{0}] Accessing computer using Cim over WinRM", computer),
                MessageLevel.Verbose,
                null);
            try
            {
                object result;
                if (_parameterSetName == "Class")
                {
                    result = connection.GetCimRMInstance(cred, ClassName, Namespace);
                }
                else
                {
                    result = connection.QueryCimRMInstance(cred, Query, "WQL", Namespace);
                }

                WriteMessageAtLevel(
                    String.Format("[{0}] Accessing computer using Cim over WinRM - Success", computer),
                    MessageLevel.Verbose,
                    null);
                connection.ReportSuccess(ManagementConnectionType.CimRM);
                connection.AddGoodCredential(cred);
                if (!_disableCache)
                    ConnectionHost.Connections[computer] = connection;

                WriteEnumerable(result);
                return true;
            }
            catch (Exception ex)
            {
                WriteMessageAtLevel(
                    String.Format("[{0}] Accessing computer using Cim over WinRM - Failed", computer),
                    MessageLevel.Verbose,
                    null);

                CimErrorInfo errorDetails = ResolveCimError(ex, computer, ClassName, Namespace, Query);

                if (errorDetails.BadCredentials)
                {
                    connection.AddBadCredential(cred);
                    if (!_disableCache)
                        ConnectionHost.Connections[computer] = connection;
                    StopFunction(
                        String.Format("[{0}] Invalid connection credentials", computer),
                        errorRecord: new ErrorRecord(ex, "BadCredentials", ErrorCategory.AuthenticationError, computer),
                        target: computer,
                        isContinue: IsSilentContinue,
                        overrideExceptionMessage: true);
                    TestFunctionInterrupt();
                    return true; // break out of protocol loop for this computer
                }

                if (errorDetails.BadConnection)
                {
                    connection.ReportFailure(ManagementConnectionType.CimRM);
                    excludedTypes = excludedTypes | ManagementConnectionType.CimRM;
                    return false; // try next protocol
                }

                StopFunction(
                    errorDetails.Message,
                    errorRecord: new ErrorRecord(ex, "CimRMError", ErrorCategory.NotSpecified, computer),
                    target: computer,
                    isContinue: IsSilentContinue,
                    overrideExceptionMessage: true);
                TestFunctionInterrupt();
                return true; // break out of protocol loop for this computer
            }
        }

        /// <summary>
        /// Attempts to retrieve CIM data over DCOM.
        /// </summary>
        private bool ProcessCimDCOM(ManagementConnection connection, string computer, PSCredential cred, ref ManagementConnectionType excludedTypes, string enabledProtocols)
        {
            WriteMessageAtLevel(
                String.Format("[{0}] Accessing computer using Cim over DCOM", computer),
                MessageLevel.Verbose,
                null);
            try
            {
                object result;
                if (_parameterSetName == "Class")
                {
                    result = connection.GetCimDComInstance(cred, ClassName, Namespace);
                }
                else
                {
                    result = connection.QueryCimDCOMInstance(cred, Query, "WQL", Namespace);
                }

                WriteMessageAtLevel(
                    String.Format("[{0}] Accessing computer using Cim over DCOM - Success", computer),
                    MessageLevel.Verbose,
                    null);
                connection.ReportSuccess(ManagementConnectionType.CimDCOM);
                connection.AddGoodCredential(cred);
                if (!_disableCache)
                    ConnectionHost.Connections[computer] = connection;

                WriteEnumerable(result);
                return true;
            }
            catch (Exception ex)
            {
                WriteMessageAtLevel(
                    String.Format("[{0}] Accessing computer using Cim over DCOM - Failed", computer),
                    MessageLevel.Verbose,
                    null);

                CimErrorInfo errorDetails = ResolveCimError(ex, computer, ClassName, Namespace, Query);

                if (errorDetails.BadCredentials)
                {
                    connection.AddBadCredential(cred);
                    if (!_disableCache)
                        ConnectionHost.Connections[computer] = connection;
                    StopFunction(
                        String.Format("[{0}] Invalid connection credentials", computer),
                        errorRecord: new ErrorRecord(ex, "BadCredentials", ErrorCategory.AuthenticationError, computer),
                        target: computer,
                        isContinue: IsSilentContinue,
                        overrideExceptionMessage: true);
                    TestFunctionInterrupt();
                    return true;
                }

                if (errorDetails.BadConnection)
                {
                    connection.ReportFailure(ManagementConnectionType.CimDCOM);
                    excludedTypes = excludedTypes | ManagementConnectionType.CimDCOM;
                    return false;
                }

                StopFunction(
                    errorDetails.Message,
                    errorRecord: new ErrorRecord(ex, "CimDCOMError", ErrorCategory.NotSpecified, computer),
                    target: computer,
                    isContinue: IsSilentContinue,
                    overrideExceptionMessage: true);
                TestFunctionInterrupt();
                return true;
            }
        }

        /// <summary>
        /// Attempts to retrieve WMI data using Get-WmiObject via PowerShell invocation.
        /// </summary>
        private bool ProcessWmi(ManagementConnection connection, string computer, PSCredential cred, ref ManagementConnectionType excludedTypes, string enabledProtocols)
        {
            WriteMessageAtLevel(
                String.Format("[{0}] Accessing computer using WMI", computer),
                MessageLevel.Verbose,
                null);
            try
            {
                string script;
                List<object> args = new List<object>();

                if (_parameterSetName == "Class")
                {
                    script = "param($comp, $cls, $ns, $crd, $hasCrd, $hasNs) $p = @{ ComputerName = $comp; ClassName = $cls; ErrorAction = 'Stop' }; if ($hasCrd) { $p['Credential'] = $crd }; if ($hasNs) { $p['Namespace'] = $ns }; Get-WmiObject @p";
                    args.Add(computer);
                    args.Add(ClassName);
                    args.Add(Namespace);
                    args.Add(cred);
                    args.Add(cred != null);
                    args.Add(TestBound("Namespace"));
                }
                else
                {
                    script = "param($comp, $qry, $ns, $crd, $hasCrd, $hasNs) $p = @{ ComputerName = $comp; Query = $qry; ErrorAction = 'Stop' }; if ($hasCrd) { $p['Credential'] = $crd }; if ($hasNs) { $p['Namespace'] = $ns }; Get-WmiObject @p";
                    args.Add(computer);
                    args.Add(Query);
                    args.Add(Namespace);
                    args.Add(cred);
                    args.Add(cred != null);
                    args.Add(TestBound("Namespace"));
                }

                var results = InvokeCommand.InvokeScript(
                    false,
                    ScriptBlock.Create(script),
                    null,
                    args.ToArray());

                if (results != null)
                {
                    foreach (PSObject obj in results)
                    {
                        WriteObject(obj);
                    }
                }

                WriteMessageAtLevel(
                    String.Format("[{0}] Accessing computer using WMI - Success", computer),
                    MessageLevel.Verbose,
                    null);
                connection.ReportSuccess(ManagementConnectionType.Wmi);
                connection.AddGoodCredential(cred);
                if (!_disableCache)
                    ConnectionHost.Connections[computer] = connection;

                return true;
            }
            catch (Exception ex)
            {
                WriteMessageAtLevel(
                    String.Format("[{0}] Accessing computer using WMI - Failed", computer),
                    MessageLevel.Verbose,
                    null);

                // Determine error type from the exception
                string reason = GetWmiErrorReason(ex);
                string errorCategory = GetWmiErrorCategory(ex);

                if (reason == "UnauthorizedAccessException")
                {
                    connection.AddBadCredential(cred);
                    if (!_disableCache)
                        ConnectionHost.Connections[computer] = connection;
                    StopFunction(
                        String.Format("[{0}] Invalid connection credentials", computer),
                        errorRecord: new ErrorRecord(ex, "BadCredentials", ErrorCategory.AuthenticationError, computer),
                        target: computer,
                        isContinue: IsSilentContinue);
                    TestFunctionInterrupt();
                    return true;
                }

                if (errorCategory == "InvalidType")
                {
                    StopFunction(
                        String.Format("[{0}] Invalid class name ({1}), not found in current namespace ({2})", computer, ClassName, Namespace),
                        errorRecord: new ErrorRecord(ex, "InvalidClass", ErrorCategory.InvalidType, computer),
                        target: computer,
                        isContinue: IsSilentContinue);
                    TestFunctionInterrupt();
                    return true;
                }

                if (IsProviderLoadFailure(ex))
                {
                    StopFunction(
                        String.Format("[{0}] Failed to access: {1}, in namespace: {2} - There was a provider error. This indicates a potential issue with WMI on the server side.", computer, ClassName, Namespace),
                        errorRecord: new ErrorRecord(ex, "ProviderLoadFailure", ErrorCategory.NotSpecified, computer),
                        target: computer,
                        isContinue: IsSilentContinue);
                    TestFunctionInterrupt();
                    return true;
                }

                // Generic WMI failure - try next protocol
                connection.ReportFailure(ManagementConnectionType.Wmi);
                excludedTypes = excludedTypes | ManagementConnectionType.Wmi;
                return false;
            }
        }

        /// <summary>
        /// Attempts to retrieve WMI data using PowerShell Remoting.
        /// </summary>
        private bool ProcessPSRemoting(ManagementConnection connection, string computer, PSCredential cred, ref ManagementConnectionType excludedTypes)
        {
            try
            {
                WriteMessageAtLevel(
                    String.Format("[{0}] Accessing computer using PowerShell Remoting", computer),
                    MessageLevel.Verbose,
                    null);

                string scriptText = String.Format("Get-WmiObject -Class {0} -ErrorAction Stop", ClassName);
                if (TestBound("Namespace"))
                {
                    scriptText += String.Format(" -Namespace {0}", Namespace);
                }

                string invokeScript;
                List<object> args = new List<object>();
                if (Credential != null)
                {
                    invokeScript = "param($sb, $comp, $crd) Invoke-Command2 -ScriptBlock $sb -ComputerName $comp -Credential $crd -Raw";
                    args.Add(ScriptBlock.Create(scriptText));
                    args.Add(computer);
                    args.Add(Credential);
                }
                else
                {
                    invokeScript = "param($sb, $comp) Invoke-Command2 -ScriptBlock $sb -ComputerName $comp -Raw";
                    args.Add(ScriptBlock.Create(scriptText));
                    args.Add(computer);
                }

                var results = InvokeCommand.InvokeScript(
                    false,
                    ScriptBlock.Create(invokeScript),
                    null,
                    args.ToArray());

                if (results != null)
                {
                    foreach (PSObject obj in results)
                    {
                        WriteObject(obj);
                    }
                }

                WriteMessageAtLevel(
                    String.Format("[{0}] Accessing computer using PowerShell Remoting - Success", computer),
                    MessageLevel.Verbose,
                    null);
                connection.ReportSuccess(ManagementConnectionType.PowerShellRemoting);
                connection.AddGoodCredential(cred);
                if (!_disableCache)
                    ConnectionHost.Connections[computer] = connection;

                return true;
            }
            catch
            {
                // Will always consider authenticated, since any call with credentials to a server
                // that doesn't exist will also carry invalid credentials error.
                connection.ReportFailure(ManagementConnectionType.PowerShellRemoting);
                excludedTypes = excludedTypes | ManagementConnectionType.PowerShellRemoting;
                return false;
            }
        }

        #endregion Protocol Handlers

        #region CIM Error Resolution

        /// <summary>
        /// Resolves a CIM error into a structured error info object, determining whether
        /// the error indicates bad credentials, a bad connection, or a specific CIM error code.
        /// </summary>
        internal static CimErrorInfo ResolveCimError(Exception exception, string computerName, string className, string ns, string query)
        {
            if (!String.IsNullOrEmpty(query))
            {
                Match m = Regex.Match(query, @"from\s+(\S+)", RegexOptions.IgnoreCase);
                if (m.Success)
                    className = m.Groups[1].Value;
            }

            bool badConnection = false;
            bool badCredentials = false;
            int code = 0;
            string message = null;

            // Try to extract CIM status code
            CimException cimEx = FindCimException(exception);
            if (cimEx != null)
            {
                code = (int)cimEx.StatusCode;
            }

            message = GetCimErrorMessage(code, computerName, className, ns);

            // Handle code 1 - generic runtime error with HRESULT sub-codes
            if (code == 1 && cimEx != null)
            {
                string messageId = cimEx.MessageId;
                if (messageId == "HRESULT 0x8007052e" || messageId == "HRESULT 0x80070005")
                {
                    badCredentials = true;
                    message = String.Format("[{0}] Invalid connection credentials", computerName);
                }
                else if (messageId == "HRESULT 0x80041013")
                {
                    message = String.Format("[{0}] Failed to access {1} in namespace {2}", computerName, className, ns);
                }
                else if (messageId == "HRESULT 0x8004100e")
                {
                    message = String.Format("[{0}] Invalid namespace: {1}", computerName, ns);
                    code = 3;
                }
                else if (messageId == "HRESULT 0x80041010")
                {
                    message = String.Format("[{0}] Invalid class name ({1}), not found in current namespace ({2})", computerName, className, ns);
                    code = 5;
                }
                else
                {
                    badConnection = true;
                }
            }

            // Handle unknown/non-CIM codes
            if (code < 1 || code > 20)
            {
                if (cimEx != null && cimEx.ErrorData != null)
                {
                    try
                    {
                        string originalError = null;
                        foreach (CimProperty prop in cimEx.ErrorData.CimInstanceProperties)
                        {
                            if (String.Equals(prop.Name, "original_error", StringComparison.OrdinalIgnoreCase))
                            {
                                originalError = prop.Value as string;
                                break;
                            }
                        }
                        if (originalError != null && originalError.Contains("__ExtendedStatus"))
                        {
                            message = String.Format("[{0}] Something went wrong when looking for {1}, in {2}. This often indicates issues with the target system.", computerName, className, ns);
                        }
                        else
                        {
                            badConnection = true;
                        }
                    }
                    catch
                    {
                        badConnection = true;
                    }
                }
                else
                {
                    badConnection = true;
                }
            }

            return new CimErrorInfo(code, message, badConnection, badCredentials);
        }

        /// <summary>
        /// Returns the CIM error message for a given status code.
        /// </summary>
        internal static string GetCimErrorMessage(int code, string computerName, string className, string ns)
        {
            switch (code)
            {
                case 1: return String.Format("[{0}] An otherwise unexpected error happened.", computerName);
                case 2: return String.Format("[{0}] Access to computer granted, but access to {1}\\{2} denied", computerName, ns, className);
                case 3: return String.Format("[{0}] Invalid namespace: {1}", computerName, ns);
                case 4: return String.Format("[{0}] Invalid parameters were specified", computerName);
                case 5: return String.Format("[{0}] Invalid class name ({1}), not found in current namespace ({2})", computerName, className, ns);
                case 6: return String.Format("[{0}] The requested object of class {1} could not be found", computerName, className);
                case 7: return String.Format("[{0}] The operation against class {1} was not supported. This generally is a serverside WMI Provider issue (That is: It is specific to the application being managed via WMI)", computerName, className);
                case 8: return String.Format("[{0}] The operation against class {1} is refused as long as it contains instances (data)", computerName, className);
                case 9: return String.Format("[{0}] The operation against class {1} is refused as long as it contains instances (data)", computerName, className);
                case 10: return String.Format("[{0}] The operation against class {1} cannot be carried out since the specified superclass does not exist.", computerName, className);
                case 11: return String.Format("[{0}] The specified object in {1} already exists.", computerName, className);
                case 12: return String.Format("[{0}] The specified property does not exist on {1}.", computerName, className);
                case 13: return String.Format("[{0}] The input type is invalid.", computerName);
                case 14: return String.Format("[{0}] Invalid query language. Please check your query string.", computerName);
                case 15: return String.Format("[{0}] Invalid query string. Please check your syntax.", computerName);
                case 16: return String.Format("[{0}] The specified method on {1} is not available.", computerName, className);
                case 17: return String.Format("[{0}] The specified method on {1} does not exist.", computerName, className);
                case 18: return String.Format("[{0}] An unexpected response has happened in this request", computerName);
                case 19: return String.Format("[{0}] The specified destination for this request is invalid.", computerName);
                case 20: return String.Format("[{0}] The specified namespace {1} is not empty.", computerName, ns);
                default: return String.Format("[{0}] An otherwise unexpected error happened.", computerName);
            }
        }

        /// <summary>
        /// Walks the exception chain to find a CimException.
        /// </summary>
        internal static CimException FindCimException(Exception ex)
        {
            Exception current = ex;
            while (current != null)
            {
                if (current is CimException cimEx)
                    return cimEx;
                current = current.InnerException;
            }
            return null;
        }

        /// <summary>
        /// Gets the reason string from a WMI error (checking CategoryInfo.Reason equivalent).
        /// </summary>
        internal static string GetWmiErrorReason(Exception ex)
        {
            if (ex is RuntimeException runtimeEx && runtimeEx.ErrorRecord != null)
            {
                if (runtimeEx.ErrorRecord.CategoryInfo != null)
                    return runtimeEx.ErrorRecord.CategoryInfo.Reason;
            }
            if (ex is UnauthorizedAccessException)
                return "UnauthorizedAccessException";
            if (ex.InnerException is UnauthorizedAccessException)
                return "UnauthorizedAccessException";
            return null;
        }

        /// <summary>
        /// Gets the error category string from a WMI error.
        /// </summary>
        internal static string GetWmiErrorCategory(Exception ex)
        {
            if (ex is RuntimeException runtimeEx && runtimeEx.ErrorRecord != null)
            {
                if (runtimeEx.ErrorRecord.CategoryInfo != null)
                    return runtimeEx.ErrorRecord.CategoryInfo.Category.ToString();
            }
            return null;
        }

        /// <summary>
        /// Checks if the WMI error is a ProviderLoadFailure.
        /// </summary>
        internal static bool IsProviderLoadFailure(Exception ex)
        {
            // Check for ProviderLoadFailure in the exception message or error code
            if (ex.Message != null && ex.Message.Contains("ProviderLoadFailure"))
                return true;
            if (ex.InnerException != null && ex.InnerException.Message != null && ex.InnerException.Message.Contains("ProviderLoadFailure"))
                return true;

            if (ex is RuntimeException runtimeEx && runtimeEx.ErrorRecord != null)
            {
                string msg = runtimeEx.ErrorRecord.Exception != null ? runtimeEx.ErrorRecord.Exception.Message : null;
                if (msg != null && msg.Contains("ProviderLoadFailure"))
                    return true;
            }
            return false;
        }

        #endregion CIM Error Resolution

        #region Utility

        /// <summary>
        /// Writes objects from a CIM result (which may be IEnumerable) to the pipeline.
        /// </summary>
        private void WriteEnumerable(object result)
        {
            if (result == null)
                return;

            IEnumerable enumerable = result as IEnumerable;
            if (enumerable != null)
            {
                foreach (object item in enumerable)
                {
                    WriteObject(item);
                }
            }
            else
            {
                WriteObject(result);
            }
        }

        #endregion Utility

        #region Internal Types

        /// <summary>
        /// Holds the result of CIM error resolution.
        /// </summary>
        internal class CimErrorInfo
        {
            /// <summary>CIM error code</summary>
            public int ErrorCode;

            /// <summary>Human-readable error message</summary>
            public string Message;

            /// <summary>Whether the error indicates a connection problem (should try next protocol)</summary>
            public bool BadConnection;

            /// <summary>Whether the error indicates invalid credentials</summary>
            public bool BadCredentials;

            /// <summary>
            /// Creates a new CimErrorInfo instance.
            /// </summary>
            public CimErrorInfo(int errorCode, string message, bool badConnection, bool badCredentials)
            {
                ErrorCode = errorCode;
                Message = message;
                BadConnection = badConnection;
                BadCredentials = badCredentials;
            }
        }

        #endregion Internal Types
    }
}
