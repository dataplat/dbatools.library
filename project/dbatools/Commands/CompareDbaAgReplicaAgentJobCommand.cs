using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Compares SQL Agent Jobs across Availability Group replicas to identify configuration differences.
    /// Connects to each replica independently and reports missing jobs or date drift when IncludeModifiedDate is specified.
    /// </summary>
    [Cmdlet("Compare", "DbaAgReplicaAgentJob")]
    public class CompareDbaAgReplicaAgentJobCommand : DbaBaseCmdlet
    {
        #region Parameters

        /// <summary>
        /// The target SQL Server instance or instances. Can be any replica in the Availability Group.
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        public DbaInstanceParameter[] SqlInstance { get; set; }

        /// <summary>
        /// Login to the target instance using alternative credentials.
        /// </summary>
        [Parameter()]
        public PSCredential SqlCredential { get; set; }

        /// <summary>
        /// Specifies one or more Availability Group names to compare jobs across their replicas.
        /// </summary>
        [Parameter()]
        public string[] AvailabilityGroup { get; set; }

        /// <summary>
        /// Excludes system jobs from the comparison results.
        /// </summary>
        [Parameter()]
        public SwitchParameter ExcludeSystemJob { get; set; }

        /// <summary>
        /// Includes DateLastModified comparison in addition to job name comparison.
        /// </summary>
        [Parameter()]
        public SwitchParameter IncludeModifiedDate { get; set; }

        #endregion Parameters

        #region Static ScriptBlocks

        /// <summary>
        /// Connects to a specific replica and retrieves all SQL Agent Jobs.
        /// </summary>
        private static readonly ScriptBlock _getReplicaJobsScript = ScriptBlock.Create(@"
param($replicaInstance, $sc, $hasCred)
$params = @{ SqlInstance = $replicaInstance }
if ($hasCred) { $params['SqlCredential'] = $sc }
$server = Connect-DbaInstance @params
Get-DbaAgentJob -SqlInstance $server
");

        #endregion Static ScriptBlocks

        /// <summary>
        /// System jobs to exclude when ExcludeSystemJob is specified (matches PS1 list).
        /// </summary>
        private static readonly HashSet<string> _systemJobs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "syspolicy_purge_history",
            "DBA_AgentJobHistoryRetention",
            "DBA_IndexOptimize",
            "DBA_CommandLogCleanup"
        };

        /// <summary>
        /// Processes each pipeline instance to compare agent jobs across AG replicas.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (TestFunctionInterrupt())
                return;
            if (SqlInstance == null)
                return;

            foreach (DbaInstanceParameter instance in SqlInstance)
            {
                ProcessInstance(instance);
            }
        }

        private void ProcessInstance(DbaInstanceParameter instance)
        {
            Collection<PSObject> agInfoResults;
            try
            {
                agInfoResults = InvokeCommand.InvokeScript(
                    false, AgReplicaHelpers.GetAgReplicaInfoScript, null,
                    new object[]
                    {
                        instance, SqlCredential, SqlCredential != null,
                        AvailabilityGroup, TestBound("AvailabilityGroup")
                    });
            }
            catch (Exception ex)
            {
                HandleConnectionError(ex, instance);
                return;
            }

            if (agInfoResults == null || agInfoResults.Count == 0)
                return;

            foreach (PSObject agInfo in agInfoResults)
            {
                if (agInfo == null) continue;
                string agName = DbaBaseCmdlet.GetPropertyString(agInfo, "AgName");
                string[] replicaNames = AgReplicaHelpers.GetStringArray(agInfo, "ReplicaNames");

                if (replicaNames == null || replicaNames.Length < 2)
                {
                    StopFunction(
                        String.Format("Availability Group '{0}' has fewer than 2 replicas. Nothing to compare.", agName),
                        target: agName, isContinue: true);
                    TestFunctionInterrupt();
                    continue;
                }

                ProcessAvailabilityGroup(agName, replicaNames);
            }
        }

        private void ProcessAvailabilityGroup(string agName, string[] replicaNames)
        {
            Dictionary<string, List<PSObject>> jobsByReplica = new Dictionary<string, List<PSObject>>();
            List<string> allJobNames = new List<string>();
            HashSet<string> jobNameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string replicaInstance in replicaNames)
            {
                try
                {
                    Collection<PSObject> jobResults = InvokeCommand.InvokeScript(
                        false, _getReplicaJobsScript, null,
                        new object[] { replicaInstance, SqlCredential, SqlCredential != null });

                    List<PSObject> jobs = new List<PSObject>();
                    if (jobResults != null)
                    {
                        foreach (PSObject job in jobResults)
                        {
                            if (job == null) continue;
                            string jobName = DbaBaseCmdlet.GetPropertyString(job, "Name");
                            if (jobName == null) continue;

                            if (ExcludeSystemJob.IsPresent && _systemJobs.Contains(jobName))
                                continue;

                            jobs.Add(job);
                            if (!jobNameSet.Contains(jobName))
                            {
                                jobNameSet.Add(jobName);
                                allJobNames.Add(jobName);
                            }
                        }
                    }
                    jobsByReplica[replicaInstance] = jobs;
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("Failed to retrieve jobs from replica {0}", replicaInstance),
                        errorRecord: new ErrorRecord(ex, "CompareDbaAgReplicaAgentJob_GetJobs", ErrorCategory.ConnectionError, replicaInstance),
                        target: replicaInstance, isContinue: true);
                    TestFunctionInterrupt();
                }
            }

            foreach (string jobName in allJobNames)
            {
                List<PSObject> differences = new List<PSObject>();

                foreach (string replicaInstance in replicaNames)
                {
                    List<PSObject> replicaJobs;
                    if (!jobsByReplica.TryGetValue(replicaInstance, out replicaJobs))
                        continue;

                    PSObject job = AgReplicaHelpers.FindByName(replicaJobs, jobName);

                    if (job == null)
                    {
                        PSObject diff = new PSObject();
                        diff.Properties.Add(new PSNoteProperty("AvailabilityGroup", agName));
                        diff.Properties.Add(new PSNoteProperty("Replica", replicaInstance));
                        diff.Properties.Add(new PSNoteProperty("JobName", jobName));
                        diff.Properties.Add(new PSNoteProperty("Status", "Missing"));
                        diff.Properties.Add(new PSNoteProperty("DateLastModified", null));
                        differences.Add(diff);
                    }
                    else if (IncludeModifiedDate.IsPresent)
                    {
                        object dateLastModified = AgReplicaHelpers.GetPropertyValue(job, "DateLastModified");
                        PSObject diff = new PSObject();
                        diff.Properties.Add(new PSNoteProperty("AvailabilityGroup", agName));
                        diff.Properties.Add(new PSNoteProperty("Replica", replicaInstance));
                        diff.Properties.Add(new PSNoteProperty("JobName", jobName));
                        diff.Properties.Add(new PSNoteProperty("Status", "Present"));
                        diff.Properties.Add(new PSNoteProperty("DateLastModified", dateLastModified));
                        differences.Add(diff);
                    }
                }

                OutputDifferencesWithDateCheck(differences, "DateLastModified");
            }
        }

        /// <summary>
        /// Outputs differences applying the same logic as the PS1:
        /// If there are missing items, output all. If IncludeModifiedDate and dates differ, output all.
        /// </summary>
        private void OutputDifferencesWithDateCheck(List<PSObject> differences, string datePropertyName)
        {
            if (differences.Count == 0)
                return;

            bool hasMissing = false;
            foreach (PSObject diff in differences)
            {
                if ("Missing".Equals(DbaBaseCmdlet.GetPropertyString(diff, "Status"), StringComparison.OrdinalIgnoreCase))
                {
                    hasMissing = true;
                    break;
                }
            }

            if (hasMissing || IncludeModifiedDate.IsPresent)
            {
                if (IncludeModifiedDate.IsPresent)
                {
                    HashSet<string> uniqueDates = new HashSet<string>();
                    foreach (PSObject diff in differences)
                    {
                        if ("Present".Equals(DbaBaseCmdlet.GetPropertyString(diff, "Status"), StringComparison.OrdinalIgnoreCase))
                        {
                            object dateVal = AgReplicaHelpers.GetPropertyValue(diff, datePropertyName);
                            if (dateVal is DateTime dt)
                                uniqueDates.Add(dt.ToString("o"));
                            else
                                uniqueDates.Add(dateVal != null ? dateVal.ToString() : "");
                        }
                    }

                    if (uniqueDates.Count > 1 || hasMissing)
                    {
                        foreach (PSObject diff in differences)
                            WriteObject(diff);
                    }
                }
                else
                {
                    foreach (PSObject diff in differences)
                        WriteObject(diff);
                }
            }
        }

        #region Helpers

        private void HandleConnectionError(Exception ex, DbaInstanceParameter instance)
        {
            string fullMsg = AgReplicaHelpers.GetFullExceptionMessage(ex);
            if (fullMsg.Contains("HADR_NOT_ENABLED"))
            {
                StopFunction(
                    String.Format("Availability Group (HADR) is not configured for the instance: {0}.", instance),
                    target: instance, isContinue: true);
            }
            else if (fullMsg.Contains("NO_AG_FOUND"))
            {
                StopFunction(
                    String.Format("No Availability Groups found on {0} matching the specified criteria.", instance),
                    target: instance, isContinue: true);
            }
            else
            {
                StopFunction(
                    String.Format("Failure connecting to {0}", instance),
                    errorRecord: new ErrorRecord(ex, "CompareDbaAgReplicaAgentJob_Connect", ErrorCategory.ConnectionError, instance),
                    target: instance, isContinue: true);
            }
            TestFunctionInterrupt();
        }

        #endregion Helpers
    }
}
