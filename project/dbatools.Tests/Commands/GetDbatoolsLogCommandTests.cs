using System;
using System.Collections.Generic;
using System.Management.Automation;
using Dataplat.Dbatools.Commands;
using Dataplat.Dbatools.Message;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class GetDbatoolsLogCommandTests
    {
        #region FlattenMessage
        [TestMethod]
        public void FlattenMessage_MultilineInput_JoinsWithSpace()
        {
            string input = "Line one\nLine two\nLine three";
            string result = GetDbatoolsLogCommand.FlattenMessage(input);
            Assert.AreEqual("Line one Line two Line three", result);
        }

        [TestMethod]
        public void FlattenMessage_MultipleSpaces_CollapsedToSingle()
        {
            string input = "Hello    world   test";
            string result = GetDbatoolsLogCommand.FlattenMessage(input);
            Assert.AreEqual("Hello world test", result);
        }

        [TestMethod]
        public void FlattenMessage_NewlinesWithExtraSpaces_FullyFlattened()
        {
            string input = "SELECT *\n  FROM dbo.Table\n  WHERE id = 1";
            string result = GetDbatoolsLogCommand.FlattenMessage(input);
            Assert.AreEqual("SELECT * FROM dbo.Table WHERE id = 1", result);
        }

        [TestMethod]
        public void FlattenMessage_NullInput_ReturnsNull()
        {
            string result = GetDbatoolsLogCommand.FlattenMessage(null);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void FlattenMessage_EmptyString_ReturnsEmpty()
        {
            string result = GetDbatoolsLogCommand.FlattenMessage(String.Empty);
            Assert.AreEqual(String.Empty, result);
        }

        [TestMethod]
        public void FlattenMessage_SingleLineNoSpaces_Unchanged()
        {
            string input = "Simple message";
            string result = GetDbatoolsLogCommand.FlattenMessage(input);
            Assert.AreEqual("Simple message", result);
        }
        #endregion FlattenMessage

        #region FilterByTag
        [TestMethod]
        public void FilterByTag_MatchingTag_ReturnsEntry()
        {
            var entries = new List<LogEntry>
            {
                CreateLogEntry("msg1", tags: new List<string> { "backup", "restore" }),
                CreateLogEntry("msg2", tags: new List<string> { "connection" }),
                CreateLogEntry("msg3", tags: new List<string> { "backup" })
            };

            var result = GetDbatoolsLogCommand.FilterByTag(entries, new string[] { "backup" });
            Assert.AreEqual(2, result.Count);
        }

        [TestMethod]
        public void FilterByTag_NoMatchingTags_ReturnsEmpty()
        {
            var entries = new List<LogEntry>
            {
                CreateLogEntry("msg1", tags: new List<string> { "backup" }),
                CreateLogEntry("msg2", tags: new List<string> { "connection" })
            };

            var result = GetDbatoolsLogCommand.FilterByTag(entries, new string[] { "migration" });
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void FilterByTag_NullTags_ReturnsAll()
        {
            var entries = new List<LogEntry>
            {
                CreateLogEntry("msg1"),
                CreateLogEntry("msg2")
            };

            var result = GetDbatoolsLogCommand.FilterByTag(entries, null);
            Assert.AreEqual(2, result.Count);
        }

        [TestMethod]
        public void FilterByTag_CaseInsensitive_Matches()
        {
            var entries = new List<LogEntry>
            {
                CreateLogEntry("msg1", tags: new List<string> { "Backup" })
            };

            var result = GetDbatoolsLogCommand.FilterByTag(entries, new string[] { "backup" });
            Assert.AreEqual(1, result.Count);
        }
        #endregion FilterByTag

        #region FilterByRunspace
        [TestMethod]
        public void FilterByRunspace_MatchingGuid_ReturnsEntry()
        {
            Guid targetRunspace = Guid.NewGuid();
            var entries = new List<LogEntry>
            {
                CreateLogEntry("msg1", runspace: targetRunspace),
                CreateLogEntry("msg2", runspace: Guid.NewGuid()),
                CreateLogEntry("msg3", runspace: targetRunspace)
            };

            var result = GetDbatoolsLogCommand.FilterByRunspace(entries, targetRunspace);
            Assert.AreEqual(2, result.Count);
        }

        [TestMethod]
        public void FilterByRunspace_NoMatch_ReturnsEmpty()
        {
            var entries = new List<LogEntry>
            {
                CreateLogEntry("msg1", runspace: Guid.NewGuid())
            };

            var result = GetDbatoolsLogCommand.FilterByRunspace(entries, Guid.NewGuid());
            Assert.AreEqual(0, result.Count);
        }
        #endregion FilterByRunspace

        #region FilterByLevel
        [TestMethod]
        public void FilterByLevel_MatchingLevel_ReturnsEntry()
        {
            var entries = new List<LogEntry>
            {
                CreateLogEntry("msg1", level: MessageLevel.Warning),
                CreateLogEntry("msg2", level: MessageLevel.Verbose),
                CreateLogEntry("msg3", level: MessageLevel.Warning)
            };

            var result = GetDbatoolsLogCommand.FilterByLevel(entries, new MessageLevel[] { MessageLevel.Warning });
            Assert.AreEqual(2, result.Count);
        }

        [TestMethod]
        public void FilterByLevel_MultipleLevels_ReturnsMatching()
        {
            var entries = new List<LogEntry>
            {
                CreateLogEntry("msg1", level: MessageLevel.Warning),
                CreateLogEntry("msg2", level: MessageLevel.Verbose),
                CreateLogEntry("msg3", level: MessageLevel.Debug)
            };

            var result = GetDbatoolsLogCommand.FilterByLevel(entries, new MessageLevel[] { MessageLevel.Warning, MessageLevel.Debug });
            Assert.AreEqual(2, result.Count);
        }

        [TestMethod]
        public void FilterByLevel_NullLevels_ReturnsAll()
        {
            var entries = new List<LogEntry>
            {
                CreateLogEntry("msg1", level: MessageLevel.Warning),
                CreateLogEntry("msg2", level: MessageLevel.Verbose)
            };

            var result = GetDbatoolsLogCommand.FilterByLevel(entries, null);
            Assert.AreEqual(2, result.Count);
        }
        #endregion FilterByLevel

        #region FilterByTarget
        [TestMethod]
        public void FilterByTarget_MatchingTarget_ReturnsEntry()
        {
            var entries = new List<LogEntry>
            {
                CreateLogEntry("msg1", target: "server1"),
                CreateLogEntry("msg2", target: "server2"),
                CreateLogEntry("msg3", target: "server1")
            };

            var result = GetDbatoolsLogCommand.FilterByTarget(entries, "server1");
            Assert.AreEqual(2, result.Count);
        }

        [TestMethod]
        public void FilterByTarget_NullTarget_MatchesNullEntries()
        {
            var entries = new List<LogEntry>
            {
                CreateLogEntry("msg1", target: null),
                CreateLogEntry("msg2", target: "server1")
            };

            var result = GetDbatoolsLogCommand.FilterByTarget(entries, null);
            Assert.AreEqual(1, result.Count);
        }

        [TestMethod]
        public void FilterByTarget_NoMatch_ReturnsEmpty()
        {
            var entries = new List<LogEntry>
            {
                CreateLogEntry("msg1", target: "server1"),
                CreateLogEntry("msg2", target: "server2")
            };

            var result = GetDbatoolsLogCommand.FilterByTarget(entries, "server3");
            Assert.AreEqual(0, result.Count);
        }
        #endregion FilterByTarget

        #region CreateOutputObject
        [TestMethod]
        public void CreateOutputObject_AllPropertiesPresent()
        {
            var entry = CreateLogEntry(
                "Test\nmessage",
                functionName: "Get-DbaDatabase",
                moduleName: "dbatools",
                level: MessageLevel.Verbose);

            PSObject result = GetDbatoolsLogCommand.CreateOutputObject(entry);

            Assert.IsNotNull(result.Properties["CallStack"]);
            Assert.IsNotNull(result.Properties["ComputerName"]);
            Assert.IsNotNull(result.Properties["File"]);
            Assert.IsNotNull(result.Properties["FunctionName"]);
            Assert.IsNotNull(result.Properties["Level"]);
            Assert.IsNotNull(result.Properties["Line"]);
            Assert.IsNotNull(result.Properties["Message"]);
            Assert.IsNotNull(result.Properties["ModuleName"]);
            Assert.IsNotNull(result.Properties["Runspace"]);
            Assert.IsNotNull(result.Properties["Tags"]);
            Assert.IsNotNull(result.Properties["TargetObject"]);
            Assert.IsNotNull(result.Properties["Timestamp"]);
            Assert.IsNotNull(result.Properties["Type"]);
            Assert.IsNotNull(result.Properties["Username"]);
        }

        [TestMethod]
        public void CreateOutputObject_MessageIsFlattened()
        {
            var entry = CreateLogEntry("Line one\nLine two");

            PSObject result = GetDbatoolsLogCommand.CreateOutputObject(entry);

            Assert.AreEqual("Line one Line two", result.Properties["Message"].Value);
        }

        [TestMethod]
        public void CreateOutputObject_PropertyValues_MatchEntry()
        {
            var entry = CreateLogEntry(
                "test message",
                functionName: "Backup-DbaDatabase",
                moduleName: "dbatools",
                level: MessageLevel.Warning);

            PSObject result = GetDbatoolsLogCommand.CreateOutputObject(entry);

            Assert.AreEqual("Backup-DbaDatabase", result.Properties["FunctionName"].Value);
            Assert.AreEqual("dbatools", result.Properties["ModuleName"].Value);
            Assert.AreEqual(MessageLevel.Warning, result.Properties["Level"].Value);
        }
        #endregion CreateOutputObject

        #region CreateErrorOutputObject
        [TestMethod]
        public void CreateErrorOutputObject_AllPropertiesPresent()
        {
            var record = CreateErrorRecord("Error message", "Test-Function", "dbatools");

            PSObject result = GetDbatoolsLogCommand.CreateErrorOutputObject(record);

            Assert.IsNotNull(result.Properties["CallStack"]);
            Assert.IsNotNull(result.Properties["ComputerName"]);
            Assert.IsNotNull(result.Properties["File"]);
            Assert.IsNotNull(result.Properties["FunctionName"]);
            Assert.IsNotNull(result.Properties["Level"]);
            Assert.IsNotNull(result.Properties["Line"]);
            Assert.IsNotNull(result.Properties["Message"]);
            Assert.IsNotNull(result.Properties["ModuleName"]);
            Assert.IsNotNull(result.Properties["Runspace"]);
            Assert.IsNotNull(result.Properties["Tags"]);
            Assert.IsNotNull(result.Properties["TargetObject"]);
            Assert.IsNotNull(result.Properties["Timestamp"]);
            Assert.IsNotNull(result.Properties["Type"]);
            Assert.IsNotNull(result.Properties["Username"]);
        }

        [TestMethod]
        public void CreateErrorOutputObject_MissingFieldsAreNull()
        {
            var record = CreateErrorRecord("Error message", "Test-Function", "dbatools");

            PSObject result = GetDbatoolsLogCommand.CreateErrorOutputObject(record);

            // DbatoolsExceptionRecord has no CallStack, File, Level, Line, Type, Username
            // These should be null, matching PS1 Select-Object behavior
            Assert.IsNull(result.Properties["CallStack"].Value);
            Assert.IsNull(result.Properties["File"].Value);
            Assert.IsNull(result.Properties["Level"].Value);
            Assert.IsNull(result.Properties["Line"].Value);
            Assert.IsNull(result.Properties["Type"].Value);
            Assert.IsNull(result.Properties["Username"].Value);
        }

        [TestMethod]
        public void CreateErrorOutputObject_PopulatedFieldsMatch()
        {
            var record = CreateErrorRecord("Error message", "Backup-DbaDatabase", "dbatools");

            PSObject result = GetDbatoolsLogCommand.CreateErrorOutputObject(record);

            Assert.AreEqual("Backup-DbaDatabase", result.Properties["FunctionName"].Value);
            Assert.AreEqual("dbatools", result.Properties["ModuleName"].Value);
            Assert.AreEqual("Error message", result.Properties["Message"].Value);
        }

        [TestMethod]
        public void CreateErrorOutputObject_MessageIsFlattened()
        {
            var record = CreateErrorRecord("Line one\nLine two", "Test-Function", "dbatools");

            PSObject result = GetDbatoolsLogCommand.CreateErrorOutputObject(record);

            Assert.AreEqual("Line one Line two", result.Properties["Message"].Value);
        }
        #endregion CreateErrorOutputObject

        #region FilterErrorsByTag
        [TestMethod]
        public void FilterErrorsByTag_MatchingTag_ReturnsRecord()
        {
            var records = new List<DbatoolsExceptionRecord>
            {
                CreateErrorRecord("msg1", "func1", "mod1", tags: new List<string> { "backup" }),
                CreateErrorRecord("msg2", "func2", "mod2", tags: new List<string> { "connection" })
            };

            var result = GetDbatoolsLogCommand.FilterErrorsByTag(records, new string[] { "backup" });
            Assert.AreEqual(1, result.Count);
        }

        [TestMethod]
        public void FilterErrorsByTag_NullTags_ReturnsAll()
        {
            var records = new List<DbatoolsExceptionRecord>
            {
                CreateErrorRecord("msg1", "func1", "mod1"),
                CreateErrorRecord("msg2", "func2", "mod2")
            };

            var result = GetDbatoolsLogCommand.FilterErrorsByTag(records, null);
            Assert.AreEqual(2, result.Count);
        }
        #endregion FilterErrorsByTag

        #region FilterErrorsByRunspace
        [TestMethod]
        public void FilterErrorsByRunspace_MatchingGuid_ReturnsRecord()
        {
            Guid targetRunspace = Guid.NewGuid();
            var records = new List<DbatoolsExceptionRecord>
            {
                CreateErrorRecord("msg1", "func1", "mod1", runspace: targetRunspace),
                CreateErrorRecord("msg2", "func2", "mod2", runspace: Guid.NewGuid())
            };

            var result = GetDbatoolsLogCommand.FilterErrorsByRunspace(records, targetRunspace);
            Assert.AreEqual(1, result.Count);
        }
        #endregion FilterErrorsByRunspace

        #region FilterErrorsByTarget
        [TestMethod]
        public void FilterErrorsByTarget_MatchingTarget_ReturnsRecord()
        {
            var records = new List<DbatoolsExceptionRecord>
            {
                CreateErrorRecord("msg1", "func1", "mod1"),
                CreateErrorRecord("msg2", "func2", "mod2")
            };
            // TargetObject on DbatoolsExceptionRecord is a computed property from Exceptions
            // With no exceptions, it returns null
            var result = GetDbatoolsLogCommand.FilterErrorsByTarget(records, null);
            Assert.AreEqual(2, result.Count);
        }
        #endregion FilterErrorsByTarget

        #region GetFilteredLogEntries
        [TestMethod]
        public void GetFilteredLogEntries_WildcardFunctionName_FiltersCorrectly()
        {
            var pattern = new WildcardPattern("Get-Dba*", WildcardOptions.IgnoreCase);
            Assert.IsTrue(pattern.IsMatch("Get-DbaDatabase"));
            Assert.IsFalse(pattern.IsMatch("Set-DbaDatabase"));
        }
        #endregion GetFilteredLogEntries

        #region GetFilteredErrorRecords
        [TestMethod]
        public void GetFilteredErrorRecords_WildcardPattern_FiltersCorrectly()
        {
            var pattern = new WildcardPattern("Backup-*", WildcardOptions.IgnoreCase);
            Assert.IsTrue(pattern.IsMatch("Backup-DbaDatabase"));
            Assert.IsFalse(pattern.IsMatch("Get-DbaDatabase"));
        }
        #endregion GetFilteredErrorRecords

        #region Helpers
        private static LogEntry CreateLogEntry(
            string message = "test",
            string functionName = "Test-Function",
            string moduleName = "dbatools",
            MessageLevel level = MessageLevel.Verbose,
            Guid runspace = default(Guid),
            List<string> tags = null,
            object target = null)
        {
            LogEntry entry = new LogEntry();
            entry.Message = message;
            entry.FunctionName = functionName;
            entry.ModuleName = moduleName;
            entry.Level = level;
            entry.Runspace = runspace == default(Guid) ? Guid.NewGuid() : runspace;
            entry.Tags = tags ?? new List<string>();
            entry.TargetObject = target;
            entry.Timestamp = DateTime.Now;
            entry.ComputerName = Environment.MachineName;
            entry.Username = "TestUser";
            entry.File = "test.ps1";
            entry.Line = 1;
            entry.Type = LogEntryType.Information;
            return entry;
        }

        private static DbatoolsExceptionRecord CreateErrorRecord(
            string message = "error",
            string functionName = "Test-Function",
            string moduleName = "dbatools",
            Guid runspace = default(Guid),
            List<string> tags = null)
        {
            DbatoolsExceptionRecord record = new DbatoolsExceptionRecord();
            record.Message = message;
            record.FunctionName = functionName;
            record.ModuleName = moduleName;
            record.Runspace = runspace == default(Guid) ? Guid.NewGuid() : runspace;
            record.Tags = tags ?? new List<string>();
            record.Timestamp = DateTime.Now;
            record.ComputerName = Environment.MachineName;
            return record;
        }
        #endregion Helpers
    }
}
