using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Text;
using Dataplat.Dbatools.Csv.Compression;

namespace Dataplat.Dbatools.Csv.Writer
{
    /// <summary>
    /// High-performance CSV writer with compression support.
    /// Addresses issue #8646 for Export-DbaCsv with compression options.
    /// </summary>
    public sealed class CsvWriter : IDisposable
    {
        #region Fields

        private readonly TextWriter _writer;
        private readonly CsvWriterOptions _options;
        private readonly bool _ownsWriter;
        private bool _isDisposed;
        private bool _headerWritten;
        private string[] _columnNames;
        private long _rowsWritten;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new CSV writer for the specified file.
        /// </summary>
        public CsvWriter(string filePath) : this(filePath, null)
        {
        }

        /// <summary>
        /// Creates a new CSV writer for the specified file with options.
        /// </summary>
        public CsvWriter(string filePath, CsvWriterOptions options)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            _options = options ?? new CsvWriterOptions();

            Stream stream = CompressionHelper.OpenFileForWriting(
                filePath,
                _options.CompressionType,
                _options.CompressionLevel);

            _writer = new StreamWriter(stream, _options.Encoding, _options.BufferSize);
            _ownsWriter = true;
        }

        /// <summary>
        /// Creates a new CSV writer for the specified TextWriter.
        /// </summary>
        public CsvWriter(TextWriter writer, CsvWriterOptions options = null)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _options = options ?? new CsvWriterOptions();
            _ownsWriter = false;
        }

        /// <summary>
        /// Creates a new CSV writer for the specified Stream.
        /// </summary>
        public CsvWriter(Stream stream, CsvWriterOptions options = null)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            _options = options ?? new CsvWriterOptions();

            Stream compressedStream = CompressionHelper.WrapForCompression(
                stream,
                _options.CompressionType,
                _options.CompressionLevel);

            _writer = new StreamWriter(compressedStream, _options.Encoding, _options.BufferSize);
            _ownsWriter = true;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Writes the header row with the specified column names.
        /// </summary>
        public void WriteHeader(params string[] columnNames)
        {
            ThrowIfDisposed();

            if (_headerWritten)
                throw new InvalidOperationException("Header has already been written");

            if (columnNames == null || columnNames.Length == 0)
                throw new ArgumentException("At least one column name is required", nameof(columnNames));

            _columnNames = columnNames;

            if (_options.WriteHeader)
            {
                WriteRow(columnNames);
                _headerWritten = true;
            }
        }

        /// <summary>
        /// Writes a data row with the specified values.
        /// </summary>
        public void WriteRow(params object[] values)
        {
            ThrowIfDisposed();

            if (values == null)
                throw new ArgumentNullException(nameof(values));

            StringBuilder sb = new StringBuilder();
            string delimiter = _options.Delimiter;

            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0)
                    sb.Append(delimiter);

                string formatted = FormatValue(values[i]);
                sb.Append(QuoteIfNeeded(formatted, values[i]));
            }

            _writer.Write(sb.ToString());
            _writer.Write(_options.NewLine);
            _rowsWritten++;

            if (_options.FlushAfterEachRow)
                _writer.Flush();
        }

        /// <summary>
        /// Writes a data row from a dictionary (column name to value mapping).
        /// </summary>
        public void WriteRow(IDictionary<string, object> row)
        {
            ThrowIfDisposed();

            if (row == null)
                throw new ArgumentNullException(nameof(row));

            if (_columnNames == null)
            {
                // Use dictionary keys as column names
                _columnNames = new string[row.Count];
                row.Keys.CopyTo(_columnNames, 0);

                if (_options.WriteHeader && !_headerWritten)
                {
                    WriteRow(_columnNames);
                    _headerWritten = true;
                }
            }

            object[] values = new object[_columnNames.Length];
            for (int i = 0; i < _columnNames.Length; i++)
            {
                row.TryGetValue(_columnNames[i], out values[i]);
            }

            WriteRow(values);
        }

        /// <summary>
        /// Writes all records from an IDataReader.
        /// </summary>
        public long WriteFromReader(IDataReader reader)
        {
            ThrowIfDisposed();

            if (reader == null)
                throw new ArgumentNullException(nameof(reader));

            // Write header from reader schema
            if (!_headerWritten && _options.WriteHeader)
            {
                string[] headers = new string[reader.FieldCount];
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    headers[i] = reader.GetName(i);
                }
                WriteHeader(headers);
            }

            // Write data rows
            object[] values = new object[reader.FieldCount];
            long count = 0;

            while (reader.Read())
            {
                reader.GetValues(values);
                WriteRow(values);
                count++;
            }

            return count;
        }

        /// <summary>
        /// Writes all records from a DataTable.
        /// </summary>
        public long WriteFromDataTable(DataTable table)
        {
            ThrowIfDisposed();

            if (table == null)
                throw new ArgumentNullException(nameof(table));

            // Write header
            if (!_headerWritten && _options.WriteHeader)
            {
                string[] headers = new string[table.Columns.Count];
                for (int i = 0; i < table.Columns.Count; i++)
                {
                    headers[i] = table.Columns[i].ColumnName;
                }
                WriteHeader(headers);
            }

            // Write data rows
            object[] values = new object[table.Columns.Count];

            foreach (DataRow row in table.Rows)
            {
                for (int i = 0; i < table.Columns.Count; i++)
                {
                    values[i] = row[i];
                }
                WriteRow(values);
            }

            return table.Rows.Count;
        }

        /// <summary>
        /// Writes all items from an enumerable collection.
        /// </summary>
        public long WriteFromEnumerable<T>(IEnumerable<T> items, Func<T, object[]> selector)
        {
            ThrowIfDisposed();

            if (items == null)
                throw new ArgumentNullException(nameof(items));
            if (selector == null)
                throw new ArgumentNullException(nameof(selector));

            long count = 0;

            foreach (var item in items)
            {
                WriteRow(selector(item));
                count++;
            }

            return count;
        }

        /// <summary>
        /// Flushes the writer.
        /// </summary>
        public void Flush()
        {
            ThrowIfDisposed();
            _writer.Flush();
        }

        /// <summary>
        /// Gets the number of data rows written (excludes header).
        /// </summary>
        public long RowsWritten => _headerWritten ? _rowsWritten - 1 : _rowsWritten;

        #endregion

        #region Private Methods

        private string FormatValue(object value)
        {
            if (value == null || value == DBNull.Value)
                return _options.NullValue;

            if (value is DateTime dt)
            {
                if (_options.UseUtc)
                    dt = dt.ToUniversalTime();
                return dt.ToString(_options.DateTimeFormat, CultureInfo.InvariantCulture);
            }

            if (value is DateTimeOffset dto)
            {
                if (_options.UseUtc)
                    dto = dto.ToUniversalTime();
                return dto.ToString(_options.DateTimeFormat, CultureInfo.InvariantCulture);
            }

            if (value is bool b)
                return b ? "true" : "false";

            if (value is byte[] bytes)
                return Convert.ToBase64String(bytes);

            if (value is IFormattable formattable)
                return formattable.ToString(null, CultureInfo.InvariantCulture);

            return value.ToString();
        }

        private string QuoteIfNeeded(string value, object originalValue)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            bool needsQuoting;

            switch (_options.QuotingBehavior)
            {
                case CsvQuotingBehavior.Always:
                    needsQuoting = true;
                    break;

                case CsvQuotingBehavior.Never:
                    needsQuoting = false;
                    break;

                case CsvQuotingBehavior.NonNumeric:
                    needsQuoting = !IsNumericType(originalValue);
                    break;

                case CsvQuotingBehavior.AsNeeded:
                default:
                    needsQuoting = NeedsQuoting(value);
                    break;
            }

            if (!needsQuoting)
                return value;

            // Escape quotes by doubling them (RFC 4180)
            string escaped = value.Replace(_options.Quote.ToString(), new string(_options.Quote, 2));
            return $"{_options.Quote}{escaped}{_options.Quote}";
        }

        private bool NeedsQuoting(string value)
        {
            // Check if value contains delimiter, quote, or newline
            if (value.IndexOf(_options.Delimiter, StringComparison.Ordinal) >= 0)
                return true;

            if (value.IndexOf(_options.Quote) >= 0)
                return true;

            if (value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0)
                return true;

            return false;
        }

        private bool IsNumericType(object value)
        {
            return value is byte || value is sbyte ||
                   value is short || value is ushort ||
                   value is int || value is uint ||
                   value is long || value is ulong ||
                   value is float || value is double ||
                   value is decimal;
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(GetType().Name);
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// Disposes the writer and releases resources.
        /// </summary>
        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;

                if (_ownsWriter)
                {
                    _writer.Flush();
                    _writer.Dispose();
                }
            }
        }

        #endregion
    }
}
