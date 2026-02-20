using System;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Text.RegularExpressions;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Creates a new SQL Server Agent operator with notification settings for alerts and job failures.
    /// Supports email, pager, and net send notifications with configurable pager schedules.
    /// </summary>
    [Cmdlet("New", "DbaAgentOperator", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Medium)]
    [OutputType("Microsoft.SqlServer.Management.Smo.Agent.Operator")]
    public class NewDbaAgentOperatorCommand : DbaInstanceCmdlet
    {
        #region Parameters

        /// <summary>
        /// Name of the SQL Server Agent operator to create.
        /// </summary>
        [Parameter(Mandatory = true)]
        [ValidateNotNullOrEmpty()]
        public string Operator { get; set; }

        /// <summary>
        /// Email address where SQL Server Agent sends alert notifications.
        /// </summary>
        [Parameter()]
        public string EmailAddress { get; set; }

        /// <summary>
        /// Network address for receiving net send messages from SQL Server Agent.
        /// </summary>
        [Parameter()]
        public string NetSendAddress { get; set; }

        /// <summary>
        /// Email address for pager notifications.
        /// </summary>
        [Parameter()]
        public string PagerAddress { get; set; }

        /// <summary>
        /// Controls which days pager notifications are active for this operator.
        /// </summary>
        [Parameter()]
        [ValidateSet("EveryDay", "Weekdays", "Weekend", "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday")]
        public string PagerDay { get; set; }

        /// <summary>
        /// Starting time for Saturday pager notifications in HHMMSS format.
        /// </summary>
        [Parameter()]
        public string SaturdayStartTime { get; set; }

        /// <summary>
        /// Ending time for Saturday pager notifications in HHMMSS format.
        /// </summary>
        [Parameter()]
        public string SaturdayEndTime { get; set; }

        /// <summary>
        /// Starting time for Sunday pager notifications in HHMMSS format.
        /// </summary>
        [Parameter()]
        public string SundayStartTime { get; set; }

        /// <summary>
        /// Ending time for Sunday pager notifications in HHMMSS format.
        /// </summary>
        [Parameter()]
        public string SundayEndTime { get; set; }

        /// <summary>
        /// Starting time for weekday pager notifications in HHMMSS format.
        /// </summary>
        [Parameter()]
        public string WeekdayStartTime { get; set; }

        /// <summary>
        /// Ending time for weekday pager notifications in HHMMSS format.
        /// </summary>
        [Parameter()]
        public string WeekdayEndTime { get; set; }

        /// <summary>
        /// Designates this operator as the failsafe operator for the SQL Server instance.
        /// </summary>
        [Parameter()]
        public SwitchParameter IsFailsafeOperator { get; set; }

        /// <summary>
        /// Specifies how the failsafe operator receives notifications.
        /// Defaults to NotifyEmail.
        /// </summary>
        [Parameter()]
        public string FailsafeNotificationMethod { get; set; } = "NotifyEmail";

        /// <summary>
        /// Drops and recreates the operator if it already exists. Also provides default
        /// pager schedule times when not explicitly specified.
        /// </summary>
        [Parameter()]
        public SwitchParameter Force { get; set; }

        /// <summary>
        /// Accepts SQL Server Management Objects (SMO) server instances from Connect-DbaInstance via pipeline.
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        public object[] InputObject { get; set; }

        #endregion Parameters

        // Regex for validating HHMMSS time format
        private static readonly Regex TimeRegex = new Regex(@"^(?:(?:([01]?\d|2[0-3]))?([0-5]?\d))?([0-5]?\d)$", RegexOptions.Compiled);

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
        /// Connects to each SQL Server instance and creates the specified SQL Agent operator
        /// with all configured properties.
        /// </summary>
        protected override void ProcessRecord()
        {
            // Must specify at least one notification address
            if (String.IsNullOrEmpty(EmailAddress) && String.IsNullOrEmpty(NetSendAddress) && String.IsNullOrEmpty(PagerAddress))
            {
                StopFunction("You must specify either an EmailAddress, NetSendAddress, or a PagerAddress to be able to create an operator.");
                return;
            }

            // Normalize PagerDay to canonical ValidateSet casing so that case-insensitive
            // caller input (e.g. "Everyday" vs "EveryDay") resolves correctly.
            // PowerShell ValidateSet accepts values case-insensitively but preserves the
            // caller's original casing in the bound parameter, so we must normalize here.
            string normalizedPagerDay = NormalizePagerDay(PagerDay);

            // Calculate pager day interval
            int interval = CalculatePagerDayInterval(normalizedPagerDay);

            // Validate and default pager times
            string satStartTime = SaturdayStartTime;
            string satEndTime = SaturdayEndTime;
            string sunStartTime = SundayStartTime;
            string sunEndTime = SundayEndTime;
            string wkStartTime = WeekdayStartTime;
            string wkEndTime = WeekdayEndTime;

            // Saturday validation
            if (normalizedPagerDay == "EveryDay" || normalizedPagerDay == "Saturday" || normalizedPagerDay == "Weekend")
            {
                if (!ValidateAndDefaultTime(ref satStartTime, "Saturday Start", Force.IsPresent, "000000"))
                    return;
                if (!ValidateAndDefaultTime(ref satEndTime, "Saturday End", Force.IsPresent, "235959"))
                    return;
            }

            // Sunday validation
            if (normalizedPagerDay == "EveryDay" || normalizedPagerDay == "Sunday" || normalizedPagerDay == "Weekend")
            {
                if (!ValidateAndDefaultTime(ref sunStartTime, "Sunday Start", Force.IsPresent, "000000"))
                    return;
                if (!ValidateAndDefaultTime(ref sunEndTime, "Sunday End", Force.IsPresent, "235959"))
                    return;
            }

            // Weekday validation
            if (normalizedPagerDay == "EveryDay" || normalizedPagerDay == "Weekdays" ||
                normalizedPagerDay == "Monday" || normalizedPagerDay == "Tuesday" || normalizedPagerDay == "Wednesday" ||
                normalizedPagerDay == "Thursday" || normalizedPagerDay == "Friday")
            {
                if (!ValidateAndDefaultTime(ref wkStartTime, "Weekday Start", Force.IsPresent, "000000"))
                    return;
                if (!ValidateAndDefaultTime(ref wkEndTime, "Weekday End", Force.IsPresent, "235959"))
                    return;
            }

            // Validate failsafe notification method
            if (IsFailsafeOperator.IsPresent)
            {
                if (!String.Equals(FailsafeNotificationMethod, "NotifyEmail", StringComparison.OrdinalIgnoreCase) &&
                    !String.Equals(FailsafeNotificationMethod, "NotifyPager", StringComparison.OrdinalIgnoreCase))
                {
                    StopFunction("You must specify a notifiation method for the failsafe operator.");
                    return;
                }
            }

            // Format times from HHMMSS to HH:MM:SS
            string satStartFormatted = FormatTime(satStartTime);
            string satEndFormatted = FormatTime(satEndTime);
            string sunStartFormatted = FormatTime(sunStartTime);
            string sunEndFormatted = FormatTime(sunEndTime);
            string wkStartFormatted = FormatTime(wkStartTime);
            string wkEndFormatted = FormatTime(wkEndTime);

            // Process InputObject (pre-connected server objects from pipeline)
            if (InputObject != null)
            {
                foreach (object obj in InputObject)
                {
                    ProcessServer(obj, interval, satStartFormatted, satEndFormatted, sunStartFormatted, sunEndFormatted, wkStartFormatted, wkEndFormatted);
                    TestFunctionInterrupt();
                }
            }

            // Connect via SqlInstance parameters
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
                        errorRecord: new ErrorRecord(ex, "NewDbaAgentOperator_ConnectionError", ErrorCategory.ConnectionError, instance),
                        target: instance,
                        isContinue: true,
                        category: ErrorCategory.ConnectionError);
                    TestFunctionInterrupt();
                    continue;
                }

                ProcessServer(server, interval, satStartFormatted, satEndFormatted, sunStartFormatted, sunEndFormatted, wkStartFormatted, wkEndFormatted);
            }
        }

        #region Helpers

        /// <summary>
        /// Processes a single server: checks for existing operator, drops if Force, creates operator,
        /// sets properties, and outputs the result.
        /// </summary>
        private void ProcessServer(object server, int interval, string satStart, string satEnd, string sunStart, string sunEnd, string wkStart, string wkEnd)
        {
            // Get the failsafe operator name
            string failsafeOperatorName = GetFailsafeOperator(server);

            // Check if operator exists
            bool exists = OperatorExists(server, Operator);

            if (exists)
            {
                if (!Force.IsPresent)
                {
                    if (ShouldProcess(server.ToString(), String.Format("Operator {0} exists at {1}. Use -Force to drop and and create it.", Operator, server)))
                    {
                        WriteMessageAtLevel(
                            String.Format("Operator {0} exists at {1}. Use -Force to drop and create.", Operator, server),
                            MessageLevel.Verbose, null);
                    }
                    return;
                }
                else
                {
                    // If this is the failsafe operator and we're trying to set it as failsafe, skip drop
                    if (String.Equals(failsafeOperatorName, Operator, StringComparison.OrdinalIgnoreCase) && IsFailsafeOperator.IsPresent)
                    {
                        WriteMessageAtLevel(
                            String.Format("{0} is the failsafe operator. Skipping drop.", Operator),
                            MessageLevel.Verbose, null);
                        return;
                    }

                    if (ShouldProcess(server.ToString(), String.Format("Dropping operator {0}", Operator)))
                    {
                        try
                        {
                            WriteMessageAtLevel(
                                String.Format("Dropping Operator {0}", Operator),
                                MessageLevel.Verbose, null);
                            DropOperator(server, Operator);
                        }
                        catch (Exception ex)
                        {
                            StopFunction(
                                "Issue dropping operator",
                                errorRecord: new ErrorRecord(ex, "NewDbaAgentOperator_DropError", ErrorCategory.InvalidOperation, server),
                                target: server,
                                isContinue: true,
                                category: ErrorCategory.InvalidOperation);
                            TestFunctionInterrupt();
                            return;
                        }
                    }
                }
            }

            if (ShouldProcess(server.ToString(), String.Format("Creating Operator {0}", Operator)))
            {
                try
                {
                    PSObject newOperator;
                    try
                    {
                        newOperator = CreateOperatorObject(server, Operator);
                    }
                    catch (Exception ex)
                    {
                        if (IsContainedAgError(ex))
                        {
                            StopFunction(
                                "Cannot create agent operator through a contained availability group listener. SQL Server Agent objects are instance-level and must be managed on the instance directly. Please connect to the primary replica instead of the listener. Use Get-DbaAvailabilityGroup to find the current primary replica.",
                                exception: ex,
                                target: server);
                            return;
                        }
                        throw;
                    }

                    if (newOperator == null)
                    {
                        StopFunction(
                            String.Format("Failed to create operator object for {0}", Operator),
                            target: server,
                            isContinue: true);
                        TestFunctionInterrupt();
                        return;
                    }

                    // Set properties
                    if (!String.IsNullOrEmpty(EmailAddress))
                    {
                        SetProperty(newOperator, "EmailAddress", EmailAddress);
                    }

                    if (!String.IsNullOrEmpty(NetSendAddress))
                    {
                        SetProperty(newOperator, "NetSendAddress", NetSendAddress);
                    }

                    if (!String.IsNullOrEmpty(PagerAddress))
                    {
                        SetProperty(newOperator, "PagerAddress", PagerAddress);
                    }

                    if (interval > 0)
                    {
                        SetProperty(newOperator, "PagerDays", interval);
                    }

                    if (!String.IsNullOrEmpty(satStart))
                    {
                        SetProperty(newOperator, "SaturdayPagerStartTime", satStart);
                    }

                    if (!String.IsNullOrEmpty(satEnd))
                    {
                        SetProperty(newOperator, "SaturdayPagerEndTime", satEnd);
                    }

                    if (!String.IsNullOrEmpty(sunStart))
                    {
                        SetProperty(newOperator, "SundayPagerStartTime", sunStart);
                    }

                    if (!String.IsNullOrEmpty(sunEnd))
                    {
                        SetProperty(newOperator, "SundayPagerEndTime", sunEnd);
                    }

                    if (!String.IsNullOrEmpty(wkStart))
                    {
                        SetProperty(newOperator, "WeekdayPagerStartTime", wkStart);
                    }

                    if (!String.IsNullOrEmpty(wkEnd))
                    {
                        SetProperty(newOperator, "WeekdayPagerEndTime", wkEnd);
                    }

                    // Create the operator
                    InvokeMethod(newOperator, "Create");

                    // Set failsafe operator if requested
                    if (IsFailsafeOperator.IsPresent)
                    {
                        SetFailsafeOperator(server, Operator, FailsafeNotificationMethod);
                    }

                    WriteMessageAtLevel(
                        String.Format("Creating Operator {0}", Operator),
                        MessageLevel.Verbose, null);

                    // Output via Get-DbaAgentOperator
                    OutputOperator(server, Operator);
                }
                catch (Exception ex)
                {
                    StopFunction(
                        "Issue creating operator.",
                        errorRecord: new ErrorRecord(ex, "NewDbaAgentOperator_CreateError", ErrorCategory.InvalidOperation, server),
                        target: server,
                        category: ErrorCategory.InvalidOperation);
                }
            }
        }

        /// <summary>
        /// Normalizes a PagerDay value to canonical ValidateSet casing.
        /// PowerShell ValidateSet accepts values case-insensitively but preserves the
        /// caller's original casing in the bound parameter. This method maps any accepted
        /// variant (e.g. "everyday", "Everyday") to the canonical form (e.g. "EveryDay").
        /// </summary>
        internal static string NormalizePagerDay(string pagerDay)
        {
            if (String.IsNullOrEmpty(pagerDay))
                return pagerDay;

            string lower = pagerDay.ToLowerInvariant();
            switch (lower)
            {
                case "sunday": return "Sunday";
                case "monday": return "Monday";
                case "tuesday": return "Tuesday";
                case "wednesday": return "Wednesday";
                case "thursday": return "Thursday";
                case "friday": return "Friday";
                case "saturday": return "Saturday";
                case "weekdays": return "Weekdays";
                case "weekend": return "Weekend";
                case "everyday": return "EveryDay";
                default: return pagerDay;
            }
        }

        /// <summary>
        /// Calculates the pager day interval bitmask from a day name.
        /// </summary>
        internal static int CalculatePagerDayInterval(string pagerDay)
        {
            if (String.IsNullOrEmpty(pagerDay))
                return 0;

            switch (pagerDay)
            {
                case "Sunday": return 1;
                case "Monday": return 2;
                case "Tuesday": return 4;
                case "Wednesday": return 8;
                case "Thursday": return 16;
                case "Friday": return 32;
                case "Saturday": return 64;
                case "Weekdays": return 62;
                case "Weekend": return 65;
                case "EveryDay": return 127;
                default: return 0;
            }
        }

        /// <summary>
        /// Validates a time parameter and applies default if Force is used.
        /// Returns false if validation fails (StopFunction called).
        /// </summary>
        private bool ValidateAndDefaultTime(ref string time, string label, bool force, string defaultValue)
        {
            if (String.IsNullOrEmpty(time))
            {
                if (force)
                {
                    time = defaultValue;
                    WriteMessageAtLevel(
                        String.Format("{0} time was not set. Force is being used. Setting it to {1}", label, defaultValue),
                        MessageLevel.Verbose, null);
                    return true;
                }
                else
                {
                    StopFunction(String.Format("Please enter {0} time or use -Force to use defaults.", label));
                    return false;
                }
            }
            else if (!IsValidTimeFormat(time))
            {
                StopFunction(String.Format("{0} time {1} needs to match between '000000' and '235959'. Pager Day not set.", label, time));
                return false;
            }
            return true;
        }

        /// <summary>
        /// Validates that a time string matches the HHMMSS format (exactly 6 digits).
        /// </summary>
        internal static bool IsValidTimeFormat(string time)
        {
            if (String.IsNullOrEmpty(time) || time.Length != 6)
                return false;
            return TimeRegex.IsMatch(time);
        }

        /// <summary>
        /// Formats a time string from HHMMSS to HH:MM:SS.
        /// </summary>
        internal static string FormatTime(string time)
        {
            if (String.IsNullOrEmpty(time))
                return null;
            if (time.Length == 6)
                return String.Format("{0}:{1}:{2}", time.Substring(0, 2), time.Substring(2, 2), time.Substring(4, 2));
            return time;
        }

        /// <summary>
        /// Checks if an exception (or any inner exception) is a contained availability group listener error.
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
        /// Checks if an operator with the given name exists on the server.
        /// </summary>
        private bool OperatorExists(object server, string operatorName)
        {
            try
            {
                string script = "param($s, $n) $n -in $s.JobServer.Operators.Name";
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, ScriptBlock.Create(script), null, new object[] { server, operatorName });
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
        /// Gets the failsafe operator name from the server's AlertSystem.
        /// </summary>
        private string GetFailsafeOperator(object server)
        {
            try
            {
                string script = "param($s) $s.JobServer.AlertSystem.FailSafeOperator";
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, ScriptBlock.Create(script), null, new object[] { server });
                if (results != null && results.Count > 0 && results[0] != null)
                    return results[0].BaseObject.ToString();
            }
            catch (Exception)
            {
                // Best effort
            }
            return null;
        }

        /// <summary>
        /// Drops an existing operator.
        /// </summary>
        private void DropOperator(object server, string operatorName)
        {
            string script = "param($s, $n) $s.JobServer.Operators[$n].Drop()";
            InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { server, operatorName });
        }

        /// <summary>
        /// Creates a new SMO Agent Operator object.
        /// </summary>
        private PSObject CreateOperatorObject(object server, string operatorName)
        {
            string script = "param($s, $n) New-Object Microsoft.SqlServer.Management.Smo.Agent.Operator($s.JobServer, $n)";
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, ScriptBlock.Create(script), null, new object[] { server, operatorName });
            if (results != null && results.Count > 0)
                return results[0];
            return null;
        }

        /// <summary>
        /// Sets a property on a PSObject.
        /// </summary>
        private void SetProperty(PSObject obj, string propertyName, object value)
        {
            string script = String.Format("param($o, $v) $o.{0} = $v", propertyName);
            InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { obj, value });
        }

        /// <summary>
        /// Invokes a method on a PSObject.
        /// </summary>
        private void InvokeMethod(PSObject obj, string methodName)
        {
            string script = String.Format("param($o) $o.{0}()", methodName);
            InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { obj });
        }

        /// <summary>
        /// Maps the user-facing FailsafeNotificationMethod value to the correct SMO NotifyMethods enum name.
        /// The PS1 accepted "NotifyPager" but the SMO enum member is "Pager".
        /// </summary>
        internal static string NormalizeNotifyMethod(string method)
        {
            if (String.IsNullOrEmpty(method))
                return method;
            if (String.Equals(method, "NotifyPager", StringComparison.OrdinalIgnoreCase))
                return "Pager";
            return method;
        }

        /// <summary>
        /// Sets the failsafe operator on the server's AlertSystem.
        /// </summary>
        private void SetFailsafeOperator(object server, string operatorName, string notificationMethod)
        {
            string smoMethod = NormalizeNotifyMethod(notificationMethod);
            string script = @"param($s, $n, $m)
$s.JobServer.AlertSystem.FailSafeOperator = $n
$s.JobServer.AlertSystem.NotificationMethod = [Microsoft.SqlServer.Management.Smo.Agent.NotifyMethods]::$m
$s.JobServer.AlertSystem.Alter()";
            InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { server, operatorName, smoMethod });
        }

        /// <summary>
        /// Outputs the created operator via Get-DbaAgentOperator.
        /// </summary>
        private void OutputOperator(object server, string operatorName)
        {
            try
            {
                string script = "param($s, $n) Get-DbaAgentOperator -SqlInstance $s -Operator $n";
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, ScriptBlock.Create(script), null, new object[] { server, operatorName });
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
                // If Get-DbaAgentOperator fails, don't error - the operator was still created
            }
        }

        #endregion Helpers
    }
}
