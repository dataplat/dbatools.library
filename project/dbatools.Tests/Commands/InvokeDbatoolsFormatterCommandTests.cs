using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class InvokeDbatoolsFormatterCommandTests
    {
        #region ScriptAnalyzerCorrectVersion
        [TestMethod]
        public void ScriptAnalyzerCorrectVersion_IsExpectedVersion()
        {
            // Arrange & Act
            string version = Dataplat.Dbatools.Commands.InvokeDbatoolsFormatterCommand.ScriptAnalyzerCorrectVersion;

            // Assert
            Assert.AreEqual("1.18.2", version);
        }

        [TestMethod]
        public void ScriptAnalyzerCorrectVersion_IsNotNullOrEmpty()
        {
            // Arrange & Act
            string version = Dataplat.Dbatools.Commands.InvokeDbatoolsFormatterCommand.ScriptAnalyzerCorrectVersion;

            // Assert
            Assert.IsFalse(String.IsNullOrEmpty(version), "ScriptAnalyzerCorrectVersion must not be null or empty");
        }
        #endregion

        #region StripTrailingWhitespace
        [TestMethod]
        public void StripTrailingWhitespace_RemovesTrailingNewlinesWindows()
        {
            // Arrange
            string content = "function Get-Foo {\r\n}\r\n\r\n   \r\n";
            string eol = "\r\n";

            // Act
            string result = Dataplat.Dbatools.Commands.InvokeDbatoolsFormatterCommand.StripTrailingWhitespace(content, eol);

            // Assert
            Assert.AreEqual("function Get-Foo {\r\n}", result);
        }

        [TestMethod]
        public void StripTrailingWhitespace_RemovesTrailingNewlinesUnix()
        {
            // Arrange
            string content = "function Get-Foo {\n}\n\n   \n";
            string eol = "\n";

            // Act
            string result = Dataplat.Dbatools.Commands.InvokeDbatoolsFormatterCommand.StripTrailingWhitespace(content, eol);

            // Assert
            Assert.AreEqual("function Get-Foo {\n}", result);
        }

        [TestMethod]
        public void StripTrailingWhitespace_NoTrailingWhitespace_ReturnsUnchanged()
        {
            // Arrange
            string content = "function Get-Foo { }";
            string eol = "\n";

            // Act
            string result = Dataplat.Dbatools.Commands.InvokeDbatoolsFormatterCommand.StripTrailingWhitespace(content, eol);

            // Assert
            Assert.AreEqual(content, result);
        }

        [TestMethod]
        public void StripTrailingWhitespace_EmptyString_ReturnsEmpty()
        {
            // Arrange & Act
            string result = Dataplat.Dbatools.Commands.InvokeDbatoolsFormatterCommand.StripTrailingWhitespace("", "\n");

            // Assert
            Assert.AreEqual("", result);
        }
        #endregion

        #region FixCbhIndentation
        [TestMethod]
        public void FixCbhIndentation_FixesMismatchedIndentation()
        {
            // Arrange - CBH where start has 4 spaces but end has 0 spaces
            string content = "function Get-Foo {\n    <#\n    .SYNOPSIS\n        A stub\n#>\n}";

            // Act
            string result = Dataplat.Dbatools.Commands.InvokeDbatoolsFormatterCommand.FixCbhIndentation(content);

            // Assert - end tag should now have 4 spaces like the start
            Assert.IsTrue(result.Contains("    #>"), "Closing #> should be indented to match opening <#");
            Assert.IsFalse(result.Contains("\n#>"), "Original unindented #> should be replaced");
        }

        [TestMethod]
        public void FixCbhIndentation_AlreadyMatched_ReturnsUnchanged()
        {
            // Arrange - CBH where start and end both have 4 spaces
            string content = "function Get-Foo {\n    <#\n    .SYNOPSIS\n        A stub\n    #>\n}";

            // Act
            string result = Dataplat.Dbatools.Commands.InvokeDbatoolsFormatterCommand.FixCbhIndentation(content);

            // Assert
            Assert.AreEqual(content, result);
        }

        [TestMethod]
        public void FixCbhIndentation_NoCbh_ReturnsUnchanged()
        {
            // Arrange - no comment-based help at all
            string content = "function Get-Foo { Write-Output 'hello' }";

            // Act
            string result = Dataplat.Dbatools.Commands.InvokeDbatoolsFormatterCommand.FixCbhIndentation(content);

            // Assert
            Assert.AreEqual(content, result);
        }

        [TestMethod]
        public void FixCbhIndentation_DeepIndentation_MatchesStartSpaces()
        {
            // Arrange - CBH with 8-space indent at start, 2-space at end
            string content = "        <#\n        .SYNOPSIS\n            Test\n  #>\n";

            // Act
            string result = Dataplat.Dbatools.Commands.InvokeDbatoolsFormatterCommand.FixCbhIndentation(content);

            // Assert
            Assert.IsTrue(result.Contains("        #>"), "Closing #> should match the 8-space start indent");
        }
        #endregion

        // Note: ProcessRecord behavior (invoking Invoke-Formatter, reading/writing files,
        // and resolving paths) requires a PSCmdlet runtime and PSScriptAnalyzer module,
        // so cannot be unit tested without a live PowerShell session.
    }
}
