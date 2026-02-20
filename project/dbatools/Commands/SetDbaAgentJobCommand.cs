using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Modifies existing SQL Server Agent job properties and notification settings.
    /// Updates various properties of SQL Server Agent jobs including job name, description,
    /// owner, enabled/disabled status, notification settings, and schedule assignments.
    /// </summary>
    [Cmdlet("Set", "DbaAgentJob", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low)]
    [OutputType("Microsoft.SqlServer.Management.Smo.Agent.Job")]
    public class SetDbaAgentJobCommand : DbaBaseCmdlet
    {
        #region Parameters

        /// <summary>
        /// The target SQL Server instance or instances.
        /// </summary>
        [Parameter()]
        public DbaInstanceParameter[] SqlInstance { get; set; }

        /// <summary>
        /// Login to the target instance using alternative credentials.
        /// </summary>
        [Parameter()]
        [Alias("Credential", "Cred")]
        public PSCredential SqlCredential { get; set; }

        /// <summary>
        /// Specifies the name(s) of the SQL Server Agent jobs to modify.
        /// </summary>
        [Parameter()]
        public object[] Job { get; set; }

        /// <summary>
        /// Attaches existing shared schedules to the job by name.
        /// </summary>
        [Parameter()]
        public object[] Schedule { get; set; }

        /// <summary>
        /// Attaches existing shared schedules to the job by their numeric ID.
        /// </summary>
        [Parameter()]
        public int[] ScheduleId { get; set; }

        /// <summary>
        /// Renames the job to the specified name.
        /// </summary>
        [Parameter()]
        public string NewName { get; set; }

        /// <summary>
        /// Enables the job so it can be executed.
        /// </summary>
        [Parameter()]
        public SwitchParameter Enabled { get; set; }

        /// <summary>
        /// Disables the job to prevent it from running.
        /// </summary>
        [Parameter()]
        public SwitchParameter Disabled { get; set; }

        /// <summary>
        /// Updates the job's description field.
        /// </summary>
        [Parameter()]
        public string Description { get; set; }

        /// <summary>
        /// Sets which job step should execute first when the job runs.
        /// </summary>
        [Parameter()]
        public int StartStepId { get; set; }

        /// <summary>
        /// Assigns the job to a specific job category.
        /// </summary>
        [Parameter()]
        public string Category { get; set; }

        /// <summary>
        /// Changes the job owner to the specified SQL Server login.
        /// </summary>
        [Parameter()]
        public string OwnerLogin { get; set; }

        /// <summary>
        /// Controls when job execution results are logged to the Windows Application Event Log.
        /// </summary>
        [Parameter()]
        [ValidateSet("0", "Never", "1", "OnSuccess", "2", "OnFailure", "3", "Always")]
        public object EventLogLevel { get; set; }

        /// <summary>
        /// Determines when to send email notifications about job completion.
        /// </summary>
        [Parameter()]
        [ValidateSet("0", "Never", "1", "OnSuccess", "2", "OnFailure", "3", "Always")]
        public object EmailLevel { get; set; }

        /// <summary>
        /// Controls when to send network messages about job completion.
        /// </summary>
        [Parameter()]
        [ValidateSet("0", "Never", "1", "OnSuccess", "2", "OnFailure", "3", "Always")]
        public object NetsendLevel { get; set; }

        /// <summary>
        /// Determines when to send pager notifications about job completion.
        /// </summary>
        [Parameter()]
        [ValidateSet("0", "Never", "1", "OnSuccess", "2", "OnFailure", "3", "Always")]
        public object PageLevel { get; set; }

        /// <summary>
        /// Specifies which SQL Server Agent operator receives email notifications.
        /// </summary>
        [Parameter()]
        public string EmailOperator { get; set; }

        /// <summary>
        /// Specifies which SQL Server Agent operator receives network messages.
        /// </summary>
        [Parameter()]
        public string NetsendOperator { get; set; }

        /// <summary>
        /// Specifies which SQL Server Agent operator receives pager notifications.
        /// </summary>
        [Parameter()]
        public string PageOperator { get; set; }

        /// <summary>
        /// Controls when the job should automatically delete itself after execution.
        /// </summary>
        [Parameter()]
        [ValidateSet("0", "Never", "1", "OnSuccess", "2", "OnFailure", "3", "Always")]
        public object DeleteLevel { get; set; }

        /// <summary>
        /// Bypasses validation checks and creates missing job categories when specified.
        /// </summary>
        [Parameter()]
        public SwitchParameter Force { get; set; }

        /// <summary>
        /// Accepts SQL Server Agent job objects from the pipeline, typically from Get-DbaAgentJob.
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        public PSObject[] InputObject { get; set; }

        #endregion Parameters

        /// <summary>
        /// Collected job objects from pipeline and instance resolution.
        /// </summary>
        private List<PSObject> _collectedJobs = new List<PSObject>();

        private int _eventLogLevel;
        private int _emailLevel;
        private int _netsendLevel;
        private int _pageLevel;
        private int _deleteLevel;

        /// <summary>
        /// Validates parameter combinations and converts level parameters to integers.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            if (Force.IsPresent)
            {
                SessionState.PSVariable.Set("ConfirmPreference", "None");
            }

            // Convert level parameters from string/int to int
            if (TestBound("EventLogLevel"))
            {
                _eventLogLevel = ConvertCompletionLevel(EventLogLevel);
            }
            if (TestBound("EmailLevel"))
            {
                _emailLevel = ConvertCompletionLevel(EmailLevel);
            }
            if (TestBound("NetsendLevel"))
            {
                _netsendLevel = ConvertCompletionLevel(NetsendLevel);
            }
            if (TestBound("PageLevel"))
            {
                _pageLevel = ConvertCompletionLevel(PageLevel);
            }
            if (TestBound("DeleteLevel"))
            {
                _deleteLevel = ConvertCompletionLevel(DeleteLevel);
            }

            // Validate email level/operator pair
            if (TestBound("EmailLevel") && _emailLevel >= 1 && String.IsNullOrEmpty(EmailOperator))
            {
                StopFunction(
                    "Please set the e-mail operator when the e-mail level parameter is set.",
                    target: SqlInstance);
                return;
            }
            if (!String.IsNullOrEmpty(EmailOperator) && !TestBound("EmailLevel"))
            {
                StopFunction(
                    "Please set the e-mail level parameter when the e-mail level operator is set.",
                    target: SqlInstance);
                return;
            }

            // Validate netsend level/operator pair
            if (TestBound("NetsendLevel") && _netsendLevel >= 1 && String.IsNullOrEmpty(NetsendOperator))
            {
                StopFunction(
                    "Please set the netsend operator when the netsend level parameter is set.",
                    target: SqlInstance);
                return;
            }
            if (!String.IsNullOrEmpty(NetsendOperator) && !TestBound("NetsendLevel"))
            {
                StopFunction(
                    "Please set the net send level parameter when the net send level operator is set.",
                    target: SqlInstance);
                return;
            }

            // Validate page level/operator pair
            if (TestBound("PageLevel") && _pageLevel >= 1 && String.IsNullOrEmpty(PageOperator))
            {
                StopFunction(
                    "Please set the page operator when the page level parameter is set.",
                    target: SqlInstance);
                return;
            }
            if (!String.IsNullOrEmpty(PageOperator) && !TestBound("PageLevel"))
            {
                StopFunction(
                    "Please set the page level parameter when the page level operator is set.",
                    target: SqlInstance);
                return;
            }
        }

        /// <summary>
        /// Collects job objects from pipeline input and resolves job names from instances.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (TestFunctionInterrupt()) return;

            if ((InputObject == null || InputObject.Length == 0) && (Job == null || Job.Length == 0))
            {
                StopFunction(
                    "You must specify a job name or pipe in results from another command",
                    target: SqlInstance);
                return;
            }

            // Collect pipeline InputObject items
            if (InputObject != null)
            {
                foreach (PSObject obj in InputObject)
                {
                    if (obj != null)
                        _collectedJobs.Add(obj);
                }
            }

            // Resolve jobs from SqlInstance + Job names
            if (SqlInstance != null)
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
                            errorRecord: new ErrorRecord(ex, "SetDbaAgentJob_ConnectionError", ErrorCategory.ConnectionError, instance),
                            target: instance,
                            isContinue: true,
                            category: ErrorCategory.ConnectionError);
                        TestFunctionInterrupt();
                        continue;
                    }

                    if (Job != null)
                    {
                        foreach (object j in Job)
                        {
                            string jobName = (j != null) ? j.ToString() : null;
                            if (String.IsNullOrEmpty(jobName))
                                continue;

                            if (!CheckJobExists(server, jobName))
                            {
                                StopFunction(
                                    String.Format("Job {0} doesn't exist on {1}", jobName, instance),
                                    target: instance,
                                    isContinue: true);
                                TestFunctionInterrupt();
                                continue;
                            }
                            else
                            {
                                try
                                {
                                    PSObject jobObj = GetJobByName(server, jobName);
                                    if (jobObj != null)
                                    {
                                        RefreshJob(jobObj);
                                        _collectedJobs.Add(jobObj);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    StopFunction(
                                        "Something went wrong retrieving the job",
                                        exception: ex,
                                        target: j,
                                        isContinue: true);
                                    TestFunctionInterrupt();
                                    continue;
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Processes all collected jobs: applies modifications and outputs results.
        /// </summary>
        protected override void EndProcessing()
        {
            foreach (PSObject currentJob in _collectedJobs)
            {
                if (TestFunctionInterrupt()) return;

                object server = GetJobServer(currentJob);
                string jobName = GetJobName(currentJob);
                string serverName = (server != null) ? server.ToString() : "unknown";

                if (server == null && jobName == null)
                {
                    StopFunction(
                        "Could not determine server from job object. Ensure InputObject comes from Get-DbaAgentJob.",
                        target: currentJob,
                        isContinue: true);
                    TestFunctionInterrupt();
                    continue;
                }

                // Apply NewName via Rename (immediate server operation)
                if (!String.IsNullOrEmpty(NewName))
                {
                    if (ShouldProcess(serverName, String.Format("Setting job name of {0} to {1}", jobName, NewName)))
                    {
                        RenameJob(currentJob, NewName);
                    }
                }

                // Apply Schedule by name
                if (Schedule != null)
                {
                    foreach (object s in Schedule)
                    {
                        string scheduleName = (s != null) ? s.ToString() : null;
                        if (String.IsNullOrEmpty(scheduleName))
                            continue;

                        if (CheckSharedScheduleExists(server, scheduleName))
                        {
                            int sID = GetSharedScheduleId(server, scheduleName);
                            if (ShouldProcess(serverName, String.Format("Adding schedule id {0} to job {1}", sID, jobName)))
                            {
                                AddSharedSchedule(currentJob, sID);
                            }
                        }
                        else
                        {
                            StopFunction(
                                String.Format("Schedule {0} cannot be found on instance {1}", scheduleName, serverName),
                                target: s,
                                isContinue: true);
                            TestFunctionInterrupt();
                            continue;
                        }
                    }
                }

                // Apply ScheduleId
                if (ScheduleId != null)
                {
                    foreach (int sID in ScheduleId)
                    {
                        if (CheckSharedScheduleIdExists(server, sID))
                        {
                            if (ShouldProcess(serverName, String.Format("Adding schedule id {0} to job {1}", sID, jobName)))
                            {
                                AddSharedSchedule(currentJob, sID);
                            }
                        }
                        else
                        {
                            StopFunction(
                                String.Format("Schedule ID {0} cannot be found on instance {1}", sID, serverName),
                                target: sID,
                                isContinue: true);
                            TestFunctionInterrupt();
                            continue;
                        }
                    }
                }

                // Apply Enabled
                if (Enabled.IsPresent)
                {
                    WriteMessageAtLevel("Setting job to enabled", MessageLevel.Verbose, null);
                    SetJobEnabled(currentJob, true);
                }

                // Apply Disabled
                if (Disabled.IsPresent)
                {
                    WriteMessageAtLevel("Setting job to disabled", MessageLevel.Verbose, null);
                    SetJobEnabled(currentJob, false);
                }

                // Apply Description
                if (!String.IsNullOrEmpty(Description))
                {
                    WriteMessageAtLevel(String.Format("Setting job description to {0}", Description), MessageLevel.Verbose, null);
                    SetJobStringProperty(currentJob, "Description", Description);
                }

                // Apply Category
                if (!String.IsNullOrEmpty(Category))
                {
                    if (!CheckCategoryExists(server, Category))
                    {
                        if (Force.IsPresent)
                        {
                            if (ShouldProcess(serverName, String.Format("Creating job category on {0}", serverName)))
                            {
                                try
                                {
                                    CreateCategory(server, Category);
                                    WriteMessageAtLevel(String.Format("Setting job category to {0}", Category), MessageLevel.Verbose, null);
                                    SetJobStringProperty(currentJob, "Category", Category);
                                }
                                catch (Exception ex)
                                {
                                    StopFunction(
                                        String.Format("Couldn't create job category {0} from {1}", Category, serverName),
                                        exception: ex,
                                        target: serverName);
                                }
                            }
                        }
                        else
                        {
                            StopFunction(
                                String.Format("Job category {0} doesn't exist on {1}. Use -Force to create it.", Category, serverName),
                                target: serverName);
                            return;
                        }
                    }
                    else
                    {
                        WriteMessageAtLevel(String.Format("Setting job category to {0}", Category), MessageLevel.Verbose, null);
                        SetJobStringProperty(currentJob, "Category", Category);
                    }
                }

                // Apply StartStepId
                if (TestBound("StartStepId"))
                {
                    int stepCount = GetJobStepCount(currentJob);
                    if (stepCount >= 1)
                    {
                        if (CheckStepIdExists(currentJob, StartStepId))
                        {
                            WriteMessageAtLevel(String.Format("Setting job start step id to {0}", StartStepId), MessageLevel.Verbose, null);
                            SetJobIntProperty(currentJob, "StartStepID", StartStepId);
                        }
                        else
                        {
                            WriteMessageAtLevel(
                                String.Format("The step id is not present in job {0} on instance {1}", jobName, serverName),
                                MessageLevel.Warning, null);
                        }
                    }
                    else
                    {
                        StopFunction(
                            String.Format("There are no job steps present for job {0} on instance {1}", jobName, serverName),
                            target: serverName,
                            isContinue: true);
                        TestFunctionInterrupt();
                        continue;
                    }
                }

                // Apply OwnerLogin
                if (!String.IsNullOrEmpty(OwnerLogin))
                {
                    if (CheckLoginExists(server, OwnerLogin))
                    {
                        WriteMessageAtLevel(String.Format("Setting job owner login name to {0}", OwnerLogin), MessageLevel.Verbose, null);
                        SetJobStringProperty(currentJob, "OwnerLoginName", OwnerLogin);
                    }
                    else
                    {
                        StopFunction(
                            String.Format("The given owner log in name {0} does not exist on instance {1}", OwnerLogin, serverName),
                            target: serverName,
                            isContinue: true);
                        TestFunctionInterrupt();
                        continue;
                    }
                }

                // Apply EventLogLevel
                if (TestBound("EventLogLevel"))
                {
                    WriteMessageAtLevel(String.Format("Setting job event log level to {0}", _eventLogLevel), MessageLevel.Verbose, null);
                    SetJobIntProperty(currentJob, "EventLogLevel", _eventLogLevel);
                }

                // Apply EmailLevel
                if (TestBound("EmailLevel"))
                {
                    if (_emailLevel == 0)
                    {
                        SetJobNullProperty(currentJob, "OperatorToEmail");
                        SetJobIntProperty(currentJob, "EmailLevel", _emailLevel);
                    }
                    else
                    {
                        string existingOperator = GetJobPropertyString(currentJob, "OperatorToEmail");
                        if (!String.IsNullOrEmpty(EmailOperator) || !String.IsNullOrEmpty(existingOperator))
                        {
                            WriteMessageAtLevel(String.Format("Setting job e-mail level to {0}", _emailLevel), MessageLevel.Verbose, null);
                            SetJobIntProperty(currentJob, "EmailLevel", _emailLevel);
                        }
                        else
                        {
                            StopFunction(
                                String.Format("Cannot set e-mail level {0} without a valid e-mail operator name", _emailLevel),
                                target: serverName,
                                isContinue: true);
                            TestFunctionInterrupt();
                            continue;
                        }
                    }
                }

                // Apply NetsendLevel
                if (TestBound("NetsendLevel"))
                {
                    if (_netsendLevel == 0)
                    {
                        SetJobNullProperty(currentJob, "OperatorToNetSend");
                        SetJobIntProperty(currentJob, "NetSendLevel", _netsendLevel);
                    }
                    else
                    {
                        string existingOperator = GetJobPropertyString(currentJob, "OperatorToNetSend");
                        if (!String.IsNullOrEmpty(NetsendOperator) || !String.IsNullOrEmpty(existingOperator))
                        {
                            WriteMessageAtLevel(String.Format("Setting job netsend level to {0}", _netsendLevel), MessageLevel.Verbose, null);
                            SetJobIntProperty(currentJob, "NetSendLevel", _netsendLevel);
                        }
                        else
                        {
                            StopFunction(
                                String.Format("Cannot set netsend level {0} without a valid netsend operator name", _netsendLevel),
                                target: serverName,
                                isContinue: true);
                            TestFunctionInterrupt();
                            continue;
                        }
                    }
                }

                // Apply PageLevel
                if (TestBound("PageLevel"))
                {
                    if (_pageLevel == 0)
                    {
                        SetJobNullProperty(currentJob, "OperatorToPage");
                        SetJobIntProperty(currentJob, "PageLevel", _pageLevel);
                    }
                    else
                    {
                        string existingOperator = GetJobPropertyString(currentJob, "OperatorToPage");
                        if (!String.IsNullOrEmpty(PageOperator) || !String.IsNullOrEmpty(existingOperator))
                        {
                            WriteMessageAtLevel(String.Format("Setting job pager level to {0}", _pageLevel), MessageLevel.Verbose, null);
                            SetJobIntProperty(currentJob, "PageLevel", _pageLevel);
                        }
                        else
                        {
                            StopFunction(
                                String.Format("Cannot set page level {0} without a valid page operator name", _pageLevel),
                                target: serverName,
                                isContinue: true);
                            TestFunctionInterrupt();
                            continue;
                        }
                    }
                }

                // Apply EmailOperator
                if (!String.IsNullOrEmpty(EmailOperator))
                {
                    if (CheckOperatorExists(server, EmailOperator))
                    {
                        WriteMessageAtLevel(String.Format("Setting job e-mail operator to {0}", EmailOperator), MessageLevel.Verbose, null);
                        SetJobStringProperty(currentJob, "OperatorToEmail", EmailOperator);
                    }
                    else
                    {
                        StopFunction(
                            String.Format("The e-mail operator name {0} does not exist on instance {1}. Exiting..", EmailOperator, serverName),
                            target: jobName,
                            isContinue: true);
                        TestFunctionInterrupt();
                        continue;
                    }
                }

                // Apply NetsendOperator
                if (!String.IsNullOrEmpty(NetsendOperator))
                {
                    if (CheckOperatorExists(server, NetsendOperator))
                    {
                        WriteMessageAtLevel(String.Format("Setting job netsend operator to {0}", NetsendOperator), MessageLevel.Verbose, null);
                        SetJobStringProperty(currentJob, "OperatorToNetSend", NetsendOperator);
                    }
                    else
                    {
                        StopFunction(
                            String.Format("The netsend operator name {0} does not exist on instance {1}. Exiting..", NetsendOperator, serverName),
                            target: jobName,
                            isContinue: true);
                        TestFunctionInterrupt();
                        continue;
                    }
                }

                // Apply PageOperator
                if (!String.IsNullOrEmpty(PageOperator))
                {
                    if (CheckOperatorExists(server, PageOperator))
                    {
                        WriteMessageAtLevel(String.Format("Setting job pager operator to {0}", PageOperator), MessageLevel.Verbose, null);
                        SetJobStringProperty(currentJob, "OperatorToPage", PageOperator);
                    }
                    else
                    {
                        StopFunction(
                            String.Format("The page operator name {0} does not exist on instance {1}. Exiting..", PageOperator, serverName),
                            target: serverName,
                            isContinue: true);
                        TestFunctionInterrupt();
                        continue;
                    }
                }

                // Apply DeleteLevel
                if (TestBound("DeleteLevel"))
                {
                    WriteMessageAtLevel(String.Format("Setting job delete level to {0}", _deleteLevel), MessageLevel.Verbose, null);
                    SetJobIntProperty(currentJob, "DeleteLevel", _deleteLevel);
                }

                // Commit changes via Alter()
                string currentName = GetJobName(currentJob);
                if (ShouldProcess(serverName, String.Format("Changing the job {0}", currentName)))
                {
                    try
                    {
                        WriteMessageAtLevel("Changing the job", MessageLevel.Verbose, null);
                        AlterJob(currentJob);
                    }
                    catch (Exception ex)
                    {
                        StopFunction(
                            "Something went wrong changing the job",
                            exception: ex,
                            target: serverName,
                            isContinue: true);
                        TestFunctionInterrupt();
                        continue;
                    }

                    RefreshJob(currentJob);
                    OutputJob(server, currentName);
                }
            }

            WriteMessageAtLevel("Finished changing job(s)", MessageLevel.Verbose, null);
        }

        #region Helpers

        /// <summary>
        /// Converts a completion level value (string or int) to its integer representation.
        /// Handles "Never" (0), "OnSuccess" (1), "OnFailure" (2), "Always" (3), and numeric strings.
        /// </summary>
        internal static int ConvertCompletionLevel(object value)
        {
            if (value == null)
                return 0;

            if (value is int intVal)
                return intVal;

            string strVal = value.ToString();

            int parsed;
            if (Int32.TryParse(strVal, out parsed))
                return parsed;

            switch (strVal)
            {
                case "Never": return 0;
                case "OnSuccess": return 1;
                case "OnFailure": return 2;
                case "Always": return 3;
                default: return 0;
            }
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
        /// Checks if a job with the given name exists on the server.
        /// </summary>
        private bool CheckJobExists(object server, string jobName)
        {
            string script = "param($s, $n) $n -in $s.JobServer.Jobs.Name";
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, ScriptBlock.Create(script), null, new object[] { server, jobName });
            if (results != null && results.Count > 0 && results[0] != null)
            {
                object val = results[0].BaseObject;
                if (val is bool boolVal)
                    return boolVal;
            }
            return false;
        }

        /// <summary>
        /// Gets a job SMO object by name from the server's JobServer.Jobs collection.
        /// </summary>
        private PSObject GetJobByName(object server, string jobName)
        {
            string script = "param($s, $n) $s.JobServer.Jobs[$n]";
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, ScriptBlock.Create(script), null, new object[] { server, jobName });
            if (results != null && results.Count > 0)
                return results[0];
            return null;
        }

        /// <summary>
        /// Refreshes a job SMO object.
        /// </summary>
        private void RefreshJob(PSObject job)
        {
            string script = "param($j) $j.Refresh()";
            InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { job });
        }

        /// <summary>
        /// Gets the server object from a job's parent chain (Job.Parent = JobServer, Parent.Parent = Server).
        /// </summary>
        private object GetJobServer(PSObject job)
        {
            try
            {
                string script = "param($j) $j.Parent.Parent";
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, ScriptBlock.Create(script), null, new object[] { job });
                if (results != null && results.Count > 0 && results[0] != null)
                    return results[0].BaseObject;
            }
            catch (Exception)
            {
                // May fail if job object is not properly initialized
            }
            return null;
        }

        /// <summary>
        /// Gets the Name property from a job PSObject.
        /// </summary>
        internal static string GetJobName(PSObject job)
        {
            if (job == null)
                return null;
            try
            {
                PSPropertyInfo prop = job.Properties["Name"];
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
        /// Gets a string property value from a job PSObject.
        /// </summary>
        internal static string GetJobPropertyString(PSObject job, string propertyName)
        {
            if (job == null)
                return null;
            try
            {
                PSPropertyInfo prop = job.Properties[propertyName];
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
        /// Renames a job using the SMO Rename() method.
        /// </summary>
        private void RenameJob(PSObject job, string newName)
        {
            string script = "param($j, $n) $j.Rename($n)";
            InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { job, newName });
        }

        /// <summary>
        /// Checks if a shared schedule with the given name exists on the server.
        /// </summary>
        private bool CheckSharedScheduleExists(object server, string scheduleName)
        {
            string script = "param($s, $n) $n -in $s.JobServer.SharedSchedules.Name";
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, ScriptBlock.Create(script), null, new object[] { server, scheduleName });
            if (results != null && results.Count > 0 && results[0] != null)
            {
                object val = results[0].BaseObject;
                if (val is bool boolVal)
                    return boolVal;
            }
            return false;
        }

        /// <summary>
        /// Gets the ID of a shared schedule by name.
        /// </summary>
        private int GetSharedScheduleId(object server, string scheduleName)
        {
            string script = "param($s, $n) $s.JobServer.SharedSchedules[$n].ID";
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, ScriptBlock.Create(script), null, new object[] { server, scheduleName });
            if (results != null && results.Count > 0 && results[0] != null)
            {
                object val = results[0].BaseObject;
                if (val is int intVal)
                    return intVal;
                int parsed;
                if (Int32.TryParse(val.ToString(), out parsed))
                    return parsed;
            }
            return 0;
        }

        /// <summary>
        /// Checks if a shared schedule with the given ID exists on the server.
        /// </summary>
        private bool CheckSharedScheduleIdExists(object server, int scheduleId)
        {
            string script = "param($s, $id) $id -in $s.JobServer.SharedSchedules.ID";
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, ScriptBlock.Create(script), null, new object[] { server, scheduleId });
            if (results != null && results.Count > 0 && results[0] != null)
            {
                object val = results[0].BaseObject;
                if (val is bool boolVal)
                    return boolVal;
            }
            return false;
        }

        /// <summary>
        /// Adds a shared schedule to a job by schedule ID.
        /// </summary>
        private void AddSharedSchedule(PSObject job, int scheduleId)
        {
            string script = "param($j, $id) $j.AddSharedSchedule($id)";
            InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { job, scheduleId });
        }

        /// <summary>
        /// Sets the IsEnabled property on a job.
        /// </summary>
        private void SetJobEnabled(PSObject job, bool enabled)
        {
            string script = "param($j, $v) $j.IsEnabled = $v";
            InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { job, enabled });
        }

        /// <summary>
        /// Sets a string property on a job SMO object.
        /// </summary>
        private void SetJobStringProperty(PSObject job, string propertyName, string value)
        {
            string script = String.Format("param($j, $v) $j.{0} = $v", propertyName);
            InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { job, value });
        }

        /// <summary>
        /// Sets a property on a job SMO object to null.
        /// </summary>
        private void SetJobNullProperty(PSObject job, string propertyName)
        {
            string script = String.Format("param($j) $j.{0} = $null", propertyName);
            InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { job });
        }

        /// <summary>
        /// Sets an integer property on a job SMO object.
        /// </summary>
        private void SetJobIntProperty(PSObject job, string propertyName, int value)
        {
            string script = String.Format("param($j, $v) $j.{0} = $v", propertyName);
            InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { job, value });
        }

        /// <summary>
        /// Checks if a job category exists on the server.
        /// </summary>
        private bool CheckCategoryExists(object server, string categoryName)
        {
            string script = "param($s, $n) $n -in $s.JobServer.JobCategories.Name";
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, ScriptBlock.Create(script), null, new object[] { server, categoryName });
            if (results != null && results.Count > 0 && results[0] != null)
            {
                object val = results[0].BaseObject;
                if (val is bool boolVal)
                    return boolVal;
            }
            return false;
        }

        /// <summary>
        /// Creates a job category on the server via New-DbaAgentJobCategory.
        /// </summary>
        private void CreateCategory(object server, string categoryName)
        {
            string script = "param($s, $c) New-DbaAgentJobCategory -SqlInstance $s -Category $c";
            InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { server, categoryName });
        }

        /// <summary>
        /// Gets the number of job steps in a job.
        /// </summary>
        private int GetJobStepCount(PSObject job)
        {
            try
            {
                string script = "param($j) $j.JobSteps.Count";
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, ScriptBlock.Create(script), null, new object[] { job });
                if (results != null && results.Count > 0 && results[0] != null)
                {
                    object val = results[0].BaseObject;
                    if (val is int intVal)
                        return intVal;
                    int parsed;
                    if (Int32.TryParse(val.ToString(), out parsed))
                        return parsed;
                }
            }
            catch (Exception)
            {
                // May fail if job object doesn't have steps
            }
            return 0;
        }

        /// <summary>
        /// Checks if a step ID exists in the job's steps collection.
        /// </summary>
        private bool CheckStepIdExists(PSObject job, int stepId)
        {
            string script = "param($j, $id) $id -in $j.JobSteps.ID";
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, ScriptBlock.Create(script), null, new object[] { job, stepId });
            if (results != null && results.Count > 0 && results[0] != null)
            {
                object val = results[0].BaseObject;
                if (val is bool boolVal)
                    return boolVal;
            }
            return false;
        }

        /// <summary>
        /// Checks if a login exists on the server.
        /// </summary>
        private bool CheckLoginExists(object server, string loginName)
        {
            string script = "param($s, $n) $n -in $s.Logins.Name";
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, ScriptBlock.Create(script), null, new object[] { server, loginName });
            if (results != null && results.Count > 0 && results[0] != null)
            {
                object val = results[0].BaseObject;
                if (val is bool boolVal)
                    return boolVal;
            }
            return false;
        }

        /// <summary>
        /// Checks if an operator exists on the server's JobServer.
        /// </summary>
        private bool CheckOperatorExists(object server, string operatorName)
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
            return false;
        }

        /// <summary>
        /// Calls Alter() on the job to commit changes.
        /// </summary>
        private void AlterJob(PSObject job)
        {
            string script = "param($j) $j.Alter()";
            InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { job });
        }

        /// <summary>
        /// Outputs the modified job via Get-DbaAgentJob for consistent formatting.
        /// </summary>
        private void OutputJob(object server, string jobName)
        {
            try
            {
                string script = "param($s, $n) Get-DbaAgentJob -SqlInstance $s -Job $n";
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, ScriptBlock.Create(script), null, new object[] { server, jobName });
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
                // If Get-DbaAgentJob fails, don't error - the job was still modified
            }
        }

        #endregion Helpers
    }
}
