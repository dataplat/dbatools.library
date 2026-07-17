using System;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using Dataplat.Dbatools.Commands;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Commands.Test
{
    /// <summary>
    /// Host cmdlet for XpDirTreeScanner.Scan. It drives the scanner with an s3:// path and
    /// EnableException on: that branch rejects the path and calls Stop-Function BEFORE any
    /// server access, so the disconnected server handed in is never touched.
    /// </summary>
    [Cmdlet("Test", "XpDirTreeScannerHost")]
    public sealed class TestXpDirTreeScannerHostCommand : DbaBaseCmdlet
    {
        [Parameter(Mandatory = true)]
        public string Path { get; set; }

        [Parameter]
        public SwitchParameter Enable { get; set; }

        protected override void ProcessRecord()
        {
            ServerConnection connection = new ServerConnection();
            connection.ServerInstance = "tcp:127.0.0.1,9";
            connection.ConnectTimeout = 1;
            Server server = new Server(connection);
            try
            {
                System.Collections.Generic.List<PSObject> rows =
                    XpDirTreeScanner.Scan(this, server, Path, noRecurse: false, enableException: Enable.IsPresent);
                WriteObject("ROWS:" + rows.Count);
            }
            catch (Exception ex)
            {
                WriteObject("THREW:" + ex.GetType().Name);
            }
        }
    }

    /// <summary>
    /// Coverage for XpDirTreeScanner.Scan, the C# parity port of
    /// private/functions/Get-XpDirTreeRestoreFile.ps1 (recursive xp_dirtree backup-file
    /// enumeration). Faithfulness verified by line-by-line diff: the path-separator
    /// branching (case-insensitive https?:// -&gt; "/", s3:// -&gt; reject, else the SMO
    /// PathSeparator) matches; the .bak/.trn suffix guard and trailing-separator append
    /// match; the source's doubled Test-DbaPath check is preserved; the version-branch SQL
    /// (&gt;=14 dm_os_enumerate_filesystem, &lt;9 xp_dirtree, else sys.xp_dirtree) matches
    /// with a sanctioned BP-101 change (the user path binds as a typed @path SqlParameter
    /// instead of the source's string interpolation - semantic parity, injection-hardened);
    /// a DBNull file flag is skipped, matching the source's Where-Object no-match; and the
    /// recursion drops EnableException and NoRecurse exactly as the source does. The live
    /// enumeration and recursion require a SQL instance and ride the integrator gate through
    /// Get-DbaBackupInformation. The offline pin below is method-specific: an s3:// path
    /// with EnableException terminates at the S3 guard (Stop-Function throws under
    /// EnableException) BEFORE any server I/O, and without EnableException it does not throw
    /// there.
    /// </summary>
    [TestClass]
    public class XpDirTreeScannerTest
    {
        private static string Invoke(string path, bool enable)
        {
            InitialSessionState iss = InitialSessionState.CreateDefault2();
            iss.Commands.Add(new SessionStateCmdletEntry("Test-XpDirTreeScannerHost", typeof(TestXpDirTreeScannerHostCommand), null));
            using (System.Management.Automation.Runspaces.Runspace runspace = RunspaceFactory.CreateRunspace(iss))
            {
                runspace.Open();
                using (PowerShell shell = PowerShell.Create())
                {
                    shell.Runspace = runspace;
                    shell.AddCommand("Test-XpDirTreeScannerHost").AddParameter("Path", path);
                    if (enable)
                        shell.AddParameter("Enable");
                    Collection<PSObject> output = shell.Invoke();
                    Assert.AreEqual(1, output.Count, "host cmdlet emits exactly one outcome record");
                    return (string)output[0].BaseObject;
                }
            }
        }

        [TestMethod]
        public void XpDirTree_S3WithEnableExceptionTerminatesBeforeServerAccess()
        {
            // The disconnected server would fail any real access; a THREW here means the S3
            // guard terminated first (Stop-Function throws under EnableException), never
            // reaching the server. That is the S3-rejection contract.
            string outcome = Invoke("s3://bucket/backups/", enable: true);
            Assert.AreEqual("THREW:InnerCommandException", outcome);
        }

        [TestMethod]
        public void XpDirTree_S3WithoutEnableExceptionDoesNotThrowAtTheGuard()
        {
            // Without EnableException the S3 Stop-Function is non-terminating: execution
            // falls through (pathSep stays null) and only later fails at the server. The
            // failure is therefore NOT the S3 guard's InnerCommandException - it is the
            // downstream server access throwing on the unreachable instance.
            string outcome = Invoke("s3://bucket/backups/", enable: false);
            StringAssert.StartsWith(outcome, "THREW:", "the unreachable server still fails downstream");
            Assert.AreNotEqual("THREW:InnerCommandException", outcome,
                "without EnableException the S3 guard must not terminate the scan");
        }
    }
}
