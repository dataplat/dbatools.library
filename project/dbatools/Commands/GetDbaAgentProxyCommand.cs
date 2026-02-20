using System;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Retrieves SQL Server Agent proxy accounts and their associated credentials from target instances.
    /// Proxy accounts allow job steps to execute under different security contexts than the SQL Agent service account.
    /// </summary>
    [Cmdlet("Get", "DbaAgentProxy")]
    [System.Management.Automation.OutputType("Microsoft.SqlServer.Management.Smo.Agent.ProxyAccount")]
    public class GetDbaAgentProxyCommand : DbaInstanceCmdlet
    {
        #region Parameters

        /// <summary>
        /// Specifies which SQL Agent proxy accounts to retrieve by name. Supports wildcards for pattern matching.
        /// </summary>
        [Parameter()]
        public string[] Proxy { get; set; }

        /// <summary>
        /// Specifies which SQL Agent proxy accounts to exclude from results by name. Supports wildcards for pattern matching.
        /// </summary>
        [Parameter()]
        public string[] ExcludeProxy { get; set; }

        #endregion Parameters

        /// <summary>
        /// Default display properties for the output objects.
        /// </summary>
        private static readonly string[] DefaultDisplayProperties = new string[]
        {
            "ComputerName", "SqlInstance", "InstanceName", "Name", "ID",
            "CredentialID", "CredentialIdentity", "CredentialName",
            "Description", "IsEnabled"
        };

        /// <summary>
        /// Connects to each SQL Server instance and retrieves SQL Agent proxy accounts,
        /// applying include/exclude wildcard filters and adding custom properties.
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
                            String.Format("Failed to connect to {0}", instance),
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
                        String.Format("Failure connecting to {0}", instance),
                        errorRecord: new ErrorRecord(ex, "GetDbaAgentProxy_ConnectionError", ErrorCategory.ConnectionError, instance),
                        target: instance,
                        isContinue: true,
                        category: ErrorCategory.ConnectionError);
                    TestFunctionInterrupt();
                    continue;
                }

                WriteMessageAtLevel(
                    String.Format("Getting Edition from {0}", server),
                    MessageLevel.Verbose, null);

                string edition = GetPropertyString(PSObject.AsPSObject(server),"Edition");

                WriteMessageAtLevel(
                    String.Format("{0} is a {1}", server, edition),
                    MessageLevel.Verbose, null);

                if (edition != null && edition.StartsWith("Express", StringComparison.OrdinalIgnoreCase))
                {
                    StopFunction(
                        String.Format("There is no SQL Agent on {0}, it's a {1}", server, edition),
                        target: instance,
                        isContinue: true);
                    TestFunctionInterrupt();
                    continue;
                }

                // Get proxy accounts from JobServer
                Collection<PSObject> proxies;
                try
                {
                    proxies = GetProxyAccounts(server);
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("Failed to retrieve proxy accounts from {0}: {1}", server, ex.Message),
                        exception: ex,
                        target: instance,
                        isContinue: true);
                    TestFunctionInterrupt();
                    continue;
                }

                if (proxies == null || proxies.Count == 0)
                    continue;

                // Apply Proxy include filter (wildcard)
                if (TestBound("Proxy"))
                {
                    proxies = FilterIncludeWildcard(proxies, Proxy);
                }

                // Apply ExcludeProxy filter (wildcard) — can combine with Proxy filter
                if (TestBound("ExcludeProxy"))
                {
                    proxies = FilterExcludeWildcard(proxies, ExcludeProxy);
                }

                // Get server connection info for custom properties
                string computerName = GetPropertyString(PSObject.AsPSObject(server),"ComputerName");
                string serviceName = GetPropertyString(PSObject.AsPSObject(server),"ServiceName");
                string domainInstanceName = GetPropertyString(PSObject.AsPSObject(server),"DomainInstanceName");

                foreach (PSObject px in proxies)
                {
                    if (px == null)
                        continue;

                    AddOrSetProperty(px, "ComputerName", computerName);
                    AddOrSetProperty(px, "InstanceName", serviceName);
                    AddOrSetProperty(px, "SqlInstance", domainInstanceName);

                    SetDefaultDisplayPropertySet(px, DefaultDisplayProperties);

                    WriteObject(px);
                }
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
            // Return the PSObject wrapper, not BaseObject, to preserve NoteProperties
            // added by Connect-DbaInstance (ComputerName, DomainInstanceName, etc.)
            if (results != null && results.Count > 0)
                return results[0];
            return null;
        }

        /// <summary>
        /// Gets the proxy accounts collection from the server's JobServer.
        /// </summary>
        private Collection<PSObject> GetProxyAccounts(object server)
        {
            string script = "param($s) $s.JobServer.ProxyAccounts";
            return InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { server });
        }

        /// <summary>
        /// Filters proxy accounts to include only those whose Name matches any of the specified
        /// wildcard patterns, matching the PS1 -like behavior.
        /// </summary>
        internal static Collection<PSObject> FilterIncludeWildcard(Collection<PSObject> items, string[] patterns)
        {
            if (patterns == null || patterns.Length == 0)
                return items;

            WildcardPattern[] wildcards = new WildcardPattern[patterns.Length];
            for (int i = 0; i < patterns.Length; i++)
            {
                wildcards[i] = new WildcardPattern(patterns[i], WildcardOptions.IgnoreCase);
            }

            Collection<PSObject> result = new Collection<PSObject>();
            foreach (PSObject item in items)
            {
                string name = GetPropertyString(item, "Name");
                if (name == null)
                    continue;
                foreach (WildcardPattern wp in wildcards)
                {
                    if (wp.IsMatch(name))
                    {
                        result.Add(item);
                        break;
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Filters proxy accounts to exclude those whose Name matches any of the specified
        /// wildcard patterns, matching the PS1 -notlike behavior.
        /// </summary>
        internal static Collection<PSObject> FilterExcludeWildcard(Collection<PSObject> items, string[] patterns)
        {
            if (patterns == null || patterns.Length == 0)
                return items;

            WildcardPattern[] wildcards = new WildcardPattern[patterns.Length];
            for (int i = 0; i < patterns.Length; i++)
            {
                wildcards[i] = new WildcardPattern(patterns[i], WildcardOptions.IgnoreCase);
            }

            Collection<PSObject> result = new Collection<PSObject>();
            foreach (PSObject item in items)
            {
                string name = GetPropertyString(item, "Name");
                bool excluded = false;
                if (name != null)
                {
                    foreach (WildcardPattern wp in wildcards)
                    {
                        if (wp.IsMatch(name))
                        {
                            excluded = true;
                            break;
                        }
                    }
                }
                if (!excluded)
                {
                    result.Add(item);
                }
            }
            return result;
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
