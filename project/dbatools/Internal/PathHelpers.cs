using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Dataplat.Dbatools.Internal
{
    /// <summary>
    /// Static path manipulation helpers mirroring PS1 functions like
    /// Get-DbaPathSep, Join-SomePath, Join-AdminUnc, Remove-InvalidFileNameChars.
    /// </summary>
    public static class PathHelpers
    {
        /// <summary>
        /// Gets the path separator. Defaults to backslash on Windows.
        /// Mirrors Get-DbaPathSep.ps1.
        /// </summary>
        /// <param name="pathSeparator">Optional path separator from SMO server. If null/empty, returns default.</param>
        /// <returns>The path separator character as string</returns>
        public static string GetPathSeparator(string pathSeparator = null)
        {
            if (!String.IsNullOrEmpty(pathSeparator))
                return pathSeparator;
            return Path.DirectorySeparatorChar.ToString();
        }

        /// <summary>
        /// Joins two path segments. Does NOT require paths to exist.
        /// Mirrors Join-SomePath.ps1 using IO.Path.Combine.
        /// </summary>
        public static string JoinPath(string path, string childPath)
        {
            if (String.IsNullOrEmpty(path))
                return childPath;
            if (String.IsNullOrEmpty(childPath))
                return path;
            return Path.Combine(path, childPath);
        }

        /// <summary>
        /// Converts a local file path to a UNC admin share path.
        /// Mirrors Join-AdminUnc.ps1.
        /// Example: servername="sql01\instance", filepath="C:\backup" -> "\\sql01\C$\backup"
        /// </summary>
        /// <param name="serverName">Server name (may contain instance name)</param>
        /// <param name="filePath">Local file path</param>
        /// <returns>UNC admin share path, or original path if conversion not possible</returns>
        public static string JoinAdminUnc(string serverName, string filePath)
        {
            if (String.IsNullOrEmpty(filePath))
                return filePath;

            // Already UNC
            if (filePath.StartsWith(@"\\"))
                return filePath;

            // Non-Windows: return as-is
            if (!FlowControl.TestWindows())
                return filePath;

            if (String.IsNullOrEmpty(serverName))
                return filePath;

            // Extract hostname (before backslash if instance name present)
            string hostName = serverName;
            int backslashIndex = serverName.IndexOf('\\');
            if (backslashIndex >= 0)
                hostName = serverName.Substring(0, backslashIndex);

            // Replace colon with dollar sign for admin share
            string uncPath = filePath.Replace(":", "$");
            return String.Format(@"\\{0}\{1}", hostName, uncPath);
        }

        /// <summary>
        /// Removes invalid filename characters from a string.
        /// Mirrors Remove-InvalidFileNameChars.ps1.
        /// </summary>
        public static string SanitizeFileName(string name)
        {
            if (String.IsNullOrEmpty(name))
                return name;

            char[] invalidChars = Path.GetInvalidFileNameChars();
            string pattern = "[" + Regex.Escape(new string(invalidChars)) + "]";
            return Regex.Replace(name, pattern, "");
        }
    }
}
