using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text;
using Dataplat.Dbatools.Csv.Compression;
using Dataplat.Dbatools.Csv.TypeConverters;

namespace Dataplat.Dbatools.Csv.Reader
{
    /// <summary>
    /// High-performance CSV reader that implements IDataReader for SqlBulkCopy compatibility.
    /// Supports multi-character delimiters, type conversion, compression, error tracking, and static columns.
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
        private object[] _convertedValues;
        private bool _isInitialized;
        private bool _isClosed;
        private long _currentRecordIndex = -1;
        private long _currentLineNumber;

        // Buffer for efficient reading
        private readonly char[] _buffer;
        private int _bufferLength;
        private int _bufferPosition;
        private bool _endOfStream;

        // Field parsing state
        private readonly StringBuilder _fieldBuilder;
        private readonly List<string> _fieldsBuffer;

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

            Stream stream = CompressionHelper.OpenFileForReading(filePath, _options.AutoDetectCompression);
            _reader = new StreamReader(stream, _options.Encoding, detectEncodingFromByteOrderMarks: true,
                bufferSize: _options.BufferSize);
            _ownsReader = true;

            _buffer = new char[_options.BufferSize];
            _fieldBuilder = new StringBuilder(256);
            _fieldsBuffer = new List<string>(64);
            _columns = new List<CsvColumn>();
            _staticColumns = _options.StaticColumns != null ? new List<StaticColumn>(_options.StaticColumns) : new List<StaticColumn>();
            _parseErrors = _options.CollectParseErrors ? new List<CsvParseError>() : null;
        }

        /// <summary>
        /// Creates a new CSV reader for the specified TextReader.
        /// </summary>
        public CsvDataReader(TextReader reader, CsvReaderOptions options = null)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _options = options ?? new CsvReaderOptions();
            _ownsReader = false;

            _buffer = new char[_options.BufferSize];
            _fieldBuilder = new StringBuilder(256);
            _fieldsBuffer = new List<string>(64);
            _columns = new List<CsvColumn>();
            _staticColumns = _options.StaticColumns != null ? new List<StaticColumn>(_options.StaticColumns) : new List<StaticColumn>();
            _parseErrors = _options.CollectParseErrors ? new List<CsvParseError>() : null;
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

            Stream decompressedStream = CompressionHelper.WrapForDecompression(stream, compressionType);
            _reader = new StreamReader(decompressedStream, _options.Encoding, detectEncodingFromByteOrderMarks: true,
                bufferSize: _options.BufferSize);
            _ownsReader = true;

            _buffer = new char[_options.BufferSize];
            _fieldBuilder = new StringBuilder(256);
            _fieldsBuffer = new List<string>(64);
            _columns = new List<CsvColumn>();
            _staticColumns = _options.StaticColumns != null ? new List<StaticColumn>(_options.StaticColumns) : new List<StaticColumn>();
            _parseErrors = _options.CollectParseErrors ? new List<CsvParseError>() : null;
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
                    ParseLine(headerLine);

                    for (int i = 0; i < _fieldsBuffer.Count; i++)
                    {
                        string name = _fieldsBuffer[i];
                        if (_options.TrimmingOptions != ValueTrimmingOptions.None)
                        {
                            name = name.Trim();
                        }

                        // Check include/exclude filters
                        if (ShouldIncludeColumn(name))
                        {
                            var column = new CsvColumn(name, _columns.Count, GetColumnType(name));
                            _columns.Add(column);
                        }
                    }
                }
            }

            // Add static columns
            foreach (var staticCol in _staticColumns)
            {
                // Static columns don't have a CSV ordinal, they're appended at the end
            }

            // Prepare converted values array
            _convertedValues = new object[_columns.Count + _staticColumns.Count];
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
                if (line.Length > 0 && line[0] == _options.Comment)
                {
                    continue;
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
                            _columns.Add(new CsvColumn($"Column{i}", _columns.Count, typeof(string)));
                        }
                        _convertedValues = new object[_columns.Count + _staticColumns.Count];
                    }

                    _currentRecord = _fieldsBuffer.ToArray();

                    // Convert values to typed objects
                    ConvertCurrentRecord();

                    return true;
                }
                catch (Exception ex)
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
                            throw new CsvParseException($"Maximum parse errors ({_options.MaxParseErrors}) exceeded", error);
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

        private void ConvertCurrentRecord()
        {
            int csvFieldCount = Math.Min(_currentRecord.Length, _columns.Count);

            for (int i = 0; i < _columns.Count; i++)
            {
                var column = _columns[i];
                string rawValue = i < _currentRecord.Length ? _currentRecord[i] : null;

                // Apply trimming
                rawValue = ApplyTrimming(rawValue, false);

                // Check for null value
                if (rawValue != null && _options.NullValue != null && rawValue == _options.NullValue)
                {
                    rawValue = null;
                }

                // Handle null/empty values
                if (string.IsNullOrEmpty(rawValue))
                {
                    if (column.UseDefaultForNull || _options.UseColumnDefaults)
                    {
                        _convertedValues[i] = column.DefaultValue;
                    }
                    else
                    {
                        _convertedValues[i] = DBNull.Value;
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
                if (converter.TryConvert(value, out object result))
                {
                    return result;
                }
                throw new FormatException($"Cannot convert value '{value}' to type {column.DataType.Name} for column '{column.Name}'");
            }

            // Fall back to Convert.ChangeType
            return Convert.ChangeType(value, column.DataType);
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

        #region Line Parsing

        private bool ReadLine(out string line)
        {
            if (_endOfStream)
            {
                line = null;
                return false;
            }

            _fieldBuilder.Clear();
            bool inQuotes = false;

            while (true)
            {
                if (_bufferPosition >= _bufferLength)
                {
                    _bufferLength = _reader.Read(_buffer, 0, _buffer.Length);
                    _bufferPosition = 0;

                    if (_bufferLength == 0)
                    {
                        _endOfStream = true;
                        if (_fieldBuilder.Length > 0)
                        {
                            line = _fieldBuilder.ToString();
                            return true;
                        }
                        line = null;
                        return false;
                    }
                }

                char c = _buffer[_bufferPosition++];

                if (c == _options.Quote)
                {
                    inQuotes = !inQuotes;
                    _fieldBuilder.Append(c);
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
                        line = _fieldBuilder.ToString();
                        return true;
                    }
                    else
                    {
                        _fieldBuilder.Append(c);
                    }
                }
                else if (c == '\n')
                {
                    if (!inQuotes || !_options.AllowMultilineFields)
                    {
                        line = _fieldBuilder.ToString();
                        return true;
                    }
                    else
                    {
                        _fieldBuilder.Append(c);
                    }
                }
                else
                {
                    _fieldBuilder.Append(c);
                }
            }
        }

        private void ParseLine(string line)
        {
            _fieldsBuffer.Clear();

            if (string.IsNullOrEmpty(line))
                return;

            string delimiter = _options.Delimiter;
            char quote = _options.Quote;
            char escape = _options.Escape;
            int delimLength = delimiter.Length;

            StringBuilder field = new StringBuilder();
            bool inQuotes = false;
            int i = 0;

            while (i < line.Length)
            {
                char c = line[i];

                if (inQuotes)
                {
                    if (c == escape && i + 1 < line.Length && line[i + 1] == quote)
                    {
                        // Escaped quote
                        field.Append(quote);
                        i += 2;
                    }
                    else if (c == quote)
                    {
                        // End of quoted field
                        inQuotes = false;
                        i++;
                    }
                    else
                    {
                        field.Append(c);
                        i++;
                    }
                }
                else
                {
                    if (c == quote)
                    {
                        // Start of quoted field
                        inQuotes = true;
                        i++;
                    }
                    else if (MatchesDelimiter(line, i, delimiter))
                    {
                        // End of field
                        _fieldsBuffer.Add(field.ToString());
                        field.Clear();
                        i += delimLength;
                    }
                    else
                    {
                        field.Append(c);
                        i++;
                    }
                }
            }

            // Add last field
            _fieldsBuffer.Add(field.ToString());
        }

        private bool MatchesDelimiter(string line, int position, string delimiter)
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
            try
            {
                GetOrdinal(name);
                return true;
            }
            catch
            {
                return false;
            }
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

        #endregion
    }
}
