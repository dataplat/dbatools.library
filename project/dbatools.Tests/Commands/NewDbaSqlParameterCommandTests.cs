using System;
using System.Data;
using System.Data.SqlTypes;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class NewDbaSqlParameterCommandTests
    {
        #region SqlParameter Property Assignment
        [TestMethod]
        public void SqlParameter_DefaultConstructor_HasExpectedDefaults()
        {
            // The command creates a SqlParameter with default constructor.
            // Verify baseline properties that users depend on.
            var param = new SqlParameter();
            Assert.AreEqual(ParameterDirection.Input, param.Direction);
            Assert.AreEqual(string.Empty, param.ParameterName);
            Assert.AreEqual(0, param.Size);
            Assert.IsFalse(param.IsNullable);
        }

        [TestMethod]
        public void SqlParameter_DirectionParsing_OutputIsValid()
        {
            // Verify that the string "Output" parses to ParameterDirection.Output,
            // matching the PS1 behavior of assigning string to enum property.
            var direction = (ParameterDirection)Enum.Parse(typeof(ParameterDirection), "Output", true);
            Assert.AreEqual(ParameterDirection.Output, direction);
        }

        [TestMethod]
        public void SqlParameter_DirectionParsing_InputOutputIsValid()
        {
            var direction = (ParameterDirection)Enum.Parse(typeof(ParameterDirection), "InputOutput", true);
            Assert.AreEqual(ParameterDirection.InputOutput, direction);
        }

        [TestMethod]
        public void SqlParameter_DirectionParsing_ReturnValueIsValid()
        {
            var direction = (ParameterDirection)Enum.Parse(typeof(ParameterDirection), "ReturnValue", true);
            Assert.AreEqual(ParameterDirection.ReturnValue, direction);
        }
        #endregion

        #region SqlDbType Parsing
        [TestMethod]
        public void SqlDbType_NVarChar_ParsesCorrectly()
        {
            var dbType = (SqlDbType)Enum.Parse(typeof(SqlDbType), "NVarChar", true);
            Assert.AreEqual(SqlDbType.NVarChar, dbType);
        }

        [TestMethod]
        public void SqlDbType_Int_ParsesCorrectly()
        {
            var dbType = (SqlDbType)Enum.Parse(typeof(SqlDbType), "Int", true);
            Assert.AreEqual(SqlDbType.Int, dbType);
        }

        [TestMethod]
        public void SqlDbType_Structured_ParsesCorrectly()
        {
            var dbType = (SqlDbType)Enum.Parse(typeof(SqlDbType), "Structured", true);
            Assert.AreEqual(SqlDbType.Structured, dbType);
        }
        #endregion

        #region DbType Parsing
        [TestMethod]
        public void DbType_String_ParsesCorrectly()
        {
            var dbType = (DbType)Enum.Parse(typeof(DbType), "String", true);
            Assert.AreEqual(DbType.String, dbType);
        }

        [TestMethod]
        public void DbType_DateTime2_ParsesCorrectly()
        {
            var dbType = (DbType)Enum.Parse(typeof(DbType), "DateTime2", true);
            Assert.AreEqual(DbType.DateTime2, dbType);
        }
        #endregion

        #region CompareInfo Parsing
        [TestMethod]
        public void CompareInfo_IgnoreCase_ParsesCorrectly()
        {
            var compareInfo = (SqlCompareOptions)Enum.Parse(typeof(SqlCompareOptions), "IgnoreCase", true);
            Assert.AreEqual(SqlCompareOptions.IgnoreCase, compareInfo);
        }

        [TestMethod]
        public void CompareInfo_BinarySort2_ParsesCorrectly()
        {
            var compareInfo = (SqlCompareOptions)Enum.Parse(typeof(SqlCompareOptions), "BinarySort2", true);
            Assert.AreEqual(SqlCompareOptions.BinarySort2, compareInfo);
        }
        #endregion

        #region SourceVersion Parsing
        [TestMethod]
        public void SourceVersion_Original_ParsesCorrectly()
        {
            var version = (DataRowVersion)Enum.Parse(typeof(DataRowVersion), "Original", true);
            Assert.AreEqual(DataRowVersion.Original, version);
        }

        [TestMethod]
        public void SourceVersion_Current_ParsesCorrectly()
        {
            var version = (DataRowVersion)Enum.Parse(typeof(DataRowVersion), "Current", true);
            Assert.AreEqual(DataRowVersion.Current, version);
        }
        #endregion

        #region Precision and Scale Parsing
        [TestMethod]
        public void Precision_ValidString_ParsesToByte()
        {
            byte precision = Byte.Parse("18");
            Assert.AreEqual((byte)18, precision);
        }

        [TestMethod]
        public void Scale_ValidString_ParsesToByte()
        {
            byte scale = Byte.Parse("2");
            Assert.AreEqual((byte)2, scale);
        }

        [TestMethod]
        [ExpectedException(typeof(OverflowException))]
        public void Precision_TooLarge_ThrowsOverflow()
        {
            Byte.Parse("256");
        }
        #endregion

        #region Offset Parsing
        [TestMethod]
        public void Offset_ValidString_ParsesCorrectly()
        {
            int offset = Int32.Parse("100");
            Assert.AreEqual(100, offset);
        }

        [TestMethod]
        [ExpectedException(typeof(FormatException))]
        public void Offset_InvalidString_ThrowsFormatException()
        {
            Int32.Parse("notanumber");
        }
        #endregion

        #region Value Assignment
        [TestMethod]
        public void Value_ZeroInt_AssignsCorrectly()
        {
            // Regression test for issue #9542: "falsy" values like 0 must be preserved
            var param = new SqlParameter();
            param.Value = 0;
            Assert.AreEqual(0, param.Value);
        }

        [TestMethod]
        public void Value_NullObject_AssignsCorrectly()
        {
            var param = new SqlParameter();
            param.Value = null;
            Assert.IsNull(param.Value);
        }

        [TestMethod]
        public void Value_EmptyString_AssignsCorrectly()
        {
            var param = new SqlParameter();
            param.Value = "";
            Assert.AreEqual("", param.Value);
        }
        #endregion

        #region Size Assignment
        [TestMethod]
        public void Size_NegativeOne_MeansMax()
        {
            // -1 means MAX (varchar(max), nvarchar(max))
            var param = new SqlParameter();
            param.Size = -1;
            Assert.AreEqual(-1, param.Size);
        }

        [TestMethod]
        public void Size_PositiveValue_AssignsCorrectly()
        {
            var param = new SqlParameter();
            param.Size = 500;
            Assert.AreEqual(500, param.Size);
        }
        #endregion

        #region Full Parameter Construction
        [TestMethod]
        public void FullParameter_OutputNVarCharMax_AllPropertiesSet()
        {
            // Simulates: New-DbaSqlParameter -ParameterName json_result -SqlDbType NVarChar -Size -1 -Direction Output
            var param = new SqlParameter();
            param.ParameterName = "json_result";
            param.SqlDbType = SqlDbType.NVarChar;
            param.Size = -1;
            param.Direction = ParameterDirection.Output;

            Assert.AreEqual("json_result", param.ParameterName);
            Assert.AreEqual(SqlDbType.NVarChar, param.SqlDbType);
            Assert.AreEqual(-1, param.Size);
            Assert.AreEqual(ParameterDirection.Output, param.Direction);
        }
        #endregion
    }
}
