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
    /// private/functions/Add-PbmLibrary.ps1: loads the two DMF assemblies from
    /// &lt;libraryRoot&gt;\lib (Common first), is idempotent like repeated Add-Type, and
    /// surfaces load failures to the caller (which owns the Stop-Function message). The
    /// fake library root is built from the DMF assemblies the SqlManagementObjects package
    /// ships into the test output for both TFMs.
    /// </summary>
    [TestClass]
    public class DmfLibraryTest
    {
        private static string BuildFakeLibraryRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "dbatools-tests-dmf-" + Guid.NewGuid().ToString("N"));
            string lib = Path.Combine(root, "lib");
            Directory.CreateDirectory(lib);
            string binDir = AppDomain.CurrentDomain.BaseDirectory;
            File.Copy(Path.Combine(binDir, "Microsoft.SqlServer.Dmf.Common.dll"), Path.Combine(lib, "Microsoft.SqlServer.Dmf.Common.DLL"));
            File.Copy(Path.Combine(binDir, "Microsoft.SqlServer.Dmf.dll"), Path.Combine(lib, "Microsoft.SqlServer.Dmf.dll"));
            return root;
        }

        private static void TryRemove(string root)
        {
            // Loaded assembly files stay locked on net472; best-effort cleanup only.
            try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        [TestMethod]
        public void Load_LoadsBothDmfAssembliesFromLibFolder()
        {
            string root = BuildFakeLibraryRoot();
            try
            {
                DmfLibrary.Load(root);

                string[] loaded = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetName().Name)
                    .ToArray();
                Assert.IsTrue(loaded.Contains("Microsoft.SqlServer.Dmf.Common"), "Dmf.Common not loaded");
                Assert.IsTrue(loaded.Contains("Microsoft.SqlServer.Dmf"), "Dmf not loaded");
            }
            finally
            {
                TryRemove(root);
            }
        }

        [TestMethod]
        public void Load_IsIdempotentLikeRepeatedAddType()
        {
            string root = BuildFakeLibraryRoot();
            try
            {
                DmfLibrary.Load(root);
                DmfLibrary.Load(root);
            }
            finally
            {
                TryRemove(root);
            }
        }

        [TestMethod]
        public void Load_MissingAssembliesSurfaceTheLoadFailure()
        {
            // PS parity: the helper's catch wraps ANY load failure into Stop-Function
            // "Could not load DMF libraries"; the C# surface is the propagated exception.
            string root = Path.Combine(Path.GetTempPath(), "dbatools-tests-dmf-empty-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "lib"));
            try
            {
                Assert.ThrowsException<FileNotFoundException>(delegate { DmfLibrary.Load(root); });
            }
            finally
            {
                TryRemove(root);
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
