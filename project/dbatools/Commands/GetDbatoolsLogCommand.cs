using System;
using System.Collections.Generic;
using System.Management.Automation;
using Dataplat.Dbatools.Message;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Retrieves internal log entries and error messages from dbatools module execution.
    /// </summary>
    [Cmdlet("Get", "DbatoolsLog")]
    [OutputType(typeof(LogEntry))]
    [OutputType(typeof(DbatoolsExceptionRecord))]
    [OutputType(typeof(PSObject))]
    public class GetDbatoolsLogCommand : DbaBaseCmdlet
    {
        /// <summary>
        /// Filters log entries to show only messages from dbatools functions matching this pattern. Supports wildcards.
        /// </summary>
        [Parameter()]
        public string FunctionName { get; set; } = "*";

        /// <summary>
        /// Filters log entries to show only messages from modules matching this pattern. Supports wildcards.
        /// </summary>
        [Parameter()]
        public string ModuleName { get; set; } = "*";

        /// <summary>
        /// Filters log entries to show only messages related to a specific target object.
        /// </summary>
        [Parameter()]
        [AllowNull()]
        public object Target { get; set; }

        /// <summary>
        /// Filters log entries to show only messages that contain any of the specified tags.
        /// </summary>
        [Parameter()]
        public string[] Tag { get; set; }

        /// <summary>
        /// Returns log entries from only the last X PowerShell command executions in your current session.
        /// </summary>
        [Parameter()]
        public int Last { get; set; }

        /// <summary>
        /// Returns only the most recent error message from the dbatools logging system.
        /// </summary>
        [Parameter()]
        public SwitchParameter LastError { get; set; }

        /// <summary>
        /// Specifies how many recent executions to skip when using the -Last parameter.
        /// </summary>
        [Parameter()]
        public int Skip { get; set; }

        /// <summary>
        /// Returns log messages in their original format without flattening multiline content.
        /// </summary>
        [Parameter()]
        public SwitchParameter Raw { get; set; }

        /// <summary>
        /// Filters log entries to show only messages from the specified PowerShell runspace GUID.
        /// </summary>
        [Parameter()]
        public Guid Runspace { get; set; }

        /// <summary>
        /// Filters log entries by message severity level.
        /// </summary>
        [Parameter()]
        public MessageLevel[] Level { get; set; }

        /// <summary>
        /// Returns error entries from dbatools' error tracking system instead of regular log entries.
        /// </summary>
        [Parameter()]
        public SwitchParameter Errors { get; set; }

        /// <summary>
        /// Processes the log retrieval request, applying filters and outputting results.
        /// </summary>
        protected override void ProcessRecord()
        {
            WildcardPattern functionPattern = new WildcardPattern(FunctionName, WildcardOptions.IgnoreCase);
            WildcardPattern modulePattern = new WildcardPattern(ModuleName, WildcardOptions.IgnoreCase);

            bool useErrorPath = Errors.IsPresent || TestBound("LastError");

            if (useErrorPath)
            {
                ProcessErrorPath(functionPattern, modulePattern);
            }
            else
            {
                ProcessLogPath(functionPattern, modulePattern);
            }
        }

        /// <summary>
        /// Handles the regular log entry path (no -Errors or -LastError).
        /// </summary>
        private void ProcessLogPath(WildcardPattern functionPattern, WildcardPattern modulePattern)
        {
            List<LogEntry> messages = GetFilteredLogEntries(functionPattern, modulePattern);

            // Apply filters
            if (TestBound("Target"))
            {
                messages = FilterByTarget(messages, Target);
            }
            if (TestBound("Tag"))
            {
                messages = FilterByTag(messages, Tag);
            }
            if (TestBound("Runspace"))
            {
                messages = FilterByRunspace(messages, Runspace);
            }
            if (TestBound("Last"))
            {
                // Skip is always forwarded alongside Last; its default (0) is safe
                messages = FilterByLastExecution(messages, Last, Skip);
            }
            if (TestBound("Level"))
            {
                messages = FilterByLevel(messages, Level);
            }

            // Output
            if (Raw.IsPresent)
            {
                foreach (LogEntry entry in messages)
                {
                    WriteObject(entry);
                }
            }
            else
            {
                foreach (LogEntry entry in messages)
                {
                    WriteObject(CreateOutputObject(entry));
                }
            }
        }

        /// <summary>
        /// Handles the error record path (-Errors and/or -LastError).
        /// Preserves DbatoolsExceptionRecord type fidelity for -Raw output.
        /// </summary>
        private void ProcessErrorPath(WildcardPattern functionPattern, WildcardPattern modulePattern)
        {
            List<DbatoolsExceptionRecord> errorRecords;

            if (TestBound("LastError"))
            {
                // -LastError always takes precedence, returns only the last error
                errorRecords = GetLastErrorRecord(functionPattern, modulePattern);
            }
            else
            {
                errorRecords = GetFilteredErrorRecords(functionPattern, modulePattern);
            }

            // Apply filters that work on DbatoolsExceptionRecord
            if (TestBound("Target"))
            {
                errorRecords = FilterErrorsByTarget(errorRecords, Target);
            }
            if (TestBound("Tag"))
            {
                errorRecords = FilterErrorsByTag(errorRecords, Tag);
            }
            if (TestBound("Runspace"))
            {
                errorRecords = FilterErrorsByRunspace(errorRecords, Runspace);
            }
            if (TestBound("Last"))
            {
                errorRecords = FilterErrorsByLastExecution(errorRecords, Last, Skip);
            }
            if (TestBound("Level"))
            {
                // DbatoolsExceptionRecord has no Level property; in PS1 Level is $null
                // so filtering by Level on error records always returns empty (no match)
                errorRecords = new List<DbatoolsExceptionRecord>();
            }

            // Output
            if (Raw.IsPresent)
            {
                // Return raw DbatoolsExceptionRecord objects, matching PS1 behavior
                foreach (DbatoolsExceptionRecord record in errorRecords)
                {
                    WriteObject(record);
                }
            }
            else
            {
                // Create PSObject with the same property set as the log entry path,
                // but sourced from DbatoolsExceptionRecord fields.
                // Properties not on DbatoolsExceptionRecord are null, matching PS1 Select-Object behavior.
                foreach (DbatoolsExceptionRecord record in errorRecords)
                {
                    WriteObject(CreateErrorOutputObject(record));
                }
            }
        }

        #region Log Entry Filtering

        /// <summary>
        /// Gets log entries filtered by function and module name patterns.
        /// </summary>
        internal static List<LogEntry> GetFilteredLogEntries(WildcardPattern functionPattern, WildcardPattern modulePattern)
        {
            LogEntry[] allEntries = LogHost.GetLog();
            List<LogEntry> result = new List<LogEntry>();

            foreach (LogEntry entry in allEntries)
            {
                if (functionPattern.IsMatch(entry.FunctionName ?? String.Empty) &&
                    modulePattern.IsMatch(entry.ModuleName ?? String.Empty))
                {
                    result.Add(entry);
                }
            }

            return result;
        }

        /// <summary>
        /// Filters log entries by target object equality.
        /// Null-to-null matching is intentional, mirroring PS1's Where-Object -eq $null behavior.
        /// </summary>
        internal static List<LogEntry> FilterByTarget(List<LogEntry> messages, object target)
        {
            List<LogEntry> result = new List<LogEntry>();
            foreach (LogEntry entry in messages)
            {
                if (entry.TargetObject != null && entry.TargetObject.Equals(target))
                {
                    result.Add(entry);
                }
                else if (entry.TargetObject == null && target == null)
                {
                    result.Add(entry);
                }
            }
            return result;
        }

        /// <summary>
        /// Filters log entries that have any of the specified tags.
        /// </summary>
        internal static List<LogEntry> FilterByTag(List<LogEntry> messages, string[] tags)
        {
            if (tags == null || tags.Length == 0)
            {
                return messages;
            }

            List<LogEntry> result = new List<LogEntry>();
            HashSet<string> tagSet = new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase);

            foreach (LogEntry entry in messages)
            {
                if (entry.Tags != null)
                {
                    bool found = false;
                    foreach (string entryTag in entry.Tags)
                    {
                        if (tagSet.Contains(entryTag))
                        {
                            found = true;
                            break;
                        }
                    }
                    if (found)
                    {
                        result.Add(entry);
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Filters log entries by runspace GUID.
        /// </summary>
        internal static List<LogEntry> FilterByRunspace(List<LogEntry> messages, Guid runspace)
        {
            List<LogEntry> result = new List<LogEntry>();
            foreach (LogEntry entry in messages)
            {
                if (entry.Runspace == runspace)
                {
                    result.Add(entry);
                }
            }
            return result;
        }

        /// <summary>
        /// Filters log entries based on PowerShell command execution history.
        /// Uses Get-History to determine time ranges, excluding Get-DbatoolsLog invocations.
        /// </summary>
        internal List<LogEntry> FilterByLastExecution(List<LogEntry> messages, int last, int skip)
        {
            try
            {
                DateTime start;
                DateTime end;
                Guid currentRunspace;
                if (!GetHistoryTimeRange(last, skip, out start, out end, out currentRunspace))
                {
                    return new List<LogEntry>();
                }

                List<LogEntry> result = new List<LogEntry>();
                foreach (LogEntry entry in messages)
                {
                    if (entry.Timestamp > start && entry.Timestamp < end && entry.Runspace == currentRunspace)
                    {
                        result.Add(entry);
                    }
                }
                return result;
            }
            catch (Exception ex)
            {
                WriteMessageWarning(String.Format("Failed to retrieve command history for -Last filter: {0}", ex.Message));
                return new List<LogEntry>();
            }
        }

        /// <summary>
        /// Filters log entries by message level.
        /// </summary>
        internal static List<LogEntry> FilterByLevel(List<LogEntry> messages, MessageLevel[] levels)
        {
            if (levels == null || levels.Length == 0)
            {
                return messages;
            }

            HashSet<MessageLevel> levelSet = new HashSet<MessageLevel>(levels);
            List<LogEntry> result = new List<LogEntry>();
            foreach (LogEntry entry in messages)
            {
                if (levelSet.Contains(entry.Level))
                {
                    result.Add(entry);
                }
            }
            return result;
        }

        #endregion Log Entry Filtering

        #region Error Record Filtering

        /// <summary>
        /// Gets error records filtered by function and module name patterns.
        /// </summary>
        internal static List<DbatoolsExceptionRecord> GetFilteredErrorRecords(WildcardPattern functionPattern, WildcardPattern modulePattern)
        {
            DbatoolsExceptionRecord[] allErrors = LogHost.GetErrors();
            List<DbatoolsExceptionRecord> result = new List<DbatoolsExceptionRecord>();

            foreach (DbatoolsExceptionRecord record in allErrors)
            {
                if (functionPattern.IsMatch(record.FunctionName ?? String.Empty) &&
                    modulePattern.IsMatch(record.ModuleName ?? String.Empty))
                {
                    result.Add(record);
                }
            }

            return result;
        }

        /// <summary>
        /// Gets the most recent error record matching function/module patterns (Select-Object -Last 1).
        /// </summary>
        internal static List<DbatoolsExceptionRecord> GetLastErrorRecord(WildcardPattern functionPattern, WildcardPattern modulePattern)
        {
            List<DbatoolsExceptionRecord> allMatching = GetFilteredErrorRecords(functionPattern, modulePattern);
            List<DbatoolsExceptionRecord> result = new List<DbatoolsExceptionRecord>();

            if (allMatching.Count > 0)
            {
                result.Add(allMatching[allMatching.Count - 1]);
            }

            return result;
        }

        /// <summary>
        /// Filters error records by target object equality.
        /// </summary>
        internal static List<DbatoolsExceptionRecord> FilterErrorsByTarget(List<DbatoolsExceptionRecord> records, object target)
        {
            List<DbatoolsExceptionRecord> result = new List<DbatoolsExceptionRecord>();
            foreach (DbatoolsExceptionRecord record in records)
            {
                if (record.TargetObject != null && record.TargetObject.Equals(target))
                {
                    result.Add(record);
                }
                else if (record.TargetObject == null && target == null)
                {
                    result.Add(record);
                }
            }
            return result;
        }

        /// <summary>
        /// Filters error records that have any of the specified tags.
        /// </summary>
        internal static List<DbatoolsExceptionRecord> FilterErrorsByTag(List<DbatoolsExceptionRecord> records, string[] tags)
        {
            if (tags == null || tags.Length == 0)
            {
                return records;
            }

            List<DbatoolsExceptionRecord> result = new List<DbatoolsExceptionRecord>();
            HashSet<string> tagSet = new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase);

            foreach (DbatoolsExceptionRecord record in records)
            {
                if (record.Tags != null)
                {
                    bool found = false;
                    foreach (string recordTag in record.Tags)
                    {
                        if (tagSet.Contains(recordTag))
                        {
                            found = true;
                            break;
                        }
                    }
                    if (found)
                    {
                        result.Add(record);
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Filters error records by runspace GUID.
        /// </summary>
        internal static List<DbatoolsExceptionRecord> FilterErrorsByRunspace(List<DbatoolsExceptionRecord> records, Guid runspace)
        {
            List<DbatoolsExceptionRecord> result = new List<DbatoolsExceptionRecord>();
            foreach (DbatoolsExceptionRecord record in records)
            {
                if (record.Runspace == runspace)
                {
                    result.Add(record);
                }
            }
            return result;
        }

        /// <summary>
        /// Filters error records based on PowerShell command execution history.
        /// </summary>
        internal List<DbatoolsExceptionRecord> FilterErrorsByLastExecution(List<DbatoolsExceptionRecord> records, int last, int skip)
        {
            try
            {
                DateTime start;
                DateTime end;
                Guid currentRunspace;
                if (!GetHistoryTimeRange(last, skip, out start, out end, out currentRunspace))
                {
                    return new List<DbatoolsExceptionRecord>();
                }

                List<DbatoolsExceptionRecord> result = new List<DbatoolsExceptionRecord>();
                foreach (DbatoolsExceptionRecord record in records)
                {
                    if (record.Timestamp > start && record.Timestamp < end && record.Runspace == currentRunspace)
                    {
                        result.Add(record);
                    }
                }
                return result;
            }
            catch (Exception ex)
            {
                WriteMessageWarning(String.Format("Failed to retrieve command history for -Last filter: {0}", ex.Message));
                return new List<DbatoolsExceptionRecord>();
            }
        }

        #endregion Error Record Filtering

        #region History Helper

        /// <summary>
        /// Gets the time range from PowerShell command history, excluding Get-DbatoolsLog commands.
        /// </summary>
        private bool GetHistoryTimeRange(int last, int skip, out DateTime start, out DateTime end, out Guid currentRunspace)
        {
            start = DateTime.MinValue;
            end = DateTime.MaxValue;
            currentRunspace = Guid.Empty;

            string script = String.Format(
                "$h = Get-History | Where-Object {{ $_.CommandLine -notlike 'Get-DbatoolsLog*' }} | Select-Object -Last {0} -Skip {1}; " +
                "if ($h) {{ @($h[0].StartExecutionTime, $h[-1].EndExecutionTime) }} else {{ @() }}",
                last, skip);

            var results = InvokeCommand.InvokeScript(script);
            if (results == null || results.Count < 2)
            {
                return false;
            }

            if (!(results[0].BaseObject is DateTime) || !(results[1].BaseObject is DateTime))
            {
                return false;
            }

            start = (DateTime)results[0].BaseObject;
            end = (DateTime)results[1].BaseObject;

            if (System.Management.Automation.Runspaces.Runspace.DefaultRunspace != null)
            {
                currentRunspace = System.Management.Automation.Runspaces.Runspace.DefaultRunspace.InstanceId;
            }

            return true;
        }

        #endregion History Helper

        #region Output Formatting

        /// <summary>
        /// Creates a PSObject from a LogEntry with flattened message content for display output.
        /// Mirrors the PS1 Select-Object with custom Message expression.
        /// </summary>
        internal static PSObject CreateOutputObject(LogEntry entry)
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("CallStack", entry.CallStack));
            obj.Properties.Add(new PSNoteProperty("ComputerName", entry.ComputerName));
            obj.Properties.Add(new PSNoteProperty("File", entry.File));
            obj.Properties.Add(new PSNoteProperty("FunctionName", entry.FunctionName));
            obj.Properties.Add(new PSNoteProperty("Level", entry.Level));
            obj.Properties.Add(new PSNoteProperty("Line", entry.Line));
            obj.Properties.Add(new PSNoteProperty("Message", FlattenMessage(entry.Message)));
            obj.Properties.Add(new PSNoteProperty("ModuleName", entry.ModuleName));
            obj.Properties.Add(new PSNoteProperty("Runspace", entry.Runspace));
            obj.Properties.Add(new PSNoteProperty("Tags", entry.Tags));
            obj.Properties.Add(new PSNoteProperty("TargetObject", entry.TargetObject));
            obj.Properties.Add(new PSNoteProperty("Timestamp", entry.Timestamp));
            obj.Properties.Add(new PSNoteProperty("Type", entry.Type));
            obj.Properties.Add(new PSNoteProperty("Username", entry.Username));
            return obj;
        }

        /// <summary>
        /// Creates a PSObject from a DbatoolsExceptionRecord with the same property set as
        /// CreateOutputObject but sourced from error record fields. Properties not present on
        /// DbatoolsExceptionRecord (CallStack, File, Level, Line, Type, Username) are null,
        /// matching PS1 Select-Object behavior on objects missing those properties.
        /// </summary>
        internal static PSObject CreateErrorOutputObject(DbatoolsExceptionRecord record)
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("CallStack", null));
            obj.Properties.Add(new PSNoteProperty("ComputerName", record.ComputerName));
            obj.Properties.Add(new PSNoteProperty("File", null));
            obj.Properties.Add(new PSNoteProperty("FunctionName", record.FunctionName));
            obj.Properties.Add(new PSNoteProperty("Level", null));
            obj.Properties.Add(new PSNoteProperty("Line", null));
            obj.Properties.Add(new PSNoteProperty("Message", FlattenMessage(record.Message)));
            obj.Properties.Add(new PSNoteProperty("ModuleName", record.ModuleName));
            obj.Properties.Add(new PSNoteProperty("Runspace", record.Runspace));
            obj.Properties.Add(new PSNoteProperty("Tags", record.Tags));
            obj.Properties.Add(new PSNoteProperty("TargetObject", record.TargetObject));
            obj.Properties.Add(new PSNoteProperty("Timestamp", record.Timestamp));
            obj.Properties.Add(new PSNoteProperty("Type", null));
            obj.Properties.Add(new PSNoteProperty("Username", null));
            return obj;
        }

        /// <summary>
        /// Flattens a multiline message into a single line by joining on newlines
        /// and collapsing multiple consecutive spaces into a single space.
        /// Mirrors the PS1 expression: ($_.Message.Split("`n") -join " ") then collapse double spaces.
        /// Uses iterative Replace rather than Regex to preserve exact PS1 semantics (only collapses spaces, not other whitespace).
        /// </summary>
        internal static string FlattenMessage(string message)
        {
            if (String.IsNullOrEmpty(message))
            {
                return message;
            }

            // Split on newlines and join with space
            string result = String.Join(" ", message.Split('\n'));

            // Collapse multiple spaces into single space
            while (result.Contains("  "))
            {
                result = result.Replace("  ", " ");
            }

            return result;
        }

        #endregion Output Formatting
    }
}
