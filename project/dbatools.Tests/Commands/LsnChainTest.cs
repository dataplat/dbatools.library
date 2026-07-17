using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using Dataplat.Dbatools.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Commands.Test
{
    /// <summary>
    /// Host cmdlet for LsnChain.Test - it needs a live PSCmdlet for the Write-Message
    /// plumbing (discarded output, but the signature requires it). The test builds
    /// synthetic backup-header PSObjects and drives the chain check in a real runspace.
    /// </summary>
    [Cmdlet("Test", "LsnChainHost")]
    public sealed class TestLsnChainHostCommand : DbaBaseCmdlet
    {
        [Parameter(Mandatory = true)]
        public PSObject[] Files { get; set; }

        [Parameter]
        public SwitchParameter Continue { get; set; }

        protected override void ProcessRecord()
        {
            bool result = LsnChain.Test(this, Files, Continue.IsPresent, enableException: false);
            WriteObject(result);
        }
    }

    /// <summary>
    /// Coverage for LsnChain.Test, the C# parity port of
    /// private/functions/Test-DbaLsnChain.ps1 (validates a restorable LSN chain of backup
    /// headers). The eight edition-AGNOSTIC scenarios (full-only, full+t-logs, gaps,
    /// multi-full, multi-diff, diff+log, and the -Continue short-circuit) are ground-truthed
    /// against the PS source on PS 5.1 AND 7.6 (probe 2026-07-17, identical both editions)
    /// and pinned in LsnChain_GroundTruthScenarios. The single-t-log break is NOT pinned as
    /// a contract: the source is edition-split there - a lone T-log emerging from
    /// Sort-Object is a scalar with no .Count on PS 5.1, so the source's chain-break loop is
    /// skipped and a broken single-log chain PASSES on 5.1 but FAILS on 7. The port uses
    /// List.Count and returns the PS7 result on both TFMs, changing the shipped result on a
    /// PS5.1 host. Whether to keep that unification or replicate the 5.1 loop-skip is a
    /// preserve-vs-unify decision filed to the Test-DbaLsnChain owner and not yet ruled; the
    /// LsnChain_SingleTLogBreak_CharacterizesOpenEditionDivergence method only characterizes
    /// what the port currently does.
    /// </summary>
    [TestClass]
    public class LsnChainTest
    {
        private static PSObject Bh(string type, long first, long last, long chk, long dbbl, string name)
        {
            PSObject o = new PSObject();
            o.Properties.Add(new PSNoteProperty("BackupTypeDescription", type));
            o.Properties.Add(new PSNoteProperty("FirstLSN", first));
            o.Properties.Add(new PSNoteProperty("LastLSN", last));
            o.Properties.Add(new PSNoteProperty("CheckPointLsn", chk));
            o.Properties.Add(new PSNoteProperty("DatabaseBackupLsn", dbbl));
            o.Properties.Add(new PSNoteProperty("FullName", name));
            return o;
        }

        private static bool Run(PSObject[] files, bool cont)
        {
            InitialSessionState iss = InitialSessionState.CreateDefault2();
            iss.Commands.Add(new SessionStateCmdletEntry("Test-LsnChainHost", typeof(TestLsnChainHostCommand), null));
            using (System.Management.Automation.Runspaces.Runspace runspace = RunspaceFactory.CreateRunspace(iss))
            {
                runspace.Open();
                using (PowerShell shell = PowerShell.Create())
                {
                    shell.Runspace = runspace;
                    shell.AddCommand("Test-LsnChainHost").AddParameter("Files", files);
                    if (cont)
                        shell.AddParameter("Continue");
                    Collection<PSObject> output = shell.Invoke();
                    Assert.AreEqual(1, output.Count, "host emits exactly one bool");
                    return (bool)output[0].BaseObject;
                }
            }
        }

        [TestMethod]
        public void LsnChain_GroundTruthScenarios()
        {
            // Full only -> valid.
            Assert.IsTrue(Run(new[] { Bh("Database", 1000, 2000, 1500, 1500, "full.bak") }, false),
                "01 full-only");

            // Full + two sequential T-logs -> valid.
            Assert.IsTrue(Run(new[]
            {
                Bh("Database", 1000, 2000, 1500, 1500, "full.bak"),
                Bh("Transaction Log", 2000, 3000, 1500, 1500, "t1.trn"),
                Bh("Transaction Log", 3000, 4000, 1500, 1500, "t2.trn"),
            }, false), "02 full+2 valid t-logs");

            // Full + T-logs with a gap -> broken chain.
            Assert.IsFalse(Run(new[]
            {
                Bh("Database", 1000, 2000, 1500, 1500, "full.bak"),
                Bh("Transaction Log", 2000, 3000, 1500, 1500, "t1.trn"),
                Bh("Transaction Log", 3500, 4000, 1500, 1500, "t2.trn"),
            }, false), "03 t-log gap");

            // Two fulls with different FirstLSN -> unsupported.
            Assert.IsFalse(Run(new[]
            {
                Bh("Database", 1000, 2000, 1500, 1500, "fullA.bak"),
                Bh("Database", 1100, 2100, 1500, 1500, "fullB.bak"),
            }, false), "04 two fulls diff LSN");

            // Zero fulls -> unsupported.
            Assert.IsFalse(Run(new[] { Bh("Transaction Log", 2000, 3000, 1500, 1500, "t1.trn") }, false),
                "05 no full");

            // Full + diff + t-log -> valid.
            Assert.IsTrue(Run(new[]
            {
                Bh("Database", 1000, 2000, 1500, 1500, "full.bak"),
                Bh("Database Differential", 2500, 3000, 1500, 1500, "diff.bak"),
                Bh("Transaction Log", 3000, 4000, 1500, 1500, "t1.trn"),
            }, false), "06 full+diff+t-log");

            // Two diffs with different FirstLSN -> unsupported.
            Assert.IsFalse(Run(new[]
            {
                Bh("Database", 1000, 2000, 1500, 1500, "full.bak"),
                Bh("Database Differential", 2500, 3000, 1500, 1500, "diffA.bak"),
                Bh("Database Differential", 2600, 3100, 1500, 1500, "diffB.bak"),
            }, false), "07 two diffs diff LSN");

            // -Continue short-circuits to valid regardless of the (broken) chain.
            Assert.IsTrue(Run(new[]
            {
                Bh("Database", 1000, 2000, 1500, 1500, "full.bak"),
                Bh("Transaction Log", 3500, 4000, 1500, 1500, "t2.trn"),
            }, true), "09 -Continue short-circuit");
        }

        [TestMethod]
        public void LsnChain_SingleTLogBreak_CharacterizesOpenEditionDivergence()
        {
            // CHARACTERIZATION of an OPEN, FILED divergence - not a sanctioned contract.
            // A lone T-log whose FirstLSN (2500) exceeds the full anchor's LastLSN (2000)
            // is a broken chain. The SOURCE is edition-split: PS7 returns false (break
            // detected); PS5.1 returns true because a single Sort-Object result is a scalar
            // with no .Count, so the source's break loop is skipped. The port uses
            // List.Count and returns false on BOTH TFMs - which changes the shipped result
            // on a PS5.1 host (legacy true -> compiled false). This assertion locks what
            // the port CURRENTLY does; whether the port should keep the unified PS7 result
            // or replicate the PS5.1 loop-skip (bug-for-bug) is a preserve-vs-unify call
            // filed to the Test-DbaLsnChain owner (TB-092), not yet ruled.
            Assert.IsFalse(Run(new[]
            {
                Bh("Database", 1000, 2000, 1500, 1500, "full.bak"),
                Bh("Transaction Log", 2500, 3000, 1500, 1500, "t1.trn"),
            }, false), "08 single-t-log break - current port behavior (PS7-faithful)");
        }
    }
}
