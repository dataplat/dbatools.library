using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Compares server-level objects across Availability Group replicas to identify synchronization
    /// differences that would prevent seamless failover. Checks Logins (with property-level comparison),
    /// Agent Jobs, Credentials, Linked Servers, Agent Operators, Agent Alerts, Agent Proxies, and Custom Errors.
    /// </summary>
    [Cmdlet("Compare", "DbaAgReplicaSync")]
    public class CompareDbaAgReplicaSyncCommand : DbaBaseCmdlet
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
        /// Specifies one or more Availability Group names to compare objects across their replicas.
        /// </summary>
        [Parameter()]
        public string[] AvailabilityGroup { get; set; }

        /// <summary>
        /// Excludes specific object types from comparison.
        /// </summary>
        [Parameter()]
        [ValidateSet("AgentCategory", "AgentOperator", "AgentAlert", "AgentProxy", "AgentSchedule", "AgentJob", "Credentials", "CustomErrors", "DatabaseMail", "LinkedServers", "Logins", "SpConfigure", "SystemTriggers")]
        public string[] Exclude { get; set; }

        #endregion Parameters

        #region Static ScriptBlocks

        /// <summary>
        /// Gets logins with full configuration details for property-level comparison.
        /// Returns PSCustomObjects with login properties and server role membership.
        /// </summary>
        private static readonly ScriptBlock _getLoginsWithConfigScript = ScriptBlock.Create(@"
param($replicaInstance, $sc, $hasCred)
$params = @{ SqlInstance = $replicaInstance }
if ($hasCred) { $params['SqlCredential'] = $sc }
$replicaServer = Connect-DbaInstance @params
$logins = Get-DbaLogin -SqlInstance $replicaServer
foreach ($login in $logins) {
    $config = @{
        Name                      = $login.Name
        IsDisabled                = $login.IsDisabled
        DenyWindowsLogin          = $login.DenyWindowsLogin
        DefaultDatabase           = $login.DefaultDatabase
        Language                  = $login.Language
        LoginType                 = $login.LoginType.ToString()
        PasswordExpirationEnabled = $null
        PasswordPolicyEnforced    = $null
        ServerRoles               = ''
    }
    if ($login.LoginType -eq 'SqlLogin') {
        $config.PasswordExpirationEnabled = $login.PasswordExpirationEnabled
        $config.PasswordPolicyEnforced = $login.PasswordPolicyEnforced
    }
    $roles = @()
    if ($replicaServer.VersionMajor -ge 9) {
        foreach ($role in $replicaServer.Roles) {
            try { $members = $role.EnumMemberNames() } catch { $members = $role.EnumServerRoleMembers() }
            if ($members -contains $login.Name) { $roles += $role.Name }
        }
    }
    $config.ServerRoles = ($roles -join ',')
    [PSCustomObject]$config
}
");

        /// <summary>
        /// Generic script to get named objects from a replica for simple missing-only comparison.
        /// Supports: AgentJob, Credential, LinkedServer, AgentOperator, AgentAlert, AgentProxy.
        /// </summary>
        private static readonly ScriptBlock _getNamedObjectsScript = ScriptBlock.Create(@"
param($replicaInstance, $sc, $hasCred, $objectType)
$params = @{ SqlInstance = $replicaInstance }
if ($hasCred) { $params['SqlCredential'] = $sc }
$server = Connect-DbaInstance @params
switch ($objectType) {
    'AgentJob' { Get-DbaAgentJob -SqlInstance $server }
    'Credential' { $server.Credentials }
    'LinkedServer' { $server.LinkedServers }
    'AgentOperator' { Get-DbaAgentOperator -SqlInstance $server }
    'AgentAlert' { Get-DbaAgentAlert -SqlInstance $server }
    'AgentProxy' { Get-DbaAgentProxy -SqlInstance $server }
}
");

        /// <summary>
        /// Gets custom error (UserDefinedMessage) objects from a replica. These use ID instead of Name.
        /// </summary>
        private static readonly ScriptBlock _getCustomErrorsScript = ScriptBlock.Create(@"
param($replicaInstance, $sc, $hasCred)
$params = @{ SqlInstance = $replicaInstance }
if ($hasCred) { $params['SqlCredential'] = $sc }
$server = Connect-DbaInstance @params
$server.UserDefinedMessages
");

        #endregion Static ScriptBlocks

        /// <summary>
        /// Processes each pipeline instance to compare server-level objects across AG replicas.
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
            HashSet<string> excludeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Exclude != null)
            {
                foreach (string ex in Exclude)
                    excludeSet.Add(ex);
            }

            if (!excludeSet.Contains("Logins"))
            {
                CompareLogins(agName, replicaNames);
            }

            if (!excludeSet.Contains("AgentJob"))
            {
                CompareSimpleObjects(agName, replicaNames, "AgentJob", "AgentJob", "jobs");
            }
            if (!excludeSet.Contains("Credentials"))
            {
                CompareSimpleObjects(agName, replicaNames, "Credential", "Credential", "credentials");
            }
            if (!excludeSet.Contains("LinkedServers"))
            {
                CompareSimpleObjects(agName, replicaNames, "LinkedServer", "LinkedServer", "linked servers");
            }
            if (!excludeSet.Contains("AgentOperator"))
            {
                CompareSimpleObjects(agName, replicaNames, "AgentOperator", "AgentOperator", "operators");
            }
            if (!excludeSet.Contains("AgentAlert"))
            {
                CompareSimpleObjects(agName, replicaNames, "AgentAlert", "AgentAlert", "alerts");
            }
            if (!excludeSet.Contains("AgentProxy"))
            {
                CompareSimpleObjects(agName, replicaNames, "AgentProxy", "AgentProxy", "proxies");
            }

            if (!excludeSet.Contains("CustomErrors"))
            {
                CompareCustomErrors(agName, replicaNames);
            }
        }

        #region Comparison Methods

        /// <summary>
        /// Compares logins across replicas with property-level difference detection.
        /// Reports Missing logins and Different logins (property differences).
        /// </summary>
        private void CompareLogins(string agName, string[] replicaNames)
        {
            Dictionary<string, List<PSObject>> loginsByReplica = new Dictionary<string, List<PSObject>>();
            List<string> allLoginNames = new List<string>();
            HashSet<string> loginNameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string replicaInstance in replicaNames)
            {
                try
                {
                    Collection<PSObject> results = InvokeCommand.InvokeScript(
                        false, _getLoginsWithConfigScript, null,
                        new object[] { replicaInstance, SqlCredential, SqlCredential != null });

                    List<PSObject> logins = new List<PSObject>();
                    if (results != null)
                    {
                        foreach (PSObject login in results)
                        {
                            if (login == null) continue;
                            string name = AgReplicaHelpers.GetPropertyString(login, "Name");
                            if (name == null) continue;
                            logins.Add(login);
                            if (!loginNameSet.Contains(name))
                            {
                                loginNameSet.Add(name);
                                allLoginNames.Add(name);
                            }
                        }
                    }
                    loginsByReplica[replicaInstance] = logins;
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("Failed to retrieve logins from replica {0}", replicaInstance),
                        errorRecord: new ErrorRecord(ex, "CompareDbaAgReplicaSync_GetLogins", ErrorCategory.ConnectionError, replicaInstance),
                        target: replicaInstance, isContinue: true);
                    TestFunctionInterrupt();
                }
            }

            // Use the first replica as the base for property comparison (PS1 uses first hashtable entry)
            string baseReplica = null;
            foreach (string rn in replicaNames)
            {
                if (loginsByReplica.ContainsKey(rn))
                {
                    baseReplica = rn;
                    break;
                }
            }
            if (baseReplica == null)
                return;

            foreach (string loginName in allLoginNames)
            {
                PSObject baseLogin = AgReplicaHelpers.FindByName(loginsByReplica[baseReplica], loginName);

                foreach (string replicaInstance in replicaNames)
                {
                    List<PSObject> replicaLogins;
                    if (!loginsByReplica.TryGetValue(replicaInstance, out replicaLogins))
                        continue;

                    PSObject login = AgReplicaHelpers.FindByName(replicaLogins, loginName);

                    if (login == null)
                    {
                        PSObject output = new PSObject();
                        output.Properties.Add(new PSNoteProperty("AvailabilityGroup", agName));
                        output.Properties.Add(new PSNoteProperty("Replica", replicaInstance));
                        output.Properties.Add(new PSNoteProperty("ObjectType", "Login"));
                        output.Properties.Add(new PSNoteProperty("ObjectName", loginName));
                        output.Properties.Add(new PSNoteProperty("Status", "Missing"));
                        output.Properties.Add(new PSNoteProperty("PropertyDifferences", null));
                        WriteObject(output);
                    }
                    else if (baseLogin != null)
                    {
                        List<string> propertyDiffs = CompareLoginProperties(baseLogin, login);
                        if (propertyDiffs.Count > 0)
                        {
                            PSObject output = new PSObject();
                            output.Properties.Add(new PSNoteProperty("AvailabilityGroup", agName));
                            output.Properties.Add(new PSNoteProperty("Replica", replicaInstance));
                            output.Properties.Add(new PSNoteProperty("ObjectType", "Login"));
                            output.Properties.Add(new PSNoteProperty("ObjectName", loginName));
                            output.Properties.Add(new PSNoteProperty("Status", "Different"));
                            output.Properties.Add(new PSNoteProperty("PropertyDifferences", String.Join("; ", propertyDiffs.ToArray())));
                            WriteObject(output);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Compares login properties between base and current config.
        /// Returns list of difference descriptions.
        /// </summary>
        private List<string> CompareLoginProperties(PSObject baseConfig, PSObject config)
        {
            List<string> diffs = new List<string>();

            CompareProperty(diffs, baseConfig, config, "IsDisabled");
            CompareProperty(diffs, baseConfig, config, "DenyWindowsLogin");
            CompareProperty(diffs, baseConfig, config, "DefaultDatabase");
            CompareProperty(diffs, baseConfig, config, "Language");

            // SQL Login specific properties
            string baseLoginType = AgReplicaHelpers.GetPropertyString(baseConfig, "LoginType");
            string configLoginType = AgReplicaHelpers.GetPropertyString(config, "LoginType");
            if ("SqlLogin".Equals(baseLoginType, StringComparison.OrdinalIgnoreCase) &&
                "SqlLogin".Equals(configLoginType, StringComparison.OrdinalIgnoreCase))
            {
                CompareProperty(diffs, baseConfig, config, "PasswordExpirationEnabled");
                CompareProperty(diffs, baseConfig, config, "PasswordPolicyEnforced");
            }

            // Compare server roles
            string baseRoles = AgReplicaHelpers.GetPropertyString(baseConfig, "ServerRoles") ?? "";
            string configRoles = AgReplicaHelpers.GetPropertyString(config, "ServerRoles") ?? "";

            HashSet<string> baseRoleSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> configRoleSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string role in baseRoles.Split(','))
            {
                if (!String.IsNullOrEmpty(role))
                    baseRoleSet.Add(role);
            }
            foreach (string role in configRoles.Split(','))
            {
                if (!String.IsNullOrEmpty(role))
                    configRoleSet.Add(role);
            }

            List<string> missingRoles = new List<string>();
            List<string> extraRoles = new List<string>();
            foreach (string role in baseRoleSet)
            {
                if (!configRoleSet.Contains(role))
                    missingRoles.Add(role);
            }
            foreach (string role in configRoleSet)
            {
                if (!baseRoleSet.Contains(role))
                    extraRoles.Add(role);
            }

            if (missingRoles.Count > 0)
            {
                diffs.Add(String.Format("Missing ServerRoles: {0}", String.Join(", ", missingRoles.ToArray())));
            }
            if (extraRoles.Count > 0)
            {
                diffs.Add(String.Format("Extra ServerRoles: {0}", String.Join(", ", extraRoles.ToArray())));
            }

            return diffs;
        }

        /// <summary>
        /// Compares a single property between base and config, adding to diffs if different.
        /// </summary>
        private void CompareProperty(List<string> diffs, PSObject baseConfig, PSObject config, string propertyName)
        {
            string baseVal = AgReplicaHelpers.GetPropertyString(baseConfig, propertyName) ?? "";
            string configVal = AgReplicaHelpers.GetPropertyString(config, propertyName) ?? "";
            if (!String.Equals(baseVal, configVal, StringComparison.OrdinalIgnoreCase))
            {
                diffs.Add(String.Format("{0}: {1} vs {2}", propertyName, configVal, baseVal));
            }
        }

        /// <summary>
        /// Compares simple named objects (Name-based) across replicas. Reports missing-only.
        /// </summary>
        private void CompareSimpleObjects(string agName, string[] replicaNames, string scriptObjectType, string outputObjectType, string displayName)
        {
            Dictionary<string, HashSet<string>> namesByReplica = new Dictionary<string, HashSet<string>>();
            List<string> allNames = new List<string>();
            HashSet<string> nameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string replicaInstance in replicaNames)
            {
                try
                {
                    Collection<PSObject> results = InvokeCommand.InvokeScript(
                        false, _getNamedObjectsScript, null,
                        new object[] { replicaInstance, SqlCredential, SqlCredential != null, scriptObjectType });

                    HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (results != null)
                    {
                        foreach (PSObject obj in results)
                        {
                            if (obj == null) continue;
                            string name = AgReplicaHelpers.GetPropertyString(obj, "Name");
                            if (name == null) continue;
                            names.Add(name);
                            if (!nameSet.Contains(name))
                            {
                                nameSet.Add(name);
                                allNames.Add(name);
                            }
                        }
                    }
                    namesByReplica[replicaInstance] = names;
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("Failed to retrieve {0} from replica {1}", displayName, replicaInstance),
                        errorRecord: new ErrorRecord(ex, String.Format("CompareDbaAgReplicaSync_Get{0}", scriptObjectType), ErrorCategory.ConnectionError, replicaInstance),
                        target: replicaInstance, isContinue: true);
                    TestFunctionInterrupt();
                }
            }

            // Output missing objects
            foreach (string objectName in allNames)
            {
                foreach (string replicaInstance in replicaNames)
                {
                    HashSet<string> replicaNames2;
                    if (!namesByReplica.TryGetValue(replicaInstance, out replicaNames2))
                        continue;

                    if (!replicaNames2.Contains(objectName))
                    {
                        PSObject output = new PSObject();
                        output.Properties.Add(new PSNoteProperty("AvailabilityGroup", agName));
                        output.Properties.Add(new PSNoteProperty("Replica", replicaInstance));
                        output.Properties.Add(new PSNoteProperty("ObjectType", outputObjectType));
                        output.Properties.Add(new PSNoteProperty("ObjectName", objectName));
                        output.Properties.Add(new PSNoteProperty("Status", "Missing"));
                        WriteObject(output);
                    }
                }
            }
        }

        /// <summary>
        /// Compares custom errors (UserDefinedMessages) across replicas using ID-based comparison.
        /// </summary>
        private void CompareCustomErrors(string agName, string[] replicaNames)
        {
            Dictionary<string, HashSet<int>> errorsByReplica = new Dictionary<string, HashSet<int>>();
            List<int> allErrorIds = new List<int>();
            HashSet<int> errorIdSet = new HashSet<int>();

            foreach (string replicaInstance in replicaNames)
            {
                try
                {
                    Collection<PSObject> results = InvokeCommand.InvokeScript(
                        false, _getCustomErrorsScript, null,
                        new object[] { replicaInstance, SqlCredential, SqlCredential != null });

                    HashSet<int> errorIds = new HashSet<int>();
                    if (results != null)
                    {
                        foreach (PSObject obj in results)
                        {
                            if (obj == null) continue;
                            object idVal = AgReplicaHelpers.GetPropertyValue(obj, "ID");
                            if (idVal == null) continue;
                            int errorId;
                            if (idVal is int intVal)
                            {
                                errorId = intVal;
                            }
                            else
                            {
                                if (!Int32.TryParse(idVal.ToString(), out errorId))
                                    continue;
                            }
                            errorIds.Add(errorId);
                            if (!errorIdSet.Contains(errorId))
                            {
                                errorIdSet.Add(errorId);
                                allErrorIds.Add(errorId);
                            }
                        }
                    }
                    errorsByReplica[replicaInstance] = errorIds;
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("Failed to retrieve custom errors from replica {0}", replicaInstance),
                        errorRecord: new ErrorRecord(ex, "CompareDbaAgReplicaSync_GetErrors", ErrorCategory.ConnectionError, replicaInstance),
                        target: replicaInstance, isContinue: true);
                    TestFunctionInterrupt();
                }
            }

            // Output missing errors
            foreach (int errorId in allErrorIds)
            {
                foreach (string replicaInstance in replicaNames)
                {
                    HashSet<int> replicaErrors;
                    if (!errorsByReplica.TryGetValue(replicaInstance, out replicaErrors))
                        continue;

                    if (!replicaErrors.Contains(errorId))
                    {
                        PSObject output = new PSObject();
                        output.Properties.Add(new PSNoteProperty("AvailabilityGroup", agName));
                        output.Properties.Add(new PSNoteProperty("Replica", replicaInstance));
                        output.Properties.Add(new PSNoteProperty("ObjectType", "CustomError"));
                        output.Properties.Add(new PSNoteProperty("ObjectName", String.Format("Error {0}", errorId)));
                        output.Properties.Add(new PSNoteProperty("Status", "Missing"));
                        WriteObject(output);
                    }
                }
            }
        }

        #endregion Comparison Methods

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
                    errorRecord: new ErrorRecord(ex, "CompareDbaAgReplicaSync_Connect", ErrorCategory.ConnectionError, instance),
                    target: instance, isContinue: true);
            }
            TestFunctionInterrupt();
        }

        #endregion Helpers
    }
}
