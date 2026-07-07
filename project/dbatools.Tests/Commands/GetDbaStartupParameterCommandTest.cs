using System;
using System.Collections.Generic;
using Dataplat.Dbatools.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Commands.Test
{
    [TestClass]
    public class GetDbaStartupParameterCommandTest
    {
        [TestMethod]
        public void FlagsOrNone_EmptyList_ReturnsNoneString()
        {
            object result = StartupParameterParser.FlagsOrNone(new List<string>());
            Assert.AreEqual("None", result);
        }

        [TestMethod]
        public void FlagsOrNone_SingleFlag_ReturnsIntArray()
        {
            object result = StartupParameterParser.FlagsOrNone(new List<string> { "-T2544" });
            int[] arr = result as int[];
            Assert.IsNotNull(arr, "Expected int[]");
            Assert.AreEqual(1, arr.Length);
            Assert.AreEqual(2544, arr[0]);
        }

        [TestMethod]
        public void FlagsOrNone_MultipleFlags_ReturnsIntArray()
        {
            object result = StartupParameterParser.FlagsOrNone(new List<string> { "-T3604", "-T3605" });
            int[] arr = result as int[];
            Assert.IsNotNull(arr);
            Assert.AreEqual(2, arr.Length);
            Assert.AreEqual(3604, arr[0]);
            Assert.AreEqual(3605, arr[1]);
        }

        [TestMethod]
        public void Collect_MatchingPrefix_ReturnsMatches()
        {
            string[] startupParams = { "-dC:\\Data\\master.mdf", "-eC:\\Log\\ERRORLOG", "-lC:\\Log\\mastlog.ldf", "-T2544" };
            List<string> traceFlags = StartupParameterParser.Collect(startupParams, "-T");
            Assert.AreEqual(1, traceFlags.Count);
            Assert.AreEqual("-T2544", traceFlags[0]);
        }

        [TestMethod]
        public void Collect_NoMatch_ReturnsEmpty()
        {
            string[] startupParams = { "-dC:\\Data\\master.mdf", "-eC:\\Log\\ERRORLOG" };
            List<string> traceFlags = StartupParameterParser.Collect(startupParams, "-T");
            Assert.AreEqual(0, traceFlags.Count);
        }

        [TestMethod]
        public void Split_SemicolonDelimited_SplitsCorrectly()
        {
            string raw = "-dC:\\Data\\master.mdf;-eC:\\Log\\ERRORLOG;-lC:\\Log\\mastlog.ldf;-T2544";
            string[] parts = StartupParameterParser.Split(raw);
            Assert.AreEqual(4, parts.Length);
            Assert.AreEqual("-dC:\\Data\\master.mdf", parts[0]);
            Assert.AreEqual("-T2544", parts[3]);
        }
    }
}
