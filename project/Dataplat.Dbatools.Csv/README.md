# Dataplat.Dbatools.Csv

High-performance CSV reader and writer for .NET from the trusted [dbatools](https://dbatools.io) project.

## Features

- **Streaming IDataReader** - Works seamlessly with SqlBulkCopy and other ADO.NET consumers
- **Compression Support** - Automatic handling of GZip, Deflate, Brotli (net8.0+), and ZLib (net6.0+)
- **Culture-Aware Parsing** - Configurable type converters for dates, numbers, booleans, and GUIDs
- **Flexible Delimiters** - Single or multi-character delimiters
- **Robust Error Handling** - Collect errors, throw on first error, or replace with defaults
- **Performance Optimized** - ArrayPool-based memory management, pooled StringBuilder for parsing
- **Security Built-in** - Decompression bomb protection with configurable limits
- **Smart Quote Handling** - Normalize curly/smart quotes from Word/Excel
- **Lenient Parsing Mode** - Handle real-world malformed CSV data gracefully
- **Duplicate Header Support** - Rename, ignore, or use first/last occurrence
- **Field Count Mismatch Handling** - Pad, truncate, or fail on row length mismatches

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
    HasHeaderRecord = true,
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
    ParseErrorAction = CsvParseErrorAction.CollectErrors
};

using var reader = new CsvDataReader("data.csv", options);
// Process records...

// Check collected errors
foreach (var error in reader.ParseErrors)
{
    Console.WriteLine($"Row {error.RowIndex}: {error.Message}");
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

// Distinguish between null and empty string
// ,, = null, ,"", = empty string
var options = new CsvReaderOptions
{
    DistinguishEmptyFromNull = true
};
```

## Configuration Options

### CsvReaderOptions

| Option | Default | Description |
|--------|---------|-------------|
| `Delimiter` | `","` | Field delimiter (supports multi-character) |
| `HasHeaderRow` | `true` | First row contains column names |
| `Culture` | `InvariantCulture` | Culture for parsing numbers/dates |
| `ParseErrorAction` | `ThrowException` | How to handle parse errors |
| `TrimmingOptions` | `None` | Whitespace trimming options |
| `CompressionType` | `None` | Compression format (auto-detected by default) |
| `MaxDecompressedSize` | `10GB` | Limit for decompression bomb protection |
| `QuoteMode` | `Strict` | RFC 4180 strict or lenient parsing mode |
| `DuplicateHeaderBehavior` | `ThrowException` | How to handle duplicate column names |
| `MismatchedFieldAction` | `ThrowException` | How to handle rows with wrong field count |
| `NormalizeQuotes` | `false` | Convert smart/curly quotes to ASCII quotes |
| `DistinguishEmptyFromNull` | `false` | Distinguish `,,` (null) from `,"",` (empty) |

## Security Considerations

- **QuoteMode.Lenient**: Deviates from RFC 4180 and may parse data differently than expected. Use only for known malformed data sources.
- **MismatchedFieldAction.PadWithNulls/TruncateExtra**: May mask data corruption or cause silent data loss. Use with caution on untrusted data.
- **MaxDecompressedSize**: Always set an appropriate limit when processing compressed files from untrusted sources to prevent decompression bomb attacks.
- **MaxQuotedFieldLength**: Set a limit when processing untrusted data to prevent memory exhaustion from malformed multiline quoted fields.

## License

MIT License - see the [dbatools.library](https://github.com/dataplat/dbatools.library) repository for details.
