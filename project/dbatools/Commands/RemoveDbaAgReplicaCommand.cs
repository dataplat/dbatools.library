using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Removes secondary replicas from SQL Server Availability Groups by calling the Drop() method
    /// on the replica object. Supports pipeline input from Get-DbaAgReplica for batch operations.
    /// </summary>
    [Cmdlet("Remove", "DbaAgReplica", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.High)]
    public class RemoveDbaAgReplicaCommand : DbaBaseCmdlet
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
        /// Specifies the availability group(s) containing the replicas to remove. Accepts wildcards for pattern matching.
        /// </summary>
        [Parameter()]
        public string[] AvailabilityGroup { get; set; }

        /// <summary>
        /// Specifies the name(s) of the availability group replicas to remove. Accepts wildcards for pattern matching.
        /// Required when using the SqlInstance parameter.
        /// </summary>
        [Parameter()]
        public string[] Replica { get; set; }

        /// <summary>
        /// Accepts availability group replica objects from the pipeline, typically from Get-DbaAgReplica.
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        public object[] InputObject { get; set; }

        #endregion Parameters

        #region Static ScriptBlocks

        /// <summary>
        /// Calls Get-DbaAgReplica with appropriate parameters.
        /// </summary>
        private static readonly ScriptBlock _getDbaAgReplicaScript = ScriptBlock.Create(@"
param($si, $sc, $replica, $ag, $hasCred, $hasReplica, $hasAg)
$params = @{ SqlInstance = $si }
if ($hasCred) { $params['SqlCredential'] = $sc }
if ($hasReplica) { $params['Replica'] = $replica }
if ($hasAg) { $params['AvailabilityGroup'] = $ag }
Get-DbaAgReplica @params
");

        /// <summary>
        /// Gets the server name from the replica's AG parent hierarchy.
        /// AvailabilityReplica.Parent = AvailabilityGroup, AvailabilityGroup.Parent = Server.
        /// </summary>
        private static readonly ScriptBlock _getServerNameScript = ScriptBlock.Create(@"
param($replica) $replica.Parent.Parent.Name
");

        /// <summary>
        /// Drops the replica from the availability group.
        /// </summary>
        private static readonly ScriptBlock _dropReplicaScript = ScriptBlock.Create(@"
param($replica)
$replica.Drop()
");

        /// <summary>
        /// Gets the AG name from the replica's parent.
        /// </summary>
        private static readonly ScriptBlock _getAgNameScript = ScriptBlock.Create(@"
param($replica) $replica.Parent.Name
");

        #endregion Static ScriptBlocks

        /// <summary>
        /// Processes each pipeline item or SqlInstance-based query to remove replicas from availability groups.
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

            // Validate: SqlInstance requires Replica
            // PS1: if ($SqlInstance -and -not $Replica)
            if (TestBound("SqlInstance") && TestBoundNot("Replica"))
            {
                StopFunction("You must specify a replica when using the SqlInstance parameter.");
                return;
            }

            // If SqlInstance provided, fetch replicas and add to InputObject
            // PS1: $InputObject += Get-DbaAgReplica -SqlInstance $SqlInstance -SqlCredential $SqlCredential -Replica $Replica -AvailabilityGroup $AvailabilityGroup
            if (TestBound("SqlInstance"))
            {
                List<object> items = new List<object>();
                if (InputObject != null)
                {
                    items.AddRange(InputObject);
                }

                Collection<PSObject> replicas = GetDbaAgReplica();
                if (replicas != null)
                {
                    foreach (PSObject replica in replicas)
                    {
                        if (replica != null)
                        {
                            items.Add(replica.BaseObject ?? replica);
                        }
                    }
                }
                InputObject = items.ToArray();
            }

            if (InputObject == null)
                return;

            foreach (object replicaObj in InputObject)
            {
                if (replicaObj == null)
                    continue;
                ProcessReplica(replicaObj);
            }
        }

        /// <summary>
        /// Processes a single replica object: validates via ShouldProcess, drops from AG, and outputs result.
        /// </summary>
        private void ProcessReplica(object replicaObj)
        {
            PSObject replica = PSObject.AsPSObject(replicaObj);
            string replicaName = GetPropertyString(replica, "Name");
            string serverName = GetServerName(replicaObj);

            if (ShouldProcess(serverName ?? replicaName ?? "Unknown",
                String.Format("Removing availability group replica {0}", replicaName ?? "Unknown")))
            {
                try
                {
                    // Get AG name: prefer NoteProperty from Get-DbaAgReplica, fall back to Parent.Name.
                    // PS1 uses $agreplica.Parent.AvailabilityGroup which is null on raw SMO objects
                    // (AvailabilityGroup doesn't exist as a property on the SMO AG type).
                    string agName = GetPropertyString(replica, "AvailabilityGroup")
                        ?? GetAgName(replicaObj);

                    DropReplica(replicaObj);

                    PSObject output = new PSObject();
                    output.Properties.Add(new PSNoteProperty("ComputerName", GetPropertyString(replica, "ComputerName")));
                    output.Properties.Add(new PSNoteProperty("InstanceName", GetPropertyString(replica, "InstanceName")));
                    output.Properties.Add(new PSNoteProperty("SqlInstance", GetPropertyString(replica, "SqlInstance")));
                    output.Properties.Add(new PSNoteProperty("AvailabilityGroup", agName));
                    output.Properties.Add(new PSNoteProperty("Replica", replicaName));
                    output.Properties.Add(new PSNoteProperty("Status", "Removed"));
                    WriteObject(output);
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("Failed to remove replica {0}", replicaName),
                        errorRecord: new ErrorRecord(ex, "RemoveDbaAgReplica", ErrorCategory.InvalidOperation, replicaObj),
                        target: replicaObj, isContinue: true);
                    TestFunctionInterrupt();
                }
            }
        }

        #region Helpers

        /// <summary>
        /// Gets the server name from the replica's parent hierarchy via ScriptBlock.
        /// </summary>
        private string GetServerName(object replicaObj)
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, _getServerNameScript, null, new object[] { replicaObj });
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
        /// Gets the AG name from the replica's parent.
        /// </summary>
        private string GetAgName(object replicaObj)
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, _getAgNameScript, null, new object[] { replicaObj });
                if (results != null && results.Count > 0 && results[0] != null)
                    return results[0].BaseObject as string ?? results[0].ToString();
            }
            catch (Exception)
            {
                // Object may not have the expected hierarchy
            }
            return null;
        }

        /// <summary>
        /// Drops the replica from the availability group.
        /// </summary>
        private void DropReplica(object replicaObj)
        {
            InvokeCommand.InvokeScript(
                false, _dropReplicaScript, null, new object[] { replicaObj });
        }

        /// <summary>
        /// Calls Get-DbaAgReplica with the current parameters.
        /// </summary>
        private Collection<PSObject> GetDbaAgReplica()
        {
            try
            {
                return InvokeCommand.InvokeScript(true, _getDbaAgReplicaScript, null,
                    new object[]
                    {
                        SqlInstance,
                        SqlCredential,
                        Replica,
                        AvailabilityGroup,
                        SqlCredential != null,
                        Replica != null && Replica.Length > 0,
                        TestBound("AvailabilityGroup")
                    });
            }
            catch (Exception ex)
            {
                StopFunction(
                    "Failed to get availability group replicas",
                    errorRecord: new ErrorRecord(ex, "RemoveDbaAgReplica_GetReplica", ErrorCategory.ConnectionError, SqlInstance),
                    target: SqlInstance, isContinue: true);
                TestFunctionInterrupt();
                return null;
            }
        }

        #endregion Helpers
    }
}
