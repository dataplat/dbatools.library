using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class NewDbatoolsSupportPackageCommandTests
    {
        #region BuildGetVariableScript
        [TestMethod]
        public void BuildGetVariableScript_SingleVariable_ReturnsCorrectScript()
        {
            // Arrange
            string[] variables = new string[] { "myVar" };

            // Act
            string result = NewDbatoolsSupportPackageCommand.BuildGetVariableScript(variables);

            // Assert
            Assert.AreEqual("'myVar' | Get-Variable -ErrorAction Ignore", result);
        }

        [TestMethod]
        public void BuildGetVariableScript_MultipleVariables_ReturnsCommaSeparated()
        {
            // Arrange
            string[] variables = new string[] { "var1", "var2", "var3" };

            // Act
            string result = NewDbatoolsSupportPackageCommand.BuildGetVariableScript(variables);

            // Assert
            Assert.AreEqual("'var1','var2','var3' | Get-Variable -ErrorAction Ignore", result);
        }

        [TestMethod]
        public void BuildGetVariableScript_NullInput_ReturnsNull()
        {
            // Act
            string result = NewDbatoolsSupportPackageCommand.BuildGetVariableScript(null);

            // Assert
            Assert.AreEqual("$null", result);
        }

        [TestMethod]
        public void BuildGetVariableScript_EmptyArray_ReturnsNull()
        {
            // Act
            string result = NewDbatoolsSupportPackageCommand.BuildGetVariableScript(new string[0]);

            // Assert
            Assert.AreEqual("$null", result);
        }

        [TestMethod]
        public void BuildGetVariableScript_VariableWithSingleQuote_EscapesCorrectly()
        {
            // Arrange - variable names with single quotes should be escaped
            string[] variables = new string[] { "my'Var" };

            // Act
            string result = NewDbatoolsSupportPackageCommand.BuildGetVariableScript(variables);

            // Assert
            Assert.AreEqual("'my''Var' | Get-Variable -ErrorAction Ignore", result);
        }
        #endregion BuildGetVariableScript
    }
}
