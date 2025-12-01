using System.Diagnostics;
using System.Text;
using Dataplat.Dbatools.Csv.Reader;

namespace CsvBenchmarks;

public static class QuickTest
{
    public static void Run()
    {
        var dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData");
        Directory.CreateDirectory(dataDir);

        // Generate test files
        Console.WriteLine("Generating test files...");
        var testFilePath = Path.Combine(dataDir, "quicktest.csv");
        GenerateTestCsv(testFilePath, 100_000, 10);

        Console.WriteLine($"Test file size: {new FileInfo(testFilePath).Length / 1024.0:N0} KB");

        // Warm up
        Console.WriteLine("\nWarming up...");
        RunDataplat(testFilePath);
        RunLumenWorks(testFilePath);

        // Run multiple iterations
        int iterations = 5;
        var dataplatTimes = new List<double>();
        var lumenTimes = new List<double>();

        Console.WriteLine($"\nRunning {iterations} iterations...\n");

        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            int dataplatCount = RunDataplat(testFilePath);
            sw.Stop();
            dataplatTimes.Add(sw.Elapsed.TotalMilliseconds);
            Console.WriteLine($"Dataplat iteration {i + 1}: {sw.Elapsed.TotalMilliseconds:N2} ms ({dataplatCount:N0} rows)");

            sw.Restart();
            int lumenCount = RunLumenWorks(testFilePath);
            sw.Stop();
            lumenTimes.Add(sw.Elapsed.TotalMilliseconds);
            Console.WriteLine($"LumenWorks iteration {i + 1}: {sw.Elapsed.TotalMilliseconds:N2} ms ({lumenCount:N0} rows)");
            Console.WriteLine();
        }

        // Calculate averages
        double avgDataplat = dataplatTimes.Average();
        double avgLumen = lumenTimes.Average();
        double speedup = (avgLumen - avgDataplat) / avgLumen * 100;

        Console.WriteLine("=== RESULTS ===");
        Console.WriteLine($"Dataplat average: {avgDataplat:N2} ms");
        Console.WriteLine($"LumenWorks average: {avgLumen:N2} ms");
        Console.WriteLine($"Dataplat is {speedup:N1}% faster than LumenWorks");
    }

    private static int RunDataplat(string path)
    {
        int count = 0;
        using var reader = new CsvDataReader(path);
        while (reader.Read())
        {
            count++;
            for (int i = 0; i < reader.FieldCount; i++)
            {
                _ = reader.GetValue(i);
            }
        }
        return count;
    }

    private static int RunLumenWorks(string path)
    {
        int count = 0;
        using var textReader = new StreamReader(path);
        using var reader = new LumenWorks.Framework.IO.Csv.CsvReader(textReader, true);
        while (reader.ReadNextRecord())
        {
            count++;
            for (int i = 0; i < reader.FieldCount; i++)
            {
                _ = reader[i];
            }
        }
        return count;
    }

    private static void GenerateTestCsv(string path, int rows, int cols)
    {
        if (File.Exists(path))
            return;

        using var writer = new StreamWriter(path, false, Encoding.UTF8);

        // Header
        writer.WriteLine(string.Join(",", Enumerable.Range(0, cols).Select(i => $"Column{i}")));

        var random = new Random(42);
        var sb = new StringBuilder();

        for (int row = 0; row < rows; row++)
        {
            sb.Clear();
            for (int col = 0; col < cols; col++)
            {
                if (col > 0) sb.Append(',');

                string value = col switch
                {
                    0 => row.ToString(),
                    1 => $"Name{row}",
                    2 => random.Next(1, 100).ToString(),
                    3 => random.NextDouble().ToString("F4"),
                    4 => DateTime.Now.AddDays(-random.Next(365)).ToString("yyyy-MM-dd"),
                    5 => random.Next(0, 2) == 0 ? "true" : "false",
                    _ => $"Value{row}_{col}"
                };
                sb.Append(value);
            }
            writer.WriteLine(sb.ToString());
        }
    }
}
