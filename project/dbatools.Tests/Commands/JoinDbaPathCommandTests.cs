using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class JoinDbaPathCommandTests
    {
        #region JoinWithLocalSeparator
        [TestMethod]
        public void JoinWithLocalSeparator_BasePathOnly_ReturnsNormalized()
        {
            char sep = Path.DirectorySeparatorChar;
            string result = JoinDbaPathCommand.JoinWithLocalSeparator("C:\\temp", null);

            // All separators should be normalized to the local OS separator
            string expected = "C:" + sep + "temp";
            Assert.AreEqual(expected, result);
        }

        [TestMethod]
        public void JoinWithLocalSeparator_WithChildren_JoinsAllSegments()
        {
            char sep = Path.DirectorySeparatorChar;
            string result = JoinDbaPathCommand.JoinWithLocalSeparator(
                "C:\\temp",
                new string[] { "Foo", "Bar" });

            string expected = "C:" + sep + "temp" + sep + "Foo" + sep + "Bar";
            Assert.AreEqual(expected, result);
        }

        [TestMethod]
        public void JoinWithLocalSeparator_EmptyChildren_ReturnBasePath()
        {
            char sep = Path.DirectorySeparatorChar;
            string result = JoinDbaPathCommand.JoinWithLocalSeparator("C:\\temp", new string[0]);

            string expected = "C:" + sep + "temp";
            Assert.AreEqual(expected, result);
        }

        [TestMethod]
        public void JoinWithLocalSeparator_ForwardSlashBase_NormalizesToLocalSep()
        {
            char sep = Path.DirectorySeparatorChar;
            string result = JoinDbaPathCommand.JoinWithLocalSeparator(
                "/var/opt/mssql",
                new string[] { "data" });

            string expected = sep + "var" + sep + "opt" + sep + "mssql" + sep + "data";
            Assert.AreEqual(expected, result);
        }

        [TestMethod]
        public void JoinWithLocalSeparator_MixedSeparators_NormalizesAll()
        {
            char sep = Path.DirectorySeparatorChar;
            string result = JoinDbaPathCommand.JoinWithLocalSeparator(
                "C:\\temp/mixed",
                new string[] { "sub" });

            string expected = "C:" + sep + "temp" + sep + "mixed" + sep + "sub";
            Assert.AreEqual(expected, result);
        }

        [TestMethod]
        public void JoinWithLocalSeparator_NullChildren_ReturnBasePath()
        {
            char sep = Path.DirectorySeparatorChar;
            string result = JoinDbaPathCommand.JoinWithLocalSeparator("C:\\temp\\foo", null);

            string expected = "C:" + sep + "temp" + sep + "foo";
            Assert.AreEqual(expected, result);
        }

        [TestMethod]
        public void JoinWithLocalSeparator_SingleChild_JoinsCorrectly()
        {
            char sep = Path.DirectorySeparatorChar;
            string result = JoinDbaPathCommand.JoinWithLocalSeparator(
                "D:\\backup",
                new string[] { "MyDatabase" });

            string expected = "D:" + sep + "backup" + sep + "MyDatabase";
            Assert.AreEqual(expected, result);
        }

        [TestMethod]
        public void JoinWithLocalSeparator_MultipleChildren_JoinsAll()
        {
            char sep = Path.DirectorySeparatorChar;
            string result = JoinDbaPathCommand.JoinWithLocalSeparator(
                "C:\\SQL",
                new string[] { "Backup", "Full", "2024-01-15" });

            string expected = "C:" + sep + "SQL" + sep + "Backup" + sep + "Full" + sep + "2024-01-15";
            Assert.AreEqual(expected, result);
        }
        [TestMethod]
        public void JoinWithLocalSeparator_EmptyBasePath_ReturnsChildrenOnly()
        {
            char sep = Path.DirectorySeparatorChar;
            string result = JoinDbaPathCommand.JoinWithLocalSeparator(
                "",
                new string[] { "Foo", "Bar" });

            string expected = sep + "Foo" + sep + "Bar";
            Assert.AreEqual(expected, result);
        }

        [TestMethod]
        public void JoinWithLocalSeparator_ChildWithSeparators_NormalizesChildSeparators()
        {
            char sep = Path.DirectorySeparatorChar;
            string result = JoinDbaPathCommand.JoinWithLocalSeparator(
                "C:\\temp",
                new string[] { "sub/dir" });

            // Child segments containing separators should also be normalized
            string expected = "C:" + sep + "temp" + sep + "sub" + sep + "dir";
            Assert.AreEqual(expected, result);
        }

        [TestMethod]
        public void JoinWithLocalSeparator_TrailingSeparatorOnBase_HandledCorrectly()
        {
            char sep = Path.DirectorySeparatorChar;
            string result = JoinDbaPathCommand.JoinWithLocalSeparator(
                "C:\\temp\\",
                new string[] { "sub" });

            // The trailing separator + join separator results in double sep, both normalized
            string expected = "C:" + sep + "temp" + sep + sep + "sub";
            Assert.AreEqual(expected, result);
        }
        #endregion
    }
}