using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Searches and filters SQL Agent jobs across SQL Server instances using multiple criteria
    /// including job name, step name, execution status, schedule status, and notification settings.
    /// </summary>
    [Cmdlet("Find", "DbaAgentJob")]
    public class FindDbaAgentJobCommand : DbaInstanceCmdlet
    {
        #region Parameters

        /// <summary>
        /// Specifies agent job names to search for using exact matches or wildcard patterns.
        /// </summary>
        [Parameter(Position = 1)]
        [Alias("Name", "Job")]
        public string[] JobName { get; set; }

        /// <summary>
        /// Excludes specific job names from the search results using exact name matches.
        /// </summary>
        [Parameter()]
        public string[] ExcludeJobName { get; set; }

        /// <summary>
        /// Searches for jobs containing steps with specific names or patterns.
        /// </summary>
        [Parameter()]
        public string[] StepName { get; set; }

        /// <summary>
        /// Finds jobs that haven't executed successfully in the specified number of days.
        /// </summary>
        [Parameter()]
        public int LastUsed { get; set; }

        /// <summary>
        /// Finds all jobs with disabled status.
        /// </summary>
        [Parameter()]
        [Alias("Disabled")]
        public SwitchParameter IsDisabled { get; set; }

        /// <summary>
        /// Finds jobs where the last execution resulted in a failure status.
        /// </summary>
        [Parameter()]
        [Alias("Failed")]
        public SwitchParameter IsFailed { get; set; }

        /// <summary>
        /// Finds jobs that have no schedule defined.
        /// </summary>
        [Parameter()]
        [Alias("NoSchedule")]
        public SwitchParameter IsNotScheduled { get; set; }

        /// <summary>
        /// Finds jobs that lack email notification setup.
        /// </summary>
        [Parameter()]
        [Alias("NoEmailNotification")]
        public SwitchParameter IsNoEmailNotification { get; set; }

        /// <summary>
        /// Filters jobs by their assigned categories.
        /// </summary>
        [Parameter()]
        public string[] Category { get; set; }

        /// <summary>
        /// Filters jobs by their owner login name, or excludes jobs by prefixing with a dash (-).
        /// </summary>
        [Parameter()]
        public string Owner { get; set; }

        /// <summary>
        /// Limits results to jobs that last ran on or after the specified date and time.
        /// </summary>
        [Parameter()]
        public DateTime Since { get; set; }

        #endregion Parameters

        /// <summary>
        /// Default display properties matching the PS1 Select-DefaultView.
        /// </summary>
        private static readonly string[] DefaultDisplayProperties = new string[]
        {
            "ComputerName", "InstanceName", "SqlInstance", "Name", "Category",
            "OwnerLoginName", "CurrentRunStatus", "CurrentRunRetryAttempt",
            "Enabled", "LastRunDate", "LastRunOutcome", "DateCreated",
            "HasSchedule", "OperatorToEmail", "CreateDate"
        };

        /// <summary>
        /// Validates that at least one search criterion was specified.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            bool hasAnyCriteria = IsFailed.IsPresent
                || TestBound("JobName")
                || TestBound("StepName")
                || TestBound("LastUsed")
                || IsDisabled.IsPresent
                || IsNotScheduled.IsPresent
                || IsNoEmailNotification.IsPresent
                || TestBound("Category")
                || TestBound("Owner")
                || TestBound("ExcludeJobName");

            if (!hasAnyCriteria)
            {
                StopFunction("At least one search term must be specified");
                return;
            }
        }

        /// <summary>
        /// Processes each SQL Server instance, retrieves and filters agent jobs.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (TestFunctionInterrupt()) return;

            foreach (DbaInstanceParameter instance in SqlInstance)
            {
                WriteMessageAtLevel(
                    String.Format("Running Scan on: {0}", instance),
                    MessageLevel.Verbose, null);

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
                        errorRecord: new ErrorRecord(ex, "FindDbaAgentJob_ConnectionError", ErrorCategory.ConnectionError, instance),
                        target: instance,
                        isContinue: true,
                        category: ErrorCategory.ConnectionError);
                    TestFunctionInterrupt();
                    continue;
                }

                // Get all jobs from the server
                Collection<PSObject> allJobs;
                try
                {
                    allJobs = GetJobs(server);
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

                if (allJobs == null || allJobs.Count == 0)
                    continue;

                // Phase 1: Initial job filtering (replicate Get-JobList behavior)
                // When both JobName and StepName are set, StepName overwrites (matches PS1)
                List<PSObject> jobs;
                List<PSObject> output;

                if (TestBound("JobName"))
                {
                    WriteMessageAtLevel("Retrieving jobs by their name.", MessageLevel.Verbose, null);
                    jobs = FilterJobsByName(allJobs, JobName);
                    output = new List<PSObject>(jobs);
                }
                else
                {
                    jobs = new List<PSObject>(allJobs);
                    output = new List<PSObject>(jobs);
                }

                if (TestBound("StepName"))
                {
                    WriteMessageAtLevel("Retrieving jobs by their step names.", MessageLevel.Verbose, null);
                    jobs = FilterJobsByStepName(allJobs, StepName);
                    output = new List<PSObject>(jobs);
                }
                else if (!TestBound("JobName"))
                {
                    WriteMessageAtLevel("Retrieving all jobs", MessageLevel.Verbose, null);
                }

                // Phase 2: Criteria filters (each overwrites output from jobs - last one wins, matching PS1)
                if (TestBound("Category"))
                {
                    WriteMessageAtLevel("Finding job/s that have the specified category defined", MessageLevel.Verbose, null);
                    output = FilterByCategory(jobs, Category);
                }

                if (IsFailed.IsPresent)
                {
                    WriteMessageAtLevel("Checking for failed jobs.", MessageLevel.Verbose, null);
                    output = FilterByFailed(jobs);
                }

                if (TestBound("LastUsed"))
                {
                    int daysBack = LastUsed * -1;
                    DateTime sinceDate = DateTime.Now.AddDays(daysBack);
                    WriteMessageAtLevel(
                        String.Format("Finding job/s not ran in last {0} days", LastUsed),
                        MessageLevel.Verbose, null);
                    output = FilterByLastUsed(jobs, sinceDate);
                }

                if (IsDisabled.IsPresent)
                {
                    WriteMessageAtLevel("Finding job/s that are disabled", MessageLevel.Verbose, null);
                    output = FilterByDisabled(jobs);
                }

                if (IsNotScheduled.IsPresent)
                {
                    WriteMessageAtLevel("Finding job/s that have no schedule defined", MessageLevel.Verbose, null);
                    output = FilterByNotScheduled(jobs);
                }

                if (IsNoEmailNotification.IsPresent)
                {
                    WriteMessageAtLevel("Finding job/s that have no email operator defined", MessageLevel.Verbose, null);
                    output = FilterByNoEmailNotification(jobs);
                }

                if (TestBound("Owner"))
                {
                    WriteMessageAtLevel("Finding job/s with owner criteria", MessageLevel.Verbose, null);
                    if (Owner.Contains("-"))
                    {
                        string ownerDisplay = Owner.Replace("-", "");
                        WriteMessageAtLevel(
                            String.Format("Checking for jobs that NOT owned by: {0}", ownerDisplay),
                            MessageLevel.Verbose, null);
                    }
                    else
                    {
                        WriteMessageAtLevel(
                            String.Format("Checking for jobs that are owned by: {0}", Owner),
                            MessageLevel.Verbose, null);
                    }
                    output = FilterByOwner(jobs, Owner);
                }

                // Phase 3: Exclusion filters (filter from output, not jobs)
                if (TestBound("ExcludeJobName"))
                {
                    WriteMessageAtLevel("Excluding job/s based on Exclude", MessageLevel.Verbose, null);
                    output = FilterExcludeJobName(output, ExcludeJobName);
                }

                if (TestBound("Since"))
                {
                    WriteMessageAtLevel(
                        String.Format("Getting only jobs whose LastRunDate is greater than or equal to {0}", Since),
                        MessageLevel.Verbose, null);
                    output = FilterBySince(output, Since);
                }

                // Phase 4: Deduplicate (matching PS1 Select-Object -Unique)
                HashSet<object> seen = new HashSet<object>();
                List<PSObject> uniqueJobs = new List<PSObject>();
                foreach (PSObject job in output)
                {
                    if (job == null) continue;
                    object baseObj = job.BaseObject ?? (object)job;
                    if (seen.Add(baseObj))
                    {
                        uniqueJobs.Add(job);
                    }
                }

                // Get server connection info
                string computerName = GetDbaAgentJobCommand.GetServerPropertySafe(server, "ComputerName");
                string serviceName = GetDbaAgentJobCommand.GetServerPropertySafe(server, "ServiceName");
                string domainInstanceName = GetDbaAgentJobCommand.GetServerPropertySafe(server, "DomainInstanceName");

                foreach (PSObject job in uniqueJobs)
                {
                    string jobName = GetDbaAgentJobCommand.GetPSPropertyString(job, "Name");

                    GetDbaAgentJobCommand.AddOrSetProperty(job, "ComputerName", computerName);
                    GetDbaAgentJobCommand.AddOrSetProperty(job, "InstanceName", serviceName);
                    GetDbaAgentJobCommand.AddOrSetProperty(job, "SqlInstance", domainInstanceName);
                    GetDbaAgentJobCommand.AddOrSetProperty(job, "JobName", jobName);

                    // Add alias properties for default view
                    GetDbaAgentJobCommand.AddAliasProperty(job, "Enabled", "IsEnabled");
                    GetDbaAgentJobCommand.AddAliasProperty(job, "CreateDate", "DateCreated");

                    GetDbaAgentJobCommand.SetDefaultDisplayPropertySet(job, DefaultDisplayProperties);

                    WriteObject(job);
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
        /// Gets the jobs collection from the server's JobServer.
        /// </summary>
        private Collection<PSObject> GetJobs(object server)
        {
            string script = "param($s) $s.JobServer.Jobs";
            return InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, new object[] { server });
        }

        /// <summary>
        /// Filters jobs by name patterns (replicating Get-JobList -JobFilter behavior).
        /// Uses WildcardPattern for all filters (matching PS1 -Like behavior).
        /// </summary>
        internal static List<PSObject> FilterJobsByName(Collection<PSObject> allJobs, string[] jobFilters)
        {
            List<PSObject> result = new List<PSObject>();
            if (allJobs == null || jobFilters == null)
                return result;

            // Pre-compile wildcard patterns once before iterating jobs
            List<WildcardPattern> patterns = new List<WildcardPattern>(jobFilters.Length);
            foreach (string filter in jobFilters)
            {
                if (!String.IsNullOrEmpty(filter))
                    patterns.Add(new WildcardPattern(filter, WildcardOptions.IgnoreCase));
            }
            if (patterns.Count == 0) return result;

            foreach (PSObject job in allJobs)
            {
                if (job == null) continue;
                string name = GetDbaAgentJobCommand.GetPSPropertyString(job, "Name");
                if (name == null) continue;

                foreach (WildcardPattern pattern in patterns)
                {
                    if (pattern.IsMatch(name))
                    {
                        result.Add(job);
                        break;
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Filters jobs by step name patterns (replicating Get-JobList -StepFilter behavior).
        /// </summary>
        private List<PSObject> FilterJobsByStepName(Collection<PSObject> allJobs, string[] stepFilters)
        {
            List<PSObject> result = new List<PSObject>();
            if (allJobs == null || stepFilters == null)
                return result;

            // Pre-compile wildcard patterns once before iterating jobs
            List<WildcardPattern> patterns = new List<WildcardPattern>(stepFilters.Length);
            foreach (string filter in stepFilters)
            {
                if (!String.IsNullOrEmpty(filter))
                    patterns.Add(new WildcardPattern(filter, WildcardOptions.IgnoreCase));
            }
            if (patterns.Count == 0) return result;

            foreach (PSObject job in allJobs)
            {
                if (job == null) continue;

                Collection<PSObject> steps = GetJobSteps(job);
                if (steps == null || steps.Count == 0) continue;

                bool matched = false;
                foreach (PSObject step in steps)
                {
                    if (step == null) continue;
                    string stepName = GetDbaAgentJobCommand.GetPSPropertyString(step, "Name");
                    if (stepName == null) continue;

                    foreach (WildcardPattern pattern in patterns)
                    {
                        if (pattern.IsMatch(stepName))
                        {
                            matched = true;
                            break;
                        }
                    }
                    if (matched) break;
                }

                if (matched)
                {
                    result.Add(job);
                }
            }
            return result;
        }

        /// <summary>
        /// Gets the job steps collection from a job object.
        /// </summary>
        private Collection<PSObject> GetJobSteps(PSObject job)
        {
            try
            {
                string script = "param($j) $j.JobSteps";
                return InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, new object[] { job });
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Filters jobs by category (case-insensitive containment check).
        /// </summary>
        internal static List<PSObject> FilterByCategory(List<PSObject> jobs, string[] categories)
        {
            List<PSObject> result = new List<PSObject>();
            if (jobs == null || categories == null) return result;

            HashSet<string> catSet = new HashSet<string>(categories, StringComparer.OrdinalIgnoreCase);
            foreach (PSObject job in jobs)
            {
                if (job == null) continue;
                string cat = GetDbaAgentJobCommand.GetPSPropertyString(job, "Category");
                if (cat != null && catSet.Contains(cat))
                {
                    result.Add(job);
                }
            }
            return result;
        }

        /// <summary>
        /// Filters jobs where LastRunOutcome is "Failed".
        /// </summary>
        internal static List<PSObject> FilterByFailed(List<PSObject> jobs)
        {
            List<PSObject> result = new List<PSObject>();
            if (jobs == null) return result;

            foreach (PSObject job in jobs)
            {
                if (job == null) continue;
                string outcome = GetDbaAgentJobCommand.GetPSPropertyString(job, "LastRunOutcome");
                if (String.Equals(outcome, "Failed", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(job);
                }
            }
            return result;
        }

        /// <summary>
        /// Filters jobs that haven't run since the given date.
        /// </summary>
        internal static List<PSObject> FilterByLastUsed(List<PSObject> jobs, DateTime sinceDate)
        {
            List<PSObject> result = new List<PSObject>();
            if (jobs == null) return result;

            foreach (PSObject job in jobs)
            {
                if (job == null) continue;
                DateTime lastRun = GetDbaAgentJobCommand.GetPSPropertyDateTime(job, "LastRunDate");
                if (lastRun <= sinceDate)
                {
                    result.Add(job);
                }
            }
            return result;
        }

        /// <summary>
        /// Filters jobs that are disabled (IsEnabled == false).
        /// </summary>
        internal static List<PSObject> FilterByDisabled(List<PSObject> jobs)
        {
            List<PSObject> result = new List<PSObject>();
            if (jobs == null) return result;

            foreach (PSObject job in jobs)
            {
                if (job == null) continue;
                bool enabled = GetDbaAgentJobCommand.GetPSPropertyBool(job, "IsEnabled");
                if (!enabled)
                {
                    result.Add(job);
                }
            }
            return result;
        }

        /// <summary>
        /// Filters jobs that have no schedule (HasSchedule == false).
        /// </summary>
        internal static List<PSObject> FilterByNotScheduled(List<PSObject> jobs)
        {
            List<PSObject> result = new List<PSObject>();
            if (jobs == null) return result;

            foreach (PSObject job in jobs)
            {
                if (job == null) continue;
                bool hasSchedule = GetDbaAgentJobCommand.GetPSPropertyBool(job, "HasSchedule");
                if (!hasSchedule)
                {
                    result.Add(job);
                }
            }
            return result;
        }

        /// <summary>
        /// Filters jobs that have no email notification operator.
        /// </summary>
        internal static List<PSObject> FilterByNoEmailNotification(List<PSObject> jobs)
        {
            List<PSObject> result = new List<PSObject>();
            if (jobs == null) return result;

            foreach (PSObject job in jobs)
            {
                if (job == null) continue;
                string email = GetDbaAgentJobCommand.GetPSPropertyString(job, "OperatorToEmail");
                if (String.IsNullOrEmpty(email))
                {
                    result.Add(job);
                }
            }
            return result;
        }

        /// <summary>
        /// Filters jobs by owner. If Owner contains a dash, removes dashes and excludes that owner.
        /// NOTE: This replicates a quirk in the PS1 where ($Owner -match "-") triggers exclude mode
        /// for any dash anywhere in the string (e.g. "DOMAIN-User" would also trigger exclude).
        /// Preserved intentionally for behavioral compatibility with the original PS1.
        /// </summary>
        internal static List<PSObject> FilterByOwner(List<PSObject> jobs, string owner)
        {
            List<PSObject> result = new List<PSObject>();
            if (jobs == null || owner == null) return result;

            if (owner.Contains("-"))
            {
                // Exclude mode: remove all dashes and exclude matching owner
                string ownerMatch = owner.Replace("-", "");
                foreach (PSObject job in jobs)
                {
                    if (job == null) continue;
                    string loginName = GetDbaAgentJobCommand.GetPSPropertyString(job, "OwnerLoginName");
                    if (!String.Equals(loginName, ownerMatch, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(job);
                    }
                }
            }
            else
            {
                // Include mode
                foreach (PSObject job in jobs)
                {
                    if (job == null) continue;
                    string loginName = GetDbaAgentJobCommand.GetPSPropertyString(job, "OwnerLoginName");
                    if (String.Equals(loginName, owner, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(job);
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Excludes jobs by exact name match.
        /// </summary>
        internal static List<PSObject> FilterExcludeJobName(List<PSObject> output, string[] excludeNames)
        {
            List<PSObject> result = new List<PSObject>();
            if (output == null || excludeNames == null) return result;

            HashSet<string> excludeSet = new HashSet<string>(excludeNames, StringComparer.OrdinalIgnoreCase);
            foreach (PSObject job in output)
            {
                if (job == null) continue;
                string name = GetDbaAgentJobCommand.GetPSPropertyString(job, "Name");
                if (name == null || !excludeSet.Contains(name))
                {
                    result.Add(job);
                }
            }
            return result;
        }

        /// <summary>
        /// Filters jobs where LastRunDate is greater than or equal to the specified date.
        /// </summary>
        internal static List<PSObject> FilterBySince(List<PSObject> output, DateTime since)
        {
            List<PSObject> result = new List<PSObject>();
            if (output == null) return result;

            foreach (PSObject job in output)
            {
                if (job == null) continue;
                DateTime lastRun = GetDbaAgentJobCommand.GetPSPropertyDateTime(job, "LastRunDate");
                if (lastRun >= since)
                {
                    result.Add(job);
                }
            }
            return result;
        }

        #endregion Helpers
    }
}
