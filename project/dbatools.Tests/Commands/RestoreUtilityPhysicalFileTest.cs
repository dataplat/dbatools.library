using System;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using Dataplat.Dbatools.Commands;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Commands.Test
{
    /// <summary>
    /// Host cmdlet for RestoreUtility.GetDbaDbPhysicalFile: the helper needs a live
    /// PSCmdlet for its Write-Message plumbing, so the test runs it inside a real
    /// in-process runspace. The Server under test targets an unreachable endpoint;
    /// note that presetting ServerConnection.ServerVersion does NOT make
    /// Server.VersionMajor resolve offline - SMO fetches version state through a live
    /// connection regardless, so every offline invocation fails at the version read.
    /// </summary>
    [Cmdlet("Test", "PhysicalFileHost")]
    public sealed class TestPhysicalFileHostCommand : DbaBaseCmdlet
    {
        protected override void ProcessRecord()
        {
            ServerConnection connection = new ServerConnection();
            connection.ServerInstance = "tcp:127.0.0.1,9";
            connection.ConnectTimeout = 1;
            Server server = new Server(connection);
            try
            {
                System.Data.DataRow[] rows = RestoreUtility.GetDbaDbPhysicalFile(this, server);
                WriteObject("ROWS:" + rows.Length);
            }
            catch (Exception ex)
            {
                WriteObject("THREW:" + ex.GetType().Name + ":" + ex.Message);
            }
        }
    }

    /// <summary>
    /// Coverage for RestoreUtility.GetDbaDbPhysicalFile, the C# parity port of
    /// private/functions/Get-DbaDbPhysicalFile.ps1. The offline-observable contract is
    /// STRUCTURAL: the fixed "Error enumerating files" mask wraps ONLY the query leg -
    /// in the PowerShell source the try covers only the $server.Query call, with the
    /// version-branch read outside it, and the port mirrors that boundary. So a failure
    /// BEFORE the query (here: the version read on an unreachable server) must surface
    /// as the raw SMO error, never as the mask. The masked query-leg wrap itself and
    /// the live result shape require a connected instance and ride the integrator gate
    /// through Test-DbaBackupInformation.
    /// </summary>
    [TestClass]
    public class RestoreUtilityPhysicalFileTest
    {
        [TestMethod]
        public void PhysicalFile_PreQueryFailureSurfacesUnmasked()
        {
            InitialSessionState iss = InitialSessionState.CreateDefault2();
            iss.Commands.Add(new SessionStateCmdletEntry("Test-PhysicalFileHost", typeof(TestPhysicalFileHostCommand), null));
            using (System.Management.Automation.Runspaces.Runspace runspace = RunspaceFactory.CreateRunspace(iss))
            {
                runspace.Open();
                using (PowerShell shell = PowerShell.Create())
                {
                    shell.Runspace = runspace;
                    shell.AddCommand("Test-PhysicalFileHost");
                    Collection<PSObject> output = shell.Invoke();
                    Assert.AreEqual(1, output.Count, "host cmdlet emits exactly one outcome record");
                    string outcome = (string)output[0].BaseObject;
                    StringAssert.StartsWith(outcome, "THREW:ConnectionFailureException:",
                        "a pre-query failure must surface as the raw SMO error");
                    Assert.IsFalse(outcome.Contains("Error enumerating files"),
                        "the fixed mask belongs to the query leg only");
                }
            }
        }
    }
}
