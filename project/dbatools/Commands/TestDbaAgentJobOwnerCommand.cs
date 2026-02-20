using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Identifies SQL Agent jobs with incorrect ownership for security compliance auditing.
    /// Compares each job's current owner against a target login (default 'sa') and returns
    /// jobs that don't match the expected ownership.
    /// </summary>
    [Cmdlet("Test", "DbaAgentJobOwner")]
    public class TestDbaAgentJobOwnerCommand : DbaInstanceCmdlet
    {
        #region Parameters

        /// <summary>
        /// Specifies specific SQL Agent jobs to check for ownership compliance.
        /// When provided, all matching jobs are returned regardless of ownership status.
        /// </summary>
        [Parameter(Position = 1)]
        [Alias("Jobs")]
        public object[] Job { get; set; }

        /// <summary>
        /// Excludes specific SQL Agent jobs from the ownership compliance check.
        /// </summary>
        [Parameter()]
        public object[] ExcludeJob { get; set; }

        /// <summary>
        /// Specifies the target login that should own SQL Agent jobs.
        /// Defaults to 'sa' (or the renamed sysadmin account). Cannot be a Windows Group.
        /// </summary>
        [Parameter()]
        [Alias("TargetLogin")]
        public string Login { get; set; }

        #endregion Parameters

        /// <summary>
        /// Default display properties for output objects.
        /// </summary>
        private static readonly string[] DefaultDisplayProperties = new string[]
        {
            "Server", "Job", "JobType", "CurrentOwner", "TargetOwner", "OwnerMatch"
        };

        /// <summary>
        /// Accumulated results across all pipeline instances.
        /// </summary>
        private List<PSObject> _allResults;

        /// <summary>
        /// Whether the -Job parameter was explicitly bound.
        /// </summary>
        private bool _jobBound;

        /// <summary>
        /// Pre-computed hash sets for job filtering (invariant across instances).
        /// </summary>
        private HashSet<string> _jobFilter;
        private HashSet<string> _excludeFilter;

        /// <summary>
        /// Initializes processing state and captures parameter binding info.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();
            _jobBound = TestBound("Job");
            _allResults = new List<PSObject>();
            _jobFilter = ToStringHashSet(Job);
            _excludeFilter = ToStringHashSet(ExcludeJob);
        }

        /// <summary>
        /// Processes each SQL Server instance, checking agent job ownership.
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
                        errorRecord: new ErrorRecord(ex, "TestDbaAgentJobOwner_ConnectionError", ErrorCategory.ConnectionError, instance),
                        target: instance,
                        isContinue: true,
                        category: ErrorCategory.ConnectionError);
                    TestFunctionInterrupt();
                    continue;
                }

                // Resolve login for this instance
                string targetLogin = ResolveTargetLogin(server, instance);
                if (targetLogin == null)
                {
                    TestFunctionInterrupt();
                    continue;
                }

                // Gather jobs
                WriteMessageAtLevel(String.Format("Gathering jobs to check on {0}.", instance), MessageLevel.Verbose, null);

                Collection<PSObject> allJobs;
                try
                {
                    allJobs = GetJobs(server);
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("Failed to retrieve jobs from {0}", instance),
                        exception: ex,
                        target: instance,
                        isContinue: true);
                    TestFunctionInterrupt();
                    continue;
                }

                if (allJobs == null || allJobs.Count == 0)
                    continue;

                // Filter jobs using pre-computed hash sets
                List<PSObject> jobCollection = new List<PSObject>();
                foreach (PSObject jobObj in allJobs)
                {
                    if (jobObj == null)
                        continue;
                    string jobName = GetPSPropertyString(jobObj, "Name");
                    if (_jobFilter != null)
                    {
                        if (jobName != null && _jobFilter.Contains(jobName))
                            jobCollection.Add(jobObj);
                    }
                    else if (_excludeFilter != null)
                    {
                        if (jobName == null || !_excludeFilter.Contains(jobName))
                            jobCollection.Add(jobObj);
                    }
                    else
                    {
                        jobCollection.Add(jobObj);
                    }
                }

                string serverName = GetServerPropertySafe(server, "Name");

                // Process each job
                foreach (PSObject j in jobCollection)
                {
                    string jobName = GetPSPropertyString(j, "Name");
                    WriteMessageAtLevel(String.Format("Checking {0}", jobName), MessageLevel.Verbose, null);

                    int categoryId = GetPSPropertyInt(j, "CategoryID");
                    string ownerLoginName = GetPSPropertyString(j, "OwnerLoginName");

                    // Determine JobType: "Remote" if CategoryID==1, else job.JobType
                    string jobType;
                    if (categoryId == 1)
                    {
                        jobType = "Remote";
                    }
                    else
                    {
                        jobType = GetPSPropertyString(j, "JobType");
                    }

                    // Determine OwnerMatch: true if CategoryID==1 (remote), else compare
                    bool ownerMatch;
                    if (categoryId == 1)
                    {
                        ownerMatch = true;
                    }
                    else
                    {
                        ownerMatch = String.Equals(ownerLoginName, targetLogin, StringComparison.OrdinalIgnoreCase);
                    }

                    PSObject result = new PSObject();
                    result.Properties.Add(new PSNoteProperty("Server", serverName));
                    result.Properties.Add(new PSNoteProperty("Job", jobName));
                    result.Properties.Add(new PSNoteProperty("JobType", jobType));
                    result.Properties.Add(new PSNoteProperty("CurrentOwner", ownerLoginName));
                    result.Properties.Add(new PSNoteProperty("TargetOwner", targetLogin));
                    result.Properties.Add(new PSNoteProperty("OwnerMatch", ownerMatch));

                    _allResults.Add(result);
                }
            }
        }

        /// <summary>
        /// Outputs the accumulated results, filtered by ownership match unless -Job was specified.
        /// </summary>
        protected override void EndProcessing()
        {
            List<PSObject> output;
            if (_jobBound)
            {
                output = _allResults;
            }
            else
            {
                output = new List<PSObject>();
                foreach (PSObject obj in _allResults)
                {
                    PSPropertyInfo matchProp = obj.Properties["OwnerMatch"];
                    if (matchProp != null && matchProp.Value is bool bVal && !bVal)
                    {
                        output.Add(obj);
                    }
                }
            }

            foreach (PSObject obj in output)
            {
                SetDefaultDisplayPropertySet(obj, DefaultDisplayProperties);
                WriteObject(obj);
            }
        }

        #region Helpers

        /// <summary>
        /// Resolves the target login for ownership comparison.
        /// Returns null if the login is invalid (will have already called StopFunction/WriteMessage).
        /// </summary>
        private string ResolveTargetLogin(object server, DbaInstanceParameter instance)
        {
            string login = Login;
            bool loginBound = TestBound("Login");

            // Validate the login exists on the server
            if (loginBound && !String.IsNullOrEmpty(login))
            {
                bool loginExists = CheckLoginExists(server, login);
                if (!loginExists)
                {
                    if (SqlInstance.Length == 1)
                    {
                        StopFunction(String.Format("Invalid login: {0}.", login));
                        return null;
                    }
                    else
                    {
                        WriteMessageAtLevel(
                            String.Format("{0} is not a valid login on {1}. Moving on.", login, instance),
                            MessageLevel.Warning, null);
                        return null;
                    }
                }

                // Check if login is a Windows Group
                string loginType = GetLoginType(server, login);
                if (String.Equals(loginType, "WindowsGroup", StringComparison.OrdinalIgnoreCase))
                {
                    StopFunction(String.Format("{0} is a Windows Group and can not be a job owner.", login));
                    return null;
                }
            }

            // Default to "sa" if Login was not bound
            if (!loginBound)
            {
                login = "sa";
            }

            // SQL 2000 fallback (VersionMajor < 9)
            int versionMajor = GetServerVersionMajor(server);
            if (versionMajor > 0 && versionMajor < 9 && String.IsNullOrEmpty(login))
            {
                login = "sa";
            }

            // Resolve actual sa name (for orgs that renamed sa)
            if (String.Equals(login, "sa", StringComparison.OrdinalIgnoreCase))
            {
                string resolvedSa = ResolveSaLoginName(server);
                if (!String.IsNullOrEmpty(resolvedSa))
                {
                    login = resolvedSa;
                }
            }

            return login;
        }

        /// <summary>
        /// Checks whether a login name exists on the server.
        /// </summary>
        private bool CheckLoginExists(object server, string loginName)
        {
            try
            {
                string script = "param($s, $l) ($s.Logins.Name) -contains $l";
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, ScriptBlock.Create(script), null,
                    new object[] { server, loginName });
                if (results != null && results.Count > 0 && results[0] != null)
                {
                    if (results[0].BaseObject is bool bVal)
                        return bVal;
                }
            }
            catch (Exception ex)
            {
                WriteMessageAtLevel(
                    String.Format("Unable to check logins on server: {0}", ex.Message),
                    MessageLevel.Warning, null);
            }
            return false;
        }

        /// <summary>
        /// Gets the LoginType of a specific login on the server.
        /// </summary>
        private string GetLoginType(object server, string loginName)
        {
            try
            {
                string script = "param($s, $l) $s.Logins[$l].LoginType";
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, ScriptBlock.Create(script), null,
                    new object[] { server, loginName });
                if (results != null && results.Count > 0 && results[0] != null)
                    return results[0].BaseObject.ToString();
            }
            catch (Exception ex)
            {
                WriteMessageAtLevel(
                    String.Format("Unable to check login type for {0}: {1}", loginName, ex.Message),
                    MessageLevel.Warning, null);
            }
            return null;
        }

        /// <summary>
        /// Resolves the actual sa login name (the login with id=1).
        /// </summary>
        private string ResolveSaLoginName(object server)
        {
            try
            {
                string script = "param($s) ($s.Logins | Where-Object { $_.id -eq 1 }).Name";
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, ScriptBlock.Create(script), null,
                    new object[] { server });
                if (results != null && results.Count > 0 && results[0] != null)
                    return results[0].BaseObject.ToString();
            }
            catch (Exception)
            {
                // Fall through
            }
            return null;
        }

        /// <summary>
        /// Gets the VersionMajor from the server object.
        /// </summary>
        private int GetServerVersionMajor(object server)
        {
            try
            {
                PSObject pso = PSObject.AsPSObject(server);
                PSPropertyInfo prop = pso.Properties["VersionMajor"];
                if (prop != null && prop.Value != null)
                {
                    if (prop.Value is int iVal)
                        return iVal;
                    int parsed;
                    if (Int32.TryParse(prop.Value.ToString(), out parsed))
                        return parsed;
                }
            }
            catch (Exception)
            {
                // Fall through
            }
            return 0;
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
        /// Gets the jobs collection from the server's JobServer.
        /// </summary>
        private Collection<PSObject> GetJobs(object server)
        {
            string script = "param($s) $s.JobServer.Jobs";
            return InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { server });
        }

        /// <summary>
        /// Gets a string property from a server object safely.
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
                // Property may not exist
            }
            return null;
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
        /// Converts an object[] to a case-insensitive HashSet of strings.
        /// Returns null if input is null or empty.
        /// </summary>
        internal static HashSet<string> ToStringHashSet(object[] input)
        {
            if (input == null || input.Length == 0)
                return null;

            HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (object item in input)
            {
                if (item != null)
                {
                    string s = item.ToString();
                    if (!String.IsNullOrEmpty(s))
                        result.Add(s);
                }
            }

            if (result.Count == 0)
                return null;

            return result;
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
