using System;
using System.Linq;
using System.Management.Automation;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;
using Dataplat.Dbatools.Configuration;

namespace Dataplat.Dbatools.Tests.Commands
{
    // Note: limited unit test coverage - command is primarily a wrapper that invokes
    // Set-DbatoolsConfig and Register-DbatoolsConfig via InvokeScript.
    [TestClass]
    public class SetDbatoolsInsecureConnectionCommandTests
    {
        #region CmdletAttribute
        [TestMethod]
        public void CmdletAttribute_HasCorrectVerbAndNoun()
        {
            // Arrange
            var cmdletAttr = typeof(SetDbatoolsInsecureConnectionCommand)
                .GetCustomAttribute<CmdletAttribute>();

            // Assert
            Assert.IsNotNull(cmdletAttr, "CmdletAttribute should be present");
            Assert.AreEqual("Set", cmdletAttr.VerbName);
            Assert.AreEqual("DbatoolsInsecureConnection", cmdletAttr.NounName);
        }

        [TestMethod]
        public void CmdletAttribute_DoesNotDeclareShouldProcess()
        {
            // The PS1 suppresses ShouldProcess with SuppressMessageAttribute.
            // The C# cmdlet should NOT declare SupportsShouldProcess.
            var cmdletAttr = typeof(SetDbatoolsInsecureConnectionCommand)
                .GetCustomAttribute<CmdletAttribute>();

            Assert.IsNotNull(cmdletAttr);
            Assert.IsFalse(cmdletAttr.SupportsShouldProcess,
                "SupportsShouldProcess should be false (matches PS1 behavior)");
        }
        #endregion

        #region ParameterValidation
        [TestMethod]
        public void SessionOnly_IsSwitch_NotMandatory()
        {
            // Arrange
            var prop = typeof(SetDbatoolsInsecureConnectionCommand)
                .GetProperty("SessionOnly");
            var paramAttr = prop.GetCustomAttribute<ParameterAttribute>();

            // Assert
            Assert.IsNotNull(prop, "SessionOnly property should exist");
            Assert.AreEqual(typeof(SwitchParameter), prop.PropertyType);
            Assert.IsNotNull(paramAttr, "ParameterAttribute should be present");
            Assert.IsFalse(paramAttr.Mandatory, "SessionOnly should not be mandatory");
        }

        [TestMethod]
        public void Scope_DefaultsToUserDefault()
        {
            // Arrange
            var cmd = new SetDbatoolsInsecureConnectionCommand();

            // Act
            var scopeProp = typeof(SetDbatoolsInsecureConnectionCommand)
                .GetProperty("Scope");
            var scopeValue = (ConfigScope)scopeProp.GetValue(cmd);

            // Assert
            Assert.AreEqual(ConfigScope.UserDefault, scopeValue,
                "Scope should default to UserDefault");
        }

        [TestMethod]
        public void Register_IsSwitch_NotMandatory()
        {
            // Arrange
            var prop = typeof(SetDbatoolsInsecureConnectionCommand)
                .GetProperty("Register");
            var paramAttr = prop.GetCustomAttribute<ParameterAttribute>();

            // Assert
            Assert.IsNotNull(prop, "Register property should exist");
            Assert.AreEqual(typeof(SwitchParameter), prop.PropertyType);
            Assert.IsNotNull(paramAttr, "ParameterAttribute should be present");
            Assert.IsFalse(paramAttr.Mandatory, "Register should not be mandatory");
        }

        [TestMethod]
        public void HasAllThreeExpectedParameters()
        {
            // Verify all parameters from the PS1 are present in the C# cmdlet
            var type = typeof(SetDbatoolsInsecureConnectionCommand);
            var expectedParams = new[] { "SessionOnly", "Scope", "Register" };

            foreach (var paramName in expectedParams)
            {
                var prop = type.GetProperty(paramName);
                Assert.IsNotNull(prop, String.Format("Parameter {0} should exist", paramName));
                var paramAttr = prop.GetCustomAttribute<ParameterAttribute>();
                Assert.IsNotNull(paramAttr, String.Format("Parameter {0} should have ParameterAttribute", paramName));
            }
        }
        #endregion

        #region Inheritance
        [TestMethod]
        public void InheritsDbaBaseCmdlet()
        {
            // The command does NOT use SqlInstance, so it should inherit DbaBaseCmdlet, not DbaInstanceCmdlet
            Assert.IsTrue(typeof(DbaBaseCmdlet).IsAssignableFrom(typeof(SetDbatoolsInsecureConnectionCommand)),
                "Should inherit from DbaBaseCmdlet");
            Assert.IsFalse(typeof(DbaInstanceCmdlet).IsAssignableFrom(typeof(SetDbatoolsInsecureConnectionCommand)),
                "Should NOT inherit from DbaInstanceCmdlet");
        }
        #endregion
    }
}
