using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;
using Dataplat.Dbatools.Utility;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Retrieves SQL Server Agent operators with their notification settings, related jobs,
    /// and related alerts from one or more SQL Server instances.
    /// </summary>
    [Cmdlet("Get", "DbaAgentOperator")]
    [System.Management.Automation.OutputType("Microsoft.SqlServer.Management.Smo.Agent.Operator")]
    public class GetDbaAgentOperatorCommand : DbaInstanceCmdlet
    {
        #region Parameters

        /// <summary>
        /// Specifies which SQL Agent operators to retrieve by name.
        /// Accepts an array of operator names for exact matching.
        /// </summary>
        [Parameter()]
        public object[] Operator { get; set; }

        /// <summary>
        /// Excludes specified SQL Agent operators from the results by name.
        /// Accepts an array of operator names for exact matching.
        /// </summary>
        [Parameter()]
        public object[] ExcludeOperator { get; set; }

        #endregion Parameters

        /// <summary>
        /// Default display properties for the output objects.
        /// </summary>
        private static readonly string[] DefaultDisplayProperties = new string[]
        {
            "ComputerName", "InstanceName", "SqlInstance", "Name", "ID",
            "IsEnabled", "EmailAddress", "LastEmail"
        };

        /// <summary>
        /// Connects to each SQL Server instance and retrieves SQL Agent operators,
        /// applying include/exclude filters and adding custom properties including
        /// related jobs and alerts.
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
                        "Failure",
                        errorRecord: new ErrorRecord(ex, "GetDbaAgentOperator_ConnectionError", ErrorCategory.ConnectionError, instance),
                        target: instance,
                        isContinue: true,
                        category: ErrorCategory.ConnectionError);
                    TestFunctionInterrupt();
                    continue;
                }

                WriteMessageAtLevel(
                    String.Format("Getting Edition from {0}", server),
                    MessageLevel.Verbose, null);

                string edition = GetServerPropertySafe(server, "Edition");

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

                // Get operators from JobServer
                Collection<PSObject> operators;
                try
                {
                    operators = GetOperators(server);
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("Failed to retrieve operators from {0}: {1}", server, ex.Message),
                        exception: ex,
                        target: instance,
                        isContinue: true);
                    TestFunctionInterrupt();
                    continue;
                }

                if (operators == null || operators.Count == 0)
                    continue;

                // PS1 uses if/elseif: Operator and ExcludeOperator are mutually exclusive.
                // If both are supplied, Operator takes precedence and ExcludeOperator is ignored.
                if (TestBound("Operator"))
                {
                    operators = FilterIncludeOperators(operators, Operator);
                }
                else if (TestBound("ExcludeOperator"))
                {
                    operators = FilterExcludeOperators(operators, ExcludeOperator);
                }

                // Get server connection info for custom properties
                string computerName = GetServerPropertySafe(server, "ComputerName");
                string serviceName = GetServerPropertySafe(server, "ServiceName");
                string domainInstanceName = GetServerPropertySafe(server, "DomainInstanceName");

                // Get alerts and jobs for cross-referencing
                Collection<PSObject> alerts = GetAlertsSafe(server);
                Collection<PSObject> jobs = GetJobsSafe(server);

                // Process each operator
                foreach (PSObject operat in operators)
                {
                    if (operat == null)
                        continue;

                    string operName = GetOperatorPropertyString(operat, "Name");

                    // Find related jobs: where OperatorToEmail, OperatorToNetSend, or OperatorToPage matches
                    List<object> relatedJobs = FindRelatedJobs(jobs, operName);

                    // Get LastEmailDate and convert to DbaDateTime
                    DateTime lastEmailDate = GetOperatorDateProperty(operat, "LastEmailDate");
                    DbaDateTime lastEmail = new DbaDateTime(lastEmailDate);

                    // Find related alerts and track alert last email
                    DbaDateTime alertLastEmail = null;
                    List<string> relatedAlerts = FindRelatedAlerts(alerts, operName, out alertLastEmail);

                    // Add custom NoteProperties
                    AddOrSetProperty(operat, "ComputerName", computerName);
                    AddOrSetProperty(operat, "InstanceName", serviceName);
                    AddOrSetProperty(operat, "SqlInstance", domainInstanceName);
                    AddOrSetProperty(operat, "RelatedJobs", relatedJobs.ToArray());
                    AddOrSetProperty(operat, "LastEmail", lastEmail);
                    AddOrSetProperty(operat, "RelatedAlerts", relatedAlerts.ToArray());
                    AddOrSetProperty(operat, "AlertLastEmail", alertLastEmail);

                    // Create IsEnabled as a live alias of the SMO Enabled property,
                    // matching the PS1 "Enabled as IsEnabled" in Select-DefaultView
                    AddAliasProperty(operat, "IsEnabled", "Enabled");

                    // Set default display properties
                    SetDefaultDisplayPropertySet(operat, DefaultDisplayProperties);

                    WriteObject(operat);
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
        /// Gets the operators collection from the server's JobServer.
        /// </summary>
        private Collection<PSObject> GetOperators(object server)
        {
            string script = "param($s) $s.JobServer.Operators";
            return InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { server });
        }

        /// <summary>
        /// Gets the alerts collection from the server's JobServer, returning empty collection on failure.
        /// </summary>
        private Collection<PSObject> GetAlertsSafe(object server)
        {
            try
            {
                string script = "param($s) $s.JobServer.Alerts";
                Collection<PSObject> results = InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { server });
                return results ?? new Collection<PSObject>();
            }
            catch (Exception)
            {
                return new Collection<PSObject>();
            }
        }

        /// <summary>
        /// Gets the jobs collection from the server's JobServer, returning empty collection on failure.
        /// </summary>
        private Collection<PSObject> GetJobsSafe(object server)
        {
            try
            {
                string script = "param($s) $s.JobServer.Jobs";
                Collection<PSObject> results = InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { server });
                return results ?? new Collection<PSObject>();
            }
            catch (Exception)
            {
                return new Collection<PSObject>();
            }
        }

        /// <summary>
        /// Filters operators to include only those whose Name matches one of the specified values (case-insensitive).
        /// </summary>
        internal static Collection<PSObject> FilterIncludeOperators(Collection<PSObject> operators, object[] names)
        {
            string[] stringNames = ConvertToStringArray(names);
            Collection<PSObject> result = new Collection<PSObject>();
            foreach (PSObject op in operators)
            {
                string name = GetOperatorPropertyString(op, "Name");
                if (name != null && IsInArray(name, stringNames))
                {
                    result.Add(op);
                }
            }
            return result;
        }

        /// <summary>
        /// Filters operators to exclude those whose Name matches one of the specified values (case-insensitive).
        /// </summary>
        internal static Collection<PSObject> FilterExcludeOperators(Collection<PSObject> operators, object[] names)
        {
            string[] stringNames = ConvertToStringArray(names);
            Collection<PSObject> result = new Collection<PSObject>();
            foreach (PSObject op in operators)
            {
                string name = GetOperatorPropertyString(op, "Name");
                if (name == null || !IsInArray(name, stringNames))
                {
                    result.Add(op);
                }
            }
            return result;
        }

        /// <summary>
        /// Checks if a string value exists in an array using case-insensitive comparison.
        /// Matches PowerShell's -In operator behavior.
        /// </summary>
        internal static bool IsInArray(string value, string[] array)
        {
            if (value == null || array == null)
                return false;
            foreach (string item in array)
            {
                if (String.Equals(value, item, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Converts an object array to a string array by calling ToString() on each element.
        /// </summary>
        internal static string[] ConvertToStringArray(object[] input)
        {
            if (input == null)
                return new string[0];
            string[] result = new string[input.Length];
            for (int i = 0; i < input.Length; i++)
            {
                result[i] = input[i] != null ? input[i].ToString() : null;
            }
            return result;
        }

        /// <summary>
        /// Gets a string property from an operator PSObject.
        /// </summary>
        internal static string GetOperatorPropertyString(PSObject obj, string propertyName)
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
                // Ignore
            }
            return null;
        }

        /// <summary>
        /// Gets a raw property value from an operator PSObject.
        /// </summary>
        internal static object GetOperatorProperty(PSObject obj, string propertyName)
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
                // Ignore
            }
            return null;
        }

        /// <summary>
        /// Gets a DateTime property from an operator object.
        /// Returns DateTime.MinValue if the property is not found or not a DateTime.
        /// </summary>
        internal static DateTime GetOperatorDateProperty(PSObject obj, string propertyName)
        {
            try
            {
                PSPropertyInfo prop = obj.Properties[propertyName];
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
        /// Finds jobs that notify the specified operator via email, net send, or pager.
        /// Matches the PS1 pattern: Where-Object { $_.OperatorToEmail, $_.OperatorToNetSend, $_.OperatorToPage -contains $operat.Name }
        /// </summary>
        internal static List<object> FindRelatedJobs(Collection<PSObject> jobs, string operatorName)
        {
            List<object> result = new List<object>();
            if (jobs == null || String.IsNullOrEmpty(operatorName))
                return result;

            foreach (PSObject job in jobs)
            {
                if (job == null)
                    continue;

                string toEmail = GetOperatorPropertyString(job, "OperatorToEmail");
                string toNetSend = GetOperatorPropertyString(job, "OperatorToNetSend");
                string toPage = GetOperatorPropertyString(job, "OperatorToPage");

                if (String.Equals(toEmail, operatorName, StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(toNetSend, operatorName, StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(toPage, operatorName, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(job.BaseObject ?? job);
                }
            }
            return result;
        }

        /// <summary>
        /// Finds alerts that notify the specified operator by calling EnumNotifications on each alert.
        /// Also tracks the last alert email date, matching the PS1 behavior where alertlastemail
        /// is set to the LastOccurrenceDate of the last matching alert.
        /// </summary>
        private List<string> FindRelatedAlerts(Collection<PSObject> alerts, string operatorName, out DbaDateTime alertLastEmail)
        {
            // NOTE: Unlike the PS1, we deliberately reset alertLastEmail for each operator.
            // The PS1 had a variable-scoping quirk where $alertlastemail carried over from the
            // previous operator iteration if the current operator had no matching alerts.
            // The C# behavior (null when no alerts match) is correct and intentional.
            alertLastEmail = null;
            List<string> result = new List<string>();
            if (alerts == null || String.IsNullOrEmpty(operatorName))
                return result;

            foreach (PSObject alert in alerts)
            {
                if (alert == null)
                    continue;

                DataTable dtAlert = EnumAlertNotifications(alert, operatorName);
                if (dtAlert != null && dtAlert.Rows.Count > 0)
                {
                    string alertName = GetOperatorPropertyString(alert, "Name");
                    if (alertName != null)
                        result.Add(alertName);

                    DateTime lastOccurrence = GetOperatorDateProperty(alert, "LastOccurrenceDate");
                    alertLastEmail = new DbaDateTime(lastOccurrence);
                }
            }
            return result;
        }

        /// <summary>
        /// Calls EnumNotifications on an alert SMO object with the specified operator name.
        /// Returns the DataTable result or null if the call fails.
        /// </summary>
        private DataTable EnumAlertNotifications(PSObject alert, string operatorName)
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false,
                    ScriptBlock.Create("param($a, $n) , ($a.EnumNotifications($n))"),
                    null,
                    new object[] { alert, operatorName });
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
        /// Adds an AliasProperty on a PSObject, matching Add-Member -MemberType AliasProperty -Force behavior.
        /// This creates a live reference to the underlying property, matching PS1 Select-DefaultView 'X as Y' behavior.
        /// </summary>
        internal static void AddAliasProperty(PSObject obj, string aliasName, string referencedPropertyName)
        {
            if (obj == null)
                return;
            try
            {
                // Remove existing member if present (Force behavior)
                try { obj.Members.Remove(aliasName); }
                catch (Exception) { /* May not exist */ }

                obj.Members.Add(new PSAliasProperty(aliasName, referencedPropertyName));
            }
            catch (Exception)
            {
                // Fallback: add as NoteProperty with snapshot value
                try
                {
                    PSPropertyInfo prop = obj.Properties[referencedPropertyName];
                    if (prop != null)
                    {
                        AddOrSetProperty(obj, aliasName, prop.Value);
                    }
                }
                catch (Exception)
                {
                    // Best-effort
                }
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
