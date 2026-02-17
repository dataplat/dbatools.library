using System;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class TestDbaPathCommandTests
    {
        #region IsArrayInput
        [TestMethod]
        public void IsArrayInput_NullReturnsFalse()
        {
            bool result = TestDbaPathCommand.IsArrayInput(null);
            Assert.IsFalse(result, "Null should not be considered an array");
        }

        [TestMethod]
        public void IsArrayInput_SingleStringReturnsFalse()
        {
            bool result = TestDbaPathCommand.IsArrayInput("C:\\temp");
            Assert.IsFalse(result, "A single string should not be considered an array");
        }

        [TestMethod]
        public void IsArrayInput_StringArrayReturnsTrue()
        {
            bool result = TestDbaPathCommand.IsArrayInput(new string[] { "C:\\temp", "D:\\backup" });
            Assert.IsTrue(result, "A string array should be considered an array");
        }

        [TestMethod]
        public void IsArrayInput_ObjectArrayReturnsTrue()
        {
            bool result = TestDbaPathCommand.IsArrayInput(new object[] { "C:\\temp", "D:\\backup" });
            Assert.IsTrue(result, "An object array should be considered an array");
        }

        [TestMethod]
        public void IsArrayInput_PSObjectWrappedArrayReturnsTrue()
        {
            PSObject wrapped = new PSObject(new string[] { "C:\\temp", "D:\\backup" });
            bool result = TestDbaPathCommand.IsArrayInput(wrapped);
            Assert.IsTrue(result, "A PSObject-wrapped array should be considered an array");
        }

        [TestMethod]
        public void IsArrayInput_PSObjectWrappedStringReturnsFalse()
        {
            PSObject wrapped = new PSObject("C:\\temp");
            bool result = TestDbaPathCommand.IsArrayInput(wrapped);
            Assert.IsFalse(result, "A PSObject-wrapped string should not be considered an array");
        }

        [TestMethod]
        public void IsArrayInput_IntReturnsFalse()
        {
            bool result = TestDbaPathCommand.IsArrayInput(42);
            Assert.IsFalse(result, "An integer should not be considered an array");
        }
        #endregion

        #region ConvertToStringArray
        [TestMethod]
        public void ConvertToStringArray_NullReturnsEmptyArray()
        {
            string[] result = TestDbaPathCommand.ConvertToStringArray(null);
            Assert.IsNotNull(result, "Should return empty array, not null");
            Assert.AreEqual(0, result.Length, "Null input should produce empty array");
        }

        [TestMethod]
        public void ConvertToStringArray_SingleStringReturnsSingleElementArray()
        {
            string[] result = TestDbaPathCommand.ConvertToStringArray("C:\\temp");
            Assert.AreEqual(1, result.Length);
            Assert.AreEqual("C:\\temp", result[0]);
        }

        [TestMethod]
        public void ConvertToStringArray_StringArrayReturnsItself()
        {
            string[] input = new string[] { "C:\\temp", "D:\\backup" };
            string[] result = TestDbaPathCommand.ConvertToStringArray(input);
            Assert.AreEqual(2, result.Length);
            Assert.AreEqual("C:\\temp", result[0]);
            Assert.AreEqual("D:\\backup", result[1]);
        }

        [TestMethod]
        public void ConvertToStringArray_ObjectArrayConvertsToStrings()
        {
            object[] input = new object[] { "C:\\temp", "D:\\backup" };
            string[] result = TestDbaPathCommand.ConvertToStringArray(input);
            Assert.AreEqual(2, result.Length);
            Assert.AreEqual("C:\\temp", result[0]);
            Assert.AreEqual("D:\\backup", result[1]);
        }

        [TestMethod]
        public void ConvertToStringArray_PSObjectWrappedStringUnwraps()
        {
            PSObject wrapped = new PSObject("C:\\temp");
            string[] result = TestDbaPathCommand.ConvertToStringArray(wrapped);
            Assert.AreEqual(1, result.Length);
            Assert.AreEqual("C:\\temp", result[0]);
        }

        [TestMethod]
        public void ConvertToStringArray_ObjectArrayWithPSObjectsUnwraps()
        {
            object[] input = new object[]
            {
                new PSObject("C:\\temp"),
                new PSObject("D:\\backup")
            };
            string[] result = TestDbaPathCommand.ConvertToStringArray(input);
            Assert.AreEqual(2, result.Length);
            Assert.AreEqual("C:\\temp", result[0]);
            Assert.AreEqual("D:\\backup", result[1]);
        }

        [TestMethod]
        public void ConvertToStringArray_ObjectArraySkipsNulls()
        {
            object[] input = new object[] { "C:\\temp", null, "D:\\backup" };
            string[] result = TestDbaPathCommand.ConvertToStringArray(input);
            Assert.AreEqual(2, result.Length, "Null elements should be skipped");
            Assert.AreEqual("C:\\temp", result[0]);
            Assert.AreEqual("D:\\backup", result[1]);
        }

        [TestMethod]
        public void ConvertToStringArray_NonStringObjectConvertsViaToString()
        {
            string[] result = TestDbaPathCommand.ConvertToStringArray(42);
            Assert.AreEqual(1, result.Length);
            Assert.AreEqual("42", result[0]);
        }

        [TestMethod]
        public void ConvertToStringArray_EmptyObjectArrayReturnsEmptyArray()
        {
            object[] input = new object[0];
            string[] result = TestDbaPathCommand.ConvertToStringArray(input);
            Assert.AreEqual(0, result.Length);
        }
        #endregion

        #region BuildXpFileExistQuery
        [TestMethod]
        public void BuildXpFileExistQuery_PathWithSingleQuoteIsEscaped()
        {
            string result = TestDbaPathCommand.BuildXpFileExistQuery("C:\\it's a test");
            Assert.AreEqual("EXEC master.dbo.xp_fileexist N'C:\\it''s a test'", result);
        }

        [TestMethod]
        public void BuildXpFileExistQuery_NormalPathIsUnchanged()
        {
            string result = TestDbaPathCommand.BuildXpFileExistQuery("C:\\temp\\backup.bak");
            Assert.AreEqual("EXEC master.dbo.xp_fileexist N'C:\\temp\\backup.bak'", result);
        }

        [TestMethod]
        public void BuildXpFileExistQuery_MultipleSingleQuotesAllEscaped()
        {
            string result = TestDbaPathCommand.BuildXpFileExistQuery("C:\\'test'\\it's");
            Assert.AreEqual("EXEC master.dbo.xp_fileexist N'C:\\''test''\\it''s'", result);
        }

        [TestMethod]
        public void BuildXpFileExistQuery_UncPathIsHandled()
        {
            string result = TestDbaPathCommand.BuildXpFileExistQuery("\\\\server\\share\\file.bak");
            Assert.AreEqual("EXEC master.dbo.xp_fileexist N'\\\\server\\share\\file.bak'", result);
        }
        #endregion
    }
}
