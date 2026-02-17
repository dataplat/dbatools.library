using System;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Net.NetworkInformation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Validates SQL Server connectivity and gathers comprehensive connection diagnostics.
    /// Tests SQL Server instance connectivity while collecting detailed connection and environment
    /// information for troubleshooting. Returns authentication details, network configuration,
    /// TCP ports, and local PowerShell environment data.
    /// </summary>
    [Cmdlet("Test", "DbaConnection")]
    public class TestDbaConnectionCommand : DbaBaseCmdlet
    {
        /// <summary>
        /// The target SQL Server instance or instances. Accepts pipeline input.
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        public DbaInstanceParameter[] SqlInstance { get; set; }

        /// <summary>
        /// Windows credentials for computer-level access to the target server.
        /// Required for PSRemoting tests and TCP port detection when running under a different security context.
        /// </summary>
        [Parameter()]
        public PSCredential Credential { get; set; }

        /// <summary>
        /// Login to the target instance using alternative credentials. Accepts PowerShell credentials (Get-Credential).
        /// </summary>
        [Parameter()]
        public PSCredential SqlCredential { get; set; }

        /// <summary>
        /// Skips the PowerShell remoting connectivity test during the connection assessment.
        /// </summary>
        [Parameter()]
        public SwitchParameter SkipPSRemoting { get; set; }

        /// <summary>
        /// Processes each SQL Server instance, gathering comprehensive connection diagnostics.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (SqlInstance == null)
                return;

            foreach (DbaInstanceParameter instance in SqlInstance)
            {
                // Clear loop variables - https://github.com/dataplat/dbatools/issues/9066
                string authType = null;
                object tcpport = null;
                object authscheme = null;

                // Get local environment info
                WriteMessageVerbose("Getting local environment information");
                PSObject localInfo = GetLocalInfo();

                // Resolve network name
                PSObject resolved = null;
                try
                {
                    resolved = ResolveNetworkName(instance);
                }
                catch (Exception ex)
                {
                    StopFunction(
                        "Unable to resolve server information",
                        errorRecord: new ErrorRecord(ex, "TestDbaConnection_ResolveFailed", ErrorCategory.ConnectionError, instance),
                        target: instance,
                        isContinue: true,
                        category: ErrorCategory.ConnectionError);
                    TestFunctionInterrupt();
                    continue;
                }

                // Test PSRemoting
                object remoting = null;
                if (SkipPSRemoting.IsPresent)
                {
                    WriteMessageVerbose("Checking remote access will be skipped");
                }
                else
                {
                    WriteMessageVerbose("Checking remote access");
                    try
                    {
                        remoting = TestPSRemoting(instance);
                    }
                    catch (Exception ex)
                    {
                        remoting = ex;
                    }
                }

                // Test ping
                WriteMessageVerbose(String.Format("Testing ping to {0}", instance.ComputerName));
                bool pingable = TestPing(instance.ComputerName);

                // Test SQL connection
                bool connectSuccess = false;
                string instanceName = null;
                string username = null;
                object server = null;
                object sqlVersion = null;

                try
                {
                    server = ConnectInstance(instance);
                    connectSuccess = true;

                    // Get instance name from server
                    instanceName = GetServerProperty(server, "InstanceName") as string;
                    if (String.IsNullOrEmpty(instanceName))
                    {
                        instanceName = instance.InstanceName;
                    }

                    // Get SQL version
                    sqlVersion = GetServerProperty(server, "Version");

                    // Get connecting user
                    username = GetTrueLogin(server);
                    if (username != null && username.Contains("\\"))
                    {
                        authType = "Windows Authentication";
                    }
                    else
                    {
                        authType = "SQL Authentication";
                    }

                    // TCP Port
                    try
                    {
                        tcpport = GetTcpPort(instance);
                    }
                    catch (Exception ex)
                    {
                        tcpport = new ErrorRecord(ex, "TestDbaConnection_TcpPortError", ErrorCategory.InvalidOperation, instance);
                    }

                    // Auth Scheme
                    try
                    {
                        authscheme = GetAuthScheme(server);
                    }
                    catch (Exception ex)
                    {
                        authscheme = new ErrorRecord(ex, "TestDbaConnection_AuthSchemeError", ErrorCategory.InvalidOperation, server);
                    }
                }
                catch (Exception ex)
                {
                    connectSuccess = false;
                    instanceName = instance.InstanceName;
                    StopFunction(
                        String.Format("Failure for {0}", instance),
                        errorRecord: new ErrorRecord(ex, "TestDbaConnection_ConnectionError", ErrorCategory.ConnectionError, instance),
                        target: instance,
                        category: ErrorCategory.ConnectionError);
                }

                // Build output object
                PSObject result = new PSObject();
                result.Properties.Add(new PSNoteProperty("ComputerName", GetResolvedProperty(resolved, "ComputerName")));
                result.Properties.Add(new PSNoteProperty("InstanceName", instanceName));
                result.Properties.Add(new PSNoteProperty("SqlInstance", instance.FullSmoName));
                result.Properties.Add(new PSNoteProperty("SqlVersion", sqlVersion));
                result.Properties.Add(new PSNoteProperty("ConnectingAsUser", username));
                result.Properties.Add(new PSNoteProperty("ConnectSuccess", connectSuccess));
                result.Properties.Add(new PSNoteProperty("AuthType", authType));
                result.Properties.Add(new PSNoteProperty("AuthScheme", authscheme));
                result.Properties.Add(new PSNoteProperty("TcpPort", tcpport));
                result.Properties.Add(new PSNoteProperty("IPAddress", GetResolvedProperty(resolved, "IPAddress")));
                result.Properties.Add(new PSNoteProperty("NetBiosName", GetResolvedProperty(resolved, "FullComputerName")));
                result.Properties.Add(new PSNoteProperty("IsPingable", pingable));
                result.Properties.Add(new PSNoteProperty("PSRemotingAccessible", remoting));
                result.Properties.Add(new PSNoteProperty("DomainName", GetResolvedProperty(resolved, "Domain")));
                result.Properties.Add(new PSNoteProperty("LocalWindows", GetPSObjectProperty(localInfo, "Windows")));
                result.Properties.Add(new PSNoteProperty("LocalPowerShell", GetPSObjectProperty(localInfo, "PowerShell")));
                result.Properties.Add(new PSNoteProperty("LocalCLR", GetPSObjectProperty(localInfo, "CLR")));
                result.Properties.Add(new PSNoteProperty("LocalSMOVersion", GetPSObjectProperty(localInfo, "SMO")));
                result.Properties.Add(new PSNoteProperty("LocalDomainUser", GetPSObjectProperty(localInfo, "DomainUser")));
                result.Properties.Add(new PSNoteProperty("LocalRunAsAdmin", GetPSObjectProperty(localInfo, "RunAsAdmin")));
                result.Properties.Add(new PSNoteProperty("LocalEdition", GetPSObjectProperty(localInfo, "Edition")));

                WriteObject(result);
            }
        }

        #region Local Info
        /// <summary>
        /// Gathers local environment information (OS, PowerShell, CLR, SMO versions).
        /// </summary>
        internal PSObject GetLocalInfo()
        {
            string script = @"
param()
[PSCustomObject]@{
    Windows    = [environment]::OSVersion.Version.ToString()
    Edition    = $PSVersionTable.PSEdition
    PowerShell = $PSVersionTable.PSVersion.ToString()
    CLR        = [string]$PSVersionTable.CLRVersion
    SMO        = ((([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.FullName -like ""Microsoft.SqlServer.SMO,*"" }).FullName -Split "", "")[1]).TrimStart(""Version="")
    DomainUser = $env:computername -ne $env:USERDOMAIN
    RunAsAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] ""Administrator"")
}
";
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, new object[0]);
                if (results != null && results.Count > 0)
                    return results[0];
            }
            catch (Exception)
            {
                // Fallback: return empty object
            }
            return new PSObject();
        }
        #endregion Local Info

        #region Network Resolution
        /// <summary>
        /// Resolves network name information for the given instance by calling Resolve-DbaNetworkName.
        /// </summary>
        private PSObject ResolveNetworkName(DbaInstanceParameter instance)
        {
            string script;
            object[] args;
            if (Credential != null)
            {
                script = "param($cn, $cred) Resolve-DbaNetworkName -ComputerName $cn -Credential $cred -EnableException";
                args = new object[] { instance.ComputerName, Credential };
            }
            else
            {
                script = "param($cn) Resolve-DbaNetworkName -ComputerName $cn -EnableException";
                args = new object[] { instance.ComputerName };
            }

            Collection<PSObject> results = InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, args);
            if (results != null && results.Count > 0)
                return results[0];
            return null;
        }
        #endregion Network Resolution

        #region PSRemoting
        /// <summary>
        /// Tests PSRemoting access to the target computer by calling Invoke-Command2.
        /// Returns true on success, or the error object on failure.
        /// </summary>
        private object TestPSRemoting(DbaInstanceParameter instance)
        {
            string script;
            object[] args;
            if (Credential != null)
            {
                script = @"
param($cn, $cred)
try {
    $null = Invoke-Command2 -ComputerName $cn -Credential $cred -ScriptBlock { Get-ChildItem } -ErrorAction Stop
    $true
} catch {
    $_
}
";
                args = new object[] { instance.ComputerName, Credential };
            }
            else
            {
                script = @"
param($cn)
try {
    $null = Invoke-Command2 -ComputerName $cn -ScriptBlock { Get-ChildItem } -ErrorAction Stop
    $true
} catch {
    $_
}
";
                args = new object[] { instance.ComputerName };
            }

            Collection<PSObject> results = InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, args);
            if (results != null && results.Count > 0)
            {
                object value = results[0].BaseObject;
                if (value is bool)
                    return value;
                // Return the error record/object
                return results[0];
            }
            return null;
        }
        #endregion PSRemoting

        #region Ping
        /// <summary>
        /// Tests if the target computer responds to ICMP ping.
        /// </summary>
        internal static bool TestPing(string computerName)
        {
            try
            {
                using (Ping ping = new Ping())
                {
                    PingReply reply = ping.Send(computerName, 1000);
                    return reply.Status == IPStatus.Success;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
        #endregion Ping

        #region SQL Connection
        /// <summary>
        /// Connects to the SQL Server instance via Connect-DbaInstance.
        /// </summary>
        private object ConnectInstance(DbaInstanceParameter instance)
        {
            string script;
            object[] args;
            if (SqlCredential != null)
            {
                script = "param($i, $c) Connect-DbaInstance -SqlInstance $i -SqlCredential $c";
                args = new object[] { instance, SqlCredential };
            }
            else
            {
                script = "param($i) Connect-DbaInstance -SqlInstance $i";
                args = new object[] { instance };
            }

            Collection<PSObject> results = InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, args);
            if (results != null && results.Count > 0)
                return results[0].BaseObject;
            return null;
        }

        /// <summary>
        /// Gets the TrueLogin from the server's ConnectionContext.
        /// </summary>
        internal string GetTrueLogin(object server)
        {
            if (server == null)
                return null;

            try
            {
                var connCtx = server.GetType().GetProperty("ConnectionContext");
                if (connCtx != null)
                {
                    object ctx = connCtx.GetValue(server);
                    if (ctx != null)
                    {
                        var loginProp = ctx.GetType().GetProperty("TrueLogin");
                        if (loginProp != null)
                            return loginProp.GetValue(ctx) as string;
                    }
                }
            }
            catch (Exception)
            {
                // Reflection may fail
            }
            return null;
        }

        /// <summary>
        /// Gets a property value from the server object via reflection.
        /// </summary>
        internal object GetServerProperty(object server, string propertyName)
        {
            if (server == null)
                return null;

            try
            {
                // Unwrap PSObject
                if (server is PSObject psObj && psObj.BaseObject != null)
                    server = psObj.BaseObject;

                var prop = server.GetType().GetProperty(propertyName);
                if (prop != null)
                    return prop.GetValue(server);
            }
            catch (Exception)
            {
                // Property may not exist on all server types
            }
            return null;
        }

        /// <summary>
        /// Gets the TCP port for the SQL instance by calling Get-DbaTcpPort.
        /// </summary>
        private object GetTcpPort(DbaInstanceParameter instance)
        {
            string script;
            object[] args;
            if (SqlCredential != null && Credential != null)
            {
                script = "param($i, $sc, $c) (Get-DbaTcpPort -SqlInstance $i -SqlCredential $sc -Credential $c -EnableException).Port";
                args = new object[] { instance, SqlCredential, Credential };
            }
            else if (SqlCredential != null)
            {
                script = "param($i, $sc) (Get-DbaTcpPort -SqlInstance $i -SqlCredential $sc -EnableException).Port";
                args = new object[] { instance, SqlCredential };
            }
            else if (Credential != null)
            {
                script = "param($i, $c) (Get-DbaTcpPort -SqlInstance $i -Credential $c -EnableException).Port";
                args = new object[] { instance, Credential };
            }
            else
            {
                script = "param($i) (Get-DbaTcpPort -SqlInstance $i -EnableException).Port";
                args = new object[] { instance };
            }

            Collection<PSObject> results = InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, args);
            if (results != null && results.Count > 0 && results[0] != null)
                return results[0].BaseObject;
            return null;
        }

        /// <summary>
        /// Gets the authentication scheme by calling Test-DbaConnectionAuthScheme.
        /// </summary>
        private object GetAuthScheme(object server)
        {
            if (server == null)
                return null;

            string script = "param($s) (Test-DbaConnectionAuthScheme -SqlInstance $s -EnableException).AuthScheme";
            Collection<PSObject> results = InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, new object[] { server });
            if (results != null && results.Count > 0 && results[0] != null)
                return results[0].BaseObject;
            return null;
        }
        #endregion SQL Connection

        #region Helpers
        /// <summary>
        /// Gets a property value from a resolved PSObject (from Resolve-DbaNetworkName output).
        /// </summary>
        internal static object GetResolvedProperty(PSObject resolved, string propertyName)
        {
            if (resolved == null)
                return null;

            try
            {
                PSPropertyInfo prop = resolved.Properties[propertyName];
                if (prop != null)
                    return prop.Value;
            }
            catch (Exception)
            {
                // Property may not exist
            }
            return null;
        }

        /// <summary>
        /// Gets a property value from any PSObject by name.
        /// </summary>
        internal static object GetPSObjectProperty(PSObject obj, string propertyName)
        {
            if (obj == null)
                return null;

            try
            {
                PSPropertyInfo prop = obj.Properties[propertyName];
                if (prop != null)
                    return prop.Value;
            }
            catch (Exception)
            {
                // Property may not exist
            }
            return null;
        }
        #endregion Helpers
    }
}
