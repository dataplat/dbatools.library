using System.Management.Automation;
using File = System.IO.File;
using System.IO;
using System.Text;
using System.IO.Compression;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Implements the <c>Import-Command</c> internal command
    /// </summary>
    [Cmdlet("Import", "Command", DefaultParameterSetName = "DefaultParameter", RemotingCapability = RemotingCapability.None)]
    public class ImportCommand : PSCmdlet
    {
        #region Parameters
        /// <summary>
        /// The actual input object that is being processed
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        public string Path;
        #endregion Parameters

        #region Command Implementation
        /// <summary>
        /// Implements the begin action of the command
        /// </summary>
        protected override void BeginProcessing()
        {
        }

        /// <summary>
        /// Implements the process action of the command
        /// </summary>
        protected override void ProcessRecord()
        {
            if (Path.EndsWith("dat")) {
                FileStream fs = File.Open(Path, FileMode.Open);
                var stream = new DeflateStream(fs, CompressionMode.Decompress);
                var sr = new StreamReader(stream, Encoding.UTF8);
                SessionState.InvokeCommand.InvokeScript(false, ScriptBlock.Create(sr.ReadToEnd()), null, null);
                sr.Close();
                stream.Close();
                fs.Close();
                sr.Dispose();
                stream.Dispose();
                fs.Dispose();
            }
            else
            {
                SessionState.InvokeCommand.InvokeScript(false, ScriptBlock.Create(File.ReadAllText(Path)), null, null);
            }

        }

        /// <summary>
        /// Implements the end action of the command
        /// </summary>
        protected override void EndProcessing()
        {
        }
        #endregion Command Implementation
    }
}
/*
 *   private static void CompressFile()
    {
        using FileStream originalFileStream = File.Open(OriginalFileName, FileMode.Open);
        using FileStream compressedFileStream = File.Create(CompressedFileName);
        using var compressor = new GZipStream(compressedFileStream, CompressionMode.Compress);
        originalFileStream.CopyTo(compressor);
    }

    private static void DecompressFile()
    {
        using FileStream compressedFileStream = File.Open(CompressedFileName, FileMode.Open);
        using FileStream outputFileStream = File.Create(DecompressedFileName);
        using var decompressor = new GZipStream(compressedFileStream, CompressionMode.Decompress);
        decompressor.CopyTo(outputFileStream);
    }
*/