using Dataplat.Dbatools.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Commands.Test
{
    /// <summary>
    /// Covers the absorbed Get-AdjustedTotalRowsCopied helper carried by both
    /// ImportDbaCsvCommand and WriteDbaDbTableDataCommand. The legacy bulk copy library
    /// tracks rows copied in a 4-byte counter that wraps past int.MaxValue
    /// (dataplat/dbatools#6927); the helper reconstructs the true rows-added delta from
    /// the wrapped reported value and the previously reported value. Expected values are
    /// ground-truthed against the PowerShell helper on both editions with Int64-typed
    /// inputs - the only input type the compiled call sites produce, since both commands
    /// feed SqlRowsCopiedEventArgs.RowsCopied (Int64) and a long field into every call.
    /// The PowerShell helper additionally throws for an Int32-typed previous value of
    /// exactly int.MinValue ([math]::Abs binds the Int32 overload and overflows); that
    /// input type cannot reach these methods, whose long parameters never bind the
    /// throwing overload.
    /// </summary>
    [TestClass]
    public class AdjustedRowsCopiedTest
    {
        private static readonly long[][] GroundTruth = new[]
        {
            // { reportedRowsCopied, previousRowsCopied, expected }
            new[] { 100L, 0L, 100L },
            new[] { 5L, -10L, 15L },
            new[] { -2147483648L, 2147483647L, 1L },
            new[] { -2147483643L, 2147483647L, 6L },
            new[] { -5L, -10L, 5L },
            new[] { 0L, 12345L, 0L },
            new[] { 0L, -12345L, 0L },
            new[] { 7L, -2147483648L, 2147483655L },
            new[] { -2147483000L, 1000000L, 2146484296L },
            // A counter moving backward while negative yields a NEGATIVE delta in the
            // source; the shipped behavior is preserved rather than clamped.
            new[] { -10L, -5L, -5L },
        };

        [TestMethod]
        public void AdjustedRows_ImportCopyMatchesGroundTruth()
        {
            foreach (long[] c in GroundTruth)
            {
                Assert.AreEqual(c[2], ImportDbaCsvCommand.GetAdjustedTotalRowsCopied(c[0], c[1]),
                    $"reported={c[0]} previous={c[1]}");
            }
        }

        [TestMethod]
        public void AdjustedRows_WriteCopyMatchesGroundTruth()
        {
            foreach (long[] c in GroundTruth)
            {
                Assert.AreEqual(c[2], WriteDbaDbTableDataCommand.GetAdjustedTotalRowsCopied(c[0], c[1]),
                    $"reported={c[0]} previous={c[1]}");
            }
        }

        [TestMethod]
        public void AdjustedRows_BothCopiesAgreeAcrossBoundarySweep()
        {
            // The two absorbed copies must never drift apart. Sweep a boundary-heavy
            // grid: every combination of values around 0, +/-1, the int32 extremes and
            // near-extremes, and mid-range magnitudes on both sides.
            long[] interesting = new[]
            {
                long.MinValue + 1, (long)int.MinValue, int.MinValue + 1L, -2147483000L,
                -12345L, -10L, -5L, -1L, 0L, 1L, 5L, 10L, 12345L, 1000000L,
                int.MaxValue - 1L, (long)int.MaxValue, long.MaxValue - 1,
            };
            foreach (long reported in interesting)
            {
                foreach (long previous in interesting)
                {
                    long importResult = ImportDbaCsvCommand.GetAdjustedTotalRowsCopied(reported, previous);
                    long writeResult = WriteDbaDbTableDataCommand.GetAdjustedTotalRowsCopied(reported, previous);
                    Assert.AreEqual(importResult, writeResult,
                        $"copy drift at reported={reported} previous={previous}");
                }
            }
        }

        [TestMethod]
        public void AdjustedRows_ZeroReportedIsAlwaysZero()
        {
            // Neither branch of the source runs when the reported value is zero, so the
            // delta is zero regardless of the previous value.
            Assert.AreEqual(0L, ImportDbaCsvCommand.GetAdjustedTotalRowsCopied(0, long.MaxValue - 1));
            Assert.AreEqual(0L, ImportDbaCsvCommand.GetAdjustedTotalRowsCopied(0, long.MinValue + 1));
            Assert.AreEqual(0L, WriteDbaDbTableDataCommand.GetAdjustedTotalRowsCopied(0, long.MaxValue - 1));
            Assert.AreEqual(0L, WriteDbaDbTableDataCommand.GetAdjustedTotalRowsCopied(0, long.MinValue + 1));
        }

        [TestMethod]
        public void AdjustedRows_FullWrapCycleAccumulatesTrueTotal()
        {
            // Drive a simulated counter through a full wrap the way the bulk copy event
            // stream does: accumulate deltas across the positive climb, the wrap to
            // int.MinValue, and the climb back toward zero. The accumulated total must
            // equal the true (unwrapped) number of rows.
            long total = 0;
            long previous = 0;
            long[] reportedSequence = new[] { 1000000000L, 2000000000L, 2147483647L, -2147483648L, -2000000000L, -1L, 0L };
            long[] trueTotals = new[] { 1000000000L, 2000000000L, 2147483647L, 2147483648L, 2294967296L, 4294967295L, 4294967295L };
            for (int i = 0; i < reportedSequence.Length; i++)
            {
                total += ImportDbaCsvCommand.GetAdjustedTotalRowsCopied(reportedSequence[i], previous);
                previous = reportedSequence[i];
                // The trailing zero report adds nothing; the total keeps the last value.
                Assert.AreEqual(trueTotals[i], total, $"after reported={reportedSequence[i]}");
            }
        }
    }
}
