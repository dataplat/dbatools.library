using System;
using System.Collections.Generic;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class GetDbaAgentJobHistoryCommandTests
    {
        #region CalculateDurationSeconds

        [TestMethod]
        public void CalculateDurationSeconds_ZeroDuration_ReturnsZero()
        {
            int result = Dbatools.Commands.GetDbaAgentJobHistoryCommand.CalculateDurationSeconds(0);
            Assert.AreEqual(0, result);
        }

        [TestMethod]
        public void CalculateDurationSeconds_SecondsOnly_ReturnsCorrectSeconds()
        {
            // 45 = 0 hours, 0 minutes, 45 seconds
            int result = Dbatools.Commands.GetDbaAgentJobHistoryCommand.CalculateDurationSeconds(45);
            Assert.AreEqual(45, result);
        }

        [TestMethod]
        public void CalculateDurationSeconds_MinutesAndSeconds_ReturnsCorrectTotal()
        {
            // 112 = 0 hours, 1 minute, 12 seconds = 72 seconds
            int result = Dbatools.Commands.GetDbaAgentJobHistoryCommand.CalculateDurationSeconds(112);
            Assert.AreEqual(72, result);
        }

        [TestMethod]
        public void CalculateDurationSeconds_HoursMinutesSeconds_ReturnsCorrectTotal()
        {
            // 10530 = 1 hour, 5 minutes, 30 seconds = 3600 + 300 + 30 = 3930
            int result = Dbatools.Commands.GetDbaAgentJobHistoryCommand.CalculateDurationSeconds(10530);
            Assert.AreEqual(3930, result);
        }

        [TestMethod]
        public void CalculateDurationSeconds_ExactMinute_ReturnsCorrectTotal()
        {
            // 200 = 0 hours, 2 minutes, 0 seconds = 120
            int result = Dbatools.Commands.GetDbaAgentJobHistoryCommand.CalculateDurationSeconds(200);
            Assert.AreEqual(120, result);
        }

        [TestMethod]
        public void CalculateDurationSeconds_ExactHour_ReturnsCorrectTotal()
        {
            // 10000 = 1 hour, 0 minutes, 0 seconds = 3600
            int result = Dbatools.Commands.GetDbaAgentJobHistoryCommand.CalculateDurationSeconds(10000);
            Assert.AreEqual(3600, result);
        }

        #endregion CalculateDurationSeconds

        #region GetStatusString

        [TestMethod]
        public void GetStatusString_Failed_ReturnsFailed()
        {
            Assert.AreEqual("Failed", Dbatools.Commands.GetDbaAgentJobHistoryCommand.GetStatusString(0));
        }

        [TestMethod]
        public void GetStatusString_Succeeded_ReturnsSucceeded()
        {
            Assert.AreEqual("Succeeded", Dbatools.Commands.GetDbaAgentJobHistoryCommand.GetStatusString(1));
        }

        [TestMethod]
        public void GetStatusString_Retry_ReturnsRetry()
        {
            Assert.AreEqual("Retry", Dbatools.Commands.GetDbaAgentJobHistoryCommand.GetStatusString(2));
        }

        [TestMethod]
        public void GetStatusString_Canceled_ReturnsCanceled()
        {
            Assert.AreEqual("Canceled", Dbatools.Commands.GetDbaAgentJobHistoryCommand.GetStatusString(3));
        }

        [TestMethod]
        public void GetStatusString_Unknown_ReturnsNull()
        {
            Assert.IsNull(Dbatools.Commands.GetDbaAgentJobHistoryCommand.GetStatusString(99));
        }

        #endregion GetStatusString

        #region ResolveTokenEscape

        [TestMethod]
        public void ResolveTokenEscape_NullMethod_ReturnsValueUnchanged()
        {
            string result = Dbatools.Commands.GetDbaAgentJobHistoryCommand.ResolveTokenEscape(null, "test'value");
            Assert.AreEqual("test'value", result);
        }

        [TestMethod]
        public void ResolveTokenEscape_EmptyMethod_ReturnsValueUnchanged()
        {
            string result = Dbatools.Commands.GetDbaAgentJobHistoryCommand.ResolveTokenEscape("", "test'value");
            Assert.AreEqual("test'value", result);
        }

        [TestMethod]
        public void ResolveTokenEscape_EscapeNone_ReturnsValueUnchanged()
        {
            string result = Dbatools.Commands.GetDbaAgentJobHistoryCommand.ResolveTokenEscape("ESCAPE_NONE", "test'value");
            Assert.AreEqual("test'value", result);
        }

        [TestMethod]
        public void ResolveTokenEscape_EscapeSquote_DoublesSingleQuotes()
        {
            string result = Dbatools.Commands.GetDbaAgentJobHistoryCommand.ResolveTokenEscape("ESCAPE_SQUOTE", "it's a test");
            Assert.AreEqual("it''s a test", result);
        }

        [TestMethod]
        public void ResolveTokenEscape_EscapeDquote_DoublesDoubleQuotes()
        {
            string result = Dbatools.Commands.GetDbaAgentJobHistoryCommand.ResolveTokenEscape("ESCAPE_DQUOTE", "say \"hello\"");
            // Already doubled quotes should not be further doubled
            // Input has: say "hello" - each " should become ""
            string input = "say " + '"' + "hello" + '"';
            result = Dbatools.Commands.GetDbaAgentJobHistoryCommand.ResolveTokenEscape("ESCAPE_DQUOTE", input);
            Assert.AreEqual("say \"\"hello\"\"", result);
        }

        [TestMethod]
        public void ResolveTokenEscape_EscapeRbracket_DoublesRightBrackets()
        {
            string result = Dbatools.Commands.GetDbaAgentJobHistoryCommand.ResolveTokenEscape("ESCAPE_RBRACKET", "test]value");
            Assert.AreEqual("test]]value", result);
        }

        [TestMethod]
        public void ResolveTokenEscape_SquotePreservesAlreadyDoubled()
        {
            // Already doubled quotes should not be further doubled by the lookbehind/lookahead regex
            string result = Dbatools.Commands.GetDbaAgentJobHistoryCommand.ResolveTokenEscape("ESCAPE_SQUOTE", "it''s fine");
            Assert.AreEqual("it''s fine", result);
        }

        #endregion ResolveTokenEscape

        #region JoinAdminUNC

        [TestMethod]
        public void JoinAdminUNC_NullComputerName_ReturnsEmpty()
        {
            string result = Dbatools.Commands.GetDbaAgentJobHistoryCommand.JoinAdminUNC(null, @"C:\logs\file.txt");
            Assert.AreEqual(String.Empty, result);
        }

        [TestMethod]
        public void JoinAdminUNC_NullPath_ReturnsEmpty()
        {
            string result = Dbatools.Commands.GetDbaAgentJobHistoryCommand.JoinAdminUNC("SERVER", null);
            Assert.AreEqual(String.Empty, result);
        }

        [TestMethod]
        public void JoinAdminUNC_EmptyPath_ReturnsEmpty()
        {
            string result = Dbatools.Commands.GetDbaAgentJobHistoryCommand.JoinAdminUNC("SERVER", "");
            Assert.AreEqual(String.Empty, result);
        }

        [TestMethod]
        public void JoinAdminUNC_DriveLetterPath_ConvertsToUNC()
        {
            string result = Dbatools.Commands.GetDbaAgentJobHistoryCommand.JoinAdminUNC("SERVER", @"C:\logs\file.txt");
            Assert.AreEqual(@"\\SERVER\C$\logs\file.txt", result);
        }

        [TestMethod]
        public void JoinAdminUNC_NonDrivePath_JoinsWithBackslash()
        {
            string result = Dbatools.Commands.GetDbaAgentJobHistoryCommand.JoinAdminUNC("SERVER", "share\\file.txt");
            Assert.AreEqual(@"\\SERVER\share\file.txt", result);
        }

        #endregion JoinAdminUNC

        #region ResolveJobToken

        [TestMethod]
        public void ResolveJobToken_NullOutfile_ReturnsNull()
        {
            PSObject exec = new PSObject();
            Dictionary<string, string> propMap = new Dictionary<string, string>();
            string result = Dbatools.Commands.GetDbaAgentJobHistoryCommand.ResolveJobToken(exec, null, null, propMap);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void ResolveJobToken_EmptyOutfile_ReturnsEmpty()
        {
            PSObject exec = new PSObject();
            Dictionary<string, string> propMap = new Dictionary<string, string>();
            string result = Dbatools.Commands.GetDbaAgentJobHistoryCommand.ResolveJobToken(exec, null, "", propMap);
            Assert.AreEqual("", result);
        }

        [TestMethod]
        public void ResolveJobToken_SimpleToken_ResolvesFromPropMap()
        {
            PSObject exec = new PSObject();
            Dictionary<string, string> propMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            propMap["INST"] = "MSSQLSERVER";

            string result = Dbatools.Commands.GetDbaAgentJobHistoryCommand.ResolveJobToken(
                exec, null, "$(INST)_output.txt", propMap);
            Assert.AreEqual("MSSQLSERVER_output.txt", result);
        }

        [TestMethod]
        public void ResolveJobToken_StepId_ResolvesFromExecution()
        {
            PSObject exec = new PSObject();
            exec.Properties.Add(new PSNoteProperty("StepID", 3));
            Dictionary<string, string> propMap = new Dictionary<string, string>();

            string result = Dbatools.Commands.GetDbaAgentJobHistoryCommand.ResolveJobToken(
                exec, null, "step_$(STEPID).log", propMap);
            Assert.AreEqual("step_3.log", result);
        }

        [TestMethod]
        public void ResolveJobToken_JobId_ConvertsToHex()
        {
            PSObject exec = new PSObject();
            Guid jobId = new Guid("E7718A84-8B43-46D0-8F8D-4FC4464F9FC5");
            exec.Properties.Add(new PSNoteProperty("JobID", jobId));
            Dictionary<string, string> propMap = new Dictionary<string, string>();

            string result = Dbatools.Commands.GetDbaAgentJobHistoryCommand.ResolveJobToken(
                exec, null, "$(JOBID)__output", propMap);
            Assert.AreEqual("0x848A71E7438BD0468F8D4FC4464F9FC5__output", result);
        }

        [TestMethod]
        public void ResolveJobToken_Date_FormatsAsYYYYMMDD()
        {
            PSObject exec = new PSObject();
            exec.Properties.Add(new PSNoteProperty("RunDate", new DateTime(2017, 9, 26, 13, 0, 0)));
            Dictionary<string, string> propMap = new Dictionary<string, string>();

            string result = Dbatools.Commands.GetDbaAgentJobHistoryCommand.ResolveJobToken(
                exec, null, "$(DATE)_output.log", propMap);
            Assert.AreEqual("20170926_output.log", result);
        }

        [TestMethod]
        public void ResolveJobToken_Time_FormatsAsHHMMSS()
        {
            PSObject exec = new PSObject();
            exec.Properties.Add(new PSNoteProperty("RunDate", new DateTime(2017, 9, 26, 13, 0, 1)));
            Dictionary<string, string> propMap = new Dictionary<string, string>();

            string result = Dbatools.Commands.GetDbaAgentJobHistoryCommand.ResolveJobToken(
                exec, null, "$(TIME)_output.log", propMap);
            Assert.AreEqual("130001_output.log", result);
        }

        [TestMethod]
        public void ResolveJobToken_StrtDt_UsesOutcomeRunDate()
        {
            PSObject exec = new PSObject();
            exec.Properties.Add(new PSNoteProperty("RunDate", new DateTime(2017, 9, 26, 13, 0, 1)));

            PSObject outcome = new PSObject();
            outcome.Properties.Add(new PSNoteProperty("RunDate", new DateTime(2017, 9, 26, 13, 0, 0)));

            Dictionary<string, string> propMap = new Dictionary<string, string>();

            string result = Dbatools.Commands.GetDbaAgentJobHistoryCommand.ResolveJobToken(
                exec, outcome, "$(STRTDT)_output.log", propMap);
            Assert.AreEqual("20170926_output.log", result);
        }

        [TestMethod]
        public void ResolveJobToken_StrtTm_UsesOutcomeRunDate()
        {
            PSObject exec = new PSObject();
            exec.Properties.Add(new PSNoteProperty("RunDate", new DateTime(2017, 9, 26, 13, 0, 1)));

            PSObject outcome = new PSObject();
            outcome.Properties.Add(new PSNoteProperty("RunDate", new DateTime(2017, 9, 26, 13, 0, 0)));

            Dictionary<string, string> propMap = new Dictionary<string, string>();

            string result = Dbatools.Commands.GetDbaAgentJobHistoryCommand.ResolveJobToken(
                exec, outcome, "$(STRTTM)_output.log", propMap);
            Assert.AreEqual("130000_output.log", result);
        }

        [TestMethod]
        public void ResolveJobToken_EscapeSquote_AppliesEscaping()
        {
            PSObject exec = new PSObject();
            Dictionary<string, string> propMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            propMap["SQLLOGDIR"] = "ErrorLog_'_\"_]_Path";

            string result = Dbatools.Commands.GetDbaAgentJobHistoryCommand.ResolveJobToken(
                exec, null, "$(ESCAPE_SQUOTE(SQLLOGDIR))__output", propMap);
            Assert.AreEqual("ErrorLog_''_\"_]_Path__output", result);
        }

        [TestMethod]
        public void ResolveJobToken_EscapeDquote_AppliesEscaping()
        {
            PSObject exec = new PSObject();
            Dictionary<string, string> propMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            propMap["SQLLOGDIR"] = "ErrorLog_'_\"_]_Path";

            string result = Dbatools.Commands.GetDbaAgentJobHistoryCommand.ResolveJobToken(
                exec, null, "$(ESCAPE_DQUOTE(SQLLOGDIR))__output", propMap);
            Assert.AreEqual("ErrorLog_'_\"\"_]_Path__output", result);
        }

        [TestMethod]
        public void ResolveJobToken_EscapeRbracket_AppliesEscaping()
        {
            PSObject exec = new PSObject();
            Dictionary<string, string> propMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            propMap["SQLLOGDIR"] = "ErrorLog_'_\"_]_Path";

            string result = Dbatools.Commands.GetDbaAgentJobHistoryCommand.ResolveJobToken(
                exec, null, "$(ESCAPE_RBRACKET(SQLLOGDIR))__output", propMap);
            Assert.AreEqual("ErrorLog_'_\"_]]_Path__output", result);
        }

        [TestMethod]
        public void ResolveJobToken_EscapeNone_NoEscaping()
        {
            PSObject exec = new PSObject();
            Dictionary<string, string> propMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            propMap["SQLLOGDIR"] = "ErrorLog_'_\"_]_Path";

            string result = Dbatools.Commands.GetDbaAgentJobHistoryCommand.ResolveJobToken(
                exec, null, "$(ESCAPE_NONE(SQLLOGDIR))__output", propMap);
            Assert.AreEqual("ErrorLog_'_\"_]_Path__output", result);
        }

        #endregion ResolveJobToken

        #region AddOrSetProperty

        [TestMethod]
        public void AddOrSetProperty_NewProperty_AddsProperty()
        {
            PSObject obj = new PSObject();
            Dbatools.Commands.GetDbaAgentJobHistoryCommand.AddOrSetProperty(obj, "TestProp", "TestValue");
            Assert.AreEqual("TestValue", obj.Properties["TestProp"].Value);
        }

        [TestMethod]
        public void AddOrSetProperty_ExistingProperty_UpdatesValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("TestProp", "OldValue"));
            Dbatools.Commands.GetDbaAgentJobHistoryCommand.AddOrSetProperty(obj, "TestProp", "NewValue");
            Assert.AreEqual("NewValue", obj.Properties["TestProp"].Value);
        }

        [TestMethod]
        public void AddOrSetProperty_NullObject_DoesNotThrow()
        {
            // Should not throw
            Dbatools.Commands.GetDbaAgentJobHistoryCommand.AddOrSetProperty(null, "TestProp", "TestValue");
        }

        #endregion AddOrSetProperty

        #region GetPSPropertyHelpers

        [TestMethod]
        public void GetPSPropertyString_NullObject_ReturnsNull()
        {
            Assert.IsNull(Dbatools.Commands.GetDbaAgentJobHistoryCommand.GetPSPropertyString(null, "prop"));
        }

        [TestMethod]
        public void GetPSPropertyString_ExistingProperty_ReturnsValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "TestJob"));
            Assert.AreEqual("TestJob", Dbatools.Commands.GetDbaAgentJobHistoryCommand.GetPSPropertyString(obj, "Name"));
        }

        [TestMethod]
        public void GetPSPropertyString_MissingProperty_ReturnsNull()
        {
            PSObject obj = new PSObject();
            Assert.IsNull(Dbatools.Commands.GetDbaAgentJobHistoryCommand.GetPSPropertyString(obj, "NonExistent"));
        }

        [TestMethod]
        public void GetPSPropertyInt_ExistingProperty_ReturnsValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("StepID", 5));
            Assert.AreEqual(5, Dbatools.Commands.GetDbaAgentJobHistoryCommand.GetPSPropertyInt(obj, "StepID"));
        }

        [TestMethod]
        public void GetPSPropertyInt_MissingProperty_ReturnsZero()
        {
            PSObject obj = new PSObject();
            Assert.AreEqual(0, Dbatools.Commands.GetDbaAgentJobHistoryCommand.GetPSPropertyInt(obj, "Missing"));
        }

        [TestMethod]
        public void GetPSPropertyDateTime_ExistingProperty_ReturnsValue()
        {
            PSObject obj = new PSObject();
            DateTime expected = new DateTime(2017, 9, 26, 13, 0, 0);
            obj.Properties.Add(new PSNoteProperty("RunDate", expected));
            Assert.AreEqual(expected, Dbatools.Commands.GetDbaAgentJobHistoryCommand.GetPSPropertyDateTime(obj, "RunDate"));
        }

        [TestMethod]
        public void GetPSPropertyGuid_ExistingProperty_ReturnsValue()
        {
            PSObject obj = new PSObject();
            Guid expected = new Guid("E7718A84-8B43-46D0-8F8D-4FC4464F9FC5");
            obj.Properties.Add(new PSNoteProperty("JobID", expected));
            Assert.AreEqual(expected, Dbatools.Commands.GetDbaAgentJobHistoryCommand.GetPSPropertyGuid(obj, "JobID"));
        }

        [TestMethod]
        public void GetPSPropertyGuid_MissingProperty_ReturnsGuidEmpty()
        {
            PSObject obj = new PSObject();
            Assert.AreEqual(Guid.Empty, Dbatools.Commands.GetDbaAgentJobHistoryCommand.GetPSPropertyGuid(obj, "Missing"));
        }

        #endregion GetPSPropertyHelpers
    }
}
