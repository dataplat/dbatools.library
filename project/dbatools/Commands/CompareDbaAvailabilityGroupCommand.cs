using System;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Compares configuration across Availability Group replicas to identify differences in
    /// Jobs, Logins, Credentials, and Operators. Orchestrates calls to the individual
    /// Compare-DbaAgReplica* commands based on the Type parameter.
    /// </summary>
    [Cmdlet("Compare", "DbaAvailabilityGroup")]
    public class CompareDbaAvailabilityGroupCommand : DbaBaseCmdlet
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
        /// Specifies one or more Availability Group names to compare across their replicas.
        /// </summary>
        [Parameter()]
        public string[] AvailabilityGroup { get; set; }

        /// <summary>
        /// Specifies which object types to compare. Default is All.
        /// </summary>
        [Parameter()]
        [ValidateSet("AgentJob", "Login", "Credential", "Operator", "All")]
        public string[] Type { get; set; } = new string[] { "All" };

        /// <summary>
        /// Excludes system jobs from the agent job comparison.
        /// </summary>
        [Parameter()]
        public SwitchParameter ExcludeSystemJob { get; set; }

        /// <summary>
        /// Excludes built-in system logins from the login comparison.
        /// </summary>
        [Parameter()]
        public SwitchParameter ExcludeSystemLogin { get; set; }

        /// <summary>
        /// Includes DateLastModified comparison for jobs and modify_date comparison for logins.
        /// </summary>
        [Parameter()]
        public SwitchParameter IncludeModifiedDate { get; set; }

        #endregion Parameters

        #region Static ScriptBlocks

        /// <summary>
        /// Calls Compare-DbaAgReplicaAgentJob with appropriate parameters.
        /// </summary>
        private static readonly ScriptBlock _compareAgentJobScript = ScriptBlock.Create(@"
param($si, $sc, $hasCred, $ag, $hasAg, $excludeSystemJob, $includeModifiedDate, $enableException)
$params = @{ SqlInstance = $si }
if ($hasCred) { $params['SqlCredential'] = $sc }
if ($hasAg) { $params['AvailabilityGroup'] = $ag }
if ($excludeSystemJob) { $params['ExcludeSystemJob'] = $true }
if ($includeModifiedDate) { $params['IncludeModifiedDate'] = $true }
if ($enableException) { $params['EnableException'] = $true }
Compare-DbaAgReplicaAgentJob @params
");

        /// <summary>
        /// Calls Compare-DbaAgReplicaLogin with appropriate parameters.
        /// </summary>
        private static readonly ScriptBlock _compareLoginScript = ScriptBlock.Create(@"
param($si, $sc, $hasCred, $ag, $hasAg, $excludeSystemLogin, $includeModifiedDate, $enableException)
$params = @{ SqlInstance = $si }
if ($hasCred) { $params['SqlCredential'] = $sc }
if ($hasAg) { $params['AvailabilityGroup'] = $ag }
if ($excludeSystemLogin) { $params['ExcludeSystemLogin'] = $true }
if ($includeModifiedDate) { $params['IncludeModifiedDate'] = $true }
if ($enableException) { $params['EnableException'] = $true }
Compare-DbaAgReplicaLogin @params
");

        /// <summary>
        /// Calls Compare-DbaAgReplicaCredential with appropriate parameters.
        /// </summary>
        private static readonly ScriptBlock _compareCredentialScript = ScriptBlock.Create(@"
param($si, $sc, $hasCred, $ag, $hasAg, $enableException)
$params = @{ SqlInstance = $si }
if ($hasCred) { $params['SqlCredential'] = $sc }
if ($hasAg) { $params['AvailabilityGroup'] = $ag }
if ($enableException) { $params['EnableException'] = $true }
Compare-DbaAgReplicaCredential @params
");

        /// <summary>
        /// Calls Compare-DbaAgReplicaOperator with appropriate parameters.
        /// </summary>
        private static readonly ScriptBlock _compareOperatorScript = ScriptBlock.Create(@"
param($si, $sc, $hasCred, $ag, $hasAg, $enableException)
$params = @{ SqlInstance = $si }
if ($hasCred) { $params['SqlCredential'] = $sc }
if ($hasAg) { $params['AvailabilityGroup'] = $ag }
if ($enableException) { $params['EnableException'] = $true }
Compare-DbaAgReplicaOperator @params
");

        #endregion Static ScriptBlocks

        /// <summary>
        /// Processes each pipeline instance, delegating to the appropriate Compare-DbaAgReplica* commands.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (TestFunctionInterrupt())
                return;
            if (SqlInstance == null)
                return;

            // Expand "All" to the full list (matching PS1 behavior)
            string[] effectiveTypes = Type;
            if (effectiveTypes == null || effectiveTypes.Length == 0)
            {
                effectiveTypes = new string[] { "AgentJob", "Login", "Credential", "Operator" };
            }
            else
            {
                foreach (string t in effectiveTypes)
                {
                    if (String.Equals(t, "All", StringComparison.OrdinalIgnoreCase))
                    {
                        effectiveTypes = new string[] { "AgentJob", "Login", "Credential", "Operator" };
                        break;
                    }
                }
            }

            bool hasAg = TestBound("AvailabilityGroup");
            bool hasCred = SqlCredential != null;
            bool enableEx = EnableException.IsPresent;

            foreach (DbaInstanceParameter instance in SqlInstance)
            {
                foreach (string compType in effectiveTypes)
                {
                    try
                    {
                        Collection<PSObject> results = null;

                        if (String.Equals(compType, "AgentJob", StringComparison.OrdinalIgnoreCase))
                        {
                            results = InvokeCommand.InvokeScript(
                                false, _compareAgentJobScript, null,
                                new object[]
                                {
                                    instance, SqlCredential, hasCred,
                                    AvailabilityGroup, hasAg,
                                    ExcludeSystemJob.IsPresent, IncludeModifiedDate.IsPresent, enableEx
                                });
                        }
                        else if (String.Equals(compType, "Login", StringComparison.OrdinalIgnoreCase))
                        {
                            results = InvokeCommand.InvokeScript(
                                false, _compareLoginScript, null,
                                new object[]
                                {
                                    instance, SqlCredential, hasCred,
                                    AvailabilityGroup, hasAg,
                                    ExcludeSystemLogin.IsPresent, IncludeModifiedDate.IsPresent, enableEx
                                });
                        }
                        else if (String.Equals(compType, "Credential", StringComparison.OrdinalIgnoreCase))
                        {
                            results = InvokeCommand.InvokeScript(
                                false, _compareCredentialScript, null,
                                new object[]
                                {
                                    instance, SqlCredential, hasCred,
                                    AvailabilityGroup, hasAg, enableEx
                                });
                        }
                        else if (String.Equals(compType, "Operator", StringComparison.OrdinalIgnoreCase))
                        {
                            results = InvokeCommand.InvokeScript(
                                false, _compareOperatorScript, null,
                                new object[]
                                {
                                    instance, SqlCredential, hasCred,
                                    AvailabilityGroup, hasAg, enableEx
                                });
                        }

                        if (results != null)
                        {
                            foreach (PSObject result in results)
                            {
                                if (result != null)
                                    WriteObject(result);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        StopFunction(
                            String.Format("Error comparing {0} on {1}", compType, instance),
                            errorRecord: new ErrorRecord(ex, String.Format("CompareDbaAvailabilityGroup_{0}", compType), ErrorCategory.InvalidOperation, instance),
                            target: instance, isContinue: true);
                        TestFunctionInterrupt();
                    }
                }
            }
        }
    }
}
