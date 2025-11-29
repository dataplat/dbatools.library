# Changelog

All notable changes to Dataplat.Dbatools.Csv will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Null and empty string validation for `Delimiter` property to prevent runtime errors
- Decompression bomb protection tests verifying `LimitedReadStream` security feature

### Changed
- Build script now uses XML parsing for version updates instead of regex (safer for edge cases)

## [1.0.0] - 2024-11-28

### Added
- High-performance CSV reader implementing `IDataReader` for seamless `SqlBulkCopy` integration
- High-performance CSV writer with streaming support
- Multi-character delimiter support (e.g., `::`, `||`, `\t`)
- Automatic compression detection and handling (GZip, Deflate, Brotli, ZLib)
- Decompression bomb protection via configurable `MaxDecompressedSize` limit (default 10GB)
- Parallel processing pipeline for 2-4x performance on large files
- String interning for reduced memory allocations on repeated values
- Culture-aware type conversion with customizable converters
- Configurable error handling: throw, skip, or collect parse errors
- Quote handling modes: Strict (RFC 4180) and Lenient for malformed data
- Mismatched field count handling: throw, pad with nulls, or truncate
- Duplicate header handling options
- Static column injection for adding computed values to each record
- Column filtering (include/exclude)
- `DistinguishEmptyFromNull` option for precise null vs empty string semantics
- Smart/curly quote normalization
- Skip rows feature for files with preamble content
- SourceLink support for debugging NuGet packages

### Performance
- 20%+ faster than LumenWorks CsvReader in benchmarks
- 64KB default buffer size (vs 4KB in LumenWorks)
- Span-based parsing with `ArrayPool<T>` for minimal allocations
- SIMD-optimized delimiter matching via `Span.SequenceEqual`
- Zero-copy direct field parsing where possible
