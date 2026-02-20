using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Modifies properties of existing SQL Agent alerts including enabled status and name.
    /// Supports modifying alerts by name via SqlInstance or by pipeline input from Get-DbaAgentAlert.
    /// </summary>
    [Cmdlet("Set", "DbaAgentAlert", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low)]
    [OutputType("Microsoft.SqlServer.Management.Smo.Agent.Alert")]
    public class SetDbaAgentAlertCommand : DbaBaseCmdlet
    {
        #region Parameters

        /// <summary>
        /// The target SQL Server instance or instances.
        /// </summary>
        [Parameter()]
        public DbaInstanceParameter[] SqlInstance { get; set; }

        /// <summary>
        /// Login to the target instance using alternative credentials.
        /// </summary>
        [Parameter()]
        [Alias("Credential", "Cred")]
        public PSCredential SqlCredential { get; set; }

        /// <summary>
        /// Specifies the name(s) of the SQL Agent alerts to modify.
        /// </summary>
        [Parameter()]
        public object[] Alert { get; set; }

        /// <summary>
        /// Sets a new name for the alert being modified.
        /// </summary>
        [Parameter()]
        public string NewName { get; set; }

        /// <summary>
        /// Enables the specified SQL Agent alert(s).
        /// </summary>
        [Parameter()]
        public SwitchParameter Enabled { get; set; }

        /// <summary>
        /// Disables the specified SQL Agent alert(s).
        /// </summary>
        [Parameter()]
        public SwitchParameter Disabled { get; set; }

        /// <summary>
        /// Bypasses confirmation prompts.
        /// </summary>
        [Parameter()]
        public SwitchParameter Force { get; set; }

        /// <summary>
        /// Accepts SQL Agent alert objects from the pipeline, typically from Get-DbaAgentAlert.
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        public PSObject[] InputObject { get; set; }

        #endregion Parameters

        /// <summary>
        /// Collected alert objects from pipeline and instance resolution.
        /// </summary>
        private List<PSObject> _collectedAlerts = new List<PSObject>();

        /// <summary>
        /// Handles Force parameter by setting ConfirmPreference to None.
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
        /// Collects alert objects from pipeline input and resolves alert names from instances.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (TestFunctionInterrupt()) return;

            if ((InputObject == null || InputObject.Length == 0) && Alert == null)
            {
                StopFunction(
                    "You must specify an alert name or pipe in results from another command",
                    target: SqlInstance);
                return;
            }

            // Collect pipeline InputObject items
            if (InputObject != null)
            {
                foreach (PSObject obj in InputObject)
                {
                    if (obj != null)
                        _collectedAlerts.Add(obj);
                }
            }

            // Resolve alerts from SqlInstance + Alert names
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
                            errorRecord: new ErrorRecord(ex, "SetDbaAgentAlert_ConnectionError", ErrorCategory.ConnectionError, instance),
                            target: instance,
                            isContinue: true,
                            category: ErrorCategory.ConnectionError);
                        TestFunctionInterrupt();
                        continue;
                    }

                    if (Alert != null)
                    {
                        foreach (object a in Alert)
                        {
                            string alertName = (a != null) ? a.ToString() : null;
                            if (String.IsNullOrEmpty(alertName))
                                continue;

                            bool exists = CheckAlertExists(server, alertName);
                            if (!exists)
                            {
                                StopFunction(
                                    String.Format("Alert {0} doesn't exists on {1}", alertName, instance),
                                    target: instance);
                            }
                            else
                            {
                                try
                                {
                                    PSObject alertObj = GetAlertByName(server, alertName);
                                    if (alertObj != null)
                                    {
                                        RefreshAlert(alertObj);
                                        _collectedAlerts.Add(alertObj);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    StopFunction(
                                        "Something went wrong retrieving the alert",
                                        exception: ex,
                                        target: a,
                                        isContinue: true);
                                    TestFunctionInterrupt();
                                    continue;
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Processes all collected alerts: applies modifications and outputs results.
        /// </summary>
        protected override void EndProcessing()
        {
            foreach (PSObject currentAlert in _collectedAlerts)
            {
                if (TestFunctionInterrupt()) return;

                // Get server from alert's parent chain (Alert.Parent = JobServer, Parent.Parent = Server)
                object server = GetAlertServer(currentAlert);
                string alertName = GetAlertName(currentAlert);
                string serverName = (server != null) ? server.ToString() : "unknown";

                // Guard against invalid InputObject (e.g. wrong type piped in)
                if (server == null && alertName == null)
                {
                    StopFunction(
                        "Could not determine server from alert object. Ensure InputObject comes from Get-DbaAgentAlert.",
                        target: currentAlert,
                        isContinue: true);
                    TestFunctionInterrupt();
                    continue;
                }

                // Apply NewName via Rename (immediate server operation)
                if (!String.IsNullOrEmpty(NewName))
                {
                    if (ShouldProcess(serverName, String.Format("Setting alert name to {0} for {1}", NewName, alertName)))
                    {
                        try
                        {
                            RenameAlert(currentAlert, NewName);
                        }
                        catch (Exception ex)
                        {
                            StopFunction(
                                String.Format("Something went wrong renaming the alert {0}", alertName),
                                exception: ex,
                                target: currentAlert,
                                isContinue: true);
                            TestFunctionInterrupt();
                            continue;
                        }
                    }
                }

                // Apply Enabled (in-memory property change)
                if (Enabled.IsPresent)
                {
                    WriteMessageAtLevel("Setting alert to enabled", MessageLevel.Verbose, null);
                    SetAlertEnabled(currentAlert, true);
                }

                // Apply Disabled (in-memory property change)
                if (Disabled.IsPresent)
                {
                    WriteMessageAtLevel("Setting alert to disabled", MessageLevel.Verbose, null);
                    SetAlertEnabled(currentAlert, false);
                }

                // Commit changes via Alter()
                string currentName = GetAlertName(currentAlert);
                if (ShouldProcess(serverName, String.Format("Committing changes for alert {0}", currentName)))
                {
                    try
                    {
                        WriteMessageAtLevel(
                            String.Format("Committing changes for alert {0}", currentName),
                            MessageLevel.Verbose, null);

                        AlterAlert(currentAlert);
                    }
                    catch (Exception ex)
                    {
                        StopFunction(
                            "Something went wrong changing the alert",
                            exception: ex,
                            target: currentAlert,
                            isContinue: true);
                        TestFunctionInterrupt();
                        continue;
                    }

                    // Output via Get-DbaAgentAlert for consistent formatting
                    OutputAlert(server, currentName);
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

            Collection<PSObject> results = InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, args);
            if (results != null && results.Count > 0)
                return results[0].BaseObject;
            return null;
        }

        /// <summary>
        /// Checks if an alert with the given name exists on the server.
        /// </summary>
        private bool CheckAlertExists(object server, string alertName)
        {
            string script = "param($s, $n) $n -in $s.JobServer.Alerts.Name";
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, ScriptBlock.Create(script), null, new object[] { server, alertName });
            if (results != null && results.Count > 0 && results[0] != null)
            {
                object val = results[0].BaseObject;
                if (val is bool boolVal)
                    return boolVal;
            }
            return false;
        }

        /// <summary>
        /// Gets an alert SMO object by name from the server's JobServer.Alerts collection.
        /// </summary>
        private PSObject GetAlertByName(object server, string alertName)
        {
            string script = "param($s, $n) $s.JobServer.Alerts[$n]";
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, ScriptBlock.Create(script), null, new object[] { server, alertName });
            if (results != null && results.Count > 0)
                return results[0];
            return null;
        }

        /// <summary>
        /// Refreshes an alert SMO object.
        /// </summary>
        private void RefreshAlert(PSObject alert)
        {
            string script = "param($a) $a.Refresh()";
            InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { alert });
        }

        /// <summary>
        /// Gets the server object from an alert's parent chain (Alert.Parent.Parent = Server).
        /// </summary>
        private object GetAlertServer(PSObject alert)
        {
            try
            {
                string script = "param($a) $a.Parent.Parent";
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, ScriptBlock.Create(script), null, new object[] { alert });
                if (results != null && results.Count > 0 && results[0] != null)
                    return results[0].BaseObject;
            }
            catch (Exception)
            {
                // May fail if alert object is not properly initialized
            }
            return null;
        }

        /// <summary>
        /// Gets the Name property from an alert PSObject.
        /// </summary>
        internal static string GetAlertName(PSObject alert)
        {
            if (alert == null)
                return null;
            try
            {
                PSPropertyInfo prop = alert.Properties["Name"];
                if (prop != null && prop.Value != null)
                    return prop.Value.ToString();
            }
            catch (Exception)
            {
                // Ignore
            }
            return null;
        }

        /// <summary>
        /// Renames an alert using the SMO Rename() method.
        /// </summary>
        private void RenameAlert(PSObject alert, string newName)
        {
            string script = "param($a, $n) $a.Rename($n)";
            InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { alert, newName });
        }

        /// <summary>
        /// Sets the IsEnabled property on an alert.
        /// </summary>
        private void SetAlertEnabled(PSObject alert, bool enabled)
        {
            string script = "param($a, $v) $a.IsEnabled = $v";
            InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { alert, enabled });
        }

        /// <summary>
        /// Calls Alter() on the alert to commit changes.
        /// </summary>
        private void AlterAlert(PSObject alert)
        {
            string script = "param($a) $a.Alter()";
            InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { alert });
        }

        /// <summary>
        /// Outputs the modified alert via Get-DbaAgentAlert for consistent formatting.
        /// </summary>
        private void OutputAlert(object server, string alertName)
        {
            try
            {
                string script = "param($s, $n) Get-DbaAgentAlert -SqlInstance $s -Alert $n";
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, ScriptBlock.Create(script), null, new object[] { server, alertName });
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
                // If Get-DbaAgentAlert fails, don't error - the alert was still modified
            }
        }

        #endregion Helpers
    }
}
