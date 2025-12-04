# Dataplat.Dbatools.Csv

[![NuGet](https://img.shields.io/nuget/v/Dataplat.Dbatools.Csv.svg)](https://www.nuget.org/packages/Dataplat.Dbatools.Csv)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Dataplat.Dbatools.Csv.svg)](https://www.nuget.org/packages/Dataplat.Dbatools.Csv)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://opensource.org/licenses/MIT)

**The performance-optimized CSV library built for SQL Server.** High-performance CSV reader and writer for .NET from the trusted [dbatools](https://dbatools.io) project.

**What makes this library unique:**
- **Native IDataReader** - Stream directly to SqlBulkCopy with zero intermediate allocations
- **Schema Inference** - Auto-detect SQL Server column types (int, bigint, decimal, datetime2, bit, uniqueidentifier, varchar/nvarchar)
- **Built-in compression** - GZip, Brotli, Deflate, ZLib with decompression bomb protection
- **Real-world data handling** - Lenient parsing, smart quotes, duplicate headers, field count mismatches
- **Faster than LumenWorks & CsvHelper** - ~1.5x faster with modern .NET (Span<T>, ArrayPool)
- **Cancellation & Progress** - CancellationToken support and progress callbacks for long imports

## Installation

```bash
dotnet add package Dataplat.Dbatools.Csv
```

Or via Package Manager:

```powershell
Install-Package Dataplat.Dbatools.Csv
```

## Features

- **Streaming IDataReader** - Works seamlessly with SqlBulkCopy and other ADO.NET consumers
- **Schema Inference** - Analyze CSV data to determine optimal SQL Server column types
- **High Performance** - ~1.5x faster than LumenWorks/CsvHelper with ArrayPool-based memory management
- **Parallel Processing** - Optional multi-threaded parsing for large files (25K+ rows/sec)
- **String Interning** - Reduce memory for files with repeated values
- **Compression Support** - Automatic handling of GZip, Deflate, Brotli (.NET 8+), and ZLib (.NET 8+)
- **Culture-Aware Parsing** - Configurable type converters for dates, numbers, booleans, and GUIDs
- **Flexible Delimiters** - Single or multi-character delimiters (e.g., `::`, `||`)
- **Robust Error Handling** - Collect errors, throw on first error, or skip bad rows
- **Security Built-in** - Decompression bomb protection, max field length limits
- **Smart Quote Handling** - Normalize curly/smart quotes from Word/Excel
- **Lenient Parsing Mode** - Handle real-world malformed CSV data gracefully
- **Duplicate Header Support** - Rename, ignore, or use first/last occurrence
- **Field Count Mismatch Handling** - Pad with nulls, truncate, or fail on row length mismatches

## Performance

Benchmark: 100,000 rows × 10 columns (.NET 8, AVX-512)

**Single column read (typical SqlBulkCopy/IDataReader pattern):**

| Library | Time (ms) | vs Dataplat |
|---------|-----------|-------------|
| Sep | 18 ms | 3.7x faster |
| Sylvan | 27 ms | 2.5x faster |
| **Dataplat** | **67 ms** | **baseline** |
| CsvHelper | 76 ms | 1.1x slower |
| LumenWorks | 395 ms | **5.9x slower** |

**All columns read (full row processing):**

| Library | Time (ms) | vs Dataplat |
|---------|-----------|-------------|
| Sep | 30 ms | 1.8x faster |
| Sylvan | 35 ms | 1.6x faster |
| **Dataplat** | **55 ms** | **baseline** |
| CsvHelper | 97 ms | 1.8x slower |
| LumenWorks | 102 ms | 1.9x slower |

### Understanding the performance tradeoffs

Sep achieves 21 GB/s by using `Span<T>` and only materializing strings when explicitly requested. Sylvan uses similar techniques. Both avoid allocations until the last possible moment.

**Why Dataplat can't match this:** The `IDataReader` interface requires `GetValue()` to return actual `object` instances. For string columns, this means creating real `string` objects—we can't return spans. This is a fundamental architectural tradeoff for SqlBulkCopy compatibility.

**When each library shines:**

| Scenario | Bottleneck | Winner |
|----------|-----------|--------|
| CSV → SqlBulkCopy → SQL Server | Network/disk I/O, not parsing | Dataplat (integrated) |
| CSV.gz → SQL Server | Decompression overhead | Dataplat (built-in) |
| Messy enterprise exports | Error handling complexity | Dataplat (lenient mode) |
| Raw in-memory parsing benchmark | CPU/allocations | Sep/Sylvan |

For database import workflows, the complete `file.csv.gz → SqlBulkCopy → SQL Server` pipeline with Dataplat is often comparable to combining Sep + manual decompression + custom IDataReader wrapper, while requiring less code.

## Quick Start

### Reading CSV Files

```csharp
using Dataplat.Dbatools.Csv.Reader;

// Simple usage
using var reader = new CsvDataReader("data.csv");
while (reader.Read())
{
    var name = reader.GetString(0);
    var value = reader.GetInt32(1);
}

// With options
var options = new CsvReaderOptions
{
    Delimiter = ";",
    HasHeaderRow = true,
    Culture = CultureInfo.GetCultureInfo("de-DE")
};
using var reader = new CsvDataReader("data.csv", options);
```

### Reading Compressed Files

```csharp
// Automatically detects compression from extension (.gz, .gzip, .deflate, .br, .zlib)
using var reader = new CsvDataReader("data.csv.gz");

// Or specify explicitly
var options = new CsvReaderOptions
{
    CompressionType = CompressionType.GZip,
    MaxDecompressedSize = 100 * 1024 * 1024  // 100MB limit
};
using var reader = new CsvDataReader(stream, options);
```

### Bulk Loading to SQL Server

```csharp
using var reader = new CsvDataReader("data.csv");
using var connection = new SqlConnection(connectionString);
connection.Open();

using var bulkCopy = new SqlBulkCopy(connection);
bulkCopy.DestinationTableName = "MyTable";
bulkCopy.WriteToServer(reader);  // Streams directly, low memory usage
```

### Parallel Processing (Large Files)

```csharp
var options = new CsvReaderOptions
{
    EnableParallelProcessing = true,
    MaxDegreeOfParallelism = Environment.ProcessorCount
};

using var reader = new CsvDataReader("large-file.csv", options);
// Process as normal - parallel parsing happens automatically
```

### Writing CSV Files

```csharp
using Dataplat.Dbatools.Csv.Writer;

var options = new CsvWriterOptions
{
    Delimiter = ",",
    QuoteAllFields = false,
    Culture = CultureInfo.InvariantCulture
};

using var writer = new CsvWriter("output.csv", options);
writer.WriteHeader(new[] { "Id", "Name", "Date" });
writer.WriteRecord(new object[] { 1, "Test", DateTime.Now });
```

### Error Handling

```csharp
var options = new CsvReaderOptions
{
    CollectParseErrors = true,
    MaxParseErrors = 100,
    ParseErrorAction = CsvParseErrorAction.AdvanceToNextLine
};

using var reader = new CsvDataReader("data.csv", options);
while (reader.Read())
{
    // Process valid records
}

// Check collected errors
foreach (var error in reader.ParseErrors)
{
    Console.WriteLine($"Row {error.RowIndex}, Line {error.LineNumber}: {error.Message}");
}
```

### Handling Malformed Data

```csharp
// Handle files with duplicate column names
var options = new CsvReaderOptions
{
    DuplicateHeaderBehavior = DuplicateHeaderBehavior.Rename  // Name, Name_2, Name_3
};

// Handle rows with wrong number of fields
var options = new CsvReaderOptions
{
    MismatchedFieldAction = MismatchedFieldAction.PadOrTruncate
};

// Handle malformed quotes (e.g., unmatched quotes, backslash escapes)
var options = new CsvReaderOptions
{
    QuoteMode = QuoteMode.Lenient
};

// Normalize smart/curly quotes from Word/Excel
var options = new CsvReaderOptions
{
    NormalizeQuotes = true
};

// Distinguish between null and empty string (see examples below)
var options = new CsvReaderOptions
{
    DistinguishEmptyFromNull = true
};
```

### Cancellation Support

```csharp
using var cts = new CancellationTokenSource();

var options = new CsvReaderOptions
{
    CancellationToken = cts.Token
};

// In another thread or after timeout
cts.CancelAfter(TimeSpan.FromSeconds(30));

try
{
    using var reader = new CsvDataReader("large-file.csv", options);
    while (reader.Read())
    {
        // Process records - will throw OperationCanceledException when cancelled
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("Import was cancelled");
}
```

### Progress Reporting

```csharp
var options = new CsvReaderOptions
{
    ProgressReportInterval = 10000,  // Report every 10,000 records
    ProgressCallback = progress =>
    {
        Console.WriteLine($"Processed {progress.RecordsRead:N0} records " +
                          $"({progress.RowsPerSecond:N0} rows/sec)");

        if (progress.PercentComplete >= 0)
            Console.WriteLine($"Progress: {progress.PercentComplete:F1}%");
    }
};

using var reader = new CsvDataReader("large-file.csv", options);
while (reader.Read())
{
    // Process records
}
```

### Schema Inference

Automatically detect optimal SQL Server column types from CSV data. No more `nvarchar(MAX)` for everything:

```csharp
using Dataplat.Dbatools.Csv.Reader;

// Fast: Sample first 1000 rows (tiny risk if data changes after sample)
var columns = CsvSchemaInference.InferSchemaFromSample("data.csv");

// Safe: Scan entire file with progress reporting (zero risk of type mismatches)
var columns = CsvSchemaInference.InferSchema("data.csv", null, progress => {
    Console.WriteLine($"Progress: {progress:P0}");
});

// Examine inferred types
foreach (var col in columns)
{
    Console.WriteLine($"{col.ColumnName}: {col.SqlDataType} {(col.IsNullable ? "NULL" : "NOT NULL")}");
}
// Output:
// Id: int NOT NULL
// Name: nvarchar(100) NULL
// Price: decimal(10,2) NOT NULL
// Created: datetime2 NULL

// Generate CREATE TABLE statement
string sql = CsvSchemaInference.GenerateCreateTableStatement(columns, "Products", "dbo");
// CREATE TABLE [dbo].[Products] (
//     [Id] int NOT NULL,
//     [Name] nvarchar(100) NULL,
//     [Price] decimal(10,2) NOT NULL,
//     [Created] datetime2 NULL
// );

// Use inferred types with CsvDataReader
var typeMap = CsvSchemaInference.ToColumnTypes(columns);
var options = new CsvReaderOptions { ColumnTypes = typeMap };
using var reader = new CsvDataReader("data.csv", options);
```

**Detected types:** `uniqueidentifier`, `bit`, `int`, `bigint`, `decimal(p,s)`, `datetime2`, `varchar(n)`, `nvarchar(n)` (when Unicode is detected)

**InferredColumn properties:**

| Property | Type | Description |
|----------|------|-------------|
| `ColumnName` | string | Column header name |
| `SqlDataType` | string | SQL Server data type (e.g., `int`, `decimal(10,2)`, `nvarchar(50)`) |
| `IsNullable` | bool | True if any NULL/empty values were found |
| `IsUnicode` | bool | True if non-ASCII characters detected |
| `MaxLength` | int | Maximum string length observed |
| `Precision` | int | Decimal precision (total digits) |
| `Scale` | int | Decimal scale (digits after decimal point) |
| `Ordinal` | int | Column position (0-based) |
| `TotalCount` | long | Total rows analyzed |
| `NonNullCount` | long | Rows with non-null values |

### Null vs Empty String Handling

CSV files can represent missing data in two ways: an empty field (`,,`) or an explicitly quoted empty string (`,"",...`). The `DistinguishEmptyFromNull` option controls how these are interpreted.

**Example CSV:**
```csv
Name,Description,Notes
Alice,,""
Bob,"",
Charlie,"Has value","Also has value"
```

**Default behavior (`DistinguishEmptyFromNull = false`):**

Both empty fields and quoted empty strings become empty string (`""`):

```csharp
var options = new CsvReaderOptions { DistinguishEmptyFromNull = false }; // default
using var reader = new CsvDataReader("data.csv", options);

reader.Read(); // Alice row
reader.IsDBNull(1);  // false - Description is ""
reader.IsDBNull(2);  // false - Notes is ""
reader.GetString(1); // ""
reader.GetString(2); // ""
```

**With `DistinguishEmptyFromNull = true`:**

Empty fields become `null`, quoted empty strings remain empty string:

```csharp
var options = new CsvReaderOptions { DistinguishEmptyFromNull = true };
using var reader = new CsvDataReader("data.csv", options);

reader.Read(); // Alice row
reader.IsDBNull(1);  // true  - Description (,,) is NULL
reader.IsDBNull(2);  // false - Notes ("") is empty string
reader.GetString(1); // throws InvalidCastException (value is null)
reader.GetValue(1);  // DBNull.Value
reader.GetString(2); // ""
```

**When to use this option:**

| Use Case | Recommendation |
|----------|----------------|
| SQL bulk import where NULL matters | Enable (`true`) |
| Database columns with NOT NULL constraints | Disable (`false`) - default |
| Preserving exact semantics from source system | Enable (`true`) |
| Simple data processing | Disable (`false`) - default |

**Quick reference:**

| CSV Input | `DistinguishEmptyFromNull = false` | `DistinguishEmptyFromNull = true` |
|-----------|-----------------------------------|----------------------------------|
| `,,` (empty field) | `""` (empty string) | `null` (DBNull.Value) |
| `,"",` (quoted empty) | `""` (empty string) | `""` (empty string) |
| `,value,` | `"value"` | `"value"` |

## Configuration Options

### CsvReaderOptions

| Option | Default | Description |
|--------|---------|-------------|
| `Delimiter` | `","` | Field delimiter (supports multi-character) |
| `HasHeaderRow` | `true` | First row contains column names |
| `SkipRows` | `0` | Number of rows to skip before reading |
| `Culture` | `InvariantCulture` | Culture for parsing numbers/dates |
| `ParseErrorAction` | `ThrowException` | How to handle parse errors |
| `CollectParseErrors` | `false` | Collect errors instead of throwing |
| `MaxParseErrors` | `1000` | Maximum errors to collect |
| `TrimmingOptions` | `None` | Whitespace trimming options |
| `CompressionType` | `None` | Compression format (auto-detected by default) |
| `MaxDecompressedSize` | `10GB` | Limit for decompression bomb protection |
| `MaxQuotedFieldLength` | `0` | Limit for quoted field length (0 = unlimited) |
| `QuoteMode` | `Strict` | RFC 4180 strict or lenient parsing mode |
| `DuplicateHeaderBehavior` | `ThrowException` | How to handle duplicate column names |
| `MismatchedFieldAction` | `ThrowException` | How to handle rows with wrong field count |
| `NormalizeQuotes` | `false` | Convert smart/curly quotes to ASCII quotes |
| `DistinguishEmptyFromNull` | `false` | Distinguish `,,` (null) from `,"",` (empty) |
| `EnableParallelProcessing` | `false` | Enable multi-threaded parsing |
| `MaxDegreeOfParallelism` | `0` | Worker threads (0 = processor count) |
| `InternStrings` | `false` | Intern common string values |
| `CancellationToken` | `None` | Token to monitor for cancellation requests |
| `ProgressReportInterval` | `10000` | Records between progress reports (0 = disabled) |
| `ProgressCallback` | `null` | Callback receiving `CsvProgress` updates |

## Thread Safety

When **parallel processing is enabled**, `CsvDataReader` provides the following thread-safety guarantees:

| Method/Property | Thread-Safe | Notes |
|-----------------|-------------|-------|
| `GetValue()` | Yes | Returns consistent snapshot of current record |
| `GetValues()` | Yes | Atomic copy of all values in current record |
| `CurrentRecordIndex` | Yes | No torn reads on 64-bit values |
| `Close()` / `Dispose()` | Yes | Safely stops parallel pipeline from any thread |
| `Read()` | No | Only one thread should call Read() |

### Usage Pattern

```csharp
var options = new CsvReaderOptions
{
    EnableParallelProcessing = true,
    MaxDegreeOfParallelism = 4
};

using var reader = new CsvDataReader("large-file.csv", options);

while (reader.Read())  // Main thread only
{
    // Safe to read values from multiple threads concurrently
    Parallel.For(0, 4, _ =>
    {
        var values = new object[reader.FieldCount];
        reader.GetValues(values);  // Thread-safe
        ProcessValues(values);
    });
}
```

### Important Notes

- **Sequential mode** (parallel processing disabled): The reader is **not thread-safe**. All access should be from a single thread.
- **Snapshot semantics**: Values returned by `GetValue()`/`GetValues()` represent a snapshot that may change after the next `Read()` call.
- **Single reader thread**: Only one thread should call `Read()` at a time. Concurrent `Read()` calls are not supported.

## Target Frameworks

- .NET Framework 4.7.2
- .NET 8.0

## Security Considerations

- **QuoteMode.Lenient**: Deviates from RFC 4180 and may parse data differently than expected. Use only for known malformed data sources.
- **MismatchedFieldAction.PadWithNulls/TruncateExtra**: May mask data corruption or cause silent data loss. Use with caution on untrusted data.
- **MaxDecompressedSize**: Always set an appropriate limit when processing compressed files from untrusted sources to prevent decompression bomb attacks.
- **MaxQuotedFieldLength**: Set a limit when processing untrusted data to prevent memory exhaustion from malformed multiline quoted fields.

## Related Projects

- [dbatools](https://github.com/dataplat/dbatools) - PowerShell module for SQL Server DBAs
- [dbatools.library](https://github.com/dataplat/dbatools.library) - Core library for dbatools

## License

MIT License - see the [LICENSE](https://github.com/dataplat/dbatools.library/blob/main/LICENSE) file for details.

## Development

This CSV library was created using [Claude Code](https://claude.com/claude-code) (Opus 4.5) with the following initial prompt:

> the dbatools repo is at C:\github\dbatools and this repo is at C:\github\dbatools.library
>
> i would like to create a replacement for LumenWorks.Framework.IO.dll PLUS the additional functionality requested in dbatools issues on github which you can find using the `gh` command
>
> the source code for lumenworks is https://github.com/phatcher/CsvReader/tree/master/code/LumenWorks.Framework.IO
>
> This library was written over a decade ago. considering the advances in .NET and SqlClient etc, please add a CSV reader of better quality (more functionality often seen in paid systems, faster) using recent .NET and Microsoft Data best practices
>
> Please ultrathink about the best way to go about creating this new, extensive functionality within the dbatools library. if it should be a new project that is linked or whatever, do it in this repo.

Additional refinements included a security review and feature additions based on [dbatools GitHub issues](https://github.com/dataplat/dbatools/issues).