using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class NewDbaAgentAlertCategoryCommandTests
    {
        // Note: limited unit test coverage - command is primarily an SMO wrapper
        // that delegates to InvokeScript for all SMO operations. Testable logic
        // is limited to the IsContainedAgError helper.

        #region IsContainedAgError

        [TestMethod]
        public void IsContainedAgError_NullException_ReturnsFalse()
        {
            bool result = Dataplat.Dbatools.Commands.NewDbaAgentAlertCategoryCommand.IsContainedAgError(null);
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void IsContainedAgError_MessageContainsNewParent_ReturnsTrue()
        {
            var ex = new Exception("Value cannot be null. (Parameter 'newParent')");
            bool result = Dataplat.Dbatools.Commands.NewDbaAgentAlertCategoryCommand.IsContainedAgError(ex);
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void IsContainedAgError_MessageDoesNotContainNewParent_ReturnsFalse()
        {
            var ex = new Exception("Some other error occurred");
            bool result = Dataplat.Dbatools.Commands.NewDbaAgentAlertCategoryCommand.IsContainedAgError(ex);
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void IsContainedAgError_InnerExceptionContainsNewParent_ReturnsTrue()
        {
            var inner = new Exception("Value cannot be null. (Parameter 'newParent')");
            var outer = new Exception("Outer error", inner);
            bool result = Dataplat.Dbatools.Commands.NewDbaAgentAlertCategoryCommand.IsContainedAgError(outer);
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void IsContainedAgError_CaseInsensitive_ReturnsTrue()
        {
            var ex = new Exception("Parameter 'NEWPARENT' is null");
            bool result = Dataplat.Dbatools.Commands.NewDbaAgentAlertCategoryCommand.IsContainedAgError(ex);
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void IsContainedAgError_EmptyMessage_ReturnsFalse()
        {
            var ex = new Exception("");
            bool result = Dataplat.Dbatools.Commands.NewDbaAgentAlertCategoryCommand.IsContainedAgError(ex);
            Assert.IsFalse(result);
        }

        #endregion IsContainedAgError
    }
}
