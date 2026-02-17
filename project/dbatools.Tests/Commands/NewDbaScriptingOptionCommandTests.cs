using System;
using Microsoft.SqlServer.Management.Smo;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class NewDbaScriptingOptionCommandTests
    {
        // Note: limited unit test coverage - command is primarily an SMO wrapper
        // that creates a new ScriptingOptions object with no parameters.

        #region ScriptingOptions Construction
        [TestMethod]
        public void ScriptingOptions_NewInstance_IsNotNull()
        {
            var options = new ScriptingOptions();
            Assert.IsNotNull(options);
        }

        [TestMethod]
        public void ScriptingOptions_NewInstance_ScriptDropsDefaultsFalse()
        {
            var options = new ScriptingOptions();
            Assert.IsFalse(options.ScriptDrops, "ScriptDrops should default to false");
        }

        [TestMethod]
        public void ScriptingOptions_NewInstance_WithDependenciesDefaultsFalse()
        {
            var options = new ScriptingOptions();
            Assert.IsFalse(options.WithDependencies, "WithDependencies should default to false");
        }

        [TestMethod]
        public void ScriptingOptions_NewInstance_IndexesDefaultsFalse()
        {
            var options = new ScriptingOptions();
            Assert.IsFalse(options.Indexes, "Indexes should default to false");
        }

        [TestMethod]
        public void ScriptingOptions_PropertiesAreSettable()
        {
            var options = new ScriptingOptions();
            options.ScriptDrops = true;
            options.WithDependencies = true;
            options.AgentAlertJob = true;
            options.AgentNotify = true;

            Assert.IsTrue(options.ScriptDrops);
            Assert.IsTrue(options.WithDependencies);
            Assert.IsTrue(options.AgentAlertJob);
            Assert.IsTrue(options.AgentNotify);
        }
        #endregion
    }
}
