using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Removes availability groups from SQL Server instances using DROP AVAILABILITY GROUP.
    /// This is typically used when decommissioning high availability setups, migrating to different
    /// solutions, or cleaning up test environments. Supports pipeline input from Get-DbaAvailabilityGroup.
    /// </summary>
    [Cmdlet("Remove", "DbaAvailabilityGroup", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.High)]
    public class RemoveDbaAvailabilityGroupCommand : DbaBaseCmdlet
    {
        #region Parameters

        /// <summary>
        /// The target SQL Server instance or instances. Server version must be SQL Server version 2012 or higher.
        /// </summary>
        [Parameter()]
        public DbaInstanceParameter[] SqlInstance { get; set; }

        /// <summary>
        /// Login to the target instance using alternative credentials. Accepts PowerShell credentials (Get-Credential).
        /// </summary>
        [Parameter()]
        public PSCredential SqlCredential { get; set; }

        /// <summary>
        /// Specifies the name(s) of specific availability groups to remove. Accepts multiple values and wildcards.
        /// </summary>
        [Parameter()]
        public string[] AvailabilityGroup { get; set; }

        /// <summary>
        /// Removes all availability groups found on the specified SQL Server instance.
        /// </summary>
        [Parameter()]
        public SwitchParameter AllAvailabilityGroups { get; set; }

        /// <summary>
        /// Accepts availability group objects from Get-DbaAvailabilityGroup for pipeline operations.
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        public object[] InputObject { get; set; }

        #endregion Parameters

        #region Static ScriptBlocks

        /// <summary>
        /// Calls Get-DbaAvailabilityGroup with appropriate parameters.
        /// </summary>
        private static readonly ScriptBlock _getDbaAvailabilityGroupScript = ScriptBlock.Create(@"
param($si, $sc, $ag, $hasCred, $hasAg)
$params = @{ SqlInstance = $si }
if ($hasCred) { $params['SqlCredential'] = $sc }
if ($hasAg) { $params['AvailabilityGroup'] = $ag }
Get-DbaAvailabilityGroup @params
");

        /// <summary>
        /// Gets the server name from the AG's parent hierarchy.
        /// AvailabilityGroup.Parent = Server.
        /// </summary>
        private static readonly ScriptBlock _getServerNameScript = ScriptBlock.Create(@"
param($ag) $ag.Parent.Name
");

        /// <summary>
        /// Drops the availability group using T-SQL (avoids SMO enumeration issues).
        /// Uses bracket-quoted name to prevent SQL injection.
        /// PS1 uses: $ag.Parent.Query("DROP AVAILABILITY GROUP $ag")
        /// </summary>
        private static readonly ScriptBlock _dropAvailabilityGroupScript = ScriptBlock.Create(@"
param($ag)
$agName = $ag.Name -replace '\]', ']]'
$null = $ag.Parent.Query(""DROP AVAILABILITY GROUP [$agName]"")
");

        #endregion Static ScriptBlocks

        /// <summary>
        /// Processes each pipeline item or SqlInstance-based query to remove availability groups.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (TestFunctionInterrupt())
                return;

            // Validate: need SqlInstance or InputObject
            if (TestBoundNot("SqlInstance", "InputObject"))
            {
                StopFunction("You must supply either -SqlInstance or an Input Object");
                return;
            }

            // Validate: SqlInstance requires AvailabilityGroup or AllAvailabilityGroups
            if (TestBound("SqlInstance") && TestBoundNot("AvailabilityGroup", "AllAvailabilityGroups"))
            {
                StopFunction("You must specify AllAvailabilityGroups groups or AvailabilityGroups when using the SqlInstance parameter.");
                return;
            }

            // If SqlInstance provided, fetch AGs and add to InputObject
            // PS1: $InputObject += Get-DbaAvailabilityGroup -SqlInstance $SqlInstance -SqlCredential $SqlCredential -AvailabilityGroup $AvailabilityGroup
            if (TestBound("SqlInstance"))
            {
                List<object> items = new List<object>();
                if (InputObject != null)
                {
                    items.AddRange(InputObject);
                }

                Collection<PSObject> ags = GetDbaAvailabilityGroup();
                if (ags != null)
                {
                    foreach (PSObject ag in ags)
                    {
                        if (ag != null)
                        {
                            items.Add(ag.BaseObject ?? ag);
                        }
                    }
                }
                InputObject = items.ToArray();
            }

            if (InputObject == null)
                return;

            foreach (object agObj in InputObject)
            {
                if (agObj == null)
                    continue;
                ProcessAvailabilityGroup(agObj);
            }
        }

        /// <summary>
        /// Processes a single AG object: validates via ShouldProcess, drops the AG, and outputs result.
        /// </summary>
        private void ProcessAvailabilityGroup(object agObj)
        {
            PSObject ag = PSObject.AsPSObject(agObj);
            string agName = GetPropertyString(ag, "Name");
            string serverName = GetServerName(agObj);

            // PS1: if ($Pscmdlet.ShouldProcess($ag.Parent.Name, "Removing availability group $ag"))
            if (ShouldProcess(serverName ?? agName ?? "Unknown",
                String.Format("Removing availability group {0}", agName ?? "Unknown")))
            {
                try
                {
                    DropAvailabilityGroup(agObj);

                    PSObject output = new PSObject();
                    output.Properties.Add(new PSNoteProperty("ComputerName", GetPropertyString(ag, "ComputerName")));
                    output.Properties.Add(new PSNoteProperty("InstanceName", GetPropertyString(ag, "InstanceName")));
                    output.Properties.Add(new PSNoteProperty("SqlInstance", GetPropertyString(ag, "SqlInstance")));
                    output.Properties.Add(new PSNoteProperty("AvailabilityGroup", agName));
                    output.Properties.Add(new PSNoteProperty("Status", "Removed"));
                    WriteObject(output);
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("Failed to remove availability group {0}", agName),
                        errorRecord: new ErrorRecord(ex, "RemoveDbaAvailabilityGroup", ErrorCategory.InvalidOperation, agObj),
                        target: agObj, isContinue: true);
                    TestFunctionInterrupt();
                }
            }
        }

        #region Helpers

        /// <summary>
        /// Gets the server name from the AG's parent hierarchy via ScriptBlock.
        /// </summary>
        private string GetServerName(object agObj)
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, _getServerNameScript, null, new object[] { agObj });
                if (results != null && results.Count > 0 && results[0] != null)
                    return results[0].BaseObject as string ?? results[0].ToString();
            }
            catch (Exception ex)
            {
                WriteMessageAtLevel(
                    String.Format("Could not resolve server name from object hierarchy: {0}", ex.Message),
                    MessageLevel.Debug, null);
            }
            return null;
        }

        /// <summary>
        /// Drops the availability group using T-SQL.
        /// </summary>
        private void DropAvailabilityGroup(object agObj)
        {
            InvokeCommand.InvokeScript(
                false, _dropAvailabilityGroupScript, null, new object[] { agObj });
        }

        /// <summary>
        /// Calls Get-DbaAvailabilityGroup with the current parameters.
        /// </summary>
        private Collection<PSObject> GetDbaAvailabilityGroup()
        {
            try
            {
                return InvokeCommand.InvokeScript(false, _getDbaAvailabilityGroupScript, null,
                    new object[]
                    {
                        SqlInstance,
                        SqlCredential,
                        AvailabilityGroup,
                        SqlCredential != null,
                        TestBound("AvailabilityGroup")
                    });
            }
            catch (Exception ex)
            {
                StopFunction(
                    "Failed to get availability groups",
                    errorRecord: new ErrorRecord(ex, "RemoveDbaAvailabilityGroup_GetAg", ErrorCategory.ConnectionError, SqlInstance),
                    target: SqlInstance, isContinue: true);
                TestFunctionInterrupt();
                return null;
            }
        }

        #endregion Helpers
    }
}
