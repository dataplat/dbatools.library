using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Retrieves availability group listener configurations including IP addresses and port numbers
    /// from SQL Server instances. Returns AvailabilityGroupListener objects with listener names,
    /// port numbers, IP configurations, and associated availability groups.
    /// </summary>
    [Cmdlet("Get", "DbaAgListener")]
    public class GetDbaAgListenerCommand : DbaBaseCmdlet
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
        public PSCredential SqlCredential { get; set; }

        /// <summary>
        /// Specifies which availability groups to include when retrieving listener information.
        /// </summary>
        [Parameter()]
        public string[] AvailabilityGroup { get; set; }

        /// <summary>
        /// Specifies which availability group listeners to return by name.
        /// </summary>
        [Parameter()]
        public string[] Listener { get; set; }

        /// <summary>
        /// Accepts availability group objects from Get-DbaAvailabilityGroup via pipeline input.
        /// Type is object[] because SMO types are loaded dynamically at runtime.
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        public object[] InputObject { get; set; }

        #endregion Parameters

        /// <summary>
        /// Default display properties for the output objects.
        /// </summary>
        private static readonly string[] DefaultDisplayProperties = new string[]
        {
            "ComputerName", "InstanceName", "SqlInstance", "AvailabilityGroup",
            "Name", "PortNumber", "ClusterIPConfiguration"
        };

        /// <summary>
        /// AG objects obtained from SqlInstance parameter in BeginProcessing, emitted in EndProcessing.
        /// </summary>
        private List<object> _sqlInstanceObjects = new List<object>();

        /// <summary>
        /// Listener name filter built from the Listener parameter.
        /// </summary>
        private HashSet<string> _listenerFilter;

        /// <summary>
        /// Tracks whether any InputObject was received via pipeline.
        /// </summary>
        private bool _receivedPipelineInput;

        /// <summary>
        /// Validates parameters and resolves SqlInstance-derived AG objects.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            // Only validate when we know for sure neither parameter will be provided.
            // When pipeline input is expected, InputObject won't be bound yet in BeginProcessing,
            // so we skip the check here and validate in EndProcessing instead.
            if (!MyInvocation.ExpectingInput && TestBoundNot("SqlInstance", "InputObject"))
            {
                StopFunction("You must supply either -SqlInstance or an Input Object");
                TestFunctionInterrupt();
                return;
            }

            // Build listener name filter once
            if (TestBound("Listener") && Listener != null)
            {
                _listenerFilter = BuildListenerFilter(Listener);
            }

            // If SqlInstance is provided, call Get-DbaAvailabilityGroup once to get AG objects
            if (TestBound("SqlInstance"))
            {
                Collection<PSObject> agObjects = GetAvailabilityGroups();
                if (agObjects != null)
                {
                    foreach (PSObject ag in agObjects)
                    {
                        if (ag != null)
                            _sqlInstanceObjects.Add(ag.BaseObject ?? ag);
                    }
                }
            }
        }

        /// <summary>
        /// Processes each pipeline InputObject item immediately for streaming output.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (TestFunctionInterrupt())
                return;

            // Process pipeline InputObject items as they arrive for streaming output
            if (InputObject != null)
            {
                _receivedPipelineInput = true;
                foreach (object obj in InputObject)
                {
                    if (obj != null)
                        ProcessAgObject(obj);
                }
            }
        }

        /// <summary>
        /// Processes AG objects obtained from SqlInstance parameter and validates pipeline input was received.
        /// </summary>
        protected override void EndProcessing()
        {
            if (TestFunctionInterrupt())
                return;

            // Deferred validation: if pipeline was expected but nothing arrived and SqlInstance wasn't bound
            if (!_receivedPipelineInput && _sqlInstanceObjects.Count == 0 && !TestBound("SqlInstance"))
            {
                StopFunction("You must supply either -SqlInstance or an Input Object");
                return;
            }

            // Process SqlInstance-derived AG objects
            foreach (object agObj in _sqlInstanceObjects)
            {
                ProcessAgObject(agObj);
            }
        }

        /// <summary>
        /// Processes a single AG object: retrieves its AvailabilityGroupListeners,
        /// applies filters, adds custom properties, and outputs results.
        /// </summary>
        private void ProcessAgObject(object agObj)
        {
            // Get AvailabilityGroupListeners from this AG object
            Collection<PSObject> listeners = GetListeners(agObj);
            if (listeners == null || listeners.Count == 0)
                return;

            foreach (PSObject listener in listeners)
            {
                if (listener == null)
                    continue;

                // Get listener name for filtering
                string listenerName = GetPropertyString(listener, "Name");

                // Apply Listener filter - exclude listeners with null names when filter is active
                if (_listenerFilter != null)
                {
                    if (listenerName == null || !_listenerFilter.Contains(listenerName))
                        continue;
                }

                // Get parent AG and Server info
                // Listener.Parent = AvailabilityGroup, Listener.Parent.Parent = Server
                PSObject parent = GetPropertyObject(listener, "Parent");
                string agName = null;
                string computerName = null;
                string serviceName = null;
                string domainInstanceName = null;

                if (parent != null)
                {
                    agName = GetPropertyString(parent, "Name");

                    PSObject server = GetPropertyObject(parent, "Parent");
                    if (server != null)
                    {
                        computerName = GetPropertyString(server, "ComputerName");
                        serviceName = GetPropertyString(server, "ServiceName");
                        domainInstanceName = GetPropertyString(server, "DomainInstanceName");
                    }
                }

                // Add custom NoteProperties
                AddOrSetProperty(listener, "ComputerName", computerName);
                AddOrSetProperty(listener, "InstanceName", serviceName);
                AddOrSetProperty(listener, "SqlInstance", domainInstanceName);
                AddOrSetProperty(listener, "AvailabilityGroup", agName);

                // Set default display properties
                SetDefaultDisplayPropertySet(listener, DefaultDisplayProperties);

                WriteObject(listener);
            }
        }

        #region Helpers

        /// <summary>
        /// Builds a case-insensitive HashSet from an array of listener names for filtering.
        /// Returns null if the input is null or empty.
        /// </summary>
        internal static HashSet<string> BuildListenerFilter(string[] listeners)
        {
            if (listeners == null || listeners.Length == 0)
                return null;

            HashSet<string> filter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in listeners)
            {
                filter.Add(name);
            }
            return filter;
        }

        /// <summary>
        /// Calls Get-DbaAvailabilityGroup with the current SqlInstance, SqlCredential,
        /// AvailabilityGroup, and EnableException parameters.
        /// </summary>
        private Collection<PSObject> GetAvailabilityGroups()
        {
            string script = @"
param($si, $sc, $ag, $hasCred, $hasAg, $ee)
$params = @{ SqlInstance = $si }
if ($hasCred) { $params['SqlCredential'] = $sc }
if ($hasAg) { $params['AvailabilityGroup'] = $ag }
$params['EnableException'] = $ee
Get-DbaAvailabilityGroup @params
";
            object[] args = new object[]
            {
                SqlInstance,
                SqlCredential,
                AvailabilityGroup,
                SqlCredential != null,
                TestBound("AvailabilityGroup"),
                EnableException.ToBool()
            };

            try
            {
                return InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, args);
            }
            catch (Exception ex)
            {
                StopFunction(
                    String.Format("Failed to get availability groups: {0}", ex.Message),
                    exception: ex);
                TestFunctionInterrupt();
                return null;
            }
        }

        /// <summary>
        /// Gets the AvailabilityGroupListeners collection from an AG object.
        /// Creates ScriptBlock per invocation to avoid thread-safety issues with shared instances.
        /// </summary>
        private Collection<PSObject> GetListeners(object agObject)
        {
            try
            {
                return InvokeCommand.InvokeScript(
                    false,
                    ScriptBlock.Create("param($ag) $ag.AvailabilityGroupListeners"),
                    null,
                    new object[] { agObject });
            }
            catch (Exception ex)
            {
                WriteMessageAtLevel(
                    String.Format("Failed to retrieve AvailabilityGroupListeners: {0}", ex.Message),
                    MessageLevel.Warning, null);
                return null;
            }
        }

        /// <summary>
        /// Adds or updates a NoteProperty on a PSObject, matching Add-Member -Force behavior.
        /// </summary>
        internal static void AddOrSetProperty(PSObject obj, string name, object value)
        {
            if (obj == null)
                return;
            try
            {
                PSPropertyInfo existing = obj.Properties[name];
                if (existing != null)
                {
                    existing.Value = value;
                }
                else
                {
                    obj.Properties.Add(new PSNoteProperty(name, value));
                }
            }
            catch (Exception)
            {
                try
                {
                    obj.Properties.Remove(name);
                    obj.Properties.Add(new PSNoteProperty(name, value));
                }
                catch (Exception)
                {
                    // Best-effort
                }
            }
        }

        /// <summary>
        /// Sets the DefaultDisplayPropertySet on a PSObject for formatted output.
        /// </summary>
        internal static void SetDefaultDisplayPropertySet(PSObject obj, string[] properties)
        {
            if (obj == null || properties == null)
                return;

            try { obj.Members.Remove("PSStandardMembers"); }
            catch (Exception) { /* May not exist yet */ }

            try
            {
                obj.Members.Add(new PSMemberSet("PSStandardMembers", new PSMemberInfo[]
                {
                    new PSPropertySet("DefaultDisplayPropertySet", properties)
                }));
            }
            catch (Exception)
            {
                // Best-effort
            }
        }

        #endregion Helpers
    }
}
