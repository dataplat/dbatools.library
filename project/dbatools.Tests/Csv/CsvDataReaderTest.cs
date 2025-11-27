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
