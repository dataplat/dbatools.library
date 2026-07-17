using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using Dataplat.Dbatools.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Commands.Test
{
    /// <summary>
    /// Command-level coverage for the absorbed Test-ExportDirectory helper
    /// (ExportDbaScriptCommand.TestExportDirectory). The helper runs in the command's
    /// begin block BEFORE any pipeline object is scripted, so piping a dummy object and
    /// pointing -Path at a real filesystem target exercises the helper directly without
    /// any SMO input. Behavior ground-truthed against the PS source Test-ExportDirectory
    /// on PS 5.1 and 7.6 (probe 2026-07-17, identical both editions): a missing path is
    /// created as a directory; an existing directory is a no-op; an existing FILE stops
    /// with "must be a directory". The two runtime pins below drive the real compiled
    /// command for the create-missing and reject-file branches. Documented minor
    /// divergence, unreachable for normal export dirs: ProviderPathExists writes an error
    /// for any provider exception where Test-Path would silently return false (malformed
    /// path / bad drive); the happy branches match.
    /// </summary>
    [TestClass]
    public class TestExportDirectoryTest
    {
        private static System.Management.Automation.Runspaces.Runspace NewRunspace()
        {
            InitialSessionState iss = InitialSessionState.CreateDefault2();
            iss.Commands.Add(new SessionStateCmdletEntry("Export-DbaScript", typeof(ExportDbaScriptCommand), null));
            System.Management.Automation.Runspaces.Runspace runspace = RunspaceFactory.CreateRunspace(iss);
            runspace.Open();
            return runspace;
        }

        [TestMethod]
        public void TestExportDirectory_MissingPathIsCreated()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ted-" + Guid.NewGuid().ToString("N"));
            Assert.IsFalse(Directory.Exists(dir), "precondition: path does not exist");
            try
            {
                using (System.Management.Automation.Runspaces.Runspace runspace = NewRunspace())
                using (PowerShell shell = PowerShell.Create())
                {
                    shell.Runspace = runspace;
                    // A dummy pipeline object satisfies the mandatory VFP InputObject; the
                    // begin-block Test-ExportDirectory runs first and creates the directory.
                    // Any downstream failure scripting the dummy string is irrelevant here.
                    shell.AddScript("'x' | Export-DbaScript -Path $args[0]").AddArgument(dir);
                    shell.Invoke();
                }
                Assert.IsTrue(Directory.Exists(dir), "the missing -Path was created as a directory");
            }
            finally
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
        }

        [TestMethod]
        public void TestExportDirectory_ExistingFileStopsWithMessage()
        {
            string file = Path.Combine(Path.GetTempPath(), "ted-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(file, "x");
            try
            {
                using (System.Management.Automation.Runspaces.Runspace runspace = NewRunspace())
                using (PowerShell shell = PowerShell.Create())
                {
                    shell.Runspace = runspace;
                    // -EnableException makes the begin-block Stop-Function terminating; the
                    // dummy is never scripted because the interrupt fires first.
                    shell.AddScript("'x' | Export-DbaScript -Path $args[0] -EnableException").AddArgument(file);
                    try
                    {
                        shell.Invoke();
                        Assert.IsTrue(shell.HadErrors, "an existing file -Path must surface an error");
                    }
                    catch (RuntimeException ex)
                    {
                        StringAssert.Contains(ex.Message, "must be a directory", "the stop reports the directory requirement");
                        return;
                    }
                    // If Invoke did not throw, the terminating error still lands in the stream.
                    bool sawMessage = false;
                    foreach (ErrorRecord error in shell.Streams.Error)
                    {
                        if (error.ToString().Contains("must be a directory"))
                            sawMessage = true;
                    }
                    Assert.IsTrue(sawMessage, "the error reports the must-be-a-directory message");
                }
            }
            finally
            {
                File.Delete(file);
            }
        }
    }
}
