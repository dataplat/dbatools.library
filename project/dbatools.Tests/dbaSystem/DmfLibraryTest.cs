using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Dataplat.Dbatools.dbaSystem;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.dbaSystem.Test
{
    /// <summary>
    /// TB-002 coverage for DmfLibrary.Load, the C# parity port of
    /// private/functions/Add-PbmLibrary.ps1. Assembly loading is process-global and
    /// irreversible, so every load scenario runs inside ONE sequential test whose internal
    /// ordering is deterministic: the asymmetric missing-file legs prove Common loads
    /// before Dmf, the success leg asserts Dmf's actual load location under the fake
    /// root (its first true load in the process), then idempotence re-runs the call.
    /// The fake roots are built from the DMF assemblies the SqlManagementObjects package
    /// ships into the test output for both TFMs.
    /// </summary>
    [TestClass]
    [DoNotParallelize]
    public class DmfLibraryTest
    {
        private static string NewRoot(bool withCommon, bool withDmf)
        {
            string root = Path.Combine(Path.GetTempPath(), "dbatools-tests-dmf-" + Guid.NewGuid().ToString("N"));
            string lib = Path.Combine(root, "lib");
            Directory.CreateDirectory(lib);
            string binDir = AppDomain.CurrentDomain.BaseDirectory;
            if (withCommon)
                File.Copy(Path.Combine(binDir, "Microsoft.SqlServer.Dmf.Common.dll"), Path.Combine(lib, "Microsoft.SqlServer.Dmf.Common.DLL"));
            if (withDmf)
                File.Copy(Path.Combine(binDir, "Microsoft.SqlServer.Dmf.dll"), Path.Combine(lib, "Microsoft.SqlServer.Dmf.dll"));
            return root;
        }

        private static bool IsLoaded(string assemblyName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Any(assembly => String.Equals(assembly.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase));
        }

        private static void TryRemove(string root)
        {
            // Loaded assembly files stay locked on net472; best-effort cleanup only.
            try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        [TestMethod]
        public void Load_FullLifecycle_OrderFailuresLocationAndIdempotence()
        {
            if (IsLoaded("Microsoft.SqlServer.Dmf.Common") || IsLoaded("Microsoft.SqlServer.Dmf"))
                Assert.Inconclusive("DMF already loaded in this test process; load-order and location legs cannot discriminate.");

            // Leg 1: Common missing, Dmf present -> fails on the FIRST load; Dmf must remain
            // unloaded, proving Load never reached the second file (Common-first order).
            string commonMissing = NewRoot(false, true);
            string dmfMissing = NewRoot(true, false);
            string complete = NewRoot(true, true);
            try
            {
                FileNotFoundException firstFailure = Assert.ThrowsException<FileNotFoundException>(
                    delegate { DmfLibrary.Load(commonMissing); });
                StringAssert.Contains(firstFailure.FileName, "Dmf.Common");
                Assert.IsFalse(IsLoaded("Microsoft.SqlServer.Dmf"), "Dmf loaded despite Common failing first - load order broken");
                Assert.IsFalse(IsLoaded("Microsoft.SqlServer.Dmf.Common"));

                // Leg 2: Common present, Dmf missing -> Common loads (first), then the
                // SECOND load fails on Dmf. Common being loaded afterward proves it was
                // attempted (and succeeded) before the failure.
                FileNotFoundException secondFailure = Assert.ThrowsException<FileNotFoundException>(
                    delegate { DmfLibrary.Load(dmfMissing); });
                Assert.IsFalse(secondFailure.FileName.Contains("Dmf.Common"), "failure should be the Dmf load, not Common");
                Assert.IsTrue(IsLoaded("Microsoft.SqlServer.Dmf.Common"), "Common not loaded before the Dmf failure - load order broken");
                Assert.IsFalse(IsLoaded("Microsoft.SqlServer.Dmf"));

                // Leg 3: complete root -> success, with the load LOCATION pinned per TFM.
                // Add-Type -Path shows the same split per PS edition; in production lib\
                // holds the only copy, so both editions load from lib and behavior converges.
                DmfLibrary.Load(complete);
                Assembly dmf = AppDomain.CurrentDomain.GetAssemblies()
                    .Single(assembly => String.Equals(assembly.GetName().Name, "Microsoft.SqlServer.Dmf", StringComparison.OrdinalIgnoreCase));
#if NETFRAMEWORK
                // Full-framework LoadFrom context: the file under the fake root is what loads.
                StringAssert.StartsWith(Path.GetFullPath(dmf.Location), Path.GetFullPath(complete), "Dmf loaded from outside the fake library root");
#else
                // .NET (Core) LoadFrom resolves a colliding identity against the Default
                // AssemblyLoadContext first; the test app ships Dmf in its deps graph, so
                // the bin copy deterministically wins over the fake-root file.
                StringAssert.StartsWith(Path.GetFullPath(dmf.Location), Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory), "Dmf resolved from an unexpected location for the Default ALC");
#endif

                // Leg 4: idempotence - re-running with everything already loaded succeeds,
                // like re-running the helper's Add-Type calls.
                DmfLibrary.Load(complete);
            }
            finally
            {
                TryRemove(commonMissing);
                TryRemove(dmfMissing);
                TryRemove(complete);
            }
        }

        [TestMethod]
        public void Load_NullOrEmptyRootSurfacesAFailureLikeTheHelper()
        {
            // In PS a missing $script:libraryroot still lands in the same catch -> Stop-Function
            // flow; the port surfaces it as ArgumentNullException before touching the disk.
            Assert.ThrowsException<ArgumentNullException>(delegate { DmfLibrary.Load(null); });
            Assert.ThrowsException<ArgumentNullException>(delegate { DmfLibrary.Load(String.Empty); });
        }
    }
}
