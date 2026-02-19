using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Compares SQL Server logins across Availability Group replicas to identify configuration differences.
    /// Reports missing logins and optionally compares modify_date timestamps to detect configuration drift.
    /// </summary>
    [Cmdlet("Compare", "DbaAgReplicaLogin")]
    public class CompareDbaAgReplicaLoginCommand : DbaBaseCmdlet
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
        /// Specifies one or more Availability Group names to compare logins across their replicas.
        /// </summary>
        [Parameter()]
        public string[] AvailabilityGroup { get; set; }

        /// <summary>
        /// Excludes built-in system logins from the comparison results.
        /// </summary>
        [Parameter()]
        public SwitchParameter ExcludeSystemLogin { get; set; }

        /// <summary>
        /// Includes modify_date comparison in addition to login name comparison.
        /// </summary>
        [Parameter()]
        public SwitchParameter IncludeModifiedDate { get; set; }

        #endregion Parameters

        #region Static ScriptBlocks

        /// <summary>
        /// Connects to a specific replica and retrieves logins with optional modify_date data.
        /// When includeModifiedDate is true, queries sys.server_principals and returns enriched objects.
        /// </summary>
        private static readonly ScriptBlock _getReplicaLoginsScript = ScriptBlock.Create(@"
param($replicaInstance, $sc, $hasCred, $excludeSystem, $includeModifiedDate)
$params = @{ SqlInstance = $replicaInstance }
if ($hasCred) { $params['SqlCredential'] = $sc }
$server = Connect-DbaInstance @params
if ($excludeSystem) {
    $logins = Get-DbaLogin -SqlInstance $server -ExcludeSystemLogin
} else {
    $logins = Get-DbaLogin -SqlInstance $server
}
if ($includeModifiedDate) {
    $query = ""SELECT name, modify_date FROM sys.server_principals WHERE [type] IN ('S', 'U', 'G')""
    $modifyDates = Invoke-DbaQuery -SqlInstance $server -Query $query -As PSObject
    foreach ($login in $logins) {
        $modifyDate = ($modifyDates | Where-Object name -eq $login.Name).modify_date
        [PSCustomObject]@{
            Name       = $login.Name
            ModifyDate = $modifyDate
            CreateDate = $login.CreateDate
            LoginType  = $login.LoginType
        }
    }
} else {
    $logins
}
");

        #endregion Static ScriptBlocks

        /// <summary>
        /// Processes each pipeline instance to compare logins across AG replicas.
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
            Dictionary<string, List<PSObject>> loginsByReplica = new Dictionary<string, List<PSObject>>();
            List<string> allLoginNames = new List<string>();
            HashSet<string> loginNameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string replicaInstance in replicaNames)
            {
                try
                {
                    Collection<PSObject> loginResults = InvokeCommand.InvokeScript(
                        false, _getReplicaLoginsScript, null,
                        new object[]
                        {
                            replicaInstance, SqlCredential, SqlCredential != null,
                            ExcludeSystemLogin.IsPresent, IncludeModifiedDate.IsPresent
                        });

                    List<PSObject> logins = new List<PSObject>();
                    if (loginResults != null)
                    {
                        foreach (PSObject login in loginResults)
                        {
                            if (login == null) continue;
                            string loginName = AgReplicaHelpers.GetPropertyString(login, "Name");
                            if (loginName == null) continue;

                            logins.Add(login);
                            if (!loginNameSet.Contains(loginName))
                            {
                                loginNameSet.Add(loginName);
                                allLoginNames.Add(loginName);
                            }
                        }
                    }
                    loginsByReplica[replicaInstance] = logins;
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("Failed to retrieve logins from replica {0}", replicaInstance),
                        errorRecord: new ErrorRecord(ex, "CompareDbaAgReplicaLogin_GetLogins", ErrorCategory.ConnectionError, replicaInstance),
                        target: replicaInstance, isContinue: true);
                    TestFunctionInterrupt();
                }
            }

            foreach (string loginName in allLoginNames)
            {
                List<PSObject> differences = new List<PSObject>();

                foreach (string replicaInstance in replicaNames)
                {
                    List<PSObject> replicaLogins;
                    if (!loginsByReplica.TryGetValue(replicaInstance, out replicaLogins))
                        continue;

                    PSObject login = AgReplicaHelpers.FindByName(replicaLogins, loginName);

                    if (login == null)
                    {
                        PSObject diff = new PSObject();
                        diff.Properties.Add(new PSNoteProperty("AvailabilityGroup", agName));
                        diff.Properties.Add(new PSNoteProperty("Replica", replicaInstance));
                        diff.Properties.Add(new PSNoteProperty("LoginName", loginName));
                        diff.Properties.Add(new PSNoteProperty("Status", "Missing"));
                        diff.Properties.Add(new PSNoteProperty("ModifyDate", null));
                        diff.Properties.Add(new PSNoteProperty("CreateDate", null));
                        differences.Add(diff);
                    }
                    else if (IncludeModifiedDate.IsPresent)
                    {
                        object modifyDate = AgReplicaHelpers.GetPropertyValue(login, "ModifyDate");
                        object createDate = AgReplicaHelpers.GetPropertyValue(login, "CreateDate");
                        PSObject diff = new PSObject();
                        diff.Properties.Add(new PSNoteProperty("AvailabilityGroup", agName));
                        diff.Properties.Add(new PSNoteProperty("Replica", replicaInstance));
                        diff.Properties.Add(new PSNoteProperty("LoginName", loginName));
                        diff.Properties.Add(new PSNoteProperty("Status", "Present"));
                        diff.Properties.Add(new PSNoteProperty("ModifyDate", modifyDate));
                        diff.Properties.Add(new PSNoteProperty("CreateDate", createDate));
                        differences.Add(diff);
                    }
                }

                OutputDifferencesWithDateCheck(differences);
            }
        }

        /// <summary>
        /// Outputs differences applying the same logic as the PS1:
        /// If there are missing items, output all. If IncludeModifiedDate and dates differ, output all.
        /// </summary>
        private void OutputDifferencesWithDateCheck(List<PSObject> differences)
        {
            if (differences.Count == 0)
                return;

            bool hasMissing = false;
            foreach (PSObject diff in differences)
            {
                if ("Missing".Equals(AgReplicaHelpers.GetPropertyString(diff, "Status"), StringComparison.OrdinalIgnoreCase))
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
                        if ("Present".Equals(AgReplicaHelpers.GetPropertyString(diff, "Status"), StringComparison.OrdinalIgnoreCase))
                        {
                            object dateVal = AgReplicaHelpers.GetPropertyValue(diff, "ModifyDate");
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
                    errorRecord: new ErrorRecord(ex, "CompareDbaAgReplicaLogin_Connect", ErrorCategory.ConnectionError, instance),
                    target: instance, isContinue: true);
            }
            TestFunctionInterrupt();
        }

        #endregion Helpers
    }
}
