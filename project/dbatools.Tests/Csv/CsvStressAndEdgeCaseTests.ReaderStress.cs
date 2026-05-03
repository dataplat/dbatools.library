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
    public partial class CsvStressAndEdgeCaseTests
    {

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



        private CsvDataReader CreateReaderFromString(string csv, CsvReaderOptions options = null)
        {
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var textReader = new StreamReader(stream);
            return new CsvDataReader(textReader, options);
        }

    }
}
