using System;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Creates new SQL Server Agent job categories for organizing and managing jobs.
    /// Defaults to LocalJob type when CategoryType is not specified.
    /// </summary>
    [Cmdlet("New", "DbaAgentJobCategory", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low)]
    [OutputType("Microsoft.SqlServer.Management.Smo.Agent.JobCategory")]
    public class NewDbaAgentJobCategoryCommand : DbaInstanceCmdlet
    {
        #region Parameters

        /// <summary>
        /// Specifies the name of the SQL Agent job category to create.
        /// Accepts multiple category names when you need to create several categories at once.
        /// </summary>
        [Parameter(Mandatory = true)]
        [ValidateNotNullOrEmpty()]
        public string[] Category { get; set; }

        /// <summary>
        /// Defines the scope and purpose of the job category.
        /// Valid options are LocalJob, MultiServerJob, or None.
        /// Defaults to LocalJob when not specified.
        /// </summary>
        [Parameter()]
        [ValidateSet("LocalJob", "MultiServerJob", "None")]
        public string CategoryType { get; set; }

        /// <summary>
        /// Suppresses confirmation prompts during category creation.
        /// </summary>
        [Parameter()]
        public SwitchParameter Force { get; set; }

        #endregion Parameters

        /// <summary>
        /// Handles Force parameter and defaults CategoryType to LocalJob.
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

            if (!TestBound("CategoryType"))
            {
                WriteMessageAtLevel("Setting the category type to 'LocalJob'", MessageLevel.Verbose, null);
                CategoryType = "LocalJob";
            }
        }

        /// <summary>
        /// Connects to each SQL Server instance and creates the specified job categories.
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
                        errorRecord: new ErrorRecord(ex, "NewDbaAgentJobCategory_ConnectionError", ErrorCategory.ConnectionError, instance),
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
                            String.Format("Job category {0} already exists on {1}", cat, instance),
                            target: instance,
                            isContinue: true);
                        TestFunctionInterrupt();
                        continue;
                    }
                    else
                    {
                        if (ShouldProcess(instance.ToString(), String.Format("Adding the job category {0}", cat)))
                        {
                            try
                            {
                                try
                                {
                                    CreateJobCategory(server, cat, CategoryType);
                                }
                                catch (Exception createEx)
                                {
                                    if (IsContainedAgError(createEx))
                                    {
                                        StopFunction(
                                            "Cannot create agent job category through a contained availability group listener. SQL Server Agent objects are instance-level and must be managed on the instance directly. Please connect to the primary replica instead of the listener. Use Get-DbaAvailabilityGroup to find the current primary replica.",
                                            exception: createEx,
                                            target: cat,
                                            isContinue: true);
                                        TestFunctionInterrupt();
                                        return;
                                    }
                                    throw;
                                }

                                RefreshJobServer(server);
                            }
                            catch (Exception ex)
                            {
                                StopFunction(
                                    String.Format("Something went wrong creating the job category {0} on {1}", cat, instance),
                                    exception: ex,
                                    target: cat,
                                    isContinue: true);
                                TestFunctionInterrupt();
                                continue;
                            }
                        }
                    }

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
        /// Checks if a job category with the given name already exists on the server.
        /// </summary>
        private bool CheckCategoryExists(object server, string categoryName)
        {
            string script = "param($s, $n) $n -in $s.JobServer.JobCategories.Name";
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
        /// Creates a new JobCategory on the server with the specified type, then calls Create().
        /// </summary>
        private void CreateJobCategory(object server, string categoryName, string categoryType)
        {
            string script = @"param($s, $n, $t)
$jobCategory = New-Object Microsoft.SqlServer.Management.Smo.Agent.JobCategory($s.JobServer, $n)
$jobCategory.CategoryType = $t
$jobCategory.Create()";
            InvokeCommand.InvokeScript(
                false, ScriptBlock.Create(script), null, new object[] { server, categoryName, categoryType });
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
        /// Outputs the created category via Get-DbaAgentJobCategory.
        /// </summary>
        private void OutputCategory(object server, string categoryName)
        {
            try
            {
                string script = "param($s, $n) Get-DbaAgentJobCategory -SqlInstance $s -Category $n";
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
                    String.Format("Category '{0}' was created but Get-DbaAgentJobCategory failed: {1}", categoryName, ex.Message),
                    MessageLevel.Verbose,
                    null);
            }
        }

        #endregion Helpers
    }
}
