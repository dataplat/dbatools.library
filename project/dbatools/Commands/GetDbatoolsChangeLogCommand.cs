using System;
using System.Diagnostics;
using System.Management.Automation;
using System.Runtime.InteropServices;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Opens the dbatools release changelog in the default browser.
    /// </summary>
    [Cmdlet("Get", "DbatoolsChangeLog")]
    public class GetDbatoolsChangeLogCommand : DbaBaseCmdlet
    {
        /// <summary>
        /// Attempts to display a local changelog file instead of opening the online version.
        /// This functionality has been deprecated and will display a warning message directing
        /// users to the online changelog.
        /// </summary>
        [Parameter()]
        public SwitchParameter Local { get; set; }

        /// <summary>
        /// The URL for the dbatools releases page on GitHub.
        /// </summary>
        internal const string ChangeLogUrl = "https://github.com/dataplat/dbatools/releases";

        /// <summary>
        /// Opens the changelog URL in the default browser, or shows a warning if Local is specified.
        /// </summary>
        protected override void ProcessRecord()
        {
            try
            {
                if (!Local.IsPresent)
                {
                    OpenUrl(ChangeLogUrl);
                }
                else
                {
                    WriteMessageWarning("Sorry, changelog is only available online");
                }
            }
            catch (Exception ex)
            {
                StopFunction("Failure", exception: ex);
                TestFunctionInterrupt();
                return;
            }
        }

        /// <summary>
        /// Opens a URL in the default browser, handling cross-platform differences.
        /// </summary>
        /// <param name="url">The URL to open</param>
        internal static void OpenUrl(string url)
        {
#if NETFRAMEWORK
            using (Process proc = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }))
            {
                // fire-and-forget: proc may be null if the OS reused a window
            }
#else
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                using (Process proc = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }))
                {
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                using (Process proc = Process.Start("xdg-open", url))
                {
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                using (Process proc = Process.Start("open", url))
                {
                }
            }
            else
            {
                throw new PlatformNotSupportedException(
                    String.Format("Opening a URL is not supported on this platform: {0}", Environment.OSVersion.Platform));
            }
#endif
        }
    }
}
