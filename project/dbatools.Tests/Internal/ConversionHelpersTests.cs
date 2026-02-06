using System;
using System.Linq;
using System.Security;
using Dataplat.Dbatools.Internal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Tests.Internal
{
    [TestClass]
    public class ConversionHelpersTests
    {
        #region ByteToHex
        [TestMethod]
        public void ByteToHex_NullInput_ReturnsPrefix()
        {
            Assert.AreEqual("0x", ConversionHelpers.ByteToHex(null));
        }

        [TestMethod]
        public void ByteToHex_EmptyArray_ReturnsPrefix()
        {
            Assert.AreEqual("0x", ConversionHelpers.ByteToHex(new byte[0]));
        }

        [TestMethod]
        public void ByteToHex_SingleByte_ReturnsHex()
        {
            Assert.AreEqual("0xFF", ConversionHelpers.ByteToHex(new byte[] { 0xFF }));
        }

        [TestMethod]
        public void ByteToHex_MultipleBytes_ReturnsHex()
        {
            Assert.AreEqual("0x0102FF", ConversionHelpers.ByteToHex(new byte[] { 0x01, 0x02, 0xFF }));
        }
        #endregion

        #region HexToByte
        [TestMethod]
        public void HexToByte_NullInput_ReturnsEmpty()
        {
            Assert.AreEqual(0, ConversionHelpers.HexToByte(null).Length);
        }

        [TestMethod]
        public void HexToByte_EmptyInput_ReturnsEmpty()
        {
            Assert.AreEqual(0, ConversionHelpers.HexToByte("").Length);
        }

        [TestMethod]
        public void HexToByte_WithPrefix_ParsesCorrectly()
        {
            byte[] result = ConversionHelpers.HexToByte("0xFF");
            Assert.AreEqual(1, result.Length);
            Assert.AreEqual(0xFF, result[0]);
        }

        [TestMethod]
        public void HexToByte_WithoutPrefix_ParsesCorrectly()
        {
            byte[] result = ConversionHelpers.HexToByte("FF01");
            Assert.AreEqual(2, result.Length);
            Assert.AreEqual(0xFF, result[0]);
            Assert.AreEqual(0x01, result[1]);
        }

        [TestMethod]
        public void HexToByte_OddLength_PadsCorrectly()
        {
            byte[] result = ConversionHelpers.HexToByte("FFF");
            Assert.AreEqual(2, result.Length);
            Assert.AreEqual(0x0F, result[0]);
            Assert.AreEqual(0xFF, result[1]);
        }
        #endregion

        #region ByteToHex/HexToByte roundtrip
        [TestMethod]
        public void HexRoundtrip_PreservesData()
        {
            byte[] original = new byte[] { 0x00, 0x42, 0xAB, 0xCD, 0xFF };
            string hex = ConversionHelpers.ByteToHex(original);
            byte[] roundtrip = ConversionHelpers.HexToByte(hex);
            CollectionAssert.AreEqual(original, roundtrip);
        }
        #endregion

        #region ToJsDate
        [TestMethod]
        public void ToJsDate_ConvertsCorrectly()
        {
            var date = new DateTime(2024, 3, 15, 14, 30, 45);
            // JS months are 0-based, so March = 2
            Assert.AreEqual("new Date(2024, 2, 15, 14, 30, 45)", ConversionHelpers.ToJsDate(date));
        }

        [TestMethod]
        public void ToJsDate_January_ZeroBased()
        {
            var date = new DateTime(2024, 1, 1, 0, 0, 0);
            Assert.AreEqual("new Date(2024, 0, 1, 0, 0, 0)", ConversionHelpers.ToJsDate(date));
        }

        [TestMethod]
        public void ToJsDate_December_IsEleven()
        {
            var date = new DateTime(2024, 12, 31, 23, 59, 59);
            Assert.AreEqual("new Date(2024, 11, 31, 23, 59, 59)", ConversionHelpers.ToJsDate(date));
        }
        #endregion

        #region ConvertLSN
        [TestMethod]
        public void ConvertLSN_NullInput_ReturnsNull()
        {
            Assert.IsNull(ConversionHelpers.ConvertLSN(null));
        }

        [TestMethod]
        public void ConvertLSN_EmptyInput_ReturnsNull()
        {
            Assert.IsNull(ConversionHelpers.ConvertLSN(""));
        }

        [TestMethod]
        public void ConvertLSN_InvalidInput_ReturnsNull()
        {
            Assert.IsNull(ConversionHelpers.ConvertLSN("not-an-lsn"));
        }

        [TestMethod]
        public void ConvertLSN_HexFormat_ReturnsBoth()
        {
            string[] result = ConversionHelpers.ConvertLSN("0000002f:000044aa:002b");
            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Length);
            Assert.AreEqual("0000002f:000044aa:002b", result[0]);
            // Verify numeric is non-empty and numeric
            Assert.IsFalse(String.IsNullOrEmpty(result[1]));
        }

        [TestMethod]
        public void ConvertLSN_HexToNumeric_CorrectValue()
        {
            // 0000002f = 47, 000044aa = 17578, 002b = 43
            // Numeric format: {part1}{part2 padded to 10}{part3 padded to 5}
            string[] result = ConversionHelpers.ConvertLSN("0000002f:000044aa:002b");
            Assert.IsNotNull(result);
            Assert.AreEqual("47000001757800043", result[1]);
        }

        [TestMethod]
        public void ConvertLSN_NumericFormat_ReturnsBoth()
        {
            // Numeric -> Hex: last 5 chars = section3, prev 9 = section2, rest = section1
            // "350000000005600001": section1="3500"->0xDAC, section2="000000056"->0x38, section3="00001"->0x1
            string[] result = ConversionHelpers.ConvertLSN("350000000005600001");
            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Length);
            Assert.AreEqual("350000000005600001", result[1]);
            Assert.AreEqual("00000dac:00000038:0001", result[0]);
        }
        #endregion

        #region GetRandomPassword
        [TestMethod]
        public void GetRandomPassword_DefaultLength_Returns15()
        {
            string password = ConversionHelpers.GetRandomPassword();
            Assert.AreEqual(15, password.Length);
        }

        [TestMethod]
        public void GetRandomPassword_CustomLength_ReturnsCorrectLength()
        {
            string password = ConversionHelpers.GetRandomPassword(25);
            Assert.AreEqual(25, password.Length);
        }

        [TestMethod]
        public void GetRandomPassword_MinLength_Returns4()
        {
            // Minimum enforced at 4
            string password = ConversionHelpers.GetRandomPassword(1);
            Assert.AreEqual(4, password.Length);
        }

        [TestMethod]
        public void GetRandomPassword_ContainsAllCharGroups()
        {
            // Generate several passwords and check they contain all character groups
            for (int i = 0; i < 10; i++)
            {
                string password = ConversionHelpers.GetRandomPassword(20);
                Assert.IsTrue(password.Any(c => Char.IsUpper(c)), "Should contain uppercase");
                Assert.IsTrue(password.Any(c => Char.IsLower(c)), "Should contain lowercase");
                Assert.IsTrue(password.Any(c => Char.IsDigit(c)), "Should contain digit");
                Assert.IsTrue(password.Any(c => !Char.IsLetterOrDigit(c)), "Should contain special char");
            }
        }

        [TestMethod]
        public void GetRandomPassword_TwoCallsAreDifferent()
        {
            string pw1 = ConversionHelpers.GetRandomPassword(20);
            string pw2 = ConversionHelpers.GetRandomPassword(20);
            Assert.AreNotEqual(pw1, pw2, "Consecutive passwords should differ");
        }
        #endregion

        #region HideConnectionString
        [TestMethod]
        public void HideConnectionString_NullInput_ReturnsNull()
        {
            Assert.IsNull(ConversionHelpers.HideConnectionString(null));
        }

        [TestMethod]
        public void HideConnectionString_EmptyInput_ReturnsEmpty()
        {
            Assert.AreEqual("", ConversionHelpers.HideConnectionString(""));
        }

        [TestMethod]
        public void HideConnectionString_WithPassword_MasksIt()
        {
            string input = "Server=sql01;Database=master;User Id=sa;Password=Secret123";
            string result = ConversionHelpers.HideConnectionString(input);
            Assert.IsFalse(result.Contains("Secret123"), "Password should be masked");
            Assert.IsTrue(result.Contains("********"), "Should contain mask");
        }

        [TestMethod]
        public void HideConnectionString_NoPassword_PreservesString()
        {
            string input = "Server=sql01;Database=master;Integrated Security=true";
            string result = ConversionHelpers.HideConnectionString(input);
            Assert.IsTrue(result.Contains("sql01"), "Server should be preserved");
        }

        [TestMethod]
        public void HideConnectionString_InvalidFormat_ReturnsFailMessage()
        {
            string result = ConversionHelpers.HideConnectionString("not a connection string {{}}}}");
            Assert.AreEqual("Failed to mask the connection string", result);
        }
        #endregion

        #region FromSecureString
        [TestMethod]
        public void FromSecureString_NullInput_ReturnsNull()
        {
            Assert.IsNull(ConversionHelpers.FromSecureString(null));
        }

        [TestMethod]
        public void FromSecureString_ValidSecureString_ReturnsPlaintext()
        {
            var ss = new SecureString();
            foreach (char c in "TestPassword")
                ss.AppendChar(c);
            ss.MakeReadOnly();

            Assert.AreEqual("TestPassword", ConversionHelpers.FromSecureString(ss));
        }
        #endregion

        #region GetPasswordHash
        [TestMethod]
        public void GetPasswordHash_NullPassword_ReturnsNull()
        {
            Assert.IsNull(ConversionHelpers.GetPasswordHash(null, 11));
        }

        [TestMethod]
        public void GetPasswordHash_Sql2012_UsesSha512()
        {
            byte[] salt = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            string hash = ConversionHelpers.GetPasswordHash("test", 11, salt);
            Assert.IsNotNull(hash);
            Assert.IsTrue(hash.StartsWith("0x0200"), "SQL 2012+ should use 0200 prefix");
            Assert.IsTrue(hash.Contains("01020304"), "Should contain the salt");
        }

        [TestMethod]
        public void GetPasswordHash_Sql2008_UsesSha1()
        {
            byte[] salt = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            string hash = ConversionHelpers.GetPasswordHash("test", 10, salt);
            Assert.IsNotNull(hash);
            Assert.IsTrue(hash.StartsWith("0x0100"), "SQL 2008 should use 0100 prefix");
        }

        [TestMethod]
        public void GetPasswordHash_SameInput_SameOutput()
        {
            byte[] salt = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
            string hash1 = ConversionHelpers.GetPasswordHash("password", 14, salt);
            string hash2 = ConversionHelpers.GetPasswordHash("password", 14, salt);
            Assert.AreEqual(hash1, hash2, "Same password+salt should produce same hash");
        }
        #endregion
    }
}
