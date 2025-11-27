using System;
using System.IO;
using System.IO.Compression;

namespace Dataplat.Dbatools.Csv.Compression
{
    /// <summary>
    /// Helper class for detecting and handling compressed streams.
    /// Addresses issues #8554 and #8646 for compression support.
    /// </summary>
    public static class CompressionHelper
    {
        // Magic bytes for compression detection
        private static readonly byte[] GZipMagic = new byte[] { 0x1F, 0x8B };
        private static readonly byte[] DeflateZlibMagic = new byte[] { 0x78 }; // 0x78 0x01, 0x78 0x5E, 0x78 0x9C, 0x78 0xDA

        /// <summary>
        /// Detects the compression type from a file path based on extension.
        /// </summary>
        public static CompressionType DetectFromExtension(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return CompressionType.None;

            string ext = Path.GetExtension(filePath).ToLowerInvariant();

            switch (ext)
            {
                case ".gz":
                case ".gzip":
                    return CompressionType.GZip;

                case ".deflate":
                    return CompressionType.Deflate;

#if NET8_0_OR_GREATER
                case ".br":
                case ".brotli":
                    return CompressionType.Brotli;

                case ".zlib":
                case ".zl":
                    return CompressionType.ZLib;
#endif
                default:
                    return CompressionType.None;
            }
        }

        /// <summary>
        /// Detects the compression type from the stream's magic bytes.
        /// Note: The stream must be seekable for this to work.
        /// </summary>
        public static CompressionType DetectFromStream(Stream stream)
        {
            if (stream == null || !stream.CanRead)
                return CompressionType.None;

            if (!stream.CanSeek)
                return CompressionType.None;

            long originalPosition = stream.Position;
            try
            {
                byte[] header = new byte[2];
                int bytesRead = stream.Read(header, 0, 2);

                if (bytesRead < 2)
                    return CompressionType.None;

                // Check GZip magic
                if (header[0] == GZipMagic[0] && header[1] == GZipMagic[1])
                    return CompressionType.GZip;

                // Check for ZLib/Deflate (various compression levels)
                if (header[0] == 0x78 && (header[1] == 0x01 || header[1] == 0x5E || header[1] == 0x9C || header[1] == 0xDA))
                {
#if NET8_0_OR_GREATER
                    return CompressionType.ZLib;
#else
                    return CompressionType.Deflate;
#endif
                }

                return CompressionType.None;
            }
            finally
            {
                stream.Position = originalPosition;
            }
        }

        /// <summary>
        /// Wraps a stream with the appropriate decompression stream.
        /// </summary>
        public static Stream WrapForDecompression(Stream stream, CompressionType compressionType)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            switch (compressionType)
            {
                case CompressionType.None:
                    return stream;

                case CompressionType.GZip:
                    return new GZipStream(stream, CompressionMode.Decompress, leaveOpen: false);

                case CompressionType.Deflate:
                    return new DeflateStream(stream, CompressionMode.Decompress, leaveOpen: false);

#if NET8_0_OR_GREATER
                case CompressionType.Brotli:
                    return new BrotliStream(stream, CompressionMode.Decompress, leaveOpen: false);

                case CompressionType.ZLib:
                    return new ZLibStream(stream, CompressionMode.Decompress, leaveOpen: false);
#endif
                default:
                    throw new ArgumentException($"Unknown compression type: {compressionType}", nameof(compressionType));
            }
        }

        /// <summary>
        /// Wraps a stream with the appropriate compression stream.
        /// </summary>
        public static Stream WrapForCompression(Stream stream, CompressionType compressionType, CompressionLevel level = CompressionLevel.Optimal)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            switch (compressionType)
            {
                case CompressionType.None:
                    return stream;

                case CompressionType.GZip:
                    return new GZipStream(stream, level, leaveOpen: false);

                case CompressionType.Deflate:
                    return new DeflateStream(stream, level, leaveOpen: false);

#if NET8_0_OR_GREATER
                case CompressionType.Brotli:
                    return new BrotliStream(stream, level, leaveOpen: false);

                case CompressionType.ZLib:
                    return new ZLibStream(stream, level, leaveOpen: false);
#endif
                default:
                    throw new ArgumentException($"Unknown compression type: {compressionType}", nameof(compressionType));
            }
        }

        /// <summary>
        /// Gets the recommended file extension for a compression type.
        /// </summary>
        public static string GetFileExtension(CompressionType compressionType)
        {
            switch (compressionType)
            {
                case CompressionType.None:
                    return "";
                case CompressionType.GZip:
                    return ".gz";
                case CompressionType.Deflate:
                    return ".deflate";
#if NET8_0_OR_GREATER
                case CompressionType.Brotli:
                    return ".br";
                case CompressionType.ZLib:
                    return ".zlib";
#endif
                default:
                    return "";
            }
        }

        /// <summary>
        /// Opens a file stream with automatic decompression detection.
        /// </summary>
        public static Stream OpenFileForReading(string filePath, bool autoDetect = true)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            Stream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 65536, FileOptions.SequentialScan);

            CompressionType compressionType;

            if (autoDetect)
            {
                // First try extension
                compressionType = DetectFromExtension(filePath);

                // If no extension match, try magic bytes
                if (compressionType == CompressionType.None)
                {
                    compressionType = DetectFromStream(fileStream);
                }
            }
            else
            {
                compressionType = DetectFromExtension(filePath);
            }

            return WrapForDecompression(fileStream, compressionType);
        }

        /// <summary>
        /// Opens a file stream for writing with the specified compression.
        /// </summary>
        public static Stream OpenFileForWriting(string filePath, CompressionType compressionType, CompressionLevel level = CompressionLevel.Optimal)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            Stream fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 65536, FileOptions.SequentialScan);

            return WrapForCompression(fileStream, compressionType, level);
        }
    }
}
