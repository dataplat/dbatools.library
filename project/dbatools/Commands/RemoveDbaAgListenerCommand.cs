using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Removes availability group listeners from SQL Server instances, permanently deleting the virtual
    /// network name and IP address configuration that clients use to connect to availability group databases.
    /// Supports pipeline input from Get-DbaAgListener.
    /// </summary>
    [Cmdlet("Remove", "DbaAgListener", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.High)]
    public class RemoveDbaAgListenerCommand : DbaBaseCmdlet
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
        /// Specifies the name of specific availability group listeners to remove. Required when using SqlInstance parameter.
        /// </summary>
        [Parameter()]
        public string[] Listener { get; set; }

        /// <summary>
        /// Filters listener removal to only those within the specified availability groups.
        /// </summary>
        [Parameter()]
        public string[] AvailabilityGroup { get; set; }

        /// <summary>
        /// Accepts availability group listener objects from the pipeline, typically from Get-DbaAgListener.
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        public object[] InputObject { get; set; }

        #endregion Parameters

        #region Static ScriptBlocks

        /// <summary>
        /// Calls Get-DbaAgListener with appropriate parameters.
        /// </summary>
        private static readonly ScriptBlock _getDbaAgListenerScript = ScriptBlock.Create(@"
param($si, $sc, $listener, $ag, $hasCred, $hasListener, $hasAg)
$params = @{ SqlInstance = $si }
if ($hasCred) { $params['SqlCredential'] = $sc }
if ($hasListener) { $params['Listener'] = $listener }
if ($hasAg) { $params['AvailabilityGroup'] = $ag }
Get-DbaAgListener @params
");

        /// <summary>
        /// Gets the server name from the listener's AG parent hierarchy.
        /// AvailabilityGroupListener.Parent = AvailabilityGroup, AvailabilityGroup.Parent = Server.
        /// </summary>
        private static readonly ScriptBlock _getServerNameScript = ScriptBlock.Create(@"
param($listener) $listener.Parent.Parent.Name
");

        /// <summary>
        /// Drops the listener from the availability group and returns the AG name.
        /// </summary>
        private static readonly ScriptBlock _dropListenerScript = ScriptBlock.Create(@"
param($listener)
$agName = $listener.Parent.Name
$listener.Parent.AvailabilityGroupListeners[$listener.Name].Drop()
$agName
");

        #endregion Static ScriptBlocks

        /// <summary>
        /// Processes each pipeline item or SqlInstance-based query to remove listeners from availability groups.
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

            // Validate: SqlInstance requires Listener
            if (TestBound("SqlInstance") && TestBoundNot("Listener"))
            {
                StopFunction("You must specify one or more listeners and one or more Availability Groups when using the SqlInstance parameter.");
                return;
            }

            // If SqlInstance provided, fetch listeners and add to InputObject
            // PS1: $InputObject += Get-DbaAgListener -SqlInstance $SqlInstance -SqlCredential $SqlCredential -Listener $Listener
            if (TestBound("SqlInstance"))
            {
                List<object> items = new List<object>();
                if (InputObject != null)
                {
                    items.AddRange(InputObject);
                }

                Collection<PSObject> listeners = GetDbaAgListener();
                if (listeners != null)
                {
                    foreach (PSObject listener in listeners)
                    {
                        if (listener != null)
                        {
                            items.Add(listener.BaseObject ?? listener);
                        }
                    }
                }
                InputObject = items.ToArray();
            }

            if (InputObject == null)
                return;

            foreach (object listenerObj in InputObject)
            {
                if (listenerObj == null)
                    continue;
                ProcessListener(listenerObj);
            }
        }

        /// <summary>
        /// Processes a single listener object: validates via ShouldProcess, drops from AG, and outputs result.
        /// </summary>
        private void ProcessListener(object listenerObj)
        {
            PSObject listener = PSObject.AsPSObject(listenerObj);
            string listenerName = GetPropertyString(listener, "Name");
            string serverName = GetServerName(listenerObj);

            if (ShouldProcess(serverName ?? listenerName ?? "Unknown",
                String.Format("Removing availability group listener {0}", listenerName ?? "Unknown")))
            {
                try
                {
                    string agName = DropListener(listenerObj);

                    PSObject output = new PSObject();
                    output.Properties.Add(new PSNoteProperty("ComputerName", GetPropertyString(listener, "ComputerName")));
                    output.Properties.Add(new PSNoteProperty("InstanceName", GetPropertyString(listener, "InstanceName")));
                    output.Properties.Add(new PSNoteProperty("SqlInstance", GetPropertyString(listener, "SqlInstance")));
                    output.Properties.Add(new PSNoteProperty("AvailabilityGroup", agName));
                    output.Properties.Add(new PSNoteProperty("Listener", listenerName));
                    output.Properties.Add(new PSNoteProperty("Status", "Removed"));
                    WriteObject(output);
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("Failed to remove listener {0}", listenerName),
                        errorRecord: new ErrorRecord(ex, "RemoveDbaAgListener", ErrorCategory.InvalidOperation, listenerObj),
                        target: listenerObj, isContinue: true);
                    TestFunctionInterrupt();
                }
            }
        }

        #region Helpers

        /// <summary>
        /// Gets the server name from the listener's parent hierarchy via ScriptBlock.
        /// </summary>
        private string GetServerName(object listenerObj)
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, _getServerNameScript, null, new object[] { listenerObj });
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
        /// Drops the listener from the availability group and returns the AG name.
        /// </summary>
        private string DropListener(object listenerObj)
        {
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, _dropListenerScript, null, new object[] { listenerObj });
            if (results != null && results.Count > 0 && results[0] != null)
                return results[0].BaseObject as string ?? results[0].ToString();
            return null;
        }

        /// <summary>
        /// Calls Get-DbaAgListener with the current SqlInstance, SqlCredential, and Listener parameters.
        /// </summary>
        private Collection<PSObject> GetDbaAgListener()
        {
            try
            {
                return InvokeCommand.InvokeScript(true, _getDbaAgListenerScript, null,
                    new object[]
                    {
                        SqlInstance,
                        SqlCredential,
                        Listener,
                        AvailabilityGroup,
                        SqlCredential != null,
                        Listener != null && Listener.Length > 0,
                        TestBound("AvailabilityGroup")
                    });
            }
            catch (Exception ex)
            {
                StopFunction(
                    "Failed to get availability group listeners",
                    errorRecord: new ErrorRecord(ex, "RemoveDbaAgListener_GetListener", ErrorCategory.ConnectionError, SqlInstance),
                    target: SqlInstance, isContinue: true);
                TestFunctionInterrupt();
                return null;
            }
        }

        #endregion Helpers
    }
}
