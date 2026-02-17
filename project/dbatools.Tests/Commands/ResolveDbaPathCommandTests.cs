using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class ResolveDbaPathCommandTests
    {
        // Note: limited unit test coverage - command is primarily a PowerShell provider wrapper.
        // Core logic (Resolve-Path, Split-Path, Join-Path, Get-Location) runs through
        // SessionState.Path and InvokeCommand which require a live PowerShell session.

        #region IsDotPath
        [TestMethod]
        public void IsDotPath_DotString_ReturnsTrue()
        {
            Assert.IsTrue(ResolveDbaPathCommand.IsDotPath("."));
        }

        [TestMethod]
        public void IsDotPath_DoubleDot_ReturnsFalse()
        {
            Assert.IsFalse(ResolveDbaPathCommand.IsDotPath(".."));
        }

        [TestMethod]
        public void IsDotPath_EmptyString_ReturnsFalse()
        {
            Assert.IsFalse(ResolveDbaPathCommand.IsDotPath(""));
        }

        [TestMethod]
        public void IsDotPath_RegularPath_ReturnsFalse()
        {
            Assert.IsFalse(ResolveDbaPathCommand.IsDotPath("C:\\temp"));
        }

        [TestMethod]
        public void IsDotPath_DotSlashPath_ReturnsFalse()
        {
            Assert.IsFalse(ResolveDbaPathCommand.IsDotPath(".\\file.txt"));
        }

        [TestMethod]
        public void IsDotPath_Null_ReturnsFalse()
        {
            Assert.IsFalse(ResolveDbaPathCommand.IsDotPath(null));
        }
        #endregion

        #region IsProviderMatch
        [TestMethod]
        public void IsProviderMatch_ExactMatch_ReturnsTrue()
        {
            Assert.IsTrue(ResolveDbaPathCommand.IsProviderMatch("FileSystem", "FileSystem"));
        }

        [TestMethod]
        public void IsProviderMatch_CaseInsensitive_ReturnsTrue()
        {
            Assert.IsTrue(ResolveDbaPathCommand.IsProviderMatch("FileSystem", "filesystem"));
        }

        [TestMethod]
        public void IsProviderMatch_Mismatch_ReturnsFalse()
        {
            Assert.IsFalse(ResolveDbaPathCommand.IsProviderMatch("Registry", "FileSystem"));
        }

        [TestMethod]
        public void IsProviderMatch_NullResolved_ReturnsFalse()
        {
            Assert.IsFalse(ResolveDbaPathCommand.IsProviderMatch(null, "FileSystem"));
        }

        [TestMethod]
        public void IsProviderMatch_BothNull_ReturnsTrue()
        {
            Assert.IsTrue(ResolveDbaPathCommand.IsProviderMatch(null, null));
        }

        [TestMethod]
        public void IsProviderMatch_RegistryProvider_ReturnsTrue()
        {
            Assert.IsTrue(ResolveDbaPathCommand.IsProviderMatch("Registry", "Registry"));
        }
        #endregion

        #region FormatProviderMismatchMessage
        [TestMethod]
        public void FormatProviderMismatchMessage_FormatsCorrectly()
        {
            string result = ResolveDbaPathCommand.FormatProviderMismatchMessage("Registry", "FileSystem");
            Assert.AreEqual("Resolved provider is Registry when it should be FileSystem", result);
        }

        [TestMethod]
        public void FormatProviderMismatchMessage_IncludesBothNames()
        {
            string result = ResolveDbaPathCommand.FormatProviderMismatchMessage("Certificate", "FileSystem");
            Assert.IsTrue(result.Contains("Certificate"));
            Assert.IsTrue(result.Contains("FileSystem"));
        }

        [TestMethod]
        public void FormatProviderMismatchMessage_StartsWithResolvedProvider()
        {
            string result = ResolveDbaPathCommand.FormatProviderMismatchMessage("Variable", "FileSystem");
            Assert.IsTrue(result.StartsWith("Resolved provider is Variable"));
        }
        #endregion
    }
}
