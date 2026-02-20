using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Retrieves SQL Server Agent alert categories and their associated alert counts
    /// from one or more SQL Server instances.
    /// </summary>
    [Cmdlet("Get", "DbaAgentAlertCategory")]
    public class GetDbaAgentAlertCategoryCommand : DbaInstanceCmdlet
    {
        #region Parameters

        /// <summary>
        /// Specifies one or more alert category names to return from the SQL Server Agent.
        /// Accepts multiple values; wildcards are not supported.
        /// </summary>
        [Parameter()]
        public string[] Category { get; set; }

        #endregion Parameters

        /// <summary>
        /// Default display properties for the output objects.
        /// </summary>
        private static readonly string[] DefaultDisplayProperties = new string[]
        {
            "ComputerName", "InstanceName", "SqlInstance", "Name", "ID", "AlertCount"
        };

        /// <summary>
        /// Connects to each SQL Server instance and retrieves SQL Agent alert categories,
        /// applying the Category filter and adding custom properties including alert counts.
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
                        errorRecord: new ErrorRecord(ex, "GetDbaAgentAlertCategory_ConnectionError", ErrorCategory.ConnectionError, instance),
                        target: instance,
                        isContinue: true,
                        category: ErrorCategory.ConnectionError);
                    TestFunctionInterrupt();
                    continue;
                }

                // Get alert categories from JobServer
                Collection<PSObject> alertCategories;
                try
                {
                    alertCategories = GetAlertCategories(server);
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("Something went wrong getting the alert categories on {0}", instance),
                        exception: ex,
                        target: instance,
                        isContinue: true);
                    TestFunctionInterrupt();
                    continue;
                }

                if (alertCategories == null || alertCategories.Count == 0)
                    continue;

                // Apply Category filter if bound
                if (TestBound("Category"))
                {
                    alertCategories = FilterByCategory(alertCategories, Category);
                }

                // Get alerts for counting
                Collection<PSObject> alerts = null;
                try
                {
                    alerts = GetAlerts(server);
                }
                catch (Exception ex)
                {
                    WriteMessageAtLevel(
                        String.Format("Could not retrieve alerts from {0}. AlertCount will be 0 for all categories. Error: {1}", instance, ex.Message),
                        MessageLevel.Warning,
                        null);
                }

                // Get server connection info for custom properties
                string computerName = GetServerPropertySafe(server, "ComputerName");
                string serviceName = GetServerPropertySafe(server, "ServiceName");
                string domainInstanceName = GetServerPropertySafe(server, "DomainInstanceName");

                foreach (PSObject cat in alertCategories)
                {
                    if (cat == null)
                        continue;

                    string catName = GetPropertySafe(cat, "Name");

                    try
                    {
                        int alertCount = CountAlertsForCategory(alerts, catName);

                        AddOrSetProperty(cat, "ComputerName", computerName);
                        AddOrSetProperty(cat, "InstanceName", serviceName);
                        AddOrSetProperty(cat, "SqlInstance", domainInstanceName);
                        AddOrSetProperty(cat, "AlertCount", alertCount);

                        SetDefaultDisplayPropertySet(cat, DefaultDisplayProperties);

                        WriteObject(cat);
                    }
                    catch (Exception ex)
                    {
                        StopFunction(
                            String.Format("Something went wrong getting the alert category {0} on {1}", catName ?? "(unknown)", instance),
                            exception: ex,
                            target: cat,
                            isContinue: true);
                        TestFunctionInterrupt();
                    }
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
        /// Gets the alert categories collection from the server's JobServer.
        /// </summary>
        private Collection<PSObject> GetAlertCategories(object server)
        {
            string script = "param($s) $s.JobServer.AlertCategories";
            return InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { server });
        }

        /// <summary>
        /// Gets the alerts collection from the server's JobServer.
        /// </summary>
        private Collection<PSObject> GetAlerts(object server)
        {
            string script = "param($s) $s.JobServer.Alerts";
            return InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { server });
        }

        /// <summary>
        /// Filters alert categories by name, matching the PS1 Where-Object { $_.Name -in $Category } behavior.
        /// Uses case-insensitive comparison (PowerShell -in operator is case-insensitive by default).
        /// </summary>
        internal static Collection<PSObject> FilterByCategory(Collection<PSObject> categories, string[] categoryNames)
        {
            Collection<PSObject> result = new Collection<PSObject>();
            HashSet<string> nameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in categoryNames)
            {
                nameSet.Add(name);
            }

            foreach (PSObject cat in categories)
            {
                string catName = GetPropertySafe(cat, "Name");
                if (catName != null && nameSet.Contains(catName))
                {
                    result.Add(cat);
                }
            }
            return result;
        }

        /// <summary>
        /// Counts the number of alerts whose CategoryName matches the specified category name.
        /// Mirrors: ($server.JobServer.Alerts | Where-Object { $_.CategoryName -eq $cat.Name }).Count
        /// </summary>
        internal static int CountAlertsForCategory(Collection<PSObject> alerts, string categoryName)
        {
            if (alerts == null || alerts.Count == 0 || categoryName == null)
                return 0;

            int count = 0;
            foreach (PSObject alert in alerts)
            {
                if (alert == null)
                    continue;
                string alertCatName = GetPropertySafe(alert, "CategoryName");
                if (String.Equals(alertCatName, categoryName, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// Gets a string property from a PSObject safely.
        /// </summary>
        internal static string GetPropertySafe(PSObject obj, string propertyName)
        {
            if (obj == null)
                return null;
            try
            {
                PSPropertyInfo prop = obj.Properties[propertyName];
                if (prop != null && prop.Value != null)
                    return prop.Value.ToString();
            }
            catch (Exception)
            {
                // Property may not exist
            }
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
