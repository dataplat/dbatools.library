using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class FindDbaAgentJobCommandTests
    {
        #region Helper: CreateJobPSObject
        /// <summary>
        /// Creates a mock PSObject representing a SQL Agent job with common properties.
        /// </summary>
        private static PSObject CreateJobPSObject(
            string name,
            string category = "Uncategorized",
            string ownerLoginName = "sa",
            string lastRunOutcome = "Succeeded",
            DateTime? lastRunDate = null,
            bool isEnabled = true,
            bool hasSchedule = true,
            string operatorToEmail = "DBA")
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", name));
            obj.Properties.Add(new PSNoteProperty("Category", category));
            obj.Properties.Add(new PSNoteProperty("OwnerLoginName", ownerLoginName));
            obj.Properties.Add(new PSNoteProperty("LastRunOutcome", lastRunOutcome));
            obj.Properties.Add(new PSNoteProperty("LastRunDate", lastRunDate ?? DateTime.Now.AddDays(-1)));
            obj.Properties.Add(new PSNoteProperty("IsEnabled", isEnabled));
            obj.Properties.Add(new PSNoteProperty("HasSchedule", hasSchedule));
            obj.Properties.Add(new PSNoteProperty("OperatorToEmail", operatorToEmail));
            return obj;
        }

        /// <summary>
        /// Converts a list to a Collection for the filter methods.
        /// </summary>
        private static Collection<PSObject> ToCollection(params PSObject[] items)
        {
            Collection<PSObject> coll = new Collection<PSObject>();
            foreach (PSObject item in items)
            {
                coll.Add(item);
            }
            return coll;
        }
        #endregion

        #region FilterJobsByName
        [TestMethod]
        public void FilterJobsByName_ExactMatch_ReturnsMatchingJob()
        {
            PSObject job1 = CreateJobPSObject("BackupJob");
            PSObject job2 = CreateJobPSObject("IndexJob");
            Collection<PSObject> allJobs = ToCollection(job1, job2);

            List<PSObject> result = FindDbaAgentJobCommand.FilterJobsByName(allJobs, new string[] { "BackupJob" });
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("BackupJob", result[0].Properties["Name"].Value);
        }

        [TestMethod]
        public void FilterJobsByName_WildcardMatch_ReturnsMatchingJobs()
        {
            PSObject job1 = CreateJobPSObject("DailyBackup");
            PSObject job2 = CreateJobPSObject("WeeklyBackup");
            PSObject job3 = CreateJobPSObject("IndexMaintenance");
            Collection<PSObject> allJobs = ToCollection(job1, job2, job3);

            List<PSObject> result = FindDbaAgentJobCommand.FilterJobsByName(allJobs, new string[] { "*Backup*" });
            Assert.AreEqual(2, result.Count);
        }

        [TestMethod]
        public void FilterJobsByName_CaseInsensitive_Matches()
        {
            PSObject job1 = CreateJobPSObject("BACKUPJOB");
            Collection<PSObject> allJobs = ToCollection(job1);

            List<PSObject> result = FindDbaAgentJobCommand.FilterJobsByName(allJobs, new string[] { "backupjob" });
            Assert.AreEqual(1, result.Count);
        }

        [TestMethod]
        public void FilterJobsByName_MultipleFilters_ReturnsUnion()
        {
            PSObject job1 = CreateJobPSObject("BackupJob");
            PSObject job2 = CreateJobPSObject("IndexJob");
            PSObject job3 = CreateJobPSObject("OtherJob");
            Collection<PSObject> allJobs = ToCollection(job1, job2, job3);

            List<PSObject> result = FindDbaAgentJobCommand.FilterJobsByName(allJobs, new string[] { "BackupJob", "IndexJob" });
            Assert.AreEqual(2, result.Count);
        }

        [TestMethod]
        public void FilterJobsByName_NoMatch_ReturnsEmpty()
        {
            PSObject job1 = CreateJobPSObject("BackupJob");
            Collection<PSObject> allJobs = ToCollection(job1);

            List<PSObject> result = FindDbaAgentJobCommand.FilterJobsByName(allJobs, new string[] { "NonExistent" });
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void FilterJobsByName_NullInput_ReturnsEmpty()
        {
            List<PSObject> result = FindDbaAgentJobCommand.FilterJobsByName(null, new string[] { "test" });
            Assert.AreEqual(0, result.Count);

            result = FindDbaAgentJobCommand.FilterJobsByName(new Collection<PSObject>(), null);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void FilterJobsByName_NoDuplicatesPerJob()
        {
            PSObject job1 = CreateJobPSObject("TestBackupJob");
            Collection<PSObject> allJobs = ToCollection(job1);

            // Both filters match the same job - should only appear once
            List<PSObject> result = FindDbaAgentJobCommand.FilterJobsByName(allJobs, new string[] { "*Backup*", "*Job*" });
            Assert.AreEqual(1, result.Count);
        }
        #endregion

        #region FilterByCategory
        [TestMethod]
        public void FilterByCategory_MatchingCategory_ReturnsJobs()
        {
            PSObject job1 = CreateJobPSObject("Job1", category: "REPL-Distribution");
            PSObject job2 = CreateJobPSObject("Job2", category: "Database Maintenance");
            PSObject job3 = CreateJobPSObject("Job3", category: "REPL-Distribution");
            List<PSObject> jobs = new List<PSObject> { job1, job2, job3 };

            List<PSObject> result = FindDbaAgentJobCommand.FilterByCategory(jobs, new string[] { "REPL-Distribution" });
            Assert.AreEqual(2, result.Count);
        }

        [TestMethod]
        public void FilterByCategory_CaseInsensitive_Matches()
        {
            PSObject job1 = CreateJobPSObject("Job1", category: "repl-distribution");
            List<PSObject> jobs = new List<PSObject> { job1 };

            List<PSObject> result = FindDbaAgentJobCommand.FilterByCategory(jobs, new string[] { "REPL-Distribution" });
            Assert.AreEqual(1, result.Count);
        }

        [TestMethod]
        public void FilterByCategory_NoMatch_ReturnsEmpty()
        {
            PSObject job1 = CreateJobPSObject("Job1", category: "Database Maintenance");
            List<PSObject> jobs = new List<PSObject> { job1 };

            List<PSObject> result = FindDbaAgentJobCommand.FilterByCategory(jobs, new string[] { "REPL-Distribution" });
            Assert.AreEqual(0, result.Count);
        }
        #endregion

        #region FilterByFailed
        [TestMethod]
        public void FilterByFailed_FailedJobs_ReturnsOnlyFailed()
        {
            PSObject job1 = CreateJobPSObject("Job1", lastRunOutcome: "Failed");
            PSObject job2 = CreateJobPSObject("Job2", lastRunOutcome: "Succeeded");
            PSObject job3 = CreateJobPSObject("Job3", lastRunOutcome: "Failed");
            List<PSObject> jobs = new List<PSObject> { job1, job2, job3 };

            List<PSObject> result = FindDbaAgentJobCommand.FilterByFailed(jobs);
            Assert.AreEqual(2, result.Count);
        }

        [TestMethod]
        public void FilterByFailed_NoFailures_ReturnsEmpty()
        {
            PSObject job1 = CreateJobPSObject("Job1", lastRunOutcome: "Succeeded");
            List<PSObject> jobs = new List<PSObject> { job1 };

            List<PSObject> result = FindDbaAgentJobCommand.FilterByFailed(jobs);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void FilterByFailed_NullInput_ReturnsEmpty()
        {
            List<PSObject> result = FindDbaAgentJobCommand.FilterByFailed(null);
            Assert.AreEqual(0, result.Count);
        }
        #endregion

        #region FilterByLastUsed
        [TestMethod]
        public void FilterByLastUsed_OldJobs_ReturnsJobs()
        {
            PSObject job1 = CreateJobPSObject("OldJob", lastRunDate: DateTime.Now.AddDays(-30));
            PSObject job2 = CreateJobPSObject("RecentJob", lastRunDate: DateTime.Now.AddDays(-1));
            List<PSObject> jobs = new List<PSObject> { job1, job2 };

            DateTime sinceDate = DateTime.Now.AddDays(-10);
            List<PSObject> result = FindDbaAgentJobCommand.FilterByLastUsed(jobs, sinceDate);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("OldJob", result[0].Properties["Name"].Value);
        }
        #endregion

        #region FilterByDisabled
        [TestMethod]
        public void FilterByDisabled_DisabledJobs_ReturnsDisabled()
        {
            PSObject job1 = CreateJobPSObject("Job1", isEnabled: false);
            PSObject job2 = CreateJobPSObject("Job2", isEnabled: true);
            List<PSObject> jobs = new List<PSObject> { job1, job2 };

            List<PSObject> result = FindDbaAgentJobCommand.FilterByDisabled(jobs);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("Job1", result[0].Properties["Name"].Value);
        }
        #endregion

        #region FilterByNotScheduled
        [TestMethod]
        public void FilterByNotScheduled_UnscheduledJobs_Returns()
        {
            PSObject job1 = CreateJobPSObject("Job1", hasSchedule: false);
            PSObject job2 = CreateJobPSObject("Job2", hasSchedule: true);
            List<PSObject> jobs = new List<PSObject> { job1, job2 };

            List<PSObject> result = FindDbaAgentJobCommand.FilterByNotScheduled(jobs);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("Job1", result[0].Properties["Name"].Value);
        }
        #endregion

        #region FilterByNoEmailNotification
        [TestMethod]
        public void FilterByNoEmailNotification_NoEmail_Returns()
        {
            PSObject job1 = CreateJobPSObject("Job1", operatorToEmail: "");
            PSObject job2 = CreateJobPSObject("Job2", operatorToEmail: "DBA");
            PSObject job3 = CreateJobPSObject("Job3", operatorToEmail: null);
            List<PSObject> jobs = new List<PSObject> { job1, job2, job3 };

            List<PSObject> result = FindDbaAgentJobCommand.FilterByNoEmailNotification(jobs);
            // job1 (empty string) and job3 (null -> GetPSPropertyString returns null -> IsNullOrEmpty true)
            Assert.AreEqual(2, result.Count);
        }
        #endregion

        #region FilterByOwner
        [TestMethod]
        public void FilterByOwner_IncludeMode_MatchingOwner()
        {
            PSObject job1 = CreateJobPSObject("Job1", ownerLoginName: "sa");
            PSObject job2 = CreateJobPSObject("Job2", ownerLoginName: "DOMAIN\\User");
            List<PSObject> jobs = new List<PSObject> { job1, job2 };

            List<PSObject> result = FindDbaAgentJobCommand.FilterByOwner(jobs, "sa");
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("Job1", result[0].Properties["Name"].Value);
        }

        [TestMethod]
        public void FilterByOwner_ExcludeMode_DashPrefix()
        {
            PSObject job1 = CreateJobPSObject("Job1", ownerLoginName: "sa");
            PSObject job2 = CreateJobPSObject("Job2", ownerLoginName: "DOMAIN\\User");
            List<PSObject> jobs = new List<PSObject> { job1, job2 };

            // "-sa" triggers exclude mode: removes dash, excludes jobs owned by "sa"
            List<PSObject> result = FindDbaAgentJobCommand.FilterByOwner(jobs, "-sa");
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("Job2", result[0].Properties["Name"].Value);
        }

        [TestMethod]
        public void FilterByOwner_CaseInsensitive()
        {
            PSObject job1 = CreateJobPSObject("Job1", ownerLoginName: "SA");
            List<PSObject> jobs = new List<PSObject> { job1 };

            List<PSObject> result = FindDbaAgentJobCommand.FilterByOwner(jobs, "sa");
            Assert.AreEqual(1, result.Count);
        }

        [TestMethod]
        public void FilterByOwner_NullJobs_ReturnsEmpty()
        {
            List<PSObject> result = FindDbaAgentJobCommand.FilterByOwner(null, "sa");
            Assert.AreEqual(0, result.Count);
        }
        #endregion

        #region FilterExcludeJobName
        [TestMethod]
        public void FilterExcludeJobName_ExcludesMatching()
        {
            PSObject job1 = CreateJobPSObject("BackupJob");
            PSObject job2 = CreateJobPSObject("IndexJob");
            PSObject job3 = CreateJobPSObject("CleanupJob");
            List<PSObject> output = new List<PSObject> { job1, job2, job3 };

            List<PSObject> result = FindDbaAgentJobCommand.FilterExcludeJobName(output, new string[] { "IndexJob" });
            Assert.AreEqual(2, result.Count);
        }

        [TestMethod]
        public void FilterExcludeJobName_CaseInsensitive()
        {
            PSObject job1 = CreateJobPSObject("BackupJob");
            List<PSObject> output = new List<PSObject> { job1 };

            List<PSObject> result = FindDbaAgentJobCommand.FilterExcludeJobName(output, new string[] { "BACKUPJOB" });
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void FilterExcludeJobName_MultipleExclusions()
        {
            PSObject job1 = CreateJobPSObject("Job1");
            PSObject job2 = CreateJobPSObject("Job2");
            PSObject job3 = CreateJobPSObject("Job3");
            List<PSObject> output = new List<PSObject> { job1, job2, job3 };

            List<PSObject> result = FindDbaAgentJobCommand.FilterExcludeJobName(output, new string[] { "Job1", "Job3" });
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("Job2", result[0].Properties["Name"].Value);
        }
        #endregion

        #region FilterBySince
        [TestMethod]
        public void FilterBySince_RecentJobs_ReturnsMatching()
        {
            PSObject job1 = CreateJobPSObject("OldJob", lastRunDate: new DateTime(2016, 1, 1));
            PSObject job2 = CreateJobPSObject("RecentJob", lastRunDate: new DateTime(2024, 6, 1));
            List<PSObject> output = new List<PSObject> { job1, job2 };

            DateTime since = new DateTime(2016, 7, 1);
            List<PSObject> result = FindDbaAgentJobCommand.FilterBySince(output, since);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("RecentJob", result[0].Properties["Name"].Value);
        }

        [TestMethod]
        public void FilterBySince_ExactDate_Included()
        {
            DateTime exactDate = new DateTime(2023, 6, 15);
            PSObject job1 = CreateJobPSObject("Job1", lastRunDate: exactDate);
            List<PSObject> output = new List<PSObject> { job1 };

            List<PSObject> result = FindDbaAgentJobCommand.FilterBySince(output, exactDate);
            Assert.AreEqual(1, result.Count);
        }

        [TestMethod]
        public void FilterBySince_NullInput_ReturnsEmpty()
        {
            List<PSObject> result = FindDbaAgentJobCommand.FilterBySince(null, DateTime.Now);
            Assert.AreEqual(0, result.Count);
        }
        #endregion

        #region FilterByOwner_DashBehavior
        [TestMethod]
        public void FilterByOwner_DashInMiddleTriggersExclude()
        {
            // PS1 uses -match "-" which matches a dash ANYWHERE in the string
            // So "DOMAIN-User" would trigger exclude mode (removing all dashes)
            PSObject job1 = CreateJobPSObject("Job1", ownerLoginName: "DOMAINUser");
            PSObject job2 = CreateJobPSObject("Job2", ownerLoginName: "other");
            List<PSObject> jobs = new List<PSObject> { job1, job2 };

            // "DOMAIN-User" contains dash -> exclude mode -> removes dash -> excludes "DOMAINUser"
            List<PSObject> result = FindDbaAgentJobCommand.FilterByOwner(jobs, "DOMAIN-User");
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("Job2", result[0].Properties["Name"].Value);
        }
        #endregion
    }
}
