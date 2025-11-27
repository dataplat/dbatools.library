using System;
using System.Collections.Generic;
using System.Text;
using Dataplat.Dbatools.Csv.Compression;
using Dataplat.Dbatools.Csv.TypeConverters;

namespace Dataplat.Dbatools.Csv.Reader
{
    /// <summary>
    /// Configuration options for the CSV reader.
    /// </summary>
    public sealed class CsvReaderOptions
    {
        /// <summary>
        /// Gets or sets whether the first row contains column headers.
        /// Default is true.
        /// </summary>
        public bool HasHeaderRow { get; set; } = true;

        /// <summary>
        /// Gets or sets the number of rows to skip at the beginning of the file
        /// before reading headers or data. Addresses issue #7173 (FirstRow feature).
        /// </summary>
        public int SkipRows { get; set; }

        /// <summary>
        /// Gets or sets the field delimiter. Can be multiple characters.
        /// Default is comma. Addresses issue #6488 (multi-character delimiters).
        /// </summary>
        public string Delimiter { get; set; } = ",";

        /// <summary>
        /// Gets or sets the quote character used to enclose fields.
        /// Default is double-quote.
        /// </summary>
        public char Quote { get; set; } = '"';

        /// <summary>
        /// Gets or sets the escape character used within quoted fields.
        /// Default is double-quote (RFC 4180 standard).
        /// </summary>
        public char Escape { get; set; } = '"';

        /// <summary>
        /// Gets or sets the comment character. Lines starting with this character are ignored.
        /// Default is #.
        /// </summary>
        public char Comment { get; set; } = '#';

        /// <summary>
        /// Gets or sets the value trimming options.
        /// Default is None.
        /// </summary>
        public ValueTrimmingOptions TrimmingOptions { get; set; } = ValueTrimmingOptions.None;

        /// <summary>
        /// Gets or sets the internal buffer size in bytes.
        /// Default is 65536 (64KB) for better performance than LumenWorks' 4KB.
        /// </summary>
        public int BufferSize { get; set; } = 65536;

        /// <summary>
        /// Gets or sets the file encoding.
        /// Default is UTF-8.
        /// </summary>
        public Encoding Encoding { get; set; } = Encoding.UTF8;

        /// <summary>
        /// Gets or sets the string value to treat as null.
        /// Default is null (no special null handling).
        /// </summary>
        public string NullValue { get; set; }

        /// <summary>
        /// Gets or sets the action to take when a parse error occurs.
        /// Default is ThrowException.
        /// </summary>
        public CsvParseErrorAction ParseErrorAction { get; set; } = CsvParseErrorAction.ThrowException;

        /// <summary>
        /// Gets or sets whether to skip empty lines.
        /// Default is true.
        /// </summary>
        public bool SkipEmptyLines { get; set; } = true;

        /// <summary>
        /// Gets or sets whether quoted fields can span multiple lines.
        /// Default is true.
        /// </summary>
        public bool AllowMultilineFields { get; set; } = true;

        /// <summary>
        /// Gets or sets the maximum length of a quoted field in characters.
        /// Used to prevent memory issues with malformed data.
        /// Default is 0 (no limit).
        /// </summary>
        public int MaxQuotedFieldLength { get; set; }

        /// <summary>
        /// Gets or sets whether to detect the compression type automatically.
        /// Default is true.
        /// </summary>
        public bool AutoDetectCompression { get; set; } = true;

        /// <summary>
        /// Gets or sets the explicit compression type.
        /// Only used when AutoDetectCompression is false.
        /// </summary>
        public CompressionType CompressionType { get; set; } = CompressionType.None;

        /// <summary>
        /// Gets or sets the type converter registry to use for automatic type conversion.
        /// Default is TypeConverterRegistry.Default.
        /// </summary>
        public TypeConverterRegistry TypeConverterRegistry { get; set; } = TypeConverterRegistry.Default;

        /// <summary>
        /// Gets or sets whether to use column defaults when values are null or empty.
        /// Default is false.
        /// </summary>
        public bool UseColumnDefaults { get; set; }

        /// <summary>
        /// Gets or sets the static columns to inject into each record.
        /// Addresses issue #6676.
        /// </summary>
        public List<StaticColumn> StaticColumns { get; set; }

        /// <summary>
        /// Gets or sets the column type mappings.
        /// Key is the column name, value is the target type.
        /// </summary>
        public Dictionary<string, Type> ColumnTypes { get; set; }

        /// <summary>
        /// Gets or sets a custom date format for DateTime parsing.
        /// Addresses issue #9694.
        /// </summary>
        public string[] DateTimeFormats { get; set; }

        /// <summary>
        /// Gets or sets whether to collect parse errors instead of throwing.
        /// When true, errors are collected and can be retrieved after reading.
        /// Addresses issue #6899 (view/log bad rows).
        /// </summary>
        public bool CollectParseErrors { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of parse errors to collect before stopping.
        /// Default is 1000. Set to 0 for unlimited.
        /// </summary>
        public int MaxParseErrors { get; set; } = 1000;

        /// <summary>
        /// Gets or sets the column names to include (filter).
        /// If null or empty, all columns are included.
        /// </summary>
        public HashSet<string> IncludeColumns { get; set; }

        /// <summary>
        /// Gets or sets the column names to exclude.
        /// Applied after IncludeColumns filter.
        /// </summary>
        public HashSet<string> ExcludeColumns { get; set; }

        /// <summary>
        /// Creates a default options instance.
        /// </summary>
        public static CsvReaderOptions Default => new CsvReaderOptions();

        /// <summary>
        /// Creates options for tab-delimited files.
        /// </summary>
        public static CsvReaderOptions TabDelimited => new CsvReaderOptions { Delimiter = "\t" };

        /// <summary>
        /// Creates options for pipe-delimited files.
        /// </summary>
        public static CsvReaderOptions PipeDelimited => new CsvReaderOptions { Delimiter = "|" };

        /// <summary>
        /// Creates options for semicolon-delimited files (common in European locales).
        /// </summary>
        public static CsvReaderOptions SemicolonDelimited => new CsvReaderOptions { Delimiter = ";" };

        /// <summary>
        /// Creates a clone of these options.
        /// </summary>
        public CsvReaderOptions Clone()
        {
            return new CsvReaderOptions
            {
                HasHeaderRow = HasHeaderRow,
                SkipRows = SkipRows,
                Delimiter = Delimiter,
                Quote = Quote,
                Escape = Escape,
                Comment = Comment,
                TrimmingOptions = TrimmingOptions,
                BufferSize = BufferSize,
                Encoding = Encoding,
                NullValue = NullValue,
                ParseErrorAction = ParseErrorAction,
                SkipEmptyLines = SkipEmptyLines,
                AllowMultilineFields = AllowMultilineFields,
                MaxQuotedFieldLength = MaxQuotedFieldLength,
                AutoDetectCompression = AutoDetectCompression,
                CompressionType = CompressionType,
                TypeConverterRegistry = TypeConverterRegistry,
                UseColumnDefaults = UseColumnDefaults,
                StaticColumns = StaticColumns != null ? new List<StaticColumn>(StaticColumns) : null,
                ColumnTypes = ColumnTypes != null ? new Dictionary<string, Type>(ColumnTypes) : null,
                DateTimeFormats = DateTimeFormats,
                CollectParseErrors = CollectParseErrors,
                MaxParseErrors = MaxParseErrors,
                IncludeColumns = IncludeColumns != null ? new HashSet<string>(IncludeColumns) : null,
                ExcludeColumns = ExcludeColumns != null ? new HashSet<string>(ExcludeColumns) : null
            };
        }
    }
}
