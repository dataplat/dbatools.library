using System.Reflection;
using Dataplat.Dbatools.Commands;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Commands.Test
{
    /// <summary>
    /// Covers the absorbed Get-BulkRowsCopiedCount helper in ImportDbaCsvCommand: a
    /// reflection read of SqlBulkCopy's private _rowsCopied field (the only way to get
    /// a rows-copied count out of the legacy bulk copy library mid-flight), returning
    /// -1 on any failure. Expected values ground-truthed against the PowerShell helper
    /// (field set via reflection, values read back verbatim; null input returns -1).
    /// The counter feeds GetAdjustedTotalRowsCopied, whose -1 failure-sentinel
    /// consequences are pinned in AdjustedRowsCopiedTest.
    /// </summary>
    [TestClass]
    public class BulkRowsCopiedTest
    {
        private static readonly FieldInfo RowsCopiedField = typeof(SqlBulkCopy).GetField(
            "_rowsCopied", BindingFlags.NonPublic | BindingFlags.GetField | BindingFlags.Instance);

        [TestMethod]
        public void BulkRows_PrivateFieldStillResolves()
        {
            // The helper depends on a private field of the vendored
            // Microsoft.Data.SqlClient. If a package upgrade renames or removes it,
            // every count silently degrades to the -1 failure sentinel - which
            // GetAdjustedTotalRowsCopied then turns into a 4294967295-row phantom
            // total. This assertion makes that upgrade loudly visible instead.
            Assert.IsNotNull(RowsCopiedField);
        }

        [TestMethod]
        public void BulkRows_FreshInstanceReadsZero()
        {
            // The connection-string constructor performs no I/O, so a fresh instance
            // exposes the field's initial value.
            using (SqlBulkCopy bulk = new SqlBulkCopy("Server=localhost;"))
            {
                Assert.AreEqual(0, ImportDbaCsvCommand.GetBulkRowsCopiedCount(bulk));
            }
        }

        [TestMethod]
        public void BulkRows_FieldValuesReadBackVerbatim()
        {
            // Drive the real read path: values planted in the private field must come
            // back unchanged, including the negatives an int32 wrap can produce and
            // the -1 that collides with the failure sentinel.
            using (SqlBulkCopy bulk = new SqlBulkCopy("Server=localhost;"))
            {
                foreach (int value in new[] { 42, -1, int.MinValue, int.MaxValue })
                {
                    RowsCopiedField.SetValue(bulk, value);
                    Assert.AreEqual(value, ImportDbaCsvCommand.GetBulkRowsCopiedCount(bulk));
                }
            }
        }

        [TestMethod]
        public void BulkRows_NullInstanceReturnsFailureSentinel()
        {
            // GetValue(null) on an instance field throws inside the try; the helper
            // reports the same -1 the PowerShell source returns for a null argument.
            Assert.AreEqual(-1, ImportDbaCsvCommand.GetBulkRowsCopiedCount(null));
        }
    }
}
