using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Retrieves detailed SQL Agent job step information including execution status and configuration
    /// from SQL Server instances. Returns job step objects with properties including name, subsystem type,
    /// last run date, outcome, and current state.
    /// </summary>
    [Cmdlet("Get", "DbaAgentJobStep")]
    public class GetDbaAgentJobStepCommand : DbaBaseCmdlet
    {
        #region Parameters

        /// <summary>
        /// The target SQL Server instance or instances.
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        public DbaInstanceParameter[] SqlInstance { get; set; }

        /// <summary>
        /// Login to the target instance using alternative credentials.
        /// </summary>
        [Parameter()]
        [Alias("Credential", "Cred")]
        public PSCredential SqlCredential { get; set; }

        /// <summary>
        /// Specifies which SQL Agent jobs to include by name when retrieving job steps.
        /// </summary>
        [Parameter()]
        public string[] Job { get; set; }

        /// <summary>
        /// Specifies which SQL Agent jobs to exclude by name when retrieving job steps.
        /// </summary>
        [Parameter()]
        public string[] ExcludeJob { get; set; }

        /// <summary>
        /// Filters out disabled SQL Agent jobs from the results, showing only currently active jobs.
        /// </summary>
        [Parameter()]
        public SwitchParameter ExcludeDisabledJobs { get; set; }

        /// <summary>
        /// Accepts SQL Agent job objects from the pipeline, typically from Get-DbaAgentJob output.
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        public object[] InputObject { get; set; }

        #endregion Parameters

        /// <summary>
        /// Default display properties for the output objects.
        /// </summary>
        private static readonly string[] DefaultDisplayProperties = new string[]
        {
            "ComputerName", "InstanceName", "SqlInstance", "AgentJob", "Name",
            "SubSystem", "LastRunDate", "LastRunOutcome", "State"
        };

        /// <summary>
        /// Accumulates all job objects across ProcessRecord calls.
        /// </summary>
        private List<PSObject> _allJobs;

        /// <summary>
        /// Initializes the accumulation list.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();
            _allJobs = new List<PSObject>();
        }

        /// <summary>
        /// Connects to instances and collects jobs, or accumulates InputObject pipeline items.
        /// </summary>
        protected override void ProcessRecord()
        {
            // Process SqlInstance connections
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
                            errorRecord: new ErrorRecord(ex, "GetDbaAgentJobStep_ConnectionError", ErrorCategory.ConnectionError, instance),
                            target: instance,
                            isContinue: true,
                            category: ErrorCategory.ConnectionError);
                        TestFunctionInterrupt();
                        continue;
                    }

                    WriteMessageAtLevel(String.Format("Collecting jobs on {0}", instance), MessageLevel.Verbose, null);

                    try
                    {
                        Collection<PSObject> jobs = GetJobs(server);
                        if (jobs != null)
                        {
                            foreach (PSObject job in jobs)
                            {
                                if (job != null)
                                    _allJobs.Add(job);
                            }
                        }
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
                }
            }

            // Accumulate InputObject pipeline items
            if (InputObject != null)
            {
                foreach (object obj in InputObject)
                {
                    if (obj != null)
                    {
                        _allJobs.Add(PSObject.AsPSObject(obj));
                    }
                }
            }
        }

        /// <summary>
        /// Filters accumulated jobs and outputs their job steps with custom properties.
        /// </summary>
        protected override void EndProcessing()
        {
            // Filter by Job name (case-insensitive exact match, matching PS1 -In operator)
            if (Job != null && Job.Length > 0)
            {
                HashSet<string> jobLookup = new HashSet<string>(Job, StringComparer.OrdinalIgnoreCase);
                List<PSObject> filtered = new List<PSObject>();
                foreach (PSObject job in _allJobs)
                {
                    string name = GetPSPropertyString(job, "Name");
                    if (name != null && jobLookup.Contains(name))
                        filtered.Add(job);
                }
                _allJobs = filtered;
            }

            // Filter by ExcludeJob (case-insensitive exact match, matching PS1 -NotIn operator)
            if (ExcludeJob != null && ExcludeJob.Length > 0)
            {
                HashSet<string> excludeLookup = new HashSet<string>(ExcludeJob, StringComparer.OrdinalIgnoreCase);
                List<PSObject> filtered = new List<PSObject>();
                foreach (PSObject job in _allJobs)
                {
                    string name = GetPSPropertyString(job, "Name");
                    if (name == null || !excludeLookup.Contains(name))
                        filtered.Add(job);
                }
                _allJobs = filtered;
            }

            // Filter by ExcludeDisabledJobs
            if (ExcludeDisabledJobs.IsPresent)
            {
                List<PSObject> filtered = new List<PSObject>();
                foreach (PSObject job in _allJobs)
                {
                    bool isEnabled = GetPSPropertyBool(job, "IsEnabled");
                    if (isEnabled)
                        filtered.Add(job);
                }
                _allJobs = filtered;
            }

            WriteMessageAtLevel("Collecting job steps", MessageLevel.Verbose, null);

            // Iterate over each job's steps
            foreach (PSObject job in _allJobs)
            {
                Collection<PSObject> steps;
                try
                {
                    steps = GetJobSteps(job);
                }
                catch (Exception ex)
                {
                    string jobName = GetPSPropertyString(job, "Name");
                    WriteMessageAtLevel(
                        String.Format("Failed to retrieve steps from job '{0}': {1}", jobName, ex.Message),
                        MessageLevel.Warning, null);
                    continue;
                }

                if (steps == null || steps.Count == 0)
                    continue;

                foreach (PSObject step in steps)
                {
                    if (step == null)
                        continue;

                    // Get server info by walking up: step.Parent = Job, Job.Parent = JobServer, JobServer.Parent = Server
                    string computerName = GetParentServerProperty(step, "ComputerName");
                    string instanceName = GetParentServerProperty(step, "ServiceName");
                    string sqlInstance = GetParentServerProperty(step, "DomainInstanceName");
                    string agentJob = GetParentJobName(step);

                    // Add custom NoteProperties (matching PS1 Add-Member -Force behavior)
                    AddOrSetProperty(step, "ComputerName", computerName);
                    AddOrSetProperty(step, "InstanceName", instanceName);
                    AddOrSetProperty(step, "SqlInstance", sqlInstance);
                    AddOrSetProperty(step, "AgentJob", agentJob);

                    // Set default display properties (matching PS1 Select-DefaultView)
                    SetDefaultDisplayPropertySet(step, DefaultDisplayProperties);

                    WriteObject(step);
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
        /// Gets the job steps collection from a job object.
        /// </summary>
        private Collection<PSObject> GetJobSteps(PSObject job)
        {
            string script = "param($j) $j.JobSteps";
            return InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, new object[] { job });
        }

        /// <summary>
        /// Navigates step.Parent.Parent.Parent (Job -&gt; JobServer -&gt; Server) to get a server property.
        /// </summary>
        internal static string GetParentServerProperty(PSObject step, string propertyName)
        {
            if (step == null || propertyName == null)
                return null;
            try
            {
                // step.Parent = Job
                PSPropertyInfo parentProp = step.Properties["Parent"];
                if (parentProp == null || parentProp.Value == null) return null;

                PSObject job = PSObject.AsPSObject(parentProp.Value);
                // Job.Parent = JobServer
                PSPropertyInfo jobParentProp = job.Properties["Parent"];
                if (jobParentProp == null || jobParentProp.Value == null) return null;

                PSObject jobServer = PSObject.AsPSObject(jobParentProp.Value);
                // JobServer.Parent = Server
                PSPropertyInfo jsParentProp = jobServer.Properties["Parent"];
                if (jsParentProp == null || jsParentProp.Value == null) return null;

                PSObject server = PSObject.AsPSObject(jsParentProp.Value);
                PSPropertyInfo prop = server.Properties[propertyName];
                if (prop != null && prop.Value != null)
                    return prop.Value.ToString();
            }
            catch (Exception)
            {
                // Navigation may fail if parent chain is incomplete
            }
            return null;
        }

        /// <summary>
        /// Gets the parent job name from a job step (step.Parent.Name).
        /// </summary>
        internal static string GetParentJobName(PSObject step)
        {
            if (step == null)
                return null;
            try
            {
                PSPropertyInfo parentProp = step.Properties["Parent"];
                if (parentProp == null || parentProp.Value == null) return null;

                PSObject job = PSObject.AsPSObject(parentProp.Value);
                PSPropertyInfo nameProp = job.Properties["Name"];
                if (nameProp != null && nameProp.Value != null)
                    return nameProp.Value.ToString();
            }
            catch (Exception)
            {
                // Navigation may fail
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
