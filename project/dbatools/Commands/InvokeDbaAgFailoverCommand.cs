using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Performs manual failover of an availability group to make the target instance the new primary replica.
    /// Supports both safe failover and forced failover with potential data loss.
    /// </summary>
    [Cmdlet("Invoke", "DbaAgFailover", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.High)]
    public class InvokeDbaAgFailoverCommand : DbaBaseCmdlet
    {
        #region Parameters

        /// <summary>
        /// The SQL Server instance. Server version must be SQL Server version 2012 or higher.
        /// </summary>
        [Parameter()]
        public DbaInstanceParameter[] SqlInstance { get; set; }

        /// <summary>
        /// Login to the target instance using alternative credentials.
        /// </summary>
        [Parameter()]
        public PSCredential SqlCredential { get; set; }

        /// <summary>
        /// Specifies the name(s) of the availability groups to failover on the target instance.
        /// </summary>
        [Parameter()]
        public string[] AvailabilityGroup { get; set; }

        /// <summary>
        /// Accepts availability group objects from Get-DbaAvailabilityGroup for pipeline operations.
        /// Type is object[] because SMO types are loaded dynamically at runtime.
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        public object[] InputObject { get; set; }

        /// <summary>
        /// Performs a forced failover that allows potential data loss.
        /// </summary>
        [Parameter()]
        public SwitchParameter Force { get; set; }

        #endregion Parameters

        #region Static ScriptBlocks

        /// <summary>
        /// Script block for calling Get-DbaAvailabilityGroup.
        /// </summary>
        private static readonly ScriptBlock _getAgScript = ScriptBlock.Create(@"
param($si, $sc, $ag, $hasCred)
$params = @{ SqlInstance = $si }
if ($hasCred) { $params['SqlCredential'] = $sc }
if ($ag) { $params['AvailabilityGroup'] = $ag }
Get-DbaAvailabilityGroup @params
");

        /// <summary>
        /// Script block that performs graceful failover on an AG.
        /// </summary>
        private static readonly ScriptBlock _failoverScript = ScriptBlock.Create(@"
param($ag)
$ag.Failover()
$ag.Refresh()
$ag
");

        /// <summary>
        /// Script block that performs forced failover with potential data loss on an AG.
        /// </summary>
        private static readonly ScriptBlock _forceFailoverScript = ScriptBlock.Create(@"
param($ag)
$ag.FailoverWithPotentialDataLoss()
$ag.Refresh()
$ag
");

        #endregion Static ScriptBlocks

        /// <summary>
        /// Collected input objects across pipeline invocations.
        /// </summary>
        private List<object> _inputObjects = new List<object>();

        /// <summary>
        /// Sets up Force/ConfirmPreference override at the start.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            // Force suppresses confirmation prompts (PS1: if ($Force) { $ConfirmPreference = 'none' })
            if (Force.IsPresent)
            {
                SessionState.PSVariable.Set("ConfirmPreference", "None");
            }
        }

        /// <summary>
        /// Collects pipeline InputObject items and resolves SqlInstance items.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (TestFunctionInterrupt())
                return;

            // Validate: either SqlInstance or InputObject must be provided
            if (TestBoundNot("SqlInstance", "InputObject") && InputObject == null)
            {
                StopFunction("You must supply either -SqlInstance or an Input Object");
                return;
            }

            // Validate: AvailabilityGroup required when using SqlInstance
            if (TestBound("SqlInstance") && TestBoundNot("AvailabilityGroup"))
            {
                StopFunction("You must specify at least one availability group when using SqlInstance.");
                return;
            }

            // Collect InputObject from pipeline
            if (InputObject != null)
            {
                foreach (object obj in InputObject)
                {
                    if (obj != null)
                        _inputObjects.Add(obj);
                }
            }

            // Resolve from SqlInstance
            if (SqlInstance != null)
            {
                try
                {
                    Collection<PSObject> results = InvokeCommand.InvokeScript(
                        false, _getAgScript, null,
                        new object[] { SqlInstance, SqlCredential, AvailabilityGroup, SqlCredential != null });

                    if (results != null)
                    {
                        foreach (PSObject obj in results)
                        {
                            if (obj != null)
                                _inputObjects.Add(obj);
                        }
                    }
                }
                catch (Exception ex)
                {
                    StopFunction(
                        "Failed to get availability groups",
                        exception: ex, target: SqlInstance);
                    return;
                }
            }
        }

        /// <summary>
        /// Processes each collected AG and performs failover.
        /// </summary>
        protected override void EndProcessing()
        {
            if (TestFunctionInterrupt())
                return;

            foreach (object agObj in _inputObjects)
            {
                PSObject ag = PSObject.AsPSObject(agObj);
                string agName = GetPropertyString(ag, "Name");
                string serverName = GetServerName(ag);

                try
                {
                    if (Force.IsPresent)
                    {
                        if (ShouldProcess(serverName ?? "Server",
                            String.Format("Forcefully failing over {0}, allowing potential data loss", agName ?? "AG")))
                        {
                            Collection<PSObject> results = InvokeCommand.InvokeScript(
                                false, _forceFailoverScript, null, new object[] { agObj });

                            if (results != null)
                            {
                                foreach (PSObject result in results)
                                {
                                    if (result != null)
                                        WriteObject(result);
                                }
                            }
                        }
                    }
                    else
                    {
                        if (ShouldProcess(serverName ?? "Server",
                            String.Format("Gracefully failing over {0}", agName ?? "AG")))
                        {
                            Collection<PSObject> results = InvokeCommand.InvokeScript(
                                false, _failoverScript, null, new object[] { agObj });

                            if (results != null)
                            {
                                foreach (PSObject result in results)
                                {
                                    if (result != null)
                                        WriteObject(result);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("Failure failing over {0}", agName ?? "AG"),
                        errorRecord: new ErrorRecord(ex, "InvokeDbaAgFailover", ErrorCategory.InvalidOperation, agObj),
                        target: agObj, isContinue: true);
                    TestFunctionInterrupt();
                }
            }
        }

        #region Helpers

        /// <summary>
        /// Gets the server name from an AG's parent.
        /// </summary>
        private static string GetServerName(PSObject ag)
        {
            try
            {
                PSPropertyInfo parentProp = ag.Properties["Parent"];
                if (parentProp != null && parentProp.Value != null)
                {
                    PSObject parent = PSObject.AsPSObject(parentProp.Value);
                    PSPropertyInfo nameProp = parent.Properties["Name"];
                    if (nameProp != null && nameProp.Value != null)
                        return nameProp.Value.ToString();
                }
            }
            catch (Exception)
            {
                // Best effort
            }
            return null;
        }

        #endregion Helpers
    }
}
