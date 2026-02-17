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
    /// Retrieves SQL Server Agent job details and execution status from one or more instances.
    /// Returns job objects with properties including name, category, owner, current run status,
    /// last run outcome, and custom properties like ComputerName, InstanceName, SqlInstance.
    /// </summary>
    [Cmdlet("Get", "DbaAgentJob")]
    public class GetDbaAgentJobCommand : DbaInstanceCmdlet
    {
        #region Parameters

        /// <summary>
        /// Specifies specific SQL Agent job names to retrieve. Accepts an array of job names.
        /// </summary>
        [Parameter(Position = 1)]
        public string[] Job { get; set; }

        /// <summary>
        /// Excludes specific SQL Agent job names from the results.
        /// </summary>
        [Parameter()]
        public string[] ExcludeJob { get; set; }

        /// <summary>
        /// Filters jobs to only those containing T-SQL job steps that target specific databases.
        /// </summary>
        [Parameter()]
        public string[] Database { get; set; }

        /// <summary>
        /// Filters jobs by their assigned category.
        /// </summary>
        [Parameter()]
        public string[] Category { get; set; }

        /// <summary>
        /// Excludes jobs from specific categories from the results.
        /// </summary>
        [Parameter()]
        public string[] ExcludeCategory { get; set; }

        /// <summary>
        /// Excludes disabled SQL Agent jobs from the results.
        /// </summary>
        [Parameter()]
        public SwitchParameter ExcludeDisabledJobs { get; set; }

        /// <summary>
        /// Adds execution start date information for currently running jobs to the output.
        /// </summary>
        [Parameter()]
        public SwitchParameter IncludeExecution { get; set; }

        /// <summary>
        /// Specifies whether to return Local jobs, MultiServer jobs, or both.
        /// Defaults to both.
        /// </summary>
        [Parameter()]
        [ValidateSet("MultiServer", "Local")]
        public string[] Type { get; set; }

        #endregion Parameters

        /// <summary>
        /// Default display properties for the output objects (without StartDate).
        /// </summary>
        private static readonly string[] DefaultDisplayProperties = new string[]
        {
            "ComputerName", "InstanceName", "SqlInstance", "Name", "Category",
            "OwnerLoginName", "CurrentRunStatus", "CurrentRunRetryAttempt",
            "Enabled", "LastRunDate", "LastRunOutcome", "HasSchedule",
            "OperatorToEmail", "CreateDate"
        };

        /// <summary>
        /// Default display properties with StartDate included (for IncludeExecution).
        /// </summary>
        private static readonly string[] DefaultDisplayPropertiesWithStartDate = new string[]
        {
            "ComputerName", "InstanceName", "SqlInstance", "Name", "Category",
            "OwnerLoginName", "CurrentRunStatus", "CurrentRunRetryAttempt",
            "Enabled", "LastRunDate", "LastRunOutcome", "HasSchedule",
            "OperatorToEmail", "CreateDate", "StartDate"
        };

        /// <summary>
        /// Connects to each SQL Server instance and retrieves SQL Agent jobs,
        /// applying include/exclude filters and adding custom properties.
        /// </summary>
        protected override void ProcessRecord()
        {
            // Resolve the Type parameter default
            string[] typeFilter = Type;
            if (typeFilter == null || typeFilter.Length == 0)
            {
                typeFilter = new string[] { "MultiServer", "Local" };
            }

            // Build HashSets for Database/Category/ExcludeCategory once before the instance loop
            HashSet<string> dbLookup = null;
            if (Database != null && Database.Length > 0)
            {
                dbLookup = new HashSet<string>(Database, StringComparer.OrdinalIgnoreCase);
            }

            HashSet<string> categoryLookup = null;
            if (Category != null && Category.Length > 0)
            {
                categoryLookup = new HashSet<string>(Category, StringComparer.OrdinalIgnoreCase);
            }

            HashSet<string> excludeCategoryLookup = null;
            if (ExcludeCategory != null && ExcludeCategory.Length > 0)
            {
                excludeCategoryLookup = new HashSet<string>(ExcludeCategory, StringComparer.OrdinalIgnoreCase);
            }

            foreach (DbaInstanceParameter instance in SqlInstance)
            {
                // Validate the Job parameter for null/empty/whitespace values (per-instance, matching PS1 continue)
                string[] jobFilter = null;
                if (TestBound("Job"))
                {
                    jobFilter = FilterNonEmptyStrings(Job);
                    if (jobFilter == null || jobFilter.Length == 0)
                    {
                        WriteMessageAtLevel(
                            "The -Job parameter was explicitly provided but contains only null, empty, or whitespace values. No jobs will be returned.",
                            MessageLevel.Verbose, null);
                        continue;
                    }
                }

                // Validate the ExcludeJob parameter for null/empty/whitespace values
                string[] excludeJobFilter = null;
                if (TestBound("ExcludeJob"))
                {
                    excludeJobFilter = FilterNonEmptyStrings(ExcludeJob);
                    if (excludeJobFilter == null || excludeJobFilter.Length == 0)
                    {
                        WriteMessageAtLevel(
                            "The -ExcludeJob parameter was explicitly provided but contains only null, empty, or whitespace values. Parameter will be ignored.",
                            MessageLevel.Verbose, null);
                    }
                }

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
                        errorRecord: new ErrorRecord(ex, "GetDbaAgentJob_ConnectionError", ErrorCategory.ConnectionError, instance),
                        target: instance,
                        isContinue: true,
                        category: ErrorCategory.ConnectionError);
                    TestFunctionInterrupt();
                    continue;
                }

                // Query execution data if IncludeExecution was specified
                Dictionary<Guid, DateTime> executionLookup = null;
                if (TestBound("IncludeExecution"))
                {
                    executionLookup = GetJobExecutionData(server);
                }

                // Get jobs from JobServer
                Collection<PSObject> jobs;
                try
                {
                    jobs = GetJobs(server);
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("Failed to retrieve jobs from {0}", instance),
                        exception: ex,
                        target: instance,
                        isContinue: true);
                    TestFunctionInterrupt();
                    continue;
                }

                if (jobs == null || jobs.Count == 0)
                    continue;

                // Get server connection info for custom properties
                string computerName = GetServerPropertySafe(server, "ComputerName");
                string serviceName = GetServerPropertySafe(server, "ServiceName");
                string domainInstanceName = GetServerPropertySafe(server, "DomainInstanceName");

                foreach (PSObject jobObj in jobs)
                {
                    if (jobObj == null)
                        continue;

                    // Filter by JobType
                    string jobType = GetPSPropertyString(jobObj, "JobType");
                    if (!IsInStringArray(jobType, typeFilter))
                        continue;

                    string jobName = GetPSPropertyString(jobObj, "Name");

                    // Filter by Job include
                    if (jobFilter != null && !IsInStringArray(jobName, jobFilter))
                        continue;

                    // Filter by ExcludeJob
                    if (excludeJobFilter != null && IsInStringArray(jobName, excludeJobFilter))
                        continue;

                    // Filter by ExcludeDisabledJobs
                    if (ExcludeDisabledJobs.IsPresent)
                    {
                        bool isEnabled = GetPSPropertyBool(jobObj, "IsEnabled");
                        if (!isEnabled)
                            continue;
                    }

                    // Exclude MSX jobs (CategoryID = 1)
                    int categoryId = GetPSPropertyInt(jobObj, "CategoryID");
                    if (categoryId == 1)
                        continue;

                    // Filter by Database (check job steps for matching database names)
                    if (dbLookup != null)
                    {
                        if (!JobHasStepInDatabase(jobObj, dbLookup))
                            continue;
                    }

                    // Filter by Category
                    string jobCategory = GetPSPropertyString(jobObj, "Category");
                    if (categoryLookup != null)
                    {
                        if (!categoryLookup.Contains(jobCategory ?? String.Empty))
                            continue;
                    }

                    // Filter by ExcludeCategory
                    if (excludeCategoryLookup != null)
                    {
                        if (excludeCategoryLookup.Contains(jobCategory ?? String.Empty))
                            continue;
                    }

                    // Determine display properties
                    string[] displayProps = DefaultDisplayProperties;

                    // Add execution StartDate if applicable
                    if (executionLookup != null)
                    {
                        Guid jobId = GetPSPropertyGuid(jobObj, "JobId");
                        DateTime startDate;
                        if (jobId != Guid.Empty && executionLookup.TryGetValue(jobId, out startDate))
                        {
                            DbaDateTime dbaStartDate = new DbaDateTime(startDate);
                            AddOrSetProperty(jobObj, "StartDate", dbaStartDate);
                            displayProps = DefaultDisplayPropertiesWithStartDate;
                        }
                    }

                    // Add custom NoteProperties
                    AddOrSetProperty(jobObj, "ComputerName", computerName);
                    AddOrSetProperty(jobObj, "InstanceName", serviceName);
                    AddOrSetProperty(jobObj, "SqlInstance", domainInstanceName);

                    // Add alias properties matching PS1 Select-DefaultView behavior:
                    // 'IsEnabled as Enabled' and 'DateCreated as CreateDate' create live AliasProperties
                    AddAliasProperty(jobObj, "Enabled", "IsEnabled");
                    AddAliasProperty(jobObj, "CreateDate", "DateCreated");

                    // Set default display properties
                    SetDefaultDisplayPropertySet(jobObj, displayProps);

                    WriteObject(jobObj);
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

            Collection<PSObject> results = InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, args);
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
        /// Gets the jobs collection from the server's JobServer.
        /// </summary>
        private Collection<PSObject> GetJobs(object server)
        {
            string script = "param($s) $s.JobServer.Jobs";
            return InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, new object[] { server });
        }

        /// <summary>
        /// Queries msdb for currently executing job information.
        /// Returns a dictionary mapping job IDs to their most recent start execution date.
        /// </summary>
        private Dictionary<Guid, DateTime> GetJobExecutionData(object server)
        {
            Dictionary<Guid, DateTime> result = new Dictionary<Guid, DateTime>();
            try
            {
                string query = @"SELECT [job].[job_id] AS [JobId], [activity].[start_execution_date] AS [StartDate]
FROM [msdb].[dbo].[sysjobs_view] AS [job]
    INNER JOIN [msdb].[dbo].[sysjobactivity] AS [activity] ON [job].[job_id] = [activity].[job_id]
WHERE [activity].[run_requested_date] IS NOT NULL
    AND [activity].[start_execution_date] IS NOT NULL
    AND [activity].[stop_execution_date] IS NULL;";

                string script = "param($s, $q) $s.Query($q)";
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false,
                    ScriptBlock.Create(script),
                    null,
                    new object[] { server, query });

                if (results == null || results.Count == 0)
                    return result;

                foreach (PSObject row in results)
                {
                    if (row == null)
                        continue;

                    object baseObj = row.BaseObject;
                    if (baseObj is DataRow dataRow)
                    {
                        object jobIdObj = dataRow["JobId"];
                        object startDateObj = dataRow["StartDate"];

                        Guid jobId;
                        if (jobIdObj is Guid gid)
                        {
                            jobId = gid;
                        }
                        else if (jobIdObj is byte[] bytes)
                        {
                            jobId = new Guid(bytes);
                        }
                        else if (jobIdObj != null)
                        {
                            Guid parsed;
                            if (Guid.TryParse(jobIdObj.ToString(), out parsed))
                                jobId = parsed;
                            else
                                continue;
                        }
                        else
                        {
                            continue;
                        }

                        DateTime startDate;
                        if (startDateObj is DateTime dt)
                        {
                            startDate = dt;
                        }
                        else if (startDateObj != null)
                        {
                            DateTime parsedDt;
                            if (DateTime.TryParse(startDateObj.ToString(), out parsedDt))
                                startDate = parsedDt;
                            else
                                continue;
                        }
                        else
                        {
                            continue;
                        }

                        // Keep the most recent start date per job (matching Sort-Object -Descending | Select-Object -First 1)
                        DateTime existing;
                        if (!result.TryGetValue(jobId, out existing) || startDate > existing)
                        {
                            result[jobId] = startDate;
                        }
                    }
                    else
                    {
                        // Handle PSObject properties directly (for non-DataRow results)
                        Guid jobId = GetPSPropertyGuid(row, "JobId");
                        if (jobId == Guid.Empty)
                            continue;

                        DateTime startDate = GetPSPropertyDateTime(row, "StartDate");
                        if (startDate == DateTime.MinValue)
                            continue;

                        DateTime existingDt;
                        if (!result.TryGetValue(jobId, out existingDt) || startDate > existingDt)
                        {
                            result[jobId] = startDate;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WriteMessageAtLevel(
                    String.Format("Failed to retrieve job execution data: {0}", ex.Message),
                    MessageLevel.Warning, null);
            }

            return result;
        }

        /// <summary>
        /// Checks whether a job has any step targeting a database in the specified set.
        /// </summary>
        private bool JobHasStepInDatabase(PSObject jobObj, HashSet<string> databases)
        {
            try
            {
                string script = "param($j) $j.JobSteps";
                Collection<PSObject> steps = InvokeCommand.InvokeScript(
                    false,
                    ScriptBlock.Create(script),
                    null,
                    new object[] { jobObj });

                if (steps == null || steps.Count == 0)
                    return false;

                foreach (PSObject step in steps)
                {
                    if (step == null)
                        continue;
                    string dbName = GetPSPropertyString(step, "DatabaseName");
                    if (dbName != null && databases.Contains(dbName))
                        return true;
                }
            }
            catch (Exception ex)
            {
                WriteMessageAtLevel(
                    String.Format("Unable to enumerate job steps for job '{0}': {1}",
                        GetPSPropertyString(jobObj, "Name"), ex.Message),
                    MessageLevel.Warning, null);
            }
            return false;
        }

        /// <summary>
        /// Filters an array of strings, removing null, empty, or whitespace-only values.
        /// Returns null if the input is null or if all values are empty/whitespace.
        /// </summary>
        internal static string[] FilterNonEmptyStrings(string[] input)
        {
            if (input == null)
                return null;

            List<string> result = new List<string>();
            foreach (string s in input)
            {
                if (!String.IsNullOrWhiteSpace(s))
                    result.Add(s);
            }

            if (result.Count == 0)
                return null;

            return result.ToArray();
        }

        /// <summary>
        /// Checks whether a value is in a string array (case-insensitive).
        /// </summary>
        internal static bool IsInStringArray(string value, string[] array)
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
        /// Gets a string property from a PSObject.
        /// </summary>
        internal static string GetPSPropertyString(PSObject obj, string propertyName)
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
        /// Gets a bool property from a PSObject.
        /// </summary>
        internal static bool GetPSPropertyBool(PSObject obj, string propertyName)
        {
            if (obj == null)
                return false;
            try
            {
                PSPropertyInfo prop = obj.Properties[propertyName];
                if (prop != null && prop.Value is bool bVal)
                    return bVal;
                if (prop != null && prop.Value != null)
                {
                    bool parsed;
                    if (Boolean.TryParse(prop.Value.ToString(), out parsed))
                        return parsed;
                }
            }
            catch (Exception)
            {
                // Property may not exist
            }
            return false;
        }

        /// <summary>
        /// Gets an int property from a PSObject.
        /// </summary>
        internal static int GetPSPropertyInt(PSObject obj, string propertyName)
        {
            if (obj == null)
                return 0;
            try
            {
                PSPropertyInfo prop = obj.Properties[propertyName];
                if (prop != null && prop.Value is int iVal)
                    return iVal;
                if (prop != null && prop.Value != null)
                {
                    int parsed;
                    if (Int32.TryParse(prop.Value.ToString(), out parsed))
                        return parsed;
                }
            }
            catch (Exception)
            {
                // Property may not exist
            }
            return 0;
        }

        /// <summary>
        /// Gets a Guid property from a PSObject.
        /// </summary>
        internal static Guid GetPSPropertyGuid(PSObject obj, string propertyName)
        {
            if (obj == null)
                return Guid.Empty;
            try
            {
                PSPropertyInfo prop = obj.Properties[propertyName];
                if (prop != null && prop.Value is Guid gVal)
                    return gVal;
                if (prop != null && prop.Value != null)
                {
                    Guid parsed;
                    if (Guid.TryParse(prop.Value.ToString(), out parsed))
                        return parsed;
                }
            }
            catch (Exception)
            {
                // Property may not exist
            }
            return Guid.Empty;
        }

        /// <summary>
        /// Gets a DateTime property from a PSObject.
        /// </summary>
        internal static DateTime GetPSPropertyDateTime(PSObject obj, string propertyName)
        {
            if (obj == null)
                return DateTime.MinValue;
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
                // Property may not exist
            }
            return DateTime.MinValue;
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
