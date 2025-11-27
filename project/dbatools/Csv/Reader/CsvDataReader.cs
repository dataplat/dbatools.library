using System;
using System.Buffers;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Dataplat.Dbatools.Csv.Compression;
using Dataplat.Dbatools.Csv.TypeConverters;

namespace Dataplat.Dbatools.Csv.Reader
{
    /// <summary>
    /// High-performance CSV reader that implements IDataReader for SqlBulkCopy compatibility.
    /// Supports multi-character delimiters, type conversion, compression, error tracking, and static columns.
    /// Uses Span-based parsing and ArrayPool for maximum performance.
    /// </summary>
    public sealed class CsvDataReader : IDataReader
    {
        #region Fields

        private readonly TextReader _reader;
        private readonly CsvReaderOptions _options;
        private readonly bool _ownsReader;
        private readonly List<CsvColumn> _columns;
        private readonly List<StaticColumn> _staticColumns;
        private readonly List<CsvParseError> _parseErrors;

        private string[] _currentRecord;
        private bool[] _currentRecordWasQuoted;  // Track which fields were quoted for null vs empty
        private string[] _recordBuffer;
        private bool[] _quotedBuffer;
        private object[] _convertedValues;
        private bool _isInitialized;
        private bool _isClosed;
        private long _currentRecordIndex = -1;
        private long _currentLineNumber;

        // Buffer for efficient reading - using ArrayPool
        private char[] _buffer;
        private bool _bufferFromPool;
        private int _bufferLength;
        private int _bufferPosition;
        private bool _endOfStream;

        // Field parsing state
        private StringBuilder _lineBuilder;
        private List<FieldInfo> _fieldsBuffer;

        // Header name tracking for duplicate detection
        private readonly Dictionary<string, int> _headerNameCounts;

        // Cached max source index to avoid LINQ per-row
        private int _maxSourceIndex = -1;

        // Reusable StringBuilder for quoted field parsing to reduce allocations
        private StringBuilder _quotedFieldBuilder;

        // Smart quote characters for normalization
        private const char LeftSingleQuote = '\u2018';  // '
        private const char RightSingleQuote = '\u2019'; // '
        private const char LeftDoubleQuote = '\u201C';  // "
        private const char RightDoubleQuote = '\u201D'; // "

        #endregion

        #region Field Info Structure

        /// <summary>
        /// Holds information about a parsed field including its value and whether it was quoted.
        /// </summary>
        private struct FieldInfo
        {
            public string Value;
            public bool WasQuoted;

            public FieldInfo(string value, bool wasQuoted)
            {
                Value = value;
                WasQuoted = wasQuoted;
            }
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new CSV reader for the specified file.
        /// </summary>
        public CsvDataReader(string filePath) : this(filePath, null)
        {
        }

        /// <summary>
        /// Creates a new CSV reader for the specified file with options.
        /// </summary>
        public CsvDataReader(string filePath, CsvReaderOptions options)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            _options = options ?? new CsvReaderOptions();

            Stream stream = CompressionHelper.OpenFileForReading(filePath, _options.AutoDetectCompression, _options.MaxDecompressedSize);
            _reader = new StreamReader(stream, _options.Encoding, detectEncodingFromByteOrderMarks: true,
                bufferSize: _options.BufferSize);
            _ownsReader = true;

            InitializeBuffers();
            _columns = new List<CsvColumn>();
            _staticColumns = _options.StaticColumns != null ? new List<StaticColumn>(_options.StaticColumns) : new List<StaticColumn>();
            _parseErrors = _options.CollectParseErrors ? new List<CsvParseError>() : null;
            _headerNameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Creates a new CSV reader for the specified TextReader.
        /// </summary>
        public CsvDataReader(TextReader reader, CsvReaderOptions options = null)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _options = options ?? new CsvReaderOptions();
            _ownsReader = false;

            InitializeBuffers();
            _columns = new List<CsvColumn>();
            _staticColumns = _options.StaticColumns != null ? new List<StaticColumn>(_options.StaticColumns) : new List<StaticColumn>();
            _parseErrors = _options.CollectParseErrors ? new List<CsvParseError>() : null;
            _headerNameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Creates a new CSV reader for the specified Stream.
        /// </summary>
        public CsvDataReader(Stream stream, CsvReaderOptions options = null)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            _options = options ?? new CsvReaderOptions();

            CompressionType compressionType = _options.AutoDetectCompression
                ? CompressionHelper.DetectFromStream(stream)
                : _options.CompressionType;

            Stream decompressedStream = CompressionHelper.WrapForDecompression(stream, compressionType, _options.MaxDecompressedSize);
            _reader = new StreamReader(decompressedStream, _options.Encoding, detectEncodingFromByteOrderMarks: true,
                bufferSize: _options.BufferSize);
            _ownsReader = true;

            InitializeBuffers();
            _columns = new List<CsvColumn>();
            _staticColumns = _options.StaticColumns != null ? new List<StaticColumn>(_options.StaticColumns) : new List<StaticColumn>();
            _parseErrors = _options.CollectParseErrors ? new List<CsvParseError>() : null;
            _headerNameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        private void InitializeBuffers()
        {
            // Use ArrayPool for the main read buffer
            _buffer = ArrayPool<char>.Shared.Rent(_options.BufferSize);
            _bufferFromPool = true;
            _lineBuilder = new StringBuilder(512);
            _fieldsBuffer = new List<FieldInfo>(64);
            _quotedFieldBuilder = new StringBuilder(256);
        }

        #endregion

        #region Initialization

        private void Initialize()
        {
            if (_isInitialized) return;
            _isInitialized = true;

            // Skip initial rows if specified
            for (int i = 0; i < _options.SkipRows; i++)
            {
                if (!ReadLine(out _))
                {
                    break;
                }
                _currentLineNumber++;
            }

            // Read header row if present
            if (_options.HasHeaderRow)
            {
                if (ReadLine(out string headerLine) && !string.IsNullOrEmpty(headerLine))
                {
                    _currentLineNumber++;

                    // Normalize smart quotes if enabled
                    if (_options.NormalizeQuotes)
                    {
                        headerLine = NormalizeSmartQuotes(headerLine);
                    }

                    ParseLine(headerLine);

                    // Process headers with duplicate handling
                    ProcessHeaders();
                }
            }

            // Prepare converted values array
            _convertedValues = new object[_columns.Count + _staticColumns.Count];
        }

        private void ProcessHeaders()
        {
            _headerNameCounts.Clear();
            var headerIndicesToSkip = new HashSet<int>();

            // First pass: count occurrences for UseLastOccurrence mode
            if (_options.DuplicateHeaderBehavior == DuplicateHeaderBehavior.UseLastOccurrence)
            {
                var lastOccurrence = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < _fieldsBuffer.Count; i++)
                {
                    string name = GetTrimmedHeaderName(_fieldsBuffer[i].Value);
                    lastOccurrence[name] = i;
                }

                // Mark non-last occurrences for renaming
                var tempCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < _fieldsBuffer.Count; i++)
                {
                    string name = GetTrimmedHeaderName(_fieldsBuffer[i].Value);
                    if (lastOccurrence[name] != i)
                    {
                        // This is not the last occurrence, will be renamed
                        if (!tempCounts.ContainsKey(name))
                            tempCounts[name] = 0;
                        tempCounts[name]++;
                    }
                }
            }

            // Second pass: create columns
            for (int i = 0; i < _fieldsBuffer.Count; i++)
            {
                string name = GetTrimmedHeaderName(_fieldsBuffer[i].Value);

                // Check include/exclude filters first
                if (!ShouldIncludeColumn(name))
                    continue;

                // Handle duplicate headers
                string finalName = HandleDuplicateHeader(name, i);
                if (finalName == null)
                {
                    // Skip this column (UseFirstOccurrence mode, not the first)
                    continue;
                }

                var column = new CsvColumn(finalName, _columns.Count, GetColumnType(name));
                column.SourceIndex = i;  // Track original index for field mapping
                _columns.Add(column);

                // Update cached max source index
                if (i > _maxSourceIndex)
                    _maxSourceIndex = i;
            }
        }

        private string GetTrimmedHeaderName(string name)
        {
            if (_options.TrimmingOptions != ValueTrimmingOptions.None && name != null)
            {
                return name.Trim();
            }
            return name ?? string.Empty;
        }

        private string HandleDuplicateHeader(string name, int fieldIndex)
        {
            if (!_headerNameCounts.TryGetValue(name, out int count))
            {
                // First occurrence
                _headerNameCounts[name] = 1;
                return name;
            }

            // Duplicate found
            switch (_options.DuplicateHeaderBehavior)
            {
                case DuplicateHeaderBehavior.ThrowException:
                    throw new CsvParseException($"Duplicate column header '{name}' found at index {fieldIndex}. " +
                        "Use DuplicateHeaderBehavior option to handle duplicates.");

                case DuplicateHeaderBehavior.Rename:
                    _headerNameCounts[name] = count + 1;
                    string newName = $"{name}_{count + 1}";
                    // Ensure the new name is also unique
                    while (_headerNameCounts.ContainsKey(newName))
                    {
                        count++;
                        _headerNameCounts[name] = count + 1;
                        newName = $"{name}_{count + 1}";
                    }
                    _headerNameCounts[newName] = 1;
                    return newName;

                case DuplicateHeaderBehavior.UseFirstOccurrence:
                    // Skip this duplicate
                    return null;

                case DuplicateHeaderBehavior.UseLastOccurrence:
                    // Rename earlier occurrences, keep this one
                    _headerNameCounts[name] = count + 1;
                    return name;

                default:
                    return name;
            }
        }

        private bool ShouldIncludeColumn(string name)
        {
            if (_options.IncludeColumns != null && _options.IncludeColumns.Count > 0)
            {
                if (!_options.IncludeColumns.Contains(name))
                    return false;
            }

            if (_options.ExcludeColumns != null && _options.ExcludeColumns.Contains(name))
            {
                return false;
            }

            return true;
        }

        private Type GetColumnType(string columnName)
        {
            if (_options.ColumnTypes != null && _options.ColumnTypes.TryGetValue(columnName, out Type type))
            {
                return type;
            }
            return typeof(string);
        }

        #endregion

        #region IDataReader Implementation

        /// <summary>
        /// Reads the next record from the CSV file.
        /// </summary>
        public bool Read()
        {
            ThrowIfClosed();
            Initialize();

            while (true)
            {
                if (!ReadLine(out string line))
                {
                    _currentRecord = null;
                    return false;
                }

                _currentLineNumber++;

                // Skip empty lines if configured
                if (string.IsNullOrEmpty(line) && _options.SkipEmptyLines)
                {
                    continue;
                }

                // Skip comment lines
                if (line != null && line.Length > 0 && line[0] == _options.Comment)
                {
                    continue;
                }

                // Normalize smart quotes if enabled
                if (_options.NormalizeQuotes && line != null)
                {
                    line = NormalizeSmartQuotes(line);
                }

                try
                {
                    ParseLine(line);
                    _currentRecordIndex++;

                    // Store fields
                    if (!_options.HasHeaderRow && _columns.Count == 0)
                    {
                        // First data row defines columns when no header
                        for (int i = 0; i < _fieldsBuffer.Count; i++)
                        {
                            var col = new CsvColumn($"Column{i}", _columns.Count, typeof(string));
                            col.SourceIndex = i;
                            _columns.Add(col);
                        }
                        // Cache max source index for no-header case
                        _maxSourceIndex = _fieldsBuffer.Count - 1;
                        _convertedValues = new object[_columns.Count + _staticColumns.Count];
                    }

                    // Handle field count mismatch
                    // Use cached max source index to avoid LINQ overhead per row
                    int expectedCount = _maxSourceIndex >= 0 ? _maxSourceIndex + 1 : _fieldsBuffer.Count;
                    if (_fieldsBuffer.Count != expectedCount)
                    {
                        HandleFieldCountMismatch(line, expectedCount);
                    }

                    // Copy fields to record buffer
                    EnsureRecordBufferCapacity(_fieldsBuffer.Count);
                    for (int i = 0; i < _fieldsBuffer.Count; i++)
                    {
                        _recordBuffer[i] = _fieldsBuffer[i].Value;
                        _quotedBuffer[i] = _fieldsBuffer[i].WasQuoted;
                    }
                    _currentRecord = _recordBuffer;
                    _currentRecordWasQuoted = _quotedBuffer;

                    // Convert values to typed objects
                    ConvertCurrentRecord();

                    return true;
                }
                catch (Exception ex) when (!(ex is CsvParseException parseEx && parseEx.IsMaxErrorsExceeded))
                {
                    var error = new CsvParseError(
                        _currentRecordIndex + 1,
                        -1,
                        line,
                        ex.Message,
                        ex,
                        _currentLineNumber,
                        0);

                    if (_parseErrors != null)
                    {
                        _parseErrors.Add(error);

                        if (_options.MaxParseErrors > 0 && _parseErrors.Count >= _options.MaxParseErrors)
                        {
                            throw new CsvParseException($"Maximum parse errors ({_options.MaxParseErrors}) exceeded", error) { IsMaxErrorsExceeded = true };
                        }
                    }

                    switch (_options.ParseErrorAction)
                    {
                        case CsvParseErrorAction.ThrowException:
                            throw new CsvParseException("CSV parse error", error);

                        case CsvParseErrorAction.AdvanceToNextLine:
                            continue;

                        case CsvParseErrorAction.RaiseEvent:
                            var args = new CsvParseErrorEventArgs(error, CsvParseErrorAction.AdvanceToNextLine);
                            ParseError?.Invoke(this, args);
                            if (args.Action == CsvParseErrorAction.ThrowException)
                            {
                                throw new CsvParseException("CSV parse error", error);
                            }
                            continue;
                    }
                }
            }
        }

        private void HandleFieldCountMismatch(string line, int expectedCount)
        {
            int actualCount = _fieldsBuffer.Count;

            switch (_options.MismatchedFieldAction)
            {
                case MismatchedFieldAction.ThrowException:
                    throw new FormatException(
                        $"Row has {actualCount} field(s) but expected {expectedCount} based on header. " +
                        $"Row content: '{line}'");

                case MismatchedFieldAction.PadWithNulls:
                    // Pad missing fields with empty values (will become null)
                    while (_fieldsBuffer.Count < expectedCount)
                    {
                        _fieldsBuffer.Add(new FieldInfo(string.Empty, false));
                    }
                    break;

                case MismatchedFieldAction.TruncateExtra:
                    // Remove extra fields
                    while (_fieldsBuffer.Count > expectedCount)
                    {
                        _fieldsBuffer.RemoveAt(_fieldsBuffer.Count - 1);
                    }
                    break;

                case MismatchedFieldAction.PadOrTruncate:
                    // Both pad and truncate
                    while (_fieldsBuffer.Count < expectedCount)
                    {
                        _fieldsBuffer.Add(new FieldInfo(string.Empty, false));
                    }
                    while (_fieldsBuffer.Count > expectedCount)
                    {
                        _fieldsBuffer.RemoveAt(_fieldsBuffer.Count - 1);
                    }
                    break;
            }
        }

        private void ConvertCurrentRecord()
        {
            for (int i = 0; i < _columns.Count; i++)
            {
                var column = _columns[i];
                int sourceIndex = column.SourceIndex;

                string rawValue = sourceIndex < _currentRecord.Length ? _currentRecord[sourceIndex] : null;
                bool wasQuoted = sourceIndex < _currentRecordWasQuoted.Length && _currentRecordWasQuoted[sourceIndex];

                // Apply trimming
                rawValue = ApplyTrimming(rawValue, wasQuoted);

                // Check for explicit null value
                if (rawValue != null && _options.NullValue != null && rawValue == _options.NullValue)
                {
                    rawValue = null;
                    wasQuoted = false;  // Treat as unquoted null
                }

                // Handle null/empty values with distinction
                if (string.IsNullOrEmpty(rawValue))
                {
                    if (_options.DistinguishEmptyFromNull)
                    {
                        // If it was quoted (""), it's an explicit empty string
                        // If it was unquoted (,,), it's null
                        if (wasQuoted)
                        {
                            // Explicit empty string
                            if (column.DataType == typeof(string))
                            {
                                _convertedValues[i] = string.Empty;
                            }
                            else if (column.UseDefaultForNull || _options.UseColumnDefaults)
                            {
                                _convertedValues[i] = column.DefaultValue;
                            }
                            else
                            {
                                _convertedValues[i] = DBNull.Value;
                            }
                        }
                        else
                        {
                            // True null
                            if (column.UseDefaultForNull || _options.UseColumnDefaults)
                            {
                                _convertedValues[i] = column.DefaultValue;
                            }
                            else
                            {
                                _convertedValues[i] = DBNull.Value;
                            }
                        }
                    }
                    else
                    {
                        // Original behavior: treat both as DBNull
                        if (column.UseDefaultForNull || _options.UseColumnDefaults)
                        {
                            _convertedValues[i] = column.DefaultValue;
                        }
                        else
                        {
                            _convertedValues[i] = DBNull.Value;
                        }
                    }
                    continue;
                }

                // Convert to target type
                _convertedValues[i] = ConvertValue(rawValue, column);
            }

            // Add static column values
            for (int i = 0; i < _staticColumns.Count; i++)
            {
                _convertedValues[_columns.Count + i] = _staticColumns[i].GetValue(_currentRecordIndex);
            }
        }

        private object ConvertValue(string value, CsvColumn column)
        {
            if (column.DataType == typeof(string))
            {
                return value;
            }

            // Use custom converter if specified
            ITypeConverter converter = column.Converter ?? _options.TypeConverterRegistry?.GetConverter(column.DataType);

            if (converter != null)
            {
                // Pass culture to converter if it supports it
                if (converter is ICultureAwareConverter cultureAware)
                {
                    if (cultureAware.TryConvert(value, _options.Culture, out object result))
                    {
                        return result;
                    }
                }
                else if (converter.TryConvert(value, out object result))
                {
                    return result;
                }
                throw new FormatException($"Cannot convert value '{value}' to type {column.DataType.Name} for column '{column.Name}'");
            }

            // Fall back to Convert.ChangeType with culture
            try
            {
                return Convert.ChangeType(value, column.DataType, _options.Culture);
            }
            catch (Exception ex)
            {
                throw new FormatException($"Cannot convert value '{value}' to type {column.DataType.Name} for column '{column.Name}'", ex);
            }
        }

        private string ApplyTrimming(string value, bool isQuoted)
        {
            if (value == null || _options.TrimmingOptions == ValueTrimmingOptions.None)
                return value;

            bool shouldTrim = false;

            if ((_options.TrimmingOptions & ValueTrimmingOptions.UnquotedOnly) != 0 && !isQuoted)
                shouldTrim = true;

            if ((_options.TrimmingOptions & ValueTrimmingOptions.QuotedOnly) != 0 && isQuoted)
                shouldTrim = true;

            return shouldTrim ? value.Trim() : value;
        }

        /// <summary>
        /// Gets the number of columns in the current record.
        /// </summary>
        public int FieldCount
        {
            get
            {
                Initialize();
                return _columns.Count + _staticColumns.Count;
            }
        }

        /// <summary>
        /// Gets the value at the specified column index.
        /// </summary>
        public object this[int ordinal]
        {
            get { return GetValue(ordinal); }
        }

        /// <summary>
        /// Gets the value at the specified column name.
        /// </summary>
        public object this[string name]
        {
            get
            {
                int ordinal = GetOrdinal(name);
                return GetValue(ordinal);
            }
        }

        /// <summary>
        /// Gets the value at the specified column index.
        /// </summary>
        public object GetValue(int ordinal)
        {
            ThrowIfClosed();
            ValidateOrdinal(ordinal);
            return _convertedValues[ordinal];
        }

        /// <summary>
        /// Gets all values in the current record.
        /// </summary>
        public int GetValues(object[] values)
        {
            ThrowIfClosed();
            if (values == null)
                throw new ArgumentNullException(nameof(values));

            int count = Math.Min(values.Length, _convertedValues.Length);
            Array.Copy(_convertedValues, values, count);
            return count;
        }

        /// <summary>
        /// Gets the column name at the specified index.
        /// </summary>
        public string GetName(int ordinal)
        {
            Initialize();
            ValidateOrdinal(ordinal);

            if (ordinal < _columns.Count)
                return _columns[ordinal].Name;
            else
                return _staticColumns[ordinal - _columns.Count].Name;
        }

        /// <summary>
        /// Gets the column index for the specified name.
        /// </summary>
        public int GetOrdinal(string name)
        {
            Initialize();
            if (name == null)
                throw new ArgumentNullException(nameof(name));

            for (int i = 0; i < _columns.Count; i++)
            {
                if (string.Equals(_columns[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            for (int i = 0; i < _staticColumns.Count; i++)
            {
                if (string.Equals(_staticColumns[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return _columns.Count + i;
            }

            throw new ArgumentException($"Column '{name}' not found", nameof(name));
        }

        /// <summary>
        /// Gets the data type of the specified column.
        /// </summary>
        public Type GetFieldType(int ordinal)
        {
            Initialize();
            ValidateOrdinal(ordinal);

            if (ordinal < _columns.Count)
                return _columns[ordinal].DataType;
            else
                return _staticColumns[ordinal - _columns.Count].DataType;
        }

        /// <summary>
        /// Gets the data type name of the specified column.
        /// </summary>
        public string GetDataTypeName(int ordinal)
        {
            return GetFieldType(ordinal).Name;
        }

        /// <summary>
        /// Determines whether the specified column contains a null value.
        /// </summary>
        public bool IsDBNull(int ordinal)
        {
            ThrowIfClosed();
            ValidateOrdinal(ordinal);
            return _convertedValues[ordinal] == null || _convertedValues[ordinal] == DBNull.Value;
        }

        #endregion

        #region Typed Accessors

        public bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);
        public byte GetByte(int ordinal) => (byte)GetValue(ordinal);
        public char GetChar(int ordinal) => (char)GetValue(ordinal);
        public DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);
        public decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);
        public double GetDouble(int ordinal) => (double)GetValue(ordinal);
        public float GetFloat(int ordinal) => (float)GetValue(ordinal);
        public Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);
        public short GetInt16(int ordinal) => (short)GetValue(ordinal);
        public int GetInt32(int ordinal) => (int)GetValue(ordinal);
        public long GetInt64(int ordinal) => (long)GetValue(ordinal);
        public string GetString(int ordinal) => GetValue(ordinal)?.ToString();

        public long GetBytes(int ordinal, long fieldOffset, byte[] buffer, int bufferOffset, int length)
        {
            throw new NotSupportedException("GetBytes is not supported for CSV data");
        }

        public long GetChars(int ordinal, long fieldOffset, char[] buffer, int bufferOffset, int length)
        {
            string value = GetString(ordinal);
            if (value == null)
                return 0;

            int copyLength = Math.Min(length, value.Length - (int)fieldOffset);
            value.CopyTo((int)fieldOffset, buffer, bufferOffset, copyLength);
            return copyLength;
        }

        public IDataReader GetData(int ordinal)
        {
            throw new NotSupportedException("Nested data readers are not supported for CSV data");
        }

        #endregion

        #region Schema

        /// <summary>
        /// Gets the schema table describing the CSV columns.
        /// </summary>
        public DataTable GetSchemaTable()
        {
            Initialize();

            var schema = new DataTable("SchemaTable");
            schema.Columns.Add("ColumnName", typeof(string));
            schema.Columns.Add("ColumnOrdinal", typeof(int));
            schema.Columns.Add("ColumnSize", typeof(int));
            schema.Columns.Add("DataType", typeof(Type));
            schema.Columns.Add("AllowDBNull", typeof(bool));
            schema.Columns.Add("IsKey", typeof(bool));
            schema.Columns.Add("IsUnique", typeof(bool));
            schema.Columns.Add("IsAutoIncrement", typeof(bool));

            for (int i = 0; i < _columns.Count; i++)
            {
                var col = _columns[i];
                var row = schema.NewRow();
                row["ColumnName"] = col.Name;
                row["ColumnOrdinal"] = i;
                row["ColumnSize"] = -1;
                row["DataType"] = col.DataType;
                row["AllowDBNull"] = col.AllowNull;
                row["IsKey"] = false;
                row["IsUnique"] = false;
                row["IsAutoIncrement"] = false;
                schema.Rows.Add(row);
            }

            for (int i = 0; i < _staticColumns.Count; i++)
            {
                var col = _staticColumns[i];
                var row = schema.NewRow();
                row["ColumnName"] = col.Name;
                row["ColumnOrdinal"] = _columns.Count + i;
                row["ColumnSize"] = -1;
                row["DataType"] = col.DataType;
                row["AllowDBNull"] = true;
                row["IsKey"] = false;
                row["IsUnique"] = false;
                row["IsAutoIncrement"] = false;
                schema.Rows.Add(row);
            }

            return schema;
        }

        #endregion

        #region Line Reading

        private bool ReadLine(out string line)
        {
            if (_endOfStream)
            {
                line = null;
                return false;
            }

            _lineBuilder.Clear();
            bool inQuotes = false;
            int quotedFieldLength = 0;

            while (true)
            {
                if (_bufferPosition >= _bufferLength)
                {
                    _bufferLength = _reader.Read(_buffer, 0, _buffer.Length);
                    _bufferPosition = 0;

                    if (_bufferLength == 0)
                    {
                        _endOfStream = true;
                        if (_lineBuilder.Length > 0)
                        {
                            line = _lineBuilder.ToString();
                            return true;
                        }
                        line = null;
                        return false;
                    }
                }

                char c = _buffer[_bufferPosition++];

                if (c == _options.Quote)
                {
                    if (inQuotes)
                    {
                        inQuotes = false;
                        quotedFieldLength = 0;
                    }
                    else
                    {
                        inQuotes = true;
                        quotedFieldLength = 0;
                    }
                    _lineBuilder.Append(c);
                }
                else if (c == '\r')
                {
                    if (!inQuotes || !_options.AllowMultilineFields)
                    {
                        // Check for \r\n
                        if (_bufferPosition < _bufferLength && _buffer[_bufferPosition] == '\n')
                        {
                            _bufferPosition++;
                        }
                        else if (_bufferPosition >= _bufferLength)
                        {
                            // Peek next buffer
                            _bufferLength = _reader.Read(_buffer, 0, _buffer.Length);
                            _bufferPosition = 0;
                            if (_bufferLength > 0 && _buffer[0] == '\n')
                            {
                                _bufferPosition++;
                            }
                        }
                        line = _lineBuilder.ToString();
                        return true;
                    }
                    else
                    {
                        _lineBuilder.Append(c);
                        quotedFieldLength++;
                        CheckQuotedFieldLength(quotedFieldLength);
                    }
                }
                else if (c == '\n')
                {
                    if (!inQuotes || !_options.AllowMultilineFields)
                    {
                        line = _lineBuilder.ToString();
                        return true;
                    }
                    else
                    {
                        _lineBuilder.Append(c);
                        quotedFieldLength++;
                        CheckQuotedFieldLength(quotedFieldLength);
                    }
                }
                else
                {
                    _lineBuilder.Append(c);
                    if (inQuotes)
                    {
                        quotedFieldLength++;
                        CheckQuotedFieldLength(quotedFieldLength);
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CheckQuotedFieldLength(int length)
        {
            if (_options.MaxQuotedFieldLength > 0 && length > _options.MaxQuotedFieldLength)
            {
                throw new CsvParseException(
                    $"Quoted field exceeded maximum length of {_options.MaxQuotedFieldLength:N0} characters at line {_currentLineNumber + 1}. " +
                    "This may indicate malformed data or a denial-of-service attack.");
            }
        }

        #endregion

        #region Line Parsing (Span-Based)

        private void ParseLine(string line)
        {
            _fieldsBuffer.Clear();

            if (string.IsNullOrEmpty(line))
                return;

            ReadOnlySpan<char> lineSpan = line.AsSpan();
            string delimiter = _options.Delimiter;
            char quote = _options.Quote;
            char escape = _options.Escape;
            bool lenient = _options.QuoteMode == QuoteMode.Lenient;

            int position = 0;

            while (position <= lineSpan.Length)
            {
                var (field, wasQuoted, newPosition) = ParseField(lineSpan, position, delimiter, quote, escape, lenient);
                _fieldsBuffer.Add(new FieldInfo(field, wasQuoted));
                position = newPosition;

                if (position > lineSpan.Length)
                    break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private (string value, bool wasQuoted, int newPosition) ParseField(
            ReadOnlySpan<char> line, int start, string delimiter, char quote, char escape, bool lenient)
        {
            if (start >= line.Length)
            {
                // Empty field at end
                return (string.Empty, false, start + delimiter.Length);
            }

            // Check for quoted field
            if (line[start] == quote)
            {
                if (lenient)
                {
                    // In lenient mode, verify there's a properly closing quote
                    if (!HasMatchingClosingQuote(line, start, quote, escape, delimiter, lenient))
                    {
                        // No matching close quote - treat as unquoted field
                        return ParseUnquotedField(line, start, delimiter);
                    }
                }
                return ParseQuotedField(line, start, delimiter, quote, escape, lenient);
            }

            return ParseUnquotedField(line, start, delimiter);
        }

        private bool HasMatchingClosingQuote(ReadOnlySpan<char> line, int start, char quote, char escape, string delimiter, bool lenient)
        {
            // Start after opening quote
            int i = start + 1;

            while (i < line.Length)
            {
                // Check for RFC 4180 escaped quote (doubled quote)
                if (line[i] == escape && i + 1 < line.Length && line[i + 1] == quote)
                {
                    // Escaped quote, skip both
                    i += 2;
                }
                // In lenient mode, also check for backslash escaped quote
                else if (lenient && line[i] == '\\' && i + 1 < line.Length && line[i + 1] == quote)
                {
                    // Backslash-escaped quote, skip both
                    i += 2;
                }
                else if (line[i] == quote)
                {
                    // Found a quote - check if it's a valid closing quote
                    // (must be at end of line or followed by delimiter or whitespace before delimiter)
                    int afterQuote = i + 1;
                    if (afterQuote >= line.Length)
                    {
                        // Quote at end of line - valid closing
                        return true;
                    }

                    // Check for delimiter immediately after quote
                    if (MatchesDelimiter(line, afterQuote, delimiter))
                    {
                        return true;
                    }

                    // Check for whitespace then delimiter or end
                    int checkPos = afterQuote;
                    while (checkPos < line.Length && char.IsWhiteSpace(line[checkPos]))
                        checkPos++;
                    if (checkPos >= line.Length || MatchesDelimiter(line, checkPos, delimiter))
                    {
                        return true;
                    }

                    // Quote is not at a valid position - keep looking
                    i++;
                }
                else
                {
                    i++;
                }
            }

            return false;
        }

        private (string value, bool wasQuoted, int newPosition) ParseQuotedField(
            ReadOnlySpan<char> line, int start, string delimiter, char quote, char escape, bool lenient)
        {
            // Reuse pooled StringBuilder to reduce allocations
            _quotedFieldBuilder.Clear();
            int i = start + 1; // Skip opening quote
            bool wasQuoted = true;

            while (i < line.Length)
            {
                char c = line[i];

                // Check for escaped quote (RFC 4180: "" or custom escape like \")
                if (c == escape && i + 1 < line.Length && line[i + 1] == quote)
                {
                    _quotedFieldBuilder.Append(quote);
                    i += 2;
                }
                // In lenient mode, also handle backslash escape
                else if (lenient && c == '\\' && i + 1 < line.Length && line[i + 1] == quote)
                {
                    _quotedFieldBuilder.Append(quote);
                    i += 2;
                }
                else if (c == quote)
                {
                    // End of quoted field
                    i++;

                    // Skip to delimiter or end
                    if (i < line.Length)
                    {
                        if (MatchesDelimiter(line, i, delimiter))
                        {
                            i += delimiter.Length;
                        }
                        else if (lenient)
                        {
                            // In lenient mode, there might be trailing spaces before delimiter
                            while (i < line.Length && char.IsWhiteSpace(line[i]))
                                i++;
                            if (MatchesDelimiter(line, i, delimiter))
                                i += delimiter.Length;
                        }
                    }
                    else
                    {
                        // At end of line with no trailing delimiter - add delimiter length to signal end
                        i += delimiter.Length;
                    }

                    return (_quotedFieldBuilder.ToString(), wasQuoted, i);
                }
                else
                {
                    _quotedFieldBuilder.Append(c);
                    i++;
                }
            }

            // Unclosed quote - return position past end to signal no more fields
            return (_quotedFieldBuilder.ToString(), wasQuoted, line.Length + delimiter.Length);
        }

        private (string value, bool wasQuoted, int newPosition) ParseUnquotedField(
            ReadOnlySpan<char> line, int start, string delimiter)
        {
            int delimiterLength = delimiter.Length;

            // Use Span for fast delimiter search when delimiter is single character
            if (delimiterLength == 1)
            {
                char delimChar = delimiter[0];
                int delimIndex = line.Slice(start).IndexOf(delimChar);

                if (delimIndex < 0)
                {
                    // No more delimiters - rest of line is the field
                    return (line.Slice(start).ToString(), false, line.Length + delimiterLength);
                }

                string value = line.Slice(start, delimIndex).ToString();
                return (value, false, start + delimIndex + delimiterLength);
            }

            // Multi-character delimiter - scan character by character
            for (int i = start; i < line.Length; i++)
            {
                if (MatchesDelimiter(line, i, delimiter))
                {
                    string value = line.Slice(start, i - start).ToString();
                    return (value, false, i + delimiterLength);
                }
            }

            // No delimiter found - rest of line is the field
            return (line.Slice(start).ToString(), false, line.Length + delimiterLength);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool MatchesDelimiter(ReadOnlySpan<char> line, int position, string delimiter)
        {
            if (position + delimiter.Length > line.Length)
                return false;

            for (int i = 0; i < delimiter.Length; i++)
            {
                if (line[position + i] != delimiter[i])
                    return false;
            }
            return true;
        }

        #endregion

        #region Smart Quote Normalization

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string NormalizeSmartQuotes(string input)
        {
            if (input == null)
                return null;

            // Fast path: check if any smart quotes exist
            bool hasSmartQuotes = false;
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (c == LeftSingleQuote || c == RightSingleQuote ||
                    c == LeftDoubleQuote || c == RightDoubleQuote)
                {
                    hasSmartQuotes = true;
                    break;
                }
            }

            if (!hasSmartQuotes)
                return input;

            // Slow path: replace smart quotes
            var sb = new StringBuilder(input.Length);
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                switch (c)
                {
                    case LeftSingleQuote:
                    case RightSingleQuote:
                        sb.Append('\'');
                        break;
                    case LeftDoubleQuote:
                    case RightDoubleQuote:
                        sb.Append('"');
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        #endregion

        #region Additional Properties

        /// <summary>
        /// Gets the current record index (zero-based).
        /// </summary>
        public long CurrentRecordIndex => _currentRecordIndex;

        /// <summary>
        /// Gets the current line number in the file (one-based).
        /// </summary>
        public long CurrentLineNumber => _currentLineNumber;

        /// <summary>
        /// Gets the collection of parse errors encountered during reading.
        /// Only populated when CollectParseErrors is true.
        /// </summary>
        public IReadOnlyList<CsvParseError> ParseErrors => _parseErrors;

        /// <summary>
        /// Gets the column definitions.
        /// </summary>
        public IReadOnlyList<CsvColumn> Columns
        {
            get
            {
                Initialize();
                return _columns;
            }
        }

        /// <summary>
        /// Gets the static column definitions.
        /// </summary>
        public IReadOnlyList<StaticColumn> StaticColumnsList => _staticColumns;

        /// <summary>
        /// Gets the field headers.
        /// </summary>
        public string[] GetFieldHeaders()
        {
            Initialize();
            var headers = new string[_columns.Count + _staticColumns.Count];
            for (int i = 0; i < _columns.Count; i++)
            {
                headers[i] = _columns[i].Name;
            }
            for (int i = 0; i < _staticColumns.Count; i++)
            {
                headers[_columns.Count + i] = _staticColumns[i].Name;
            }
            return headers;
        }

        /// <summary>
        /// Gets whether the column with the specified name exists.
        /// </summary>
        public bool HasColumn(string name)
        {
            Initialize();
            if (name == null)
                return false;

            for (int i = 0; i < _columns.Count; i++)
            {
                if (string.Equals(_columns[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            for (int i = 0; i < _staticColumns.Count; i++)
            {
                if (string.Equals(_staticColumns[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Sets the type for a column. Must be called before reading.
        /// </summary>
        public void SetColumnType(string columnName, Type type)
        {
            Initialize();
            for (int i = 0; i < _columns.Count; i++)
            {
                if (string.Equals(_columns[i].Name, columnName, StringComparison.OrdinalIgnoreCase))
                {
                    _columns[i].DataType = type;
                    return;
                }
            }
            throw new ArgumentException($"Column '{columnName}' not found", nameof(columnName));
        }

        /// <summary>
        /// Adds a static column to inject values into each record.
        /// </summary>
        public void AddStaticColumn(StaticColumn column)
        {
            if (column == null)
                throw new ArgumentNullException(nameof(column));

            _staticColumns.Add(column);

            if (_convertedValues != null)
            {
                // Resize converted values array
                Array.Resize(ref _convertedValues, _columns.Count + _staticColumns.Count);
            }
        }

        #endregion

        #region Events

        /// <summary>
        /// Occurs when a parse error is encountered and ParseErrorAction is RaiseEvent.
        /// </summary>
        public event EventHandler<CsvParseErrorEventArgs> ParseError;

        #endregion

        #region IDataReader Members

        public int Depth => 0;
        public bool IsClosed => _isClosed;
        public int RecordsAffected => -1;

        public bool NextResult() => false;

        public void Close()
        {
            if (!_isClosed)
            {
                _isClosed = true;
                if (_ownsReader)
                {
                    _reader.Dispose();
                }

                // Return pooled buffer
                if (_bufferFromPool && _buffer != null)
                {
                    ArrayPool<char>.Shared.Return(_buffer);
                    _buffer = null;
                }
            }
        }

        public void Dispose()
        {
            Close();
        }

        #endregion

        #region Helpers

        private void ThrowIfClosed()
        {
            if (_isClosed)
                throw new ObjectDisposedException(GetType().Name);
        }

        private void ValidateOrdinal(int ordinal)
        {
            if (ordinal < 0 || ordinal >= _columns.Count + _staticColumns.Count)
                throw new ArgumentOutOfRangeException(nameof(ordinal));
        }

        private void EnsureRecordBufferCapacity(int requiredCapacity)
        {
            if (_recordBuffer == null || _recordBuffer.Length < requiredCapacity)
            {
                int newCapacity = Math.Max(requiredCapacity, 64);
                if (_recordBuffer != null)
                {
                    newCapacity = Math.Max(newCapacity, _recordBuffer.Length * 2);
                }
                _recordBuffer = new string[newCapacity];
                _quotedBuffer = new bool[newCapacity];
            }
        }

        #endregion
    }
}
