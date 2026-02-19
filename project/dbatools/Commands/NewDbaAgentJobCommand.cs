using System;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Creates SQL Server Agent jobs with notification settings and schedule assignments.
    /// Supports full configuration options including owner assignment, job categories, and
    /// comprehensive notification settings for email, event log, pager, and netsend.
    /// </summary>
    [Cmdlet("New", "DbaAgentJob", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low)]
    [OutputType("Microsoft.SqlServer.Management.Smo.Agent.Job")]
    public class NewDbaAgentJobCommand : DbaInstanceCmdlet
    {
        #region Parameters

        /// <summary>
        /// The name of the job. The name must be unique and cannot contain the percent (%) character.
        /// </summary>
        [Parameter(Mandatory = true)]
        [ValidateNotNullOrEmpty()]
        public string Job { get; set; }

        /// <summary>
        /// Schedule to attach to job. This can be more than one schedule.
        /// </summary>
        [Parameter()]
        public object[] Schedule { get; set; }

        /// <summary>
        /// Schedule ID to attach to job. This can be more than one schedule ID.
        /// </summary>
        [Parameter()]
        public int[] ScheduleId { get; set; }

        /// <summary>
        /// Sets the status of the job to disabled. By default a job is enabled.
        /// </summary>
        [Parameter()]
        public SwitchParameter Disabled { get; set; }

        /// <summary>
        /// The description of the job.
        /// </summary>
        [Parameter()]
        public string Description { get; set; }

        /// <summary>
        /// The identification number of the first step to execute for the job.
        /// </summary>
        [Parameter()]
        public int StartStepId { get; set; }

        /// <summary>
        /// The category of the job.
        /// </summary>
        [Parameter()]
        public string Category { get; set; }

        /// <summary>
        /// The name of the login that owns the job.
        /// </summary>
        [Parameter()]
        public string OwnerLogin { get; set; }

        /// <summary>
        /// Specifies when to place an entry in the Microsoft Windows application log for this job.
        /// Allowed values 0, Never, 1, OnSuccess, 2, OnFailure, 3, Always.
        /// </summary>
        [Parameter()]
        [ValidateSet("0", "Never", "1", "OnSuccess", "2", "OnFailure", "3", "Always")]
        public object EventLogLevel { get; set; }

        /// <summary>
        /// Specifies when to send an e-mail upon the completion of this job.
        /// Allowed values 0, Never, 1, OnSuccess, 2, OnFailure, 3, Always.
        /// </summary>
        [Parameter()]
        [ValidateSet("0", "Never", "1", "OnSuccess", "2", "OnFailure", "3", "Always")]
        public object EmailLevel { get; set; }

        /// <summary>
        /// Specifies when to send a network message upon the completion of this job.
        /// Allowed values 0, Never, 1, OnSuccess, 2, OnFailure, 3, Always.
        /// </summary>
        [Parameter()]
        [ValidateSet("0", "Never", "1", "OnSuccess", "2", "OnFailure", "3", "Always")]
        public object NetsendLevel { get; set; }

        /// <summary>
        /// Specifies when to send a page upon the completion of this job.
        /// Allowed values 0, Never, 1, OnSuccess, 2, OnFailure, 3, Always.
        /// </summary>
        [Parameter()]
        [ValidateSet("0", "Never", "1", "OnSuccess", "2", "OnFailure", "3", "Always")]
        public object PageLevel { get; set; }

        /// <summary>
        /// The e-mail name of the operator to whom the e-mail is sent when EmailLevel is reached.
        /// </summary>
        [Parameter()]
        public string EmailOperator { get; set; }

        /// <summary>
        /// The name of the operator to whom the network message is sent.
        /// </summary>
        [Parameter()]
        public string NetsendOperator { get; set; }

        /// <summary>
        /// The name of the operator to whom a page is sent.
        /// </summary>
        [Parameter()]
        public string PageOperator { get; set; }

        /// <summary>
        /// Specifies when to delete the job.
        /// Allowed values 0, Never, 1, OnSuccess, 2, OnFailure, 3, Always.
        /// </summary>
        [Parameter()]
        [ValidateSet("0", "Never", "1", "OnSuccess", "2", "OnFailure", "3", "Always")]
        public object DeleteLevel { get; set; }

        /// <summary>
        /// The force parameter will ignore some errors in the parameters and assume defaults.
        /// When a job already exists with Force, it will be removed and recreated.
        /// When a category doesn't exist with Force, it will be created.
        /// </summary>
        [Parameter()]
        public SwitchParameter Force { get; set; }

        #endregion Parameters

        private int _eventLogLevel;
        private int _emailLevel;
        private int _netsendLevel;
        private int _pageLevel;
        private int _deleteLevel;

        /// <summary>
        /// Resolves notification levels and validates operator requirements.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            if (Force.IsPresent)
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

            _eventLogLevel = ResolveNotificationLevel(EventLogLevel);
            _emailLevel = ResolveNotificationLevel(EmailLevel);
            _netsendLevel = ResolveNotificationLevel(NetsendLevel);
            _pageLevel = ResolveNotificationLevel(PageLevel);
            _deleteLevel = ResolveNotificationLevel(DeleteLevel);

            if (_emailLevel >= 1 && String.IsNullOrEmpty(EmailOperator))
            {
                StopFunction("Please set the e-mail operator when the e-mail level parameter is set.", target: SqlInstance);
                return;
            }

            if (_netsendLevel >= 1 && String.IsNullOrEmpty(NetsendOperator))
            {
                StopFunction("Please set the netsend operator when the netsend level parameter is set.", target: SqlInstance);
                return;
            }

            if (_pageLevel >= 1 && String.IsNullOrEmpty(PageOperator))
            {
                StopFunction("Please set the page operator when the page level parameter is set.", target: SqlInstance);
                return;
            }
        }

        /// <summary>
        /// Connects to each SQL Server instance and creates the specified Agent job
        /// with all configured properties including notifications and schedules.
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
                        errorRecord: new ErrorRecord(ex, "NewDbaAgentJob_ConnectionError", ErrorCategory.ConnectionError, instance),
                        target: instance, isContinue: true, category: ErrorCategory.ConnectionError);
                    TestFunctionInterrupt();
                    continue;
                }

                // Check if job already exists
                bool jobExists = CheckJobExists(server, Job);
                if (!Force.IsPresent && jobExists)
                {
                    StopFunction(String.Format("Job {0} already exists on {1}", Job, instance),
                        target: instance, isContinue: true);
                    TestFunctionInterrupt();
                    continue;
                }
                else if (Force.IsPresent && jobExists)
                {
                    WriteMessageAtLevel(String.Format("Job {0} already exists on {1}. Removing..", Job, instance),
                        MessageLevel.Verbose, null);

                    if (ShouldProcess(instance.ToString(), String.Format("Removing the job {0} on {1}", Job, instance)))
                    {
                        try
                        {
                            RemoveExistingJob(server, Job);
                            RefreshJobServer(server);
                        }
                        catch (Exception ex)
                        {
                            StopFunction(String.Format("Couldn't remove job {0} from {1}", Job, instance),
                                exception: ex, target: instance, isContinue: true);
                            TestFunctionInterrupt();
                            continue;
                        }
                    }
                }

                if (ShouldProcess(instance.ToString(), String.Format("Creating the job on {0}", instance)))
                {
                    PSObject currentJob;
                    try
                    {
                        currentJob = CreateJobObject(server, Job);
                        if (currentJob == null)
                        {
                            StopFunction("Something went wrong creating the job.",
                                target: Job, isContinue: true);
                            TestFunctionInterrupt();
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (IsContainedAgError(ex))
                        {
                            StopFunction(
                                "Cannot create agent job through a contained availability group listener. SQL Server Agent objects are instance-level and must be managed on the instance directly. Please connect to the primary replica instead of the listener. Use Get-DbaAvailabilityGroup to find the current primary replica.",
                                exception: ex, target: Job, isContinue: true);
                        }
                        else
                        {
                            StopFunction("Something went wrong creating the job.",
                                exception: ex, target: Job, isContinue: true);
                        }
                        TestFunctionInterrupt();
                        continue;
                    }

                    #region Job Options
                    // Enabled/Disabled
                    if (Disabled.IsPresent)
                    {
                        WriteMessageAtLevel("Setting job to disabled", MessageLevel.Verbose, null);
                        SetProperty(currentJob, "IsEnabled", false);
                    }
                    else
                    {
                        WriteMessageAtLevel("Setting job to enabled", MessageLevel.Verbose, null);
                        SetProperty(currentJob, "IsEnabled", true);
                    }

                    // Description
                    if (!String.IsNullOrEmpty(Description))
                    {
                        WriteMessageAtLevel("Setting job description", MessageLevel.Verbose, null);
                        SetProperty(currentJob, "Description", Description);
                    }

                    // Start step
                    if (StartStepId >= 1)
                    {
                        WriteMessageAtLevel("Setting job start step id", MessageLevel.Verbose, null);
                        SetProperty(currentJob, "StartStepID", StartStepId);
                    }

                    // Category
                    if (!String.IsNullOrEmpty(Category))
                    {
                        bool categoryExists = CheckJobCategoryExists(server, Category);
                        if (!categoryExists)
                        {
                            if (Force.IsPresent)
                            {
                                if (ShouldProcess(instance.ToString(), String.Format("Creating job category on {0}", instance)))
                                {
                                    try
                                    {
                                        RefreshJobServer(server);
                                        CreateJobCategory(server, Category);
                                        categoryExists = true;
                                    }
                                    catch (Exception ex)
                                    {
                                        StopFunction(String.Format("Couldn't create job category {0} from {1}", Category, instance),
                                            exception: ex, target: instance, isContinue: true);
                                    }
                                }
                            }
                            else
                            {
                                StopFunction(String.Format("Job category {0} doesn't exist on {1}. Use -Force to create it.", Category, instance),
                                    target: instance);
                                return;
                            }
                        }

                        // Set category on job if it exists (fixes PS1 bug where Force+create path skipped this)
                        if (categoryExists)
                        {
                            WriteMessageAtLevel("Setting job category", MessageLevel.Verbose, null);
                            SetProperty(currentJob, "Category", Category);
                        }
                    }

                    // Owner login
                    if (!String.IsNullOrEmpty(OwnerLogin))
                    {
                        bool loginExists = CheckLoginExists(server, OwnerLogin);
                        if (loginExists)
                        {
                            WriteMessageAtLevel(String.Format("Setting job owner login name to {0}", OwnerLogin), MessageLevel.Verbose, null);
                            SetProperty(currentJob, "OwnerLoginName", OwnerLogin);
                        }
                        else
                        {
                            // PS1 uses Stop-Function -Continue here but does NOT skip to next instance;
                            // execution falls through and the job is created without the specified owner
                            StopFunction(String.Format("The owner {0} does not exist on instance {1}", OwnerLogin, instance),
                                target: Job, isContinue: true);
                        }
                    }

                    // Event log level (always set, defaults to 0/Never)
                    if (_eventLogLevel >= 0)
                    {
                        WriteMessageAtLevel("Setting job event log level", MessageLevel.Verbose, null);
                        SetProperty(currentJob, "EventLogLevel", _eventLogLevel);
                    }

                    // Email operator and level
                    if (!String.IsNullOrEmpty(EmailOperator))
                    {
                        if (_emailLevel >= 1)
                        {
                            bool operatorExists = CheckOperatorExists(server, EmailOperator);
                            if (operatorExists)
                            {
                                WriteMessageAtLevel("Setting job e-mail level", MessageLevel.Verbose, null);
                                SetProperty(currentJob, "EmailLevel", _emailLevel);
                                WriteMessageAtLevel("Setting job e-mail operator", MessageLevel.Verbose, null);
                                SetProperty(currentJob, "OperatorToEmail", EmailOperator);
                            }
                            else
                            {
                                StopFunction(String.Format("The e-mail operator name {0} does not exist on instance {1}. Exiting..", EmailOperator, instance),
                                    target: Job, isContinue: true);
                            }
                        }
                        else
                        {
                            StopFunction(String.Format("Invalid combination of e-mail operator name {0} and email level {1}. Not setting the notification.", EmailOperator, _emailLevel),
                                target: Job, isContinue: true);
                        }
                    }

                    // Netsend operator and level
                    if (!String.IsNullOrEmpty(NetsendOperator))
                    {
                        if (_netsendLevel >= 1)
                        {
                            bool operatorExists = CheckOperatorExists(server, NetsendOperator);
                            if (operatorExists)
                            {
                                WriteMessageAtLevel("Setting job netsend level", MessageLevel.Verbose, null);
                                SetProperty(currentJob, "NetSendLevel", _netsendLevel);
                                WriteMessageAtLevel("Setting job netsend operator", MessageLevel.Verbose, null);
                                SetProperty(currentJob, "OperatorToNetSend", NetsendOperator);
                            }
                            else
                            {
                                StopFunction(String.Format("The netsend operator name {0} does not exist on instance {1}. Exiting..", NetsendOperator, instance),
                                    target: Job, isContinue: true);
                            }
                        }
                        else
                        {
                            // PS1 uses Write-Message without -Level (defaults to Verbose)
                            WriteMessageAtLevel(
                                String.Format("Invalid combination of netsend operator name {0} and netsend level {1}. Not setting the notification.", NetsendOperator, _netsendLevel),
                                MessageLevel.Verbose, null);
                        }
                    }

                    // Page operator and level
                    if (!String.IsNullOrEmpty(PageOperator))
                    {
                        if (_pageLevel >= 1)
                        {
                            bool operatorExists = CheckOperatorExists(server, PageOperator);
                            if (operatorExists)
                            {
                                WriteMessageAtLevel("Setting job pager level", MessageLevel.Verbose, null);
                                SetProperty(currentJob, "PageLevel", _pageLevel);
                                WriteMessageAtLevel("Setting job pager operator", MessageLevel.Verbose, null);
                                SetProperty(currentJob, "OperatorToPage", PageOperator);
                            }
                            else
                            {
                                StopFunction(String.Format("The page operator name {0} does not exist on instance {1}. Exiting..", PageOperator, instance),
                                    target: Job, isContinue: true);
                            }
                        }
                        else
                        {
                            // PS1 uses Write-Message -Level Warning
                            WriteMessageAtLevel(
                                String.Format("Invalid combination of page operator name {0} and page level {1}. Not setting the notification.", PageOperator, _pageLevel),
                                MessageLevel.Warning, null);
                        }
                    }

                    // Delete level (always set, defaults to 0/Never)
                    if (_deleteLevel >= 0)
                    {
                        WriteMessageAtLevel("Setting job delete level", MessageLevel.Verbose, null);
                        SetProperty(currentJob, "DeleteLevel", _deleteLevel);
                    }
                    #endregion Job Options

                    // Create the job
                    try
                    {
                        WriteMessageAtLevel("Creating the job", MessageLevel.Verbose, null);
                        InvokeMethod(currentJob, "Create");

                        string jobId = GetPropertyString(currentJob, "JobID");
                        WriteMessageAtLevel(String.Format("Job created with UID {0}", jobId), MessageLevel.Verbose, null);

                        WriteMessageAtLevel(String.Format("Applying the target (local) to job {0}", Job), MessageLevel.Verbose, null);
                        InvokeMethodWithArg(currentJob, "ApplyToTargetServer", "(local)");

                        InvokeMethod(currentJob, "Refresh");

                        // Attach schedules
                        if (Schedule != null && Schedule.Length > 0)
                        {
                            AttachSchedules(server, currentJob, Schedule);
                        }

                        if (ScheduleId != null && ScheduleId.Length > 0)
                        {
                            AttachScheduleIds(server, currentJob, ScheduleId);
                        }
                    }
                    catch (Exception ex)
                    {
                        StopFunction("Something went wrong creating the job",
                            exception: ex, target: currentJob, isContinue: true);
                    }
                }

                // Add TEPP cache item (matches PS1 - runs outside ShouldProcess)
                AddTeppCacheItem(server, Job);

                // Output via Get-DbaAgentJob (matches PS1 - runs outside ShouldProcess)
                OutputJob(server, Job);
            }
        }

        /// <summary>
        /// Final verbose message.
        /// </summary>
        protected override void EndProcessing()
        {
            if (TestFunctionInterrupt()) { return; }
            WriteMessageAtLevel("Finished creating job(s).", MessageLevel.Verbose, null);
        }

        #region Helpers

        /// <summary>
        /// Resolves a notification level value from string or int to an integer.
        /// Accepts: 0, 1, 2, 3, "Never", "OnSuccess", "OnFailure", "Always", or null (defaults to 0).
        /// </summary>
        internal static int ResolveNotificationLevel(object value)
        {
            if (value == null)
                return 0;

            if (value is int intVal)
                return intVal;

            string strVal = value.ToString();
            int parsed;
            if (int.TryParse(strVal, out parsed))
                return parsed;

            switch (strVal.ToUpperInvariant())
            {
                case "NEVER": return 0;
                case "ONSUCCESS": return 1;
                case "ONFAILURE": return 2;
                case "ALWAYS": return 3;
                default: return 0;
            }
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
        /// Checks if a job with the given name already exists on the server.
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
        /// Removes an existing job via Remove-DbaAgentJob.
        /// </summary>
        private void RemoveExistingJob(object server, string jobName)
        {
            string script = "param($s, $j) $null = Remove-DbaAgentJob -SqlInstance $s -Job $j -EnableException -Confirm:$false";
            InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, new object[] { server, jobName });
        }

        /// <summary>
        /// Refreshes the JobServer to pick up changes.
        /// </summary>
        private void RefreshJobServer(object server)
        {
            try
            {
                string script = "param($s) $null = $s.JobServer.Refresh()";
                InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, new object[] { server });
            }
            catch (Exception)
            {
                // Best effort refresh
            }
        }

        /// <summary>
        /// Creates a new SMO Agent Job object.
        /// </summary>
        private PSObject CreateJobObject(object server, string jobName)
        {
            string script = "param($s, $n) New-Object Microsoft.SqlServer.Management.Smo.Agent.Job($s.JobServer, $n)";
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, ScriptBlock.Create(script), null, new object[] { server, jobName });
            if (results != null && results.Count > 0)
                return results[0];
            return null;
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
        /// Checks if a job category exists on the server.
        /// </summary>
        private bool CheckJobCategoryExists(object server, string categoryName)
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
        /// Creates a job category via New-DbaAgentJobCategory.
        /// </summary>
        private void CreateJobCategory(object server, string categoryName)
        {
            string script = "param($s, $c) New-DbaAgentJobCategory -SqlInstance $s -Category $c";
            InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, new object[] { server, categoryName });
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
        /// Checks if an operator exists on the server.
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
        /// Invokes a parameterless method on a PSObject.
        /// </summary>
        private void InvokeMethod(PSObject obj, string methodName)
        {
            string script = String.Format("param($o) $o.{0}()", methodName);
            InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, new object[] { obj });
        }

        /// <summary>
        /// Invokes a method with a single argument on a PSObject.
        /// </summary>
        private void InvokeMethodWithArg(PSObject obj, string methodName, object arg)
        {
            string script = String.Format("param($o, $a) $o.{0}($a)", methodName);
            InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, new object[] { obj, arg });
        }

        /// <summary>
        /// Attaches schedules to a job via Set-DbaAgentJob.
        /// </summary>
        private void AttachSchedules(object server, PSObject job, object[] schedules)
        {
            string script = "param($s, $j, $sch) $null = Set-DbaAgentJob -SqlInstance $s -Job $j -Schedule $sch";
            InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, new object[] { server, job, schedules });
        }

        /// <summary>
        /// Attaches schedule IDs to a job via Set-DbaAgentJob.
        /// </summary>
        private void AttachScheduleIds(object server, PSObject job, int[] scheduleIds)
        {
            string script = "param($s, $j, $sid) $null = Set-DbaAgentJob -SqlInstance $s -Job $j -ScheduleId $sid";
            InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, new object[] { server, job, scheduleIds });
        }

        /// <summary>
        /// Adds a TEPP cache item for tab completion.
        /// </summary>
        private void AddTeppCacheItem(object server, string jobName)
        {
            try
            {
                string script = "param($s, $n) Add-TeppCacheItem -SqlInstance $s -Type job -Name $n";
                InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, new object[] { server, jobName });
            }
            catch (Exception)
            {
                // Best effort - TEPP cache is for tab completion only
            }
        }

        /// <summary>
        /// Outputs the created job via Get-DbaAgentJob.
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
                // If Get-DbaAgentJob fails, don't error - the job was still created
            }
        }

        #endregion Helpers
    }
}
