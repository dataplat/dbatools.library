using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Management.Automation;
using System.Text;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Retrieves SQL Agent shared schedules with detailed timing and recurrence information
    /// from one or more SQL Server instances.
    /// </summary>
    [Cmdlet("Get", "DbaAgentSchedule")]
    public class GetDbaAgentScheduleCommand : DbaInstanceCmdlet
    {
        #region Parameters

        /// <summary>
        /// Specifies one or more schedule names to retrieve from the SQL Agent shared schedules collection.
        /// </summary>
        [Parameter()]
        public string[] Schedule { get; set; }

        /// <summary>
        /// Specifies the GUID-based unique identifier of one or more shared schedules to retrieve.
        /// </summary>
        [Parameter()]
        public string[] ScheduleUid { get; set; }

        /// <summary>
        /// Specifies the numeric identifier of one or more shared schedules to retrieve.
        /// </summary>
        [Parameter()]
        public int[] Id { get; set; }

        #endregion Parameters

        /// <summary>
        /// Mapping of FrequencyTypes enum names to integer values.
        /// </summary>
        private static readonly Dictionary<string, int> FrequencyTypeMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "Once", 1 },
            { "OneTime", 1 },
            { "Daily", 4 },
            { "Weekly", 8 },
            { "Monthly", 16 },
            { "MonthlyRelative", 32 },
            { "AutoStart", 64 },
            { "OnIdle", 128 }
        };

        /// <summary>
        /// Mapping of FrequencySubDayTypes enum names to integer values.
        /// </summary>
        private static readonly Dictionary<string, int> FrequencySubDayTypeMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "Unknown", 0 },
            { "None", 0 },
            { "Once", 1 },
            { "Time", 1 },
            { "Seconds", 2 },
            { "Second", 2 },
            { "Minutes", 4 },
            { "Minute", 4 },
            { "Hours", 8 },
            { "Hour", 8 }
        };

        /// <summary>
        /// Mapping of FrequencyRelativeIntervals enum names to integer values.
        /// </summary>
        private static readonly Dictionary<string, int> FrequencyRelativeIntervalsMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "First", 1 },
            { "Second", 2 },
            { "Third", 4 },
            { "Fourth", 8 },
            { "Last", 16 }
        };

        /// <summary>
        /// Default display properties for the output objects.
        /// </summary>
        private static readonly string[] DefaultDisplayProperties = new string[]
        {
            "ComputerName", "InstanceName", "SqlInstance", "ScheduleName",
            "ActiveEndDate", "ActiveEndTimeOfDay", "ActiveStartDate", "ActiveStartTimeOfDay",
            "DateCreated", "FrequencyInterval", "FrequencyRecurrenceFactor",
            "FrequencyRelativeIntervals", "FrequencySubDayInterval", "FrequencySubDayTypes",
            "FrequencyTypes", "IsEnabled", "JobCount", "Description", "ScheduleUid"
        };

        /// <summary>
        /// Connects to each SQL Server instance and retrieves SQL Agent shared schedules,
        /// applying include filters and adding custom properties including human-readable descriptions.
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
                        errorRecord: new ErrorRecord(ex, "GetDbaAgentSchedule_ConnectionError", ErrorCategory.ConnectionError, instance),
                        target: instance,
                        isContinue: true,
                        category: ErrorCategory.ConnectionError);
                    TestFunctionInterrupt();
                    continue;
                }

                // Check for Express edition
                string edition = GetPropertyString(PSObject.AsPSObject(server), "Edition");
                if (edition != null && edition.StartsWith("Express", StringComparison.OrdinalIgnoreCase))
                {
                    StopFunction(
                        String.Format("{0} does not support SQL Server Agent. Skipping {1}.", edition, server),
                        target: instance,
                        isContinue: true);
                    TestFunctionInterrupt();
                    continue;
                }

                // Refresh and get shared schedules
                Collection<PSObject> allSchedules;
                try
                {
                    allSchedules = GetSharedSchedules(server);
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("Failed to retrieve shared schedules from {0}: {1}", server, ex.Message),
                        exception: ex,
                        target: instance,
                        isContinue: true);
                    TestFunctionInterrupt();
                    continue;
                }

                if (allSchedules == null || allSchedules.Count == 0)
                    continue;

                // Apply filters
                List<PSObject> filtered = FilterSchedules(allSchedules);

                // Get server connection info
                string computerName = GetPropertyString(PSObject.AsPSObject(server), "ComputerName");
                string serviceName = GetPropertyString(PSObject.AsPSObject(server), "ServiceName");
                string domainInstanceName = GetPropertyString(PSObject.AsPSObject(server), "DomainInstanceName");

                foreach (PSObject scheduleObj in filtered)
                {
                    if (scheduleObj == null)
                        continue;

                    string description = GetScheduleDescription(scheduleObj);

                    AddOrSetProperty(scheduleObj, "ComputerName", computerName);
                    AddOrSetProperty(scheduleObj, "InstanceName", serviceName);
                    AddOrSetProperty(scheduleObj, "SqlInstance", domainInstanceName);
                    AddOrSetProperty(scheduleObj, "Description", description);

                    // Add ScheduleName as alias for Name (matches Select-DefaultView "Name as ScheduleName")
                    string scheduleName = GetPSObjectPropertyString(scheduleObj, "Name");
                    AddOrSetProperty(scheduleObj, "ScheduleName", scheduleName);

                    SetDefaultDisplayPropertySet(scheduleObj, DefaultDisplayProperties);

                    WriteObject(scheduleObj);
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
        /// Refreshes and retrieves the SharedSchedules collection from the server's JobServer.
        /// </summary>
        private Collection<PSObject> GetSharedSchedules(object server)
        {
            string script = "param($s) $s.JobServer.SharedSchedules.Refresh(); $s.JobServer.SharedSchedules";
            return InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { server });
        }

        /// <summary>
        /// Filters the schedule collection based on bound parameters (Schedule, ScheduleUid, Id).
        /// </summary>
        private List<PSObject> FilterSchedules(Collection<PSObject> allSchedules)
        {
            bool hasScheduleFilter = TestBound("Schedule");
            bool hasUidFilter = TestBound("ScheduleUid");
            bool hasIdFilter = TestBound("Id");

            if (!hasScheduleFilter && !hasUidFilter && !hasIdFilter)
            {
                List<PSObject> all = new List<PSObject>();
                foreach (PSObject s in allSchedules)
                    all.Add(s);
                return all;
            }

            List<PSObject> result = new List<PSObject>();

            if (hasScheduleFilter)
            {
                foreach (PSObject s in allSchedules)
                {
                    string name = GetPSObjectPropertyString(s, "Name");
                    if (name != null && IsInStringArray(name, Schedule))
                    {
                        if (!ContainsByReference(result, s))
                            result.Add(s);
                    }
                }
            }

            if (hasUidFilter)
            {
                foreach (PSObject s in allSchedules)
                {
                    string uid = GetPSObjectPropertyString(s, "ScheduleUid");
                    if (uid != null && IsInStringArray(uid, ScheduleUid))
                    {
                        if (!ContainsByReference(result, s))
                            result.Add(s);
                    }
                }
            }

            if (hasIdFilter)
            {
                foreach (PSObject s in allSchedules)
                {
                    int id = GetPSObjectPropertyInt(s, "Id");
                    if (IsInIntArray(id, Id))
                    {
                        if (!ContainsByReference(result, s))
                            result.Add(s);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Gets a string property value from a PSObject.
        /// </summary>
        internal static string GetPSObjectPropertyString(PSObject obj, string propertyName)
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
            }
            return null;
        }

        /// <summary>
        /// Gets an integer property value from a PSObject.
        /// </summary>
        internal static int GetPSObjectPropertyInt(PSObject obj, string propertyName)
        {
            if (obj == null)
                return 0;
            try
            {
                PSPropertyInfo prop = obj.Properties[propertyName];
                if (prop != null && prop.Value != null)
                {
                    if (prop.Value is int intVal)
                        return intVal;
                    int parsed;
                    if (int.TryParse(prop.Value.ToString(), out parsed))
                        return parsed;
                }
            }
            catch (Exception)
            {
            }
            return 0;
        }

        /// <summary>
        /// Checks if a string value is in a string array (case-insensitive).
        /// </summary>
        internal static bool IsInStringArray(string value, string[] array)
        {
            if (array == null)
                return false;
            foreach (string item in array)
            {
                if (String.Equals(value, item, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Checks if an integer value is in an integer array.
        /// </summary>
        internal static bool IsInIntArray(int value, int[] array)
        {
            if (array == null)
                return false;
            foreach (int item in array)
            {
                if (value == item)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Checks if a PSObject is already in the list by reference equality.
        /// </summary>
        private static bool ContainsByReference(List<PSObject> list, PSObject obj)
        {
            foreach (PSObject item in list)
            {
                if (ReferenceEquals(item, obj))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Generates a human-readable description of a schedule's timing pattern.
        /// Matches the Get-ScheduleDescription PS1 function behavior exactly.
        /// </summary>
        internal static string GetScheduleDescription(PSObject schedule)
        {
            DateTimeFormatInfo datetimeFormat = CultureInfo.CurrentCulture.DateTimeFormat;

            // Get date/time values
            DateTime activeStartDate = GetPSObjectPropertyDateTime(schedule, "ActiveStartDate");
            TimeSpan activeStartTimeOfDay = GetPSObjectPropertyTimeSpan(schedule, "ActiveStartTimeOfDay");
            DateTime activeEndDate = GetPSObjectPropertyDateTime(schedule, "ActiveEndDate");
            TimeSpan activeEndTimeOfDay = GetPSObjectPropertyTimeSpan(schedule, "ActiveEndTimeOfDay");

            string startDate = activeStartDate.ToString(datetimeFormat.ShortDatePattern);
            string startTime = DateTime.Today.Add(activeStartTimeOfDay).ToString(datetimeFormat.LongTimePattern);
            string endDate = activeEndDate.ToString(datetimeFormat.ShortDatePattern);
            string endTime = DateTime.Today.Add(activeEndTimeOfDay).ToString(datetimeFormat.LongTimePattern);

            int frequencyTypes = GetFrequencyTypeValue(schedule);
            int frequencyInterval = GetPSObjectPropertyInt(schedule, "FrequencyInterval");
            int frequencyRecurrenceFactor = GetPSObjectPropertyInt(schedule, "FrequencyRecurrenceFactor");
            int frequencySubDayTypes = GetFrequencySubDayTypeValue(schedule);
            int frequencySubDayInterval = GetPSObjectPropertyInt(schedule, "FrequencySubDayInterval");
            int frequencyRelativeIntervals = GetFrequencyRelativeIntervalsValue(schedule);

            StringBuilder description = new StringBuilder();

            // Start setting description based on frequency type
            switch (frequencyTypes)
            {
                case 1: // Once
                    description.AppendFormat("Occurs on {0} at {1}", startDate, startTime);
                    break;
                case 4: // Daily
                case 8: // Weekly
                case 16: // Monthly
                case 32: // MonthlyRelative
                    description.Append("Occurs every ");
                    break;
                case 64: // AutoStart
                    description.Append("Start automatically when SQL Server Agent starts ");
                    break;
                case 128: // OnIdle
                    description.Append("Start whenever the CPUs become idle");
                    break;
            }

            // Frequency type details
            switch (frequencyTypes)
            {
                case 4: // Daily
                    if (frequencyInterval == 1)
                        description.Append("day ");
                    else if (frequencyInterval > 1)
                        description.AppendFormat("{0} day(s) ", frequencyInterval);
                    break;

                case 8: // Weekly
                    if (frequencyRecurrenceFactor == 1)
                        description.Append("week on ");
                    else if (frequencyRecurrenceFactor > 1)
                        description.AppendFormat("{0} week(s) on ", frequencyRecurrenceFactor);

                    description.Append(GetWeekDaysDescription(frequencyInterval));
                    description.Append(" ");
                    break;

                case 16: // Monthly
                    if (frequencyRecurrenceFactor == 1)
                        description.Append("month ");
                    else if (frequencyRecurrenceFactor > 1)
                        description.AppendFormat("{0} month(s) ", frequencyRecurrenceFactor);

                    description.AppendFormat("on day {0} of that month ", frequencyInterval);
                    break;

                case 32: // MonthlyRelative
                    switch (frequencyRelativeIntervals)
                    {
                        case 1: description.Append("first "); break;
                        case 2: description.Append("second "); break;
                        case 4: description.Append("third "); break;
                        case 8: description.Append("fourth "); break;
                        case 16: description.Append("last "); break;
                    }

                    switch (frequencyInterval)
                    {
                        case 1: description.Append("Sunday "); break;
                        case 2: description.Append("Monday "); break;
                        case 3: description.Append("Tuesday "); break;
                        case 4: description.Append("Wednesday "); break;
                        case 5: description.Append("Thursday "); break;
                        case 6: description.Append("Friday "); break;
                        case 7: description.Append("Saturday "); break;
                        case 8: description.Append("Day "); break;
                        case 9: description.Append("Weekday "); break;
                        case 10: description.Append("Weekend day "); break;
                    }

                    description.AppendFormat("of every {0} month(s) ", frequencyRecurrenceFactor);
                    break;
            }

            // Sub-day and date range details
            if (frequencyTypes != 64 && frequencyTypes != 128)
            {
                if (frequencySubDayTypes == 0 || frequencySubDayTypes == 1)
                {
                    description.AppendFormat("at {0}. ", startTime);
                }
                else
                {
                    switch (frequencySubDayTypes)
                    {
                        case 2: // Seconds
                            description.AppendFormat("every {0} second(s) ", frequencySubDayInterval);
                            break;
                        case 4: // Minutes
                            description.AppendFormat("every {0} minute(s) ", frequencySubDayInterval);
                            break;
                        case 8: // Hours
                            description.AppendFormat("every {0} hour(s) ", frequencySubDayInterval);
                            break;
                    }

                    description.AppendFormat("between {0} and {1}. ", startTime, endTime);
                }

                // End date check - note: "will used" matches original PS1 behavior
                if (activeEndDate.Year == 9999)
                    description.AppendFormat("Schedule will be used starting on {0}.", startDate);
                else
                    description.AppendFormat("Schedule will used between {0} and {1}.", startDate, endDate);
            }

            return description.ToString();
        }

        /// <summary>
        /// Converts the weekly frequency interval bitmask to a comma-separated day list.
        /// Bit values: 1=Sunday, 2=Monday, 4=Tuesday, 8=Wednesday, 16=Thursday, 32=Friday, 64=Saturday.
        /// Output order: Monday, Tuesday, Wednesday, Thursday, Friday, Saturday, Sunday.
        /// </summary>
        internal static string GetWeekDaysDescription(int frequencyInterval)
        {
            // Days array: [0]=Monday, [1]=Tuesday, [2]=Wednesday, [3]=Thursday, [4]=Friday, [5]=Saturday, [6]=Sunday
            string[] days = new string[7];

            while (frequencyInterval > 0)
            {
                if (frequencyInterval >= 64)
                {
                    days[5] = "Saturday";
                    frequencyInterval -= 64;
                }
                else if (frequencyInterval >= 32)
                {
                    days[4] = "Friday";
                    frequencyInterval -= 32;
                }
                else if (frequencyInterval >= 16)
                {
                    days[3] = "Thursday";
                    frequencyInterval -= 16;
                }
                else if (frequencyInterval >= 8)
                {
                    days[2] = "Wednesday";
                    frequencyInterval -= 8;
                }
                else if (frequencyInterval >= 4)
                {
                    days[1] = "Tuesday";
                    frequencyInterval -= 4;
                }
                else if (frequencyInterval >= 2)
                {
                    days[0] = "Monday";
                    frequencyInterval -= 2;
                }
                else if (frequencyInterval >= 1)
                {
                    days[6] = "Sunday";
                    frequencyInterval -= 1;
                }
            }

            // Join non-null days
            List<string> dayList = new List<string>();
            foreach (string day in days)
            {
                if (day != null)
                    dayList.Add(day);
            }
            return String.Join(", ", dayList);
        }

        /// <summary>
        /// Gets a DateTime property from a PSObject.
        /// </summary>
        internal static DateTime GetPSObjectPropertyDateTime(PSObject obj, string propertyName)
        {
            if (obj == null)
                return DateTime.MinValue;
            try
            {
                PSPropertyInfo prop = obj.Properties[propertyName];
                if (prop != null && prop.Value != null)
                {
                    if (prop.Value is DateTime dt)
                        return dt;
                    DateTime parsed;
                    if (DateTime.TryParse(prop.Value.ToString(), out parsed))
                        return parsed;
                }
            }
            catch (Exception)
            {
            }
            return DateTime.MinValue;
        }

        /// <summary>
        /// Gets a TimeSpan property from a PSObject.
        /// </summary>
        internal static TimeSpan GetPSObjectPropertyTimeSpan(PSObject obj, string propertyName)
        {
            if (obj == null)
                return TimeSpan.Zero;
            try
            {
                PSPropertyInfo prop = obj.Properties[propertyName];
                if (prop != null && prop.Value != null)
                {
                    if (prop.Value is TimeSpan ts)
                        return ts;
                    TimeSpan parsed;
                    if (TimeSpan.TryParse(prop.Value.ToString(), out parsed))
                        return parsed;
                }
            }
            catch (Exception)
            {
            }
            return TimeSpan.Zero;
        }

        /// <summary>
        /// Gets the FrequencyTypes value as an integer from the schedule object.
        /// Handles both numeric values and SMO enum string representations.
        /// </summary>
        internal static int GetFrequencyTypeValue(PSObject schedule)
        {
            return GetEnumPropertyAsInt(schedule, "FrequencyTypes", FrequencyTypeMap);
        }

        /// <summary>
        /// Gets the FrequencySubDayTypes value as an integer.
        /// </summary>
        internal static int GetFrequencySubDayTypeValue(PSObject schedule)
        {
            return GetEnumPropertyAsInt(schedule, "FrequencySubDayTypes", FrequencySubDayTypeMap);
        }

        /// <summary>
        /// Gets the FrequencyRelativeIntervals value as an integer.
        /// </summary>
        internal static int GetFrequencyRelativeIntervalsValue(PSObject schedule)
        {
            return GetEnumPropertyAsInt(schedule, "FrequencyRelativeIntervals", FrequencyRelativeIntervalsMap);
        }

        /// <summary>
        /// Gets an enum or int property from a PSObject, mapping string names to int values.
        /// </summary>
        internal static int GetEnumPropertyAsInt(PSObject obj, string propertyName, Dictionary<string, int> nameMap)
        {
            if (obj == null)
                return 0;
            try
            {
                PSPropertyInfo prop = obj.Properties[propertyName];
                if (prop == null || prop.Value == null)
                    return 0;

                if (prop.Value is int intVal)
                    return intVal;

                string strVal = prop.Value.ToString();
                int parsed;
                if (int.TryParse(strVal, out parsed))
                    return parsed;

                // Try mapping name
                int mapped;
                if (nameMap.TryGetValue(strVal, out mapped))
                    return mapped;
            }
            catch (Exception)
            {
            }
            return 0;
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
            catch (Exception) { }

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
