using System;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Creates a new step within an existing SQL Server Agent job with configurable execution options
    /// and flow control. Each step can execute different types of commands (T-SQL, PowerShell, SSIS
    /// packages, OS commands) and includes retry logic, success/failure branching, and output capture.
    /// </summary>
    [Cmdlet("New", "DbaAgentJobStep", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low)]
    [OutputType("Microsoft.SqlServer.Management.Smo.Agent.JobStep")]
    public class NewDbaAgentJobStepCommand : DbaInstanceCmdlet
    {
        #region Parameters

        /// <summary>
        /// Specifies the SQL Server Agent job name where the new step will be added.
        /// Accepts job names or job objects from Get-DbaAgentJob.
        /// </summary>
        [Parameter(Mandatory = true)]
        [ValidateNotNullOrEmpty()]
        public object[] Job { get; set; }

        /// <summary>
        /// Sets the execution order position for this step within the job sequence.
        /// If not specified, adds the step at the end.
        /// </summary>
        [Parameter()]
        public int StepId { get; set; }

        /// <summary>
        /// Defines a descriptive name for the job step.
        /// </summary>
        [Parameter(Mandatory = true)]
        [ValidateNotNullOrEmpty()]
        public string StepName { get; set; }

        /// <summary>
        /// Determines what execution engine SQL Server Agent uses to run the step command.
        /// Defaults to TransactSql.
        /// </summary>
        [Parameter()]
        [ValidateSet("ActiveScripting", "AnalysisCommand", "AnalysisQuery", "CmdExec", "Distribution",
            "LogReader", "Merge", "PowerShell", "QueueReader", "Snapshot", "Ssis", "TransactSql")]
        public string Subsystem { get; set; }

        /// <summary>
        /// Specifies the Analysis Services server name when using AnalysisScripting, AnalysisCommand,
        /// or AnalysisQuery subsystems.
        /// </summary>
        [Parameter()]
        public string SubsystemServer { get; set; }

        /// <summary>
        /// Contains the actual code or command that the job step will execute.
        /// </summary>
        [Parameter()]
        public string Command { get; set; }

        /// <summary>
        /// Defines the exit code that indicates successful completion for CmdExec subsystem steps.
        /// </summary>
        [Parameter()]
        public int CmdExecSuccessCode { get; set; }

        /// <summary>
        /// Controls job flow when this step completes successfully.
        /// Default QuitWithSuccess ends the job with success status.
        /// </summary>
        [Parameter()]
        [ValidateSet("QuitWithSuccess", "QuitWithFailure", "GoToNextStep", "GoToStep")]
        public string OnSuccessAction { get; set; }

        /// <summary>
        /// Specifies which step to execute next when OnSuccessAction is set to GoToStep.
        /// </summary>
        [Parameter()]
        public int OnSuccessStepId { get; set; }

        /// <summary>
        /// Determines job behavior when this step fails.
        /// Default QuitWithFailure stops the job and reports failure.
        /// </summary>
        [Parameter()]
        [ValidateSet("QuitWithSuccess", "QuitWithFailure", "GoToNextStep", "GoToStep")]
        public string OnFailAction { get; set; }

        /// <summary>
        /// Identifies the step to execute when OnFailAction is GoToStep and this step fails.
        /// </summary>
        [Parameter()]
        public int OnFailStepId { get; set; }

        /// <summary>
        /// Specifies the database context for TransactSql subsystem steps.
        /// </summary>
        [Parameter()]
        public string Database { get; set; }

        /// <summary>
        /// Sets the database user context for executing T-SQL steps.
        /// </summary>
        [Parameter()]
        public string DatabaseUser { get; set; }

        /// <summary>
        /// Sets how many times SQL Server Agent will retry this step if it fails.
        /// Defaults to 0 (no retries).
        /// </summary>
        [Parameter()]
        public int RetryAttempts { get; set; }

        /// <summary>
        /// Defines the wait time in minutes between retry attempts. Defaults to 0.
        /// </summary>
        [Parameter()]
        public int RetryInterval { get; set; }

        /// <summary>
        /// Specifies a file path where the step's output will be written.
        /// </summary>
        [Parameter()]
        public string OutputFileName { get; set; }

        /// <summary>
        /// Inserts the new step at the specified StepId position, automatically renumbering
        /// subsequent steps.
        /// </summary>
        [Parameter()]
        public SwitchParameter Insert { get; set; }

        /// <summary>
        /// Controls how job step output and history are logged and stored.
        /// </summary>
        [Parameter()]
        [ValidateSet("AppendAllCmdExecOutputToJobHistory", "AppendToJobHistory", "AppendToLogFile",
            "AppendToTableLog", "LogToTableWithOverwrite", "None", "ProvideStopProcessEvent")]
        public string[] Flag { get; set; }

        /// <summary>
        /// Specifies a SQL Server Agent proxy account to use for step execution.
        /// </summary>
        [Parameter()]
        public string ProxyName { get; set; }

        /// <summary>
        /// Bypasses validation checks and overwrites existing steps with the same name or ID.
        /// </summary>
        [Parameter()]
        public SwitchParameter Force { get; set; }

        #endregion Parameters

        private string _subsystem;
        private string _onSuccessAction;
        private string _onFailAction;

        /// <summary>
        /// Validates parameter combinations before processing instances.
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

            // Set defaults for parameters that have default values in PS1
            _subsystem = TestBound("Subsystem") ? Subsystem : "TransactSql";
            _onSuccessAction = TestBound("OnSuccessAction") ? OnSuccessAction : "QuitWithSuccess";
            _onFailAction = TestBound("OnFailAction") ? OnFailAction : "QuitWithFailure";

            // Check the parameter on success step id
            if (!String.Equals(_onSuccessAction, "GoToStep", StringComparison.OrdinalIgnoreCase) && OnSuccessStepId >= 1)
            {
                StopFunction("Parameter OnSuccessStepId can only be used with OnSuccessAction 'GoToStep'.", target: SqlInstance);
                return;
            }

            // Check the parameter on fail step id
            if (!String.Equals(_onFailAction, "GoToStep", StringComparison.OrdinalIgnoreCase) && OnFailStepId >= 1)
            {
                StopFunction("Parameter OnFailStepId can only be used with OnFailAction 'GoToStep'.", target: SqlInstance);
                return;
            }

            if (_subsystem == "AnalysisCommand" || _subsystem == "AnalysisQuery")
            {
                if (String.IsNullOrEmpty(SubsystemServer))
                {
                    StopFunction(
                        String.Format("Please enter the server value using -SubSystemServer for subsystem {0}.", _subsystem),
                        target: _subsystem);
                    return;
                }
            }
        }

        /// <summary>
        /// Connects to each SQL Server instance and creates the job step with all configured properties.
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
                        errorRecord: new ErrorRecord(ex, "NewDbaAgentJobStep_ConnectionError", ErrorCategory.ConnectionError, instance),
                        target: instance, isContinue: true, category: ErrorCategory.ConnectionError);
                    TestFunctionInterrupt();
                    continue;
                }

                foreach (object j in Job)
                {
                    string jobName = ResolveJobName(j);

                    // Check if the job exists
                    if (!CheckJobExists(server, jobName))
                    {
                        WriteMessageAtLevel(
                            String.Format("Job {0} doesn't exist on {1}", jobName, instance),
                            MessageLevel.Warning, null);
                        continue;
                    }

                    // Get the job from the server
                    PSObject currentJob;
                    try
                    {
                        currentJob = GetJob(server, jobName);
                        if (currentJob == null)
                        {
                            StopFunction(
                                String.Format("Could not retrieve job {0} from {1}", jobName, instance),
                                target: instance, isContinue: true);
                            TestFunctionInterrupt();
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        StopFunction(
                            String.Format("Could not retrieve job {0} from {1}", jobName, instance),
                            exception: ex, target: instance, isContinue: true);
                        TestFunctionInterrupt();
                        continue;
                    }

                    // Create the job step object
                    PSObject jobStep;
                    try
                    {
                        jobStep = CreateJobStepObject(currentJob);
                        if (jobStep == null)
                        {
                            StopFunction("Something went wrong creating the job step",
                                target: instance, isContinue: true);
                            TestFunctionInterrupt();
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (IsContainedAgError(ex))
                        {
                            StopFunction(
                                "Cannot create agent job step through a contained availability group listener. SQL Server Agent objects are instance-level and must be managed on the instance directly. Please connect to the primary replica instead of the listener. Use Get-DbaAvailabilityGroup to find the current primary replica.",
                                exception: ex, target: instance, isContinue: true);
                        }
                        else
                        {
                            StopFunction("Something went wrong creating the job step",
                                exception: ex, target: instance, isContinue: true);
                        }
                        TestFunctionInterrupt();
                        continue;
                    }

                    #region Job Step Options

                    // Step Name
                    bool stepNameExists = CheckStepNameExists(currentJob, StepName);
                    if (!stepNameExists)
                    {
                        SetProperty(jobStep, "Name", StepName);
                    }
                    else if (stepNameExists && Force.IsPresent)
                    {
                        WriteMessageAtLevel(
                            String.Format("Step {0} already exists for job. Force is used. Removing existing step", StepName),
                            MessageLevel.Verbose, null);

                        RemoveJobStep(instance, currentJob, StepName);
                        SetProperty(jobStep, "Name", StepName);
                    }
                    else
                    {
                        StopFunction(
                            String.Format("The step name {0} already exists for job {1}", StepName, jobName),
                            target: instance, isContinue: true);
                        TestFunctionInterrupt();
                        continue;
                    }

                    // Step ID
                    if (TestBound("StepId"))
                    {
                        bool stepIdExists = CheckStepIdExists(currentJob, StepId);
                        if (!stepIdExists)
                        {
                            WriteMessageAtLevel(
                                String.Format("Setting job step step id to {0}", StepId),
                                MessageLevel.Verbose, null);
                            SetProperty(jobStep, "ID", StepId);
                        }
                        else if (stepIdExists && Insert.IsPresent)
                        {
                            WriteMessageAtLevel(
                                String.Format("Inserting step as step {0}", StepId),
                                MessageLevel.Verbose, null);
                            RenumberStepsForInsert(currentJob, StepId);
                            SetProperty(jobStep, "ID", StepId);
                        }
                        else if (stepIdExists && Force.IsPresent)
                        {
                            WriteMessageAtLevel(
                                String.Format("Step ID {0} already exists for job. Force is used. Removing existing step", StepId),
                                MessageLevel.Verbose, null);

                            string existingStepName = GetStepNameById(currentJob, StepId);
                            if (existingStepName != null)
                            {
                                RemoveJobStep(instance, currentJob, existingStepName);
                            }
                            SetProperty(jobStep, "ID", StepId);
                        }
                        else
                        {
                            StopFunction(
                                String.Format("The step id {0} already exists for job {1}", StepId, jobName),
                                target: instance, isContinue: true);
                            TestFunctionInterrupt();
                            continue;
                        }
                    }
                    else
                    {
                        // Auto-assign next step ID
                        int nextId = GetJobStepCount(currentJob) + 1;
                        SetProperty(jobStep, "ID", nextId);
                    }

                    // Subsystem
                    WriteMessageAtLevel(
                        String.Format("Setting job step subsystem to {0}", _subsystem),
                        MessageLevel.Verbose, null);
                    SetProperty(jobStep, "Subsystem", _subsystem);

                    // SubsystemServer
                    if (!String.IsNullOrEmpty(SubsystemServer))
                    {
                        WriteMessageAtLevel(
                            String.Format("Setting job step subsystem server to {0}", SubsystemServer),
                            MessageLevel.Verbose, null);
                        SetProperty(jobStep, "Server", SubsystemServer);
                    }

                    // Command
                    if (!String.IsNullOrEmpty(Command))
                    {
                        WriteMessageAtLevel(
                            String.Format("Setting job step command to {0}", Command),
                            MessageLevel.Verbose, null);
                        SetProperty(jobStep, "Command", Command);
                    }

                    // CmdExecSuccessCode
                    if (TestBound("CmdExecSuccessCode"))
                    {
                        WriteMessageAtLevel(
                            String.Format("Setting job step command exec success code to {0}", CmdExecSuccessCode),
                            MessageLevel.Verbose, null);
                        SetProperty(jobStep, "CommandExecutionSuccessCode", CmdExecSuccessCode);
                    }

                    // OnSuccessAction
                    WriteMessageAtLevel(
                        String.Format("Setting job step success action to {0}", _onSuccessAction),
                        MessageLevel.Verbose, null);
                    SetProperty(jobStep, "OnSuccessAction", _onSuccessAction);

                    // OnSuccessStepId
                    if (OnSuccessStepId > 0)
                    {
                        WriteMessageAtLevel(
                            String.Format("Setting job step success step id to {0}", OnSuccessStepId),
                            MessageLevel.Verbose, null);
                        SetProperty(jobStep, "OnSuccessStep", OnSuccessStepId);
                    }

                    // OnFailAction
                    WriteMessageAtLevel(
                        String.Format("Setting job step fail action to {0}", _onFailAction),
                        MessageLevel.Verbose, null);
                    SetProperty(jobStep, "OnFailAction", _onFailAction);

                    // OnFailStepId
                    if (OnFailStepId > 0)
                    {
                        WriteMessageAtLevel(
                            String.Format("Setting job step fail step id to {0}", OnFailStepId),
                            MessageLevel.Verbose, null);
                        SetProperty(jobStep, "OnFailStep", OnFailStepId);
                    }

                    // Database
                    if (!String.IsNullOrEmpty(Database))
                    {
                        if (CheckDatabaseExists(server, Database))
                        {
                            WriteMessageAtLevel(
                                String.Format("Setting job step database name to {0}", Database),
                                MessageLevel.Verbose, null);
                            SetProperty(jobStep, "DatabaseName", Database);
                        }
                        else
                        {
                            StopFunction(
                                String.Format("The database is not present on instance {0}.", instance),
                                target: instance, isContinue: true);
                            TestFunctionInterrupt();
                            continue;
                        }
                    }

                    // DatabaseUser (requires Database to be set)
                    if (!String.IsNullOrEmpty(DatabaseUser) && !String.IsNullOrEmpty(Database))
                    {
                        if (CheckDatabaseUserExists(server, Database, DatabaseUser))
                        {
                            WriteMessageAtLevel(
                                String.Format("Setting job step database username to {0}", DatabaseUser),
                                MessageLevel.Verbose, null);
                            SetProperty(jobStep, "DatabaseUserName", DatabaseUser);
                        }
                        else
                        {
                            StopFunction(
                                String.Format("The database user is not present in the database {0} on instance {1}.", Database, instance),
                                target: instance, isContinue: true);
                            TestFunctionInterrupt();
                            continue;
                        }
                    }

                    // RetryAttempts
                    if (TestBound("RetryAttempts"))
                    {
                        WriteMessageAtLevel(
                            String.Format("Setting job step retry attempts to {0}", RetryAttempts),
                            MessageLevel.Verbose, null);
                        SetProperty(jobStep, "RetryAttempts", RetryAttempts);
                    }

                    // RetryInterval
                    if (TestBound("RetryInterval"))
                    {
                        WriteMessageAtLevel(
                            String.Format("Setting job step retry interval to {0}", RetryInterval),
                            MessageLevel.Verbose, null);
                        SetProperty(jobStep, "RetryInterval", RetryInterval);
                    }

                    // OutputFileName
                    if (!String.IsNullOrEmpty(OutputFileName))
                    {
                        WriteMessageAtLevel(
                            String.Format("Setting job step output file name to {0}", OutputFileName),
                            MessageLevel.Verbose, null);
                        SetProperty(jobStep, "OutputFileName", OutputFileName);
                    }

                    // ProxyName
                    if (!String.IsNullOrEmpty(ProxyName))
                    {
                        if (CheckProxyExists(server, ProxyName))
                        {
                            WriteMessageAtLevel(
                                String.Format("Setting job step proxy name to {0}", ProxyName),
                                MessageLevel.Verbose, null);
                            SetProperty(jobStep, "ProxyName", ProxyName);
                        }
                        else
                        {
                            StopFunction(
                                String.Format("The proxy name {0} doesn't exist on instance {1}.", ProxyName, instance),
                                target: instance, isContinue: true);
                            TestFunctionInterrupt();
                            continue;
                        }
                    }

                    // Flags
                    if (Flag != null && Flag.Length >= 1)
                    {
                        WriteMessageAtLevel(
                            String.Format("Setting job step flag(s) to {0}", String.Join(",", Flag)),
                            MessageLevel.Verbose, null);
                        SetJobStepFlags(jobStep, Flag);
                    }

                    #endregion Job Step Options

                    // Execute
                    if (ShouldProcess(instance.ToString(), String.Format("Creating the job step {0}", StepName)))
                    {
                        try
                        {
                            WriteMessageAtLevel("Creating the job step", MessageLevel.Verbose, null);
                            InvokeMethod(jobStep, "Create");
                            InvokeMethod(currentJob, "Alter");
                        }
                        catch (Exception ex)
                        {
                            StopFunction("Something went wrong creating the job step",
                                exception: ex, target: instance, isContinue: true);
                            TestFunctionInterrupt();
                            continue;
                        }

                        // Return the job step
                        WriteObject(jobStep);
                    }
                } // foreach job
            } // foreach instance
        }

        /// <summary>
        /// Final verbose message.
        /// </summary>
        protected override void EndProcessing()
        {
            if (TestFunctionInterrupt()) { return; }
            WriteMessageAtLevel("Finished creating job step(s)", MessageLevel.Verbose, null);
        }

        #region Helpers

        /// <summary>
        /// Resolves a job parameter value to a job name string.
        /// Accepts string names or SMO Job objects.
        /// </summary>
        internal static string ResolveJobName(object job)
        {
            if (job == null)
                return null;

            if (job is string strJob)
                return strJob;

            // Handle PSObject wrapping
            if (job is PSObject psObj)
            {
                if (psObj.BaseObject is string baseStr)
                    return baseStr;

                // Try to get the Name property (SMO Job objects)
                try
                {
                    PSPropertyInfo nameProp = psObj.Properties["Name"];
                    if (nameProp != null && nameProp.Value != null)
                        return nameProp.Value.ToString();
                }
                catch (Exception)
                {
                    // Fall through to ToString
                }

                return psObj.BaseObject.ToString();
            }

            return job.ToString();
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
        /// Checks if a job with the given name exists on the server.
        /// </summary>
        private bool CheckJobExists(object server, string jobName)
        {
            string script = "param($s, $n) $n -in $s.JobServer.Jobs.Name";
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, ScriptBlock.Create(script), null, new object[] { server, jobName });
            return ExtractBool(results);
        }

        /// <summary>
        /// Gets a job object from the server by name.
        /// </summary>
        private PSObject GetJob(object server, string jobName)
        {
            string script = "param($s, $n) $s.JobServer.Jobs[$n]";
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, ScriptBlock.Create(script), null, new object[] { server, jobName });
            if (results != null && results.Count > 0)
                return results[0];
            return null;
        }

        /// <summary>
        /// Creates a new SMO Agent JobStep object with the specified parent job.
        /// </summary>
        private PSObject CreateJobStepObject(PSObject parentJob)
        {
            string script = @"param($j)
$step = New-Object Microsoft.SqlServer.Management.Smo.Agent.JobStep
$step.Parent = $j
$step";
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, ScriptBlock.Create(script), null, new object[] { parentJob });
            if (results != null && results.Count > 0)
                return results[0];
            return null;
        }

        /// <summary>
        /// Checks if a step with the given name exists in the job.
        /// </summary>
        private bool CheckStepNameExists(PSObject job, string stepName)
        {
            string script = "param($j, $n) $n -in $j.JobSteps.Name";
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, ScriptBlock.Create(script), null, new object[] { job, stepName });
            return ExtractBool(results);
        }

        /// <summary>
        /// Checks if a step with the given ID exists in the job.
        /// </summary>
        private bool CheckStepIdExists(PSObject job, int stepId)
        {
            string script = "param($j, $id) $id -in $j.JobSteps.ID";
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, ScriptBlock.Create(script), null, new object[] { job, stepId });
            return ExtractBool(results);
        }

        /// <summary>
        /// Gets the name of a step by its ID.
        /// </summary>
        private string GetStepNameById(PSObject job, int stepId)
        {
            string script = "param($j, $id) ($j.JobSteps | Where-Object { $_.ID -eq $id }).Name";
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, ScriptBlock.Create(script), null, new object[] { job, stepId });
            if (results != null && results.Count > 0 && results[0] != null)
                return results[0].BaseObject.ToString();
            return null;
        }

        /// <summary>
        /// Renumbers existing steps to make room for an insertion at the specified step ID.
        /// Also updates OnFailStep and OnSuccessStep references on affected steps.
        /// </summary>
        private void RenumberStepsForInsert(PSObject job, int stepId)
        {
            string script = @"param($j, $sid)
foreach ($tStep in $j.JobSteps) {
    if ($tStep.Id -ge $sid) {
        $tStep.Id = ($tStep.ID) + 1
    }
    if ($tStep.OnFailStep -ge $sid -and $tStep.OnFailStep -ne 0) {
        $tStep.OnFailStep = ($tStep.OnFailStep) + 1
    }
    if ($tStep.OnSuccessStep -ge $sid -and $tStep.OnSuccessStep -ne 0) {
        $tStep.OnSuccessStep = ($tStep.OnSuccessStep) + 1
    }
}";
            InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, new object[] { job, stepId });
        }

        /// <summary>
        /// Gets the number of job steps in a job.
        /// </summary>
        private int GetJobStepCount(PSObject job)
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
                if (int.TryParse(val.ToString(), out parsed))
                    return parsed;
            }
            return 0;
        }

        /// <summary>
        /// Removes a job step via Remove-DbaAgentJobStep.
        /// </summary>
        private void RemoveJobStep(DbaInstanceParameter instance, PSObject job, string stepName)
        {
            string script;
            object[] args;
            if (SqlCredential != null)
            {
                script = "param($i, $j, $s, $c) Remove-DbaAgentJobStep -SqlInstance $i -Job $j -StepName $s -SqlCredential $c -Confirm:$false";
                args = new object[] { instance, job, stepName, SqlCredential };
            }
            else
            {
                script = "param($i, $j, $s) Remove-DbaAgentJobStep -SqlInstance $i -Job $j -StepName $s -Confirm:$false";
                args = new object[] { instance, job, stepName };
            }
            InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, args);
        }

        /// <summary>
        /// Checks if a database exists on the server.
        /// </summary>
        private bool CheckDatabaseExists(object server, string databaseName)
        {
            string script = "param($s, $n) $n -in $s.Databases.Name";
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, ScriptBlock.Create(script), null, new object[] { server, databaseName });
            return ExtractBool(results);
        }

        /// <summary>
        /// Checks if a database user exists in the specified database.
        /// </summary>
        private bool CheckDatabaseUserExists(object server, string databaseName, string userName)
        {
            string script = "param($s, $d, $u) $u -in $s.Databases[$d].Users.Name";
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, ScriptBlock.Create(script), null, new object[] { server, databaseName, userName });
            return ExtractBool(results);
        }

        /// <summary>
        /// Checks if a proxy account exists on the server.
        /// </summary>
        private bool CheckProxyExists(object server, string proxyName)
        {
            string script = "param($s, $n) $n -in $s.JobServer.ProxyAccounts.Name";
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, ScriptBlock.Create(script), null, new object[] { server, proxyName });
            return ExtractBool(results);
        }

        /// <summary>
        /// Sets the JobStepFlags property from a string array of flag names.
        /// Combines multiple flags using bitwise OR for the [Flags] enum.
        /// </summary>
        private void SetJobStepFlags(PSObject jobStep, string[] flags)
        {
            string script = @"param($s, $f)
$combined = [Microsoft.SqlServer.Management.Smo.Agent.JobStepFlags]::None
foreach ($flag in $f) {
    $combined = $combined -bor [Microsoft.SqlServer.Management.Smo.Agent.JobStepFlags]$flag
}
$s.JobStepFlags = $combined";
            InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, new object[] { jobStep, flags });
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
        /// Extracts a boolean result from a PowerShell invocation.
        /// </summary>
        private static bool ExtractBool(Collection<PSObject> results)
        {
            if (results != null && results.Count > 0 && results[0] != null)
            {
                object val = results[0].BaseObject;
                if (val is bool boolVal)
                    return boolVal;
            }
            return false;
        }

        #endregion Helpers
    }
}