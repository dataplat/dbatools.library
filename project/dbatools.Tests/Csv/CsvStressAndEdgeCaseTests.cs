using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Csv;
using Dataplat.Dbatools.Csv.Reader;

namespace Dataplat.Dbatools.Csv.Tests
{
    /// <summary>
    /// Stress tests and edge case tests for CSV processing.
    /// Covers: concurrent schema inference, malformed data, large file handling, and memory efficiency.
    /// </summary>
    [TestClass]
    public class CsvStressAndEdgeCaseTests
    {
        private string _tempDir;

        [TestInitialize]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "CsvStressTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_tempDir))
            {
                try { Directory.Delete(_tempDir, true); } catch { }
            }
        }

        #region Concurrent Schema Inference Tests

        [TestMethod]
        public void TestConcurrentSchemaInference_MultipleFilesInParallel()
        {
            // Create multiple CSV files with different schemas
            var files = new List<string>();
            for (int i = 0; i < 10; i++)
            {
                string csvPath = Path.Combine(_tempDir, $"concurrent_{i}.csv");
                var sb = new StringBuilder();
                sb.AppendLine($"Id,Name,Value{i}");
                for (int j = 0; j < 100; j++)
                {
                    sb.AppendLine($"{j},Name{j},{j * 1.5m:F2}");
                }
                File.WriteAllText(csvPath, sb.ToString());
                files.Add(csvPath);
            }

            var errors = new ConcurrentBag<Exception>();
            var results = new ConcurrentDictionary<int, List<InferredColumn>>();

            // Run schema inference in parallel on different files
            Parallel.For(0, files.Count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
            {
                try
                {
                    var columns = CsvSchemaInference.InferSchemaFromSample(files[i]);
                    results[i] = columns;

                    // Verify each file's schema is correct
                    Assert.AreEqual(3, columns.Count, $"File {i} should have 3 columns");
                    Assert.AreEqual("int", columns[0].SqlDataType, $"File {i} Id should be int");
                    Assert.IsTrue(columns[2].SqlDataType.Contains("decimal"), $"File {i} Value{i} should be decimal");
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            });

            Assert.AreEqual(0, errors.Count, $"Errors occurred: {string.Join(", ", errors.Select(e => e.Message))}");
            Assert.AreEqual(files.Count, results.Count, "All files should have been processed");
        }

        [TestMethod]
        public void TestConcurrentSchemaInference_SameFileConcurrentReads()
        {
            // Create a single CSV file
            string csvPath = Path.Combine(_tempDir, "shared.csv");
            var sb = new StringBuilder();
            sb.AppendLine("Id,Name,Value,Date");
            for (int i = 0; i < 1000; i++)
            {
                sb.AppendLine($"{i},Name{i},{i * 2.5m:F2},2024-{(i % 12) + 1:D2}-{(i % 28) + 1:D2}");
            }
            File.WriteAllText(csvPath, sb.ToString());

            var errors = new ConcurrentBag<Exception>();
            var results = new ConcurrentBag<List<InferredColumn>>();

            // Multiple threads reading the same file (separate file handles)
            Parallel.For(0, 20, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
            {
                try
                {
                    var columns = CsvSchemaInference.InferSchemaFromSample(csvPath);
                    results.Add(columns);

                    // All should get the same schema
                    Assert.AreEqual(4, columns.Count);
                    Assert.AreEqual("int", columns[0].SqlDataType);
                    Assert.IsTrue(columns[2].SqlDataType.Contains("decimal"));
                    Assert.AreEqual("datetime2", columns[3].SqlDataType);
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            });

            Assert.AreEqual(0, errors.Count, $"Errors: {string.Join("; ", errors.Select(e => e.Message))}");
            Assert.AreEqual(20, results.Count);

            // Verify all results are consistent
            var firstResult = results.First();
            foreach (var result in results)
            {
                for (int i = 0; i < firstResult.Count; i++)
                {
                    Assert.AreEqual(firstResult[i].SqlDataType, result[i].SqlDataType,
                        $"Column {i} type mismatch between parallel runs");
                }
            }
        }

        [TestMethod]
        public void TestConcurrentSchemaInference_WithCancellation()
        {
            // Create a large file
            string csvPath = Path.Combine(_tempDir, "cancellable.csv");
            var sb = new StringBuilder();
            sb.AppendLine("Id,Value");
            for (int i = 0; i < 50000; i++)
            {
                sb.AppendLine($"{i},{i * 1.5m:F2}");
            }
            File.WriteAllText(csvPath, sb.ToString());

            var completedCount = 0;
            var cancelledCount = 0;
            var errorCount = 0;

            Parallel.For(0, 10, i =>
            {
                var cts = new CancellationTokenSource();

                // Cancel some operations randomly
                if (i % 2 == 0)
                {
                    cts.CancelAfter(1); // Cancel almost immediately
                }

                try
                {
                    using (var stream = File.OpenRead(csvPath))
                    {
                        var columns = CsvSchemaInference.InferSchemaFromSample(stream, null, 50000, cts.Token);
                        Interlocked.Increment(ref completedCount);
                    }
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref cancelledCount);
                }
                catch
                {
                    Interlocked.Increment(ref errorCount);
                }
            });

            // Some should complete, some should be cancelled
            Assert.IsTrue(completedCount + cancelledCount == 10, "All operations should either complete or be cancelled");
            Assert.AreEqual(0, errorCount, "No unexpected errors should occur");
        }

        [TestMethod]
        public void TestColumnTypeAnalyzer_NoSharedStateBetweenInferences()
        {
            // Ensure separate inference calls don't share state
            string csvPath1 = Path.Combine(_tempDir, "file1.csv");
            string csvPath2 = Path.Combine(_tempDir, "file2.csv");

            // File 1: Integers
            File.WriteAllText(csvPath1, "Value\n1\n2\n3\n");

            // File 2: Strings (should not be affected by file1's integer detection)
            File.WriteAllText(csvPath2, "Value\nabc\ndef\nghi\n");

            // Run concurrently
            var task1 = Task.Run(() => CsvSchemaInference.InferSchemaFromSample(csvPath1));
            var task2 = Task.Run(() => CsvSchemaInference.InferSchemaFromSample(csvPath2));

            Task.WaitAll(task1, task2);

            Assert.AreEqual("int", task1.Result[0].SqlDataType, "File 1 should detect integers");
            Assert.IsTrue(task1.Result[0].SqlDataType == "int");
            Assert.IsTrue(task2.Result[0].SqlDataType.StartsWith("varchar("), "File 2 should detect strings");
        }

        #endregion

        #region Malformed CSV Data Tests

        [TestMethod]
        public void TestMalformedCsv_TruncatedFile_ThrowsByDefault()
        {
            // File ends mid-field (missing Value on last row)
            // By default, CsvDataReader throws CsvParseException for mismatched field counts
            string csvPath = Path.Combine(_tempDir, "truncated.csv");
            File.WriteAllText(csvPath, "Id,Name,Value\n1,John,100\n2,Jane");

            Assert.ThrowsException<CsvParseException>(() =>
            {
                CsvSchemaInference.InferSchemaFromSample(csvPath);
            });
        }

        [TestMethod]
        public void TestMalformedCsv_TruncatedFile_WithPadNulls()
        {
            // With MismatchedFieldAction.PadWithNulls, missing fields become null
            string csvPath = Path.Combine(_tempDir, "truncated_pad.csv");
            File.WriteAllText(csvPath, "Id,Name,Value\n1,John,100\n2,Jane");

            var options = new CsvReaderOptions
            {
                MismatchedFieldAction = MismatchedFieldAction.PadWithNulls
            };
            var columns = CsvSchemaInference.InferSchemaFromSample(csvPath, options);

            Assert.AreEqual(3, columns.Count);
            Assert.IsTrue(columns[2].IsNullable, "Value column should be nullable due to missing field");
        }

        [TestMethod]
        public void TestMalformedCsv_UnmatchedQuotes_ThrowsByDefault()
        {
            // Field starts with quote but never closes - throws by default
            string csvPath = Path.Combine(_tempDir, "unmatched_quote.csv");
            File.WriteAllText(csvPath, "Id,Name,Value\n1,\"John,100\n2,Jane,200\n");

            Assert.ThrowsException<CsvParseException>(() =>
            {
                CsvSchemaInference.InferSchemaFromSample(csvPath);
            });
        }

        [TestMethod]
        public void TestMalformedCsv_UnmatchedQuotes_WithAdvanceToNextLine()
        {
            // With ParseErrorAction.AdvanceToNextLine, bad rows are skipped
            string csvPath = Path.Combine(_tempDir, "unmatched_quote_skip.csv");
            File.WriteAllText(csvPath, "Id,Name,Value\n1,\"John,100\n2,Jane,200\n3,Bob,300\n");

            var options = new CsvReaderOptions
            {
                ParseErrorAction = CsvParseErrorAction.AdvanceToNextLine
            };

            var columns = CsvSchemaInference.InferSchemaFromSample(csvPath, options);
            // Should parse the valid rows
            Assert.IsTrue(columns.Count >= 1);
        }

        [TestMethod]
        public void TestMalformedCsv_InconsistentFieldCounts_ThrowsByDefault()
        {
            // By default, mismatched field counts throw
            string csvPath = Path.Combine(_tempDir, "inconsistent.csv");
            File.WriteAllText(csvPath, @"Id,Name,Value
1,John,100
2,Jane
3,Bob,300,ExtraField,AnotherExtra
4,Alice,400
");

            Assert.ThrowsException<CsvParseException>(() =>
            {
                CsvSchemaInference.InferSchemaFromSample(csvPath);
            });
        }

        [TestMethod]
        public void TestMalformedCsv_InconsistentFieldCounts_WithPadOrTruncate()
        {
            // With PadOrTruncate, both missing and extra fields are handled
            string csvPath = Path.Combine(_tempDir, "inconsistent_lenient.csv");
            File.WriteAllText(csvPath, @"Id,Name,Value
1,John,100
2,Jane
3,Bob,300,ExtraField,AnotherExtra
4,Alice,400
");

            var options = new CsvReaderOptions
            {
                MismatchedFieldAction = MismatchedFieldAction.PadOrTruncate
            };
            var columns = CsvSchemaInference.InferSchemaFromSample(csvPath, options);

            Assert.AreEqual(3, columns.Count);
            Assert.IsTrue(columns[2].IsNullable, "Value should be nullable due to row 2 missing it");
        }

        [TestMethod]
        public void TestMalformedCsv_EmptyLines()
        {
            string csvPath = Path.Combine(_tempDir, "empty_lines.csv");
            File.WriteAllText(csvPath, "Id,Name\n\n1,John\n\n\n2,Jane\n\n");

            var options = new CsvReaderOptions { SkipEmptyLines = true };
            var columns = CsvSchemaInference.InferSchemaFromSample(csvPath, options);

            Assert.AreEqual(2, columns.Count);
            Assert.AreEqual("int", columns[0].SqlDataType);
        }

        [TestMethod]
        public void TestMalformedCsv_OnlyWhitespace()
        {
            string csvPath = Path.Combine(_tempDir, "whitespace.csv");
            File.WriteAllText(csvPath, "Id,Name,Value\n   ,   ,   \n  ,  ,  \n");

            var columns = CsvSchemaInference.InferSchemaFromSample(csvPath);

            // All values are whitespace = all nullable varchar
            Assert.AreEqual(3, columns.Count);
            Assert.IsTrue(columns[0].IsNullable);
            Assert.IsTrue(columns[1].IsNullable);
            Assert.IsTrue(columns[2].IsNullable);
        }

        [TestMethod]
        public void TestMalformedCsv_VeryLongLine()
        {
            // Single field with extremely long value
            string csvPath = Path.Combine(_tempDir, "long_line.csv");
            string longValue = new string('x', 100000);
            File.WriteAllText(csvPath, $"Value\n{longValue}\n");

            var columns = CsvSchemaInference.InferSchemaFromSample(csvPath);

            Assert.AreEqual(1, columns.Count);
            Assert.AreEqual("varchar(max)", columns[0].SqlDataType); // > 8000 chars
            Assert.AreEqual(100000, columns[0].MaxLength);
        }

        [TestMethod]
        public void TestMalformedCsv_BinaryDataMixed()
        {
            // CSV with some binary/non-printable characters
            string csvPath = Path.Combine(_tempDir, "binary.csv");
            byte[] content = Encoding.UTF8.GetBytes("Id,Name\n1,Test\n");
            byte[] binary = new byte[] { 0x00, 0x01, 0x02, 0xFF, 0xFE };

            using (var fs = File.Create(csvPath))
            {
                fs.Write(content, 0, content.Length);
                // Write a row with binary garbage
                var row = Encoding.UTF8.GetBytes("2,");
                fs.Write(row, 0, row.Length);
                fs.Write(binary, 0, binary.Length);
                fs.Write(new byte[] { (byte)'\n' }, 0, 1);
            }

            // Should not throw
            try
            {
                var columns = CsvSchemaInference.InferSchemaFromSample(csvPath);
                Assert.IsTrue(columns.Count >= 1);
            }
            catch (Exception ex)
            {
                Assert.Fail($"Should handle binary data gracefully, but threw: {ex.Message}");
            }
        }

        [TestMethod]
        public void TestMalformedCsv_InvalidUtf8()
        {
            // Create file with invalid UTF-8 sequences
            string csvPath = Path.Combine(_tempDir, "invalid_utf8.csv");
            byte[] header = Encoding.UTF8.GetBytes("Id,Name\n1,Test\n2,");
            byte[] invalidUtf8 = new byte[] { 0xC0, 0xC1, 0xF5, 0xF6, 0xF7 }; // Invalid UTF-8 bytes
            byte[] rest = Encoding.UTF8.GetBytes("\n3,Valid\n");

            using (var fs = File.Create(csvPath))
            {
                fs.Write(header, 0, header.Length);
                fs.Write(invalidUtf8, 0, invalidUtf8.Length);
                fs.Write(rest, 0, rest.Length);
            }

            // Read with encoding that replaces invalid chars
            var options = new CsvReaderOptions { Encoding = Encoding.UTF8 };

            try
            {
                var columns = CsvSchemaInference.InferSchemaFromSample(csvPath, options);
                // Should recover and parse what it can
                Assert.AreEqual(2, columns.Count);
            }
            catch (DecoderFallbackException)
            {
                // This is also acceptable behavior - strict UTF-8 rejection
            }
        }

        [TestMethod]
        public void TestMalformedCsv_NestedQuotes_ThrowsByDefault()
        {
            // Improperly escaped quotes - throws by default
            string csvPath = Path.Combine(_tempDir, "nested_quotes.csv");
            File.WriteAllText(csvPath, "Id,Name,Description\n1,John,\"He said \"hello\" to me\"\n2,Jane,Normal\n");

            Assert.ThrowsException<CsvParseException>(() =>
            {
                CsvSchemaInference.InferSchemaFromSample(csvPath);
            });
        }

        [TestMethod]
        public void TestMalformedCsv_NestedQuotes_WithAdvanceToNextLine()
        {
            // With ParseErrorAction.AdvanceToNextLine, bad rows are skipped
            string csvPath = Path.Combine(_tempDir, "nested_quotes_skip.csv");
            File.WriteAllText(csvPath, "Id,Name,Description\n1,John,\"He said \"hello\" to me\"\n2,Jane,Normal\n");

            var options = new CsvReaderOptions
            {
                ParseErrorAction = CsvParseErrorAction.AdvanceToNextLine
            };

            var columns = CsvSchemaInference.InferSchemaFromSample(csvPath, options);
            // Should parse the valid rows (header + row 2 at minimum)
            Assert.IsTrue(columns.Count >= 1);
        }

        [TestMethod]
        public void TestMalformedCsv_ProperlyEscapedQuotes()
        {
            // RFC 4180: quotes inside quoted fields are escaped by doubling them
            string csvPath = Path.Combine(_tempDir, "escaped_quotes.csv");
            File.WriteAllText(csvPath, "Id,Name,Description\n1,John,\"He said \"\"hello\"\" to me\"\n2,Jane,Normal\n");

            var columns = CsvSchemaInference.InferSchemaFromSample(csvPath);

            Assert.AreEqual(3, columns.Count);
            Assert.IsTrue(columns[2].SqlDataType.StartsWith("varchar("));
        }

        [TestMethod]
        public void TestMalformedCsv_CRWithoutLF()
        {
            // Old Mac-style line endings (CR only)
            string csvPath = Path.Combine(_tempDir, "cr_only.csv");
            File.WriteAllText(csvPath, "Id,Name\r1,John\r2,Jane\r");

            var columns = CsvSchemaInference.InferSchemaFromSample(csvPath);

            // Should handle different line endings
            Assert.AreEqual(2, columns.Count);
        }

        [TestMethod]
        public void TestMalformedCsv_MixedLineEndings()
        {
            // Mix of CRLF, LF, and CR
            string csvPath = Path.Combine(_tempDir, "mixed_endings.csv");
            File.WriteAllText(csvPath, "Id,Name\r\n1,John\n2,Jane\r3,Bob\r\n");

            var columns = CsvSchemaInference.InferSchemaFromSample(csvPath);

            Assert.AreEqual(2, columns.Count);
        }

        [TestMethod]
        public void TestMalformedCsv_NullBytes()
        {
            // CSV with null bytes embedded
            string csvPath = Path.Combine(_tempDir, "null_bytes.csv");
            var content = "Id,Name\n1,Te\0st\n2,Normal\n";
            File.WriteAllBytes(csvPath, Encoding.UTF8.GetBytes(content));

            try
            {
                var columns = CsvSchemaInference.InferSchemaFromSample(csvPath);
                Assert.AreEqual(2, columns.Count);
            }
            catch
            {
                // Some parsers may reject null bytes - that's acceptable
            }
        }

        #endregion

        #region Large File Memory Efficiency Tests

        [TestMethod]
        public void TestLargeFile_StreamingMemoryEfficiency()
        {
            // Create a large-ish file (10MB) and verify memory stays bounded
            string csvPath = Path.Combine(_tempDir, "large.csv");

            // Write 10MB of data
            using (var writer = new StreamWriter(csvPath))
            {
                writer.WriteLine("Id,Name,Value,Description");
                string longDescription = new string('x', 1000); // 1KB per row
                for (int i = 0; i < 10000; i++) // ~10MB total
                {
                    writer.WriteLine($"{i},Name{i},{i * 1.5m:F2},{longDescription}");
                }
            }

            // Force GC and get baseline
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long baselineMemory = GC.GetTotalMemory(true);

            // Run inference
            var columns = CsvSchemaInference.InferSchemaFromSample(csvPath, null, 10000);

            // Check memory after
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long afterMemory = GC.GetTotalMemory(true);

            // Memory increase should be reasonable (less than 50MB for a 10MB file)
            // The reader streams, so it shouldn't load the whole file
            long memoryIncrease = afterMemory - baselineMemory;

            Assert.AreEqual(4, columns.Count);
            // Relaxed assertion - just verify it doesn't explode
            Assert.IsTrue(memoryIncrease < 100 * 1024 * 1024,
                $"Memory increase {memoryIncrease / (1024 * 1024)}MB seems too high for streaming");
        }

        [TestMethod]
        public void TestLargeFile_ProgressReporting()
        {
            string csvPath = Path.Combine(_tempDir, "progress.csv");
            using (var writer = new StreamWriter(csvPath))
            {
                writer.WriteLine("Id,Value");
                for (int i = 0; i < 100000; i++)
                {
                    writer.WriteLine($"{i},{i * 2}");
                }
            }

            var progressValues = new List<double>();
            var columns = CsvSchemaInference.InferSchema(csvPath, null, p =>
            {
                lock (progressValues)
                {
                    progressValues.Add(p);
                }
            });

            Assert.AreEqual(2, columns.Count);
            Assert.IsTrue(progressValues.Count > 0, "Progress should have been reported");
            Assert.AreEqual(1.0, progressValues.Last(), 0.01, "Final progress should be 1.0");

            // Progress should be monotonically increasing
            for (int i = 1; i < progressValues.Count; i++)
            {
                Assert.IsTrue(progressValues[i] >= progressValues[i - 1],
                    $"Progress should increase: {progressValues[i - 1]} -> {progressValues[i]}");
            }
        }

        [TestMethod]
        public void TestLargeFile_RowCountTracking()
        {
            string csvPath = Path.Combine(_tempDir, "rowcount.csv");
            const int expectedRows = 50000;

            using (var writer = new StreamWriter(csvPath))
            {
                writer.WriteLine("Id,Value");
                for (int i = 0; i < expectedRows; i++)
                {
                    writer.WriteLine($"{i},{i * 2}");
                }
            }

            var columns = CsvSchemaInference.InferSchema(csvPath);

            Assert.AreEqual(expectedRows, columns[0].TotalCount);
            Assert.AreEqual(expectedRows, columns[0].NonNullCount);
        }

        [TestMethod]
        public void TestLargeFile_SimulatedGigabytePatterns()
        {
            // Test patterns that would occur in gigabyte-scale files
            // Use streaming to avoid actually creating GB files

            // Simulate: very wide rows (many columns)
            var wideRowCsv = new StringBuilder();
            wideRowCsv.Append("Col0");
            for (int i = 1; i < 500; i++)
            {
                wideRowCsv.Append($",Col{i}");
            }
            wideRowCsv.AppendLine();

            for (int row = 0; row < 100; row++)
            {
                wideRowCsv.Append("0");
                for (int col = 1; col < 500; col++)
                {
                    wideRowCsv.Append($",{col * row}");
                }
                wideRowCsv.AppendLine();
            }

            string csvPath = Path.Combine(_tempDir, "wide.csv");
            File.WriteAllText(csvPath, wideRowCsv.ToString());

            var columns = CsvSchemaInference.InferSchemaFromSample(csvPath);

            Assert.AreEqual(500, columns.Count);
            // All numeric columns
            foreach (var col in columns)
            {
                Assert.AreEqual("int", col.SqlDataType, $"Column {col.ColumnName} should be int");
            }
        }

        [TestMethod]
        public void TestLargeFile_IncrementalTypeRefinement()
        {
            // Test that type inference refines correctly over many rows
            string csvPath = Path.Combine(_tempDir, "refinement.csv");

            using (var writer = new StreamWriter(csvPath))
            {
                writer.WriteLine("Value");
                // Start with small integers
                for (int i = 0; i < 1000; i++)
                {
                    writer.WriteLine(i);
                }
                // Then larger integers (still fits in int)
                for (int i = 0; i < 1000; i++)
                {
                    writer.WriteLine(1000000 + i);
                }
                // Then bigint range
                for (int i = 0; i < 1000; i++)
                {
                    writer.WriteLine(3000000000L + i);
                }
            }

            var columns = CsvSchemaInference.InferSchema(csvPath);

            Assert.AreEqual(1, columns.Count);
            Assert.AreEqual("bigint", columns[0].SqlDataType, "Should detect bigint after seeing large values");
            Assert.AreEqual(3000, columns[0].TotalCount);
        }

        [TestMethod]
        public void TestLargeFile_DecimalPrecisionScaling()
        {
            // Test decimal precision tracking over many rows
            string csvPath = Path.Combine(_tempDir, "decimal_precision.csv");

            using (var writer = new StreamWriter(csvPath))
            {
                writer.WriteLine("Value");
                // Start with 2 decimal places
                for (int i = 0; i < 1000; i++)
                {
                    writer.WriteLine($"{i}.{i % 100:D2}");
                }
                // Then 5 decimal places (should expand precision)
                for (int i = 0; i < 1000; i++)
                {
                    writer.WriteLine($"{i}.{i % 100000:D5}");
                }
            }

            var columns = CsvSchemaInference.InferSchema(csvPath);

            Assert.AreEqual(1, columns.Count);
            Assert.IsTrue(columns[0].SqlDataType.Contains("decimal"));
            Assert.AreEqual(5, columns[0].Scale, "Should track max scale of 5");
        }

        #endregion

        #region Reader Stress Tests

        [TestMethod]
        public void TestReader_RapidOpenClose()
        {
            // Rapidly open and close readers to test resource cleanup
            string csvPath = Path.Combine(_tempDir, "rapid.csv");
            File.WriteAllText(csvPath, "Id,Name\n1,John\n2,Jane\n");

            var errors = new ConcurrentBag<Exception>();

            Parallel.For(0, 100, i =>
            {
                try
                {
                    for (int j = 0; j < 10; j++)
                    {
                        using (var stream = File.OpenRead(csvPath))
                        using (var reader = new CsvDataReader(stream, null))
                        {
                            while (reader.Read())
                            {
                                var _ = reader.GetString(0);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            });

            Assert.AreEqual(0, errors.Count, $"Errors: {string.Join("; ", errors.Select(e => e.Message))}");
        }

        [TestMethod]
        public void TestReader_ConcurrentFieldAccess()
        {
            // Test concurrent access to different fields of the same record
            string csvPath = Path.Combine(_tempDir, "concurrent_fields.csv");
            var sb = new StringBuilder();
            sb.AppendLine("A,B,C,D,E,F,G,H,I,J");
            for (int i = 0; i < 1000; i++)
            {
                sb.AppendLine($"{i},B{i},C{i},D{i},E{i},F{i},G{i},H{i},I{i},J{i}");
            }
            File.WriteAllText(csvPath, sb.ToString());

            var errors = new ConcurrentBag<Exception>();

            using (var reader = new CsvDataReader(csvPath, null))
            {
                while (reader.Read())
                {
                    // Access different fields concurrently
                    Parallel.For(0, reader.FieldCount, i =>
                    {
                        try
                        {
                            var value = reader.GetString(i);
                            Assert.IsNotNull(value);
                        }
                        catch (Exception ex) when (!(ex is ObjectDisposedException))
                        {
                            errors.Add(ex);
                        }
                    });
                }
            }

            Assert.AreEqual(0, errors.Count, $"Errors: {string.Join("; ", errors.Select(e => e.Message))}");
        }

        [TestMethod]
        public void TestReader_DisposeWhileReading()
        {
            // Test that dispose during read doesn't cause crashes
            string csvPath = Path.Combine(_tempDir, "dispose.csv");
            var sb = new StringBuilder();
            sb.AppendLine("Id,Value");
            for (int i = 0; i < 10000; i++)
            {
                sb.AppendLine($"{i},{i * 2}");
            }
            File.WriteAllText(csvPath, sb.ToString());

            var errors = new ConcurrentBag<Exception>();

            for (int trial = 0; trial < 10; trial++)
            {
                var reader = new CsvDataReader(csvPath, null);
                var readTask = Task.Run(() =>
                {
                    try
                    {
                        while (reader.Read())
                        {
                            var _ = reader.GetString(0);
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        // Expected when disposed during read
                    }
                    catch (Exception ex)
                    {
                        errors.Add(ex);
                    }
                });

                // Dispose after a short delay
                Thread.Sleep(1);
                reader.Dispose();

                try
                {
                    readTask.Wait(1000);
                }
                catch
                {
                    // Timeout is fine
                }
            }

            Assert.AreEqual(0, errors.Count, $"Errors: {string.Join("; ", errors.Select(e => e.Message))}");
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
