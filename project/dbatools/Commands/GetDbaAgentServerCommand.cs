using System;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Retrieves SQL Server Agent service configuration and status information from one or more instances.
    /// Returns JobServer objects with properties including service state, logging levels, job history settings,
    /// and service accounts.
    /// </summary>
    [Cmdlet("Get", "DbaAgentServer")]
    [System.Management.Automation.OutputType("Microsoft.SqlServer.Management.Smo.Agent.JobServer")]
    public class GetDbaAgentServerCommand : DbaInstanceCmdlet
    {
        /// <summary>
        /// Default display properties for the output objects.
        /// </summary>
        private static readonly string[] DefaultDisplayProperties = new string[]
        {
            "ComputerName", "InstanceName", "SqlInstance",
            "AgentDomainGroup", "AgentLogLevel", "AgentMailType", "AgentShutdownWaitTime",
            "ErrorLogFile", "IdleCpuDuration", "IdleCpuPercentage", "IsCpuPollingEnabled",
            "JobServerType", "LoginTimeout", "JobHistoryIsEnabled", "MaximumHistoryRows",
            "MaximumJobHistoryRows", "MsxAccountCredentialName", "MsxAccountName",
            "MsxServerName", "Name", "NetSendRecipient", "ServiceAccount", "ServiceStartMode",
            "SqlAgentAutoStart", "SqlAgentMailProfile", "SqlAgentRestart", "SqlServerRestart",
            "State", "SysAdminOnly"
        };

        /// <summary>
        /// Connects to each SQL Server instance and retrieves the JobServer (Agent) configuration.
        /// </summary>
        protected override void ProcessRecord()
        {
            foreach (DbaInstanceParameter instance in SqlInstance)
            {
                object server;
                try
                {
                    server = ConnectInstance(instance);
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
                        errorRecord: new ErrorRecord(ex, "GetDbaAgentServer_ConnectionError", ErrorCategory.ConnectionError, instance),
                        target: instance,
                        isContinue: true,
                        category: ErrorCategory.ConnectionError);
                    TestFunctionInterrupt();
                    continue;
                }

                PSObject jobServer;
                try
                {
                    jobServer = GetJobServer(server);
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("Failed to retrieve Agent Server from {0}: {1}", server, ex.Message),
                        exception: ex,
                        target: instance,
                        isContinue: true);
                    TestFunctionInterrupt();
                    continue;
                }

                if (jobServer == null)
                    continue;

                // Add connection info NoteProperties
                string computerName = GetServerPropertySafe(server, "ComputerName");
                string serviceName = GetServerPropertySafe(server, "ServiceName");
                string domainInstanceName = GetServerPropertySafe(server, "DomainInstanceName");

                AddOrSetProperty(jobServer, "ComputerName", computerName);
                AddOrSetProperty(jobServer, "InstanceName", serviceName);
                AddOrSetProperty(jobServer, "SqlInstance", domainInstanceName);

                // Add computed JobHistoryIsEnabled as a ScriptProperty (live, like the PS1)
                AddScriptProperty(jobServer, "JobHistoryIsEnabled",
                    "switch ($this.MaximumHistoryRows) { -1 { $false } default { $true } }");

                // Set default display properties
                SetDefaultDisplayPropertySet(jobServer, DefaultDisplayProperties);

                WriteObject(jobServer);
            }
        }

        #region Helpers

        /// <summary>
        /// Connects to a SQL Server instance via Connect-DbaInstance.
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

            Collection<PSObject> results = InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, args);
            if (results != null && results.Count > 0)
                return results[0];
            return null;
        }

        /// <summary>
        /// Gets a string property from a server object using PSObject property access.
        /// </summary>
        internal static string GetServerPropertySafe(object server, string propertyName)
        {
            if (server == null)
                return null;
            try
            {
                PSObject pso = PSObject.AsPSObject(server);
                PSPropertyInfo prop = pso.Properties[propertyName];
                if (prop != null && prop.Value != null)
                    return prop.Value.ToString();
            }
            catch (Exception)
            {
                // Property may not exist on this object type
            }
            return null;
        }

        /// <summary>
        /// Gets the JobServer object from the server.
        /// </summary>
        private PSObject GetJobServer(object server)
        {
            string script = "param($s) $s.JobServer";
            Collection<PSObject> results = InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { server });
            if (results != null && results.Count > 0 && results[0] != null)
                return results[0];
            return null;
        }

        /// <summary>
        /// Adds a ScriptProperty to a PSObject, removing any existing property with the same name first.
        /// This creates a live computed property that re-evaluates on each access.
        /// </summary>
        internal static void AddScriptProperty(PSObject obj, string name, string getterScript)
        {
            if (obj == null)
                return;
            try
            {
                PSPropertyInfo existing = obj.Properties[name];
                if (existing != null)
                    obj.Properties.Remove(name);
            }
            catch (Exception) { /* May not exist */ }

            try
            {
                obj.Properties.Add(new PSScriptProperty(name, ScriptBlock.Create(getterScript)));
            }
            catch (Exception)
            {
                // Best-effort
            }
        }

        /// <summary>
        /// Adds or updates a NoteProperty on a PSObject, matching Add-Member -Force behavior.
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
                // Force-add by removing then adding
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
        /// Sets the DefaultDisplayPropertySet on a PSObject for formatted output.
        /// Uses remove-before-add pattern for idempotent behavior.
        /// </summary>
        internal static void SetDefaultDisplayPropertySet(PSObject obj, string[] properties)
        {
            if (obj == null || properties == null)
                return;

            // Remove any existing PSStandardMembers first to avoid ExtendedTypeSystemException
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
