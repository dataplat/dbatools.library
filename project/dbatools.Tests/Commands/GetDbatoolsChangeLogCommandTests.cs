using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class GetDbatoolsChangeLogCommandTests
    {
        #region ChangeLogUrl
        [TestMethod]
        public void ChangeLogUrl_IsGitHubReleasesUrl()
        {
            // Arrange & Act
            string url = Dataplat.Dbatools.Commands.GetDbatoolsChangeLogCommand.ChangeLogUrl;

            // Assert
            Assert.AreEqual("https://github.com/dataplat/dbatools/releases", url);
        }

        [TestMethod]
        public void ChangeLogUrl_IsNotNullOrEmpty()
        {
            // Arrange & Act
            string url = Dataplat.Dbatools.Commands.GetDbatoolsChangeLogCommand.ChangeLogUrl;

            // Assert
            Assert.IsFalse(String.IsNullOrEmpty(url), "ChangeLogUrl must not be null or empty");
        }

        [TestMethod]
        public void ChangeLogUrl_StartsWithHttps()
        {
            // Arrange & Act
            string url = Dataplat.Dbatools.Commands.GetDbatoolsChangeLogCommand.ChangeLogUrl;

            // Assert
            Assert.IsTrue(url.StartsWith("https://", StringComparison.OrdinalIgnoreCase),
                "ChangeLogUrl should use HTTPS");
        }
        #endregion

        #region OpenUrl
        [TestMethod]
        public void OpenUrl_NullUrl_ThrowsException()
        {
            // Verify that passing null to OpenUrl throws an exception
            // Process.Start with null FileName throws InvalidOperationException
            Assert.ThrowsException<InvalidOperationException>(() =>
            {
                Dataplat.Dbatools.Commands.GetDbatoolsChangeLogCommand.OpenUrl(null);
            });
        }
        #endregion

        // Note: limited unit test coverage - command is primarily a browser-launch
        // wrapper. ProcessRecord behavior (branching on Local switch, calling OpenUrl)
        // requires a PSCmdlet runtime and cannot be unit tested without it.
    }
}
