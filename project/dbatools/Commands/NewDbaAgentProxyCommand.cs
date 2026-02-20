using System;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Creates SQL Server Agent proxy accounts to enable job steps to run under different security contexts.
    /// Proxy accounts use existing SQL Server credentials and can be assigned to specific subsystems.
    /// </summary>
    [Cmdlet("New", "DbaAgentProxy", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low)]
    [OutputType("Microsoft.SqlServer.Management.Smo.Agent.ProxyAccount")]
    public class NewDbaAgentProxyCommand : DbaInstanceCmdlet
    {
        #region Parameters

        /// <summary>
        /// Specifies the name for the SQL Agent proxy account being created.
        /// </summary>
        [Parameter(Mandatory = true)]
        [ValidateNotNullOrEmpty()]
        public string[] Name { get; set; }

        /// <summary>
        /// Specifies the name of an existing SQL Server credential that the proxy will use.
        /// </summary>
        [Parameter(Mandatory = true)]
        [ValidateNotNullOrEmpty()]
        public string[] ProxyCredential { get; set; }

        /// <summary>
        /// Specifies which SQL Agent subsystems can use this proxy account. Defaults to CmdExec.
        /// </summary>
        [Parameter()]
        [ValidateSet("ActiveScripting", "AnalysisCommand", "AnalysisQuery", "CmdExec", "Distribution", "LogReader", "Merge", "PowerShell", "QueueReader", "Snapshot", "Ssis")]
        public string[] SubSystem { get; set; } = new string[] { "CmdExec" };

        /// <summary>
        /// Provides a text description for the proxy account.
        /// </summary>
        [Parameter()]
        public string Description { get; set; }

        /// <summary>
        /// Specifies which SQL Server logins can use this proxy account.
        /// </summary>
        [Parameter()]
        public string[] Login { get; set; }

        /// <summary>
        /// Specifies which SQL Server fixed server roles can use this proxy account.
        /// </summary>
        [Parameter()]
        public string[] ServerRole { get; set; }

        /// <summary>
        /// Specifies which msdb database roles can use this proxy account.
        /// </summary>
        [Parameter()]
        public string[] MsdbRole { get; set; }

        /// <summary>
        /// Creates the proxy account in a disabled state.
        /// </summary>
        [Parameter()]
        public SwitchParameter Disabled { get; set; }

        /// <summary>
        /// Drops and recreates the proxy account if one with the same name already exists.
        /// </summary>
        [Parameter()]
        public SwitchParameter Force { get; set; }

        #endregion Parameters

        /// <summary>
        /// Default display properties for the output objects.
        /// </summary>
        private static readonly string[] DefaultDisplayProperties = new string[]
        {
            "ComputerName", "InstanceName", "SqlInstance", "ID", "Name",
            "CredentialName", "CredentialIdentity", "Description",
            "Logins", "ServerRoles", "MsdbRoles", "SubSystems", "IsEnabled"
        };

        /// <summary>
        /// Suppresses confirmation prompts when Force is used.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();
            if (Force.IsPresent)
            {
                SessionState.PSVariable.Set("ConfirmPreference", "None");
            }
        }

        /// <summary>
        /// Connects to each SQL Server instance and creates the specified proxy accounts.
        /// </summary>
        protected override void ProcessRecord()
        {
            foreach (DbaInstanceParameter instance in SqlInstance)
            {
                object server;
                try
                {
                    server = ConnectInstance(instance, 9);
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
                }
                catch (Exception ex)
                {
                    StopFunction(
                        "Failure",
                        errorRecord: new ErrorRecord(ex, "NewDbaAgentProxy_ConnectionError", ErrorCategory.ConnectionError, instance),
                        target: instance,
                        isContinue: true,
                        category: ErrorCategory.ConnectionError);
                    TestFunctionInterrupt();
                    continue;
                }

                // Check ActiveScripting not supported on SQL 2016+
                int versionMajor = GetVersionMajor(server);
                if (ContainsSubSystem(SubSystem, "ActiveScripting") && versionMajor >= 13)
                {
                    StopFunction(
                        "ActiveScripting (ActiveX script) is not supported in SQL Server 2016 or higher",
                        target: server,
                        isContinue: true);
                    TestFunctionInterrupt();
                    continue;
                }

                // Get JobServer
                object jobServer;
                try
                {
                    jobServer = GetJobServer(server);
                    if (jobServer == null)
                    {
                        StopFunction(
                            "Failure. Is SQL Agent started?",
                            target: instance,
                            isContinue: true,
                            category: ErrorCategory.ConnectionError);
                        TestFunctionInterrupt();
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    StopFunction(
                        "Failure. Is SQL Agent started?",
                        errorRecord: new ErrorRecord(ex, "NewDbaAgentProxy_AgentError", ErrorCategory.ConnectionError, instance),
                        target: instance,
                        isContinue: true,
                        category: ErrorCategory.ConnectionError);
                    TestFunctionInterrupt();
                    continue;
                }

                foreach (string proxyName in Name)
                {
                    ProcessProxy(server, jobServer, instance, proxyName);
                }
            }
        }

        #region Helpers

        /// <summary>
        /// Processes a single proxy creation for a given server and proxy name.
        /// </summary>
        private void ProcessProxy(object server, object jobServer, DbaInstanceParameter instance, string proxyName)
        {
            // Check if proxy already exists
            bool exists = ProxyExists(jobServer, proxyName);

            if (exists)
            {
                if (Force.IsPresent)
                {
                    if (ShouldProcess(instance.ToString(), String.Format("Dropping {0}", proxyName)))
                    {
                        try
                        {
                            DropProxy(jobServer, proxyName);
                            RefreshProxyAccounts(jobServer);
                        }
                        catch (Exception ex)
                        {
                            StopFunction(
                                String.Format("Failed to drop existing proxy {0}", proxyName),
                                errorRecord: new ErrorRecord(ex, "NewDbaAgentProxy_DropError", ErrorCategory.InvalidOperation, instance),
                                target: instance,
                                isContinue: true,
                                category: ErrorCategory.InvalidOperation);
                            TestFunctionInterrupt();
                            return;
                        }
                    }
                }
                else
                {
                    WriteMessageAtLevel(
                        String.Format("Proxy account {0} already exists on {1}. Use -Force to drop and recreate.", proxyName, instance),
                        MessageLevel.Warning, null);
                    return;
                }
            }

            // Check that the credential exists
            if (!CredentialExists(server, ProxyCredential))
            {
                WriteMessageAtLevel(
                    String.Format("Credential '{0}' does not exist on {1}", JoinArray(ProxyCredential), instance),
                    MessageLevel.Warning, null);
                return;
            }

            // Create the proxy object and call Create() inside ShouldProcess
            PSObject proxy = null;
            if (ShouldProcess(instance.ToString(), String.Format("Adding {0} with the {1} credential", proxyName, JoinArray(ProxyCredential))))
            {
                try
                {
                    bool isEnabled = !Disabled.IsPresent;
                    proxy = CreateProxyObject(jobServer, proxyName, ProxyCredential, isEnabled, Description);
                }
                catch (Exception ex)
                {
                    if (IsContainedAgError(ex))
                    {
                        StopFunction(
                            "Cannot create agent proxy through a contained availability group listener. SQL Server Agent objects are instance-level and must be managed on the instance directly. Please connect to the primary replica instead of the listener. Use Get-DbaAvailabilityGroup to find the current primary replica.",
                            exception: ex,
                            target: instance,
                            isContinue: true);
                        TestFunctionInterrupt();
                        return;
                    }
                    // Non-newParent exceptions: rethrow to match PS1 behavior
                    throw;
                }

                // Call Create()
                try
                {
                    InvokeMethod(proxy, "Create");
                }
                catch (Exception ex)
                {
                    StopFunction(
                        "Could not create proxy account",
                        errorRecord: new ErrorRecord(ex, "NewDbaAgentProxy_CreateError", ErrorCategory.InvalidOperation, instance),
                        target: instance,
                        isContinue: true,
                        category: ErrorCategory.InvalidOperation);
                    TestFunctionInterrupt();
                    return;
                }
            }

            // Add logins - outside the outer ShouldProcess to match PS1 behavior
            // Each has its own ShouldProcess guard
            if (proxy != null && Login != null)
            {
                foreach (string loginName in Login)
                {
                    if (LoginExists(server, loginName))
                    {
                        if (ShouldProcess(instance.ToString(), String.Format("Adding login {0} to proxy", loginName)))
                        {
                            InvokeMethodWithArg(proxy, "AddLogin", loginName);
                        }
                    }
                    else
                    {
                        WriteMessageAtLevel(
                            String.Format("Login '{0}' does not exist on {1}", loginName, instance),
                            MessageLevel.Warning, null);
                    }
                }
            }

            // Add server roles
            if (proxy != null && ServerRole != null)
            {
                foreach (string role in ServerRole)
                {
                    if (ServerRoleExists(server, role))
                    {
                        if (ShouldProcess(instance.ToString(), String.Format("Adding server role {0} to proxy", role)))
                        {
                            InvokeMethodWithArg(proxy, "AddServerRole", role);
                        }
                    }
                    else
                    {
                        WriteMessageAtLevel(
                            String.Format("Server Role '{0}' does not exist on {1}", role, instance),
                            MessageLevel.Warning, null);
                    }
                }
            }

            // Add msdb roles
            if (proxy != null && MsdbRole != null)
            {
                foreach (string role in MsdbRole)
                {
                    if (MsdbRoleExists(server, role))
                    {
                        if (ShouldProcess(instance.ToString(), String.Format("Adding msdb role {0} to proxy", role)))
                        {
                            InvokeMethodWithArg(proxy, "AddMsdbRole", role);
                        }
                    }
                    else
                    {
                        WriteMessageAtLevel(
                            String.Format("msdb role '{0}' does not exist on {1}", role, instance),
                            MessageLevel.Warning, null);
                    }
                }
            }

            // Add subsystems
            if (proxy != null)
            {
                foreach (string system in SubSystem)
                {
                    if (ShouldProcess(instance.ToString(), String.Format("Adding subsystem {0} to proxy", system)))
                    {
                        InvokeMethodWithArg(proxy, "AddSubSystem", system);
                    }
                }
            }

            // Alter and refresh, then output
            if (proxy != null && ShouldProcess("console", "Outputting Proxy object"))
            {
                try
                {
                    InvokeMethod(proxy, "Alter");
                    InvokeMethod(proxy, "Refresh");
                }
                catch (Exception ex)
                {
                    WriteMessageAtLevel(
                        String.Format("Warning: Alter/Refresh failed: {0}", ex.Message),
                        MessageLevel.Warning, null);
                }

                // Add custom properties
                string computerName = GetPropertyString(PSObject.AsPSObject(server), "ComputerName");
                string serviceName = GetPropertyString(PSObject.AsPSObject(server), "ServiceName");
                string domainInstanceName = GetPropertyString(PSObject.AsPSObject(server), "DomainInstanceName");

                AddOrSetProperty(proxy, "ComputerName", computerName);
                AddOrSetProperty(proxy, "InstanceName", serviceName);
                AddOrSetProperty(proxy, "SqlInstance", domainInstanceName);

                // Enumerate logins, roles, subsystems
                AddOrSetProperty(proxy, "Logins", EnumProxyCollection(proxy, "EnumLogins"));
                AddOrSetProperty(proxy, "ServerRoles", EnumProxyCollection(proxy, "EnumServerRoles"));
                AddOrSetProperty(proxy, "MsdbRoles", EnumProxyCollection(proxy, "EnumMsdbRoles"));
                AddOrSetProperty(proxy, "SubSystems", EnumProxyCollection(proxy, "EnumSubSystems"));

                SetDefaultDisplayPropertySet(proxy, DefaultDisplayProperties);

                WriteObject(proxy);
            }
        }

        /// <summary>
        /// Checks if a subsystem name is in the array (case-insensitive).
        /// </summary>
        internal static bool ContainsSubSystem(string[] subSystems, string name)
        {
            if (subSystems == null)
                return false;
            foreach (string s in subSystems)
            {
                if (String.Equals(s, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Joins a string array for display purposes.
        /// </summary>
        internal static string JoinArray(string[] values)
        {
            if (values == null || values.Length == 0)
                return String.Empty;
            return String.Join(", ", values);
        }

        /// <summary>
        /// Checks if an exception is a contained AG listener error.
        /// </summary>
        internal static bool IsContainedAgError(Exception ex)
        {
            Exception current = ex;
            while (current != null)
            {
                if (current.Message != null &&
                    current.Message.IndexOf("newParent", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                current = current.InnerException;
            }
            return false;
        }

        /// <summary>
        /// Connects to a SQL Server instance via Connect-DbaInstance with minimum version.
        /// </summary>
        private object ConnectInstance(DbaInstanceParameter instance, int minimumVersion)
        {
            string script;
            object[] args;
            if (SqlCredential != null)
            {
                script = "param($i, $c, $v) Connect-DbaInstance -SqlInstance $i -SqlCredential $c -MinimumVersion $v";
                args = new object[] { instance, SqlCredential, minimumVersion };
            }
            else
            {
                script = "param($i, $v) Connect-DbaInstance -SqlInstance $i -MinimumVersion $v";
                args = new object[] { instance, minimumVersion };
            }

            Collection<PSObject> results = InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, args);
            if (results != null && results.Count > 0)
                return results[0];
            return null;
        }

        /// <summary>
        /// Gets the VersionMajor from the server object.
        /// </summary>
        private int GetVersionMajor(object server)
        {
            try
            {
                string script = "param($s) $s.VersionMajor";
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, ScriptBlock.Create(script), null, new object[] { server });
                if (results != null && results.Count > 0 && results[0] != null)
                {
                    object val = results[0].BaseObject;
                    if (val is int intVal)
                        return intVal;
                    int parsed;
                    if (Int32.TryParse(val.ToString(), out parsed))
                        return parsed;
                }
            }
            catch (Exception)
            {
                // Best effort
            }
            return 0;
        }

        /// <summary>
        /// Gets the JobServer from the server object.
        /// </summary>
        private object GetJobServer(object server)
        {
            string script = "param($s) $s.JobServer";
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, ScriptBlock.Create(script), null, new object[] { server });
            if (results != null && results.Count > 0 && results[0] != null)
                return results[0];
            return null;
        }

        /// <summary>
        /// Checks if a proxy account exists on the job server.
        /// </summary>
        private bool ProxyExists(object jobServer, string proxyName)
        {
            try
            {
                string script = "param($js, $n) $null -ne $js.ProxyAccounts[$n]";
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, ScriptBlock.Create(script), null, new object[] { jobServer, proxyName });
                if (results != null && results.Count > 0 && results[0] != null)
                {
                    object val = results[0].BaseObject;
                    if (val is bool boolVal)
                        return boolVal;
                }
            }
            catch (Exception)
            {
                // Best effort
            }
            return false;
        }

        /// <summary>
        /// Drops a proxy account from the job server.
        /// </summary>
        private void DropProxy(object jobServer, string proxyName)
        {
            string script = "param($js, $n) $js.ProxyAccounts[$n].Drop()";
            InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, new object[] { jobServer, proxyName });
        }

        /// <summary>
        /// Refreshes the proxy accounts collection.
        /// </summary>
        private void RefreshProxyAccounts(object jobServer)
        {
            string script = "param($js) $js.ProxyAccounts.Refresh()";
            InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, new object[] { jobServer });
        }

        /// <summary>
        /// Checks if a credential exists on the server.
        /// </summary>
        private bool CredentialExists(object server, string[] credentialNames)
        {
            try
            {
                // The PS1 checks $server.Credentials[$ProxyCredential] where ProxyCredential is string[]
                // PowerShell indexes with the first element when a string[] is passed as an indexer
                string credName = (credentialNames != null && credentialNames.Length > 0) ? credentialNames[0] : null;
                if (credName == null)
                    return false;
                string script = "param($s, $n) $null -ne $s.Credentials[$n]";
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, ScriptBlock.Create(script), null, new object[] { server, credName });
                if (results != null && results.Count > 0 && results[0] != null)
                {
                    object val = results[0].BaseObject;
                    if (val is bool boolVal)
                        return boolVal;
                }
            }
            catch (Exception)
            {
                // Best effort
            }
            return false;
        }

        /// <summary>
        /// Creates a new ProxyAccount SMO object.
        /// </summary>
        private PSObject CreateProxyObject(object jobServer, string proxyName, string[] credentialNames, bool isEnabled, string description)
        {
            string credName = (credentialNames != null && credentialNames.Length > 0) ? credentialNames[0] : null;
            string script = "param($js, $n, $c, $e, $d) New-Object Microsoft.SqlServer.Management.Smo.Agent.ProxyAccount -ArgumentList $js, $n, $c, $e, $d";
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, ScriptBlock.Create(script), null, new object[] { jobServer, proxyName, credName, isEnabled, description });
            if (results != null && results.Count > 0)
                return results[0];
            return null;
        }

        /// <summary>
        /// Checks if a login exists on the server.
        /// </summary>
        private bool LoginExists(object server, string loginName)
        {
            try
            {
                string script = "param($s, $n) $null -ne $s.Logins[$n]";
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, ScriptBlock.Create(script), null, new object[] { server, loginName });
                if (results != null && results.Count > 0 && results[0] != null)
                {
                    object val = results[0].BaseObject;
                    if (val is bool boolVal)
                        return boolVal;
                }
            }
            catch (Exception)
            {
                // Best effort
            }
            return false;
        }

        /// <summary>
        /// Checks if a server role exists on the server.
        /// </summary>
        private bool ServerRoleExists(object server, string roleName)
        {
            try
            {
                string script = "param($s, $n) $null -ne $s.Roles[$n]";
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, ScriptBlock.Create(script), null, new object[] { server, roleName });
                if (results != null && results.Count > 0 && results[0] != null)
                {
                    object val = results[0].BaseObject;
                    if (val is bool boolVal)
                        return boolVal;
                }
            }
            catch (Exception)
            {
                // Best effort
            }
            return false;
        }

        /// <summary>
        /// Checks if an msdb role exists on the server.
        /// </summary>
        private bool MsdbRoleExists(object server, string roleName)
        {
            try
            {
                string script = "param($s, $n) $null -ne $s.Databases['msdb'].Roles[$n]";
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, ScriptBlock.Create(script), null, new object[] { server, roleName });
                if (results != null && results.Count > 0 && results[0] != null)
                {
                    object val = results[0].BaseObject;
                    if (val is bool boolVal)
                        return boolVal;
                }
            }
            catch (Exception)
            {
                // Best effort
            }
            return false;
        }

        /// <summary>
        /// Invokes a method on a PSObject with no arguments.
        /// </summary>
        private void InvokeMethod(PSObject obj, string methodName)
        {
            string script = String.Format("param($o) $o.{0}()", methodName);
            InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, new object[] { obj });
        }

        /// <summary>
        /// Invokes a method on a PSObject with a single string argument.
        /// Uses AgentSubSystem enum for AddSubSystem method.
        /// </summary>
        private void InvokeMethodWithArg(PSObject obj, string methodName, string arg)
        {
            string script;
            if (String.Equals(methodName, "AddSubSystem", StringComparison.OrdinalIgnoreCase))
            {
                script = "param($o, $a) $o.AddSubSystem([Microsoft.SqlServer.Management.Smo.Agent.AgentSubSystem]::$a)";
            }
            else
            {
                script = String.Format("param($o, $a) $o.{0}($a)", methodName);
            }
            InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, new object[] { obj, arg });
        }

        /// <summary>
        /// Enumerates a proxy collection (EnumLogins, EnumServerRoles, etc.).
        /// </summary>
        private object EnumProxyCollection(PSObject proxy, string methodName)
        {
            try
            {
                // Use comma operator to prevent PowerShell from enumerating the DataTable
                // This preserves the DataTable as a single object, matching PS1 behavior
                string script = String.Format("param($p) ,$p.{0}()", methodName);
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, ScriptBlock.Create(script), null, new object[] { proxy });
                if (results != null && results.Count > 0 && results[0] != null)
                    return results[0].BaseObject;
            }
            catch (Exception)
            {
                // Best effort
            }
            return null;
        }

        /// <summary>
        /// Adds or updates a NoteProperty on a PSObject.
        /// </summary>
        internal static void AddOrSetProperty(PSObject obj, string name, object value)
        {
            if (obj == null)
                return;
            try
            {
                PSPropertyInfo existing = obj.Properties[name];
                if (existing != null)
                {
                    existing.Value = value;
                }
                else
                {
                    obj.Properties.Add(new PSNoteProperty(name, value));
                }
            }
            catch (Exception)
            {
                try
                {
                    obj.Properties.Remove(name);
                    obj.Properties.Add(new PSNoteProperty(name, value));
                }
                catch (Exception)
                {
                    // Best-effort
                }
            }
        }

        /// <summary>
        /// Sets the DefaultDisplayPropertySet on a PSObject.
        /// </summary>
        internal static void SetDefaultDisplayPropertySet(PSObject obj, string[] properties)
        {
            if (obj == null || properties == null)
                return;

            try { obj.Members.Remove("PSStandardMembers"); }
            catch (Exception) { /* May not exist yet */ }

            try
            {
                obj.Members.Add(new PSMemberSet("PSStandardMembers", new PSMemberInfo[]
                {
                    new PSPropertySet("DefaultDisplayPropertySet", properties)
                }));
            }
            catch (Exception)
            {
                // Best-effort
            }
        }

        #endregion Helpers
    }
}
