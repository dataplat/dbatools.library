using System;
using System.IO;
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
