using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using System.Data;
using System.Text;
using System.Globalization;
using Dataplat.Dbatools.Csv.Reader;
using nietras.SeparatedValues;
using CsvHelper;
using CsvHelper.Configuration;

// Alias to avoid ambiguity with Sylvan and CsvHelper
using DataplatCsvReader = Dataplat.Dbatools.Csv.Reader.CsvDataReader;

namespace CsvBenchmarks;

public class Program
{
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--quick")
        {
            QuickTest.Run();
            return;
        }

        var config = ManualConfig.Create(DefaultConfig.Instance)
            .AddDiagnoser(MemoryDiagnoser.Default)
            .WithSummaryStyle(SummaryStyle.Default.WithRatioStyle(RatioStyle.Percentage))
            .AddColumn(StatisticColumn.OperationsPerSecond)
            .AddExporter(MarkdownExporter.GitHub);

        BenchmarkRunner.Run<CsvReaderBenchmarks>(config);
    }
}

[MemoryDiagnoser]
[RankColumn]
public class CsvReaderBenchmarks
{
    private string _smallCsvPath;
    private string _mediumCsvPath;
    private string _largeCsvPath;
    private string _wideCsvPath;
    private string _quotedCsvPath;

    [GlobalSetup]
    public void Setup()
    {
        var dataDir = Path.Combine(Path.GetDirectoryName(typeof(CsvReaderBenchmarks).Assembly.Location), "TestData");
        Directory.CreateDirectory(dataDir);

        // Small: 1,000 rows, 10 columns
        _smallCsvPath = Path.Combine(dataDir, "small.csv");
        GenerateCsv(_smallCsvPath, 1_000, 10, quoteAll: false);

        // Medium: 100,000 rows, 10 columns
        _mediumCsvPath = Path.Combine(dataDir, "medium.csv");
        GenerateCsv(_mediumCsvPath, 100_000, 10, quoteAll: false);

        // Large: 1,000,000 rows, 10 columns
        _largeCsvPath = Path.Combine(dataDir, "large.csv");
        GenerateCsv(_largeCsvPath, 1_000_000, 10, quoteAll: false);

        // Wide: 100,000 rows, 50 columns
        _wideCsvPath = Path.Combine(dataDir, "wide.csv");
        GenerateCsv(_wideCsvPath, 100_000, 50, quoteAll: false);

        // Quoted: 100,000 rows, 10 columns with quoted fields
        _quotedCsvPath = Path.Combine(dataDir, "quoted.csv");
        GenerateCsv(_quotedCsvPath, 100_000, 10, quoteAll: true);

        Console.WriteLine($"Small CSV: {new FileInfo(_smallCsvPath).Length / 1024.0:N0} KB");
        Console.WriteLine($"Medium CSV: {new FileInfo(_mediumCsvPath).Length / 1024.0:N0} KB");
        Console.WriteLine($"Large CSV: {new FileInfo(_largeCsvPath).Length / 1024.0 / 1024.0:N1} MB");
        Console.WriteLine($"Wide CSV: {new FileInfo(_wideCsvPath).Length / 1024.0:N0} KB");
        Console.WriteLine($"Quoted CSV: {new FileInfo(_quotedCsvPath).Length / 1024.0:N0} KB");
    }

    private void GenerateCsv(string path, int rows, int cols, bool quoteAll)
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

                if (quoteAll)
                    sb.Append($"\"{value}\"");
                else
                    sb.Append(value);
            }
            writer.WriteLine(sb.ToString());
        }
    }

    // ===================== Small File Benchmarks =====================

    [Benchmark(Baseline = true, Description = "Dataplat-Small")]
    [BenchmarkCategory("Small")]
    public int Dataplat_Small()
    {
        int count = 0;
        using var reader = new DataplatCsvReader(_smallCsvPath);
        while (reader.Read())
        {
            count++;
            _ = reader.GetValue(0);
        }
        return count;
    }

    [Benchmark(Description = "LumenWorks-Small")]
    [BenchmarkCategory("Small")]
    public int LumenWorks_Small()
    {
        int count = 0;
        using var textReader = new StreamReader(_smallCsvPath);
        using var reader = new LumenWorks.Framework.IO.Csv.CsvReader(textReader, true);
        while (reader.ReadNextRecord())
        {
            count++;
            _ = reader[0];
        }
        return count;
    }

    // ===================== Medium File Benchmarks =====================

    [Benchmark(Description = "Dataplat-Medium")]
    [BenchmarkCategory("Medium")]
    public int Dataplat_Medium()
    {
        int count = 0;
        using var reader = new DataplatCsvReader(_mediumCsvPath);
        while (reader.Read())
        {
            count++;
            _ = reader.GetValue(0);
        }
        return count;
    }

    [Benchmark(Description = "LumenWorks-Medium")]
    [BenchmarkCategory("Medium")]
    public int LumenWorks_Medium()
    {
        int count = 0;
        using var textReader = new StreamReader(_mediumCsvPath);
        using var reader = new LumenWorks.Framework.IO.Csv.CsvReader(textReader, true);
        while (reader.ReadNextRecord())
        {
            count++;
            _ = reader[0];
        }
        return count;
    }

    // ===================== Large File Benchmarks =====================

    [Benchmark(Description = "Dataplat-Large")]
    [BenchmarkCategory("Large")]
    public int Dataplat_Large()
    {
        int count = 0;
        using var reader = new DataplatCsvReader(_largeCsvPath);
        while (reader.Read())
        {
            count++;
            _ = reader.GetValue(0);
        }
        return count;
    }

    [Benchmark(Description = "LumenWorks-Large")]
    [BenchmarkCategory("Large")]
    public int LumenWorks_Large()
    {
        int count = 0;
        using var textReader = new StreamReader(_largeCsvPath);
        using var reader = new LumenWorks.Framework.IO.Csv.CsvReader(textReader, true);
        while (reader.ReadNextRecord())
        {
            count++;
            _ = reader[0];
        }
        return count;
    }

    // ===================== Wide File Benchmarks =====================

    [Benchmark(Description = "Dataplat-Wide")]
    [BenchmarkCategory("Wide")]
    public int Dataplat_Wide()
    {
        int count = 0;
        using var reader = new DataplatCsvReader(_wideCsvPath);
        while (reader.Read())
        {
            count++;
            _ = reader.GetValue(0);
        }
        return count;
    }

    [Benchmark(Description = "LumenWorks-Wide")]
    [BenchmarkCategory("Wide")]
    public int LumenWorks_Wide()
    {
        int count = 0;
        using var textReader = new StreamReader(_wideCsvPath);
        using var reader = new LumenWorks.Framework.IO.Csv.CsvReader(textReader, true);
        while (reader.ReadNextRecord())
        {
            count++;
            _ = reader[0];
        }
        return count;
    }

    // ===================== Quoted File Benchmarks =====================

    [Benchmark(Description = "Dataplat-Quoted")]
    [BenchmarkCategory("Quoted")]
    public int Dataplat_Quoted()
    {
        int count = 0;
        using var reader = new DataplatCsvReader(_quotedCsvPath);
        while (reader.Read())
        {
            count++;
            _ = reader.GetValue(0);
        }
        return count;
    }

    [Benchmark(Description = "LumenWorks-Quoted")]
    [BenchmarkCategory("Quoted")]
    public int LumenWorks_Quoted()
    {
        int count = 0;
        using var textReader = new StreamReader(_quotedCsvPath);
        using var reader = new LumenWorks.Framework.IO.Csv.CsvReader(textReader, true);
        while (reader.ReadNextRecord())
        {
            count++;
            _ = reader[0];
        }
        return count;
    }

    // ===================== Modern Library Comparisons (Medium) =====================

    [Benchmark(Description = "Sep-Medium")]
    [BenchmarkCategory("Modern")]
    public int Sep_Medium()
    {
        int count = 0;
        using var reader = Sep.Reader().FromFile(_mediumCsvPath);
        foreach (var row in reader)
        {
            count++;
            _ = row[0].ToString();
        }
        return count;
    }

    [Benchmark(Description = "Sylvan-Medium")]
    [BenchmarkCategory("Modern")]
    public int Sylvan_Medium()
    {
        int count = 0;
        using var textReader = new StreamReader(_mediumCsvPath);
        using var reader = Sylvan.Data.Csv.CsvDataReader.Create(textReader);
        while (reader.Read())
        {
            count++;
            _ = reader.GetString(0);
        }
        return count;
    }

    [Benchmark(Description = "CsvHelper-Medium")]
    [BenchmarkCategory("Modern")]
    public int CsvHelper_Medium()
    {
        int count = 0;
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true
        };
        using var textReader = new StreamReader(_mediumCsvPath);
        using var csv = new CsvHelper.CsvReader(textReader, config);
        csv.Read();
        csv.ReadHeader();
        while (csv.Read())
        {
            count++;
            _ = csv.GetField(0);
        }
        return count;
    }

    [Benchmark(Description = "Dataplat-Medium-Modern")]
    [BenchmarkCategory("Modern")]
    public int Dataplat_Medium_Modern()
    {
        int count = 0;
        using var reader = new Dataplat.Dbatools.Csv.Reader.CsvDataReader(_mediumCsvPath);
        while (reader.Read())
        {
            count++;
            _ = reader.GetValue(0);
        }
        return count;
    }

    // ===================== Modern Library Comparisons (Large) =====================

    [Benchmark(Description = "Sep-Large")]
    [BenchmarkCategory("ModernLarge")]
    public int Sep_Large()
    {
        int count = 0;
        using var reader = Sep.Reader().FromFile(_largeCsvPath);
        foreach (var row in reader)
        {
            count++;
            _ = row[0].ToString();
        }
        return count;
    }

    [Benchmark(Description = "Sylvan-Large")]
    [BenchmarkCategory("ModernLarge")]
    public int Sylvan_Large()
    {
        int count = 0;
        using var textReader = new StreamReader(_largeCsvPath);
        using var reader = Sylvan.Data.Csv.CsvDataReader.Create(textReader);
        while (reader.Read())
        {
            count++;
            _ = reader.GetString(0);
        }
        return count;
    }

    [Benchmark(Description = "CsvHelper-Large")]
    [BenchmarkCategory("ModernLarge")]
    public int CsvHelper_Large()
    {
        int count = 0;
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true
        };
        using var textReader = new StreamReader(_largeCsvPath);
        using var csv = new CsvHelper.CsvReader(textReader, config);
        csv.Read();
        csv.ReadHeader();
        while (csv.Read())
        {
            count++;
            _ = csv.GetField(0);
        }
        return count;
    }

    [Benchmark(Description = "Dataplat-Large-Modern")]
    [BenchmarkCategory("ModernLarge")]
    public int Dataplat_Large_Modern()
    {
        int count = 0;
        using var reader = new Dataplat.Dbatools.Csv.Reader.CsvDataReader(_largeCsvPath);
        while (reader.Read())
        {
            count++;
            _ = reader.GetValue(0);
        }
        return count;
    }

    // ===================== All Values Access Benchmarks =====================

    [Benchmark(Description = "Dataplat-AllValues")]
    [BenchmarkCategory("AllValues")]
    public int Dataplat_AllValues()
    {
        int count = 0;
        var options = new CsvReaderOptions { BufferSize = 65536 };
        using var reader = new DataplatCsvReader(_mediumCsvPath, options);
        object[] values = new object[reader.FieldCount];
        while (reader.Read())
        {
            count++;
            reader.GetValues(values);
        }
        return count;
    }

    [Benchmark(Description = "LumenWorks-AllValues")]
    [BenchmarkCategory("AllValues")]
    public int LumenWorks_AllValues()
    {
        int count = 0;
        using var textReader = new StreamReader(_mediumCsvPath);
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
}
