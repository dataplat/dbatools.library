using Dataplat.Dbatools.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Commands.Test
{
    /// <summary>
    /// Coverage for RestoreUtility.GetPathSep, the C# parity port of
    /// private/functions/Get-DbaPathSep.ps1. Ground truth probed on both editions:
    /// PowerShell special-cases Length on $null (0 since v3), so a null server, a
    /// null PathSeparator and an empty PathSeparator all yield the default backslash
    /// there, matching this port's IsNullOrEmpty exactly; any non-empty separator
    /// passes through verbatim. Only the null-server case is constructible offline
    /// (SMO's PathSeparator needs a live connection to read, and setting it is not
    /// possible on a disconnected Server) - the non-empty passthrough, including the
    /// forward slash on Linux instances, rides the integrator gate through the
    /// restore-family suites.
    /// </summary>
    [TestClass]
    public class RestoreUtilityPathSepTest
    {
        [TestMethod]
        public void PathSep_NullServerYieldsDefaultBackslash()
        {
            Assert.AreEqual("\\", RestoreUtility.GetPathSep(null));
        }
    }
}
