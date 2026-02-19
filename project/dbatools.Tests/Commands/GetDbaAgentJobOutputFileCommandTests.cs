using System;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class GetDbaAgentJobOutputFileCommandTests
    {
        #region JoinAdminUnc

        [TestMethod]
        public void JoinAdminUnc_LocalPath_ReturnsUncPath()
        {
            string result = Dataplat.Dbatools.Commands.GetDbaAgentJobOutputFileCommand.JoinAdminUnc(
                "ServerName", @"C:\SQLAgent\Logs\output.txt");
            Assert.AreEqual(@"\\ServerName\C$\SQLAgent\Logs\output.txt", result);
        }

        [TestMethod]
        public void JoinAdminUnc_AlreadyUncPath_ReturnsUnchanged()
        {
            string result = Dataplat.Dbatools.Commands.GetDbaAgentJobOutputFileCommand.JoinAdminUnc(
                "ServerName", @"\\OtherServer\Share\output.txt");
            Assert.AreEqual(@"\\OtherServer\Share\output.txt", result);
        }

        [TestMethod]
        public void JoinAdminUnc_NullFilePath_ReturnsNull()
        {
            string result = Dataplat.Dbatools.Commands.GetDbaAgentJobOutputFileCommand.JoinAdminUnc(
                "ServerName", null);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void JoinAdminUnc_EmptyFilePath_ReturnsEmpty()
        {
            string result = Dataplat.Dbatools.Commands.GetDbaAgentJobOutputFileCommand.JoinAdminUnc(
                "ServerName", "");
            Assert.AreEqual("", result);
        }

        [TestMethod]
        public void JoinAdminUnc_NamedInstance_ExtractsServerName()
        {
            string result = Dataplat.Dbatools.Commands.GetDbaAgentJobOutputFileCommand.JoinAdminUnc(
                @"ServerName\SQLEXPRESS", @"D:\Logs\job.log");
            Assert.AreEqual(@"\\ServerName\D$\Logs\job.log", result);
        }

        [TestMethod]
        public void JoinAdminUnc_DriveLetter_ReplacesColonWithDollar()
        {
            string result = Dataplat.Dbatools.Commands.GetDbaAgentJobOutputFileCommand.JoinAdminUnc(
                "Server1", @"E:\Output\step1.txt");
            Assert.AreEqual(@"\\Server1\E$\Output\step1.txt", result);
        }

        [TestMethod]
        public void JoinAdminUnc_NullServerName_ReturnsFilePathAsIs()
        {
            string result = Dataplat.Dbatools.Commands.GetDbaAgentJobOutputFileCommand.JoinAdminUnc(
                null, @"C:\logs\output.txt");
            Assert.AreEqual(@"C:\logs\output.txt", result);
        }

        [TestMethod]
        public void JoinAdminUnc_EmptyServerName_ReturnsFilePathAsIs()
        {
            string result = Dataplat.Dbatools.Commands.GetDbaAgentJobOutputFileCommand.JoinAdminUnc(
                "", @"C:\logs\output.txt");
            Assert.AreEqual(@"C:\logs\output.txt", result);
        }

        [TestMethod]
        public void JoinAdminUnc_NoDriveLetter_JoinsServerAndPath()
        {
            string result = Dataplat.Dbatools.Commands.GetDbaAgentJobOutputFileCommand.JoinAdminUnc(
                "Server1", @"logs\output.txt");
            Assert.AreEqual(@"\\Server1\logs\output.txt", result);
        }

        #endregion JoinAdminUnc

        #region ConvertToStringArray

        [TestMethod]
        public void ConvertToStringArray_NullInput_ReturnsNull()
        {
            string[] result = Dataplat.Dbatools.Commands.GetDbaAgentJobOutputFileCommand.ConvertToStringArray(null);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void ConvertToStringArray_MixedObjects_ConvertsToStrings()
        {
            object[] input = new object[] { "Job1", 42, "Job3" };
            string[] result = Dataplat.Dbatools.Commands.GetDbaAgentJobOutputFileCommand.ConvertToStringArray(input);
            Assert.AreEqual(3, result.Length);
            Assert.AreEqual("Job1", result[0]);
            Assert.AreEqual("42", result[1]);
            Assert.AreEqual("Job3", result[2]);
        }

        [TestMethod]
        public void ConvertToStringArray_WithNullElement_PreservesNull()
        {
            object[] input = new object[] { "Job1", null, "Job3" };
            string[] result = Dataplat.Dbatools.Commands.GetDbaAgentJobOutputFileCommand.ConvertToStringArray(input);
            Assert.AreEqual(3, result.Length);
            Assert.AreEqual("Job1", result[0]);
            Assert.IsNull(result[1]);
            Assert.AreEqual("Job3", result[2]);
        }

        #endregion ConvertToStringArray

        #region IsInStringArray

        [TestMethod]
        public void IsInStringArray_ValuePresent_ReturnsTrue()
        {
            bool result = Dataplat.Dbatools.Commands.GetDbaAgentJobOutputFileCommand.IsInStringArray(
                "Job1", new string[] { "job1", "Job2" });
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void IsInStringArray_ValueAbsent_ReturnsFalse()
        {
            bool result = Dataplat.Dbatools.Commands.GetDbaAgentJobOutputFileCommand.IsInStringArray(
                "Job3", new string[] { "Job1", "Job2" });
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void IsInStringArray_NullValue_ReturnsFalse()
        {
            bool result = Dataplat.Dbatools.Commands.GetDbaAgentJobOutputFileCommand.IsInStringArray(
                null, new string[] { "Job1" });
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void IsInStringArray_NullArray_ReturnsFalse()
        {
            bool result = Dataplat.Dbatools.Commands.GetDbaAgentJobOutputFileCommand.IsInStringArray(
                "Job1", null);
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void IsInStringArray_CaseInsensitive_ReturnsTrue()
        {
            bool result = Dataplat.Dbatools.Commands.GetDbaAgentJobOutputFileCommand.IsInStringArray(
                "JOB1", new string[] { "job1", "job2" });
            Assert.IsTrue(result);
        }

        #endregion IsInStringArray

        #region GetPSPropertyString

        [TestMethod]
        public void GetPSPropertyString_ExistingProperty_ReturnsValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "TestJob"));
            string result = Dataplat.Dbatools.Commands.GetDbaAgentJobOutputFileCommand.GetPSPropertyString(obj, "Name");
            Assert.AreEqual("TestJob", result);
        }

        [TestMethod]
        public void GetPSPropertyString_MissingProperty_ReturnsNull()
        {
            PSObject obj = new PSObject();
            string result = Dataplat.Dbatools.Commands.GetDbaAgentJobOutputFileCommand.GetPSPropertyString(obj, "DoesNotExist");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPSPropertyString_NullObject_ReturnsNull()
        {
            string result = Dataplat.Dbatools.Commands.GetDbaAgentJobOutputFileCommand.GetPSPropertyString(null, "Name");
            Assert.IsNull(result);
        }

        #endregion GetPSPropertyString

        #region GetPSPropertyInt

        [TestMethod]
        public void GetPSPropertyInt_ExistingProperty_ReturnsValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Id", 5));
            int result = Dataplat.Dbatools.Commands.GetDbaAgentJobOutputFileCommand.GetPSPropertyInt(obj, "Id");
            Assert.AreEqual(5, result);
        }

        [TestMethod]
        public void GetPSPropertyInt_StringNumber_ParsesCorrectly()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Id", "42"));
            int result = Dataplat.Dbatools.Commands.GetDbaAgentJobOutputFileCommand.GetPSPropertyInt(obj, "Id");
            Assert.AreEqual(42, result);
        }

        [TestMethod]
        public void GetPSPropertyInt_MissingProperty_ReturnsZero()
        {
            PSObject obj = new PSObject();
            int result = Dataplat.Dbatools.Commands.GetDbaAgentJobOutputFileCommand.GetPSPropertyInt(obj, "Id");
            Assert.AreEqual(0, result);
        }

        #endregion GetPSPropertyInt

        #region SetDefaultDisplayPropertySet

        [TestMethod]
        public void SetDefaultDisplayPropertySet_NullObject_DoesNotThrow()
        {
            Dataplat.Dbatools.Commands.GetDbaAgentJobOutputFileCommand.SetDefaultDisplayPropertySet(
                null, new string[] { "Prop1" });
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_NullProperties_DoesNotThrow()
        {
            PSObject obj = new PSObject();
            Dataplat.Dbatools.Commands.GetDbaAgentJobOutputFileCommand.SetDefaultDisplayPropertySet(
                obj, null);
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_ValidInput_AddsPSStandardMembers()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "Test"));
            obj.Properties.Add(new PSNoteProperty("Id", 1));

            Dataplat.Dbatools.Commands.GetDbaAgentJobOutputFileCommand.SetDefaultDisplayPropertySet(
                obj, new string[] { "Name" });

            PSMemberInfo members = obj.Members["PSStandardMembers"];
            Assert.IsNotNull(members);
        }

        #endregion SetDefaultDisplayPropertySet

        #region GetServerPropertySafe

        [TestMethod]
        public void GetServerPropertySafe_NullServer_ReturnsNull()
        {
            string result = Dataplat.Dbatools.Commands.GetDbaAgentJobOutputFileCommand.GetServerPropertySafe(
                null, "ComputerName");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetServerPropertySafe_ExistingProperty_ReturnsValue()
        {
            PSObject server = new PSObject();
            server.Properties.Add(new PSNoteProperty("ComputerName", "SQL01"));
            string result = Dataplat.Dbatools.Commands.GetDbaAgentJobOutputFileCommand.GetServerPropertySafe(
                server, "ComputerName");
            Assert.AreEqual("SQL01", result);
        }

        [TestMethod]
        public void GetServerPropertySafe_MissingProperty_ReturnsNull()
        {
            PSObject server = new PSObject();
            string result = Dataplat.Dbatools.Commands.GetDbaAgentJobOutputFileCommand.GetServerPropertySafe(
                server, "NonExistent");
            Assert.IsNull(result);
        }

        #endregion GetServerPropertySafe
    }
}
