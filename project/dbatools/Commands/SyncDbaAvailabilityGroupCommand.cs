using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Synchronizes server-level objects from primary to secondary replicas in availability groups.
    /// Copies logins, SQL Agent jobs, linked servers, and other critical server objects to ensure
    /// applications work seamlessly regardless of which replica becomes primary.
    /// </summary>
    [Cmdlet("Sync", "DbaAvailabilityGroup", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Medium)]
    public class SyncDbaAvailabilityGroupCommand : DbaBaseCmdlet
    {
        #region Parameters

        /// <summary>
        /// The primary replica SQL Server instance for the availability group.
        /// </summary>
        [Parameter()]
        public DbaInstanceParameter Primary { get; set; }

        /// <summary>
        /// Login to the primary instance using alternative credentials.
        /// </summary>
        [Parameter()]
        public PSCredential PrimarySqlCredential { get; set; }

        /// <summary>
        /// The secondary replica SQL Server instances.
        /// </summary>
        [Parameter()]
        public DbaInstanceParameter[] Secondary { get; set; }

        /// <summary>
        /// Login to the secondary instances using alternative credentials.
        /// </summary>
        [Parameter()]
        public PSCredential SecondarySqlCredential { get; set; }

        /// <summary>
        /// OS credential for password retrieval via PowerShell remoting.
        /// </summary>
        [Parameter()]
        public PSCredential Credential { get; set; }

        /// <summary>
        /// The name of the specific availability group to synchronize server objects for.
        /// </summary>
        [Parameter()]
        public string AvailabilityGroup { get; set; }

        /// <summary>
        /// Excludes specific object types from being synchronized.
        /// </summary>
        [Parameter()]
        [Alias("ExcludeType")]
        [ValidateSet("AgentCategory", "AgentOperator", "AgentAlert", "AgentProxy", "AgentSchedule", "AgentJob", "Credentials", "CustomErrors", "DatabaseMail", "DatabaseOwner", "LinkedServers", "Logins", "LoginPermissions", "SpConfigure", "SystemTriggers")]
        public string[] Exclude { get; set; }

        /// <summary>
        /// Specifies which login accounts to synchronize.
        /// </summary>
        [Parameter()]
        public string[] Login { get; set; }

        /// <summary>
        /// Specifies login accounts to skip during synchronization.
        /// </summary>
        [Parameter()]
        public string[] ExcludeLogin { get; set; }

        /// <summary>
        /// Specifies which SQL Agent jobs to synchronize.
        /// </summary>
        [Parameter()]
        public string[] Job { get; set; }

        /// <summary>
        /// Specifies SQL Agent jobs to skip during synchronization.
        /// </summary>
        [Parameter()]
        public string[] ExcludeJob { get; set; }

        /// <summary>
        /// Disables all synchronized jobs on secondary replicas after copying.
        /// </summary>
        [Parameter()]
        public SwitchParameter DisableJobOnDestination { get; set; }

        /// <summary>
        /// Accepts availability group objects from Get-DbaAvailabilityGroup for pipeline processing.
        /// Type is object[] because SMO types are loaded dynamically at runtime.
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        public object[] InputObject { get; set; }

        /// <summary>
        /// Copies objects without password values.
        /// </summary>
        [Parameter()]
        public SwitchParameter ExcludePassword { get; set; }

        /// <summary>
        /// Drops and recreates existing objects on secondary replicas.
        /// </summary>
        [Parameter()]
        public SwitchParameter Force { get; set; }

        #endregion Parameters

        #region Static ScriptBlocks

        /// <summary>
        /// Script block for opening a dedicated admin connection.
        /// </summary>
        private static readonly ScriptBlock _connectDacScript = ScriptBlock.Create(@"
param($si, $sc, $hasCred)
$params = @{ SqlInstance = $si; DedicatedAdminConnection = $true; WarningAction = 'SilentlyContinue' }
if ($hasCred) { $params['SqlCredential'] = $sc }
Connect-DbaInstance @params
");

        /// <summary>
        /// Script block for opening a normal connection.
        /// </summary>
        private static readonly ScriptBlock _connectScript = ScriptBlock.Create(@"
param($si, $sc, $hasCred)
$params = @{ SqlInstance = $si }
if ($hasCred) { $params['SqlCredential'] = $sc }
Connect-DbaInstance @params
");

        /// <summary>
        /// Script block to get availability group and discover secondaries.
        /// </summary>
        private static readonly ScriptBlock _getAgScript = ScriptBlock.Create(@"
param($server, $agName)
Get-DbaAvailabilityGroup -SqlInstance $server -AvailabilityGroup $agName
");

        /// <summary>
        /// Script block to discover secondary names from AG objects.
        /// </summary>
        private static readonly ScriptBlock _getSecondariesScript = ScriptBlock.Create(@"
param($agObjects, $primaryDomainInstanceName)
($agObjects.AvailabilityReplicas | Where-Object Name -ne $primaryDomainInstanceName).Name | Select-Object -Unique
");

        /// <summary>
        /// Master sync script that delegates to all Copy-Dba*/Sync-Dba* commands.
        /// </summary>
        private static readonly ScriptBlock _syncScript = ScriptBlock.Create(@"
param($server, $secondaries, $exclude, $login, $excludeLogin, $job, $excludeJob,
      $disableJobOnDest, $credential, $excludePassword, $force,
      $hasCred, $hasLogin, $hasExcludeLogin, $hasJob, $hasExcludeJob,
      $primaryName, $secondaryNames)

$stepCounter = 0
$totalSteps = 15
$activity = ""Syncing availability group""

if ($exclude -notcontains 'SpConfigure') {
    Write-Progress -Activity $activity -Status 'Syncing SQL Server Configuration' -PercentComplete (($stepCounter / $totalSteps) * 100)
    $stepCounter++
    Copy-DbaSpConfigure -Source $server -Destination $secondaries
}

if ($exclude -notcontains 'Logins') {
    Write-Progress -Activity $activity -Status 'Syncing logins' -PercentComplete (($stepCounter / $totalSteps) * 100)
    $stepCounter++
    $loginParams = @{ Source = $server; Destination = $secondaries; Force = $force }
    if ($hasLogin) { $loginParams['Login'] = $login }
    if ($hasExcludeLogin) { $loginParams['ExcludeLogin'] = $excludeLogin }
    Copy-DbaLogin @loginParams
}

if ($exclude -notcontains 'DatabaseOwner') {
    Write-Progress -Activity $activity -Status 'Updating database owners' -PercentComplete (($stepCounter / $totalSteps) * 100)
    $stepCounter++
    foreach ($sec in $secondaries) {
        $null = Update-SqlDbOwner -Source $server -Destination $sec
    }
}

if ($exclude -notcontains 'CustomErrors') {
    Write-Progress -Activity $activity -Status 'Syncing custom errors' -PercentComplete (($stepCounter / $totalSteps) * 100)
    $stepCounter++
    Copy-DbaCustomError -Source $server -Destination $secondaries -Force:$force
}

if ($exclude -notcontains 'Credentials') {
    Write-Progress -Activity $activity -Status 'Syncing SQL credentials' -PercentComplete (($stepCounter / $totalSteps) * 100)
    $stepCounter++
    $credParams = @{ Source = $server; Destination = $secondaries; Force = $force; ExcludePassword = $excludePassword }
    if ($hasCred) { $credParams['Credential'] = $credential }
    Copy-DbaCredential @credParams
}

if ($exclude -notcontains 'DatabaseMail') {
    Write-Progress -Activity $activity -Status 'Syncing database mail' -PercentComplete (($stepCounter / $totalSteps) * 100)
    $stepCounter++
    $mailParams = @{ Source = $server; Destination = $secondaries; Force = $force; ExcludePassword = $excludePassword }
    if ($hasCred) { $mailParams['Credential'] = $credential }
    Copy-DbaDbMail @mailParams
}

if ($exclude -notcontains 'LinkedServers') {
    Write-Progress -Activity $activity -Status 'Syncing linked servers' -PercentComplete (($stepCounter / $totalSteps) * 100)
    $stepCounter++
    $lsParams = @{ Source = $server; Destination = $secondaries; Force = $force; ExcludePassword = $excludePassword }
    if ($hasCred) { $lsParams['Credential'] = $credential }
    Copy-DbaLinkedServer @lsParams
}

if ($exclude -notcontains 'SystemTriggers') {
    Write-Progress -Activity $activity -Status 'Syncing System Triggers' -PercentComplete (($stepCounter / $totalSteps) * 100)
    $stepCounter++
    Copy-DbaInstanceTrigger -Source $server -Destination $secondaries -Force:$force
}

if ($exclude -notcontains 'AgentCategory') {
    Write-Progress -Activity $activity -Status 'Syncing Agent Categories' -PercentComplete (($stepCounter / $totalSteps) * 100)
    $stepCounter++
    Copy-DbaAgentJobCategory -Source $server -Destination $secondaries -Force:$force
    foreach ($sec in $secondaries) {
        if ($sec.JobServer) {
            $sec.JobServer.JobCategories.Refresh()
            $sec.JobServer.OperatorCategories.Refresh()
            $sec.JobServer.AlertCategories.Refresh()
        }
    }
}

if ($exclude -notcontains 'AgentOperator') {
    Write-Progress -Activity $activity -Status 'Syncing Agent Operators' -PercentComplete (($stepCounter / $totalSteps) * 100)
    $stepCounter++
    Copy-DbaAgentOperator -Source $server -Destination $secondaries -Force:$force
    foreach ($sec in $secondaries) {
        if ($sec.JobServer) {
            $sec.JobServer.Operators.Refresh()
        }
    }
}

if ($exclude -notcontains 'AgentAlert') {
    Write-Progress -Activity $activity -Status 'Syncing Agent Alerts' -PercentComplete (($stepCounter / $totalSteps) * 100)
    $stepCounter++
    Copy-DbaAgentAlert -Source $server -Destination $secondaries -Force:$force -IncludeDefaults
}

if ($exclude -notcontains 'AgentProxy') {
    Write-Progress -Activity $activity -Status 'Syncing Agent Proxy Accounts' -PercentComplete (($stepCounter / $totalSteps) * 100)
    $stepCounter++
    Copy-DbaAgentProxy -Source $server -Destination $secondaries -Force:$force
    foreach ($sec in $secondaries) {
        if ($sec.JobServer) {
            $sec.JobServer.ProxyAccounts.Refresh()
        }
    }
}

if ($exclude -notcontains 'AgentSchedule') {
    Write-Progress -Activity $activity -Status 'Syncing Agent Schedules' -PercentComplete (($stepCounter / $totalSteps) * 100)
    $stepCounter++
    Copy-DbaAgentSchedule -Source $server -Destination $secondaries -Force:$force
    foreach ($sec in $secondaries) {
        if ($sec.JobServer) {
            $sec.JobServer.SharedSchedules.Refresh()
            $sec.JobServer.Refresh()
        }
        $sec.Refresh()
    }
}

if ($exclude -notcontains 'AgentJob') {
    Write-Progress -Activity $activity -Status 'Syncing Agent Jobs' -PercentComplete (($stepCounter / $totalSteps) * 100)
    $stepCounter++
    $splatGetJob = @{ SqlInstance = $server }
    if ($hasJob) { $splatGetJob['Job'] = $job }
    if ($hasExcludeJob) { $splatGetJob['ExcludeJob'] = $excludeJob }
    $jobsToSync = Get-DbaAgentJob @splatGetJob

    $splatCopyJob = @{
        Destination          = $secondaries
        Force                = $force
        DisableOnDestination = $disableJobOnDest
        InputObject          = $jobsToSync
    }
    Copy-DbaAgentJob @splatCopyJob
}

if ($exclude -notcontains 'LoginPermissions') {
    Write-Progress -Activity $activity -Status 'Syncing login permissions' -PercentComplete (($stepCounter / $totalSteps) * 100)
    $stepCounter++
    $permParams = @{ Source = $server; Destination = $secondaries }
    if ($hasLogin) { $permParams['Login'] = $login }
    if ($hasExcludeLogin) { $permParams['ExcludeLogin'] = $excludeLogin }
    Sync-DbaLoginPermission @permParams
}

Write-Progress -Activity $activity -Completed
");

        /// <summary>
        /// Script block to disconnect a DAC connection.
        /// </summary>
        private static readonly ScriptBlock _disconnectScript = ScriptBlock.Create(@"
param($server)
$server | Disconnect-DbaInstance -WhatIf:$false
");

        #endregion Static ScriptBlocks

        /// <summary>
        /// Tracks all primary/secondary combos to avoid duplicate syncs.
        /// </summary>
        private List<SyncCombo> _allCombos = new List<SyncCombo>();

        /// <summary>
        /// Whether a DAC was opened by this cmdlet (needs cleanup).
        /// </summary>
        private bool _dacOpened;

        /// <summary>
        /// The server object for DAC disconnect in EndProcessing.
        /// </summary>
        private object _dacServer;

        /// <summary>
        /// Sets up Force/ConfirmPreference override.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            if (Force.IsPresent)
            {
                SessionState.PSVariable.Set("ConfirmPreference", "None");
            }
        }

        /// <summary>
        /// Collects primary/secondary combos, deduplicating as pipeline items arrive.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (TestFunctionInterrupt())
                return;

            // Validate: either Primary or InputObject must be provided
            if (TestBoundNot("Primary", "InputObject") && InputObject == null)
            {
                StopFunction("You must supply either -Primary or an Input Object");
                return;
            }

            // Validate: need Secondary or AvailabilityGroup or InputObject
            if (!TestBound("AvailabilityGroup") && !TestBound("Secondary") && InputObject == null)
            {
                StopFunction("You must specify a secondary or an availability group.");
                return;
            }

            object server = null;
            List<object> inputObjects = new List<object>();

            // Handle InputObject from pipeline
            if (InputObject != null)
            {
                foreach (object obj in InputObject)
                {
                    if (obj != null)
                        inputObjects.Add(obj);
                }

                if (inputObjects.Count > 0)
                {
                    // Check DAC requirement
                    bool dacNeeded = !ExcludePassword.IsPresent;
                    if (dacNeeded)
                    {
                        // Check if pipeline source already has DAC
                        PSObject firstObj = PSObject.AsPSObject(inputObjects[0]);
                        string parentName = GetParentName(firstObj);
                        bool dacConnected = parentName != null && parentName.StartsWith("ADMIN:", StringComparison.OrdinalIgnoreCase);

                        if (!dacConnected)
                        {
                            StopFunction("Pipeline source must use a dedicated admin connection to retrieve passwords. Use -ExcludePassword to bypass this requirement if you don't need passwords.");
                            return;
                        }
                    }

                    WriteMessageVerbose("Reusing dedicated admin connection for password retrieval.");
                    server = GetParentObject(inputObjects[0]);
                }
            }
            else
            {
                // Connect to primary
                try
                {
                    if (ExcludePassword.IsPresent)
                    {
                        WriteMessageVerbose("Opening normal connection because we don't need the passwords.");
                        Collection<PSObject> connResults = InvokeCommand.InvokeScript(
                            false, _connectScript, null,
                            new object[] { Primary, PrimarySqlCredential, PrimarySqlCredential != null });

                        if (connResults != null && connResults.Count > 0)
                            server = connResults[0];
                    }
                    else
                    {
                        WriteMessageVerbose("Opening dedicated admin connection for password retrieval.");
                        Collection<PSObject> connResults = InvokeCommand.InvokeScript(
                            false, _connectDacScript, null,
                            new object[] { Primary, PrimarySqlCredential, PrimarySqlCredential != null });

                        if (connResults != null && connResults.Count > 0)
                        {
                            server = connResults[0];
                            _dacOpened = true;
                            _dacServer = server;
                        }
                    }
                }
                catch (Exception ex)
                {
                    StopFunction(
                        "Failure",
                        errorRecord: new ErrorRecord(ex, "SyncDbaAvailabilityGroup_Connect", ErrorCategory.ConnectionError, Primary),
                        target: Primary);
                    return;
                }
            }

            if (server == null)
                return;

            // Get AG objects if AvailabilityGroup is specified
            if (TestBound("AvailabilityGroup") && !String.IsNullOrEmpty(AvailabilityGroup))
            {
                try
                {
                    Collection<PSObject> agResults = InvokeCommand.InvokeScript(
                        false, _getAgScript, null,
                        new object[] { server, AvailabilityGroup });

                    if (agResults != null)
                    {
                        foreach (PSObject ag in agResults)
                        {
                            if (ag != null)
                                inputObjects.Add(ag);
                        }
                    }
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("Failed to get availability group {0}", AvailabilityGroup),
                        exception: ex, target: AvailabilityGroup, isContinue: true);
                    TestFunctionInterrupt();
                }
            }

            // Discover secondaries from InputObject AG objects
            List<string> secondaryNames = new List<string>();
            if (Secondary != null)
            {
                foreach (DbaInstanceParameter sec in Secondary)
                {
                    secondaryNames.Add(sec.ToString());
                }
            }

            if (inputObjects.Count > 0)
            {
                try
                {
                    PSObject serverPs = PSObject.AsPSObject(server);
                    string domainInstanceName = GetPropertyString(serverPs, "DomainInstanceName");

                    Collection<PSObject> secResults = InvokeCommand.InvokeScript(
                        false, _getSecondariesScript, null,
                        new object[] { inputObjects.ToArray(), domainInstanceName });

                    if (secResults != null)
                    {
                        foreach (PSObject secResult in secResults)
                        {
                            if (secResult != null && secResult.BaseObject != null)
                            {
                                string secName = secResult.BaseObject.ToString();
                                if (!String.IsNullOrEmpty(secName))
                                    secondaryNames.Add(secName);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    StopFunction(
                        "Failed to discover secondaries",
                        exception: ex, isContinue: true);
                    TestFunctionInterrupt();
                }
            }

            // Connect to secondaries
            List<object> secondaries = new List<object>();
            if (secondaryNames.Count > 0)
            {
                secondaryNames.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string secName in secondaryNames)
                {
                    try
                    {
                        Collection<PSObject> secConnResults = InvokeCommand.InvokeScript(
                            false, _connectScript, null,
                            new object[] { secName, SecondarySqlCredential, SecondarySqlCredential != null });

                        if (secConnResults != null && secConnResults.Count > 0)
                            secondaries.Add(secConnResults[0]);
                    }
                    catch (Exception ex)
                    {
                        StopFunction(
                            "Failure",
                            errorRecord: new ErrorRecord(ex, "SyncDbaAvailabilityGroup_ConnectSec", ErrorCategory.ConnectionError, secName),
                            target: secName, isContinue: true);
                        TestFunctionInterrupt();
                    }
                }
            }

            // Check for duplicate combo
            string primaryName = GetPropertyString(PSObject.AsPSObject(server), "Name") ?? "";
            string secondaryKey = BuildSecondaryKey(secondaries);

            bool isDuplicate = false;
            foreach (SyncCombo existing in _allCombos)
            {
                if (String.Equals(existing.PrimaryName, primaryName, StringComparison.OrdinalIgnoreCase) &&
                    String.Equals(existing.SecondaryKey, secondaryKey, StringComparison.OrdinalIgnoreCase))
                {
                    isDuplicate = true;
                    break;
                }
            }

            if (!isDuplicate)
            {
                _allCombos.Add(new SyncCombo
                {
                    PrimaryName = primaryName,
                    SecondaryKey = secondaryKey,
                    Server = server,
                    Secondaries = secondaries.ToArray()
                });
            }
        }

        /// <summary>
        /// Executes the sync for all collected primary/secondary combos.
        /// </summary>
        protected override void EndProcessing()
        {
            if (TestFunctionInterrupt())
                return;

            foreach (SyncCombo combo in _allCombos)
            {
                if (combo.Secondaries == null || combo.Secondaries.Length == 0)
                {
                    StopFunction("No secondaries found.");
                    return;
                }

                try
                {
                    InvokeCommand.InvokeScript(
                        false, _syncScript, null,
                        new object[]
                        {
                            combo.Server,
                            combo.Secondaries,
                            Exclude,
                            Login,
                            ExcludeLogin,
                            Job,
                            ExcludeJob,
                            DisableJobOnDestination.IsPresent,
                            Credential,
                            ExcludePassword.IsPresent,
                            Force.IsPresent,
                            Credential != null,
                            TestBound("Login"),
                            TestBound("ExcludeLogin"),
                            TestBound("Job"),
                            TestBound("ExcludeJob")
                        });
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("Failure syncing availability group from {0}", combo.PrimaryName),
                        exception: ex, target: combo.Server, isContinue: true);
                    TestFunctionInterrupt();
                }
            }

            // Disconnect DAC if we opened it
            if (_dacOpened && _dacServer != null)
            {
                try
                {
                    InvokeCommand.InvokeScript(
                        false, _disconnectScript, null,
                        new object[] { _dacServer });
                }
                catch (Exception)
                {
                    // Best effort DAC disconnect
                }
            }
        }

        #region Helpers

        /// <summary>
        /// Tracks a primary/secondary combination for sync deduplication.
        /// </summary>
        private class SyncCombo
        {
            public string PrimaryName;
            public string SecondaryKey;
            public object Server;
            public object[] Secondaries;
        }

        /// <summary>
        /// Builds a key from secondary server names for deduplication.
        /// </summary>
        private static string BuildSecondaryKey(List<object> secondaries)
        {
            List<string> names = new List<string>();
            foreach (object sec in secondaries)
            {
                PSObject ps = PSObject.AsPSObject(sec);
                string name = GetPropertyString(ps, "Name");
                if (name != null)
                    names.Add(name);
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return String.Join(",", names.ToArray());
        }

        /// <summary>
        /// Gets the Parent.Name from an AG object.
        /// </summary>
        private static string GetParentName(PSObject obj)
        {
            try
            {
                PSPropertyInfo parentProp = obj.Properties["Parent"];
                if (parentProp != null && parentProp.Value != null)
                {
                    PSObject parent = PSObject.AsPSObject(parentProp.Value);
                    return GetPropertyString(parent, "Name");
                }
            }
            catch (Exception)
            {
                // Best effort
            }
            return null;
        }

        /// <summary>
        /// Gets the Parent object from an AG object.
        /// </summary>
        private static object GetParentObject(object obj)
        {
            try
            {
                PSObject psObj = PSObject.AsPSObject(obj);
                PSPropertyInfo parentProp = psObj.Properties["Parent"];
                if (parentProp != null && parentProp.Value != null)
                    return parentProp.Value;
            }
            catch (Exception)
            {
                // Best effort
            }
            return null;
        }

        #endregion Helpers
    }
}
