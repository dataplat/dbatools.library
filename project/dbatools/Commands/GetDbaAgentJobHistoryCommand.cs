using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Text.RegularExpressions;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;
using Dataplat.Dbatools.Utility;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Retrieves SQL Server Agent job execution history from the msdb database.
    /// Returns job history records with calculated fields like duration, start/end dates,
    /// and optionally resolved output file paths with SQL Agent token replacement.
    /// </summary>
    [Cmdlet("Get", "DbaAgentJobHistory", DefaultParameterSetName = "Default")]
    public class GetDbaAgentJobHistoryCommand : DbaInstanceCmdlet
    {
        #region Parameters

        /// <summary>
        /// The target SQL Server instance(s) to target. Overrides base to scope to "Server" parameter set
        /// so it is not mandatory when using the "Collection" parameter set (JobCollection pipeline).
        /// </summary>
        [Parameter(Mandatory = true, ValueFromPipeline = true, ParameterSetName = "Server")]
        public new DbaInstanceParameter[] SqlInstance { get; set; }

        /// <summary>
        /// Specifies specific SQL Agent jobs to retrieve history for by name.
        /// </summary>
        [Parameter(Position = 1)]
        public object[] Job { get; set; }

        /// <summary>
        /// Excludes specified jobs from the history results by name.
        /// </summary>
        [Parameter()]
        public object[] ExcludeJob { get; set; }

        /// <summary>
        /// Sets the earliest date and time for job history records to include.
        /// Defaults to 1900-01-01.
        /// </summary>
        [Parameter()]
        public DateTime StartDate { get; set; } = new DateTime(1900, 1, 1);

        /// <summary>
        /// Sets the latest date and time for job history records to include.
        /// Defaults to the current date and time.
        /// </summary>
        [Parameter()]
        public DateTime EndDate { get; set; } = DateTime.Now;

        /// <summary>
        /// Filters job history to only show executions with a specific completion result.
        /// Valid values: Failed, Succeeded, Retry, Cancelled, InProgress, Unknown.
        /// </summary>
        [Parameter()]
        [ValidateSet("Failed", "Succeeded", "Retry", "Cancelled", "InProgress", "Unknown")]
        public string OutcomeType { get; set; }

        /// <summary>
        /// Returns only job-level execution summaries, excluding individual step details.
        /// </summary>
        [Parameter()]
        public SwitchParameter ExcludeJobSteps { get; set; }

        /// <summary>
        /// Includes resolved output file paths for job steps that write to files.
        /// </summary>
        [Parameter()]
        public SwitchParameter WithOutputFile { get; set; }

        /// <summary>
        /// Accepts an array of SMO job objects for pipeline input from Get-DbaAgentJob.
        /// </summary>
        [Parameter(Mandatory = true, ValueFromPipeline = true, ParameterSetName = "Collection")]
        public object JobCollection { get; set; }

        #endregion Parameters

        /// <summary>
        /// Default display properties without output file info.
        /// </summary>
        private static readonly string[] DefaultDisplayProperties = new string[]
        {
            "ComputerName", "InstanceName", "SqlInstance", "Job", "StepName",
            "RunDate", "StartDate", "EndDate", "Duration", "Status",
            "OperatorEmailed", "Message"
        };

        /// <summary>
        /// Default display properties with output file info.
        /// </summary>
        private static readonly string[] DefaultDisplayPropertiesWithOutput = new string[]
        {
            "ComputerName", "InstanceName", "SqlInstance", "Job", "StepName",
            "RunDate", "StartDate", "EndDate", "Duration", "Status",
            "OperatorEmailed", "Message", "OutputFileName", "RemoteOutputFileName"
        };

        /// <summary>
        /// Regex for matching SQL Agent token placeholders like $(TOKEN) or $(METHOD(TOKEN)).
        /// </summary>
        private static readonly Regex TokenRegex = new Regex(
            @"\$\((?<method>[^()]+)\((?<tok>[^)]+)\)\)|\$\((?<tok>[^)]+)\)",
            RegexOptions.Compiled);

        /// <summary>
        /// Regex for matching a single quote not preceded or followed by another single quote.
        /// </summary>
        private static readonly Regex SquoteRegex = new Regex(@"(?<!')'(?!')", RegexOptions.Compiled);

        /// <summary>
        /// Regex for matching a double quote not preceded or followed by another double quote.
        /// </summary>
        private static readonly Regex DquoteRegex = new Regex(@"(?<!"")""(?!"")", RegexOptions.Compiled);

        /// <summary>
        /// Regex for matching a right bracket not preceded or followed by another right bracket.
        /// </summary>
        private static readonly Regex RbrackRegex = new Regex(@"(?<!])](?!])", RegexOptions.Compiled);

        /// <summary>
        /// Validates parameters in BeginProcessing.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            if (ExcludeJobSteps.IsPresent && WithOutputFile.IsPresent)
            {
                StopFunction("You can't use -ExcludeJobSteps and -WithOutputFile together");
                return;
            }
        }

        /// <summary>
        /// Processes each SQL Server instance or job collection object from pipeline.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (TestFunctionInterrupt()) return;

            if (JobCollection != null)
            {
                ProcessJobCollection(JobCollection);
                return;
            }

            if (SqlInstance == null) return;

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
                        errorRecord: new ErrorRecord(ex, "GetDbaAgentJobHistory_ConnectionError", ErrorCategory.ConnectionError, instance),
                        target: instance,
                        isContinue: true,
                        category: ErrorCategory.ConnectionError);
                    TestFunctionInterrupt();
                    continue;
                }

                if (ExcludeJob != null && ExcludeJob.Length > 0)
                {
                    // Get all jobs, exclude the specified ones, then get history for each remaining
                    string[] allJobNames = GetJobNames(server);
                    if (allJobNames != null)
                    {
                        HashSet<string> excludeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (object ej in ExcludeJob)
                        {
                            if (ej != null)
                                excludeSet.Add(ej.ToString());
                        }

                        foreach (string jobName in allJobNames)
                        {
                            if (!excludeSet.Contains(jobName))
                            {
                                GetJobHistory(server, new object[] { jobName }, instance);
                            }
                        }
                    }
                }
                else
                {
                    GetJobHistory(server, Job, instance);
                }
            }
        }

        #region Core Logic

        /// <summary>
        /// Processes a JobCollection pipeline object - extracts the job name and parent server.
        /// </summary>
        private void ProcessJobCollection(object jobCollection)
        {
            try
            {
                PSObject pso = PSObject.AsPSObject(jobCollection);
                string jobName = GetPSPropertyString(pso, "Name");
                // Job.Parent = JobServer, Job.Parent.Parent = Server
                object parent = GetPSPropertyObject(pso, "Parent");
                if (parent != null)
                {
                    PSObject parentPso = PSObject.AsPSObject(parent);
                    object server = GetPSPropertyObject(parentPso, "Parent");
                    if (server != null)
                    {
                        GetJobHistory(server, new object[] { jobName }, null);
                    }
                }
            }
            catch (Exception ex)
            {
                StopFunction(
                    String.Format("Failed to process job collection item: {0}", ex.Message),
                    exception: ex,
                    target: jobCollection,
                    isContinue: true);
                TestFunctionInterrupt();
            }
        }

        /// <summary>
        /// Retrieves job history from a server, applying filters and producing output objects.
        /// </summary>
        private void GetJobHistory(object server, object[] jobFilter, DbaInstanceParameter instance)
        {
            // Build property map for token resolution
            string computerName = GetServerPropertySafe(server, "ComputerName");
            string serviceName = GetServerPropertySafe(server, "ServiceName");
            string domainInstanceName = GetServerPropertySafe(server, "DomainInstanceName");
            string installDataDirectory = GetServerPropertySafe(server, "InstallDataDirectory");
            string errorLogPath = GetServerPropertySafe(server, "ErrorLogPath");

            Dictionary<string, string> propMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            propMap["INST"] = serviceName ?? String.Empty;
            propMap["MACH"] = computerName ?? String.Empty;
            propMap["SQLDIR"] = installDataDirectory ?? String.Empty;
            propMap["SQLLOGDIR"] = errorLogPath ?? String.Empty;
            propMap["SRVR"] = domainInstanceName ?? String.Empty;

            try
            {
                WriteMessageAtLevel(
                    String.Format("Attempting to get job history from {0}", instance != null ? instance.ToString() : domainInstanceName),
                    MessageLevel.Verbose, null);

                // Get job history executions using the filter
                Collection<PSObject> executions = EnumJobHistory(server, jobFilter);
                if (executions == null || executions.Count == 0)
                    return;

                // Filter out job steps if requested
                List<PSObject> filteredExecutions = new List<PSObject>();
                foreach (PSObject exec in executions)
                {
                    if (exec == null) continue;
                    if (ExcludeJobSteps.IsPresent)
                    {
                        int stepId = GetPSPropertyInt(exec, "StepID");
                        if (stepId != 0) continue;
                    }
                    filteredExecutions.Add(exec);
                }

                // Build output file map if needed
                Dictionary<string, Dictionary<int, string>> outMap = null;
                if (WithOutputFile.IsPresent)
                {
                    outMap = GetOutputFileMap(server, jobFilter);
                }

                // Track the job-level outcome for token resolution (StepID == 0 records)
                PSObject currentOutcome = null;

                foreach (PSObject execution in filteredExecutions)
                {
                    // Calculate status
                    int runStatus = GetPSPropertyInt(execution, "RunStatus");
                    string status = GetStatusString(runStatus);

                    // Add standard properties
                    AddOrSetProperty(execution, "ComputerName", computerName);
                    AddOrSetProperty(execution, "InstanceName", serviceName);
                    AddOrSetProperty(execution, "SqlInstance", domainInstanceName);

                    // Calculate duration from RunDuration (hhmmss format)
                    int runDuration = GetPSPropertyInt(execution, "RunDuration");
                    int durationSeconds = CalculateDurationSeconds(runDuration);

                    DateTime runDate = GetPSPropertyDateTime(execution, "RunDate");
                    DbaDateTime startDate = new DbaDateTime(runDate);
                    DbaDateTime endDate = new DbaDateTime(runDate.AddSeconds(durationSeconds));
                    DbaTimeSpanPretty duration = new DbaTimeSpanPretty(TimeSpan.FromSeconds(durationSeconds));

                    AddOrSetProperty(execution, "StartDate", startDate);
                    AddOrSetProperty(execution, "EndDate", endDate);
                    AddOrSetProperty(execution, "Duration", duration);
                    AddOrSetProperty(execution, "Status", status);

                    // Add alias for JobName as Job
                    AddAliasProperty(execution, "Job", "JobName");

                    if (WithOutputFile.IsPresent)
                    {
                        int stepId = GetPSPropertyInt(execution, "StepID");
                        if (stepId == 0)
                        {
                            currentOutcome = execution;
                        }

                        string outName = String.Empty;
                        string outRemote = String.Empty;
                        try
                        {
                            string jobName = GetPSPropertyString(execution, "JobName");
                            if (outMap != null && jobName != null)
                            {
                                Dictionary<int, string> stepMap;
                                if (outMap.TryGetValue(jobName, out stepMap))
                                {
                                    string rawOutFile;
                                    if (stepMap.TryGetValue(stepId, out rawOutFile) && rawOutFile != null)
                                    {
                                        outName = ResolveJobToken(execution, currentOutcome, rawOutFile, propMap);
                                        outRemote = JoinAdminUNC(computerName, outName);
                                    }
                                }
                            }
                        }
                        catch (Exception outEx)
                        {
                            WriteMessageAtLevel(
                                String.Format("Failed to resolve output file for job '{0}' step {1}: {2}",
                                    GetPSPropertyString(execution, "JobName"),
                                    GetPSPropertyInt(execution, "StepID"),
                                    outEx.Message),
                                MessageLevel.Verbose, null);
                            outName = String.Empty;
                            outRemote = String.Empty;
                        }

                        AddOrSetProperty(execution, "OutputFileName", outName);
                        AddOrSetProperty(execution, "RemoteOutputFileName", outRemote);
                        AddOrSetProperty(execution, "TypeName", "AgentJobHistory");

                        SetDefaultDisplayPropertySet(execution, DefaultDisplayPropertiesWithOutput);
                    }
                    else
                    {
                        AddOrSetProperty(execution, "TypeName", "AgentJobHistory");
                        SetDefaultDisplayPropertySet(execution, DefaultDisplayProperties);
                    }

                    // Insert type name for format.ps1xml matching (matches Select-DefaultView -TypeName behavior)
                    try { execution.TypeNames.Insert(0, "dbatools.AgentJobHistory"); }
                    catch (Exception) { /* best-effort */ }

                    WriteObject(execution);
                }
            }
            catch (Exception ex)
            {
                StopFunction(
                    String.Format("Could not get Agent Job History from {0}", instance != null ? instance.ToString() : domainInstanceName),
                    exception: ex,
                    target: instance,
                    isContinue: true);
                TestFunctionInterrupt();
            }
        }

        #endregion Core Logic

        #region Token Resolution

        /// <summary>
        /// Resolves SQL Agent token placeholders in an output file path.
        /// </summary>
        internal static string ResolveJobToken(PSObject exec, PSObject outcome, string outfile, Dictionary<string, string> propMap)
        {
            if (String.IsNullOrEmpty(outfile))
                return outfile;

            MatchCollection matches = TokenRegex.Matches(outfile);
            foreach (Match match in matches)
            {
                string tok = match.Groups["tok"].Value;
                string escMethod = match.Groups["method"].Value;

                string repl;
                if (propMap.ContainsKey(tok))
                {
                    repl = ResolveTokenEscape(escMethod, propMap[tok]);
                    outfile = outfile.Replace(match.Value, repl);
                }
                else if (String.Equals(tok, "STEPID", StringComparison.OrdinalIgnoreCase))
                {
                    repl = ResolveTokenEscape(escMethod, GetPSPropertyInt(exec, "StepID").ToString());
                    outfile = outfile.Replace(match.Value, repl);
                }
                else if (String.Equals(tok, "JOBID", StringComparison.OrdinalIgnoreCase))
                {
                    Guid jobId = GetPSPropertyGuid(exec, "JobID");
                    // Convert to binary(16) hex format matching SQL Server
                    byte[] bytes = jobId.ToByteArray();
                    System.Text.StringBuilder sb = new System.Text.StringBuilder("0x", 34);
                    for (int i = 0; i < bytes.Length; i++)
                    {
                        sb.Append(bytes[i].ToString("X2"));
                    }
                    repl = ResolveTokenEscape(escMethod, sb.ToString());
                    outfile = outfile.Replace(match.Value, repl);
                }
                else if (String.Equals(tok, "STRTDT", StringComparison.OrdinalIgnoreCase))
                {
                    // Use outcome (job-level) RunDate for STRTDT
                    DateTime outcomeRunDate = outcome != null
                        ? GetPSPropertyDateTime(outcome, "RunDate")
                        : GetPSPropertyDateTime(exec, "RunDate");
                    repl = ResolveTokenEscape(escMethod, outcomeRunDate.ToString("yyyyMMdd"));
                    outfile = outfile.Replace(match.Value, repl);
                }
                else if (String.Equals(tok, "STRTTM", StringComparison.OrdinalIgnoreCase))
                {
                    // Use outcome (job-level) RunDate for STRTTM, convert to int to strip leading zeros
                    DateTime outcomeRunDate = outcome != null
                        ? GetPSPropertyDateTime(outcome, "RunDate")
                        : GetPSPropertyDateTime(exec, "RunDate");
                    int timeVal = Int32.Parse(outcomeRunDate.ToString("HHmmss"));
                    repl = ResolveTokenEscape(escMethod, timeVal.ToString());
                    outfile = outfile.Replace(match.Value, repl);
                }
                else if (String.Equals(tok, "DATE", StringComparison.OrdinalIgnoreCase))
                {
                    DateTime execRunDate = GetPSPropertyDateTime(exec, "RunDate");
                    repl = ResolveTokenEscape(escMethod, execRunDate.ToString("yyyyMMdd"));
                    outfile = outfile.Replace(match.Value, repl);
                }
                else if (String.Equals(tok, "TIME", StringComparison.OrdinalIgnoreCase))
                {
                    DateTime execRunDate = GetPSPropertyDateTime(exec, "RunDate");
                    int timeVal = Int32.Parse(execRunDate.ToString("HHmmss"));
                    repl = ResolveTokenEscape(escMethod, timeVal.ToString());
                    outfile = outfile.Replace(match.Value, repl);
                }
            }

            return outfile;
        }

        /// <summary>
        /// Applies SQL Agent escape method to a token value.
        /// </summary>
        internal static string ResolveTokenEscape(string method, string value)
        {
            if (String.IsNullOrEmpty(method))
                return value;

            if (String.Equals(method, "ESCAPE_SQUOTE", StringComparison.OrdinalIgnoreCase))
                return SquoteRegex.Replace(value, "''");

            if (String.Equals(method, "ESCAPE_DQUOTE", StringComparison.OrdinalIgnoreCase))
                return DquoteRegex.Replace(value, "\"\"");

            if (String.Equals(method, "ESCAPE_RBRACKET", StringComparison.OrdinalIgnoreCase))
                return RbrackRegex.Replace(value, "]]");

            if (String.Equals(method, "ESCAPE_NONE", StringComparison.OrdinalIgnoreCase))
                return value;

            return value;
        }

        /// <summary>
        /// Converts a UNC admin share path from a local path.
        /// E.g., "C:\logs\file.txt" on server "SRV" becomes "\\SRV\C$\logs\file.txt".
        /// </summary>
        internal static string JoinAdminUNC(string computerName, string localPath)
        {
            if (String.IsNullOrEmpty(computerName) || String.IsNullOrEmpty(localPath))
                return String.Empty;

            // If path has a drive letter like C:\..., convert to \\server\C$\...
            if (localPath.Length >= 2 && localPath[1] == ':')
            {
                string driveLetter = localPath.Substring(0, 1);
                string rest = localPath.Substring(2);
                return String.Format("\\\\{0}\\{1}${2}", computerName, driveLetter, rest);
            }

            // Otherwise, just join with backslash
            return String.Format("\\\\{0}\\{1}", computerName, localPath);
        }

        #endregion Token Resolution

        #region Duration Calculation

        /// <summary>
        /// Calculates duration in seconds from RunDuration integer in hhmmss format.
        /// For example, 112 means 1 minute 12 seconds = 72 seconds.
        /// </summary>
        internal static int CalculateDurationSeconds(int runDuration)
        {
            int seconds = runDuration % 100;
            int minutes = (runDuration % 10000) / 100;
            int hours = (runDuration % 1000000) / 10000;
            return seconds + (minutes * 60) + (hours * 3600);
        }

        #endregion Duration Calculation

        #region Status Mapping

        /// <summary>
        /// Maps a RunStatus integer to a human-readable status string.
        /// </summary>
        internal static string GetStatusString(int runStatus)
        {
            switch (runStatus)
            {
                case 0: return "Failed";
                case 1: return "Succeeded";
                case 2: return "Retry";
                case 3: return "Canceled";
                default: return null;
            }
        }

        #endregion Status Mapping

        #region SMO Interaction Helpers

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
        /// Enumerates job history from a server using a JobHistoryFilter.
        /// </summary>
        private Collection<PSObject> EnumJobHistory(object server, object[] jobFilter)
        {
            // Build the filter script that creates and applies JobHistoryFilter
            // We pass StartDate, EndDate, OutcomeType, and optional job names to filter
            string script;
            List<object> argsList = new List<object>();

            if (jobFilter != null && jobFilter.Length > 0)
            {
                script = @"
param($srv, $sd, $ed, $ot, $jobs)
$filter = New-Object Microsoft.SqlServer.Management.Smo.Agent.JobHistoryFilter
$filter.StartRunDate = $sd
$filter.EndRunDate = $ed
if ($ot) { $filter.OutcomeTypes = [Microsoft.SqlServer.Management.Smo.Agent.CompletionResult]$ot }
$results = @()
foreach ($j in $jobs) {
    $filter.JobName = $j
    $results += $srv.JobServer.EnumJobHistory($filter)
}
$results
";
                argsList.Add(server);
                argsList.Add(StartDate);
                argsList.Add(EndDate);
                argsList.Add(TestBound("OutcomeType") ? (object)OutcomeType : null);
                argsList.Add(jobFilter);
            }
            else
            {
                script = @"
param($srv, $sd, $ed, $ot)
$filter = New-Object Microsoft.SqlServer.Management.Smo.Agent.JobHistoryFilter
$filter.StartRunDate = $sd
$filter.EndRunDate = $ed
if ($ot) { $filter.OutcomeTypes = [Microsoft.SqlServer.Management.Smo.Agent.CompletionResult]$ot }
$srv.JobServer.EnumJobHistory($filter)
";
                argsList.Add(server);
                argsList.Add(StartDate);
                argsList.Add(EndDate);
                argsList.Add(TestBound("OutcomeType") ? (object)OutcomeType : null);
            }

            return InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, argsList.ToArray());
        }

        /// <summary>
        /// Gets all job names from the server's JobServer.
        /// </summary>
        private string[] GetJobNames(object server)
        {
            try
            {
                string script = "param($s) $s.JobServer.Jobs.Name";
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, ScriptBlock.Create(script), null, new object[] { server });
                if (results == null || results.Count == 0)
                    return null;

                List<string> names = new List<string>();
                foreach (PSObject r in results)
                {
                    if (r != null && r.BaseObject != null)
                        names.Add(r.BaseObject.ToString());
                }
                return names.ToArray();
            }
            catch (Exception ex)
            {
                WriteMessageAtLevel(
                    String.Format("Failed to retrieve job names: {0}", ex.Message),
                    MessageLevel.Warning, null);
                return null;
            }
        }

        /// <summary>
        /// Gets output file map by calling Get-DbaAgentJobOutputFile.
        /// Returns a nested dictionary: JobName -> StepId -> OutputFileName.
        /// </summary>
        private Dictionary<string, Dictionary<int, string>> GetOutputFileMap(object server, object[] jobFilter)
        {
            Dictionary<string, Dictionary<int, string>> result = new Dictionary<string, Dictionary<int, string>>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string script;
                object[] args;
                if (jobFilter != null && jobFilter.Length > 0)
                {
                    if (SqlCredential != null)
                    {
                        script = "param($s, $c, $j) Get-DbaAgentJobOutputFile -SqlInstance $s -SqlCredential $c -Job $j";
                        args = new object[] { server, SqlCredential, jobFilter };
                    }
                    else
                    {
                        script = "param($s, $j) Get-DbaAgentJobOutputFile -SqlInstance $s -Job $j";
                        args = new object[] { server, jobFilter };
                    }
                }
                else
                {
                    if (SqlCredential != null)
                    {
                        script = "param($s, $c) Get-DbaAgentJobOutputFile -SqlInstance $s -SqlCredential $c";
                        args = new object[] { server, SqlCredential };
                    }
                    else
                    {
                        script = "param($s) Get-DbaAgentJobOutputFile -SqlInstance $s";
                        args = new object[] { server };
                    }
                }

                Collection<PSObject> outfiles = InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, args);
                if (outfiles == null) return result;

                foreach (PSObject outfile in outfiles)
                {
                    if (outfile == null) continue;
                    string jobName = GetPSPropertyString(outfile, "Job");
                    int stepId = GetPSPropertyInt(outfile, "StepId");
                    string outputFileName = GetPSPropertyString(outfile, "OutputFileName");

                    if (jobName == null) continue;

                    Dictionary<int, string> stepMap;
                    if (!result.TryGetValue(jobName, out stepMap))
                    {
                        stepMap = new Dictionary<int, string>();
                        result[jobName] = stepMap;
                    }
                    stepMap[stepId] = outputFileName ?? String.Empty;
                }
            }
            catch (Exception ex)
            {
                WriteMessageAtLevel(
                    String.Format("Failed to retrieve output file information: {0}", ex.Message),
                    MessageLevel.Warning, null);
            }

            return result;
        }

        #endregion SMO Interaction Helpers

        #region PSObject Helpers

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
        /// Gets an object property from a PSObject.
        /// </summary>
        private static object GetPSPropertyObject(PSObject obj, string propertyName)
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
                // Property may not exist
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
        /// Adds an AliasProperty on a PSObject.
        /// </summary>
        internal static void AddAliasProperty(PSObject obj, string aliasName, string referencedPropertyName)
        {
            if (obj == null)
                return;
            try
            {
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

        #endregion PSObject Helpers
    }
}
