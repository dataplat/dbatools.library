using System;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Creates new SQL Agent alert categories for organizing and managing database alerts.
    /// Alert categories allow DBAs to logically group alerts by function, severity, or
    /// responsibility for better management, reporting, and maintenance workflows.
    /// </summary>
    [Cmdlet("New", "DbaAgentAlertCategory", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low)]
    [OutputType("Microsoft.SqlServer.Management.Smo.Agent.AlertCategory")]
    public class NewDbaAgentAlertCategoryCommand : DbaInstanceCmdlet
    {
        #region Parameters

        /// <summary>
        /// Specifies the name or names of the alert categories to create in SQL Server Agent.
        /// Multiple categories can be created in a single operation by providing an array of category names.
        /// </summary>
        [Parameter(Mandatory = true)]
        [ValidateNotNullOrEmpty()]
        public string[] Category { get; set; }

        /// <summary>
        /// Bypasses confirmation prompts and creates the alert categories without user interaction.
        /// </summary>
        [Parameter()]
        public SwitchParameter Force { get; set; }

        #endregion Parameters

        /// <summary>
        /// Handles Force parameter by overriding ConfirmPreference.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            if (Force.IsPresent)
            {
                try
                {
                    string script = "$ConfirmPreference = 'None'";
                    InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, null);
                }
                catch (Exception)
                {
                    // Best effort
                }
            }
        }

        /// <summary>
        /// Connects to each SQL Server instance and creates the specified alert categories.
        /// </summary>
        protected override void ProcessRecord()
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
                        errorRecord: new ErrorRecord(ex, "NewDbaAgentAlertCategory_ConnectionError", ErrorCategory.ConnectionError, instance),
                        target: instance,
                        isContinue: true,
                        category: ErrorCategory.ConnectionError);
                    TestFunctionInterrupt();
                    continue;
                }

                foreach (string cat in Category)
                {
                    if (CheckCategoryExists(server, cat))
                    {
                        StopFunction(
                            String.Format("Alert category {0} already exists on {1}", cat, instance),
                            target: instance,
                            isContinue: true);
                        TestFunctionInterrupt();
                    }
                    else
                    {
                        if (ShouldProcess(instance.ToString(), String.Format("Adding the alert category {0}", cat)))
                        {
                            try
                            {
                                try
                                {
                                    CreateAlertCategory(server, cat);
                                }
                                catch (Exception createEx)
                                {
                                    if (IsContainedAgError(createEx))
                                    {
                                        StopFunction(
                                            "Cannot create agent alert category through a contained availability group listener. SQL Server Agent objects are instance-level and must be managed on the instance directly. Please connect to the primary replica instead of the listener. Use Get-DbaAvailabilityGroup to find the current primary replica.",
                                            exception: createEx,
                                            target: cat,
                                            isContinue: true);
                                        TestFunctionInterrupt();
                                        // PS1 uses 'return' here which exits the entire process block,
                                        // skipping remaining categories for this instance
                                        return;
                                    }
                                    throw;
                                }

                                RefreshJobServer(server);
                            }
                            catch (Exception ex)
                            {
                                StopFunction(
                                    String.Format("Something went wrong creating the alert category {0} on {1}", cat, instance),
                                    exception: ex,
                                    target: cat,
                                    isContinue: true);
                                TestFunctionInterrupt();
                                continue;
                            }
                        }
                    }

                    // Output the category via Get-DbaAgentAlertCategory
                    OutputCategory(server, cat);
                }
            }
        }

        #region Helpers

        /// <summary>
        /// Checks if an exception is a contained availability group listener error.
        /// </summary>
        internal static bool IsContainedAgError(Exception ex)
        {
            if (ex == null)
                return false;

            // Walk the exception chain looking for "newParent"
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

            Collection<PSObject> results = InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, args);
            if (results != null && results.Count > 0)
                return results[0].BaseObject;
            return null;
        }

        /// <summary>
        /// Checks if an alert category with the given name already exists on the server.
        /// </summary>
        private bool CheckCategoryExists(object server, string categoryName)
        {
            string script = "param($s, $n) $n -in $s.JobServer.AlertCategories.Name";
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, ScriptBlock.Create(script), null, new object[] { server, categoryName });
            if (results != null && results.Count > 0 && results[0] != null)
            {
                object val = results[0].BaseObject;
                if (val is bool boolVal)
                    return boolVal;
            }
            return false;
        }

        /// <summary>
        /// Creates a new AlertCategory object on the server and calls Create().
        /// </summary>
        private void CreateAlertCategory(object server, string categoryName)
        {
            string script = @"param($s, $n)
$alertCategory = New-Object Microsoft.SqlServer.Management.Smo.Agent.AlertCategory($s.JobServer, $n)
$alertCategory.Create()";
            InvokeCommand.InvokeScript(
                false, ScriptBlock.Create(script), null, new object[] { server, categoryName });
        }

        /// <summary>
        /// Refreshes the JobServer to pick up changes.
        /// </summary>
        private void RefreshJobServer(object server)
        {
            try
            {
                string script = "param($s) $null = $s.JobServer.Refresh()";
                InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { server });
            }
            catch (Exception)
            {
                // Best effort refresh
            }
        }

        /// <summary>
        /// Outputs the created category via Get-DbaAgentAlertCategory.
        /// </summary>
        private void OutputCategory(object server, string categoryName)
        {
            try
            {
                string script = "param($s, $n) Get-DbaAgentAlertCategory -SqlInstance $s -Category $n";
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, ScriptBlock.Create(script), null, new object[] { server, categoryName });
                if (results != null)
                {
                    foreach (PSObject result in results)
                    {
                        WriteObject(result);
                    }
                }
            }
            catch (Exception ex)
            {
                WriteMessageAtLevel(
                    String.Format("Category '{0}' was created but Get-DbaAgentAlertCategory failed: {1}", categoryName, ex.Message),
                    MessageLevel.Verbose,
                    null);
            }
        }

        #endregion Helpers
    }
}
