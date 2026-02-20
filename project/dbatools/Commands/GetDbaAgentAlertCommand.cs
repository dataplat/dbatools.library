using System;
using System.Collections.ObjectModel;
using System.Data;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;
using Dataplat.Dbatools.Utility;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Retrieves SQL Server Agent alert configurations from one or more instances.
    /// Returns alert objects with properties including name, type, severity, message ID,
    /// notification settings, and custom properties like ComputerName, InstanceName,
    /// SqlInstance, Notifications, and LastRaised.
    /// </summary>
    [Cmdlet("Get", "DbaAgentAlert")]
    public class GetDbaAgentAlertCommand : DbaInstanceCmdlet
    {
        #region Parameters

        /// <summary>
        /// Specifies the specific SQL Agent alert names to retrieve from the target instances.
        /// Accepts wildcards for pattern matching.
        /// </summary>
        [Parameter()]
        public string[] Alert { get; set; }

        /// <summary>
        /// Specifies SQL Agent alert names to exclude from the results.
        /// Accepts wildcards for pattern matching.
        /// </summary>
        [Parameter()]
        public string[] ExcludeAlert { get; set; }

        #endregion Parameters

        /// <summary>
        /// Default display properties for the output objects.
        /// </summary>
        private static readonly string[] DefaultDisplayProperties = new string[]
        {
            "ComputerName", "SqlInstance", "InstanceName", "Name", "ID", "JobName",
            "AlertType", "CategoryName", "Severity", "MessageID", "IsEnabled",
            "DelayBetweenResponses", "LastRaised", "OccurrenceCount"
        };

        /// <summary>
        /// Connects to each SQL Server instance and retrieves SQL Agent alerts,
        /// applying include/exclude filters and adding custom properties.
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
                        errorRecord: new ErrorRecord(ex, "GetDbaAgentAlert_ConnectionError", ErrorCategory.ConnectionError, instance),
                        target: instance,
                        isContinue: true,
                        category: ErrorCategory.ConnectionError);
                    TestFunctionInterrupt();
                    continue;
                }

                WriteMessageAtLevel(
                    String.Format("Getting Edition from {0}", server),
                    MessageLevel.Debug, null);

                string edition = GetServerPropertySafe(server, "Edition");

                WriteMessageAtLevel(
                    String.Format("{0} is a {1}", server, edition),
                    MessageLevel.Debug, null);

                if (edition != null && edition.StartsWith("Express", StringComparison.OrdinalIgnoreCase))
                {
                    StopFunction(
                        String.Format("There is no SQL Agent on {0}, it's a {1}", server, edition),
                        target: instance,
                        isContinue: true);
                    TestFunctionInterrupt();
                    continue;
                }

                // Get alerts from JobServer
                Collection<PSObject> alerts;
                try
                {
                    alerts = GetAlerts(server);
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("Failed to retrieve alerts from {0}: {1}", server, ex.Message),
                        exception: ex,
                        target: instance,
                        isContinue: true);
                    TestFunctionInterrupt();
                    continue;
                }

                if (alerts == null || alerts.Count == 0)
                    continue;

                // Apply Alert include filter (wildcard matching)
                if (TestBound("Alert"))
                {
                    alerts = FilterIncludeAlerts(alerts, Alert);
                }

                // Apply ExcludeAlert filter (wildcard matching)
                if (TestBound("ExcludeAlert"))
                {
                    alerts = FilterExcludeAlerts(alerts, ExcludeAlert);
                }

                // Get server connection info for custom properties
                string computerName = GetServerPropertySafe(server, "ComputerName");
                string serviceName = GetServerPropertySafe(server, "ServiceName");
                string domainInstanceName = GetServerPropertySafe(server, "DomainInstanceName");

                // Process each alert
                foreach (PSObject alertObj in alerts)
                {
                    if (alertObj == null)
                        continue;

                    // Get LastOccurrenceDate and convert to DbaDateTime
                    DateTime lastOccurrence = GetAlertDateProperty(alertObj, "LastOccurrenceDate");
                    DbaDateTime lastRaised = new DbaDateTime(lastOccurrence);

                    // Add custom NoteProperties
                    AddOrSetProperty(alertObj, "ComputerName", computerName);
                    AddOrSetProperty(alertObj, "InstanceName", serviceName);
                    AddOrSetProperty(alertObj, "SqlInstance", domainInstanceName);

                    // EnumNotifications
                    DataTable notifications = EnumNotifications(alertObj);
                    AddOrSetProperty(alertObj, "Notifications", notifications);

                    AddOrSetProperty(alertObj, "LastRaised", lastRaised);

                    // Set default display properties
                    SetDefaultDisplayPropertySet(alertObj, DefaultDisplayProperties);

                    WriteObject(alertObj);
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
            if (results != null && results.Count > 0)
                return results[0].BaseObject;
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
        /// Gets the alerts collection from the server's JobServer.
        /// Throws on failure so the caller can surface the error.
        /// </summary>
        private Collection<PSObject> GetAlerts(object server)
        {
            string script = "param($s) $s.JobServer.Alerts";
            return InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { server });
        }

        /// <summary>
        /// Filters alerts to include only those matching the specified wildcard patterns.
        /// </summary>
        internal static Collection<PSObject> FilterIncludeAlerts(Collection<PSObject> alerts, string[] patterns)
        {
            Collection<PSObject> result = new Collection<PSObject>();
            foreach (string pattern in patterns)
            {
                WildcardPattern wp = new WildcardPattern(pattern, WildcardOptions.IgnoreCase);
                foreach (PSObject alert in alerts)
                {
                    string name = GetAlertName(alert);
                    if (name != null && wp.IsMatch(name) && !ContainsAlert(result, alert))
                    {
                        result.Add(alert);
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Filters alerts to exclude those matching the specified wildcard patterns.
        /// </summary>
        internal static Collection<PSObject> FilterExcludeAlerts(Collection<PSObject> alerts, string[] patterns)
        {
            // Pre-compile wildcard patterns to avoid per-alert allocation
            WildcardPattern[] compiled = new WildcardPattern[patterns.Length];
            for (int i = 0; i < patterns.Length; i++)
            {
                compiled[i] = new WildcardPattern(patterns[i], WildcardOptions.IgnoreCase);
            }

            Collection<PSObject> result = new Collection<PSObject>();
            foreach (PSObject alert in alerts)
            {
                string name = GetAlertName(alert);
                bool excluded = false;
                foreach (WildcardPattern wp in compiled)
                {
                    if (name != null && wp.IsMatch(name))
                    {
                        excluded = true;
                        break;
                    }
                }
                if (!excluded)
                {
                    result.Add(alert);
                }
            }
            return result;
        }

        /// <summary>
        /// Gets the Name property from an alert PSObject.
        /// </summary>
        internal static string GetAlertName(PSObject alert)
        {
            if (alert == null)
                return null;
            try
            {
                PSPropertyInfo prop = alert.Properties["Name"];
                if (prop != null && prop.Value != null)
                    return prop.Value.ToString();
            }
            catch (Exception)
            {
                // Ignore
            }
            return null;
        }

        /// <summary>
        /// Checks if the collection already contains the specified alert by reference identity.
        /// Reference equality is correct here because PSObjects from the same SMO collection
        /// are unique instances and we want to detect duplicates within a single session.
        /// </summary>
        private static bool ContainsAlert(Collection<PSObject> collection, PSObject alert)
        {
            foreach (PSObject item in collection)
            {
                if (ReferenceEquals(item, alert))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Gets a DateTime property from an alert object.
        /// SMO returns DateTime.MinValue (0001-01-01) for alerts that have never been raised.
        /// DbaDateTime handles this sentinel value and will display it appropriately.
        /// </summary>
        internal static DateTime GetAlertDateProperty(PSObject alert, string propertyName)
        {
            try
            {
                PSPropertyInfo prop = alert.Properties[propertyName];
                if (prop != null && prop.Value is DateTime dt)
                    return dt;
                if (prop != null && prop.Value != null)
                {
                    DateTime parsed;
                    if (DateTime.TryParse(prop.Value.ToString(), out parsed))
                        return parsed;
                }
            }
            catch (Exception)
            {
                // Ignore
            }
            return DateTime.MinValue;
        }

        /// <summary>
        /// Calls EnumNotifications() on the alert SMO object to get notification info.
        /// Uses unary comma operator to prevent PowerShell from enumerating the DataTable
        /// into individual DataRows through the pipeline.
        /// </summary>
        private DataTable EnumNotifications(PSObject alert)
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false,
                    ScriptBlock.Create("param($a) , ($a.EnumNotifications())"),
                    null,
                    new object[] { alert });
                if (results != null && results.Count > 0 && results[0] != null)
                {
                    object baseObj = results[0].BaseObject;
                    if (baseObj is DataTable dt)
                        return dt;
                }
            }
            catch (Exception)
            {
                // Ignore - some alerts may not support EnumNotifications
            }
            return null;
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
