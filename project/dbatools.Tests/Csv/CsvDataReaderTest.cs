using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Csv;
using Dataplat.Dbatools.Csv.Reader;

namespace Dataplat.Dbatools.Csv.Tests
{
    [TestClass]
    public class CsvDataReaderTest
    {
        #region Basic Reading Tests

        [TestMethod]
        public void TestBasicReading()
        {
            string csv = "Name,Age,City\nJohn,30,New York\nJane,25,Boston";
            using (var reader = CreateReaderFromString(csv))
            {
                Assert.AreEqual(3, reader.FieldCount);
                Assert.AreEqual("Name", reader.GetName(0));
                Assert.AreEqual("Age", reader.GetName(1));
                Assert.AreEqual("City", reader.GetName(2));

                Assert.IsTrue(reader.Read());
                Assert.AreEqual("John", reader.GetString(0));
                Assert.AreEqual("30", reader.GetString(1));
                Assert.AreEqual("New York", reader.GetString(2));

                Assert.IsTrue(reader.Read());
                Assert.AreEqual("Jane", reader.GetString(0));
                Assert.AreEqual("25", reader.GetString(1));
                Assert.AreEqual("Boston", reader.GetString(2));

                Assert.IsFalse(reader.Read());
            }
        }

        [TestMethod]
        public void TestNoHeaderRow()
        {
            string csv = "John,30,New York\nJane,25,Boston";
            var options = new CsvReaderOptions { HasHeaderRow = false };

            using (var reader = CreateReaderFromString(csv, options))
            {
                Assert.IsTrue(reader.Read());
                Assert.AreEqual(3, reader.FieldCount);
                Assert.AreEqual("Column0", reader.GetName(0));
                Assert.AreEqual("John", reader.GetString(0));
            }
        }

        [TestMethod]
        public void TestNoHeaderRowSetColumnTypeBeforeRead()
        {
            // This tests the fix for the NoHeaderRow issue where SetColumnType
            // was failing because columns weren't created until Read() was called.
            string csv = "John,30,New York\nJane,25,Boston";
            var options = new CsvReaderOptions { HasHeaderRow = false };

            using (var reader = CreateReaderFromString(csv, options))
            {
                // SetColumnType should work BEFORE Read() is called
                // This requires columns to be initialized during Initialize()
                reader.SetColumnType("Column0", typeof(string));
                reader.SetColumnType("Column1", typeof(int));
                reader.SetColumnType("Column2", typeof(string));

                Assert.IsTrue(reader.Read());
                Assert.AreEqual(3, reader.FieldCount);
                Assert.AreEqual("Column0", reader.GetName(0));
                Assert.AreEqual("John", reader.GetString(0));
                Assert.AreEqual(30, reader.GetInt32(1));  // Converted to int
                Assert.AreEqual("New York", reader.GetString(2));

                Assert.IsTrue(reader.Read());
                Assert.AreEqual("Jane", reader.GetString(0));
                Assert.AreEqual(25, reader.GetInt32(1));  // Converted to int
                Assert.AreEqual("Boston", reader.GetString(2));

                Assert.IsFalse(reader.Read());
            }
        }

        [TestMethod]
        public void TestNoHeaderRowHasColumnBeforeRead()
        {
            // Test that HasColumn works before Read() when HasHeaderRow = false
            string csv = "John,30,New York\nJane,25,Boston";
            var options = new CsvReaderOptions { HasHeaderRow = false };

            using (var reader = CreateReaderFromString(csv, options))
            {
                // HasColumn should work BEFORE Read() is called
                Assert.IsTrue(reader.HasColumn("Column0"));
                Assert.IsTrue(reader.HasColumn("Column1"));
                Assert.IsTrue(reader.HasColumn("Column2"));
                Assert.IsFalse(reader.HasColumn("Column3"));

                // FieldCount should also work
                Assert.AreEqual(3, reader.FieldCount);

                // GetFieldHeaders should work
                var headers = reader.GetFieldHeaders();
                Assert.AreEqual(3, headers.Length);
                Assert.AreEqual("Column0", headers[0]);
                Assert.AreEqual("Column1", headers[1]);
                Assert.AreEqual("Column2", headers[2]);
            }
        }

        [TestMethod]
        public void TestEmptyFile()
        {
            string csv = "";
            using (var reader = CreateReaderFromString(csv))
            {
                Assert.IsFalse(reader.Read());
            }
        }

        [TestMethod]
        public void TestHeaderOnly()
        {
            string csv = "Name,Age,City";
            using (var reader = CreateReaderFromString(csv))
            {
                Assert.AreEqual(3, reader.FieldCount);
                Assert.IsFalse(reader.Read());
            }
        }

        #endregion

        #region Delimiter Tests

        [TestMethod]
        public void TestTabDelimiter()
        {
            string csv = "Name\tAge\tCity\nJohn\t30\tNew York";
            var options = new CsvReaderOptions { Delimiter = "\t" };

            using (var reader = CreateReaderFromString(csv, options))
            {
                Assert.AreEqual(3, reader.FieldCount);
                Assert.IsTrue(reader.Read());
                Assert.AreEqual("John", reader.GetString(0));
                Assert.AreEqual("30", reader.GetString(1));
                Assert.AreEqual("New York", reader.GetString(2));
            }
        }

        [TestMethod]
        public void TestPipeDelimiter()
        {
            string csv = "Name|Age|City\nJohn|30|New York";
            var options = new CsvReaderOptions { Delimiter = "|" };

            using (var reader = CreateReaderFromString(csv, options))
            {
                Assert.AreEqual(3, reader.FieldCount);
                Assert.IsTrue(reader.Read());
                Assert.AreEqual("John", reader.GetString(0));
            }
        }

        [TestMethod]
        public void TestMultiCharacterDelimiter()
        {
            // Addresses issue #6488: Add custom multi-character delimiter support
            string csv = "Name^!Age^!City\nJohn^!30^!New York";
            var options = new CsvReaderOptions { Delimiter = "^!" };

            using (var reader = CreateReaderFromString(csv, options))
            {
                Assert.AreEqual(3, reader.FieldCount);
                Assert.IsTrue(reader.Read());
                Assert.AreEqual("John", reader.GetString(0));
                Assert.AreEqual("30", reader.GetString(1));
                Assert.AreEqual("New York", reader.GetString(2));
            }
        }

        #endregion

        #region Quoting Tests

        [TestMethod]
        public void TestQuotedFields()
        {
            string csv = "Name,Description\nJohn,\"Hello, World\"\nJane,\"Line1\nLine2\"";
            using (var reader = CreateReaderFromString(csv))
            {
                Assert.IsTrue(reader.Read());
                Assert.AreEqual("Hello, World", reader.GetString(1));

                Assert.IsTrue(reader.Read());
                Assert.AreEqual("Line1\nLine2", reader.GetString(1));
            }
        }

        [TestMethod]
        public void TestEscapedQuotes()
        {
            string csv = "Name,Quote\nJohn,\"He said \"\"Hello\"\"\"";
            using (var reader = CreateReaderFromString(csv))
            {
                Assert.IsTrue(reader.Read());
                Assert.AreEqual("He said \"Hello\"", reader.GetString(1));
            }
        }

        #endregion

        #region Skip Rows Tests

        [TestMethod]
        public void TestSkipRows()
        {
            // Addresses issue #7173: Import-DbaCsv "FirstRow" Feature
            string csv = "This is a header comment\nAnother comment line\nName,Age\nJohn,30";
            var options = new CsvReaderOptions { SkipRows = 2 };

            using (var reader = CreateReaderFromString(csv, options))
            {
                Assert.AreEqual(2, reader.FieldCount);
                Assert.AreEqual("Name", reader.GetName(0));
                Assert.IsTrue(reader.Read());
                Assert.AreEqual("John", reader.GetString(0));
            }
        }

        [TestMethod]
        public void TestSkipEmptyLines()
        {
            string csv = "Name,Age\n\nJohn,30\n\nJane,25\n";
            var options = new CsvReaderOptions { SkipEmptyLines = true };

            using (var reader = CreateReaderFromString(csv, options))
            {
                Assert.IsTrue(reader.Read());
                Assert.AreEqual("John", reader.GetString(0));
                Assert.IsTrue(reader.Read());
                Assert.AreEqual("Jane", reader.GetString(0));
                Assert.IsFalse(reader.Read());
            }
        }

        [TestMethod]
        public void TestCommentLines()
        {
            string csv = "Name,Age\n# This is a comment\nJohn,30";
            var options = new CsvReaderOptions { Comment = '#' };

            using (var reader = CreateReaderFromString(csv, options))
            {
                Assert.IsTrue(reader.Read());
                Assert.AreEqual("John", reader.GetString(0));
                Assert.IsFalse(reader.Read());
            }
        }

        #endregion

        #region Type Conversion Tests

        [TestMethod]
        public void TestBooleanConversion()
        {
            // Addresses issue #8409: 1 and 0 should convert to boolean
            string csv = "Name,Active\nJohn,1\nJane,0\nBob,true\nAlice,false";
            var options = new CsvReaderOptions
            {
                ColumnTypes = new System.Collections.Generic.Dictionary<string, Type>
                {
                    { "Active", typeof(bool) }
                }
            };

            using (var reader = CreateReaderFromString(csv, options))
            {
                Assert.IsTrue(reader.Read());
                Assert.AreEqual(true, reader.GetBoolean(1));

                Assert.IsTrue(reader.Read());
                Assert.AreEqual(false, reader.GetBoolean(1));

                Assert.IsTrue(reader.Read());
                Assert.AreEqual(true, reader.GetBoolean(1));

                Assert.IsTrue(reader.Read());
                Assert.AreEqual(false, reader.GetBoolean(1));
            }
        }

        [TestMethod]
        public void TestGuidConversion()
        {
            // Addresses issue #9433: GUID target column support
            string csv = "Id,Name\n550e8400-e29b-41d4-a716-446655440000,Test";
            var options = new CsvReaderOptions
            {
                ColumnTypes = new System.Collections.Generic.Dictionary<string, Type>
                {
                    { "Id", typeof(Guid) }
                }
            };

            using (var reader = CreateReaderFromString(csv, options))
            {
                Assert.IsTrue(reader.Read());
                Guid expected = Guid.Parse("550e8400-e29b-41d4-a716-446655440000");
                Assert.AreEqual(expected, reader.GetGuid(0));
            }
        }

        [TestMethod]
        public void TestDateTimeConversion()
        {
            string csv = "Name,Created\nJohn,2024-01-15 10:30:00";
            var options = new CsvReaderOptions
            {
                ColumnTypes = new System.Collections.Generic.Dictionary<string, Type>
                {
                    { "Created", typeof(DateTime) }
                }
            };

            using (var reader = CreateReaderFromString(csv, options))
            {
                Assert.IsTrue(reader.Read());
                DateTime dt = reader.GetDateTime(1);
                Assert.AreEqual(2024, dt.Year);
                Assert.AreEqual(1, dt.Month);
                Assert.AreEqual(15, dt.Day);
            }
        }

        [TestMethod]
        public void TestNumericConversion()
        {
            string csv = "Int,Long,Double,Decimal\n42,9999999999,3.14159,123.45";
            var options = new CsvReaderOptions
            {
                ColumnTypes = new System.Collections.Generic.Dictionary<string, Type>
                {
                    { "Int", typeof(int) },
                    { "Long", typeof(long) },
                    { "Double", typeof(double) },
                    { "Decimal", typeof(decimal) }
                }
            };

            using (var reader = CreateReaderFromString(csv, options))
            {
                Assert.IsTrue(reader.Read());
                Assert.AreEqual(42, reader.GetInt32(0));
                Assert.AreEqual(9999999999L, reader.GetInt64(1));
                Assert.AreEqual(3.14159, reader.GetDouble(2), 0.00001);
                Assert.AreEqual(123.45m, reader.GetDecimal(3));
            }
        }

        #endregion

        #region Static Column Tests

        [TestMethod]
        public void TestStaticColumns()
        {
            // Addresses issue #6676: Static column mappings for storing/tagging metadata
            string csv = "Name,Age\nJohn,30\nJane,25";
            var options = new CsvReaderOptions
            {
                StaticColumns = new System.Collections.Generic.List<StaticColumn>
                {
                    new StaticColumn("FileName", "test.csv"),
                    new StaticColumn("ImportDate", DateTime.Today, typeof(DateTime))
                }
            };

            using (var reader = CreateReaderFromString(csv, options))
            {
                Assert.AreEqual(4, reader.FieldCount);
                Assert.AreEqual("Name", reader.GetName(0));
                Assert.AreEqual("Age", reader.GetName(1));
                Assert.AreEqual("FileName", reader.GetName(2));
                Assert.AreEqual("ImportDate", reader.GetName(3));

                Assert.IsTrue(reader.Read());
                Assert.AreEqual("John", reader.GetString(0));
                Assert.AreEqual("test.csv", reader.GetValue(2));
                Assert.AreEqual(DateTime.Today, reader.GetValue(3));
            }
        }

        [TestMethod]
        public void TestRowNumberStaticColumn()
        {
            string csv = "Name\nJohn\nJane\nBob";
            var options = new CsvReaderOptions
            {
                StaticColumns = new System.Collections.Generic.List<StaticColumn>
                {
                    StaticColumn.RowNumber("RowNum")
                }
            };

            using (var reader = CreateReaderFromString(csv, options))
            {
                Assert.IsTrue(reader.Read());
                Assert.AreEqual(1L, reader.GetValue(1));

                Assert.IsTrue(reader.Read());
                Assert.AreEqual(2L, reader.GetValue(1));

                Assert.IsTrue(reader.Read());
                Assert.AreEqual(3L, reader.GetValue(1));
            }
        }

        #endregion

        #region Error Handling Tests

        [TestMethod]
        public void TestParseErrorCollection()
        {
            // Addresses issue #6899: View/log bad rows during import
            string csv = "Name,Age\nJohn,30\nBadRow\nJane,25";
            var options = new CsvReaderOptions
            {
                CollectParseErrors = true,
                ParseErrorAction = CsvParseErrorAction.AdvanceToNextLine
            };

            using (var reader = CreateReaderFromString(csv, options))
            {
                int rowCount = 0;
                while (reader.Read())
                {
                    rowCount++;
                }

                // Should have read 2 valid rows (John and Jane)
                Assert.AreEqual(2, rowCount);
            }
        }

        [TestMethod]
        public void TestNullValue()
        {
            string csv = "Name,Age\nJohn,NULL\nJane,25";
            var options = new CsvReaderOptions { NullValue = "NULL" };

            using (var reader = CreateReaderFromString(csv, options))
            {
                Assert.IsTrue(reader.Read());
                Assert.IsTrue(reader.IsDBNull(1));

                Assert.IsTrue(reader.Read());
                Assert.IsFalse(reader.IsDBNull(1));
            }
        }

        #endregion

        #region Trimming Tests

        [TestMethod]
        public void TestTrimAll()
        {
            string csv = "Name,Age\n  John  ,  30  ";
            var options = new CsvReaderOptions { TrimmingOptions = ValueTrimmingOptions.All };

            using (var reader = CreateReaderFromString(csv, options))
            {
                Assert.IsTrue(reader.Read());
                Assert.AreEqual("John", reader.GetString(0));
                Assert.AreEqual("30", reader.GetString(1));
            }
        }

        [TestMethod]
        public void TestNoTrimming()
        {
            string csv = "Name,Age\n  John  ,  30  ";
            var options = new CsvReaderOptions { TrimmingOptions = ValueTrimmingOptions.None };

            using (var reader = CreateReaderFromString(csv, options))
            {
                Assert.IsTrue(reader.Read());
                Assert.AreEqual("  John  ", reader.GetString(0));
                Assert.AreEqual("  30  ", reader.GetString(1));
            }
        }

        #endregion

        #region IDataReader Tests

        [TestMethod]
        public void TestGetOrdinal()
        {
            string csv = "Name,Age,City\nJohn,30,NYC";
            using (var reader = CreateReaderFromString(csv))
            {
                Assert.AreEqual(0, reader.GetOrdinal("Name"));
                Assert.AreEqual(1, reader.GetOrdinal("Age"));
                Assert.AreEqual(2, reader.GetOrdinal("City"));
            }
        }

        [TestMethod]
        public void TestGetOrdinalCaseInsensitive()
        {
            string csv = "Name,Age,City\nJohn,30,NYC";
            using (var reader = CreateReaderFromString(csv))
            {
                Assert.AreEqual(0, reader.GetOrdinal("name"));
                Assert.AreEqual(0, reader.GetOrdinal("NAME"));
            }
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void TestGetOrdinalNotFound()
        {
            string csv = "Name,Age\nJohn,30";
            using (var reader = CreateReaderFromString(csv))
            {
                reader.GetOrdinal("NonExistent");
            }
        }

        [TestMethod]
        public void TestGetValues()
        {
            string csv = "Name,Age\nJohn,30";
            using (var reader = CreateReaderFromString(csv))
            {
                Assert.IsTrue(reader.Read());

                object[] values = new object[2];
                int count = reader.GetValues(values);

                Assert.AreEqual(2, count);
                Assert.AreEqual("John", values[0]);
                Assert.AreEqual("30", values[1]);
            }
        }

        [TestMethod]
        public void TestGetSchemaTable()
        {
            string csv = "Name,Age\nJohn,30";
            using (var reader = CreateReaderFromString(csv))
            {
                var schema = reader.GetSchemaTable();

                Assert.AreEqual(2, schema.Rows.Count);
                Assert.AreEqual("Name", schema.Rows[0]["ColumnName"]);
                Assert.AreEqual("Age", schema.Rows[1]["ColumnName"]);
                Assert.AreEqual(typeof(string), schema.Rows[0]["DataType"]);
            }
        }

        #endregion

        #region Null vs Empty Tests

        [TestMethod]
        public void TestDistinguishEmptyFromNull_WhenEnabled()
        {
            // Addresses LumenWorks issue #68
            // Unquoted empty = null, quoted empty = empty string
            string csv = "A,B,C\n1,,3\n4,\"\",6";
            var options = new CsvReaderOptions { DistinguishEmptyFromNull = true };

            using (var reader = CreateReaderFromString(csv, options))
            {
                // Row 1: 1,,3 - middle field is unquoted empty -> should be DBNull
                Assert.IsTrue(reader.Read());
                Assert.AreEqual("1", reader.GetString(0));
                Assert.IsTrue(reader.IsDBNull(1), "Unquoted empty should be DBNull");
                Assert.AreEqual("3", reader.GetString(2));

                // Row 2: 4,"",6 - middle field is quoted empty -> should be empty string
                Assert.IsTrue(reader.Read());
                Assert.AreEqual("4", reader.GetString(0));
                Assert.IsFalse(reader.IsDBNull(1), "Quoted empty should NOT be DBNull");
                Assert.AreEqual("", reader.GetValue(1));
                Assert.AreEqual("6", reader.GetString(2));
            }
        }

        [TestMethod]
        public void TestDistinguishEmptyFromNull_WhenDisabled()
        {
            // Default behavior - both become DBNull
            string csv = "A,B,C\n1,,3\n4,\"\",6";
            var options = new CsvReaderOptions { DistinguishEmptyFromNull = false };

            using (var reader = CreateReaderFromString(csv, options))
            {
                Assert.IsTrue(reader.Read());
                Assert.IsTrue(reader.IsDBNull(1), "Unquoted empty should be DBNull");

                Assert.IsTrue(reader.Read());
                Assert.IsTrue(reader.IsDBNull(1), "Quoted empty should also be DBNull when DistinguishEmptyFromNull is false");
            }
        }

        #endregion

        #region Duplicate Header Tests

        [TestMethod]
        public void TestDuplicateHeaders_ThrowException()
        {
            // Default behavior should throw
            string csv = "Name,Age,Name\nJohn,30,Smith";
            var options = new CsvReaderOptions { DuplicateHeaderBehavior = DuplicateHeaderBehavior.ThrowException };

            Assert.ThrowsException<CsvParseException>(() =>
            {
                using (var reader = CreateReaderFromString(csv, options))
                {
                    // Accessing FieldCount triggers header reading
                    var _ = reader.FieldCount;
                }
            });
        }

        [TestMethod]
        public void TestDuplicateHeaders_Rename()
        {
            // Addresses LumenWorks issue #39
            string csv = "Name,Age,Name,Name\nJohn,30,Smith,Jr";
            var options = new CsvReaderOptions { DuplicateHeaderBehavior = DuplicateHeaderBehavior.Rename };

            using (var reader = CreateReaderFromString(csv, options))
            {
                Assert.AreEqual(4, reader.FieldCount);
                Assert.AreEqual("Name", reader.GetName(0));
                Assert.AreEqual("Age", reader.GetName(1));
                Assert.AreEqual("Name_2", reader.GetName(2));
                Assert.AreEqual("Name_3", reader.GetName(3));

                Assert.IsTrue(reader.Read());
                Assert.AreEqual("John", reader.GetString(0));
                Assert.AreEqual("Smith", reader.GetString(2));
                Assert.AreEqual("Jr", reader.GetString(3));
            }
        }

        [TestMethod]
        public void TestDuplicateHeaders_UseFirstOccurrence()
        {
            string csv = "Name,Age,Name\nJohn,30,Smith";
            var options = new CsvReaderOptions
            {
                DuplicateHeaderBehavior = DuplicateHeaderBehavior.UseFirstOccurrence,
                MismatchedFieldAction = MismatchedFieldAction.TruncateExtra
            };

            using (var reader = CreateReaderFromString(csv, options))
            {
                Assert.AreEqual(2, reader.FieldCount);
                Assert.AreEqual("Name", reader.GetName(0));
                Assert.AreEqual("Age", reader.GetName(1));

                Assert.IsTrue(reader.Read());
                Assert.AreEqual("John", reader.GetString(0));
                Assert.AreEqual("30", reader.GetString(1));
            }
        }

        #endregion

        #region Empty Header Tests

        [TestMethod]
        public void TestEmptyHeader_GeneratesDefaultName()
        {
            // LumenWorks compatibility: empty headers become Column#
            // Reproduces the issue from dbatools where Import-DbaCsv failed
            // with empty headers causing SQL errors
            string csv = ",ValidHeader\nValue1,Value2";

            using (var reader = CreateReaderFromString(csv))
            {
                Assert.AreEqual(2, reader.FieldCount);
                Assert.AreEqual("Column0", reader.GetName(0));  // Empty header -> Column0
                Assert.AreEqual("ValidHeader", reader.GetName(1));

                Assert.IsTrue(reader.Read());
                Assert.AreEqual("Value1", reader.GetString(0));
                Assert.AreEqual("Value2", reader.GetString(1));
            }
        }

        [TestMethod]
        public void TestMultipleEmptyHeaders_GeneratesUniqueNames()
        {
            // Multiple empty headers should each get a unique name based on their index
            string csv = ",,ValidHeader,\nA,B,C,D";

            using (var reader = CreateReaderFromString(csv))
            {
                Assert.AreEqual(4, reader.FieldCount);
                Assert.AreEqual("Column0", reader.GetName(0));
                Assert.AreEqual("Column1", reader.GetName(1));
                Assert.AreEqual("ValidHeader", reader.GetName(2));
                Assert.AreEqual("Column3", reader.GetName(3));

                Assert.IsTrue(reader.Read());
                Assert.AreEqual("A", reader.GetString(0));
                Assert.AreEqual("B", reader.GetString(1));
                Assert.AreEqual("C", reader.GetString(2));
                Assert.AreEqual("D", reader.GetString(3));
            }
        }

        [TestMethod]
        public void TestWhitespaceOnlyHeader_GeneratesDefaultName()
        {
            // Whitespace-only headers should also be treated as empty
            string csv = "   ,ValidHeader\nValue1,Value2";

            using (var reader = CreateReaderFromString(csv))
            {
                Assert.AreEqual(2, reader.FieldCount);
                Assert.AreEqual("Column0", reader.GetName(0));  // Whitespace -> Column0
                Assert.AreEqual("ValidHeader", reader.GetName(1));
            }
        }

        [TestMethod]
        public void TestCustomDefaultHeaderName()
        {
            // Users can customize the default header name prefix
            string csv = ",ValidHeader\nValue1,Value2";
            var options = new CsvReaderOptions { DefaultHeaderName = "Field" };

            using (var reader = CreateReaderFromString(csv, options))
            {
                Assert.AreEqual(2, reader.FieldCount);
                Assert.AreEqual("Field0", reader.GetName(0));  // Custom prefix
                Assert.AreEqual("ValidHeader", reader.GetName(1));
            }
        }

        [TestMethod]
        public void TestEmptyHeaderWithTrimming()
        {
            // When trimming is enabled, whitespace headers should still become Column#
            string csv = "  ,  ValidHeader  \nValue1,Value2";
            var options = new CsvReaderOptions { TrimmingOptions = ValueTrimmingOptions.All };

            using (var reader = CreateReaderFromString(csv, options))
            {
                Assert.AreEqual(2, reader.FieldCount);
                Assert.AreEqual("Column0", reader.GetName(0));  // Trimmed empty -> Column0
                Assert.AreEqual("ValidHeader", reader.GetName(1));  // Trimmed
            }
        }

        [TestMethod]
        public void TestEmptyHeaderInMiddle()
        {
            // Empty header in the middle of other headers
            string csv = "First,,Last\nA,B,C";

            using (var reader = CreateReaderFromString(csv))
            {
                Assert.AreEqual(3, reader.FieldCount);
                Assert.AreEqual("First", reader.GetName(0));
                Assert.AreEqual("Column1", reader.GetName(1));  // Middle empty -> Column1
                Assert.AreEqual("Last", reader.GetName(2));

                Assert.IsTrue(reader.Read());
                Assert.AreEqual("A", reader.GetString(0));
                Assert.AreEqual("B", reader.GetString(1));
                Assert.AreEqual("C", reader.GetString(2));
            }
        }

        [TestMethod]
        public void TestDefaultHeaderNameValidation_RejectsNull()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
            {
                new CsvReaderOptions { DefaultHeaderName = null };
            });
        }

        [TestMethod]
        public void TestDefaultHeaderNameValidation_RejectsEmpty()
        {
            Assert.ThrowsException<ArgumentException>(() =>
            {
                new CsvReaderOptions { DefaultHeaderName = "" };
            });
        }

        [TestMethod]
        public void TestDefaultHeaderNameValidation_RejectsWhitespace()
        {
            Assert.ThrowsException<ArgumentException>(() =>
            {
                new CsvReaderOptions { DefaultHeaderName = "   " };
            });
        }

        #endregion

        #region Culture Support Tests

        [TestMethod]
        public void TestCultureInfo_GermanDecimals()
        {
            // Addresses LumenWorks issue #66
            // German uses comma as decimal separator, semicolon as delimiter
            string csv = "Name;Price\nApple;1,50\nBanana;2,75";
            var germanCulture = new System.Globalization.CultureInfo("de-DE");
            var options = new CsvReaderOptions
            {
                Delimiter = ";",
                Culture = germanCulture,
                ColumnTypes = new System.Collections.Generic.Dictionary<string, Type>
                {
                    { "Price", typeof(decimal) }
                }
            };

            using (var reader = CreateReaderFromString(csv, options))
            {
                Assert.IsTrue(reader.Read());
                Assert.AreEqual(1.50m, reader.GetDecimal(1));

                Assert.IsTrue(reader.Read());
                Assert.AreEqual(2.75m, reader.GetDecimal(1));
            }
        }

        #endregion

        #region Lenient Quote Mode Tests

        [TestMethod]
        public void TestLenientQuoteMode_UnmatchedQuote()
        {
            // Addresses LumenWorks issues #47 and #56
            // Quote at start but not enclosing the field
            string csv = "ID;Name\n6224613;\"SINUS POLSKA\", MIEDZYRZECZ";
            var options = new CsvReaderOptions
            {
                Delimiter = ";",
                QuoteMode = QuoteMode.Lenient
            };

            using (var reader = CreateReaderFromString(csv, options))
            {
                Assert.IsTrue(reader.Read());
                Assert.AreEqual("6224613", reader.GetString(0));
                // In lenient mode, unmatched quote is treated as literal
                Assert.AreEqual("\"SINUS POLSKA\", MIEDZYRZECZ", reader.GetString(1));
            }
        }

        [TestMethod]
        public void TestLenientQuoteMode_BackslashEscape()
        {
            // Lenient mode handles backslash escapes
            string csv = "Name,Quote\nJohn,\"He said \\\"Hello\\\"\"";
            var options = new CsvReaderOptions { QuoteMode = QuoteMode.Lenient };

            using (var reader = CreateReaderFromString(csv, options))
            {
                Assert.IsTrue(reader.Read());
                Assert.AreEqual("He said \"Hello\"", reader.GetString(1));
            }
        }

        #endregion

        #region Field Count Mismatch Tests

        [TestMethod]
        public void TestMismatchedFields_ThrowException()
        {
            string csv = "A,B,C\n1,2\n4,5,6";
            var options = new CsvReaderOptions { MismatchedFieldAction = MismatchedFieldAction.ThrowException };

            using (var reader = CreateReaderFromString(csv, options))
            {
                Assert.ThrowsException<CsvParseException>(() => reader.Read());
            }
        }

        [TestMethod]
        public void TestMismatchedFields_PadWithNulls()
        {
            string csv = "A,B,C\n1,2\n4,5,6";
            var options = new CsvReaderOptions { MismatchedFieldAction = MismatchedFieldAction.PadWithNulls };

            using (var reader = CreateReaderFromString(csv, options))
            {
                // First row has 2 fields but expects 3 - should pad with null
                Assert.IsTrue(reader.Read());
                Assert.AreEqual("1", reader.GetString(0));
                Assert.AreEqual("2", reader.GetString(1));
                Assert.IsTrue(reader.IsDBNull(2), "Missing field should be padded with null");

                // Second row is complete
                Assert.IsTrue(reader.Read());
                Assert.AreEqual("4", reader.GetString(0));
                Assert.AreEqual("5", reader.GetString(1));
                Assert.AreEqual("6", reader.GetString(2));
            }
        }

        [TestMethod]
        public void TestMismatchedFields_TruncateExtra()
        {
            string csv = "A,B\n1,2,3,4\n5,6";
            var options = new CsvReaderOptions { MismatchedFieldAction = MismatchedFieldAction.TruncateExtra };

            using (var reader = CreateReaderFromString(csv, options))
            {
                Assert.AreEqual(2, reader.FieldCount);

                // First row has 4 fields but only 2 columns - should truncate
                Assert.IsTrue(reader.Read());
                Assert.AreEqual("1", reader.GetString(0));
                Assert.AreEqual("2", reader.GetString(1));

                // Second row is normal
                Assert.IsTrue(reader.Read());
                Assert.AreEqual("5", reader.GetString(0));
                Assert.AreEqual("6", reader.GetString(1));
            }
        }

        [TestMethod]
        public void TestMismatchedFields_PadOrTruncate()
        {
            string csv = "A,B,C\n1,2\n4,5,6,7,8";
            var options = new CsvReaderOptions { MismatchedFieldAction = MismatchedFieldAction.PadOrTruncate };

            using (var reader = CreateReaderFromString(csv, options))
            {
                // First row: too few fields - pad
                Assert.IsTrue(reader.Read());
                Assert.AreEqual("1", reader.GetString(0));
                Assert.AreEqual("2", reader.GetString(1));
                Assert.IsTrue(reader.IsDBNull(2));

                // Second row: too many fields - truncate
                Assert.IsTrue(reader.Read());
                Assert.AreEqual("4", reader.GetString(0));
                Assert.AreEqual("5", reader.GetString(1));
                Assert.AreEqual("6", reader.GetString(2));
            }
        }

        #endregion

        #region Smart Quote Normalization Tests

        [TestMethod]
        public void TestNormalizeSmartQuotes()
        {
            // Addresses LumenWorks issue #25
            // Smart/curly quotes from Word/Excel should be normalized to straight quotes
            string csv = "Name,Description\nJohn,\u201CHello World\u201D";  // "Hello World" with curly quotes
            var options = new CsvReaderOptions { NormalizeQuotes = true };

            using (var reader = CreateReaderFromString(csv, options))
            {
                Assert.IsTrue(reader.Read());
                // The curly quotes should be normalized to straight quotes and treated as field delimiters
                Assert.AreEqual("Hello World", reader.GetString(1));
            }
        }

        #endregion

        #region Parallel Processing Tests

        [TestMethod]
        public void TestParallelProcessing_BasicReading()
        {
            // Test that parallel processing produces the same results as sequential
            string csv = "Name,Age,City\nJohn,30,New York\nJane,25,Boston\nBob,35,Chicago";
            var options = new CsvReaderOptions
            {
                EnableParallelProcessing = true,
                MaxDegreeOfParallelism = 2
            };

            using (var reader = CreateReaderFromString(csv, options))
            {
                Assert.AreEqual(3, reader.FieldCount);

                Assert.IsTrue(reader.Read());
                Assert.AreEqual("John", reader.GetString(0));
                Assert.AreEqual("30", reader.GetString(1));
                Assert.AreEqual("New York", reader.GetString(2));

                Assert.IsTrue(reader.Read());
                Assert.AreEqual("Jane", reader.GetString(0));
                Assert.AreEqual("25", reader.GetString(1));
                Assert.AreEqual("Boston", reader.GetString(2));

                Assert.IsTrue(reader.Read());
                Assert.AreEqual("Bob", reader.GetString(0));
                Assert.AreEqual("35", reader.GetString(1));
                Assert.AreEqual("Chicago", reader.GetString(2));

                Assert.IsFalse(reader.Read());
            }
        }

        [TestMethod]
        public void TestParallelProcessing_LargeFile()
        {
            // Generate a larger CSV file to test parallel processing performance
            var sb = new StringBuilder();
            sb.AppendLine("Id,Name,Value,Description");
            const int rowCount = 1000;
            for (int i = 0; i < rowCount; i++)
            {
                sb.AppendLine($"{i},Name{i},{i * 10},Description for row {i}");
            }

            var options = new CsvReaderOptions
            {
                EnableParallelProcessing = true,
                MaxDegreeOfParallelism = 4,
                ParallelBatchSize = 50
            };

            using (var reader = CreateReaderFromString(sb.ToString(), options))
            {
                int count = 0;
                while (reader.Read())
                {
                    // Verify data integrity - check that records are delivered in order
                    Assert.AreEqual(count.ToString(), reader.GetString(0));
                    Assert.AreEqual($"Name{count}", reader.GetString(1));
                    Assert.AreEqual((count * 10).ToString(), reader.GetString(2));
                    count++;
                }
                Assert.AreEqual(rowCount, count);
            }
        }

        [TestMethod]
        public void TestParallelProcessing_WithTypeConversion()
        {
            string csv = "Id,Amount,Active\n1,100.50,true\n2,200.75,false\n3,300.25,true";
            var options = new CsvReaderOptions
            {
                EnableParallelProcessing = true,
                MaxDegreeOfParallelism = 2,
                ColumnTypes = new System.Collections.Generic.Dictionary<string, Type>
                {
                    { "Id", typeof(int) },
                    { "Amount", typeof(decimal) },
                    { "Active", typeof(bool) }
                }
            };

            using (var reader = CreateReaderFromString(csv, options))
            {
                Assert.IsTrue(reader.Read());
                Assert.AreEqual(1, reader.GetInt32(0));
                Assert.AreEqual(100.50m, reader.GetDecimal(1));
                Assert.AreEqual(true, reader.GetBoolean(2));

                Assert.IsTrue(reader.Read());
                Assert.AreEqual(2, reader.GetInt32(0));
                Assert.AreEqual(200.75m, reader.GetDecimal(1));
                Assert.AreEqual(false, reader.GetBoolean(2));

                Assert.IsTrue(reader.Read());
                Assert.AreEqual(3, reader.GetInt32(0));
                Assert.AreEqual(300.25m, reader.GetDecimal(1));
                Assert.AreEqual(true, reader.GetBoolean(2));

                Assert.IsFalse(reader.Read());
            }
        }

        [TestMethod]
        public void TestParallelProcessing_WithQuotedFields()
        {
            string csv = "Name,Description\n\"John Doe\",\"A \"\"quoted\"\" value\"\n\"Jane Smith\",\"Line1\nLine2\"";
            var options = new CsvReaderOptions
            {
                EnableParallelProcessing = true,
                MaxDegreeOfParallelism = 2
            };

            using (var reader = CreateReaderFromString(csv, options))
            {
                Assert.IsTrue(reader.Read());
                Assert.AreEqual("John Doe", reader.GetString(0));
                Assert.AreEqual("A \"quoted\" value", reader.GetString(1));

                Assert.IsTrue(reader.Read());
                Assert.AreEqual("Jane Smith", reader.GetString(0));
                Assert.AreEqual("Line1\nLine2", reader.GetString(1));

                Assert.IsFalse(reader.Read());
            }
        }

        [TestMethod]
        public void TestParallelProcessing_SingleThread()
        {
            // Test with MaxDegreeOfParallelism = 1 (effectively sequential but using pipeline)
            string csv = "A,B,C\n1,2,3\n4,5,6";
            var options = new CsvReaderOptions
            {
                EnableParallelProcessing = true,
                MaxDegreeOfParallelism = 1
            };

            using (var reader = CreateReaderFromString(csv, options))
            {
                Assert.IsTrue(reader.Read());
                Assert.AreEqual("1", reader.GetString(0));

                Assert.IsTrue(reader.Read());
                Assert.AreEqual("4", reader.GetString(0));

                Assert.IsFalse(reader.Read());
            }
        }

        [TestMethod]
        public void TestParallelProcessing_EmptyFile()
        {
            string csv = "Name,Age\n";
            var options = new CsvReaderOptions
            {
                EnableParallelProcessing = true
            };

            using (var reader = CreateReaderFromString(csv, options))
            {
                Assert.AreEqual(2, reader.FieldCount);
                Assert.IsFalse(reader.Read());
            }
        }

        [TestMethod]
        public void TestParallelProcessing_SkipEmptyLines()
        {
            string csv = "Name,Age\n\nJohn,30\n\nJane,25\n";
            var options = new CsvReaderOptions
            {
                EnableParallelProcessing = true,
                SkipEmptyLines = true
            };

            using (var reader = CreateReaderFromString(csv, options))
            {
                Assert.IsTrue(reader.Read());
                Assert.AreEqual("John", reader.GetString(0));

                Assert.IsTrue(reader.Read());
                Assert.AreEqual("Jane", reader.GetString(0));

                Assert.IsFalse(reader.Read());
            }
        }

        [TestMethod]
        public void TestParallelProcessing_CommentLines()
        {
            string csv = "Name,Age\n# This is a comment\nJohn,30\n# Another comment\nJane,25";
            var options = new CsvReaderOptions
            {
                EnableParallelProcessing = true,
                Comment = '#'
            };

            using (var reader = CreateReaderFromString(csv, options))
            {
                Assert.IsTrue(reader.Read());
                Assert.AreEqual("John", reader.GetString(0));

                Assert.IsTrue(reader.Read());
                Assert.AreEqual("Jane", reader.GetString(0));

                Assert.IsFalse(reader.Read());
            }
        }

        [TestMethod]
        public void TestParallelProcessing_NullValues()
        {
            string csv = "Name,Age\nJohn,NULL\nJane,25";
            var options = new CsvReaderOptions
            {
                EnableParallelProcessing = true,
                NullValue = "NULL"
            };

            using (var reader = CreateReaderFromString(csv, options))
            {
                Assert.IsTrue(reader.Read());
                Assert.AreEqual("John", reader.GetString(0));
                Assert.IsTrue(reader.IsDBNull(1));

                Assert.IsTrue(reader.Read());
                Assert.AreEqual("Jane", reader.GetString(0));
                Assert.AreEqual("25", reader.GetString(1));

                Assert.IsFalse(reader.Read());
            }
        }

        [TestMethod]
        public void TestParallelProcessing_MismatchedFields_PadWithNulls()
        {
            string csv = "A,B,C\n1,2\n4,5,6";
            var options = new CsvReaderOptions
            {
                EnableParallelProcessing = true,
                MismatchedFieldAction = MismatchedFieldAction.PadWithNulls
            };

            using (var reader = CreateReaderFromString(csv, options))
            {
                Assert.IsTrue(reader.Read());
                Assert.AreEqual("1", reader.GetString(0));
                Assert.AreEqual("2", reader.GetString(1));
                Assert.IsTrue(reader.IsDBNull(2));

                Assert.IsTrue(reader.Read());
                Assert.AreEqual("4", reader.GetString(0));
                Assert.AreEqual("5", reader.GetString(1));
                Assert.AreEqual("6", reader.GetString(2));

                Assert.IsFalse(reader.Read());
            }
        }

        [TestMethod]
        public void TestParallelProcessing_NoHeaderRow()
        {
            string csv = "John,30,New York\nJane,25,Boston";
            var options = new CsvReaderOptions
            {
                HasHeaderRow = false,
                EnableParallelProcessing = true
            };

            using (var reader = CreateReaderFromString(csv, options))
            {
                Assert.IsTrue(reader.Read());
                Assert.AreEqual(3, reader.FieldCount);
                Assert.AreEqual("Column0", reader.GetName(0));
                Assert.AreEqual("John", reader.GetString(0));
                Assert.AreEqual("30", reader.GetString(1));

                Assert.IsTrue(reader.Read());
                Assert.AreEqual("Jane", reader.GetString(0));

                Assert.IsFalse(reader.Read());
            }
        }

        [TestMethod]
        public void TestParallelProcessing_StaticColumns()
        {
            string csv = "Name,Age\nJohn,30\nJane,25";
            var options = new CsvReaderOptions
            {
                EnableParallelProcessing = true,
                StaticColumns = new System.Collections.Generic.List<StaticColumn>
                {
                    new StaticColumn("Source", "TestFile")
                }
            };

            using (var reader = CreateReaderFromString(csv, options))
            {
                Assert.AreEqual(3, reader.FieldCount);

                Assert.IsTrue(reader.Read());
                Assert.AreEqual("John", reader.GetString(0));
                Assert.AreEqual("TestFile", reader.GetString(2));

                Assert.IsTrue(reader.Read());
                Assert.AreEqual("Jane", reader.GetString(0));
                Assert.AreEqual("TestFile", reader.GetString(2));

                Assert.IsFalse(reader.Read());
            }
        }

        [TestMethod]
        public void TestParallelProcessing_Disabled()
        {
            // Verify sequential mode still works correctly
            string csv = "Name,Age\nJohn,30\nJane,25";
            var options = new CsvReaderOptions
            {
                EnableParallelProcessing = false  // Explicitly disabled (default)
            };

            using (var reader = CreateReaderFromString(csv, options))
            {
                Assert.IsTrue(reader.Read());
                Assert.AreEqual("John", reader.GetString(0));

                Assert.IsTrue(reader.Read());
                Assert.AreEqual("Jane", reader.GetString(0));

                Assert.IsFalse(reader.Read());
            }
        }

        #endregion

        #region Thread-Safety Stress Tests

        [TestMethod]
        public void TestParallelProcessing_ConcurrentGetValueAccess()
        {
            // Stress test: Multiple threads calling GetValue while Read() advances
            // This tests the thread-safety of _convertedValues access
            var sb = new StringBuilder();
            sb.AppendLine("Id,Name,Value");
            const int rowCount = 500;
            for (int i = 0; i < rowCount; i++)
            {
                sb.AppendLine($"{i},Name{i},{i * 10}");
            }

            var options = new CsvReaderOptions
            {
                EnableParallelProcessing = true,
                MaxDegreeOfParallelism = 4
            };

            using (var reader = CreateReaderFromString(sb.ToString(), options))
            {
                var errors = new System.Collections.Concurrent.ConcurrentBag<Exception>();
                int recordsProcessed = 0;

                while (reader.Read())
                {
                    System.Threading.Interlocked.Increment(ref recordsProcessed);

                    // Spawn multiple threads to read values concurrently
                    var tasks = new System.Threading.Tasks.Task[4];
                    for (int t = 0; t < tasks.Length; t++)
                    {
                        int threadId = t;
                        tasks[t] = System.Threading.Tasks.Task.Run(() =>
                        {
                            try
                            {
                                // Read all values multiple times
                                for (int iteration = 0; iteration < 10; iteration++)
                                {
                                    var val0 = reader.GetValue(0);
                                    var val1 = reader.GetValue(1);
                                    var val2 = reader.GetValue(2);

                                    // Also test GetValues
                                    var values = new object[3];
                                    reader.GetValues(values);

                                    // Access CurrentRecordIndex
                                    var idx = reader.CurrentRecordIndex;
                                }
                            }
                            catch (Exception ex) when (!(ex is ObjectDisposedException))
                            {
                                errors.Add(ex);
                            }
                        });
                    }

                    System.Threading.Tasks.Task.WaitAll(tasks);
                }

                Assert.AreEqual(rowCount, recordsProcessed, "Should process all records");
                Assert.AreEqual(0, errors.Count, $"Should have no errors, but got: {string.Join(", ", errors)}");
            }
        }

        [TestMethod]
        public void TestParallelProcessing_HighConcurrencyStress()
        {
            // High-concurrency stress test with many worker threads
            var sb = new StringBuilder();
            sb.AppendLine("A,B,C,D,E,F,G,H,I,J");
            const int rowCount = 1000;
            for (int i = 0; i < rowCount; i++)
            {
                sb.AppendLine($"{i},{i+1},{i+2},{i+3},{i+4},{i+5},{i+6},{i+7},{i+8},{i+9}");
            }

            var options = new CsvReaderOptions
            {
                EnableParallelProcessing = true,
                MaxDegreeOfParallelism = Environment.ProcessorCount
            };

            using (var reader = CreateReaderFromString(sb.ToString(), options))
            {
                var errors = new System.Collections.Concurrent.ConcurrentBag<Exception>();
                var allValues = new System.Collections.Concurrent.ConcurrentBag<long>();

                while (reader.Read())
                {
                    long recordIndex = reader.CurrentRecordIndex;
                    allValues.Add(recordIndex);

                    // Concurrent reads from many threads
                    System.Threading.Tasks.Parallel.For(0, 8, threadIdx =>
                    {
                        try
                        {
                            var values = new object[10];
                            int count = reader.GetValues(values);
                            Assert.AreEqual(10, count, "Should return all 10 values");
                        }
                        catch (Exception ex) when (!(ex is ObjectDisposedException))
                        {
                            errors.Add(ex);
                        }
                    });
                }

                Assert.AreEqual(rowCount, allValues.Count, "Should process all records");
                Assert.AreEqual(0, errors.Count, $"Should have no errors: {string.Join("; ", errors)}");

                // Verify all record indices were captured (0 to rowCount-1)
                var sortedIndices = allValues.OrderBy(x => x).ToList();
                for (int i = 0; i < rowCount; i++)
                {
                    Assert.AreEqual(i, sortedIndices[i], $"Record index {i} should be present");
                }
            }
        }

        [TestMethod]
        public void TestParallelProcessing_CurrentRecordIndexConsistency()
        {
            // Test that CurrentRecordIndex remains consistent during parallel processing
            var sb = new StringBuilder();
            sb.AppendLine("Id,Value");
            const int rowCount = 200;
            for (int i = 0; i < rowCount; i++)
            {
                sb.AppendLine($"{i},{i * 100}");
            }

            var options = new CsvReaderOptions
            {
                EnableParallelProcessing = true,
                MaxDegreeOfParallelism = 4
            };

            using (var reader = CreateReaderFromString(sb.ToString(), options))
            {
                long lastIndex = -1;

                while (reader.Read())
                {
                    long currentIndex = reader.CurrentRecordIndex;

                    // Verify record indices are strictly increasing
                    Assert.IsTrue(currentIndex > lastIndex,
                        $"Record index should increase: last={lastIndex}, current={currentIndex}");

                    // Read the index multiple times from different threads
                    var indices = new System.Collections.Concurrent.ConcurrentBag<long>();
                    System.Threading.Tasks.Parallel.For(0, 4, _ =>
                    {
                        indices.Add(reader.CurrentRecordIndex);
                    });

                    // All reads should return the same index (no torn reads)
                    var uniqueIndices = indices.Distinct().ToList();
                    Assert.AreEqual(1, uniqueIndices.Count,
                        $"All concurrent reads should return same index, got: {string.Join(", ", uniqueIndices)}");

                    lastIndex = currentIndex;
                }

                Assert.AreEqual(rowCount - 1, lastIndex, "Should have processed all records");
            }
        }

        [TestMethod]
        public void TestParallelProcessing_DisposeDuringRead()
        {
            // Test that disposing the reader while threads are accessing it doesn't crash
            var sb = new StringBuilder();
            sb.AppendLine("Id,Name,Value");
            for (int i = 0; i < 100; i++)
            {
                sb.AppendLine($"{i},Name{i},{i * 10}");
            }

            var options = new CsvReaderOptions
            {
                EnableParallelProcessing = true,
                MaxDegreeOfParallelism = 4
            };

            var errors = new System.Collections.Concurrent.ConcurrentBag<Exception>();
            var reader = CreateReaderFromString(sb.ToString(), options);

            // Start reading in the background
            var readTask = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    while (reader.Read())
                    {
                        // Simulate some work
                        System.Threading.Thread.Sleep(1);
                    }
                }
                catch (ObjectDisposedException)
                {
                    // Expected when reader is disposed
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            });

            // Wait a bit then dispose
            System.Threading.Thread.Sleep(10);
            reader.Dispose();

            readTask.Wait(TimeSpan.FromSeconds(5));

            // Should not have unexpected errors (ObjectDisposedException is fine)
            foreach (var error in errors)
            {
                Assert.Fail($"Unexpected error during dispose: {error}");
            }
        }

        [TestMethod]
        public void TestParallelProcessing_RepeatedGetValuesStress()
        {
            // Stress test repeated GetValues calls to detect race conditions in Array.Copy
            var sb = new StringBuilder();
            sb.AppendLine("Col1,Col2,Col3,Col4,Col5");
            const int rowCount = 300;
            for (int i = 0; i < rowCount; i++)
            {
                sb.AppendLine($"A{i},B{i},C{i},D{i},E{i}");
            }

            var options = new CsvReaderOptions
            {
                EnableParallelProcessing = true,
                MaxDegreeOfParallelism = 4
            };

            using (var reader = CreateReaderFromString(sb.ToString(), options))
            {
                int recordCount = 0;
                var inconsistencies = new System.Collections.Concurrent.ConcurrentBag<string>();

                while (reader.Read())
                {
                    recordCount++;
                    string expectedPrefix = $"A{reader.CurrentRecordIndex}";

                    // Multiple threads calling GetValues simultaneously
                    System.Threading.Tasks.Parallel.For(0, 10, iteration =>
                    {
                        var values = new object[5];
                        reader.GetValues(values);

                        // Check that all values are from the same record (consistent snapshot)
                        string val0 = values[0]?.ToString() ?? "";
                        string val1 = values[1]?.ToString() ?? "";

                        // Extract the numeric suffix
                        if (val0.StartsWith("A") && val1.StartsWith("B"))
                        {
                            string suffix0 = val0.Substring(1);
                            string suffix1 = val1.Substring(1);

                            if (suffix0 != suffix1)
                            {
                                inconsistencies.Add($"Inconsistent record: {val0} vs {val1}");
                            }
                        }
                    });
                }

                Assert.AreEqual(rowCount, recordCount, "Should process all records");
                Assert.AreEqual(0, inconsistencies.Count,
                    $"Should have consistent snapshots: {string.Join("; ", inconsistencies.Take(5))}");
            }
        }

        #endregion

        #region Compression and Security Tests

        [TestMethod]
        public void TestDecompressionBombProtection_ThrowsWhenExceeded()
        {
            // Create CSV data that will exceed the size limit when decompressed
            var csvBuilder = new StringBuilder();
            csvBuilder.AppendLine("Name,Value");
            for (int i = 0; i < 100; i++)
            {
                csvBuilder.AppendLine($"Row{i},SomeDataThatRepeatsWell");
            }
            string csvData = csvBuilder.ToString();
            byte[] uncompressedBytes = Encoding.UTF8.GetBytes(csvData);

            // Compress the data using GZip
            byte[] compressedBytes;
            using (var compressedStream = new MemoryStream())
            {
                using (var gzipStream = new GZipStream(compressedStream, CompressionLevel.Optimal, leaveOpen: true))
                {
                    gzipStream.Write(uncompressedBytes, 0, uncompressedBytes.Length);
                }
                compressedBytes = compressedStream.ToArray();
            }

            // Set a size limit smaller than the uncompressed data
            var options = new CsvReaderOptions
            {
                MaxDecompressedSize = uncompressedBytes.Length / 2 // Limit to half the actual size
            };

            // Use CompressionHelper to decompress with limit
            using (var compressedInput = new MemoryStream(compressedBytes))
            {
                var decompressedStream = Dataplat.Dbatools.Csv.Compression.CompressionHelper.WrapForDecompression(
                    compressedInput,
                    Dataplat.Dbatools.Csv.Compression.CompressionType.GZip,
                    options.MaxDecompressedSize);

                using (var reader = new StreamReader(decompressedStream))
                {
                    var ex = Assert.ThrowsException<InvalidOperationException>(() =>
                    {
                        // Read all content to trigger the bomb protection
                        reader.ReadToEnd();
                    });

                    Assert.IsTrue(ex.Message.Contains("Decompressed data exceeded maximum allowed size"),
                        $"Expected bomb protection message, got: {ex.Message}");
                    Assert.IsTrue(ex.Message.Contains("decompression bomb"),
                        $"Expected 'decompression bomb' in message, got: {ex.Message}");
                }
            }
        }

        [TestMethod]
        public void TestDecompressionBombProtection_AllowsWithinLimit()
        {
            // Create small CSV data
            string csvData = "Name,Value\nRow1,Data1\nRow2,Data2\n";
            byte[] uncompressedBytes = Encoding.UTF8.GetBytes(csvData);

            // Compress the data
            byte[] compressedBytes;
            using (var compressedStream = new MemoryStream())
            {
                using (var gzipStream = new GZipStream(compressedStream, CompressionLevel.Optimal, leaveOpen: true))
                {
                    gzipStream.Write(uncompressedBytes, 0, uncompressedBytes.Length);
                }
                compressedBytes = compressedStream.ToArray();
            }

            // Set a size limit larger than the uncompressed data
            long sizeLimit = uncompressedBytes.Length * 2;

            using (var compressedInput = new MemoryStream(compressedBytes))
            {
                var decompressedStream = Dataplat.Dbatools.Csv.Compression.CompressionHelper.WrapForDecompression(
                    compressedInput,
                    Dataplat.Dbatools.Csv.Compression.CompressionType.GZip,
                    sizeLimit);

                using (var reader = new StreamReader(decompressedStream))
                {
                    // Should not throw - data is within limit
                    string content = reader.ReadToEnd();
                    Assert.IsTrue(content.Contains("Row1,Data1"));
                    Assert.IsTrue(content.Contains("Row2,Data2"));
                }
            }
        }

        [TestMethod]
        public void TestDecompressionBombProtection_UnlimitedWhenZero()
        {
            // Create CSV data
            string csvData = "Name,Value\nRow1,Data1\n";
            byte[] uncompressedBytes = Encoding.UTF8.GetBytes(csvData);

            // Compress the data
            byte[] compressedBytes;
            using (var compressedStream = new MemoryStream())
            {
                using (var gzipStream = new GZipStream(compressedStream, CompressionLevel.Optimal, leaveOpen: true))
                {
                    gzipStream.Write(uncompressedBytes, 0, uncompressedBytes.Length);
                }
                compressedBytes = compressedStream.ToArray();
            }

            // Set limit to 0 (unlimited)
            using (var compressedInput = new MemoryStream(compressedBytes))
            {
                var decompressedStream = Dataplat.Dbatools.Csv.Compression.CompressionHelper.WrapForDecompression(
                    compressedInput,
                    Dataplat.Dbatools.Csv.Compression.CompressionType.GZip,
                    maxDecompressedSize: 0); // Unlimited

                using (var reader = new StreamReader(decompressedStream))
                {
                    // Should not throw even with 0 limit (means unlimited)
                    string content = reader.ReadToEnd();
                    Assert.IsTrue(content.Contains("Row1,Data1"));
                }
            }
        }

        #endregion

        #region Helper Methods

        private CsvDataReader CreateReaderFromString(string csv, CsvReaderOptions options = null)
        {
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var textReader = new StreamReader(stream);
            return new CsvDataReader(textReader, options);
        }

        #endregion
    }
}
