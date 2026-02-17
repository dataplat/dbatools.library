using System;
using System.Collections.Generic;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class GetDbaBuildCommandTests
    {
        #region NormalizeMajorVersion
        [TestMethod]
        public void NormalizeMajorVersion_SQL2016_Returns2016()
        {
            string result = GetDbaBuildCommand.NormalizeMajorVersion("SQL2016");
            Assert.AreEqual("2016", result);
        }

        [TestMethod]
        public void NormalizeMajorVersion_JustYear_ReturnsYear()
        {
            string result = GetDbaBuildCommand.NormalizeMajorVersion("2019");
            Assert.AreEqual("2019", result);
        }

        [TestMethod]
        public void NormalizeMajorVersion_2008R2_Returns2008R2()
        {
            string result = GetDbaBuildCommand.NormalizeMajorVersion("2008R2");
            Assert.AreEqual("2008R2", result);
        }

        [TestMethod]
        public void NormalizeMajorVersion_SQL2008R2_Returns2008R2()
        {
            string result = GetDbaBuildCommand.NormalizeMajorVersion("SQL2008R2");
            Assert.AreEqual("2008R2", result);
        }

        [TestMethod]
        public void NormalizeMajorVersion_Invalid_ReturnsNull()
        {
            Assert.IsNull(GetDbaBuildCommand.NormalizeMajorVersion("InvalidVersion"));
            Assert.IsNull(GetDbaBuildCommand.NormalizeMajorVersion("SQL"));
            Assert.IsNull(GetDbaBuildCommand.NormalizeMajorVersion("123"));
            Assert.IsNull(GetDbaBuildCommand.NormalizeMajorVersion(""));
            Assert.IsNull(GetDbaBuildCommand.NormalizeMajorVersion(null));
        }

        [TestMethod]
        public void NormalizeMajorVersion_CaseInsensitive()
        {
            Assert.AreEqual("2008R2", GetDbaBuildCommand.NormalizeMajorVersion("sql2008R2"));
            Assert.AreEqual("2008R2", GetDbaBuildCommand.NormalizeMajorVersion("sql2008r2"));
            Assert.AreEqual("2016", GetDbaBuildCommand.NormalizeMajorVersion("Sql2016"));
        }
        #endregion

        #region NormalizeServicePack
        [TestMethod]
        public void NormalizeServicePack_SP1_ReturnsSP1()
        {
            Assert.AreEqual("SP1", GetDbaBuildCommand.NormalizeServicePack("SP1"));
        }

        [TestMethod]
        public void NormalizeServicePack_JustNumber_ReturnsSPX()
        {
            Assert.AreEqual("SP2", GetDbaBuildCommand.NormalizeServicePack("2"));
        }

        [TestMethod]
        public void NormalizeServicePack_Zero_ReturnsRTM()
        {
            Assert.AreEqual("RTM", GetDbaBuildCommand.NormalizeServicePack("0"));
            Assert.AreEqual("RTM", GetDbaBuildCommand.NormalizeServicePack("SP0"));
        }

        [TestMethod]
        public void NormalizeServicePack_RTM_ReturnsRTM()
        {
            Assert.AreEqual("RTM", GetDbaBuildCommand.NormalizeServicePack("RTM"));
        }

        [TestMethod]
        public void NormalizeServicePack_NullOrEmpty_ReturnsRTM()
        {
            Assert.AreEqual("RTM", GetDbaBuildCommand.NormalizeServicePack(null));
            Assert.AreEqual("RTM", GetDbaBuildCommand.NormalizeServicePack(""));
        }

        [TestMethod]
        public void NormalizeServicePack_Invalid_ReturnsNull()
        {
            Assert.IsNull(GetDbaBuildCommand.NormalizeServicePack("ABC"));
            Assert.IsNull(GetDbaBuildCommand.NormalizeServicePack("SP-1"));
        }
        #endregion

        #region NormalizeCumulativeUpdate
        [TestMethod]
        public void NormalizeCumulativeUpdate_CU5_ReturnsCU5()
        {
            Assert.AreEqual("CU5", GetDbaBuildCommand.NormalizeCumulativeUpdate("CU5"));
        }

        [TestMethod]
        public void NormalizeCumulativeUpdate_JustNumber_ReturnsCUX()
        {
            Assert.AreEqual("CU12", GetDbaBuildCommand.NormalizeCumulativeUpdate("12"));
        }

        [TestMethod]
        public void NormalizeCumulativeUpdate_Zero_ReturnsEmpty()
        {
            Assert.AreEqual("", GetDbaBuildCommand.NormalizeCumulativeUpdate("0"));
            Assert.AreEqual("", GetDbaBuildCommand.NormalizeCumulativeUpdate("CU0"));
        }

        [TestMethod]
        public void NormalizeCumulativeUpdate_NullOrEmpty_ReturnsEmpty()
        {
            Assert.AreEqual("", GetDbaBuildCommand.NormalizeCumulativeUpdate(null));
            Assert.AreEqual("", GetDbaBuildCommand.NormalizeCumulativeUpdate(""));
        }

        [TestMethod]
        public void NormalizeCumulativeUpdate_Invalid_ReturnsNull()
        {
            Assert.IsNull(GetDbaBuildCommand.NormalizeCumulativeUpdate("ABC"));
            Assert.IsNull(GetDbaBuildCommand.NormalizeCumulativeUpdate("CU-1"));
        }
        #endregion

        #region ParseKbNumber
        [TestMethod]
        public void ParseKbNumber_WithKBPrefix_ReturnsNumber()
        {
            Assert.AreEqual("4057119", GetDbaBuildCommand.ParseKbNumber("KB4057119"));
        }

        [TestMethod]
        public void ParseKbNumber_JustNumber_ReturnsNumber()
        {
            Assert.AreEqual("4057119", GetDbaBuildCommand.ParseKbNumber("4057119"));
        }

        [TestMethod]
        public void ParseKbNumber_LowerCase_ReturnsNumber()
        {
            Assert.AreEqual("4057119", GetDbaBuildCommand.ParseKbNumber("kb4057119"));
        }

        [TestMethod]
        public void ParseKbNumber_Invalid_ReturnsNull()
        {
            Assert.IsNull(GetDbaBuildCommand.ParseKbNumber("KBABC"));
            Assert.IsNull(GetDbaBuildCommand.ParseKbNumber(""));
            Assert.IsNull(GetDbaBuildCommand.ParseKbNumber(null));
        }
        #endregion

        #region KBListContains
        [TestMethod]
        public void KBListContains_SingleString_MatchesExact()
        {
            Assert.IsTrue(GetDbaBuildCommand.KBListContains("274329", "274329"));
            Assert.IsFalse(GetDbaBuildCommand.KBListContains("274329", "999999"));
        }

        [TestMethod]
        public void KBListContains_Array_FindsMatch()
        {
            object[] arr = new object[] { "307655", "30754" };
            Assert.IsTrue(GetDbaBuildCommand.KBListContains(arr, "307655"));
            Assert.IsTrue(GetDbaBuildCommand.KBListContains(arr, "30754"));
            Assert.IsFalse(GetDbaBuildCommand.KBListContains(arr, "999999"));
        }

        [TestMethod]
        public void KBListContains_Null_ReturnsFalse()
        {
            Assert.IsFalse(GetDbaBuildCommand.KBListContains(null, "123"));
            Assert.IsFalse(GetDbaBuildCommand.KBListContains("123", null));
        }
        #endregion

        #region SPContains
        [TestMethod]
        public void SPContains_SingleString_MatchesExact()
        {
            Assert.IsTrue(GetDbaBuildCommand.SPContains("RTM", "RTM"));
            Assert.IsTrue(GetDbaBuildCommand.SPContains("SP1", "SP1"));
            Assert.IsFalse(GetDbaBuildCommand.SPContains("SP1", "SP2"));
        }

        [TestMethod]
        public void SPContains_Array_FindsMatch()
        {
            object[] arr = new object[] { "RTM", "LATEST" };
            Assert.IsTrue(GetDbaBuildCommand.SPContains(arr, "RTM"));
            Assert.IsTrue(GetDbaBuildCommand.SPContains(arr, "LATEST"));
            Assert.IsFalse(GetDbaBuildCommand.SPContains(arr, "SP1"));
        }

        [TestMethod]
        public void SPContains_Null_ReturnsFalse()
        {
            Assert.IsFalse(GetDbaBuildCommand.SPContains(null, "RTM"));
            Assert.IsFalse(GetDbaBuildCommand.SPContains("RTM", null));
        }

        [TestMethod]
        public void SPContains_CaseInsensitive()
        {
            Assert.IsTrue(GetDbaBuildCommand.SPContains("rtm", "RTM"));
            Assert.IsTrue(GetDbaBuildCommand.SPContains("SP1", "sp1"));
        }
        #endregion

        #region GetPSObjectProperty
        [TestMethod]
        public void GetPSObjectProperty_ExistingProperty_ReturnsValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("TestProp", "TestValue"));
            string result = GetDbaBuildCommand.GetPSObjectProperty<string>(obj, "TestProp");
            Assert.AreEqual("TestValue", result);
        }

        [TestMethod]
        public void GetPSObjectProperty_NonExistentProperty_ReturnsNull()
        {
            PSObject obj = new PSObject();
            string result = GetDbaBuildCommand.GetPSObjectProperty<string>(obj, "NoSuchProp");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPSObjectProperty_NullInput_ReturnsNull()
        {
            string result = GetDbaBuildCommand.GetPSObjectProperty<string>(null, "TestProp");
            Assert.IsNull(result);
        }
        #endregion

        #region GetPSObjectPropertyRaw
        [TestMethod]
        public void GetPSObjectPropertyRaw_StringProperty_ReturnsString()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "2016"));
            object result = GetDbaBuildCommand.GetPSObjectPropertyRaw(obj, "Name");
            Assert.AreEqual("2016", result);
        }

        [TestMethod]
        public void GetPSObjectPropertyRaw_NullProperty_ReturnsNull()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", null));
            object result = GetDbaBuildCommand.GetPSObjectPropertyRaw(obj, "Name");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPSObjectPropertyRaw_MissingProperty_ReturnsNull()
        {
            PSObject obj = new PSObject();
            object result = GetDbaBuildCommand.GetPSObjectPropertyRaw(obj, "Missing");
            Assert.IsNull(result);
        }
        #endregion
    }
}
