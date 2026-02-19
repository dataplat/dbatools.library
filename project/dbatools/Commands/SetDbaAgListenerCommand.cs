using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Modifies the port number for Availability Group listeners on SQL Server instances.
    /// Changes the TCP port that clients use to connect to the availability group.
    /// </summary>
    [Cmdlet("Set", "DbaAgListener", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.High)]
    public class SetDbaAgListenerCommand : DbaBaseCmdlet
    {
        #region Parameters

        /// <summary>
        /// The target SQL Server instance or instances. Server version must be SQL Server version 2012 or higher.
        /// </summary>
        [Parameter()]
        public DbaInstanceParameter[] SqlInstance { get; set; }

        /// <summary>
        /// Login to the target instance using alternative credentials.
        /// </summary>
        [Parameter()]
        [Alias("Credential", "Cred")]
        public PSCredential SqlCredential { get; set; }

        /// <summary>
        /// Specifies the name of the availability group containing the listener to modify.
        /// Required when using SqlInstance parameter.
        /// </summary>
        [Parameter()]
        public string[] AvailabilityGroup { get; set; }

        /// <summary>
        /// Specifies the name of specific listeners to modify within the availability group.
        /// </summary>
        [Parameter()]
        public string[] Listener { get; set; }

        /// <summary>
        /// Sets the new port number for the availability group listener.
        /// </summary>
        [Parameter(Mandatory = true)]
        public int Port { get; set; }

        /// <summary>
        /// Accepts availability group listener objects from the pipeline, typically from Get-DbaAgListener.
        /// InputObject is object[] rather than AvailabilityGroupListener[] because the SMO assembly
        /// is loaded dynamically and is not available at compile time.
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        public object[] InputObject { get; set; }

        #endregion Parameters

        #region Static ScriptBlocks

        /// <summary>
        /// Script block for calling Get-DbaAgListener.
        /// </summary>
        private static readonly ScriptBlock _getAgListenersScript = ScriptBlock.Create(@"
param($si, $sc, $ag, $listener, $hasCred, $hasListener, $ee)
$params = @{ SqlInstance = $si; AvailabilityGroup = $ag }
if ($hasCred) { $params['SqlCredential'] = $sc }
if ($hasListener) { $params['Listener'] = $listener }
$params['EnableException'] = $ee
Get-DbaAgListener @params
");

        /// <summary>
        /// Script block for setting PortNumber and calling Alter() on a listener.
        /// </summary>
        private static readonly ScriptBlock _setPortAndAlterScript = ScriptBlock.Create(@"
param($listener, $port)
$listener.PortNumber = $port
$listener.Alter()
");

        #endregion Static ScriptBlocks

        /// <summary>
        /// Collects InputObject items across pipeline invocations.
        /// </summary>
        private List<object> _inputObjects = new List<object>();

        /// <summary>
        /// Tracks whether any InputObject was received via pipeline.
        /// </summary>
        private bool _receivedPipelineInput;

        /// <summary>
        /// Validates parameters at the start of processing.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            // Validate: either SqlInstance or InputObject must be provided
            // Skip this check when pipeline input is expected (InputObject not bound yet)
            if (!MyInvocation.ExpectingInput && TestBoundNot("SqlInstance", "InputObject"))
            {
                StopFunction("You must supply either -SqlInstance or an Input Object");
                TestFunctionInterrupt();
                return;
            }

            // Validate: AvailabilityGroup is required when SqlInstance is specified
            if (TestBound("SqlInstance") && TestBoundNot("AvailabilityGroup"))
            {
                StopFunction("You must specify one or more Availability Groups when using the SqlInstance parameter.");
                TestFunctionInterrupt();
                return;
            }
        }

        /// <summary>
        /// Collects pipeline InputObject items.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (TestFunctionInterrupt())
                return;

            if (InputObject != null)
            {
                _receivedPipelineInput = true;
                foreach (object obj in InputObject)
                {
                    if (obj != null)
                    {
                        _inputObjects.Add(obj is PSObject pso ? pso.BaseObject : obj);
                    }
                }
            }
        }

        /// <summary>
        /// Resolves listeners and applies the port change.
        /// </summary>
        protected override void EndProcessing()
        {
            if (TestFunctionInterrupt())
                return;

            // Deferred validation: if pipeline was expected but nothing arrived and SqlInstance wasn't bound
            if (!_receivedPipelineInput && _inputObjects.Count == 0 && !TestBound("SqlInstance"))
            {
                StopFunction("You must supply either -SqlInstance or an Input Object");
                return;
            }

            // If SqlInstance is provided, call Get-DbaAgListener to resolve listener objects
            if (TestBound("SqlInstance"))
            {
                Collection<PSObject> resolved = GetAgListeners();
                if (resolved != null)
                {
                    foreach (PSObject item in resolved)
                    {
                        if (item != null)
                            _inputObjects.Add(item.BaseObject);
                    }
                }
            }

            // Process each listener
            foreach (object listenerObj in _inputObjects)
            {
                ProcessListener(listenerObj);
                if (TestFunctionInterrupt())
                    return;
            }
        }

        /// <summary>
        /// Processes a single listener object: sets port and calls Alter().
        /// </summary>
        private void ProcessListener(object listenerObj)
        {
            PSObject listener = PSObject.AsPSObject(listenerObj);
            string parentName = GetParentAgName(listener);
            string listenerName = GetPropertyString(listener, "Name");

            string target = parentName ?? listenerName ?? "AvailabilityGroup";
            string action = String.Format("Setting port to {0} for {1}", Port, listenerName ?? "listener");

            if (ShouldProcess(target, action))
            {
                try
                {
                    SetPortAndAlter(listenerObj);
                    WriteObject(listenerObj);
                }
                catch (Exception ex)
                {
                    StopFunction("Failure", exception: ex, target: listenerObj);
                }
            }
        }

        #region Helpers

        /// <summary>
        /// Calls Get-DbaAgListener to resolve listener objects from SqlInstance parameters.
        /// </summary>
        private Collection<PSObject> GetAgListeners()
        {
            object[] args = new object[]
            {
                SqlInstance,
                SqlCredential,
                AvailabilityGroup,
                Listener,
                SqlCredential != null,
                TestBound("Listener"),
                EnableException.ToBool()
            };

            try
            {
                return InvokeCommand.InvokeScript(false, _getAgListenersScript, null, args);
            }
            catch (Exception ex)
            {
                StopFunction(
                    String.Format("Failed to get AG listeners: {0}", ex.Message),
                    exception: ex);
                TestFunctionInterrupt();
                return null;
            }
        }

        /// <summary>
        /// Sets the PortNumber property and calls Alter() on the listener object.
        /// Uses PowerShell script invocation since SMO types are loaded dynamically.
        /// </summary>
        private void SetPortAndAlter(object listenerObj)
        {
            InvokeCommand.InvokeScript(false, _setPortAndAlterScript, null, new object[] { listenerObj, Port });
        }

        /// <summary>
        /// Gets the parent availability group name from a listener object.
        /// </summary>
        internal static string GetParentAgName(PSObject listener)
        {
            if (listener == null)
                return null;

            PSObject parent = GetPropertyObject(listener, "Parent");
            if (parent != null)
                return GetPropertyString(parent, "Name");

            return null;
        }

        #endregion Helpers
    }
}
