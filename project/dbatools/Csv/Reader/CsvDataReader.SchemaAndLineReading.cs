using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dataplat.Dbatools.Csv.Compression;
using Dataplat.Dbatools.Csv.TypeConverters;

namespace Dataplat.Dbatools.Csv.Reader
{
    public sealed partial class CsvDataReader
    {

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
                    String.Format("Quoted field exceeded maximum length of {0:N0} characters at line {1}. ", _options.MaxQuotedFieldLength, _currentLineNumber + 1) +
                    "This may indicate malformed data or a denial-of-service attack.");
            }
        }

    }
}
