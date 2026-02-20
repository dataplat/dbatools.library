using System;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Text.RegularExpressions;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Creates a new SQL Server Agent schedule for automated job execution, supporting all
    /// frequency options including one-time, daily, weekly, monthly, and relative monthly.
    /// </summary>
    [Cmdlet("New", "DbaAgentSchedule", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low)]
    [OutputType("Microsoft.SqlServer.Management.Smo.Agent.JobSchedule")]
    public class NewDbaAgentScheduleCommand : DbaInstanceCmdlet
    {
        #region Parameters

        /// <summary>
        /// Specifies existing SQL Server Agent jobs to immediately attach this schedule to after creation.
        /// </summary>
        [Parameter()]
        public object[] Job { get; set; }

        /// <summary>
        /// The name for the new schedule that will appear in SQL Server Agent.
        /// </summary>
        [Parameter()]
        public object Schedule { get; set; }

        /// <summary>
        /// Creates the schedule in a disabled state, preventing any attached jobs from running
        /// until the schedule is manually enabled.
        /// </summary>
        [Parameter()]
        public SwitchParameter Disabled { get; set; }

        /// <summary>
        /// Determines the basic execution pattern for jobs using this schedule.
        /// </summary>
        [Parameter()]
        [ValidateSet("Once", "OneTime", "Daily", "Weekly", "Monthly", "MonthlyRelative",
            "AgentStart", "AutoStart", "IdleComputer", "OnIdle")]
        public object FrequencyType { get; set; }

        /// <summary>
        /// Defines which specific days the job executes based on the FrequencyType selected.
        /// </summary>
        [Parameter()]
        public object[] FrequencyInterval { get; set; }

        /// <summary>
        /// Sets the time interval unit when jobs need to run multiple times per day.
        /// </summary>
        [Parameter()]
        [ValidateSet("Once", "Time", "Seconds", "Second", "Minutes", "Minute", "Hours", "Hour")]
        public object FrequencySubdayType { get; set; }

        /// <summary>
        /// Specifies how often the job repeats within a day when FrequencySubdayType is not Once.
        /// </summary>
        [Parameter()]
        public int FrequencySubdayInterval { get; set; }

        /// <summary>
        /// Determines which occurrence of a day type to use for MonthlyRelative schedules.
        /// </summary>
        [Parameter()]
        [ValidateSet("Unused", "First", "Second", "Third", "Fourth", "Last")]
        public object FrequencyRelativeInterval { get; set; }

        /// <summary>
        /// Controls how many weeks or months to skip between executions.
        /// </summary>
        [Parameter()]
        public int FrequencyRecurrenceFactor { get; set; }

        /// <summary>
        /// Describe common frequencies as text, e.g. "Every 5 minutes" or "Every sunday at 02:00:00".
        /// </summary>
        [Parameter()]
        public string FrequencyText { get; set; }

        /// <summary>
        /// The earliest date this schedule can execute jobs, formatted as yyyyMMdd.
        /// </summary>
        [Parameter()]
        public string StartDate { get; set; }

        /// <summary>
        /// The latest date this schedule can execute jobs, formatted as yyyyMMdd.
        /// </summary>
        [Parameter()]
        public string EndDate { get; set; }

        /// <summary>
        /// The time of day when job execution can begin, formatted as HHmmss.
        /// </summary>
        [Parameter()]
        public string StartTime { get; set; }

        /// <summary>
        /// The time of day when job execution must stop, formatted as HHmmss.
        /// </summary>
        [Parameter()]
        public string EndTime { get; set; }

        /// <summary>
        /// The SQL Server login that owns this schedule.
        /// </summary>
        [Parameter()]
        public string Owner { get; set; }

        /// <summary>
        /// Bypasses parameter validation and applies default values for missing required parameters.
        /// Also removes any existing schedule with the same name before creating the new one.
        /// </summary>
        [Parameter()]
        public SwitchParameter Force { get; set; }

        #endregion Parameters

        // Computed values from BeginProcessing
        private int _frequencyTypeInt;
        private int _frequencySubdayTypeInt;
        private int _frequencyRelativeIntervalInt;
        private int _frequencyRecurrenceFactor;
        private int _interval;
        private DateTime _activeStartDate;
        private DateTime _activeEndDate;
        private TimeSpan _activeStartTimeOfDay;
        private TimeSpan _activeEndTimeOfDay;
        private string _scheduleName;
        private bool _forceEnabled;

        /// <summary>
        /// Validates and converts all schedule parameters before processing instances.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            _forceEnabled = Force.IsPresent;

            if (_forceEnabled)
            {
                try
                {
                    InvokeCommand.InvokeScript(false, ScriptBlock.Create("$ConfirmPreference = 'None'"), null, null);
                }
                catch (Exception)
                {
                    // Best effort
                }
            }

            // Parse FrequencyText if provided
            if (!String.IsNullOrEmpty(FrequencyText))
            {
                if (!ParseFrequencyText(FrequencyText))
                {
                    StopFunction("FrequencyText can not be parsed.");
                    return;
                }
            }

            // Default FrequencyInterval to 1 for Daily when not specified
            string freqTypeStr = FrequencyType != null ? FrequencyType.ToString() : null;
            if (String.Equals(freqTypeStr, "Daily", StringComparison.OrdinalIgnoreCase)
                && (FrequencyInterval == null || FrequencyInterval.Length == 0))
            {
                FrequencyInterval = new object[] { 1 };
            }

            // Schedule name is required
            _scheduleName = Schedule != null ? Schedule.ToString() : null;
            if (String.IsNullOrEmpty(_scheduleName))
            {
                StopFunction("A schedule was not provided! Please provide a schedule name.");
                return;
            }

            // Translate FrequencyType to int
            _frequencyTypeInt = ConvertFrequencyType(freqTypeStr);

            // Translate FrequencySubdayType to int
            string subdayStr = FrequencySubdayType != null ? FrequencySubdayType.ToString() : null;
            _frequencySubdayTypeInt = ConvertFrequencySubdayType(subdayStr);

            // Translate FrequencyRelativeInterval to int
            string relativeStr = FrequencyRelativeInterval != null ? FrequencyRelativeInterval.ToString() : null;
            _frequencyRelativeIntervalInt = ConvertFrequencyRelativeInterval(relativeStr);

            // Store recurrence factor
            _frequencyRecurrenceFactor = FrequencyRecurrenceFactor;

            // Validate daily frequency interval
            if (_frequencyTypeInt == 4 && FrequencyInterval != null && FrequencyInterval.Length > 0)
            {
                string firstVal = FrequencyInterval[0].ToString();
                int intVal;
                bool isInt = int.TryParse(firstVal, out intVal);
                if (isInt && (intVal < 1 || intVal >= 365)
                    && !String.Equals(firstVal, "EveryDay", StringComparison.OrdinalIgnoreCase)
                    && !_forceEnabled)
                {
                    StopFunction("The daily frequency type requires a frequency interval to be between 1 and 365 or 'EveryDay'.", target: SqlInstance);
                    return;
                }
            }

            // Check recurrence factor for weekly or monthly
            if ((_frequencyTypeInt == 16 || _frequencyTypeInt == 8) && _frequencyRecurrenceFactor < 1)
            {
                if (_forceEnabled)
                {
                    _frequencyRecurrenceFactor = 1;
                    WriteMessageAtLevel(
                        String.Format("Recurrence factor not set for weekly or monthly interval. Setting it to {0}.", _frequencyRecurrenceFactor),
                        MessageLevel.Verbose, null);
                }
                else
                {
                    StopFunction(
                        String.Format("The recurrence factor {0} (parameter FrequencyRecurrenceFactor) needs to be at least one when using a weekly or monthly interval.", _frequencyRecurrenceFactor),
                        target: SqlInstance);
                    return;
                }
            }

            // Validate subday interval
            if (!ValidateSubdayInterval())
            {
                return;
            }

            // Calculate the interval value based on frequency type
            _interval = CalculateInterval(_frequencyTypeInt, FrequencyInterval);

            // Validate frequency type is set
            if (_frequencyTypeInt == 0)
            {
                if (_forceEnabled)
                {
                    WriteMessageAtLevel("Parameter FrequencyType must be set to at least [Once]. Setting it to 'Once'.", MessageLevel.Warning, null);
                    _frequencyTypeInt = 1;
                }
                else
                {
                    StopFunction("Parameter FrequencyType must be set to at least [Once]", target: SqlInstance);
                    return;
                }
            }

            // Validate interval for recurring schedules
            if ((_frequencyTypeInt == 4 || _frequencyTypeInt == 8 || _frequencyTypeInt == 32) && _interval < 1)
            {
                if (_forceEnabled)
                {
                    WriteMessageAtLevel("Parameter FrequencyInterval must be provided for a recurring schedule. Setting it to first day of the week.", MessageLevel.Warning, null);
                    _interval = 1;
                }
                else
                {
                    StopFunction("Parameter FrequencyInterval must be provided for a recurring schedule.", target: SqlInstance);
                    return;
                }
            }

            // Parse dates and times
            if (!ParseStartDate()) return;
            if (!ParseEndDate()) return;

            if (_activeEndDate < _activeStartDate)
            {
                StopFunction(String.Format("End date {0} cannot be before start date {1}.", EndDate, StartDate), target: SqlInstance);
                return;
            }

            if (!ParseStartTime()) return;
            if (!ParseEndTime()) return;
        }

        /// <summary>
        /// Connects to each SQL Server instance and creates the schedule with all configured properties.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (TestFunctionInterrupt()) { return; }

            foreach (DbaInstanceParameter instance in SqlInstance)
            {
                object server;
                try
                {
                    server = ConnectInstance(instance);
                    if (server == null)
                    {
                        StopFunction("Failure", target: instance, isContinue: true, category: ErrorCategory.ConnectionError);
                        TestFunctionInterrupt();
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    StopFunction("Failure",
                        errorRecord: new ErrorRecord(ex, "NewDbaAgentSchedule_ConnectionError", ErrorCategory.ConnectionError, instance),
                        target: instance, isContinue: true, category: ErrorCategory.ConnectionError);
                    TestFunctionInterrupt();
                    continue;
                }

                PSObject jobSchedule = null;

                if (ShouldProcess(instance.ToString(), String.Format("Adding the schedule {0}", _scheduleName)))
                {
                    try
                    {
                        WriteMessageAtLevel(
                            String.Format("Adding the schedule {0} on instance {1}", _scheduleName, instance),
                            MessageLevel.Verbose, null);

                        // Create the schedule object
                        try
                        {
                            jobSchedule = CreateScheduleObject(server, _scheduleName);
                            if (jobSchedule == null)
                            {
                                StopFunction("Something went wrong creating the schedule.", target: instance, isContinue: true);
                                TestFunctionInterrupt();
                                continue;
                            }
                        }
                        catch (Exception ex)
                        {
                            if (IsContainedAgError(ex))
                            {
                                StopFunction(
                                    "Cannot create agent schedule through a contained availability group listener. SQL Server Agent objects are instance-level and must be managed on the instance directly. Please connect to the primary replica instead of the listener. Use Get-DbaAvailabilityGroup to find the current primary replica.",
                                    exception: ex, target: instance, isContinue: true);
                            }
                            else
                            {
                                StopFunction("Something went wrong creating the schedule.",
                                    exception: ex, target: instance, isContinue: true);
                            }
                            TestFunctionInterrupt();
                            continue;
                        }

                        // Set schedule properties
                        if (Disabled.IsPresent)
                        {
                            WriteMessageAtLevel("Setting job schedule to disabled", MessageLevel.Verbose, null);
                            SetProperty(jobSchedule, "IsEnabled", false);
                        }
                        else
                        {
                            WriteMessageAtLevel("Setting job schedule to enabled", MessageLevel.Verbose, null);
                            SetProperty(jobSchedule, "IsEnabled", true);
                        }

                        if (_interval >= 1)
                        {
                            WriteMessageAtLevel(
                                String.Format("Setting job schedule frequency interval to {0}", _interval),
                                MessageLevel.Verbose, null);
                            SetProperty(jobSchedule, "FrequencyInterval", _interval);
                        }

                        if (_frequencyTypeInt >= 1)
                        {
                            WriteMessageAtLevel(
                                String.Format("Setting job schedule frequency to {0}", _frequencyTypeInt),
                                MessageLevel.Verbose, null);
                            SetProperty(jobSchedule, "FrequencyTypes", _frequencyTypeInt);
                        }

                        if (_frequencySubdayTypeInt >= 1)
                        {
                            WriteMessageAtLevel(
                                String.Format("Setting job schedule frequency subday type to {0}", _frequencySubdayTypeInt),
                                MessageLevel.Verbose, null);
                            SetProperty(jobSchedule, "FrequencySubDayTypes", _frequencySubdayTypeInt);
                        }

                        if (FrequencySubdayInterval >= 1)
                        {
                            WriteMessageAtLevel(
                                String.Format("Setting job schedule frequency subday interval to {0}", FrequencySubdayInterval),
                                MessageLevel.Verbose, null);
                            SetProperty(jobSchedule, "FrequencySubDayInterval", FrequencySubdayInterval);
                        }

                        if (_frequencyRelativeIntervalInt >= 1 && _frequencyTypeInt == 32)
                        {
                            WriteMessageAtLevel(
                                String.Format("Setting job schedule frequency relative interval to {0}", _frequencyRelativeIntervalInt),
                                MessageLevel.Verbose, null);
                            SetProperty(jobSchedule, "FrequencyRelativeIntervals", _frequencyRelativeIntervalInt);
                        }

                        if (_frequencyRecurrenceFactor >= 1 && (_frequencyTypeInt == 8 || _frequencyTypeInt == 16 || _frequencyTypeInt == 32))
                        {
                            WriteMessageAtLevel(
                                String.Format("Setting job schedule frequency recurrence factor to {0}", _frequencyRecurrenceFactor),
                                MessageLevel.Verbose, null);
                            SetProperty(jobSchedule, "FrequencyRecurrenceFactor", _frequencyRecurrenceFactor);
                        }

                        WriteMessageAtLevel(
                            String.Format("Setting job schedule start date to {0} / {1}", StartDate, _activeStartDate),
                            MessageLevel.Verbose, null);
                        SetProperty(jobSchedule, "ActiveStartDate", _activeStartDate);

                        WriteMessageAtLevel(
                            String.Format("Setting job schedule end date to {0} / {1}", EndDate, _activeEndDate),
                            MessageLevel.Verbose, null);
                        SetProperty(jobSchedule, "ActiveEndDate", _activeEndDate);

                        WriteMessageAtLevel(
                            String.Format("Setting job schedule start time to {0} / {1}", StartTime, _activeStartTimeOfDay),
                            MessageLevel.Verbose, null);
                        SetProperty(jobSchedule, "ActiveStartTimeOfDay", _activeStartTimeOfDay);

                        WriteMessageAtLevel(
                            String.Format("Setting job schedule end time to {0} / {1}", EndTime, _activeEndTimeOfDay),
                            MessageLevel.Verbose, null);
                        SetProperty(jobSchedule, "ActiveEndTimeOfDay", _activeEndTimeOfDay);

                        if (!String.IsNullOrEmpty(Owner))
                        {
                            SetProperty(jobSchedule, "OwnerLoginName", Owner);
                        }

                        InvokeMethod(jobSchedule, "Create");

                        WriteMessageAtLevel(
                            String.Format("Job schedule created with UID {0}", GetPropertyString(PSObject.AsPSObject(jobSchedule), "ScheduleUid")),
                            MessageLevel.Verbose, null);
                    }
                    catch (Exception ex)
                    {
                        StopFunction("Something went wrong adding the schedule.",
                            exception: ex, target: instance, isContinue: true);
                        TestFunctionInterrupt();
                        continue;
                    }

                    // Refresh server objects
                    RefreshServerObjects(server);

                    // Add to TEPP cache
                    try
                    {
                        string teppScript = "param($s, $n) Add-TeppCacheItem -SqlInstance $s -Type schedule -Name $n";
                        InvokeCommand.InvokeScript(false, ScriptBlock.Create(teppScript), null, new object[] { server, _scheduleName });
                    }
                    catch (Exception)
                    {
                        // TEPP cache is non-critical
                    }

                } // ShouldProcess

                // Attach to jobs if specified (outside ShouldProcess to match PS1 behavior,
                // AttachScheduleToJobs has its own per-job ShouldProcess)
                if (Job != null && Job.Length > 0)
                {
                    AttachScheduleToJobs(server, instance, jobSchedule);
                }

                // Output the schedule (outside ShouldProcess to match PS1 behavior)
                if (jobSchedule != null)
                {
                    OutputSchedule(server, jobSchedule);
                }
            } // foreach instance
        }

        /// <summary>
        /// Final verbose message.
        /// </summary>
        protected override void EndProcessing()
        {
            if (TestFunctionInterrupt()) { return; }
            WriteMessageAtLevel("Finished creating job schedule(s).", MessageLevel.Verbose, null);
        }

        #region Helpers - Conversion

        /// <summary>
        /// Converts a FrequencyType string to its integer value.
        /// </summary>
        internal static int ConvertFrequencyType(string frequencyType)
        {
            if (String.IsNullOrEmpty(frequencyType))
                return 0;

            if (String.Equals(frequencyType, "Once", StringComparison.OrdinalIgnoreCase)
                || String.Equals(frequencyType, "OneTime", StringComparison.OrdinalIgnoreCase))
                return 1;
            if (String.Equals(frequencyType, "Daily", StringComparison.OrdinalIgnoreCase))
                return 4;
            if (String.Equals(frequencyType, "Weekly", StringComparison.OrdinalIgnoreCase))
                return 8;
            if (String.Equals(frequencyType, "Monthly", StringComparison.OrdinalIgnoreCase))
                return 16;
            if (String.Equals(frequencyType, "MonthlyRelative", StringComparison.OrdinalIgnoreCase))
                return 32;
            if (String.Equals(frequencyType, "AgentStart", StringComparison.OrdinalIgnoreCase)
                || String.Equals(frequencyType, "AutoStart", StringComparison.OrdinalIgnoreCase))
                return 64;
            if (String.Equals(frequencyType, "IdleComputer", StringComparison.OrdinalIgnoreCase)
                || String.Equals(frequencyType, "OnIdle", StringComparison.OrdinalIgnoreCase))
                return 128;

            return 1;
        }

        /// <summary>
        /// Converts a FrequencySubdayType string to its integer value.
        /// </summary>
        internal static int ConvertFrequencySubdayType(string subdayType)
        {
            if (String.IsNullOrEmpty(subdayType))
                return 1;

            if (String.Equals(subdayType, "Once", StringComparison.OrdinalIgnoreCase)
                || String.Equals(subdayType, "Time", StringComparison.OrdinalIgnoreCase))
                return 1;
            if (String.Equals(subdayType, "Seconds", StringComparison.OrdinalIgnoreCase)
                || String.Equals(subdayType, "Second", StringComparison.OrdinalIgnoreCase))
                return 2;
            if (String.Equals(subdayType, "Minutes", StringComparison.OrdinalIgnoreCase)
                || String.Equals(subdayType, "Minute", StringComparison.OrdinalIgnoreCase))
                return 4;
            if (String.Equals(subdayType, "Hours", StringComparison.OrdinalIgnoreCase)
                || String.Equals(subdayType, "Hour", StringComparison.OrdinalIgnoreCase))
                return 8;

            return 1;
        }

        /// <summary>
        /// Converts a FrequencyRelativeInterval string to its integer value.
        /// </summary>
        internal static int ConvertFrequencyRelativeInterval(string relativeInterval)
        {
            if (String.IsNullOrEmpty(relativeInterval))
                return 0;

            if (String.Equals(relativeInterval, "First", StringComparison.OrdinalIgnoreCase))
                return 1;
            if (String.Equals(relativeInterval, "Second", StringComparison.OrdinalIgnoreCase))
                return 2;
            if (String.Equals(relativeInterval, "Third", StringComparison.OrdinalIgnoreCase))
                return 4;
            if (String.Equals(relativeInterval, "Fourth", StringComparison.OrdinalIgnoreCase))
                return 8;
            if (String.Equals(relativeInterval, "Last", StringComparison.OrdinalIgnoreCase))
                return 16;
            if (String.Equals(relativeInterval, "Unused", StringComparison.OrdinalIgnoreCase))
                return 0;

            return 0;
        }

        /// <summary>
        /// Calculates the interval value based on frequency type and interval parameters.
        /// </summary>
        internal static int CalculateInterval(int frequencyType, object[] frequencyInterval)
        {
            if (frequencyInterval == null || frequencyInterval.Length == 0)
                return 0;

            // Daily (4)
            if (frequencyType == 4)
            {
                int result = 1;
                string firstVal = frequencyInterval[0].ToString();
                int intVal;
                if (int.TryParse(firstVal, out intVal))
                {
                    result = intVal;
                }
                return result;
            }

            // Weekly (8)
            if (frequencyType == 8)
            {
                int result = 0;
                foreach (object item in frequencyInterval)
                {
                    string val = item.ToString();
                    int intVal;

                    if (String.Equals(val, "Sunday", StringComparison.OrdinalIgnoreCase))
                        result += 1;
                    else if (String.Equals(val, "Monday", StringComparison.OrdinalIgnoreCase))
                        result += 2;
                    else if (String.Equals(val, "Tuesday", StringComparison.OrdinalIgnoreCase))
                        result += 4;
                    else if (String.Equals(val, "Wednesday", StringComparison.OrdinalIgnoreCase))
                        result += 8;
                    else if (String.Equals(val, "Thursday", StringComparison.OrdinalIgnoreCase))
                        result += 16;
                    else if (String.Equals(val, "Friday", StringComparison.OrdinalIgnoreCase))
                        result += 32;
                    else if (String.Equals(val, "Saturday", StringComparison.OrdinalIgnoreCase))
                        result += 64;
                    else if (String.Equals(val, "Weekdays", StringComparison.OrdinalIgnoreCase))
                        result = 62;
                    else if (String.Equals(val, "Weekend", StringComparison.OrdinalIgnoreCase))
                        result = 65;
                    else if (String.Equals(val, "EveryDay", StringComparison.OrdinalIgnoreCase))
                        result = 127;
                    else if (int.TryParse(val, out intVal))
                    {
                        // Handle numeric values - special overwrite values
                        if (intVal == 62) result = 62;
                        else if (intVal == 65) result = 65;
                        else if (intVal >= 120 && intVal <= 127) result = intVal;
                        else result += intVal;
                    }
                }
                return result;
            }

            // Monthly (16)
            if (frequencyType == 16)
            {
                int result = 0;
                foreach (object item in frequencyInterval)
                {
                    string val = item.ToString();
                    int intVal;
                    if (int.TryParse(val, out intVal) && intVal >= 1 && intVal <= 31)
                    {
                        result = intVal;
                    }
                }
                return result;
            }

            // MonthlyRelative (32)
            if (frequencyType == 32)
            {
                int result = 0;
                foreach (object item in frequencyInterval)
                {
                    string val = item.ToString();
                    int intVal;

                    if (String.Equals(val, "Sunday", StringComparison.OrdinalIgnoreCase))
                        result += 1;
                    else if (String.Equals(val, "Monday", StringComparison.OrdinalIgnoreCase))
                        result += 2;
                    else if (String.Equals(val, "Tuesday", StringComparison.OrdinalIgnoreCase))
                        result += 3;
                    else if (String.Equals(val, "Wednesday", StringComparison.OrdinalIgnoreCase))
                        result += 4;
                    else if (String.Equals(val, "Thursday", StringComparison.OrdinalIgnoreCase))
                        result += 5;
                    else if (String.Equals(val, "Friday", StringComparison.OrdinalIgnoreCase))
                        result += 6;
                    else if (String.Equals(val, "Saturday", StringComparison.OrdinalIgnoreCase))
                        result += 7;
                    else if (String.Equals(val, "Day", StringComparison.OrdinalIgnoreCase))
                        result += 8;
                    else if (String.Equals(val, "Weekdays", StringComparison.OrdinalIgnoreCase))
                        result += 9;
                    else if (String.Equals(val, "WeekendDay", StringComparison.OrdinalIgnoreCase))
                        result += 10;
                    else if (int.TryParse(val, out intVal) && intVal >= 1 && intVal <= 10)
                    {
                        result += intVal;
                    }
                }
                return result;
            }

            return 0;
        }

        /// <summary>
        /// Parses a date string in yyyyMMdd format to a DateTime.
        /// </summary>
        internal static bool TryParseDate(string dateStr, out DateTime result)
        {
            result = DateTime.MinValue;
            if (String.IsNullOrEmpty(dateStr) || dateStr.Length < 8)
                return false;

            try
            {
                int year = int.Parse(dateStr.Substring(0, 4));
                int month = int.Parse(dateStr.Substring(4, 2));
                int day = int.Parse(dateStr.Substring(6, 2));
                result = new DateTime(year, month, day);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Parses a time string in HHmmss format to a TimeSpan.
        /// </summary>
        internal static bool TryParseTime(string timeStr, out TimeSpan result)
        {
            result = TimeSpan.Zero;
            if (String.IsNullOrEmpty(timeStr) || timeStr.Length < 6)
                return false;

            try
            {
                int hours = int.Parse(timeStr.Substring(0, 2));
                int minutes = int.Parse(timeStr.Substring(2, 2));
                int seconds = int.Parse(timeStr.Substring(4, 2));
                result = new TimeSpan(hours, minutes, seconds);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Parses the FrequencyText parameter using regex and sets related parameters.
        /// Returns true if parsing succeeded.
        /// </summary>
        private bool ParseFrequencyText(string text)
        {
            Regex regex = new Regex(
                @"every(\s+(?<interval>\d+))?\s+(?<unit>minute|hour|day|sunday|monday|tuesday|wednesday|thursday|friday|saturday)s?(\s+starting)?(\s+at\s+(?<start>\d\d:\d\d:\d\d))?",
                RegexOptions.IgnoreCase);

            Match match = regex.Match(text);
            if (!match.Success)
                return false;

            string textInterval = match.Groups["interval"].Success ? match.Groups["interval"].Value : null;
            string textUnit = match.Groups["unit"].Value;
            string textStart = match.Groups["start"].Success ? match.Groups["start"].Value : null;

            int intervalVal = 1;
            if (!String.IsNullOrEmpty(textInterval))
            {
                int.TryParse(textInterval, out intervalVal);
            }

            if (String.Equals(textUnit, "minute", StringComparison.OrdinalIgnoreCase)
                || String.Equals(textUnit, "hour", StringComparison.OrdinalIgnoreCase)
                || String.Equals(textUnit, "day", StringComparison.OrdinalIgnoreCase))
            {
                FrequencyType = "Daily";
                if (String.Equals(textUnit, "minute", StringComparison.OrdinalIgnoreCase)
                    || String.Equals(textUnit, "hour", StringComparison.OrdinalIgnoreCase))
                {
                    FrequencySubdayType = textUnit;
                    FrequencySubdayInterval = intervalVal;
                }
            }
            else
            {
                FrequencyType = "Weekly";
                FrequencyInterval = new object[] { textUnit };
            }

            if (!String.IsNullOrEmpty(textStart))
            {
                StartTime = textStart.Replace(":", "");
            }

            if (Schedule == null || String.IsNullOrEmpty(Schedule.ToString()))
            {
                Schedule = FrequencyText;
            }

            _forceEnabled = true;
            return true;
        }

        #endregion Helpers - Conversion

        #region Helpers - Validation

        private bool ValidateSubdayInterval()
        {
            if (_frequencySubdayTypeInt == 2 && !(FrequencySubdayInterval >= 10 && FrequencySubdayInterval <= 59))
            {
                StopFunction(
                    String.Format("Subday interval {0} must be between 10 and 59 when subday type is 'Seconds'", FrequencySubdayInterval),
                    target: SqlInstance);
                return false;
            }
            if (_frequencySubdayTypeInt == 4 && !(FrequencySubdayInterval >= 1 && FrequencySubdayInterval <= 59))
            {
                StopFunction(
                    String.Format("Subday interval {0} must be between 1 and 59 when subday type is 'Minutes'", FrequencySubdayInterval),
                    target: SqlInstance);
                return false;
            }
            if (_frequencySubdayTypeInt == 8 && !(FrequencySubdayInterval >= 1 && FrequencySubdayInterval <= 23))
            {
                StopFunction(
                    String.Format("Subday interval {0} must be between 1 and 23 when subday type is 'Hours'", FrequencySubdayInterval),
                    target: SqlInstance);
                return false;
            }
            return true;
        }

        private bool ParseStartDate()
        {
            if (String.IsNullOrEmpty(StartDate) && _forceEnabled)
            {
                StartDate = DateTime.Now.ToString("yyyyMMdd");
                WriteMessageAtLevel(
                    String.Format("Start date was not set. Force is being used. Setting it to {0}", StartDate),
                    MessageLevel.Verbose, null);
            }
            else if (String.IsNullOrEmpty(StartDate))
            {
                StopFunction("Please enter a start date or use -Force to use defaults.", target: SqlInstance);
                return false;
            }

            if (!TryParseDate(StartDate, out _activeStartDate))
            {
                StopFunction(
                    String.Format("Start date {0} needs to be a valid date with format yyyyMMdd.", StartDate),
                    target: SqlInstance);
                return false;
            }
            return true;
        }

        private bool ParseEndDate()
        {
            if (String.IsNullOrEmpty(EndDate) && _forceEnabled)
            {
                EndDate = "99991231";
                WriteMessageAtLevel(
                    String.Format("End date was not set. Force is being used. Setting it to {0}", EndDate),
                    MessageLevel.Verbose, null);
            }
            else if (String.IsNullOrEmpty(EndDate))
            {
                StopFunction("Please enter an end date or use -Force to use defaults.", target: SqlInstance);
                return false;
            }

            if (!TryParseDate(EndDate, out _activeEndDate))
            {
                StopFunction(
                    String.Format("End date {0} needs to be a valid date with format yyyyMMdd.", EndDate),
                    target: SqlInstance);
                return false;
            }
            return true;
        }

        private bool ParseStartTime()
        {
            if (String.IsNullOrEmpty(StartTime) && _forceEnabled)
            {
                StartTime = "000000";
                WriteMessageAtLevel(
                    String.Format("Start time was not set. Force is being used. Setting it to {0}", StartTime),
                    MessageLevel.Verbose, null);
            }
            else if (String.IsNullOrEmpty(StartTime))
            {
                StopFunction("Please enter a start time or use -Force to use defaults.", target: SqlInstance);
                return false;
            }

            if (!TryParseTime(StartTime, out _activeStartTimeOfDay))
            {
                StopFunction(
                    String.Format("Start time {0} needs to be a valid time with format HHmmss.", StartTime),
                    target: SqlInstance);
                return false;
            }
            return true;
        }

        private bool ParseEndTime()
        {
            if (String.IsNullOrEmpty(EndTime) && _forceEnabled)
            {
                EndTime = "235959";
                WriteMessageAtLevel(
                    String.Format("End time was not set. Force is being used. Setting it to {0}", EndTime),
                    MessageLevel.Verbose, null);
            }
            else if (String.IsNullOrEmpty(EndTime))
            {
                StopFunction("Please enter an end time or use -Force to use defaults.", target: SqlInstance);
                return false;
            }

            if (!TryParseTime(EndTime, out _activeEndTimeOfDay))
            {
                StopFunction(
                    String.Format("End time {0} needs to be a valid time with format HHmmss.", EndTime),
                    target: SqlInstance);
                return false;
            }
            return true;
        }

        #endregion Helpers - Validation

        #region Helpers - SMO Operations

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

            Collection<PSObject> results = InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, args);
            if (results != null && results.Count > 0)
                return results[0].BaseObject;
            return null;
        }

        /// <summary>
        /// Creates a new SMO Agent JobSchedule object.
        /// </summary>
        private PSObject CreateScheduleObject(object server, string scheduleName)
        {
            string script = "param($s, $n) New-Object Microsoft.SqlServer.Management.Smo.Agent.JobSchedule($s.JobServer, $n)";
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, ScriptBlock.Create(script), null, new object[] { server, scheduleName });
            if (results != null && results.Count > 0)
                return results[0];
            return null;
        }

        /// <summary>
        /// Checks if an exception is a contained availability group listener error.
        /// </summary>
        internal static bool IsContainedAgError(Exception ex)
        {
            if (ex == null)
                return false;

            Exception current = ex;
            while (current != null)
            {
                if (current.Message != null &&
                    current.Message.IndexOf("newParent", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
                current = current.InnerException;
            }
            return false;
        }

        /// <summary>
        /// Sets a property on a PSObject to a value.
        /// </summary>
        private void SetProperty(PSObject obj, string propertyName, object value)
        {
            string script = String.Format("param($o, $v) $o.{0} = $v", propertyName);
            InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, new object[] { obj, value });
        }

        /// <summary>
        /// Invokes a parameterless method on a PSObject.
        /// </summary>
        private void InvokeMethod(PSObject obj, string methodName)
        {
            string script = String.Format("param($o) $o.{0}()", methodName);
            InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, new object[] { obj });
        }

        /// <summary>
        /// Refreshes server, job server, and shared schedules after schedule creation.
        /// </summary>
        private void RefreshServerObjects(object server)
        {
            try
            {
                string script = @"param($s)
$null = $s.Refresh()
$null = $s.JobServer.Refresh()
$null = $s.JobServer.SharedSchedules.Refresh()";
                InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, new object[] { server });
            }
            catch (Exception)
            {
                // Refresh is best-effort
            }
        }

        /// <summary>
        /// Attaches the schedule to the specified jobs.
        /// </summary>
        private void AttachScheduleToJobs(object server, DbaInstanceParameter instance, PSObject jobSchedule)
        {
            // Get the schedule ID
            string idScript = "param($js) $js.Id";
            Collection<PSObject> idResults = InvokeCommand.InvokeScript(
                false, ScriptBlock.Create(idScript), null, new object[] { jobSchedule });

            object scheduleId = null;
            if (idResults != null && idResults.Count > 0 && idResults[0] != null)
            {
                scheduleId = idResults[0].BaseObject;
            }

            if (scheduleId == null)
                return;

            // Get jobs using Get-DbaAgentJob
            string getJobsScript;
            object[] getJobsArgs;
            if (SqlCredential != null)
            {
                getJobsScript = "param($s, $j, $c) Get-DbaAgentJob -SqlInstance $s -Job $j -SqlCredential $c";
                getJobsArgs = new object[] { server, Job, SqlCredential };
            }
            else
            {
                getJobsScript = "param($s, $j) Get-DbaAgentJob -SqlInstance $s -Job $j";
                getJobsArgs = new object[] { server, Job };
            }

            Collection<PSObject> jobs;
            try
            {
                jobs = InvokeCommand.InvokeScript(false, ScriptBlock.Create(getJobsScript), null, getJobsArgs);
            }
            catch (Exception)
            {
                return;
            }

            if (jobs == null)
                return;

            foreach (PSObject j in jobs)
            {
                if (j == null) continue;

                string jobName = GetPropertyString(j, "Name");
                if (ShouldProcess(instance.ToString(), String.Format("Adding the schedule {0} to job {1}", _scheduleName, jobName)))
                {
                    WriteMessageAtLevel(
                        String.Format("Adding schedule {0} to job {1}", _scheduleName, jobName),
                        MessageLevel.Verbose, null);

                    string attachScript = "param($j, $id) $j.AddSharedSchedule($id)";
                    try
                    {
                        InvokeCommand.InvokeScript(false, ScriptBlock.Create(attachScript), null, new object[] { j, scheduleId });

                        string refreshScript = "param($js) $js.Refresh()";
                        InvokeCommand.InvokeScript(false, ScriptBlock.Create(refreshScript), null, new object[] { jobSchedule });
                    }
                    catch (Exception attachEx)
                    {
                        StopFunction(
                            String.Format("Something went wrong attaching schedule {0} to job {1}.", _scheduleName, jobName),
                            exception: attachEx, target: instance, isContinue: true);
                        TestFunctionInterrupt();
                    }
                }
            }
        }

        /// <summary>
        /// Outputs the schedule using Get-DbaAgentSchedule.
        /// </summary>
        private void OutputSchedule(object server, PSObject jobSchedule)
        {
            string uidScript = "param($js) $js.ScheduleUid";
            Collection<PSObject> uidResults = InvokeCommand.InvokeScript(
                false, ScriptBlock.Create(uidScript), null, new object[] { jobSchedule });

            object scheduleUid = null;
            if (uidResults != null && uidResults.Count > 0 && uidResults[0] != null)
            {
                scheduleUid = uidResults[0].BaseObject;
            }

            if (scheduleUid == null)
                return;

            string script = "param($s, $uid) Get-DbaAgentSchedule -SqlInstance $s -ScheduleUid $uid";
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, ScriptBlock.Create(script), null, new object[] { server, scheduleUid });

                if (results != null)
                {
                    foreach (PSObject result in results)
                    {
                        if (result != null)
                        {
                            WriteObject(result);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // If Get-DbaAgentSchedule fails, output the raw schedule object
                WriteObject(jobSchedule);
            }
        }

        #endregion Helpers - SMO Operations
    }
}
