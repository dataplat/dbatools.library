using System;
using System.IO;
using Dataplat.Dbatools.Internal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Tests.Internal
{
    [TestClass]
    public class PathHelpersTests
    {
        #region GetPathSeparator
        [TestMethod]
        public void GetPathSeparator_NullInput_ReturnsDefault()
        {
            string result = PathHelpers.GetPathSeparator(null);
            Assert.AreEqual(Path.DirectorySeparatorChar.ToString(), result);
        }

        [TestMethod]
        public void GetPathSeparator_EmptyInput_ReturnsDefault()
        {
            string result = PathHelpers.GetPathSeparator("");
            Assert.AreEqual(Path.DirectorySeparatorChar.ToString(), result);
        }

        [TestMethod]
        public void GetPathSeparator_CustomSeparator_ReturnsIt()
        {
            Assert.AreEqual("/", PathHelpers.GetPathSeparator("/"));
        }

        [TestMethod]
        public void GetPathSeparator_BackslashSeparator_ReturnsIt()
        {
            Assert.AreEqual("\\", PathHelpers.GetPathSeparator("\\"));
        }
        #endregion

        #region JoinPath
        [TestMethod]
        public void JoinPath_BothSegments_CombinesThem()
        {
            string result = PathHelpers.JoinPath("C:\\backup", "mydb.bak");
            Assert.AreEqual(Path.Combine("C:\\backup", "mydb.bak"), result);
        }

        [TestMethod]
        public void JoinPath_NullParent_ReturnsChild()
        {
            Assert.AreEqual("file.txt", PathHelpers.JoinPath(null, "file.txt"));
        }

        [TestMethod]
        public void JoinPath_EmptyParent_ReturnsChild()
        {
            Assert.AreEqual("file.txt", PathHelpers.JoinPath("", "file.txt"));
        }

        [TestMethod]
        public void JoinPath_NullChild_ReturnsParent()
        {
            Assert.AreEqual("C:\\backup", PathHelpers.JoinPath("C:\\backup", null));
        }

        [TestMethod]
        public void JoinPath_EmptyChild_ReturnsParent()
        {
            Assert.AreEqual("C:\\backup", PathHelpers.JoinPath("C:\\backup", ""));
        }
        #endregion

        #region JoinAdminUnc
        [TestMethod]
        public void JoinAdminUnc_LocalPath_ConvertsToUnc()
        {
            if (!FlowControl.TestWindows())
            {
                Assert.Inconclusive("JoinAdminUnc UNC conversion only works on Windows");
                return;
            }
            string result = PathHelpers.JoinAdminUnc("sql01", "C:\\backup");
            Assert.AreEqual("\\\\sql01\\C$\\backup", result);
        }

        [TestMethod]
        public void JoinAdminUnc_InstanceName_ExtractsHostname()
        {
            if (!FlowControl.TestWindows())
            {
                Assert.Inconclusive("JoinAdminUnc UNC conversion only works on Windows");
                return;
            }
            string result = PathHelpers.JoinAdminUnc("sql01\\INST01", "D:\\data\\mydb.mdf");
            Assert.AreEqual("\\\\sql01\\D$\\data\\mydb.mdf", result);
        }

        [TestMethod]
        public void JoinAdminUnc_AlreadyUnc_ReturnsAsIs()
        {
            string unc = "\\\\server\\share\\file.bak";
            Assert.AreEqual(unc, PathHelpers.JoinAdminUnc("sql01", unc));
        }

        [TestMethod]
        public void JoinAdminUnc_NullFilePath_ReturnsNull()
        {
            Assert.IsNull(PathHelpers.JoinAdminUnc("sql01", null));
        }

        [TestMethod]
        public void JoinAdminUnc_EmptyFilePath_ReturnsEmpty()
        {
            Assert.AreEqual("", PathHelpers.JoinAdminUnc("sql01", ""));
        }

        [TestMethod]
        public void JoinAdminUnc_NullServer_ReturnsOriginalPath()
        {
            Assert.AreEqual("C:\\backup", PathHelpers.JoinAdminUnc(null, "C:\\backup"));
        }
        #endregion

        #region SanitizeFileName
        [TestMethod]
        public void SanitizeFileName_NullInput_ReturnsNull()
        {
            Assert.IsNull(PathHelpers.SanitizeFileName(null));
        }

        [TestMethod]
        public void SanitizeFileName_EmptyInput_ReturnsEmpty()
        {
            Assert.AreEqual("", PathHelpers.SanitizeFileName(""));
        }

        [TestMethod]
        public void SanitizeFileName_ValidName_ReturnsUnchanged()
        {
            Assert.AreEqual("myfile.txt", PathHelpers.SanitizeFileName("myfile.txt"));
        }

        [TestMethod]
        public void SanitizeFileName_InvalidChars_RemovesThem()
        {
            string result = PathHelpers.SanitizeFileName("my<file>:name.txt");
            Assert.AreEqual("myfilename.txt", result);
        }

        [TestMethod]
        public void SanitizeFileName_PathSeparators_RemovesThem()
        {
            string result = PathHelpers.SanitizeFileName("dir\\sub/file.txt");
            Assert.AreEqual("dirsubfile.txt", result);
        }

        [TestMethod]
        public void SanitizeFileName_PreservesDotsAndSpaces()
        {
            Assert.AreEqual("my file.backup.bak", PathHelpers.SanitizeFileName("my file.backup.bak"));
        }
        #endregion
    }
}
