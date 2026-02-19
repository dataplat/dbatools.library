using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Compares SQL Agent Operators across Availability Group replicas to identify configuration differences.
    /// Reports operators that are missing on some replicas or have different email addresses across replicas.
    /// </summary>
    [Cmdlet("Compare", "DbaAgReplicaOperator")]
    public class CompareDbaAgReplicaOperatorCommand : DbaBaseCmdlet
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
        /// Specifies one or more Availability Group names to compare operators across their replicas.
        /// </summary>
        [Parameter()]
        public string[] AvailabilityGroup { get; set; }

        #endregion Parameters

        #region Static ScriptBlocks

        /// <summary>
        /// Connects to a specific replica and retrieves all SQL Agent Operators.
        /// </summary>
        private static readonly ScriptBlock _getReplicaOperatorsScript = ScriptBlock.Create(@"
param($replicaInstance, $sc, $hasCred)
$params = @{ SqlInstance = $replicaInstance }
if ($hasCred) { $params['SqlCredential'] = $sc }
$server = Connect-DbaInstance @params
Get-DbaAgentOperator -SqlInstance $server
");

        #endregion Static ScriptBlocks

        /// <summary>
        /// Processes each pipeline instance to compare operators across AG replicas.
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
                string agName = AgReplicaHelpers.GetPropertyString(agInfo, "AgName");
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
            Dictionary<string, List<PSObject>> operatorsByReplica = new Dictionary<string, List<PSObject>>();
            List<string> allOperatorNames = new List<string>();
            HashSet<string> operatorNameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string replicaInstance in replicaNames)
            {
                try
                {
                    Collection<PSObject> opResults = InvokeCommand.InvokeScript(
                        false, _getReplicaOperatorsScript, null,
                        new object[] { replicaInstance, SqlCredential, SqlCredential != null });

                    List<PSObject> operators = new List<PSObject>();
                    if (opResults != null)
                    {
                        foreach (PSObject op in opResults)
                        {
                            if (op == null) continue;
                            string opName = AgReplicaHelpers.GetPropertyString(op, "Name");
                            if (opName == null) continue;

                            operators.Add(op);
                            if (!operatorNameSet.Contains(opName))
                            {
                                operatorNameSet.Add(opName);
                                allOperatorNames.Add(opName);
                            }
                        }
                    }
                    operatorsByReplica[replicaInstance] = operators;
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("Failed to retrieve operators from replica {0}", replicaInstance),
                        errorRecord: new ErrorRecord(ex, "CompareDbaAgReplicaOperator_GetOps", ErrorCategory.ConnectionError, replicaInstance),
                        target: replicaInstance, isContinue: true);
                    TestFunctionInterrupt();
                }
            }

            foreach (string operatorName in allOperatorNames)
            {
                List<PSObject> differences = new List<PSObject>();

                foreach (string replicaInstance in replicaNames)
                {
                    List<PSObject> replicaOps;
                    if (!operatorsByReplica.TryGetValue(replicaInstance, out replicaOps))
                        continue;

                    PSObject op = AgReplicaHelpers.FindByName(replicaOps, operatorName);

                    if (op == null)
                    {
                        PSObject diff = new PSObject();
                        diff.Properties.Add(new PSNoteProperty("AvailabilityGroup", agName));
                        diff.Properties.Add(new PSNoteProperty("Replica", replicaInstance));
                        diff.Properties.Add(new PSNoteProperty("OperatorName", operatorName));
                        diff.Properties.Add(new PSNoteProperty("Status", "Missing"));
                        diff.Properties.Add(new PSNoteProperty("EmailAddress", null));
                        differences.Add(diff);
                    }
                    else
                    {
                        string email = AgReplicaHelpers.GetPropertyString(op, "EmailAddress");
                        PSObject diff = new PSObject();
                        diff.Properties.Add(new PSNoteProperty("AvailabilityGroup", agName));
                        diff.Properties.Add(new PSNoteProperty("Replica", replicaInstance));
                        diff.Properties.Add(new PSNoteProperty("OperatorName", operatorName));
                        diff.Properties.Add(new PSNoteProperty("Status", "Present"));
                        diff.Properties.Add(new PSNoteProperty("EmailAddress", email));
                        differences.Add(diff);
                    }
                }

                // Output only if there are missing operators or different email addresses
                if (differences.Count > 0)
                {
                    bool hasMissing = false;
                    HashSet<string> uniqueEmails = new HashSet<string>(StringComparer.Ordinal);
                    foreach (PSObject diff in differences)
                    {
                        string status = AgReplicaHelpers.GetPropertyString(diff, "Status");
                        if ("Missing".Equals(status, StringComparison.OrdinalIgnoreCase))
                        {
                            hasMissing = true;
                        }
                        else
                        {
                            string email = AgReplicaHelpers.GetPropertyString(diff, "EmailAddress") ?? "";
                            uniqueEmails.Add(email);
                        }
                    }

                    if (hasMissing || uniqueEmails.Count > 1)
                    {
                        foreach (PSObject diff in differences)
                            WriteObject(diff);
                    }
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
                    errorRecord: new ErrorRecord(ex, "CompareDbaAgReplicaOperator_Connect", ErrorCategory.ConnectionError, instance),
                    target: instance, isContinue: true);
            }
            TestFunctionInterrupt();
        }

        #endregion Helpers
    }
}
