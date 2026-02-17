using System;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class GetDbaAgentJobCommandTests
    {
        #region FilterNonEmptyStrings
        [TestMethod]
        public void FilterNonEmptyStrings_NullInput_ReturnsNull()
        {
            string[] result = GetDbaAgentJobCommand.FilterNonEmptyStrings(null);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void FilterNonEmptyStrings_AllEmpty_ReturnsNull()
        {
            string[] input = new string[] { "", "  ", null };
            string[] result = GetDbaAgentJobCommand.FilterNonEmptyStrings(input);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void FilterNonEmptyStrings_MixedInput_ReturnsOnlyNonEmpty()
        {
            string[] input = new string[] { "job1", "", "job2", null, "  " };
            string[] result = GetDbaAgentJobCommand.FilterNonEmptyStrings(input);
            Assert.AreEqual(2, result.Length);
            Assert.AreEqual("job1", result[0]);
            Assert.AreEqual("job2", result[1]);
        }

        [TestMethod]
        public void FilterNonEmptyStrings_AllValid_ReturnsAll()
        {
            string[] input = new string[] { "a", "b", "c" };
            string[] result = GetDbaAgentJobCommand.FilterNonEmptyStrings(input);
            Assert.AreEqual(3, result.Length);
        }

        [TestMethod]
        public void FilterNonEmptyStrings_EmptyArray_ReturnsNull()
        {
            string[] input = new string[0];
            string[] result = GetDbaAgentJobCommand.FilterNonEmptyStrings(input);
            Assert.IsNull(result);
        }
        #endregion

        #region IsInStringArray
        [TestMethod]
        public void IsInStringArray_MatchExists_ReturnsTrue()
        {
            string[] array = new string[] { "Local", "MultiServer" };
            Assert.IsTrue(GetDbaAgentJobCommand.IsInStringArray("Local", array));
        }

        [TestMethod]
        public void IsInStringArray_CaseInsensitive_ReturnsTrue()
        {
            string[] array = new string[] { "Local", "MultiServer" };
            Assert.IsTrue(GetDbaAgentJobCommand.IsInStringArray("local", array));
        }

        [TestMethod]
        public void IsInStringArray_NoMatch_ReturnsFalse()
        {
            string[] array = new string[] { "Local", "MultiServer" };
            Assert.IsFalse(GetDbaAgentJobCommand.IsInStringArray("Unknown", array));
        }

        [TestMethod]
        public void IsInStringArray_NullValue_ReturnsFalse()
        {
            string[] array = new string[] { "Local" };
            Assert.IsFalse(GetDbaAgentJobCommand.IsInStringArray(null, array));
        }

        [TestMethod]
        public void IsInStringArray_NullArray_ReturnsFalse()
        {
            Assert.IsFalse(GetDbaAgentJobCommand.IsInStringArray("Local", null));
        }
        #endregion

        #region GetPSPropertyString
        [TestMethod]
        public void GetPSPropertyString_ExistingProperty_ReturnsValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "TestJob"));
            string result = GetDbaAgentJobCommand.GetPSPropertyString(obj, "Name");
            Assert.AreEqual("TestJob", result);
        }

        [TestMethod]
        public void GetPSPropertyString_MissingProperty_ReturnsNull()
        {
            PSObject obj = new PSObject();
            string result = GetDbaAgentJobCommand.GetPSPropertyString(obj, "NonExistent");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPSPropertyString_NullObj_ReturnsNull()
        {
            string result = GetDbaAgentJobCommand.GetPSPropertyString(null, "Name");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPSPropertyString_NullValue_ReturnsNull()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", null));
            string result = GetDbaAgentJobCommand.GetPSPropertyString(obj, "Name");
            Assert.IsNull(result);
        }
        #endregion

        #region GetPSPropertyBool
        [TestMethod]
        public void GetPSPropertyBool_TrueValue_ReturnsTrue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("IsEnabled", true));
            Assert.IsTrue(GetDbaAgentJobCommand.GetPSPropertyBool(obj, "IsEnabled"));
        }

        [TestMethod]
        public void GetPSPropertyBool_FalseValue_ReturnsFalse()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("IsEnabled", false));
            Assert.IsFalse(GetDbaAgentJobCommand.GetPSPropertyBool(obj, "IsEnabled"));
        }

        [TestMethod]
        public void GetPSPropertyBool_MissingProperty_ReturnsFalse()
        {
            PSObject obj = new PSObject();
            Assert.IsFalse(GetDbaAgentJobCommand.GetPSPropertyBool(obj, "NonExistent"));
        }

        [TestMethod]
        public void GetPSPropertyBool_StringTrue_ReturnsTrue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("IsEnabled", "True"));
            Assert.IsTrue(GetDbaAgentJobCommand.GetPSPropertyBool(obj, "IsEnabled"));
        }
        #endregion

        #region GetPSPropertyInt
        [TestMethod]
        public void GetPSPropertyInt_ValidInt_ReturnsValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("CategoryID", 5));
            Assert.AreEqual(5, GetDbaAgentJobCommand.GetPSPropertyInt(obj, "CategoryID"));
        }

        [TestMethod]
        public void GetPSPropertyInt_MissingProperty_ReturnsZero()
        {
            PSObject obj = new PSObject();
            Assert.AreEqual(0, GetDbaAgentJobCommand.GetPSPropertyInt(obj, "NonExistent"));
        }

        [TestMethod]
        public void GetPSPropertyInt_StringInt_ReturnsValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("CategoryID", "42"));
            Assert.AreEqual(42, GetDbaAgentJobCommand.GetPSPropertyInt(obj, "CategoryID"));
        }
        #endregion

        #region GetPSPropertyGuid
        [TestMethod]
        public void GetPSPropertyGuid_ValidGuid_ReturnsValue()
        {
            Guid expected = Guid.NewGuid();
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("JobId", expected));
            Assert.AreEqual(expected, GetDbaAgentJobCommand.GetPSPropertyGuid(obj, "JobId"));
        }

        [TestMethod]
        public void GetPSPropertyGuid_MissingProperty_ReturnsEmpty()
        {
            PSObject obj = new PSObject();
            Assert.AreEqual(Guid.Empty, GetDbaAgentJobCommand.GetPSPropertyGuid(obj, "NonExistent"));
        }

        [TestMethod]
        public void GetPSPropertyGuid_StringGuid_ReturnsValue()
        {
            Guid expected = Guid.NewGuid();
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("JobId", expected.ToString()));
            Assert.AreEqual(expected, GetDbaAgentJobCommand.GetPSPropertyGuid(obj, "JobId"));
        }
        #endregion

        #region GetPSPropertyDateTime
        [TestMethod]
        public void GetPSPropertyDateTime_ValidDate_ReturnsValue()
        {
            DateTime expected = new DateTime(2025, 6, 15, 10, 30, 0);
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("DateCreated", expected));
            Assert.AreEqual(expected, GetDbaAgentJobCommand.GetPSPropertyDateTime(obj, "DateCreated"));
        }

        [TestMethod]
        public void GetPSPropertyDateTime_MissingProperty_ReturnsMinValue()
        {
            PSObject obj = new PSObject();
            Assert.AreEqual(DateTime.MinValue, GetDbaAgentJobCommand.GetPSPropertyDateTime(obj, "NonExistent"));
        }
        #endregion

        #region AddOrSetProperty
        [TestMethod]
        public void AddOrSetProperty_NewProperty_AddsIt()
        {
            PSObject obj = new PSObject();
            GetDbaAgentJobCommand.AddOrSetProperty(obj, "ComputerName", "SERVER01");
            Assert.AreEqual("SERVER01", obj.Properties["ComputerName"].Value);
        }

        [TestMethod]
        public void AddOrSetProperty_ExistingProperty_UpdatesIt()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("ComputerName", "OLD"));
            GetDbaAgentJobCommand.AddOrSetProperty(obj, "ComputerName", "NEW");
            Assert.AreEqual("NEW", obj.Properties["ComputerName"].Value);
        }

        [TestMethod]
        public void AddOrSetProperty_NullObj_DoesNotThrow()
        {
            GetDbaAgentJobCommand.AddOrSetProperty(null, "Name", "Value");
            // No exception means success
        }
        #endregion

        #region SetDefaultDisplayPropertySet
        [TestMethod]
        public void SetDefaultDisplayPropertySet_SetsProperties()
        {
            PSObject obj = new PSObject();
            string[] props = new string[] { "Name", "Category" };
            GetDbaAgentJobCommand.SetDefaultDisplayPropertySet(obj, props);

            PSMemberInfo member = obj.Members["PSStandardMembers"];
            Assert.IsNotNull(member);
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_NullObj_DoesNotThrow()
        {
            GetDbaAgentJobCommand.SetDefaultDisplayPropertySet(null, new string[] { "Name" });
            // No exception means success
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_CalledTwice_DoesNotThrow()
        {
            PSObject obj = new PSObject();
            string[] props = new string[] { "Name" };
            GetDbaAgentJobCommand.SetDefaultDisplayPropertySet(obj, props);
            GetDbaAgentJobCommand.SetDefaultDisplayPropertySet(obj, props);

            PSMemberInfo member = obj.Members["PSStandardMembers"];
            Assert.IsNotNull(member);
        }
        #endregion

        #region AddAliasProperty
        [TestMethod]
        public void AddAliasProperty_CreatesLiveAlias()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("IsEnabled", true));
            GetDbaAgentJobCommand.AddAliasProperty(obj, "Enabled", "IsEnabled");

            PSMemberInfo member = obj.Members["Enabled"];
            Assert.IsNotNull(member);
            Assert.IsInstanceOfType(member, typeof(PSAliasProperty));
        }

        [TestMethod]
        public void AddAliasProperty_ReflectsUnderlyingValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("IsEnabled", true));
            GetDbaAgentJobCommand.AddAliasProperty(obj, "Enabled", "IsEnabled");

            Assert.AreEqual(true, obj.Properties["Enabled"].Value);
        }

        [TestMethod]
        public void AddAliasProperty_NullObj_DoesNotThrow()
        {
            GetDbaAgentJobCommand.AddAliasProperty(null, "Enabled", "IsEnabled");
            // No exception means success
        }
        #endregion

        #region GetServerPropertySafe
        [TestMethod]
        public void GetServerPropertySafe_NullServer_ReturnsNull()
        {
            string result = GetDbaAgentJobCommand.GetServerPropertySafe(null, "ComputerName");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetServerPropertySafe_ExistingProperty_ReturnsValue()
        {
            PSObject server = new PSObject();
            server.Properties.Add(new PSNoteProperty("ComputerName", "SERVER01"));
            string result = GetDbaAgentJobCommand.GetServerPropertySafe(server, "ComputerName");
            Assert.AreEqual("SERVER01", result);
        }

        [TestMethod]
        public void GetServerPropertySafe_MissingProperty_ReturnsNull()
        {
            PSObject server = new PSObject();
            string result = GetDbaAgentJobCommand.GetServerPropertySafe(server, "NonExistent");
            Assert.IsNull(result);
        }
        #endregion
    }
}
