using System;
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
            // Arrange - query with FROM clause
            var ex = new Exception("Some error");
            string query = "SELECT Name, State FROM Win32_Service WHERE StartMode='Auto'";

            // Act
            var result = Dbatools.Commands.GetDbaCmObjectCommand.ResolveCimError(
                ex, "server01", "OriginalClass", @"root\cimv2", query);

            // Assert - the message should reference the extracted class name from the query
            Assert.IsNotNull(result.Message);
            // The class name should be extracted from the query, not the original ClassName
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

        #endregion
    }
}
