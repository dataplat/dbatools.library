using System;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Retrieves output file paths configured for SQL Agent job steps.
    /// Returns both the local file path and the UNC path for remote access,
    /// but only for job steps that have an output file configured.
    /// </summary>
    [Cmdlet("Get", "DbaAgentJobOutputFile")]
    public class GetDbaAgentJobOutputFileCommand : DbaInstanceCmdlet
    {
        #region Parameters

        /// <summary>
        /// Specifies specific SQL Agent job names to examine for output file configurations.
        /// </summary>
        [Parameter()]
        public object[] Job { get; set; }

        /// <summary>
        /// Specifies SQL Agent jobs to exclude from the output file search.
        /// </summary>
        [Parameter()]
        public object[] ExcludeJob { get; set; }

        #endregion Parameters

        /// <summary>
        /// Default display properties (StepId is excluded from default view).
        /// </summary>
        private static readonly string[] DefaultDisplayProperties = new string[]
        {
            "ComputerName", "InstanceName", "SqlInstance", "Job", "JobStep",
            "OutputFileName", "RemoteOutputFileName"
        };

        /// <summary>
        /// Connects to each SQL Server instance and retrieves job step output file paths.
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
                        errorRecord: new ErrorRecord(ex, "GetDbaAgentJobOutputFile_ConnectionError", ErrorCategory.ConnectionError, instance),
                        target: instance,
                        isContinue: true,
                        category: ErrorCategory.ConnectionError);
                    TestFunctionInterrupt();
                    continue;
                }

                // Get server connection info for output properties
                string computerName = GetServerPropertySafe(server, "ComputerName");
                string serviceName = GetServerPropertySafe(server, "ServiceName");
                string domainInstanceName = GetServerPropertySafe(server, "DomainInstanceName");

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

                // Filter by Job include
                if (TestBound("Job"))
                {
                    jobs = FilterJobsByName(jobs, Job, include: true);
                }

                // Filter by ExcludeJob
                if (TestBound("ExcludeJob"))
                {
                    jobs = FilterJobsByName(jobs, ExcludeJob, include: false);
                }

                // Iterate each job and its steps
                foreach (PSObject jobObj in jobs)
                {
                    if (jobObj == null)
                        continue;

                    string jobName = GetPSPropertyString(jobObj, "Name");

                    Collection<PSObject> steps;
                    try
                    {
                        steps = GetJobSteps(jobObj);
                    }
                    catch (Exception ex)
                    {
                        StopFunction(
                            String.Format("Failed to retrieve steps for job '{0}' on {1}", jobName, instance),
                            exception: ex,
                            target: jobObj,
                            isContinue: true);
                        TestFunctionInterrupt();
                        continue;
                    }

                    if (steps == null || steps.Count == 0)
                        continue;

                    foreach (PSObject step in steps)
                    {
                        if (step == null)
                            continue;

                        string outputFileName = GetPSPropertyString(step, "OutputFileName");

                        if (!String.IsNullOrEmpty(outputFileName))
                        {
                            string stepName = GetPSPropertyString(step, "Name");
                            int stepId = GetPSPropertyInt(step, "Id");
                            string remoteOutputFileName = JoinAdminUnc(computerName, outputFileName);

                            PSObject result = new PSObject();
                            result.Properties.Add(new PSNoteProperty("ComputerName", computerName));
                            result.Properties.Add(new PSNoteProperty("InstanceName", serviceName));
                            result.Properties.Add(new PSNoteProperty("SqlInstance", domainInstanceName));
                            result.Properties.Add(new PSNoteProperty("Job", jobName));
                            result.Properties.Add(new PSNoteProperty("JobStep", stepName));
                            result.Properties.Add(new PSNoteProperty("OutputFileName", outputFileName));
                            result.Properties.Add(new PSNoteProperty("RemoteOutputFileName", remoteOutputFileName));
                            result.Properties.Add(new PSNoteProperty("StepId", stepId));

                            SetDefaultDisplayPropertySet(result, DefaultDisplayProperties);

                            WriteObject(result);
                        }
                        else
                        {
                            string stepName = GetPSPropertyString(step, "Name");
                            WriteMessageAtLevel(
                                String.Format("{0} for {1} has no output file", stepName, jobName),
                                MessageLevel.Verbose, null);
                        }
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
            return InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { server });
        }

        /// <summary>
        /// Gets the job steps collection from a job object.
        /// </summary>
        private Collection<PSObject> GetJobSteps(PSObject jobObj)
        {
            string script = "param($j) $j.JobSteps";
            return InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { jobObj });
        }

        /// <summary>
        /// Filters jobs collection by name using include or exclude logic.
        /// Matches PS1's Where-Object Name -In / -NotIn behavior.
        /// </summary>
        private static Collection<PSObject> FilterJobsByName(Collection<PSObject> jobs, object[] filterNames, bool include)
        {
            // Build a set of filter names as strings
            string[] names = ConvertToStringArray(filterNames);
            if (names == null || names.Length == 0)
                return include ? new Collection<PSObject>() : jobs;

            Collection<PSObject> result = new Collection<PSObject>();
            foreach (PSObject job in jobs)
            {
                if (job == null)
                    continue;

                string jobName = GetPSPropertyString(job, "Name");
                bool isInList = IsInStringArray(jobName, names);

                if (include && isInList)
                    result.Add(job);
                else if (!include && !isInList)
                    result.Add(job);
            }
            return result;
        }

        /// <summary>
        /// Converts an object array to a string array (handles object[] from PS1 parameter).
        /// </summary>
        internal static string[] ConvertToStringArray(object[] input)
        {
            if (input == null)
                return null;

            string[] result = new string[input.Length];
            for (int i = 0; i < input.Length; i++)
            {
                result[i] = input[i] != null ? input[i].ToString() : null;
            }
            return result;
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
        /// Converts a local file path to an admin UNC path.
        /// Mirrors the Join-AdminUnc PowerShell function behavior.
        /// </summary>
        internal static string JoinAdminUnc(string serverName, string filePath)
        {
            // If filepath is null/empty, on Linux/Mac, or already a UNC path, return as-is
            if (String.IsNullOrEmpty(filePath))
                return filePath;

            if (filePath.StartsWith("\\\\", StringComparison.Ordinal))
                return filePath;

            // Check for non-Windows platforms (RuntimeInformation not available on net472)
            bool isNonWindows = false;
#if NETCOREAPP
            isNonWindows = !System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows);
#endif
            if (isNonWindows)
                return filePath;

            // If serverName is null/empty, cannot build UNC path
            if (String.IsNullOrEmpty(serverName))
                return filePath;

            // Extract just the computer name (before backslash for named instances)
            if (serverName.Contains("\\"))
            {
                serverName = serverName.Split('\\')[0];
            }

            // Convert drive letter path: C:\path -> \\server\C$\path
            if (filePath.Length >= 2 && filePath[1] == ':')
            {
                string driveLetter = filePath.Substring(0, 1);
                string rest = filePath.Substring(2);
                return String.Format("\\\\{0}\\{1}${2}", serverName, driveLetter, rest);
            }

            // No drive letter — just join server and path
            return String.Format("\\\\{0}\\{1}", serverName, filePath);
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
