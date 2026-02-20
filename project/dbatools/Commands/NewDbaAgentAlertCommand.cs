using System;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Creates SQL Server Agent alerts for automated monitoring and notification of errors,
    /// performance conditions, or system events. Alerts can automatically notify operators
    /// via email, pager, or net send when triggered, and optionally execute jobs in response.
    /// </summary>
    [Cmdlet("New", "DbaAgentAlert", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low)]
    [OutputType("Microsoft.SqlServer.Management.Smo.Agent.Alert")]
    public class NewDbaAgentAlertCommand : DbaInstanceCmdlet
    {
        #region Parameters

        /// <summary>
        /// Specifies the name for the new SQL Server Agent alert. Must be unique within the instance.
        /// </summary>
        [Parameter(Mandatory = true)]
        [ValidateNotNullOrEmpty()]
        public string Alert { get; set; }

        /// <summary>
        /// Assigns the alert to a specific category for organization and management purposes.
        /// </summary>
        [Parameter()]
        public string Category { get; set; }

        /// <summary>
        /// Restricts the alert to monitor events occurring only in the specified database.
        /// </summary>
        [Parameter()]
        public string Database { get; set; }

        /// <summary>
        /// Specifies which SQL Server Agent operators will receive notifications when this alert fires.
        /// </summary>
        [Parameter()]
        public string[] Operator { get; set; }

        /// <summary>
        /// Sets the minimum time in seconds between alert notifications to prevent notification spam.
        /// Defaults to 60 seconds.
        /// </summary>
        [Parameter()]
        public int DelayBetweenResponses { get; set; } = 60;

        /// <summary>
        /// Creates the alert in a disabled state, preventing it from triggering until manually enabled.
        /// </summary>
        [Parameter()]
        public SwitchParameter Disabled { get; set; }

        /// <summary>
        /// Filters alert triggers to only events containing this keyword in the error message text.
        /// </summary>
        [Parameter()]
        public string EventDescriptionKeyword { get; set; }

        /// <summary>
        /// Identifies the source application or component for WMI event monitoring.
        /// </summary>
        [Parameter()]
        public string EventSource { get; set; }

        /// <summary>
        /// Specifies the GUID of a SQL Server Agent job to automatically execute when the alert fires.
        /// </summary>
        [Parameter()]
        public string JobId { get; set; } = "00000000-0000-0000-0000-000000000000";

        /// <summary>
        /// Sets the SQL Server error severity level (0-25) that triggers this alert.
        /// </summary>
        [Parameter()]
        public int Severity { get; set; }

        /// <summary>
        /// Creates an alert that triggers on a specific SQL Server error message number.
        /// </summary>
        [Parameter()]
        public int MessageId { get; set; }

        /// <summary>
        /// Defines custom text to include in alert notifications sent to operators.
        /// </summary>
        [Parameter()]
        public string NotificationMessage { get; set; }

        /// <summary>
        /// Defines a performance counter condition that triggers the alert when threshold values are exceeded.
        /// </summary>
        [Parameter()]
        public string PerformanceCondition { get; set; }

        /// <summary>
        /// Specifies the WMI namespace to monitor for events.
        /// </summary>
        [Parameter()]
        public string WmiEventNamespace { get; set; }

        /// <summary>
        /// Defines the WQL query that determines which WMI events trigger the alert.
        /// </summary>
        [Parameter()]
        public string WmiEventQuery { get; set; }

        /// <summary>
        /// The method to use to notify operators of the alert.
        /// Valid values are None, NotifyEmail, Pager, NetSend, NotifyAll. Default is NotifyAll.
        /// </summary>
        [Parameter()]
        [ValidateSet("None", "NotifyEmail", "Pager", "NetSend", "NotifyAll")]
        public string NotifyMethod { get; set; } = "NotifyAll";

        #endregion Parameters

        /// <summary>
        /// Connects to each SQL Server instance and creates the specified SQL Agent alert
        /// with all configured properties including notifications.
        /// </summary>
        protected override void ProcessRecord()
        {
            // If MessageId is specified without Severity, set Severity to 0
            if (MessageId > 0 && !TestBound("Severity"))
            {
                Severity = 0;
            }

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
                        errorRecord: new ErrorRecord(ex, "NewDbaAgentAlert_ConnectionError", ErrorCategory.ConnectionError, instance),
                        target: instance,
                        isContinue: true,
                        category: ErrorCategory.ConnectionError);
                    TestFunctionInterrupt();
                    continue;
                }

                // Check if alert already exists
                bool alertExists = CheckAlertExists(server, Alert);
                if (alertExists)
                {
                    StopFunction(
                        String.Format("Alert '{0}' already exists on {1}", Alert, instance),
                        target: instance,
                        isContinue: true);
                    TestFunctionInterrupt();
                }
                else if (ShouldProcess(instance.ToString(), String.Format("Adding the alert {0}", Alert)))
                {
                    try
                    {
                        // Create the alert object
                        PSObject newAlert = CreateAlertObject(server, Alert);
                        if (newAlert == null)
                        {
                            StopFunction(
                                String.Format("Something went wrong creating the alert {0} on {1}", Alert, instance),
                                target: Alert,
                                isContinue: true);
                            TestFunctionInterrupt();
                            continue;
                        }

                        // Set alert properties
                        SetAlertProperties(newAlert);

                        // Call Create()
                        InvokeAlertCreate(newAlert);

                        // Add operator notifications
                        if (Operator != null && Operator.Length > 0 && !String.IsNullOrEmpty(NotifyMethod))
                        {
                            foreach (string op in Operator)
                            {
                                try
                                {
                                    WriteMessageAtLevel(
                                        String.Format("Adding notification of type {0} for {1} to {2}", NotifyMethod, op, instance),
                                        MessageLevel.Verbose, null);

                                    AddNotification(newAlert, op, NotifyMethod);
                                    InvokeAlertAlter(newAlert);
                                }
                                catch (Exception notifyEx)
                                {
                                    StopFunction(
                                        String.Format("Error adding notification of type {0} for {1} to {2}", NotifyMethod, op, instance),
                                        exception: notifyEx,
                                        target: Alert,
                                        isContinue: true);
                                    TestFunctionInterrupt();
                                    continue;
                                }
                            }
                        }

                        // Refresh JobServer
                        RefreshJobServer(server);
                    }
                    catch (Exception ex)
                    {
                        // Check for contained AG listener error
                        if (IsContainedAgError(ex))
                        {
                            StopFunction(
                                "Cannot create agent alert through a contained availability group listener. SQL Server Agent objects are instance-level and must be managed on the instance directly. Please connect to the primary replica instead of the listener. Use Get-DbaAvailabilityGroup to find the current primary replica.",
                                exception: ex,
                                target: instance,
                                isContinue: true);
                            TestFunctionInterrupt();
                            continue;
                        }

                        StopFunction(
                            String.Format("Something went wrong creating the alert {0} on {1}", Alert, instance),
                            exception: ex,
                            target: Alert,
                            isContinue: true);
                        TestFunctionInterrupt();
                        continue;
                    }
                }

                // Output the alert via Get-DbaAgentAlert
                OutputAlert(server, Alert);
            }
        }

        #region Helpers

        /// <summary>
        /// Checks if an exception is a contained availability group listener error.
        /// </summary>
        internal static bool IsContainedAgError(Exception ex)
        {
            if (ex == null || ex.Message == null)
                return false;
            return ex.Message.IndexOf("newParent", StringComparison.OrdinalIgnoreCase) >= 0;
        }

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
        /// Checks if an alert with the given name already exists on the server.
        /// </summary>
        private bool CheckAlertExists(object server, string alertName)
        {
            string script = "param($s, $n) $n -in $s.JobServer.Alerts.Name";
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, ScriptBlock.Create(script), null, new object[] { server, alertName });
            if (results != null && results.Count > 0 && results[0] != null)
            {
                object val = results[0].BaseObject;
                if (val is bool boolVal)
                    return boolVal;
            }
            return false;
        }

        /// <summary>
        /// Creates a new SMO Agent Alert object.
        /// </summary>
        private PSObject CreateAlertObject(object server, string alertName)
        {
            string script = "param($s, $n) New-Object Microsoft.SqlServer.Management.Smo.Agent.Alert($s.JobServer, $n)";
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, ScriptBlock.Create(script), null, new object[] { server, alertName });
            if (results != null && results.Count > 0)
                return results[0];
            return null;
        }

        /// <summary>
        /// Sets all configured properties on the alert object.
        /// Mirrors the PS1 loop that sets properties dynamically.
        /// </summary>
        private void SetAlertProperties(PSObject alert)
        {
            // CategoryName
            if (!String.IsNullOrEmpty(Category))
            {
                SetProperty(alert, "CategoryName", Category);
            }

            // DatabaseName
            if (!String.IsNullOrEmpty(Database))
            {
                SetProperty(alert, "DatabaseName", Database);
            }

            // DelayBetweenResponses
            SetProperty(alert, "DelayBetweenResponses", DelayBetweenResponses);

            // EventDescriptionKeyword
            if (!String.IsNullOrEmpty(EventDescriptionKeyword))
            {
                SetProperty(alert, "EventDescriptionKeyword", EventDescriptionKeyword);
            }

            // EventSource
            if (!String.IsNullOrEmpty(EventSource))
            {
                SetProperty(alert, "EventSource", EventSource);
            }

            // JobID
            if (!String.IsNullOrEmpty(JobId))
            {
                Guid jobGuid;
                if (Guid.TryParse(JobId, out jobGuid))
                {
                    SetProperty(alert, "JobID", jobGuid);
                }
            }

            // MessageID
            if (MessageId > 0)
            {
                SetProperty(alert, "MessageID", MessageId);
            }

            // NotificationMessage
            if (!String.IsNullOrEmpty(NotificationMessage))
            {
                SetProperty(alert, "NotificationMessage", NotificationMessage);
            }

            // PerformanceCondition
            if (!String.IsNullOrEmpty(PerformanceCondition))
            {
                SetProperty(alert, "PerformanceCondition", PerformanceCondition);
            }

            // WmiEventNamespace
            if (!String.IsNullOrEmpty(WmiEventNamespace))
            {
                SetProperty(alert, "WmiEventNamespace", WmiEventNamespace);
            }

            // WmiEventQuery
            if (!String.IsNullOrEmpty(WmiEventQuery))
            {
                SetProperty(alert, "WmiEventQuery", WmiEventQuery);
            }

            // IncludeEventDescription (mapped from NotifyMethod)
            if (!String.IsNullOrEmpty(NotifyMethod))
            {
                SetPropertyViaScript(alert, "IncludeEventDescription",
                    String.Format("[Microsoft.SqlServer.Management.Smo.Agent.NotifyMethods]::{0}", NotifyMethod));
            }

            // IsEnabled (inverse of Disabled switch)
            if (Disabled.IsPresent)
            {
                SetProperty(alert, "IsEnabled", false);
            }

            // Severity
            if (Severity > 0 || TestBound("Severity"))
            {
                SetProperty(alert, "Severity", Severity);
            }
        }

        /// <summary>
        /// Sets a property on a PSObject to a value.
        /// </summary>
        private void SetProperty(PSObject obj, string propertyName, object value)
        {
            string script = String.Format("param($o, $v) $o.{0} = $v", propertyName);
            InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { obj, value });
        }

        /// <summary>
        /// Sets a property via a script expression (for enum conversions).
        /// </summary>
        private void SetPropertyViaScript(PSObject obj, string propertyName, string valueExpression)
        {
            string script = String.Format("param($o) $o.{0} = {1}", propertyName, valueExpression);
            InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { obj });
        }

        /// <summary>
        /// Calls Create() on the alert object.
        /// </summary>
        private void InvokeAlertCreate(PSObject alert)
        {
            string script = "param($a) $a.Create()";
            InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { alert });
        }

        /// <summary>
        /// Calls Alter() on the alert object.
        /// </summary>
        private void InvokeAlertAlter(PSObject alert)
        {
            string script = "param($a) $a.Alter()";
            InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { alert });
        }

        /// <summary>
        /// Adds a notification for an operator with the specified notify method.
        /// </summary>
        private void AddNotification(PSObject alert, string operatorName, string notifyMethod)
        {
            string script = String.Format(
                "param($a, $op) $a.AddNotification($op, [Microsoft.SqlServer.Management.Smo.Agent.NotifyMethods]::{0})",
                notifyMethod);
            InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { alert, operatorName });
        }

        /// <summary>
        /// Refreshes the JobServer to pick up changes.
        /// </summary>
        private void RefreshJobServer(object server)
        {
            try
            {
                string script = "param($s) $null = $s.JobServer.Refresh()";
                InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { server });
            }
            catch (Exception)
            {
                // Best effort refresh
            }
        }

        /// <summary>
        /// Outputs the created alert via Get-DbaAgentAlert.
        /// </summary>
        private void OutputAlert(object server, string alertName)
        {
            try
            {
                string script = "param($s, $n) Get-DbaAgentAlert -SqlInstance $s -Alert $n";
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, ScriptBlock.Create(script), null, new object[] { server, alertName });
                if (results != null)
                {
                    foreach (PSObject result in results)
                    {
                        WriteObject(result);
                    }
                }
            }
            catch (Exception)
            {
                // If Get-DbaAgentAlert fails, don't error - the alert was still created
            }
        }

        #endregion Helpers
    }
}
