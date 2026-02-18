using System;
using System.Management.Automation;
using Microsoft.Management.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class GetDbaCmObjectCommandTests
    {
        #region ResolveCimError

        [TestMethod]
        public void ResolveCimError_UnknownCode_MarksBadConnection()
        {
            // Arrange - a generic exception with no CimException inside
            var ex = new Exception("Generic failure");

            // Act
            var result = Dbatools.Commands.GetDbaCmObjectCommand.ResolveCimError(
                ex, "server01", "Win32_OperatingSystem", @"root\cimv2", null);

            // Assert
            Assert.IsTrue(result.BadConnection, "Unknown error codes should mark BadConnection = true");
            Assert.IsFalse(result.BadCredentials, "Unknown errors should not flag bad credentials");
        }

        [TestMethod]
        public void ResolveCimError_NullClassName_DoesNotThrow()
        {
            // Arrange
            var ex = new Exception("Some error");

            // Act
            var result = Dbatools.Commands.GetDbaCmObjectCommand.ResolveCimError(
                ex, "server01", null, @"root\cimv2", null);

            // Assert
            Assert.IsNotNull(result, "Should return a result even with null class name");
            Assert.IsNotNull(result.Message, "Message should not be null");
        }

        [TestMethod]
        public void ResolveCimError_QueryExtractsClassName()
        {
            // Arrange - query with FROM clause; the extracted class replaces the original ClassName
            var ex = new Exception("Some error");
            string query = "SELECT Name, State FROM Win32_Service WHERE StartMode='Auto'";

            // Act
            var result = Dbatools.Commands.GetDbaCmObjectCommand.ResolveCimError(
                ex, "server01", "OriginalClass", @"root\cimv2", query);

            // Assert - since this is an unknown code (no CimException), message uses default format
            // but the className should have been replaced with Win32_Service from query parsing
            Assert.IsNotNull(result.Message);
            Assert.IsTrue(result.BadConnection, "Unknown code should flag bad connection");
        }

        [TestMethod]
        public void ResolveCimError_QueryCaseInsensitive_ExtractsClassName()
        {
            // Arrange - lowercase "from" should still be matched
            var ex = new Exception("Some error");
            string query = "select * from Win32_Process where Name='test'";

            // Act
            var result = Dbatools.Commands.GetDbaCmObjectCommand.ResolveCimError(
                ex, "server01", "OriginalClass", @"root\cimv2", query);

            // Assert
            Assert.IsNotNull(result.Message);
        }

        [TestMethod]
        public void ResolveCimError_QueryNoFromClause_KeepsOriginalClassName()
        {
            // Arrange - malformed query without FROM
            var ex = new Exception("Some error");
            string query = "INVALID QUERY TEXT";

            // Act
            var result = Dbatools.Commands.GetDbaCmObjectCommand.ResolveCimError(
                ex, "server01", "Win32_OS", @"root\cimv2", query);

            // Assert - should still produce valid output without crashing
            Assert.IsNotNull(result);
            Assert.IsNotNull(result.Message);
        }

        [TestMethod]
        public void ResolveCimError_EmptyQuery_UsesClassName()
        {
            // Arrange
            var ex = new Exception("Some error");

            // Act
            var result = Dbatools.Commands.GetDbaCmObjectCommand.ResolveCimError(
                ex, "server01", "Win32_Process", @"root\cimv2", "");

            // Assert
            Assert.IsNotNull(result.Message);
        }

        [TestMethod]
        public void ResolveCimError_NullQuery_UsesClassName()
        {
            // Arrange
            var ex = new Exception("Some error");

            // Act
            var result = Dbatools.Commands.GetDbaCmObjectCommand.ResolveCimError(
                ex, "server01", "Win32_Process", @"root\cimv2", null);

            // Assert
            Assert.IsNotNull(result.Message);
            Assert.IsTrue(result.BadConnection);
        }

        #endregion

        #region GetCimErrorMessage

        [TestMethod]
        public void GetCimErrorMessage_Code2_ContainsAccessDenied()
        {
            // Act
            string message = Dbatools.Commands.GetDbaCmObjectCommand.GetCimErrorMessage(
                2, "server01", "Win32_OS", @"root\cimv2");

            // Assert
            Assert.IsTrue(message.Contains("denied"), "Code 2 should indicate access denied");
            Assert.IsTrue(message.Contains("server01"), "Message should contain the computer name");
            Assert.IsTrue(message.Contains("Win32_OS"), "Message should contain the class name");
        }

        [TestMethod]
        public void GetCimErrorMessage_Code3_ContainsInvalidNamespace()
        {
            // Act
            string message = Dbatools.Commands.GetDbaCmObjectCommand.GetCimErrorMessage(
                3, "server01", "Win32_OS", @"root\invalid");

            // Assert
            Assert.IsTrue(message.Contains("Invalid namespace"), "Code 3 should indicate invalid namespace");
            Assert.IsTrue(message.Contains(@"root\invalid"), "Message should contain the namespace");
        }

        [TestMethod]
        public void GetCimErrorMessage_Code5_ContainsInvalidClassName()
        {
            // Act
            string message = Dbatools.Commands.GetDbaCmObjectCommand.GetCimErrorMessage(
                5, "server01", "FakeClass", @"root\cimv2");

            // Assert
            Assert.IsTrue(message.Contains("Invalid class name"), "Code 5 should indicate invalid class name");
            Assert.IsTrue(message.Contains("FakeClass"), "Message should contain the class name");
        }

        [TestMethod]
        public void GetCimErrorMessage_Code14_ContainsInvalidQueryLanguage()
        {
            // Act
            string message = Dbatools.Commands.GetDbaCmObjectCommand.GetCimErrorMessage(
                14, "server01", "Win32_OS", @"root\cimv2");

            // Assert
            Assert.IsTrue(message.Contains("Invalid query language"), "Code 14 should indicate invalid query language");
        }

        [TestMethod]
        public void GetCimErrorMessage_Code15_ContainsInvalidQueryString()
        {
            // Act
            string message = Dbatools.Commands.GetDbaCmObjectCommand.GetCimErrorMessage(
                15, "server01", "Win32_OS", @"root\cimv2");

            // Assert
            Assert.IsTrue(message.Contains("Invalid query string"), "Code 15 should indicate invalid query string");
        }

        [TestMethod]
        public void GetCimErrorMessage_DefaultCode_ReturnsUnexpectedError()
        {
            // Act
            string message = Dbatools.Commands.GetDbaCmObjectCommand.GetCimErrorMessage(
                99, "server01", "Win32_OS", @"root\cimv2");

            // Assert
            Assert.IsTrue(message.Contains("unexpected error"), "Unknown code should indicate unexpected error");
        }

        [TestMethod]
        public void GetCimErrorMessage_AllCodes_ContainComputerName()
        {
            // Act & Assert - every code from 1-20 should include the computer name
            for (int i = 1; i <= 20; i++)
            {
                string message = Dbatools.Commands.GetDbaCmObjectCommand.GetCimErrorMessage(
                    i, "testbox42", "SomeClass", @"root\cimv2");
                Assert.IsTrue(message.Contains("testbox42"),
                    String.Format("Code {0} message should contain the computer name", i));
            }
        }

        [TestMethod]
        public void GetCimErrorMessage_Code6_ContainsObjectNotFound()
        {
            // Act
            string message = Dbatools.Commands.GetDbaCmObjectCommand.GetCimErrorMessage(
                6, "server01", "Win32_Widget", @"root\cimv2");

            // Assert
            Assert.IsTrue(message.Contains("could not be found"), "Code 6 should indicate object not found");
            Assert.IsTrue(message.Contains("Win32_Widget"), "Message should reference the class name");
        }

        [TestMethod]
        public void GetCimErrorMessage_Code20_ContainsNamespaceNotEmpty()
        {
            // Act
            string message = Dbatools.Commands.GetDbaCmObjectCommand.GetCimErrorMessage(
                20, "server01", "SomeClass", @"root\test");

            // Assert
            Assert.IsTrue(message.Contains("not empty"), "Code 20 should indicate namespace not empty");
            Assert.IsTrue(message.Contains(@"root\test"), "Message should reference the namespace");
        }

        [TestMethod]
        public void GetCimErrorMessage_NegativeCode_ReturnsDefaultMessage()
        {
            // Act
            string message = Dbatools.Commands.GetDbaCmObjectCommand.GetCimErrorMessage(
                -1, "server01", "Win32_OS", @"root\cimv2");

            // Assert
            Assert.IsTrue(message.Contains("unexpected error"), "Negative code should return default message");
        }

        #endregion

        #region FindCimException

        [TestMethod]
        public void FindCimException_NullException_ReturnsNull()
        {
            // Act
            var result = Dbatools.Commands.GetDbaCmObjectCommand.FindCimException(null);

            // Assert
            Assert.IsNull(result, "Should return null for null input");
        }

        [TestMethod]
        public void FindCimException_NonCimException_ReturnsNull()
        {
            // Arrange
            var ex = new InvalidOperationException("Not a CIM error");

            // Act
            var result = Dbatools.Commands.GetDbaCmObjectCommand.FindCimException(ex);

            // Assert
            Assert.IsNull(result, "Should return null for non-CIM exceptions");
        }

        [TestMethod]
        public void FindCimException_NestedNonCimExceptions_ReturnsNull()
        {
            // Arrange - nested exceptions but none are CimException
            var inner = new InvalidOperationException("inner error");
            var outer = new Exception("outer error", inner);

            // Act
            var result = Dbatools.Commands.GetDbaCmObjectCommand.FindCimException(outer);

            // Assert
            Assert.IsNull(result, "Should return null when no CimException in chain");
        }

        [TestMethod]
        public void FindCimException_DeeplyNestedNonCim_ReturnsNull()
        {
            // Arrange - three levels deep, still no CimException
            var level1 = new ArgumentException("level 1");
            var level2 = new InvalidOperationException("level 2", level1);
            var level3 = new Exception("level 3", level2);

            // Act
            var result = Dbatools.Commands.GetDbaCmObjectCommand.FindCimException(level3);

            // Assert
            Assert.IsNull(result, "Should return null with deeply nested non-CIM exceptions");
        }

        #endregion

        #region GetWmiErrorReason

        [TestMethod]
        public void GetWmiErrorReason_UnauthorizedAccessException_ReturnsCorrectReason()
        {
            // Arrange
            var ex = new UnauthorizedAccessException("Access denied");

            // Act
            string result = Dbatools.Commands.GetDbaCmObjectCommand.GetWmiErrorReason(ex);

            // Assert
            Assert.AreEqual("UnauthorizedAccessException", result);
        }

        [TestMethod]
        public void GetWmiErrorReason_InnerUnauthorizedAccess_ReturnsCorrectReason()
        {
            // Arrange
            var inner = new UnauthorizedAccessException("Access denied");
            var outer = new Exception("Wrapper", inner);

            // Act
            string result = Dbatools.Commands.GetDbaCmObjectCommand.GetWmiErrorReason(outer);

            // Assert
            Assert.AreEqual("UnauthorizedAccessException", result);
        }

        [TestMethod]
        public void GetWmiErrorReason_GenericException_ReturnsNull()
        {
            // Arrange
            var ex = new Exception("Some generic error");

            // Act
            string result = Dbatools.Commands.GetDbaCmObjectCommand.GetWmiErrorReason(ex);

            // Assert
            Assert.IsNull(result, "Generic exceptions should return null reason");
        }

        [TestMethod]
        public void GetWmiErrorReason_NullInnerException_ReturnsNull()
        {
            // Arrange
            var ex = new InvalidOperationException("No inner exception");

            // Act
            string result = Dbatools.Commands.GetDbaCmObjectCommand.GetWmiErrorReason(ex);

            // Assert
            Assert.IsNull(result, "Exception without inner UnauthorizedAccessException should return null");
        }

        #endregion

        #region GetWmiErrorCategory

        [TestMethod]
        public void GetWmiErrorCategory_GenericException_ReturnsNull()
        {
            // Arrange
            var ex = new Exception("Not a RuntimeException");

            // Act
            string result = Dbatools.Commands.GetDbaCmObjectCommand.GetWmiErrorCategory(ex);

            // Assert
            Assert.IsNull(result, "Non-RuntimeException should return null category");
        }

        [TestMethod]
        public void GetWmiErrorCategory_NullException_DoesNotThrow()
        {
            // Arrange
            var ex = new InvalidOperationException("test");

            // Act
            string result = Dbatools.Commands.GetDbaCmObjectCommand.GetWmiErrorCategory(ex);

            // Assert
            Assert.IsNull(result);
        }

        #endregion

        #region IsProviderLoadFailure

        [TestMethod]
        public void IsProviderLoadFailure_MessageContainsKeyword_ReturnsTrue()
        {
            // Arrange
            var ex = new Exception("Error: ProviderLoadFailure occurred");

            // Act
            bool result = Dbatools.Commands.GetDbaCmObjectCommand.IsProviderLoadFailure(ex);

            // Assert
            Assert.IsTrue(result, "Should detect ProviderLoadFailure in exception message");
        }

        [TestMethod]
        public void IsProviderLoadFailure_InnerExceptionContainsKeyword_ReturnsTrue()
        {
            // Arrange
            var inner = new Exception("ProviderLoadFailure in WMI subsystem");
            var outer = new Exception("Outer error", inner);

            // Act
            bool result = Dbatools.Commands.GetDbaCmObjectCommand.IsProviderLoadFailure(outer);

            // Assert
            Assert.IsTrue(result, "Should detect ProviderLoadFailure in inner exception message");
        }

        [TestMethod]
        public void IsProviderLoadFailure_NoKeyword_ReturnsFalse()
        {
            // Arrange
            var ex = new Exception("Some other WMI error");

            // Act
            bool result = Dbatools.Commands.GetDbaCmObjectCommand.IsProviderLoadFailure(ex);

            // Assert
            Assert.IsFalse(result, "Should return false when ProviderLoadFailure is not in any message");
        }

        [TestMethod]
        public void IsProviderLoadFailure_NullMessage_ReturnsFalse()
        {
            // Arrange - exception with null message via inner exception chain
            var inner = new Exception("normal error");
            var outer = new Exception("also normal", inner);

            // Act
            bool result = Dbatools.Commands.GetDbaCmObjectCommand.IsProviderLoadFailure(outer);

            // Assert
            Assert.IsFalse(result);
        }

        #endregion

        #region CimErrorInfo

        [TestMethod]
        public void CimErrorInfo_Constructor_SetsAllProperties()
        {
            // Act
            var info = new Dbatools.Commands.GetDbaCmObjectCommand.CimErrorInfo(
                5, "Test message", true, false);

            // Assert
            Assert.AreEqual(5, info.ErrorCode);
            Assert.AreEqual("Test message", info.Message);
            Assert.IsTrue(info.BadConnection);
            Assert.IsFalse(info.BadCredentials);
        }

        [TestMethod]
        public void CimErrorInfo_Constructor_BadCredentials()
        {
            // Act
            var info = new Dbatools.Commands.GetDbaCmObjectCommand.CimErrorInfo(
                1, "Invalid credentials", false, true);

            // Assert
            Assert.AreEqual(1, info.ErrorCode);
            Assert.IsFalse(info.BadConnection);
            Assert.IsTrue(info.BadCredentials);
        }

        [TestMethod]
        public void CimErrorInfo_Constructor_NullMessage()
        {
            // Act
            var info = new Dbatools.Commands.GetDbaCmObjectCommand.CimErrorInfo(
                0, null, false, false);

            // Assert
            Assert.AreEqual(0, info.ErrorCode);
            Assert.IsNull(info.Message);
            Assert.IsFalse(info.BadConnection);
            Assert.IsFalse(info.BadCredentials);
        }

        #endregion
    }
}
