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
    /// <summary>
    /// High-performance CSV reader that implements IDataReader for SqlBulkCopy compatibility.
    /// Supports multi-character delimiters, type conversion, compression, error tracking, and static columns.
    /// Uses Span-based parsing and ArrayPool for maximum performance.
    /// </summary>
    /// <remarks>
    /// <para><b>Thread-Safety Guarantees:</b></para>
    /// <para>
    /// When parallel processing is enabled (<see cref="CsvReaderOptions.EnableParallelProcessing"/>),
    /// this class provides the following thread-safety guarantees:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <see cref="GetValue"/> and <see cref="GetValues"/> are thread-safe and can be called
    /// from multiple threads concurrently while <see cref="Read"/> is executing on another thread.
    /// These methods return consistent snapshots of the current record's data.
    /// </description></item>
    /// <item><description>
    /// <see cref="CurrentRecordIndex"/> is safe to read from any thread without torn reads.
    /// </description></item>
    /// <item><description>
    /// <see cref="Close"/> and <see cref="Dispose"/> can be called from any thread and will
    /// safely stop the parallel processing pipeline.
    /// </description></item>
    /// </list>
    /// <para>
    /// <b>Important:</b> While the above methods are thread-safe for concurrent reads,
    /// the values returned represent a snapshot that may change after the next <see cref="Read"/> call.
    /// Only one thread should call <see cref="Read"/> at a time.
    /// </para>
    /// <para>
    /// In sequential mode (parallel processing disabled), the class is not thread-safe.
    /// All access should be from a single thread.
    /// </para>
    /// </remarks>
    public sealed class CsvDataReader : IDataReader
    {
        #region Fields

        private readonly TextReader _reader;
        private readonly CsvReaderOptions _options;
        private readonly bool _ownsReader;
        private readonly List<CsvColumn> _columns;
        private readonly List<StaticColumn> _staticColumns;
        private readonly List<CsvParseError> _parseErrors;

        // Thread-safety note: These fields are accessed from the main thread during Read() and from
        // user threads via GetValue()/GetValues(). We use volatile for reference visibility and
        // lock (_resultLock) for atomic operations involving _convertedValues.
        private volatile string[] _currentRecord;
        private volatile bool[] _currentRecordWasQuoted;  // Track which fields were quoted for null vs empty
        private string[] _recordBuffer;
        private bool[] _quotedBuffer;
        private volatile object[] _convertedValues;
        private bool _isInitialized;
        private volatile bool _isClosed;
        private long _currentRecordIndex = -1;
        private long _currentLineNumber;

        // Buffer for first data row when no header row (needed to create columns during Initialize)
        private string _bufferedFirstLine;
        private bool _hasBufferedFirstLine;

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

        // Cached max source index to avoid LINQ per-row (volatile for thread visibility in parallel mode)
        private volatile int _maxSourceIndex = -1;

        // Reusable StringBuilder for quoted field parsing to reduce allocations
        private StringBuilder _quotedFieldBuilder;

        // String interning for common values to reduce allocations
        private HashSet<string> _internedStrings;

        // Smart quote characters for normalization
        private const char LeftSingleQuote = '\u2018';  // '
        private const char RightSingleQuote = '\u2019'; // '
        private const char LeftDoubleQuote = '\u201C';  // "
        private const char RightDoubleQuote = '\u201D'; // "

        // Direct field parsing state (eliminates intermediate line string allocation)
        private char _delimiterFirstChar;              // First char of delimiter for fast check
        private bool _singleCharDelimiter;             // True if delimiter is single char (common case)
        private bool _endOfRecord;                     // True when we've hit newline
        private StringBuilder _fieldAccumulator;       // For fields spanning buffer boundaries

#if NET8_0_OR_GREATER
        // SIMD-accelerated search for delimiter and newlines (NET 8+ only)
        private System.Buffers.SearchValues<char> _fieldTerminators;
#endif

        // Fast path optimization flags - determined during initialization
        private bool _useFastConversion;               // True when simple string-only conversion can be used
        private bool _useFastParsing;                  // True when ultra-fast inline parsing can be used

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

        #region Parallel Processing Structures

        /// <summary>
        /// Represents a line read from the input with its metadata.
        /// </summary>
        private struct LineData
        {
            public string Line;
            public long LineNumber;
            public long RecordIndex;

            public LineData(string line, long lineNumber, long recordIndex)
            {
                Line = line;
                LineNumber = lineNumber;
                RecordIndex = recordIndex;
            }
        }

        /// <summary>
        /// Represents a fully parsed and converted record ready for consumption.
        /// </summary>
        private sealed class ParsedRecord
        {
            public object[] Values;
            public long RecordIndex;
            public long LineNumber;
            public CsvParseError Error;

            public ParsedRecord(object[] values, long recordIndex, long lineNumber)
            {
                Values = values;
                RecordIndex = recordIndex;
                LineNumber = lineNumber;
                Error = null;
            }

            public ParsedRecord(CsvParseError error, long recordIndex, long lineNumber)
            {
                Values = null;
                RecordIndex = recordIndex;
                LineNumber = lineNumber;
                Error = error;
            }
        }

        #endregion

        #region Parallel Processing Fields

        // Parallel processing state (volatile for thread visibility across main/worker threads)
        private volatile bool _useParallelProcessing;
        private BlockingCollection<LineData> _lineQueue;
        private BlockingCollection<ParsedRecord> _resultQueue;
        private Thread _producerThread;
        private Thread[] _workerThreads;
        private CancellationTokenSource _cancellationSource;
        private volatile Exception _pipelineException;
        private int _activeWorkers;

        // Thread-safe error collection for parallel mode
        private ConcurrentQueue<CsvParseError> _parallelParseErrors;

        // Progress tracking
        private Stopwatch _progressStopwatch;
        private long _totalFileSize = -1;
        private long _lastProgressReport;
        private Stream _underlyingStream;

        // Result queue for ordered delivery
        private readonly object _resultLock = new object();
        private long _nextExpectedRecordIndex;
        private readonly SortedDictionary<long, ParsedRecord> _pendingResults = new SortedDictionary<long, ParsedRecord>();
        private ParsedRecord _currentParsedRecord;

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

            // Get file size for progress reporting before opening
            try
            {
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Exists)
                    _totalFileSize = fileInfo.Length;
            }
            catch { /* Ignore file info errors */ }

            Stream stream = CompressionHelper.OpenFileForReading(filePath, _options.AutoDetectCompression, _options.MaxDecompressedSize);
            _underlyingStream = stream;
            _reader = new StreamReader(stream, _options.Encoding, detectEncodingFromByteOrderMarks: true,
                bufferSize: _options.BufferSize);
            _ownsReader = true;

            InitializeBuffers();
            InitializeProgressTracking();
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
            InitializeProgressTracking();
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

            // Try to get stream length for progress reporting
            if (stream.CanSeek)
            {
                try { _totalFileSize = stream.Length; }
                catch { /* Ignore errors */ }
            }

            CompressionType compressionType = _options.AutoDetectCompression
                ? CompressionHelper.DetectFromStream(stream)
                : _options.CompressionType;

            Stream decompressedStream = CompressionHelper.WrapForDecompression(stream, compressionType, _options.MaxDecompressedSize);
            _underlyingStream = decompressedStream;
            _reader = new StreamReader(decompressedStream, _options.Encoding, detectEncodingFromByteOrderMarks: true,
                bufferSize: _options.BufferSize);
            _ownsReader = true;

            InitializeBuffers();
            InitializeProgressTracking();
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

            // Initialize direct field parsing state
            _delimiterFirstChar = _options.Delimiter[0];
            _singleCharDelimiter = _options.Delimiter.Length == 1;
            _fieldAccumulator = new StringBuilder(256);

#if NET8_0_OR_GREATER
            // Create SIMD-accelerated search values for field terminators
            // For single-char delimiter: search for delimiter, \r, \n
            if (_singleCharDelimiter)
            {
                _fieldTerminators = System.Buffers.SearchValues.Create(
                    new char[] { _delimiterFirstChar, '\r', '\n' });
            }
#endif

            // Initialize string interning if enabled
            if (_options.InternStrings)
            {
                InitializeStringInterning();
            }
        }

        private void InitializeStringInterning()
        {
            // Start with common values that frequently appear in CSV files
            _internedStrings = new HashSet<string>(StringComparer.Ordinal)
            {
                string.Empty,
                "NULL",
                "null",
                "Null",
                "N/A",
                "n/a",
                "NA",
                "na",
                "-",
                "0",
                "1",
                "true",
                "True",
                "TRUE",
                "false",
                "False",
                "FALSE",
                "Yes",
                "yes",
                "YES",
                "No",
                "no",
                "NO",
                "Y",
                "N",
                "y",
                "n"
            };

            // Add custom intern strings if specified
            if (_options.CustomInternStrings != null)
            {
                foreach (var s in _options.CustomInternStrings)
                {
                    _internedStrings.Add(s);
                }
            }

            // Intern the null value if configured
            if (_options.NullValue != null)
            {
                _internedStrings.Add(_options.NullValue);
            }
        }

        private void InitializeProgressTracking()
        {
            // Start stopwatch if progress reporting is enabled
            if (_options.ProgressCallback != null && _options.ProgressReportInterval > 0)
            {
                _progressStopwatch = Stopwatch.StartNew();
            }
        }

        /// <summary>
        /// Attempts to return an interned string for the given value.
        /// Fast path rejects strings that are too long for interning.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private string TryInternString(string value)
        {
            // Fast path: skip lookup if interning disabled or string too long
            // Most interned strings are short (null, true, false, empty, etc.)
            if (_internedStrings == null || value.Length > 10)
                return value;

            if (_internedStrings.TryGetValue(value, out string interned))
                return interned;

            return value;
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
            else
            {
                // No header row - peek at first data row to create columns
                // This allows SetColumnType to be called before Read()
                InitializeColumnsFromFirstDataRow();
            }

            // Cache converters for each column to avoid per-row registry lookups
            CacheColumnConverters();

            // Determine if we can use fast path optimizations
            InitializeFastPathOptimizations();

            // Prepare converted values array
            _convertedValues = new object[_columns.Count + _staticColumns.Count];

            // Start parallel processing pipeline if enabled
            if (_options.EnableParallelProcessing)
            {
                StartParallelPipeline();
            }
        }

        private void CacheColumnConverters()
        {
            foreach (var column in _columns)
            {
                // Skip string columns - they don't need conversion
                if (column.DataType == typeof(string))
                    continue;

                // Use custom converter if specified, otherwise look up from registry
                column.CachedConverter = column.Converter ?? _options.TypeConverterRegistry?.GetConverter(column.DataType);
            }
        }

        /// <summary>
        /// Initializes fast path optimization flags based on options and column configuration.
        /// </summary>
        private void InitializeFastPathOptimizations()
        {
            // Check if all columns are strings (no type conversion needed)
            bool hasNonStringColumns = false;
            for (int i = 0; i < _columns.Count; i++)
            {
                if (_columns[i].DataType != typeof(string) || _columns[i].CachedConverter != null)
                {
                    hasNonStringColumns = true;
                    break;
                }
            }

            // Static columns always need conversion (they compute values)
            if (_staticColumns.Count > 0)
            {
                hasNonStringColumns = true;
            }

            // Determine if we can use the fast conversion path:
            // - No trimming options
            // - No null value configured
            // - No DistinguishEmptyFromNull
            // - No UseColumnDefaults
            // - No static columns
            // - All columns are strings
            _useFastConversion = !hasNonStringColumns
                && _options.TrimmingOptions == ValueTrimmingOptions.None
                && _options.NullValue == null
                && !_options.DistinguishEmptyFromNull
                && !_options.UseColumnDefaults
                && _staticColumns.Count == 0;

            // Determine if we can use the ultra-fast inline parsing path:
            // - Single-character delimiter
            // - No quote normalization
            // - No comment character
            // - No parallel processing
            // All of the above plus fast conversion conditions
            _useFastParsing = _useFastConversion
                && _singleCharDelimiter
                && !_options.NormalizeQuotes
                && _options.Comment == '\0'
                && !_options.EnableParallelProcessing
                && _options.QuoteMode != QuoteMode.Lenient;
        }

        private void InitializeColumnsFromFirstDataRow()
        {
            // Read the first data row to determine column count
            // Buffer it so it can be returned on the first Read() call
            while (true)
            {
                if (!ReadLine(out string line))
                {
                    // No data rows - leave columns empty
                    return;
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

                // Parse the line to get field count
                ParseLine(line);

                // Create columns based on field count
                for (int i = 0; i < _fieldsBuffer.Count; i++)
                {
                    var col = new CsvColumn($"Column{i}", _columns.Count, typeof(string));
                    col.SourceIndex = i;
                    _columns.Add(col);
                }

                // Cache max source index
                _maxSourceIndex = _fieldsBuffer.Count - 1;

                // Buffer this line so it's returned on the first Read()
                _bufferedFirstLine = line;
                _hasBufferedFirstLine = true;

                break;
            }
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

        #region Parallel Processing Pipeline

        private void StartParallelPipeline()
        {
            _useParallelProcessing = true;
            _cancellationSource = new CancellationTokenSource();

            int workerCount = _options.MaxDegreeOfParallelism > 0
                ? _options.MaxDegreeOfParallelism
                : Environment.ProcessorCount;

            int queueCapacity = _options.ParallelQueueDepth * _options.ParallelBatchSize;

            // Create bounded blocking collections for backpressure
            _lineQueue = new BlockingCollection<LineData>(new ConcurrentQueue<LineData>(), queueCapacity);
            _resultQueue = new BlockingCollection<ParsedRecord>(new ConcurrentQueue<ParsedRecord>(), queueCapacity);

            // Initialize thread-safe error collection
            if (_options.CollectParseErrors)
            {
                _parallelParseErrors = new ConcurrentQueue<CsvParseError>();
            }

            _nextExpectedRecordIndex = 0;
            _activeWorkers = workerCount;

            // Start producer thread (line reader)
            _producerThread = new Thread(ProducerLoop)
            {
                Name = "CsvReader-Producer",
                IsBackground = true
            };
            _producerThread.Start();

            // Start worker threads (parsers)
            _workerThreads = new Thread[workerCount];
            for (int i = 0; i < workerCount; i++)
            {
                _workerThreads[i] = new Thread(WorkerLoop)
                {
                    Name = $"CsvReader-Worker-{i}",
                    IsBackground = true
                };
                _workerThreads[i].Start();
            }
        }

        private void ProducerLoop()
        {
            try
            {
                long recordIndex = 0;
                var ct = _cancellationSource.Token;

                while (!ct.IsCancellationRequested)
                {
                    string line;

                    // Check for buffered first line (no-header mode)
                    if (_hasBufferedFirstLine)
                    {
                        line = _bufferedFirstLine;
                        _hasBufferedFirstLine = false;
                        _bufferedFirstLine = null;
                        // Line number was already incremented during initialization
                    }
                    else
                    {
                        if (!ReadLine(out line))
                        {
                            break;
                        }

                        Interlocked.Increment(ref _currentLineNumber);

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
                    }

                    // Normalize smart quotes if enabled (must be done in producer for consistency)
                    if (_options.NormalizeQuotes && line != null)
                    {
                        line = NormalizeSmartQuotes(line);
                    }

                    var lineData = new LineData(line, Interlocked.Read(ref _currentLineNumber), recordIndex++);

                    // Add to queue with cancellation support
                    try
                    {
                        _lineQueue.Add(lineData, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _pipelineException = ex;
            }
            finally
            {
                _lineQueue.CompleteAdding();
            }
        }

        private void WorkerLoop()
        {
            // Thread-local parsing state
            var fieldsBuffer = new List<FieldInfo>(64);
            var quotedFieldBuilder = new StringBuilder(256);
            var ct = _cancellationSource.Token;

            try
            {
                foreach (var lineData in _lineQueue.GetConsumingEnumerable(ct))
                {
                    if (ct.IsCancellationRequested)
                        break;

                    ParsedRecord result;
                    try
                    {
                        // Parse line
                        ParseLineThreadSafe(lineData.Line, fieldsBuffer, quotedFieldBuilder);

                        // Handle field count mismatch
                        int expectedCount = _maxSourceIndex >= 0 ? _maxSourceIndex + 1 : fieldsBuffer.Count;
                        if (fieldsBuffer.Count != expectedCount)
                        {
                            HandleFieldCountMismatchThreadSafe(fieldsBuffer, lineData.Line, expectedCount);
                        }

                        // Convert to typed values
                        object[] values = ConvertRecordThreadSafe(fieldsBuffer, lineData.RecordIndex);

                        result = new ParsedRecord(values, lineData.RecordIndex, lineData.LineNumber);
                    }
                    catch (Exception ex)
                    {
                        var error = new CsvParseError(
                            lineData.RecordIndex + 1,
                            -1,
                            lineData.Line,
                            ex.Message,
                            ex,
                            lineData.LineNumber,
                            0);

                        if (_parallelParseErrors != null)
                        {
                            _parallelParseErrors.Enqueue(error);

                            if (_options.MaxParseErrors > 0 && _parallelParseErrors.Count >= _options.MaxParseErrors)
                            {
                                _pipelineException = new CsvParseException($"Maximum parse errors ({_options.MaxParseErrors}) exceeded", error) { IsMaxErrorsExceeded = true };
                                _cancellationSource.Cancel();
                                return;
                            }
                        }

                        if (_options.ParseErrorAction == CsvParseErrorAction.ThrowException)
                        {
                            _pipelineException = new CsvParseException("CSV parse error", error);
                            _cancellationSource.Cancel();
                            return;
                        }

                        // For AdvanceToNextLine, create error record
                        result = new ParsedRecord(error, lineData.RecordIndex, lineData.LineNumber);
                    }

                    try
                    {
                        _resultQueue.Add(result, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
            }
            catch (InvalidOperationException)
            {
                // Collection completed, normal completion
            }
            catch (Exception ex)
            {
                _pipelineException = ex;
                _cancellationSource.Cancel();
            }
            finally
            {
                // Signal completion when all workers are done
                if (Interlocked.Decrement(ref _activeWorkers) == 0)
                {
                    _resultQueue.CompleteAdding();
                }
            }
        }

        /// <summary>
        /// Thread-safe line parsing that uses provided buffers instead of instance fields.
        /// </summary>
        private void ParseLineThreadSafe(string line, List<FieldInfo> fieldsBuffer, StringBuilder quotedFieldBuilder)
        {
            fieldsBuffer.Clear();

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
                var (field, wasQuoted, newPosition) = ParseFieldThreadSafe(lineSpan, position, delimiter, quote, escape, lenient, quotedFieldBuilder);
                fieldsBuffer.Add(new FieldInfo(field, wasQuoted));
                position = newPosition;

                if (position > lineSpan.Length)
                    break;
            }
        }

        /// <summary>
        /// Thread-safe field parsing that uses provided StringBuilder.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private (string value, bool wasQuoted, int newPosition) ParseFieldThreadSafe(
            ReadOnlySpan<char> line, int start, string delimiter, char quote, char escape, bool lenient, StringBuilder quotedFieldBuilder)
        {
            if (start >= line.Length)
            {
                return (string.Empty, false, start + delimiter.Length);
            }

            if (line[start] == quote)
            {
                if (lenient)
                {
                    var result = TryParseQuotedFieldLenientThreadSafe(line, start, delimiter, quote, escape, quotedFieldBuilder);
                    if (result.wasValidQuoted)
                    {
                        return (result.value, true, result.newPosition);
                    }
                    return ParseUnquotedField(line, start, delimiter);
                }
                return ParseQuotedFieldThreadSafe(line, start, delimiter, quote, escape, quotedFieldBuilder);
            }

            return ParseUnquotedField(line, start, delimiter);
        }

        private (string value, bool wasValidQuoted, int newPosition) TryParseQuotedFieldLenientThreadSafe(
            ReadOnlySpan<char> line, int start, string delimiter, char quote, char escape, StringBuilder quotedFieldBuilder)
        {
            quotedFieldBuilder.Clear();
            int i = start + 1;

            while (i < line.Length)
            {
                char c = line[i];

                if (c == escape && i + 1 < line.Length && line[i + 1] == quote)
                {
                    quotedFieldBuilder.Append(quote);
                    i += 2;
                }
                else if (c == '\\' && i + 1 < line.Length && line[i + 1] == quote)
                {
                    quotedFieldBuilder.Append(quote);
                    i += 2;
                }
                else if (c == quote)
                {
                    int afterQuote = i + 1;

                    if (afterQuote >= line.Length)
                    {
                        string value = TryInternString(quotedFieldBuilder.ToString());
                        return (value, true, line.Length + delimiter.Length);
                    }

                    if (MatchesDelimiter(line, afterQuote, delimiter))
                    {
                        string value = TryInternString(quotedFieldBuilder.ToString());
                        return (value, true, afterQuote + delimiter.Length);
                    }

                    int checkPos = afterQuote;
                    while (checkPos < line.Length && char.IsWhiteSpace(line[checkPos]))
                        checkPos++;

                    if (checkPos >= line.Length)
                    {
                        string value = TryInternString(quotedFieldBuilder.ToString());
                        return (value, true, line.Length + delimiter.Length);
                    }

                    if (MatchesDelimiter(line, checkPos, delimiter))
                    {
                        string value = TryInternString(quotedFieldBuilder.ToString());
                        return (value, true, checkPos + delimiter.Length);
                    }

                    quotedFieldBuilder.Append(c);
                    i++;
                }
                else
                {
                    quotedFieldBuilder.Append(c);
                    i++;
                }
            }

            return (null, false, 0);
        }

        private (string value, bool wasQuoted, int newPosition) ParseQuotedFieldThreadSafe(
            ReadOnlySpan<char> line, int start, string delimiter, char quote, char escape, StringBuilder quotedFieldBuilder)
        {
            quotedFieldBuilder.Clear();
            int i = start + 1;
            bool wasQuoted = true;

            while (i < line.Length)
            {
                char c = line[i];

                if (c == escape && i + 1 < line.Length && line[i + 1] == quote)
                {
                    quotedFieldBuilder.Append(quote);
                    i += 2;
                }
                else if (c == quote)
                {
                    i++;

                    if (i < line.Length)
                    {
                        if (MatchesDelimiter(line, i, delimiter))
                        {
                            i += delimiter.Length;
                        }
                    }
                    else
                    {
                        i += delimiter.Length;
                    }

                    string value = TryInternString(quotedFieldBuilder.ToString());
                    return (value, wasQuoted, i);
                }
                else
                {
                    quotedFieldBuilder.Append(c);
                    i++;
                }
            }

            string finalValue = TryInternString(quotedFieldBuilder.ToString());
            return (finalValue, wasQuoted, line.Length + delimiter.Length);
        }

        private void HandleFieldCountMismatchThreadSafe(List<FieldInfo> fieldsBuffer, string line, int expectedCount)
        {
            int actualCount = fieldsBuffer.Count;

            switch (_options.MismatchedFieldAction)
            {
                case MismatchedFieldAction.ThrowException:
                    throw new FormatException(
                        $"Row has {actualCount} field(s) but expected {expectedCount} based on header. " +
                        $"Row content: '{line}'");

                case MismatchedFieldAction.PadWithNulls:
                    while (fieldsBuffer.Count < expectedCount)
                    {
                        fieldsBuffer.Add(new FieldInfo(string.Empty, false));
                    }
                    break;

                case MismatchedFieldAction.TruncateExtra:
                    while (fieldsBuffer.Count > expectedCount)
                    {
                        fieldsBuffer.RemoveAt(fieldsBuffer.Count - 1);
                    }
                    break;

                case MismatchedFieldAction.PadOrTruncate:
                    while (fieldsBuffer.Count < expectedCount)
                    {
                        fieldsBuffer.Add(new FieldInfo(string.Empty, false));
                    }
                    while (fieldsBuffer.Count > expectedCount)
                    {
                        fieldsBuffer.RemoveAt(fieldsBuffer.Count - 1);
                    }
                    break;
            }
        }

        /// <summary>
        /// Thread-safe record conversion that creates a new values array.
        /// </summary>
        private object[] ConvertRecordThreadSafe(List<FieldInfo> fieldsBuffer, long recordIndex)
        {
            var values = new object[_columns.Count + _staticColumns.Count];

            for (int i = 0; i < _columns.Count; i++)
            {
                var column = _columns[i];
                int sourceIndex = column.SourceIndex;

                string rawValue = sourceIndex < fieldsBuffer.Count ? fieldsBuffer[sourceIndex].Value : null;
                bool wasQuoted = sourceIndex < fieldsBuffer.Count && fieldsBuffer[sourceIndex].WasQuoted;

                // Apply trimming
                rawValue = ApplyTrimming(rawValue, wasQuoted);

                // Check for explicit null value
                if (rawValue != null && _options.NullValue != null && rawValue == _options.NullValue)
                {
                    rawValue = null;
                    wasQuoted = false;
                }

                // Handle null/empty values
                if (string.IsNullOrEmpty(rawValue))
                {
                    if (_options.DistinguishEmptyFromNull)
                    {
                        if (wasQuoted)
                        {
                            if (column.DataType == typeof(string))
                            {
                                values[i] = string.Empty;
                            }
                            else if (column.UseDefaultForNull || _options.UseColumnDefaults)
                            {
                                values[i] = column.DefaultValue;
                            }
                            else
                            {
                                values[i] = DBNull.Value;
                            }
                        }
                        else
                        {
                            if (column.UseDefaultForNull || _options.UseColumnDefaults)
                            {
                                values[i] = column.DefaultValue;
                            }
                            else
                            {
                                values[i] = DBNull.Value;
                            }
                        }
                    }
                    else
                    {
                        if (column.UseDefaultForNull || _options.UseColumnDefaults)
                        {
                            values[i] = column.DefaultValue;
                        }
                        else
                        {
                            values[i] = DBNull.Value;
                        }
                    }
                    continue;
                }

                // Convert to target type
                values[i] = ConvertValue(rawValue, column);
            }

            // Add static column values
            for (int i = 0; i < _staticColumns.Count; i++)
            {
                values[_columns.Count + i] = _staticColumns[i].GetValue(recordIndex);
            }

            return values;
        }

        /// <summary>
        /// Reads the next record from the parallel pipeline.
        /// </summary>
        private bool ReadParallel()
        {
            // Check for pipeline errors
            if (_pipelineException != null)
            {
                throw _pipelineException;
            }

            while (true)
            {
                // Check if we have a result ready in the pending buffer
                lock (_resultLock)
                {
                    if (_pendingResults.TryGetValue(_nextExpectedRecordIndex, out var record))
                    {
                        _pendingResults.Remove(_nextExpectedRecordIndex);
                        _nextExpectedRecordIndex++;

                        // Skip error records in AdvanceToNextLine mode
                        if (record.Error != null)
                        {
                            if (_options.ParseErrorAction == CsvParseErrorAction.RaiseEvent)
                            {
                                var args = new CsvParseErrorEventArgs(record.Error, CsvParseErrorAction.AdvanceToNextLine);
                                ParseError?.Invoke(this, args);
                                if (args.Action == CsvParseErrorAction.ThrowException)
                                {
                                    throw new CsvParseException("CSV parse error", record.Error);
                                }
                            }
                            continue;
                        }

                        _currentParsedRecord = record;
                        Interlocked.Exchange(ref _currentRecordIndex, record.RecordIndex);
                        Array.Copy(record.Values, _convertedValues, record.Values.Length);
                        return true;
                    }
                }

                // Try to read from result queue
                ParsedRecord result;
                try
                {
                    if (!_resultQueue.TryTake(out result, 100))
                    {
                        // Check if completed
                        if (_resultQueue.IsCompleted)
                        {
                            // Check for any remaining buffered results
                            lock (_resultLock)
                            {
                                if (_pendingResults.Count > 0 && _pendingResults.TryGetValue(_nextExpectedRecordIndex, out var lastRecord))
                                {
                                    _pendingResults.Remove(_nextExpectedRecordIndex);
                                    _nextExpectedRecordIndex++;

                                    if (lastRecord.Error != null)
                                    {
                                        continue;
                                    }

                                    _currentParsedRecord = lastRecord;
                                    Interlocked.Exchange(ref _currentRecordIndex, lastRecord.RecordIndex);
                                    Array.Copy(lastRecord.Values, _convertedValues, lastRecord.Values.Length);
                                    return true;
                                }
                            }

                            // Check for pipeline errors one more time
                            if (_pipelineException != null)
                            {
                                throw _pipelineException;
                            }

                            _currentRecord = null;
                            return false;
                        }

                        // Check for pipeline errors
                        if (_pipelineException != null)
                        {
                            throw _pipelineException;
                        }

                        continue;
                    }
                }
                catch (InvalidOperationException)
                {
                    // Collection completed
                    if (_pipelineException != null)
                    {
                        throw _pipelineException;
                    }

                    _currentRecord = null;
                    return false;
                }

                // Check for pipeline errors after reading
                if (_pipelineException != null)
                {
                    throw _pipelineException;
                }

                // If this is the next expected record, use it directly
                if (result.RecordIndex == _nextExpectedRecordIndex)
                {
                    _nextExpectedRecordIndex++;

                    // Skip error records
                    if (result.Error != null)
                    {
                        if (_options.ParseErrorAction == CsvParseErrorAction.RaiseEvent)
                        {
                            var args = new CsvParseErrorEventArgs(result.Error, CsvParseErrorAction.AdvanceToNextLine);
                            ParseError?.Invoke(this, args);
                            if (args.Action == CsvParseErrorAction.ThrowException)
                            {
                                throw new CsvParseException("CSV parse error", result.Error);
                            }
                        }
                        continue;
                    }

                    // Synchronize to prevent GetValue/GetValues from reading during Array.Copy
                    lock (_resultLock)
                    {
                        _currentParsedRecord = result;
                        Interlocked.Exchange(ref _currentRecordIndex, result.RecordIndex);
                        Array.Copy(result.Values, _convertedValues, result.Values.Length);
                    }
                    return true;
                }

                // Out of order - buffer it
                lock (_resultLock)
                {
                    _pendingResults[result.RecordIndex] = result;
                }
            }
        }

        private void StopParallelPipeline()
        {
            if (!_useParallelProcessing)
                return;

            _cancellationSource?.Cancel();

            try
            {
                // Wait for producer thread to complete
                if (_producerThread != null && _producerThread.IsAlive)
                {
                    _producerThread.Join(TimeSpan.FromSeconds(5));
                }

                // Wait for worker threads to complete
                if (_workerThreads != null)
                {
                    foreach (var thread in _workerThreads)
                    {
                        if (thread != null && thread.IsAlive)
                        {
                            thread.Join(TimeSpan.FromSeconds(5));
                        }
                    }
                }
            }
            finally
            {
                _cancellationSource?.Dispose();
                _cancellationSource = null;

                _lineQueue?.Dispose();
                _lineQueue = null;

                _resultQueue?.Dispose();
                _resultQueue = null;

                _useParallelProcessing = false;
            }

            // Transfer parallel errors to main error list
            if (_parallelParseErrors != null && _parseErrors != null)
            {
                while (_parallelParseErrors.TryDequeue(out var error))
                {
                    _parseErrors.Add(error);
                }
            }
        }

        #endregion

        #region IDataReader Implementation

        /// <summary>
        /// Reads the next record from the CSV file.
        /// </summary>
        /// <exception cref="OperationCanceledException">Thrown when the <see cref="CsvReaderOptions.CancellationToken"/> is cancelled.</exception>
        public bool Read()
        {
            ThrowIfClosed();

            // Check for cancellation
            _options.CancellationToken.ThrowIfCancellationRequested();

            Initialize();

            bool result;

            // Use parallel pipeline if enabled
            if (_useParallelProcessing)
            {
                result = ReadParallel();
            }
            // Handle buffered first line from no-header initialization (must use line-based parsing)
            else if (_hasBufferedFirstLine)
            {
                result = ReadBufferedFirstLine();
            }
            else
            {
                // Use direct field-by-field parsing (high-performance path)
                result = ReadSequentialDirect();
            }

            // Report progress if enabled
            if (result)
            {
                ReportProgressIfNeeded();
            }

            return result;
        }

        /// <summary>
        /// Reports progress to the callback if configured and interval has been reached.
        /// </summary>
        private void ReportProgressIfNeeded()
        {
            var callback = _options.ProgressCallback;
            int interval = _options.ProgressReportInterval;

            if (callback == null || interval <= 0)
                return;

            long currentRecord = _currentRecordIndex;
            if (currentRecord - _lastProgressReport >= interval)
            {
                _lastProgressReport = currentRecord;

                long bytesRead = -1;
                if (_underlyingStream != null && _underlyingStream.CanSeek)
                {
                    try { bytesRead = _underlyingStream.Position; }
                    catch { /* Ignore seek errors */ }
                }

                var elapsed = _progressStopwatch?.Elapsed ?? TimeSpan.Zero;
                var progress = new CsvProgress(
                    currentRecord,
                    _currentLineNumber,
                    bytesRead,
                    _totalFileSize,
                    elapsed);

                callback(progress);
            }
        }

        /// <summary>
        /// Handles the special case of reading the buffered first line from no-header initialization.
        /// </summary>
        private bool ReadBufferedFirstLine()
        {
            string line = _bufferedFirstLine;
            _hasBufferedFirstLine = false;
            _bufferedFirstLine = null;

            try
            {
                ParseLine(line);
                _currentRecordIndex++;

                int expectedCount = _maxSourceIndex >= 0 ? _maxSourceIndex + 1 : _fieldsBuffer.Count;
                if (_fieldsBuffer.Count != expectedCount)
                {
                    HandleFieldCountMismatch(line, expectedCount);
                }

                EnsureRecordBufferCapacity(_fieldsBuffer.Count);
                for (int i = 0; i < _fieldsBuffer.Count; i++)
                {
                    _recordBuffer[i] = _fieldsBuffer[i].Value;
                    _quotedBuffer[i] = _fieldsBuffer[i].WasQuoted;
                }
                _currentRecord = _recordBuffer;
                _currentRecordWasQuoted = _quotedBuffer;

                ConvertCurrentRecord();
                return true;
            }
            catch (Exception ex) when (!(ex is CsvParseException parseEx && parseEx.IsMaxErrorsExceeded))
            {
                return HandleParseError(ex, line);
            }
        }

        /// <summary>
        /// High-performance sequential reading using direct field-by-field parsing.
        /// Eliminates intermediate line string allocation for ~10-15% performance improvement.
        /// </summary>
        private bool ReadSequentialDirect()
        {
#if NET8_0_OR_GREATER
            // Ultra-fast path: inline parsing directly to _convertedValues for simple CSV
            if (_useFastParsing && _isInitialized)
            {
                return ReadSequentialUltraFast();
            }
#endif

            while (true)
            {
                try
                {
                    if (!ReadNextRecordDirect())
                    {
                        _currentRecord = null;
                        return false;
                    }

                    _currentRecordIndex++;

                    // Handle field count mismatch
                    int expectedCount = _maxSourceIndex >= 0 ? _maxSourceIndex + 1 : _fieldsBuffer.Count;
                    if (_fieldsBuffer.Count != expectedCount)
                    {
                        HandleFieldCountMismatchDirect(expectedCount);
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
                    if (!HandleParseError(ex, null))
                    {
                        // AdvanceToNextLine - continue to next record
                        continue;
                    }
                    // If HandleParseError returns true, an exception was thrown or we should return
                }
            }
        }

#if NET8_0_OR_GREATER
        /// <summary>
        /// Ultra-fast inline parsing for simple CSV files (no quotes, no special options).
        /// Writes directly to _convertedValues, skipping all intermediate buffers.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private bool ReadSequentialUltraFast()
        {
            char delimChar = _delimiterFirstChar;
            char quoteChar = _options.Quote;
            int columnCount = _columns.Count;
            var values = _convertedValues;

            while (true)
            {
                // Ensure we have data
                if (_bufferPosition >= _bufferLength)
                {
                    if (!RefillBuffer())
                    {
                        _currentRecord = null;
                        return false;
                    }
                }

                // Skip empty lines
                while (_bufferPosition < _bufferLength)
                {
                    char c = _buffer[_bufferPosition];
                    if (c == '\r')
                    {
                        _bufferPosition++;
                        _currentLineNumber++;
                        if (_bufferPosition < _bufferLength && _buffer[_bufferPosition] == '\n')
                            _bufferPosition++;
                        continue;
                    }
                    if (c == '\n')
                    {
                        _bufferPosition++;
                        _currentLineNumber++;
                        continue;
                    }
                    break; // Found start of record
                }

                if (_bufferPosition >= _bufferLength)
                    continue; // Need more data

                // Parse the record directly into _convertedValues
                _currentRecordIndex++;
                int fieldIndex = 0;

                while (fieldIndex < columnCount)
                {
                    if (_bufferPosition >= _bufferLength)
                    {
                        // Buffer exhausted mid-record - fall back to standard path
                        _currentRecordIndex--;
                        _useFastParsing = false;
                        return ReadSequentialDirect();
                    }

                    char c = _buffer[_bufferPosition];

                    // Check for quoted field - fall back to standard path
                    if (c == quoteChar)
                    {
                        _currentRecordIndex--;
                        _useFastParsing = false;
                        return ReadSequentialDirect();
                    }

                    int fieldStart = _bufferPosition;

                    // Use SIMD to find delimiter or newline
                    ReadOnlySpan<char> remaining = _buffer.AsSpan(_bufferPosition, _bufferLength - _bufferPosition);
                    int idx = remaining.IndexOfAny(_fieldTerminators);

                    if (idx < 0)
                    {
                        // No terminator found - fall back to standard path
                        _currentRecordIndex--;
                        _useFastParsing = false;
                        return ReadSequentialDirect();
                    }

                    _bufferPosition += idx;
                    c = _buffer[_bufferPosition];

                    // Create field string
                    int sourceIndex = _columns[fieldIndex].SourceIndex;
                    if (sourceIndex == fieldIndex) // Common case: sequential columns
                    {
                        int length = _bufferPosition - fieldStart;
                        if (length == 0)
                        {
                            values[fieldIndex] = DBNull.Value;
                        }
                        else
                        {
                            values[fieldIndex] = new string(_buffer, fieldStart, length);
                        }
                    }
                    else
                    {
                        // Column mapping is non-trivial - fall back
                        _currentRecordIndex--;
                        _useFastParsing = false;
                        return ReadSequentialDirect();
                    }

                    if (c == delimChar)
                    {
                        _bufferPosition++; // Skip delimiter
                        fieldIndex++;
                    }
                    else // c == '\r' || c == '\n'
                    {
                        // End of record - skip newline
                        if (c == '\r')
                        {
                            _bufferPosition++;
                            if (_bufferPosition < _bufferLength && _buffer[_bufferPosition] == '\n')
                                _bufferPosition++;
                        }
                        else
                        {
                            _bufferPosition++;
                        }
                        fieldIndex++;
                        break;
                    }
                }

                // Fill remaining columns with DBNull
                while (fieldIndex < columnCount)
                {
                    values[fieldIndex] = DBNull.Value;
                    fieldIndex++;
                }

                _currentRecord = _recordBuffer;
                _currentLineNumber++;
                return true;
            }
        }
#endif

        /// <summary>
        /// Handles parse errors consistently for both parsing paths.
        /// </summary>
        private bool HandleParseError(Exception ex, string line)
        {
            var error = new CsvParseError(
                _currentRecordIndex + 1,
                -1,
                line ?? "(direct parsing - line not available)",
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
                    return false; // Signal to continue

                case CsvParseErrorAction.RaiseEvent:
                    var args = new CsvParseErrorEventArgs(error, CsvParseErrorAction.AdvanceToNextLine);
                    ParseError?.Invoke(this, args);
                    if (args.Action == CsvParseErrorAction.ThrowException)
                    {
                        throw new CsvParseException("CSV parse error", error);
                    }
                    return false; // Signal to continue
            }

            return false;
        }

        /// <summary>
        /// Handles field count mismatch for direct parsing mode.
        /// </summary>
        private void HandleFieldCountMismatchDirect(int expectedCount)
        {
            int actualCount = _fieldsBuffer.Count;

            switch (_options.MismatchedFieldAction)
            {
                case MismatchedFieldAction.ThrowException:
                    throw new FormatException(
                        $"Row has {actualCount} field(s) but expected {expectedCount} based on header.");

                case MismatchedFieldAction.PadWithNulls:
                    while (_fieldsBuffer.Count < expectedCount)
                    {
                        _fieldsBuffer.Add(new FieldInfo(string.Empty, false));
                    }
                    break;

                case MismatchedFieldAction.TruncateExtra:
                    while (_fieldsBuffer.Count > expectedCount)
                    {
                        _fieldsBuffer.RemoveAt(_fieldsBuffer.Count - 1);
                    }
                    break;

                case MismatchedFieldAction.PadOrTruncate:
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
            // Fast path: all columns are strings with no special handling needed
            if (_useFastConversion)
            {
                ConvertCurrentRecordFast();
                return;
            }

            // Standard path with all options supported
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

        /// <summary>
        /// Fast conversion path for simple string-only columns with no special handling.
        /// This avoids all the per-column checks and branching.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ConvertCurrentRecordFast()
        {
            int columnCount = _columns.Count;
            var record = _currentRecord;
            var values = _convertedValues;

            // Direct copy of string values - no conversion, trimming, or null handling
            for (int i = 0; i < columnCount; i++)
            {
                int sourceIndex = _columns[i].SourceIndex;
                string rawValue = sourceIndex < record.Length ? record[sourceIndex] : null;

                // Empty strings become DBNull for consistency with database behavior
                if (string.IsNullOrEmpty(rawValue))
                {
                    values[i] = DBNull.Value;
                }
                else
                {
                    values[i] = rawValue;
                }
            }
        }

        private object ConvertValue(string value, CsvColumn column)
        {
            if (column.DataType == typeof(string))
            {
                return value;
            }

            // Use cached converter (resolved during initialization) to avoid per-row registry lookups
            ITypeConverter converter = column.CachedConverter;

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
        /// <remarks>
        /// Thread-safety: In parallel mode, this method is thread-safe and can be called
        /// from any thread while Read() is being called from another thread. However,
        /// the value returned represents a snapshot and may change after the next Read() call.
        /// </remarks>
        public object GetValue(int ordinal)
        {
            ThrowIfClosed();
            ValidateOrdinal(ordinal);

            // In parallel mode, synchronize access to prevent torn reads during Array.Copy
            if (_useParallelProcessing)
            {
                lock (_resultLock)
                {
                    return _convertedValues[ordinal];
                }
            }
            return _convertedValues[ordinal];
        }

        /// <summary>
        /// Gets all values in the current record.
        /// </summary>
        /// <remarks>
        /// Thread-safety: In parallel mode, this method is thread-safe and can be called
        /// from any thread while Read() is being called from another thread. However,
        /// the values returned represent a snapshot and may change after the next Read() call.
        /// </remarks>
        public int GetValues(object[] values)
        {
            ThrowIfClosed();
            if (values == null)
                throw new ArgumentNullException(nameof(values));

            // In parallel mode, synchronize access to prevent torn reads during Array.Copy
            if (_useParallelProcessing)
            {
                lock (_resultLock)
                {
                    int count = Math.Min(values.Length, _convertedValues.Length);
                    Array.Copy(_convertedValues, values, count);
                    return count;
                }
            }

            int seqCount = Math.Min(values.Length, _convertedValues.Length);
            Array.Copy(_convertedValues, values, seqCount);
            return seqCount;
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

        /// <inheritdoc />
        public bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);
        /// <inheritdoc />
        public byte GetByte(int ordinal) => (byte)GetValue(ordinal);
        /// <inheritdoc />
        public char GetChar(int ordinal) => (char)GetValue(ordinal);
        /// <inheritdoc />
        public DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);
        /// <inheritdoc />
        public decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);
        /// <inheritdoc />
        public double GetDouble(int ordinal) => (double)GetValue(ordinal);
        /// <inheritdoc />
        public float GetFloat(int ordinal) => (float)GetValue(ordinal);
        /// <inheritdoc />
        public Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);
        /// <inheritdoc />
        public short GetInt16(int ordinal) => (short)GetValue(ordinal);
        /// <inheritdoc />
        public int GetInt32(int ordinal) => (int)GetValue(ordinal);
        /// <inheritdoc />
        public long GetInt64(int ordinal) => (long)GetValue(ordinal);
        /// <inheritdoc />
        public string GetString(int ordinal) => GetValue(ordinal)?.ToString();

        /// <inheritdoc />
        public long GetBytes(int ordinal, long fieldOffset, byte[] buffer, int bufferOffset, int length)
        {
            throw new NotSupportedException("GetBytes is not supported for CSV data");
        }

        /// <inheritdoc />
        public long GetChars(int ordinal, long fieldOffset, char[] buffer, int bufferOffset, int length)
        {
            string value = GetString(ordinal);
            if (value == null)
                return 0;

            int copyLength = Math.Min(length, value.Length - (int)fieldOffset);
            value.CopyTo((int)fieldOffset, buffer, bufferOffset, copyLength);
            return copyLength;
        }

        /// <inheritdoc />
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

        #region Direct Field Parsing (Zero-Copy from Buffer)

        /// <summary>
        /// Reads the next record directly from the buffer without creating intermediate line strings.
        /// This is the high-performance path that eliminates ~1 string allocation per row.
        /// </summary>
        private bool ReadNextRecordDirect()
        {
            _fieldsBuffer.Clear();
            _endOfRecord = false;

            // Skip empty lines and comments
            while (!_endOfStream)
            {
                // Skip whitespace at start of line if needed
                if (!EnsureBufferData())
                {
                    return _fieldsBuffer.Count > 0;
                }

                // Check for empty line
                char c = _buffer[_bufferPosition];
                if (c == '\r' || c == '\n')
                {
                    SkipNewline();
                    _currentLineNumber++;
                    if (_options.SkipEmptyLines)
                        continue;
                    // Empty line as a record with empty fields is not typical, return no fields
                    return false;
                }

                // Check for comment line
                if (c == _options.Comment)
                {
                    SkipToEndOfLine();
                    _currentLineNumber++;
                    continue;
                }

                // Found start of data - parse fields
                break;
            }

            if (_endOfStream && _bufferPosition >= _bufferLength)
                return false;

            // Parse all fields in the record
            while (!_endOfRecord && !_endOfStream)
            {
                ReadNextFieldDirect();
            }

            _currentLineNumber++;
            return _fieldsBuffer.Count > 0 || !_endOfStream;
        }

        /// <summary>
        /// Reads the next field directly from the buffer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ReadNextFieldDirect()
        {
            if (!EnsureBufferData())
            {
                _endOfRecord = true;
                return;
            }

            char c = _buffer[_bufferPosition];

            // Check for quoted field (including smart quotes when NormalizeQuotes is enabled)
            if (c == _options.Quote || (_options.NormalizeQuotes && IsSmartDoubleQuote(c)))
            {
                if (_options.QuoteMode == QuoteMode.Lenient)
                {
                    ReadQuotedFieldDirectLenient();
                }
                else
                {
                    ReadQuotedFieldDirect();
                }
                return;
            }

            // Unquoted field - fast path for single-char delimiter
            if (_singleCharDelimiter)
            {
                ReadUnquotedFieldDirectSingleDelim();
            }
            else
            {
                ReadUnquotedFieldDirectMultiDelim();
            }
        }

        /// <summary>
        /// Checks if a character is a smart double quote.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsSmartDoubleQuote(char c)
        {
            return c == LeftDoubleQuote || c == RightDoubleQuote;
        }

        /// <summary>
        /// Fast path for unquoted fields with single-character delimiter.
        /// Uses SIMD-accelerated search on .NET 8+.
        /// </summary>
        private void ReadUnquotedFieldDirectSingleDelim()
        {
            int fieldStart = _bufferPosition;

#if NET8_0_OR_GREATER
            // SIMD-accelerated path for .NET 8+
            if (!_options.NormalizeQuotes)
            {
                ReadUnquotedFieldSimd(fieldStart);
                return;
            }
#endif
            // Scalar path for .NET Framework or when smart quote normalization is enabled
            ReadUnquotedFieldScalar(fieldStart);
        }

#if NET8_0_OR_GREATER
        /// <summary>
        /// SIMD-accelerated unquoted field parsing for .NET 8+.
        /// Uses SearchValues to find delimiter or newline in a single vectorized operation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ReadUnquotedFieldSimd(int fieldStart)
        {
            char delimChar = _delimiterFirstChar;

            while (true)
            {
                // Create a span from current position to end of buffer
                ReadOnlySpan<char> remaining = _buffer.AsSpan(_bufferPosition, _bufferLength - _bufferPosition);

                // SIMD search for delimiter, \r, or \n
                int idx = remaining.IndexOfAny(_fieldTerminators);

                if (idx >= 0)
                {
                    _bufferPosition += idx;
                    char c = _buffer[_bufferPosition];

                    if (c == delimChar)
                    {
                        // Found delimiter - extract field
                        string value = CreateFieldString(fieldStart, _bufferPosition - fieldStart);
                        _fieldsBuffer.Add(new FieldInfo(value, false));
                        _bufferPosition++; // Skip delimiter
                        return;
                    }

                    // Must be \r or \n - end of record
                    string fieldValue = CreateFieldString(fieldStart, _bufferPosition - fieldStart);
                    _fieldsBuffer.Add(new FieldInfo(fieldValue, false));
                    SkipNewline();
                    _endOfRecord = true;
                    return;
                }

                // No terminator found in current buffer - field spans buffers
                _bufferPosition = _bufferLength;
                ReadUnquotedFieldSpanningBuffer(fieldStart);
                return;
            }
        }
#endif

        /// <summary>
        /// Scalar (non-SIMD) unquoted field parsing. Used on .NET Framework
        /// and when smart quote normalization is enabled.
        /// </summary>
        private void ReadUnquotedFieldScalar(int fieldStart)
        {
            char delimChar = _delimiterFirstChar;

            // Scan for delimiter, newline, or end of buffer
            while (_bufferPosition < _bufferLength)
            {
                char c = _buffer[_bufferPosition];

                if (c == delimChar)
                {
                    // Found delimiter - extract field
                    string value = CreateFieldString(fieldStart, _bufferPosition - fieldStart);
                    _fieldsBuffer.Add(new FieldInfo(value, false));
                    _bufferPosition++; // Skip delimiter
                    return;
                }

                if (c == '\r' || c == '\n')
                {
                    // End of record
                    string value = CreateFieldString(fieldStart, _bufferPosition - fieldStart);
                    _fieldsBuffer.Add(new FieldInfo(value, false));
                    SkipNewline();
                    _endOfRecord = true;
                    return;
                }

                // Handle smart quotes if enabled
                if (_options.NormalizeQuotes && IsSmartQuote(c))
                {
                    // Need to handle smart quote normalization - fall back to accumulator
                    ReadUnquotedFieldWithNormalization(fieldStart);
                    return;
                }

                _bufferPosition++;
            }

            // Hit end of buffer - field may span buffers
            ReadUnquotedFieldSpanningBuffer(fieldStart);
        }

        /// <summary>
        /// Handles unquoted fields that span buffer boundaries.
        /// </summary>
        private void ReadUnquotedFieldSpanningBuffer(int fieldStart)
        {
            _fieldAccumulator.Clear();

            // Append what we have so far
            if (_bufferPosition > fieldStart)
            {
                _fieldAccumulator.Append(_buffer, fieldStart, _bufferPosition - fieldStart);
            }

            char delimChar = _delimiterFirstChar;

            // Continue reading until we find delimiter or newline
            while (true)
            {
                if (!RefillBuffer())
                {
                    // End of stream - whatever we accumulated is the field
                    string value = TryInternString(_fieldAccumulator.ToString());
                    _fieldsBuffer.Add(new FieldInfo(value, false));
                    _endOfRecord = true;
                    return;
                }

                while (_bufferPosition < _bufferLength)
                {
                    char c = _buffer[_bufferPosition];

                    if (_singleCharDelimiter && c == delimChar)
                    {
                        string value = TryInternString(_fieldAccumulator.ToString());
                        _fieldsBuffer.Add(new FieldInfo(value, false));
                        _bufferPosition++;
                        return;
                    }

                    if (!_singleCharDelimiter && c == delimChar && MatchesDelimiterAtPosition())
                    {
                        string value = TryInternString(_fieldAccumulator.ToString());
                        _fieldsBuffer.Add(new FieldInfo(value, false));
                        _bufferPosition += _options.Delimiter.Length;
                        return;
                    }

                    if (c == '\r' || c == '\n')
                    {
                        string value = TryInternString(_fieldAccumulator.ToString());
                        _fieldsBuffer.Add(new FieldInfo(value, false));
                        SkipNewline();
                        _endOfRecord = true;
                        return;
                    }

                    // Handle smart quote normalization
                    if (_options.NormalizeQuotes)
                    {
                        c = NormalizeSmartQuoteChar(c);
                    }

                    _fieldAccumulator.Append(c);
                    _bufferPosition++;
                }
            }
        }

        /// <summary>
        /// Handles unquoted fields with smart quote normalization.
        /// </summary>
        private void ReadUnquotedFieldWithNormalization(int fieldStart)
        {
            _fieldAccumulator.Clear();

            // Copy and normalize what we've seen so far
            for (int i = fieldStart; i < _bufferPosition; i++)
            {
                _fieldAccumulator.Append(NormalizeSmartQuoteChar(_buffer[i]));
            }

            char delimChar = _delimiterFirstChar;

            while (true)
            {
                while (_bufferPosition < _bufferLength)
                {
                    char c = _buffer[_bufferPosition];

                    if (_singleCharDelimiter && c == delimChar)
                    {
                        string value = TryInternString(_fieldAccumulator.ToString());
                        _fieldsBuffer.Add(new FieldInfo(value, false));
                        _bufferPosition++;
                        return;
                    }

                    if (c == '\r' || c == '\n')
                    {
                        string value = TryInternString(_fieldAccumulator.ToString());
                        _fieldsBuffer.Add(new FieldInfo(value, false));
                        SkipNewline();
                        _endOfRecord = true;
                        return;
                    }

                    _fieldAccumulator.Append(NormalizeSmartQuoteChar(c));
                    _bufferPosition++;
                }

                if (!RefillBuffer())
                {
                    string value = TryInternString(_fieldAccumulator.ToString());
                    _fieldsBuffer.Add(new FieldInfo(value, false));
                    _endOfRecord = true;
                    return;
                }
            }
        }

        /// <summary>
        /// Path for unquoted fields with multi-character delimiter.
        /// </summary>
        private void ReadUnquotedFieldDirectMultiDelim()
        {
            int fieldStart = _bufferPosition;
            char delimFirstChar = _delimiterFirstChar;
            int delimLength = _options.Delimiter.Length;

            while (_bufferPosition < _bufferLength)
            {
                char c = _buffer[_bufferPosition];

                if (c == delimFirstChar && _bufferPosition + delimLength <= _bufferLength)
                {
                    if (MatchesDelimiterAtPosition())
                    {
                        string value = CreateFieldString(fieldStart, _bufferPosition - fieldStart);
                        _fieldsBuffer.Add(new FieldInfo(value, false));
                        _bufferPosition += delimLength;
                        return;
                    }
                }

                if (c == '\r' || c == '\n')
                {
                    string value = CreateFieldString(fieldStart, _bufferPosition - fieldStart);
                    _fieldsBuffer.Add(new FieldInfo(value, false));
                    SkipNewline();
                    _endOfRecord = true;
                    return;
                }

                if (_options.NormalizeQuotes && IsSmartQuote(c))
                {
                    ReadUnquotedFieldWithNormalization(fieldStart);
                    return;
                }

                _bufferPosition++;
            }

            // Hit end of buffer
            ReadUnquotedFieldSpanningBuffer(fieldStart);
        }

        /// <summary>
        /// Reads a quoted field directly from the buffer.
        /// </summary>
        private void ReadQuotedFieldDirect()
        {
            _bufferPosition++; // Skip opening quote
            _quotedFieldBuilder.Clear();

            char quote = _options.Quote;
            char escape = _options.Escape;
            int quotedLength = 0;

            while (true)
            {
                if (!EnsureBufferData())
                {
                    // Unterminated quoted field at end of file
                    string value = TryInternString(_quotedFieldBuilder.ToString());
                    _fieldsBuffer.Add(new FieldInfo(value, true));
                    _endOfRecord = true;
                    return;
                }

                char c = _buffer[_bufferPosition];

                // Handle escaped quotes (RFC 4180: "" or custom escape like \")
                if (c == escape && _bufferPosition + 1 < _bufferLength)
                {
                    char next = _buffer[_bufferPosition + 1];
                    if (next == quote || (_options.NormalizeQuotes && IsSmartDoubleQuote(next)))
                    {
                        _quotedFieldBuilder.Append(quote);
                        _bufferPosition += 2;
                        quotedLength += 2;
                        CheckQuotedFieldLength(quotedLength);
                        continue;
                    }
                }

                // Check for closing quote (including smart quotes when NormalizeQuotes is enabled)
                if (c == quote || (_options.NormalizeQuotes && IsSmartDoubleQuote(c)))
                {
                    // Found closing quote
                    _bufferPosition++; // Skip closing quote

                    // Skip to delimiter or newline
                    SkipAfterQuotedField();

                    string value = TryInternString(_quotedFieldBuilder.ToString());
                    _fieldsBuffer.Add(new FieldInfo(value, true));
                    return;
                }

                // Handle smart quote normalization for single quotes
                if (_options.NormalizeQuotes && (c == LeftSingleQuote || c == RightSingleQuote))
                {
                    c = '\'';
                }

                _quotedFieldBuilder.Append(c);
                _bufferPosition++;
                quotedLength++;
                CheckQuotedFieldLength(quotedLength);
            }
        }

        /// <summary>
        /// Reads a quoted field in lenient mode - if the quote doesn't properly close,
        /// treat it as a literal character and return the whole field as unquoted.
        /// </summary>
        private void ReadQuotedFieldDirectLenient()
        {
            char openingQuote = _buffer[_bufferPosition];
            _bufferPosition++; // Skip opening quote
            _quotedFieldBuilder.Clear();

            // In lenient mode, we also track the raw content in case we need to return it as unquoted
            _fieldAccumulator.Clear();
            _fieldAccumulator.Append(openingQuote); // Include opening quote in raw content

            char quote = _options.Quote;
            char escape = _options.Escape;
            int quotedLength = 0;

            while (true)
            {
                if (!EnsureBufferData())
                {
                    // EOF - return accumulated raw content as unquoted (no valid closing quote found)
                    string value = TryInternString(_fieldAccumulator.ToString());
                    _fieldsBuffer.Add(new FieldInfo(value, false));
                    _endOfRecord = true;
                    return;
                }

                char c = _buffer[_bufferPosition];

                // Handle escaped quotes (RFC 4180: "" or backslash escape)
                if (c == escape && _bufferPosition + 1 < _bufferLength)
                {
                    char next = _buffer[_bufferPosition + 1];
                    if (next == quote || (_options.NormalizeQuotes && IsSmartDoubleQuote(next)))
                    {
                        _quotedFieldBuilder.Append(quote);
                        _fieldAccumulator.Append(c);
                        _fieldAccumulator.Append(next);
                        _bufferPosition += 2;
                        quotedLength += 2;
                        CheckQuotedFieldLength(quotedLength);
                        continue;
                    }
                }

                // Backslash escape in lenient mode
                if (c == '\\' && _bufferPosition + 1 < _bufferLength)
                {
                    char next = _buffer[_bufferPosition + 1];
                    if (next == quote || (_options.NormalizeQuotes && IsSmartDoubleQuote(next)))
                    {
                        _quotedFieldBuilder.Append(quote);
                        _fieldAccumulator.Append(c);
                        _fieldAccumulator.Append(next);
                        _bufferPosition += 2;
                        quotedLength += 2;
                        CheckQuotedFieldLength(quotedLength);
                        continue;
                    }
                }

                // Check for closing quote
                if (c == quote || (_options.NormalizeQuotes && IsSmartDoubleQuote(c)))
                {
                    int afterQuote = _bufferPosition + 1;

                    // Validate closing quote position - must be followed by delimiter, newline, or EOF
                    if (afterQuote >= _bufferLength)
                    {
                        // Need more data to validate
                        bool hadMoreData = PeekMoreDataWithoutMoving();
                        if (!hadMoreData)
                        {
                            // EOF - this is a valid closing quote
                            _bufferPosition++; // Skip closing quote
                            string value = TryInternString(_quotedFieldBuilder.ToString());
                            _fieldsBuffer.Add(new FieldInfo(value, true));
                            _endOfRecord = true;
                            return;
                        }
                        // There's more data - continue checking
                        afterQuote = _bufferPosition + 1;
                    }

                    if (afterQuote < _bufferLength)
                    {
                        char afterChar = _buffer[afterQuote];

                        // Valid close: followed by delimiter
                        if ((_singleCharDelimiter && afterChar == _delimiterFirstChar) ||
                            (!_singleCharDelimiter && afterChar == _delimiterFirstChar && MatchesDelimiterAt(afterQuote)))
                        {
                            _bufferPosition++; // Skip closing quote
                            _bufferPosition += _singleCharDelimiter ? 1 : _options.Delimiter.Length; // Skip delimiter
                            string value = TryInternString(_quotedFieldBuilder.ToString());
                            _fieldsBuffer.Add(new FieldInfo(value, true));
                            return;
                        }

                        // Valid close: followed by newline
                        if (afterChar == '\r' || afterChar == '\n')
                        {
                            _bufferPosition++; // Skip closing quote
                            SkipNewline();
                            string value = TryInternString(_quotedFieldBuilder.ToString());
                            _fieldsBuffer.Add(new FieldInfo(value, true));
                            _endOfRecord = true;
                            return;
                        }

                        // Valid close: followed by whitespace then delimiter/newline
                        int checkPos = afterQuote;
                        while (checkPos < _bufferLength && char.IsWhiteSpace(_buffer[checkPos]) &&
                               _buffer[checkPos] != '\r' && _buffer[checkPos] != '\n')
                        {
                            checkPos++;
                        }

                        if (checkPos < _bufferLength)
                        {
                            char checkChar = _buffer[checkPos];
                            if ((_singleCharDelimiter && checkChar == _delimiterFirstChar) ||
                                checkChar == '\r' || checkChar == '\n')
                            {
                                _bufferPosition = checkPos;
                                if (checkChar == '\r' || checkChar == '\n')
                                {
                                    SkipNewline();
                                    _endOfRecord = true;
                                }
                                else
                                {
                                    _bufferPosition += _singleCharDelimiter ? 1 : _options.Delimiter.Length;
                                }
                                string value = TryInternString(_quotedFieldBuilder.ToString());
                                _fieldsBuffer.Add(new FieldInfo(value, true));
                                return;
                            }
                        }

                        // Not a valid closing position - treat quote as literal and include it
                        _quotedFieldBuilder.Append(c);
                        _fieldAccumulator.Append(c);
                        _bufferPosition++;
                        quotedLength++;
                        CheckQuotedFieldLength(quotedLength);
                        continue;
                    }
                }

                // Handle newline - if we reach newline without valid closing quote, return raw content
                if (c == '\r' || c == '\n')
                {
                    string value = TryInternString(_fieldAccumulator.ToString());
                    _fieldsBuffer.Add(new FieldInfo(value, false));
                    SkipNewline();
                    _endOfRecord = true;
                    return;
                }

                // Handle smart quote normalization for single quotes
                char normalized = c;
                if (_options.NormalizeQuotes && (c == LeftSingleQuote || c == RightSingleQuote))
                {
                    normalized = '\'';
                }

                _quotedFieldBuilder.Append(normalized);
                _fieldAccumulator.Append(c);
                _bufferPosition++;
                quotedLength++;
                CheckQuotedFieldLength(quotedLength);
            }
        }

        /// <summary>
        /// Reads an unquoted field starting from the current position (used for lenient mode fallback).
        /// </summary>
        private void ReadUnquotedFieldFromCurrentPosition()
        {
            _fieldAccumulator.Clear();

            while (true)
            {
                while (_bufferPosition < _bufferLength)
                {
                    char c = _buffer[_bufferPosition];

                    if (_singleCharDelimiter && c == _delimiterFirstChar)
                    {
                        string value = TryInternString(_fieldAccumulator.ToString());
                        _fieldsBuffer.Add(new FieldInfo(value, false));
                        _bufferPosition++;
                        return;
                    }

                    if (!_singleCharDelimiter && c == _delimiterFirstChar && MatchesDelimiterAtPosition())
                    {
                        string value = TryInternString(_fieldAccumulator.ToString());
                        _fieldsBuffer.Add(new FieldInfo(value, false));
                        _bufferPosition += _options.Delimiter.Length;
                        return;
                    }

                    if (c == '\r' || c == '\n')
                    {
                        string value = TryInternString(_fieldAccumulator.ToString());
                        _fieldsBuffer.Add(new FieldInfo(value, false));
                        SkipNewline();
                        _endOfRecord = true;
                        return;
                    }

                    _fieldAccumulator.Append(c);
                    _bufferPosition++;
                }

                if (!RefillBuffer())
                {
                    string value = TryInternString(_fieldAccumulator.ToString());
                    _fieldsBuffer.Add(new FieldInfo(value, false));
                    _endOfRecord = true;
                    return;
                }
            }
        }

        /// <summary>
        /// Checks if the delimiter matches at the specified position.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool MatchesDelimiterAt(int position)
        {
            string delimiter = _options.Delimiter;
            int delimLength = delimiter.Length;
            if (position + delimLength > _bufferLength)
                return false;

            // Use Span.SequenceEqual for vectorized comparison on multi-char delimiters
            ReadOnlySpan<char> bufferSlice = _buffer.AsSpan(position, delimLength);
            return bufferSlice.SequenceEqual(delimiter.AsSpan());
        }

        /// <summary>
        /// Attempts to peek more data into the buffer without consuming it.
        /// </summary>
        private bool PeekMoreData()
        {
            if (_endOfStream)
                return false;

            int remaining = _bufferLength - _bufferPosition;
            if (remaining > 0)
            {
                // Move remaining data to start of buffer
                Array.Copy(_buffer, _bufferPosition, _buffer, 0, remaining);
            }

            int read = _reader.Read(_buffer, remaining, _buffer.Length - remaining);
            _bufferLength = remaining + read;
            _bufferPosition = 0;

            if (read == 0)
            {
                _endOfStream = true;
                return _bufferLength > 0;
            }

            return true;
        }

        /// <summary>
        /// Checks if there's more data available without moving buffer contents.
        /// Returns true if more data was read, false if at EOF.
        /// </summary>
        private bool PeekMoreDataWithoutMoving()
        {
            if (_endOfStream)
                return false;

            // If there's room in the buffer, try to read more
            if (_bufferLength < _buffer.Length)
            {
                int read = _reader.Read(_buffer, _bufferLength, _buffer.Length - _bufferLength);
                _bufferLength += read;

                if (read == 0)
                {
                    _endOfStream = true;
                    return false;
                }

                return true;
            }

            // Buffer is full - need to compact and read
            int remaining = _bufferLength - _bufferPosition;
            if (remaining > 0)
            {
                Array.Copy(_buffer, _bufferPosition, _buffer, 0, remaining);
            }

            int newRead = _reader.Read(_buffer, remaining, _buffer.Length - remaining);
            _bufferLength = remaining + newRead;
            _bufferPosition = 0;

            if (newRead == 0)
            {
                _endOfStream = true;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Skips whitespace and delimiter after a quoted field.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SkipAfterQuotedField()
        {
            // Skip any whitespace between closing quote and delimiter
            while (_bufferPosition < _bufferLength)
            {
                char c = _buffer[_bufferPosition];

                if (c == '\r' || c == '\n')
                {
                    SkipNewline();
                    _endOfRecord = true;
                    return;
                }

                if (_singleCharDelimiter && c == _delimiterFirstChar)
                {
                    _bufferPosition++;
                    return;
                }

                if (!_singleCharDelimiter && c == _delimiterFirstChar && MatchesDelimiterAtPosition())
                {
                    _bufferPosition += _options.Delimiter.Length;
                    return;
                }

                // Skip whitespace between quote and delimiter (lenient)
                if (char.IsWhiteSpace(c))
                {
                    _bufferPosition++;
                    continue;
                }

                // Unexpected character - in strict mode this would be an error
                // For now, just stop here
                return;
            }

            // End of buffer - try to refill
            if (RefillBuffer())
            {
                SkipAfterQuotedField();
            }
            else
            {
                _endOfRecord = true;
            }
        }

        /// <summary>
        /// Creates a string from a range in the buffer, with optional interning.
        /// Optimized for the common case of non-interned strings.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private string CreateFieldString(int start, int length)
        {
            if (length == 0)
                return string.Empty;

            // Fast path: no interning or string too long for intern table
            if (_internedStrings == null || length > 10)
            {
                return new string(_buffer, start, length);
            }

            // Check intern table for short strings
            string s = new string(_buffer, start, length);
            if (_internedStrings.TryGetValue(s, out string interned))
                return interned;
            return s;
        }

        /// <summary>
        /// Checks if the delimiter matches at the current buffer position.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool MatchesDelimiterAtPosition()
        {
            string delimiter = _options.Delimiter;
            int delimLength = delimiter.Length;
            if (_bufferPosition + delimLength > _bufferLength)
                return false;

            // Use Span.SequenceEqual for vectorized comparison on multi-char delimiters
            ReadOnlySpan<char> bufferSlice = _buffer.AsSpan(_bufferPosition, delimLength);
            return bufferSlice.SequenceEqual(delimiter.AsSpan());
        }

        /// <summary>
        /// Skips newline characters (handles \r, \n, and \r\n).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SkipNewline()
        {
            if (_bufferPosition >= _bufferLength)
                return;

            char c = _buffer[_bufferPosition];
            if (c == '\r')
            {
                _bufferPosition++;
                // Check for \r\n
                if (_bufferPosition < _bufferLength && _buffer[_bufferPosition] == '\n')
                {
                    _bufferPosition++;
                }
                else if (_bufferPosition >= _bufferLength)
                {
                    // Need to check across buffer boundary
                    if (RefillBuffer() && _bufferPosition < _bufferLength && _buffer[_bufferPosition] == '\n')
                    {
                        _bufferPosition++;
                    }
                }
            }
            else if (c == '\n')
            {
                _bufferPosition++;
            }
        }

        /// <summary>
        /// Skips to the end of the current line (for comments).
        /// </summary>
        private void SkipToEndOfLine()
        {
            while (_bufferPosition < _bufferLength)
            {
                char c = _buffer[_bufferPosition];
                if (c == '\r' || c == '\n')
                {
                    SkipNewline();
                    return;
                }
                _bufferPosition++;
            }

            // Continue skipping if we hit buffer boundary
            if (RefillBuffer())
            {
                SkipToEndOfLine();
            }
        }

        /// <summary>
        /// Ensures there is data available in the buffer. Returns false if end of stream.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool EnsureBufferData()
        {
            if (_bufferPosition < _bufferLength)
                return true;

            return RefillBuffer();
        }

        /// <summary>
        /// Refills the buffer from the reader.
        /// </summary>
        private bool RefillBuffer()
        {
            if (_endOfStream)
                return false;

            _bufferLength = _reader.Read(_buffer, 0, _buffer.Length);
            _bufferPosition = 0;

            if (_bufferLength == 0)
            {
                _endOfStream = true;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Checks if a character is a smart quote.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsSmartQuote(char c)
        {
            return c == LeftSingleQuote || c == RightSingleQuote ||
                   c == LeftDoubleQuote || c == RightDoubleQuote;
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
                    // In lenient mode, try to parse as quoted field with inline validation
                    // This combines the validation and parsing into a single pass
                    var result = TryParseQuotedFieldLenient(line, start, delimiter, quote, escape);
                    if (result.wasValidQuoted)
                    {
                        return (result.value, true, result.newPosition);
                    }
                    // No valid closing quote found - treat as unquoted field
                    return ParseUnquotedField(line, start, delimiter);
                }
                return ParseQuotedField(line, start, delimiter, quote, escape);
            }

            return ParseUnquotedField(line, start, delimiter);
        }

        /// <summary>
        /// Attempts to parse a quoted field in lenient mode, validating the closing quote in a single pass.
        /// Returns wasValidQuoted=false if no valid closing quote is found.
        /// </summary>
        private (string value, bool wasValidQuoted, int newPosition) TryParseQuotedFieldLenient(
            ReadOnlySpan<char> line, int start, string delimiter, char quote, char escape)
        {
            _quotedFieldBuilder.Clear();
            int i = start + 1; // Skip opening quote

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
                else if (c == '\\' && i + 1 < line.Length && line[i + 1] == quote)
                {
                    _quotedFieldBuilder.Append(quote);
                    i += 2;
                }
                else if (c == quote)
                {
                    // Found a quote - check if it's a valid closing quote
                    int afterQuote = i + 1;

                    // Check if at end of line - valid closing
                    if (afterQuote >= line.Length)
                    {
                        string value = TryInternString(_quotedFieldBuilder.ToString());
                        return (value, true, line.Length + delimiter.Length);
                    }

                    // Check for delimiter immediately after quote
                    if (MatchesDelimiter(line, afterQuote, delimiter))
                    {
                        string value = TryInternString(_quotedFieldBuilder.ToString());
                        return (value, true, afterQuote + delimiter.Length);
                    }

                    // Check for whitespace then delimiter or end
                    int checkPos = afterQuote;
                    while (checkPos < line.Length && char.IsWhiteSpace(line[checkPos]))
                        checkPos++;

                    if (checkPos >= line.Length)
                    {
                        string value = TryInternString(_quotedFieldBuilder.ToString());
                        return (value, true, line.Length + delimiter.Length);
                    }

                    if (MatchesDelimiter(line, checkPos, delimiter))
                    {
                        string value = TryInternString(_quotedFieldBuilder.ToString());
                        return (value, true, checkPos + delimiter.Length);
                    }

                    // Quote is not at a valid position - include it in content and continue looking
                    _quotedFieldBuilder.Append(c);
                    i++;
                }
                else
                {
                    _quotedFieldBuilder.Append(c);
                    i++;
                }
            }

            // No valid closing quote found - return invalid
            return (null, false, 0);
        }

        private (string value, bool wasQuoted, int newPosition) ParseQuotedField(
            ReadOnlySpan<char> line, int start, string delimiter, char quote, char escape)
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
                    }
                    else
                    {
                        // At end of line with no trailing delimiter - add delimiter length to signal end
                        i += delimiter.Length;
                    }

                    string value = TryInternString(_quotedFieldBuilder.ToString());
                    return (value, wasQuoted, i);
                }
                else
                {
                    _quotedFieldBuilder.Append(c);
                    i++;
                }
            }

            // Unclosed quote - return position past end to signal no more fields
            string finalValue = TryInternString(_quotedFieldBuilder.ToString());
            return (finalValue, wasQuoted, line.Length + delimiter.Length);
        }

        private (string value, bool wasQuoted, int newPosition) ParseUnquotedField(
            ReadOnlySpan<char> line, int start, string delimiter)
        {
            int delimiterLength = delimiter.Length;
            ReadOnlySpan<char> remaining = line.Slice(start);

            // Use Span for fast delimiter search when delimiter is single character
            if (delimiterLength == 1)
            {
                char delimChar = delimiter[0];
                int delimIndex = remaining.IndexOf(delimChar);

                if (delimIndex < 0)
                {
                    // No more delimiters - rest of line is the field
                    string value = TryInternString(remaining.ToString());
                    return (value, false, line.Length + delimiterLength);
                }

                string fieldValue = TryInternString(remaining.Slice(0, delimIndex).ToString());
                return (fieldValue, false, start + delimIndex + delimiterLength);
            }

            // Multi-character delimiter - use optimized Span.IndexOf for the first char, then verify
            ReadOnlySpan<char> delimSpan = delimiter.AsSpan();
            char firstDelimChar = delimiter[0];
            int searchStart = 0;

            while (searchStart < remaining.Length)
            {
                // Find next occurrence of first delimiter character
                int firstCharIndex = remaining.Slice(searchStart).IndexOf(firstDelimChar);
                if (firstCharIndex < 0)
                {
                    // No more potential delimiters - rest of line is the field
                    string value = TryInternString(remaining.ToString());
                    return (value, false, line.Length + delimiterLength);
                }

                int candidatePos = searchStart + firstCharIndex;

                // Check if full delimiter matches at this position
                if (candidatePos + delimiterLength <= remaining.Length &&
                    remaining.Slice(candidatePos, delimiterLength).SequenceEqual(delimSpan))
                {
                    string value = TryInternString(remaining.Slice(0, candidatePos).ToString());
                    return (value, false, start + candidatePos + delimiterLength);
                }

                // Not a match, continue searching after this position
                searchStart = candidatePos + 1;
            }

            // No delimiter found - rest of line is the field
            string finalValue = TryInternString(remaining.ToString());
            return (finalValue, false, line.Length + delimiterLength);
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

        // Threshold for stackalloc vs ArrayPool - 512 chars = 1KB on stack
        private const int StackAllocThreshold = 512;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string NormalizeSmartQuotes(string input)
        {
            if (input == null)
                return null;

            ReadOnlySpan<char> inputSpan = input.AsSpan();

            // Fast path: check if any smart quotes exist
            int firstSmartQuoteIndex = -1;
            for (int i = 0; i < inputSpan.Length; i++)
            {
                char c = inputSpan[i];
                if (c == LeftSingleQuote || c == RightSingleQuote ||
                    c == LeftDoubleQuote || c == RightDoubleQuote)
                {
                    firstSmartQuoteIndex = i;
                    break;
                }
            }

            if (firstSmartQuoteIndex < 0)
                return input;

            // Slow path: replace smart quotes using Span
            return inputSpan.Length <= StackAllocThreshold
                ? NormalizeSmartQuotesStackAlloc(inputSpan, firstSmartQuoteIndex)
                : NormalizeSmartQuotesPooled(inputSpan, firstSmartQuoteIndex);
        }

        private static string NormalizeSmartQuotesStackAlloc(ReadOnlySpan<char> input, int firstSmartQuoteIndex)
        {
            Span<char> buffer = stackalloc char[input.Length];

            // Copy prefix that has no smart quotes
            input.Slice(0, firstSmartQuoteIndex).CopyTo(buffer);

            // Process remainder
            int writePos = firstSmartQuoteIndex;
            for (int i = firstSmartQuoteIndex; i < input.Length; i++)
            {
                char c = input[i];
                buffer[writePos++] = NormalizeSmartQuoteChar(c);
            }

            // Use char array constructor for .NET Framework compatibility
            return buffer.Slice(0, writePos).ToString();
        }

        private static string NormalizeSmartQuotesPooled(ReadOnlySpan<char> input, int firstSmartQuoteIndex)
        {
            char[] buffer = ArrayPool<char>.Shared.Rent(input.Length);
            try
            {
                Span<char> bufferSpan = buffer.AsSpan(0, input.Length);

                // Copy prefix that has no smart quotes
                input.Slice(0, firstSmartQuoteIndex).CopyTo(bufferSpan);

                // Process remainder
                int writePos = firstSmartQuoteIndex;
                for (int i = firstSmartQuoteIndex; i < input.Length; i++)
                {
                    char c = input[i];
                    bufferSpan[writePos++] = NormalizeSmartQuoteChar(c);
                }

                return bufferSpan.Slice(0, writePos).ToString();
            }
            finally
            {
                ArrayPool<char>.Shared.Return(buffer);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static char NormalizeSmartQuoteChar(char c)
        {
            if (c == LeftSingleQuote || c == RightSingleQuote)
                return '\'';
            if (c == LeftDoubleQuote || c == RightDoubleQuote)
                return '"';
            return c;
        }

        #endregion

        #region Additional Properties

        /// <summary>
        /// Gets the current record index (zero-based).
        /// </summary>
        /// <remarks>
        /// Thread-safety: Uses atomic read to prevent torn reads on 64-bit values.
        /// </remarks>
        public long CurrentRecordIndex => Interlocked.Read(ref _currentRecordIndex);

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
        /// Gets the options used by this reader.
        /// </summary>
        public CsvReaderOptions Options => _options;

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
                    // Re-cache the converter for this column
                    if (type != typeof(string))
                    {
                        _columns[i].CachedConverter = _columns[i].Converter ?? _options.TypeConverterRegistry?.GetConverter(type);
                        // Invalidate fast path when non-string column type is set
                        _useFastConversion = false;
                        _useFastParsing = false;
                    }
                    else
                    {
                        _columns[i].CachedConverter = null;
                    }
                    return;
                }
            }

            // Also check static columns - they already have a type set, but allow changing it
            for (int i = 0; i < _staticColumns.Count; i++)
            {
                if (string.Equals(_staticColumns[i].Name, columnName, StringComparison.OrdinalIgnoreCase))
                {
                    _staticColumns[i].DataType = type;
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

            var convertedValues = _convertedValues;
            if (convertedValues != null)
            {
                // Resize converted values array
                Array.Resize(ref convertedValues, _columns.Count + _staticColumns.Count);
                _convertedValues = convertedValues;
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

        /// <inheritdoc />
        public int Depth => 0;
        /// <inheritdoc />
        public bool IsClosed => _isClosed;
        /// <inheritdoc />
        public int RecordsAffected => -1;

        /// <inheritdoc />
        public bool NextResult() => false;

        /// <inheritdoc />
        public void Close()
        {
            if (!_isClosed)
            {
                _isClosed = true;

                // Stop parallel pipeline first
                StopParallelPipeline();

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

        /// <inheritdoc />
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
