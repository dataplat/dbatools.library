using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Net;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Creates a network listener endpoint for an Availability Group to provide client connectivity.
    /// </summary>
    [Cmdlet("Add", "DbaAgListener", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low)]
    public class AddDbaAgListenerCommand : DbaBaseCmdlet
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
        /// The name of the Availability Group that will receive the listener.
        /// </summary>
        [Parameter()]
        public string[] AvailabilityGroup { get; set; }

        /// <summary>
        /// Specifies a custom network name for the listener. Defaults to the Availability Group name.
        /// </summary>
        [Parameter()]
        public string Name { get; set; }

        /// <summary>
        /// One or more static IP addresses for the listener.
        /// </summary>
        [Parameter()]
        public IPAddress[] IPAddress { get; set; }

        /// <summary>
        /// Network subnet addresses for the listener IPs. Auto-calculated if not provided.
        /// </summary>
        [Parameter()]
        public IPAddress[] SubnetIP { get; set; }

        /// <summary>
        /// Subnet mask for each listener IP address. Defaults to 255.255.255.0.
        /// </summary>
        [Parameter()]
        public IPAddress[] SubnetMask { get; set; }

        /// <summary>
        /// TCP port number for client connections. Defaults to 1433.
        /// </summary>
        [Parameter()]
        [ValidateRange(1, 65535)]
        public int Port { get; set; } = 1433;

        /// <summary>
        /// Configures the listener to use DHCP for IP assignment.
        /// </summary>
        [Parameter()]
        public SwitchParameter Dhcp { get; set; }

        /// <summary>
        /// Returns the listener object without creating it on the server.
        /// </summary>
        [Parameter()]
        public SwitchParameter Passthru { get; set; }

        /// <summary>
        /// Accepts Availability Group objects from the pipeline (from Get-DbaAvailabilityGroup).
        /// InputObject is object[] because SMO types are loaded dynamically.
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        public object[] InputObject { get; set; }

        #endregion Parameters

        #region Static ScriptBlocks

        /// <summary>
        /// Script block for calling Get-DbaAvailabilityGroup.
        /// </summary>
        private static readonly ScriptBlock _getAvailabilityGroupScript = ScriptBlock.Create(@"
param($si, $sc, $ag, $hasCred, $ee)
$params = @{ SqlInstance = $si; AvailabilityGroup = $ag; EnableException = $ee }
if ($hasCred) { $params['SqlCredential'] = $sc }
Get-DbaAvailabilityGroup @params
");

        /// <summary>
        /// Script block for creating the listener SMO object and configuring IP addresses.
        /// Returns the listener object before Create() so C# can control Passthru vs Create separately.
        /// </summary>
        private static readonly ScriptBlock _createListenerScript = ScriptBlock.Create(@"
param($ag, $listenerName, $port, $ipAddresses, $subnetIPs, $subnetMasks, $isDhcp, $hasIPAddress, $ipCount)
$aglistener = New-Object Microsoft.SqlServer.Management.Smo.AvailabilityGroupListener -ArgumentList $ag, $listenerName
$aglistener.PortNumber = $port
$ipIndex = 0
do {
    $listenerip = New-Object Microsoft.SqlServer.Management.Smo.AvailabilityGroupListenerIPAddress -ArgumentList $aglistener
    if ($hasIPAddress -and $ipAddresses) {
        $listenerip.IPAddress = $ipAddresses[$ipIndex]
    }
    if ($subnetIPs) {
        $listenerip.SubnetMask = $subnetMasks[$ipIndex]
        $listenerip.SubnetIP = $subnetIPs[$ipIndex]
    }
    $listenerip.IsDHCP = $isDhcp
    $aglistener.AvailabilityGroupListenerIPAddresses.Add($listenerip)
} while ((++$ipIndex) -lt $ipCount)
$aglistener
");

        /// <summary>
        /// Script block for calling Create() on the listener object.
        /// Mirrors Invoke-Create private function behavior.
        /// </summary>
        private static readonly ScriptBlock _invokeCreateScript = ScriptBlock.Create(@"
param($listener)
$ErrorActionPreference = 'Stop'
$listener.Create()
");

        /// <summary>
        /// Script block for calling Get-DbaAgListener to retrieve the created listener.
        /// EnableException is not passed to match PS1 behavior (retrieval failures are non-terminating).
        /// </summary>
        private static readonly ScriptBlock _getAgListenerScript = ScriptBlock.Create(@"
param($server, $agName, $listenerName)
Get-DbaAgListener -SqlInstance $server -AvailabilityGroup $agName -Listener $listenerName
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
        /// Resolved subnet masks (expanded to match IP count).
        /// </summary>
        private IPAddress[] _resolvedSubnetMask;

        /// <summary>
        /// Resolved subnet IPs (calculated or expanded).
        /// </summary>
        private IPAddress[] _resolvedSubnetIP;

        /// <summary>
        /// Validates parameters at the start of processing.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            // Validate: either SqlInstance or InputObject must be provided
            if (!MyInvocation.ExpectingInput && TestBoundNot("SqlInstance", "InputObject"))
            {
                StopFunction("You must supply either -SqlInstance or an Input Object");
                TestFunctionInterrupt();
                return;
            }

            // SqlInstance requires AvailabilityGroup
            if (TestBound("SqlInstance") && TestBoundNot("AvailabilityGroup"))
            {
                StopFunction("You must specify one or more Availability Groups when using the SqlInstance parameter.");
                TestFunctionInterrupt();
                return;
            }

            // DHCP validation
            if (Dhcp)
            {
                if (TestBound("IPAddress"))
                {
                    StopFunction("You cannot specify both an IP address and the Dhcp switch.");
                    TestFunctionInterrupt();
                    return;
                }

                int maskCount = SubnetMask != null ? SubnetMask.Length : 0;
                int sipCount = SubnetIP != null ? SubnetIP.Length : 0;
                if (maskCount > 1 || sipCount > 1)
                {
                    StopFunction("You can only specify a single subnet when using Dhcp.");
                    TestFunctionInterrupt();
                    return;
                }
            }

            // Apply default SubnetMask if not bound (PS1 defaults to 255.255.255.0)
            if (!TestBound("SubnetMask"))
            {
                SubnetMask = new IPAddress[] { System.Net.IPAddress.Parse("255.255.255.0") };
            }

            // Resolve SubnetMask and SubnetIP when IPAddress is provided
            if (TestBound("IPAddress"))
            {
                // Expand SubnetMask to match IPAddress count
                if (IPAddress.Length != SubnetMask.Length)
                {
                    if (SubnetMask.Length == 1)
                    {
                        _resolvedSubnetMask = ExpandArray(SubnetMask, IPAddress.Length);
                    }
                    else
                    {
                        StopFunction("When specifying multiple IP addresses, the number of subnet masks must match, or give one mask to be used for all IP addresses.");
                        TestFunctionInterrupt();
                        return;
                    }
                }
                else
                {
                    _resolvedSubnetMask = SubnetMask;
                }

                // Calculate or validate SubnetIP
                if (!TestBound("SubnetIP"))
                {
                    // Auto-calculate subnet from IP and mask using bitwise AND
                    _resolvedSubnetIP = new IPAddress[IPAddress.Length];
                    for (int i = 0; i < IPAddress.Length; i++)
                    {
                        _resolvedSubnetIP[i] = CalculateSubnet(IPAddress[i], _resolvedSubnetMask[i]);
                    }
                }
                else
                {
                    if (IPAddress.Length != SubnetIP.Length)
                    {
                        if (SubnetIP.Length == 1)
                        {
                            _resolvedSubnetIP = ExpandArray(SubnetIP, IPAddress.Length);
                        }
                        else
                        {
                            StopFunction("When specifying subnet IPs explicitly, the number of subnets must match the number of IPs, or use one subnet to be applied to all IPs.");
                            TestFunctionInterrupt();
                            return;
                        }
                    }
                    else
                    {
                        _resolvedSubnetIP = SubnetIP;
                    }
                }
            }
            else
            {
                // No IPAddress bound (DHCP mode or pipeline will provide IPs)
                _resolvedSubnetMask = SubnetMask;
                _resolvedSubnetIP = SubnetIP;
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
        /// Resolves availability groups and creates listeners.
        /// </summary>
        protected override void EndProcessing()
        {
            if (TestFunctionInterrupt())
                return;

            // Deferred validation: pipeline expected but nothing arrived
            if (!_receivedPipelineInput && _inputObjects.Count == 0 && !TestBound("SqlInstance"))
            {
                StopFunction("You must supply either -SqlInstance or an Input Object");
                return;
            }

            // Resolve AGs from SqlInstance
            if (TestBound("SqlInstance"))
            {
                Collection<PSObject> resolved = GetAvailabilityGroups();
                if (resolved != null)
                {
                    foreach (PSObject item in resolved)
                    {
                        if (item != null)
                            _inputObjects.Add(item.BaseObject);
                    }
                }
            }

            // Process each AG
            foreach (object agObj in _inputObjects)
            {
                ProcessAvailabilityGroup(agObj);
                if (TestFunctionInterrupt())
                    return;
            }
        }

        /// <summary>
        /// Creates a listener for a single availability group.
        /// </summary>
        private void ProcessAvailabilityGroup(object agObj)
        {
            PSObject ag = PSObject.AsPSObject(agObj);
            string agName = GetPropertyString(ag, "Name");

            // Default listener name to AG name if not bound
            string listenerName = TestBound("Name") ? Name : agName;

            // Get parent server for ShouldProcess and output
            PSObject parent = GetPropertyObject(ag, "Parent");
            string parentName = parent != null ? GetPropertyString(parent, "Name") : null;

            string ipDisplay = IPAddress != null
                ? String.Join(",", GetIPAddressStrings(IPAddress))
                : "DHCP";
            string target = parentName ?? "AvailabilityGroup";
            string action = String.Format("Adding {0} to {1}", ipDisplay, agName ?? "AvailabilityGroup");

            if (ShouldProcess(target, action))
            {
                bool createSucceeded = false;
                try
                {
                    // Create listener SMO object with IP addresses.
                    // ipCount=0 for DHCP mode: the do/while loop in the script runs once
                    // (do always executes the body), creating one IP entry with IsDHCP=true.
                    int ipCount = IPAddress != null ? IPAddress.Length : 0;
                    Collection<PSObject> result = InvokeCommand.InvokeScript(
                        false,
                        _createListenerScript,
                        null,
                        new object[]
                        {
                            agObj,
                            listenerName,
                            Port,
                            IPAddress,
                            _resolvedSubnetIP,
                            _resolvedSubnetMask,
                            Dhcp.ToBool(),
                            TestBound("IPAddress"),
                            ipCount
                        });

                    if (Passthru)
                    {
                        // Return the uncreated listener object for further configuration
                        if (result != null && result.Count > 0)
                        {
                            WriteObject(result[0]);
                        }
                        return;
                    }

                    // Call Create() on the listener (mirrors Invoke-Create behavior)
                    if (result != null && result.Count > 0)
                    {
                        object listenerObj = result[0].BaseObject;
                        InvokeCommand.InvokeScript(true, _invokeCreateScript, null, new object[] { listenerObj });
                        createSucceeded = true;
                    }
                }
                catch (Exception ex)
                {
                    StopFunction("Failure", exception: ex, target: agObj);
                    if (TestFunctionInterrupt())
                        return;
                }

                // Retrieve the created listener for output
                if (createSucceeded)
                {
                    try
                    {
                        object serverObj = parent != null ? parent.BaseObject : null;
                        Collection<PSObject> listeners = InvokeCommand.InvokeScript(
                            false,
                            _getAgListenerScript,
                            null,
                            new object[]
                            {
                                serverObj,
                                agName,
                                listenerName
                            });

                        if (listeners != null)
                        {
                            foreach (PSObject listener in listeners)
                            {
                                if (listener != null)
                                    WriteObject(listener);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        StopFunction(
                            String.Format("Failed to retrieve listener after creation: {0}", ex.Message),
                            exception: ex,
                            target: agObj);
                    }
                }
            }
        }

        #region Helpers

        /// <summary>
        /// Calls Get-DbaAvailabilityGroup to resolve AG objects from SqlInstance parameters.
        /// </summary>
        private Collection<PSObject> GetAvailabilityGroups()
        {
            try
            {
                return InvokeCommand.InvokeScript(
                    false,
                    _getAvailabilityGroupScript,
                    null,
                    new object[]
                    {
                        SqlInstance,
                        SqlCredential,
                        AvailabilityGroup,
                        SqlCredential != null,
                        EnableException.ToBool()
                    });
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
        /// Calculates the subnet address by performing a bitwise AND of an IP address and subnet mask.
        /// Equivalent to PS1: ($IPAddress.Address -band $SubnetMask.Address) -as [ipaddress]
        /// </summary>
        internal static IPAddress CalculateSubnet(IPAddress ip, IPAddress mask)
        {
            byte[] ipBytes = ip.GetAddressBytes();
            byte[] maskBytes = mask.GetAddressBytes();
            int length = Math.Min(ipBytes.Length, maskBytes.Length);
            byte[] result = new byte[length];
            for (int i = 0; i < length; i++)
            {
                result[i] = (byte)(ipBytes[i] & maskBytes[i]);
            }
            return new IPAddress(result);
        }

        /// <summary>
        /// Expands a single-element IPAddress array to the target count by repeating the first element.
        /// Used when one subnet mask or subnet IP is provided for multiple IP addresses.
        /// </summary>
        internal static IPAddress[] ExpandArray(IPAddress[] source, int targetCount)
        {
            if (source == null || source.Length == 0)
                return source;

            IPAddress[] expanded = new IPAddress[targetCount];
            for (int i = 0; i < targetCount; i++)
            {
                expanded[i] = source[0];
            }
            return expanded;
        }

        /// <summary>
        /// Converts an array of IPAddress objects to their string representations.
        /// </summary>
        internal static string[] GetIPAddressStrings(IPAddress[] addresses)
        {
            if (addresses == null)
                return new string[0];

            string[] result = new string[addresses.Length];
            for (int i = 0; i < addresses.Length; i++)
            {
                result[i] = addresses[i].ToString();
            }
            return result;
        }

        #endregion Helpers
    }
}
