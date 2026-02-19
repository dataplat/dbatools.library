using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class InvokeDbatoolsRenameHelperCommandTests
    {
        #region GetParamRenames
        [TestMethod]
        public void GetParamRenames_ReturnsExpectedCount()
        {
            var renames = Dbatools.Commands.InvokeDbatoolsRenameHelperCommand.GetParamRenames();
            Assert.AreEqual(18, renames.Count, "Should contain 18 parameter renames");
        }

        [TestMethod]
        public void GetParamRenames_ContainsKnownEntries()
        {
            var renames = Dbatools.Commands.InvokeDbatoolsRenameHelperCommand.GetParamRenames();

            Assert.AreEqual("ExcludeSystem", renames["ExcludeAllSystemDb"]);
            Assert.AreEqual("ExcludeUser", renames["ExcludeAllUserDb"]);
            Assert.AreEqual("SharedPath", renames["NetworkShare"]);
            Assert.AreEqual("UseLastBackup", renames["UseLastBackups"]);
            Assert.AreEqual("SqlInstance", renames["ServerInstance"]);
            Assert.AreEqual("ExcludeSystemLogins", renames["NoSystemLogins"]);
        }

        [TestMethod]
        public void GetParamRenames_ContainsInvokeSqlcmd2()
        {
            // This command rename is intentionally in the param renames dictionary (PS1 quirk)
            var renames = Dbatools.Commands.InvokeDbatoolsRenameHelperCommand.GetParamRenames();
            Assert.AreEqual("Invoke-DbaQuery", renames["Invoke-Sqlcmd2"]);
        }
        #endregion

        #region GetCommandRenames
        [TestMethod]
        public void GetCommandRenames_ReturnsNonEmptyDictionary()
        {
            var renames = Dbatools.Commands.InvokeDbatoolsRenameHelperCommand.GetCommandRenames();
            Assert.IsTrue(renames.Count > 200, String.Format("Should contain over 200 command renames, got {0}", renames.Count));
        }

        [TestMethod]
        public void GetCommandRenames_ContainsKnownEntries()
        {
            var renames = Dbatools.Commands.InvokeDbatoolsRenameHelperCommand.GetCommandRenames();

            Assert.AreEqual("Find-DbaDbDuplicateIndex", renames["Find-DbaDuplicateIndex"]);
            Assert.AreEqual("Connect-DbaInstance", renames["Connect-DbaServer"]);
            Assert.AreEqual("Export-DbaUser", renames["Export-SqlUser"]);
            Assert.AreEqual("Copy-DbaLogin", renames["Copy-SqlLogin"]);
            Assert.AreEqual("Get-DbaDbBackupHistory", renames["Get-DbaBackupHistory"]);
            Assert.AreEqual("Repair-DbaInstanceName", renames["Test-DbaInstanceName"]);
        }

        [TestMethod]
        public void GetCommandRenames_ContainsCmsEntries()
        {
            var renames = Dbatools.Commands.InvokeDbatoolsRenameHelperCommand.GetCommandRenames();

            Assert.AreEqual("Add-DbaRegServer", renames["Add-DbaCmsRegServer"]);
            Assert.AreEqual("Get-DbaRegServer", renames["Get-DbaCmsRegServer"]);
            Assert.AreEqual("Remove-DbaRegServer", renames["Remove-DbaCmsRegServer"]);
        }

        [TestMethod]
        public void GetCommandRenames_ContainsServerToInstanceRenames()
        {
            var renames = Dbatools.Commands.InvokeDbatoolsRenameHelperCommand.GetCommandRenames();

            Assert.AreEqual("Copy-DbaInstanceAuditSpecification", renames["Copy-DbaServerAuditSpecification"]);
            Assert.AreEqual("Copy-DbaInstanceAudit", renames["Copy-DbaServerAudit"]);
            Assert.AreEqual("Get-DbaInstanceTrigger", renames["Get-DbaServerTrigger"]);
            Assert.AreEqual("Get-DbaInstanceAudit", renames["Get-DbaServerAudit"]);
        }
        #endregion

        #region DictionaryIntegrity
        [TestMethod]
        public void ParamAndCommandRenames_HaveNoDuplicateKeys()
        {
            var paramRenames = Dbatools.Commands.InvokeDbatoolsRenameHelperCommand.GetParamRenames();
            var commandRenames = Dbatools.Commands.InvokeDbatoolsRenameHelperCommand.GetCommandRenames();

            // Keys should be unique within each dictionary (enforced by Dictionary<> constructor)
            // Verify no overlap between the two dictionaries
            var allKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in paramRenames.Keys)
            {
                Assert.IsTrue(allKeys.Add(key), String.Format("Duplicate key found: {0}", key));
            }
            foreach (var key in commandRenames.Keys)
            {
                Assert.IsTrue(allKeys.Add(key), String.Format("Duplicate key found across dictionaries: {0}", key));
            }
        }

        [TestMethod]
        public void Renames_ValuesAreNotNullOrEmpty()
        {
            var paramRenames = Dbatools.Commands.InvokeDbatoolsRenameHelperCommand.GetParamRenames();
            var commandRenames = Dbatools.Commands.InvokeDbatoolsRenameHelperCommand.GetCommandRenames();

            foreach (var entry in paramRenames)
            {
                Assert.IsFalse(String.IsNullOrEmpty(entry.Value),
                    String.Format("Param rename value for '{0}' should not be null or empty", entry.Key));
            }
            foreach (var entry in commandRenames)
            {
                Assert.IsFalse(String.IsNullOrEmpty(entry.Value),
                    String.Format("Command rename value for '{0}' should not be null or empty", entry.Key));
            }
        }
        #endregion
    }
}
