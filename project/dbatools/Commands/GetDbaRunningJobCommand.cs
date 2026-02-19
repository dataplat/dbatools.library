using System;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Retrieves SQL Server Agent jobs that are currently executing from one or more instances.
    /// Filters out idle jobs and returns only those with an active run status.
    /// </summary>
    [Cmdlet("Get", "DbaRunningJob")]
    // Note: DbaBaseCmdlet used intentionally — SqlInstance is optional here because
    // the command also accepts InputObject (Job objects) from the pipeline.
    // DbaInstanceCmdlet declares SqlInstance as Mandatory, which would break the InputObject-only path.
    public class GetDbaRunningJobCommand : DbaBaseCmdlet
    {
        #region Parameters

        /// <summary>
        /// The target SQL Server instance(s) to check for running jobs.
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        public DbaInstanceParameter[] SqlInstance { get; set; }

        /// <summary>
        /// Credential to use for SQL Server authentication.
        /// </summary>
        [Parameter()]
        public PSCredential SqlCredential { get; set; }

        /// <summary>
        /// Accepts SQL Server Agent job objects piped from Get-DbaAgentJob for filtering to only running jobs.
        /// Use this when you need to check execution status on a specific set of jobs rather than all jobs on an instance.
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        public PSObject[] InputObject { get; set; }

        #endregion Parameters

        #region Cached ScriptBlocks

        private static readonly ScriptBlock ConnectWithCredScript =
            ScriptBlock.Create("param($i, $c) Connect-DbaInstance -SqlInstance $i -SqlCredential $c");

        private static readonly ScriptBlock ConnectScript =
            ScriptBlock.Create("param($i) Connect-DbaInstance -SqlInstance $i");

        private static readonly ScriptBlock RefreshJobsScript =
            ScriptBlock.Create("param($s) $s.JobServer.Jobs.Refresh($true)");

        private static readonly ScriptBlock GetAgentJobScript =
            ScriptBlock.Create("param($s) Get-DbaAgentJob -SqlInstance $s -IncludeExecution");

        private static readonly ScriptBlock RefreshJobScript =
            ScriptBlock.Create("param($j) $j.Refresh()");

        #endregion Cached ScriptBlocks

        /// <summary>
        /// Processes each SQL Server instance or input job object, filtering to only running jobs.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (SqlInstance != null)
            {
                foreach (DbaInstanceParameter instance in SqlInstance)
                {
                    try
                    {
                        object server = ConnectInstance(instance);
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

                        // Refresh JobServer.Jobs (including children) for up-to-date information
                        RefreshJobServerJobs(server);

                        // Get all jobs with execution info via Get-DbaAgentJob -IncludeExecution
                        Collection<PSObject> jobs = InvokeGetDbaAgentJob(server);
                        if (jobs == null || jobs.Count == 0)
                            continue;

                        // Filter to only running jobs (CurrentRunStatus != Idle)
                        foreach (PSObject jobObj in jobs)
                        {
                            if (jobObj == null)
                                continue;

                            if (IsRunning(jobObj))
                            {
                                WriteObject(jobObj);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        StopFunction(
                            "Failure",
                            errorRecord: new ErrorRecord(ex, "GetDbaRunningJob_ConnectionError", ErrorCategory.ConnectionError, instance),
                            target: instance,
                            isContinue: true,
                            category: ErrorCategory.ConnectionError);
                        TestFunctionInterrupt();
                        continue;
                    }
                }
            }

            if (InputObject != null)
            {
                foreach (PSObject job in InputObject)
                {
                    if (job == null)
                        continue;

                    // Refresh the job to get up-to-date information.
                    // No try/catch here — matching PS1 behavior where Refresh() errors propagate naturally.
                    RefreshJob(job);

                    if (IsRunning(job))
                    {
                        WriteObject(job);
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
            object[] args;
            ScriptBlock script;
            if (SqlCredential != null)
            {
                script = ConnectWithCredScript;
                args = new object[] { instance, SqlCredential };
            }
            else
            {
                script = ConnectScript;
                args = new object[] { instance };
            }

            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, script, null, args);

            if (results != null && results.Count > 0)
                return results[0].BaseObject;
            return null;
        }

        /// <summary>
        /// Refreshes the JobServer.Jobs collection with children for up-to-date status.
        /// </summary>
        private void RefreshJobServerJobs(object server)
        {
            try
            {
                InvokeCommand.InvokeScript(
                    false, RefreshJobsScript, null, new object[] { server });
            }
            catch (Exception ex)
            {
                WriteMessageAtLevel(
                    String.Format("Failed to refresh job server jobs: {0}", ex.Message),
                    MessageLevel.Warning, null);
            }
        }

        /// <summary>
        /// Calls Get-DbaAgentJob -SqlInstance $server -IncludeExecution to get enriched job objects.
        /// </summary>
        private Collection<PSObject> InvokeGetDbaAgentJob(object server)
        {
            return InvokeCommand.InvokeScript(
                false, GetAgentJobScript, null, new object[] { server });
        }

        /// <summary>
        /// Refreshes a single job object to get up-to-date status information.
        /// </summary>
        private void RefreshJob(PSObject job)
        {
            object baseObj = job.BaseObject;
            InvokeCommand.InvokeScript(
                false, RefreshJobScript, null, new object[] { baseObj });
        }

        /// <summary>
        /// Checks if a job's CurrentRunStatus is not Idle.
        /// </summary>
        internal static bool IsRunning(PSObject jobObj)
        {
            if (jobObj == null)
                return false;
            try
            {
                PSPropertyInfo prop = jobObj.Properties["CurrentRunStatus"];
                if (prop == null || prop.Value == null)
                    return false;
                string status = prop.Value.ToString();
                return !String.Equals(status, "Idle", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        #endregion Helpers
    }
}
