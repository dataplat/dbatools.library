using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Configuration;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class ExportDbatoolsConfigCommandTests
    {
        #region GetConfigsByFullName
        [TestMethod]
        public void GetConfigsByFullName_ExactMatch_ReturnsSingleConfig()
        {
            var config = new Config { Module = "testexport", Name = "setting1" };
            config.Value = "testvalue";
            ConfigurationHost.Configurations["testexport.setting1"] = config;

            try
            {
                List<Config> results = ExportDbatoolsConfigCommand.GetConfigsByFullName("testexport.setting1");

                Assert.AreEqual(1, results.Count);
                Assert.AreEqual("testexport.setting1", results[0].FullName);
            }
            finally
            {
                Config removed;
                ConfigurationHost.Configurations.TryRemove("testexport.setting1", out removed);
            }
        }

        [TestMethod]
        public void GetConfigsByFullName_WildcardMatch_ReturnsMultiple()
        {
            var config1 = new Config { Module = "testexport2", Name = "alpha" };
            config1.Value = "a";
            var config2 = new Config { Module = "testexport2", Name = "beta" };
            config2.Value = "b";
            ConfigurationHost.Configurations["testexport2.alpha"] = config1;
            ConfigurationHost.Configurations["testexport2.beta"] = config2;

            try
            {
                List<Config> results = ExportDbatoolsConfigCommand.GetConfigsByFullName("testexport2.*");

                Assert.AreEqual(2, results.Count);
            }
            finally
            {
                Config removed;
                ConfigurationHost.Configurations.TryRemove("testexport2.alpha", out removed);
                ConfigurationHost.Configurations.TryRemove("testexport2.beta", out removed);
            }
        }

        [TestMethod]
        public void GetConfigsByFullName_NoMatch_ReturnsEmpty()
        {
            List<Config> results = ExportDbatoolsConfigCommand.GetConfigsByFullName("nonexistent.module.xyz");

            Assert.AreEqual(0, results.Count);
        }

        [TestMethod]
        public void GetConfigsByFullName_HiddenConfig_IsExcluded()
        {
            var config = new Config { Module = "testexphidden", Name = "secret", Hidden = true };
            config.Value = "hidden";
            ConfigurationHost.Configurations["testexphidden.secret"] = config;

            try
            {
                List<Config> results = ExportDbatoolsConfigCommand.GetConfigsByFullName("testexphidden.secret");

                Assert.AreEqual(0, results.Count, "Hidden configs should be excluded");
            }
            finally
            {
                Config removed;
                ConfigurationHost.Configurations.TryRemove("testexphidden.secret", out removed);
            }
        }
        #endregion

        #region GetConfigsByModule
        [TestMethod]
        public void GetConfigsByModule_MatchesModule_ReturnsConfigs()
        {
            var config1 = new Config { Module = "testmod", Name = "x" };
            config1.Value = 1;
            var config2 = new Config { Module = "testmod", Name = "y" };
            config2.Value = 2;
            var config3 = new Config { Module = "othermod", Name = "z" };
            config3.Value = 3;
            ConfigurationHost.Configurations["testmod.x"] = config1;
            ConfigurationHost.Configurations["testmod.y"] = config2;
            ConfigurationHost.Configurations["othermod.z"] = config3;

            try
            {
                List<Config> results = ExportDbatoolsConfigCommand.GetConfigsByModule("testmod", "*");

                Assert.AreEqual(2, results.Count);
            }
            finally
            {
                Config removed;
                ConfigurationHost.Configurations.TryRemove("testmod.x", out removed);
                ConfigurationHost.Configurations.TryRemove("testmod.y", out removed);
                ConfigurationHost.Configurations.TryRemove("othermod.z", out removed);
            }
        }

        [TestMethod]
        public void GetConfigsByModule_WithNameFilter_FiltersCorrectly()
        {
            var config1 = new Config { Module = "testmod3", Name = "timeout" };
            config1.Value = 30;
            var config2 = new Config { Module = "testmod3", Name = "retries" };
            config2.Value = 3;
            ConfigurationHost.Configurations["testmod3.timeout"] = config1;
            ConfigurationHost.Configurations["testmod3.retries"] = config2;

            try
            {
                List<Config> results = ExportDbatoolsConfigCommand.GetConfigsByModule("testmod3", "time*");

                Assert.AreEqual(1, results.Count);
                Assert.AreEqual("timeout", results[0].Name);
            }
            finally
            {
                Config removed;
                ConfigurationHost.Configurations.TryRemove("testmod3.timeout", out removed);
                ConfigurationHost.Configurations.TryRemove("testmod3.retries", out removed);
            }
        }

        [TestMethod]
        public void GetConfigsByModule_NoMatch_ReturnsEmpty()
        {
            List<Config> results = ExportDbatoolsConfigCommand.GetConfigsByModule("nonexistent_xyz", "*");

            Assert.AreEqual(0, results.Count);
        }

        [TestMethod]
        public void GetConfigsByModule_HiddenConfig_IsExcluded()
        {
            var config = new Config { Module = "testmodhidden", Name = "internal", Hidden = true };
            config.Value = "secret";
            ConfigurationHost.Configurations["testmodhidden.internal"] = config;

            try
            {
                List<Config> results = ExportDbatoolsConfigCommand.GetConfigsByModule("testmodhidden", "*");

                Assert.AreEqual(0, results.Count, "Hidden configs should be excluded from module search");
            }
            finally
            {
                Config removed;
                ConfigurationHost.Configurations.TryRemove("testmodhidden.internal", out removed);
            }
        }
        #endregion

        #region GetModuleExportConfigs
        [TestMethod]
        public void GetModuleExportConfigs_FiltersModuleExportAndUnchanged()
        {
            // Config that is ModuleExport=true and Unchanged=false (should be included)
            var config1 = new Config { Module = "testmodexp", Name = "exported", ModuleExport = true };
            config1.Initialized = true;
            config1.Value = "changed";
            // Config that is ModuleExport=false (should be excluded)
            var config2 = new Config { Module = "testmodexp", Name = "notexported", ModuleExport = false };
            config2.Initialized = true;
            config2.Value = "changed";
            // Config that is Unchanged (should be excluded)
            var config3 = new Config { Module = "testmodexp", Name = "unchanged", ModuleExport = true };

            ConfigurationHost.Configurations["testmodexp.exported"] = config1;
            ConfigurationHost.Configurations["testmodexp.notexported"] = config2;
            ConfigurationHost.Configurations["testmodexp.unchanged"] = config3;

            try
            {
                List<Config> results = ExportDbatoolsConfigCommand.GetModuleExportConfigs("testmodexp");

                Assert.AreEqual(1, results.Count);
                Assert.AreEqual("exported", results[0].Name);
            }
            finally
            {
                Config removed;
                ConfigurationHost.Configurations.TryRemove("testmodexp.exported", out removed);
                ConfigurationHost.Configurations.TryRemove("testmodexp.notexported", out removed);
                ConfigurationHost.Configurations.TryRemove("testmodexp.unchanged", out removed);
            }
        }

        [TestMethod]
        public void GetModuleExportConfigs_IncludesHiddenConfigs()
        {
            var config = new Config { Module = "testmodexph", Name = "hiddenbut", ModuleExport = true, Hidden = true };
            config.Initialized = true;
            config.Value = "changed";
            ConfigurationHost.Configurations["testmodexph.hiddenbut"] = config;

            try
            {
                List<Config> results = ExportDbatoolsConfigCommand.GetModuleExportConfigs("testmodexph");

                Assert.AreEqual(1, results.Count, "Hidden configs should be included when ModuleExport is true (equivalent to -Force)");
            }
            finally
            {
                Config removed;
                ConfigurationHost.Configurations.TryRemove("testmodexph.hiddenbut", out removed);
            }
        }

        [TestMethod]
        public void GetModuleExportConfigs_NoMatch_ReturnsEmpty()
        {
            List<Config> results = ExportDbatoolsConfigCommand.GetModuleExportConfigs("nonexistent_modexp_xyz");

            Assert.AreEqual(0, results.Count);
        }
        #endregion

        #region RegistryScopeValidation
        [TestMethod]
        public void RegistryBitMask_UserDefault_IsDetected()
        {
            int scope = (int)ConfigScope.UserDefault;
            bool isRegistry = (scope & 15) != 0;

            Assert.IsTrue(isRegistry, "UserDefault (1) should be detected as registry scope");
        }

        [TestMethod]
        public void RegistryBitMask_UserMandatory_IsDetected()
        {
            int scope = (int)ConfigScope.UserMandatory;
            bool isRegistry = (scope & 15) != 0;

            Assert.IsTrue(isRegistry, "UserMandatory (2) should be detected as registry scope");
        }

        [TestMethod]
        public void RegistryBitMask_SystemDefault_IsDetected()
        {
            int scope = (int)ConfigScope.SystemDefault;
            bool isRegistry = (scope & 15) != 0;

            Assert.IsTrue(isRegistry, "SystemDefault (4) should be detected as registry scope");
        }

        [TestMethod]
        public void RegistryBitMask_SystemMandatory_IsDetected()
        {
            int scope = (int)ConfigScope.SystemMandatory;
            bool isRegistry = (scope & 15) != 0;

            Assert.IsTrue(isRegistry, "SystemMandatory (8) should be detected as registry scope");
        }

        [TestMethod]
        public void RegistryBitMask_FileUserLocal_NotDetected()
        {
            int scope = (int)ConfigScope.FileUserLocal;
            bool isRegistry = (scope & 15) != 0;

            Assert.IsFalse(isRegistry, "FileUserLocal (16) should not be detected as registry scope");
        }

        [TestMethod]
        public void RegistryBitMask_FileUserShared_NotDetected()
        {
            int scope = (int)ConfigScope.FileUserShared;
            bool isRegistry = (scope & 15) != 0;

            Assert.IsFalse(isRegistry, "FileUserShared (32) should not be detected as registry scope");
        }

        [TestMethod]
        public void RegistryBitMask_FileSystem_NotDetected()
        {
            int scope = (int)ConfigScope.FileSystem;
            bool isRegistry = (scope & 15) != 0;

            Assert.IsFalse(isRegistry, "FileSystem (64) should not be detected as registry scope");
        }
        #endregion

        #region FileScopeBitMask
        [TestMethod]
        public void FileScopeBitMask_FileUserLocal_Bit16()
        {
            int scope = (int)ConfigScope.FileUserLocal;
            Assert.IsTrue((scope & 16) != 0, "FileUserLocal should match bit 16");
            Assert.IsFalse((scope & 32) != 0, "FileUserLocal should not match bit 32");
            Assert.IsFalse((scope & 64) != 0, "FileUserLocal should not match bit 64");
        }

        [TestMethod]
        public void FileScopeBitMask_FileUserShared_Bit32()
        {
            int scope = (int)ConfigScope.FileUserShared;
            Assert.IsFalse((scope & 16) != 0, "FileUserShared should not match bit 16");
            Assert.IsTrue((scope & 32) != 0, "FileUserShared should match bit 32");
            Assert.IsFalse((scope & 64) != 0, "FileUserShared should not match bit 64");
        }

        [TestMethod]
        public void FileScopeBitMask_FileSystem_Bit64()
        {
            int scope = (int)ConfigScope.FileSystem;
            Assert.IsFalse((scope & 16) != 0, "FileSystem should not match bit 16");
            Assert.IsFalse((scope & 32) != 0, "FileSystem should not match bit 32");
            Assert.IsTrue((scope & 64) != 0, "FileSystem should match bit 64");
        }
        #endregion

        #region PathHelpers
        [TestMethod]
        public void GetPsVersionName_ReturnsNonEmpty()
        {
            string result = ExportDbatoolsConfigCommand.GetPsVersionName();

            Assert.IsFalse(String.IsNullOrEmpty(result), "PS version name should not be empty");
            Assert.IsTrue(
                result == "PowerShell" || result == "WindowsPowerShell",
                String.Format("Expected 'PowerShell' or 'WindowsPowerShell', got '{0}'", result));
        }

        [TestMethod]
        public void GetFileUserLocalPath_ReturnsNonEmptyPath()
        {
            string path = ExportDbatoolsConfigCommand.GetFileUserLocalPath();

            Assert.IsFalse(String.IsNullOrEmpty(path), "FileUserLocal path should not be empty");
            Assert.IsTrue(path.Contains("dbatools"), String.Format("Path should contain 'dbatools', got '{0}'", path));
        }

        [TestMethod]
        public void GetFileUserSharedPath_ReturnsNonEmptyPath()
        {
            string path = ExportDbatoolsConfigCommand.GetFileUserSharedPath();

            Assert.IsFalse(String.IsNullOrEmpty(path), "FileUserShared path should not be empty");
            Assert.IsTrue(path.Contains("dbatools"), String.Format("Path should contain 'dbatools', got '{0}'", path));
        }

        [TestMethod]
        public void GetFileSystemPath_ReturnsNonEmptyPath()
        {
            string path = ExportDbatoolsConfigCommand.GetFileSystemPath();

            Assert.IsFalse(String.IsNullOrEmpty(path), "FileSystem path should not be empty");
            Assert.IsTrue(path.Contains("dbatools"), String.Format("Path should contain 'dbatools', got '{0}'", path));
        }

        [TestMethod]
        public void GetFileUserLocalPath_ContainsPsVersionName()
        {
            string psVersionName = ExportDbatoolsConfigCommand.GetPsVersionName();
            string path = ExportDbatoolsConfigCommand.GetFileUserLocalPath();

            Assert.IsTrue(
                path.IndexOf(psVersionName, StringComparison.OrdinalIgnoreCase) >= 0,
                String.Format("FileUserLocal path should contain '{0}', got '{1}'", psVersionName, path));
        }
        #endregion
    }
}
