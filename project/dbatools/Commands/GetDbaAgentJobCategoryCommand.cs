using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Retrieves SQL Server Agent job categories with usage counts and filtering options.
    /// Returns job category objects with properties including name, ID, category type,
    /// and a computed JobCount showing how many jobs are assigned to each category.
    /// </summary>
    [Cmdlet("Get", "DbaAgentJobCategory")]
    public class GetDbaAgentJobCategoryCommand : DbaInstanceCmdlet
    {
        #region Parameters

        /// <summary>
        /// Specifies one or more job category names to return.
        /// Filters the results to only those categories matching the specified names.
        /// </summary>
        [Parameter()]
        [ValidateNotNullOrEmpty()]
        public string[] Category { get; set; }

        /// <summary>
        /// Filters job categories by their deployment type: LocalJob, MultiServerJob, or None.
        /// </summary>
        [Parameter()]
        [ValidateSet("LocalJob", "MultiServerJob", "None")]
        public string CategoryType { get; set; }

        #endregion Parameters

        /// <summary>
        /// Default display properties for the output objects.
        /// </summary>
        private static readonly string[] DefaultDisplayProperties = new string[]
        {
            "ComputerName", "InstanceName", "SqlInstance", "Name", "ID", "CategoryType", "JobCount"
        };

        /// <summary>
        /// Connects to each SQL Server instance and retrieves SQL Agent job categories,
        /// applying name and type filters and adding custom properties including job counts.
        /// </summary>
        protected override void ProcessRecord()
        {
            // Build category name lookup once before the instance loop
            HashSet<string> categoryLookup = null;
            if (Category != null && Category.Length > 0)
            {
                categoryLookup = new HashSet<string>(Category, StringComparer.OrdinalIgnoreCase);
            }

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
                        errorRecord: new ErrorRecord(ex, "GetDbaAgentJobCategory_ConnectionError", ErrorCategory.ConnectionError, instance),
                        target: instance,
                        isContinue: true,
                        category: ErrorCategory.ConnectionError);
                    TestFunctionInterrupt();
                    continue;
                }

                // Get job categories from JobServer
                Collection<PSObject> jobCategories;
                try
                {
                    jobCategories = GetJobCategories(server);
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("Failed to retrieve job categories from {0}", instance),
                        exception: ex,
                        target: instance,
                        isContinue: true);
                    TestFunctionInterrupt();
                    continue;
                }

                if (jobCategories == null || jobCategories.Count == 0)
                    continue;

                // Get jobs collection for counting
                Collection<PSObject> jobs;
                try
                {
                    jobs = GetJobs(server);
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("Something went wrong getting the job categories on {0}", instance),
                        exception: ex,
                        target: instance,
                        isContinue: true);
                    TestFunctionInterrupt();
                    continue;
                }

                // Build a lookup of CategoryID -> job count
                Dictionary<int, int> jobCountByCategory = BuildJobCountLookup(jobs);

                // Get server connection info for custom properties
                string computerName = GetServerPropertySafe(server, "ComputerName");
                string serviceName = GetServerPropertySafe(server, "ServiceName");
                string domainInstanceName = GetServerPropertySafe(server, "DomainInstanceName");

                foreach (PSObject catObj in jobCategories)
                {
                    if (catObj == null)
                        continue;

                    string catName = GetPSPropertyString(catObj, "Name");
                    string catType = GetPSPropertyString(catObj, "CategoryType");

                    // Apply Category name filter (case-insensitive exact match, matching PS1 -in)
                    if (categoryLookup != null && (catName == null || !categoryLookup.Contains(catName)))
                        continue;

                    // Apply CategoryType filter (case-insensitive exact match, matching PS1 -in)
                    if (CategoryType != null && !String.Equals(catType, CategoryType, StringComparison.OrdinalIgnoreCase))
                        continue;

                    try
                    {
                        // Get job count for this category
                        int catId = GetPSPropertyInt(catObj, "ID");
                        int jobCount = 0;
                        jobCountByCategory.TryGetValue(catId, out jobCount);

                        // Add custom NoteProperties
                        AddOrSetProperty(catObj, "ComputerName", computerName);
                        AddOrSetProperty(catObj, "InstanceName", serviceName);
                        AddOrSetProperty(catObj, "SqlInstance", domainInstanceName);
                        AddOrSetProperty(catObj, "JobCount", jobCount);

                        // Set default display properties
                        SetDefaultDisplayPropertySet(catObj, DefaultDisplayProperties);

                        WriteObject(catObj);
                    }
                    catch (PipelineStoppedException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        StopFunction(
                            String.Format("Something went wrong getting the job category {0} on {1}", catName, instance),
                            exception: ex,
                            target: catObj,
                            isContinue: true);
                        TestFunctionInterrupt();
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
        /// Gets a string property from a server object using PSObject property access.
        /// </summary>
        internal static string GetServerPropertySafe(object server, string propertyName)
        {
            if (server == null)
                return null;
            try
            {
                PSObject pso = PSObject.AsPSObject(server);
                PSPropertyInfo prop = pso.Properties[propertyName];
                if (prop != null && prop.Value != null)
                    return prop.Value.ToString();
            }
            catch (Exception)
            {
                // Property may not exist on this object type
            }
            return null;
        }

        /// <summary>
        /// Gets the job categories collection from the server's JobServer.
        /// </summary>
        private Collection<PSObject> GetJobCategories(object server)
        {
            string script = "param($s) $s.JobServer.JobCategories";
            return InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { server });
        }

        /// <summary>
        /// Gets the jobs collection from the server's JobServer.
        /// </summary>
        private Collection<PSObject> GetJobs(object server)
        {
            string script = "param($s) $s.JobServer.Jobs";
            return InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { server });
        }

        /// <summary>
        /// Builds a lookup dictionary mapping CategoryID to job count.
        /// </summary>
        internal static Dictionary<int, int> BuildJobCountLookup(Collection<PSObject> jobs)
        {
            Dictionary<int, int> result = new Dictionary<int, int>();
            if (jobs == null)
                return result;

            foreach (PSObject job in jobs)
            {
                if (job == null)
                    continue;

                int categoryId = GetPSPropertyInt(job, "CategoryID");
                int count;
                if (result.TryGetValue(categoryId, out count))
                {
                    result[categoryId] = count + 1;
                }
                else
                {
                    result[categoryId] = 1;
                }
            }
            return result;
        }

        /// <summary>
        /// Gets a string property from a PSObject.
        /// </summary>
        internal static string GetPSPropertyString(PSObject obj, string propertyName)
        {
            if (obj == null)
                return null;
            try
            {
                PSPropertyInfo prop = obj.Properties[propertyName];
                if (prop != null && prop.Value != null)
                    return prop.Value.ToString();
            }
            catch (Exception)
            {
                // Property may not exist
            }
            return null;
        }

        /// <summary>
        /// Gets an int property from a PSObject.
        /// </summary>
        internal static int GetPSPropertyInt(PSObject obj, string propertyName)
        {
            if (obj == null)
                return 0;
            try
            {
                PSPropertyInfo prop = obj.Properties[propertyName];
                if (prop != null && prop.Value is int iVal)
                    return iVal;
                if (prop != null && prop.Value != null)
                {
                    int parsed;
                    if (Int32.TryParse(prop.Value.ToString(), out parsed))
                        return parsed;
                }
            }
            catch (Exception)
            {
                // Property may not exist
            }
            return 0;
        }

        /// <summary>
        /// Adds or updates a NoteProperty on a PSObject, matching Add-Member -Force behavior.
        /// </summary>
        internal static void AddOrSetProperty(PSObject obj, string name, object value)
        {
            if (obj == null)
                return;
            try
            {
                PSPropertyInfo existing = obj.Properties[name];
                if (existing != null)
                {
                    existing.Value = value;
                }
                else
                {
                    obj.Properties.Add(new PSNoteProperty(name, value));
                }
            }
            catch (Exception)
            {
                // Force-add by removing then adding
                try
                {
                    obj.Properties.Remove(name);
                    obj.Properties.Add(new PSNoteProperty(name, value));
                }
                catch (Exception)
                {
                    // Best-effort
                }
            }
        }

        /// <summary>
        /// Sets the DefaultDisplayPropertySet on a PSObject for formatted output.
        /// </summary>
        internal static void SetDefaultDisplayPropertySet(PSObject obj, string[] properties)
        {
            if (obj == null || properties == null)
                return;

            try { obj.Members.Remove("PSStandardMembers"); }
            catch (Exception) { /* May not exist yet */ }

            try
            {
                obj.Members.Add(new PSMemberSet("PSStandardMembers", new PSMemberInfo[]
                {
                    new PSPropertySet("DefaultDisplayPropertySet", properties)
                }));
            }
            catch (Exception)
            {
                // Best-effort
            }
        }

        #endregion Helpers
    }
}
