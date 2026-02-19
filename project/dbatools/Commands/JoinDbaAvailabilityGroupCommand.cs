using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Adds a SQL Server instance as a secondary replica to an existing availability group.
    /// For SQL Server 2017 and later, supports specifying the cluster type (External, Wsfc, or None).
    /// </summary>
    [Cmdlet("Join", "DbaAvailabilityGroup", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low)]
    public class JoinDbaAvailabilityGroupCommand : DbaBaseCmdlet
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
        /// Specifies the name of the availability group(s) to join.
        /// </summary>
        [Parameter()]
        public string[] AvailabilityGroup { get; set; }

        /// <summary>
        /// Specifies the cluster type for the availability group when joining SQL Server 2017 or later.
        /// </summary>
        [Parameter()]
        [ValidateSet("External", "Wsfc", "None")]
        public string ClusterType { get; set; }

        /// <summary>
        /// Accepts availability group objects from Get-DbaAvailabilityGroup for pipeline operations.
        /// Type is object[] because SMO types are loaded dynamically at runtime.
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        public object[] InputObject { get; set; }

        #endregion Parameters

        #region Static ScriptBlocks

        /// <summary>
        /// Script block for calling Connect-DbaInstance.
        /// </summary>
        private static readonly ScriptBlock _connectScript = ScriptBlock.Create(@"
param($si, $sc, $hasCred)
$params = @{ SqlInstance = $si }
if ($hasCred) { $params['SqlCredential'] = $sc }
Connect-DbaInstance @params
");

        /// <summary>
        /// Script block for joining an AG using T-SQL with cluster type (SQL 2017+).
        /// </summary>
        private static readonly ScriptBlock _joinWithClusterTypeScript = ScriptBlock.Create(@"
param($server, $agName, $clusterType)
$escapedAgName = $agName -replace '\]', ']]'
$server.Query(""ALTER AVAILABILITY GROUP [$escapedAgName] JOIN WITH (CLUSTER_TYPE = $clusterType)"")
");

        /// <summary>
        /// Script block for joining an AG using the SMO method (pre-2017 or no ClusterType).
        /// </summary>
        private static readonly ScriptBlock _joinSmoScript = ScriptBlock.Create(@"
param($server, $agName)
$server.JoinAvailabilityGroup($agName)
");

        #endregion Static ScriptBlocks

        /// <summary>
        /// AG names collected from InputObject and parameters.
        /// </summary>
        private List<string> _agNames = new List<string>();

        /// <summary>
        /// ClusterType resolved from InputObject (avoids mutating the parameter property).
        /// </summary>
        private string _resolvedClusterType;

        /// <summary>
        /// Collects pipeline InputObject items and resolves AG names.
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

            // Extract AG names from InputObject
            if (InputObject != null)
            {
                foreach (object obj in InputObject)
                {
                    if (obj == null) continue;
                    PSObject psObj = PSObject.AsPSObject(obj);
                    string name = GetPropertyString(psObj, "Name");
                    if (name != null)
                        _agNames.Add(name);

                    // Extract ClusterType from InputObject if not bound
                    if (!TestBound("ClusterType") && String.IsNullOrEmpty(_resolvedClusterType))
                    {
                        string ct = GetPropertyString(psObj, "ClusterType");
                        if (!String.IsNullOrEmpty(ct))
                            _resolvedClusterType = ct;
                    }
                }
            }

            // Add bound AvailabilityGroup names
            if (AvailabilityGroup != null)
            {
                foreach (string ag in AvailabilityGroup)
                {
                    if (!String.IsNullOrEmpty(ag))
                        _agNames.Add(ag);
                }
            }
        }

        /// <summary>
        /// Connects to instances and joins AGs.
        /// </summary>
        protected override void EndProcessing()
        {
            if (TestFunctionInterrupt())
                return;

            if (_agNames.Count == 0)
            {
                StopFunction("No availability group to add");
                return;
            }

            if (SqlInstance == null || SqlInstance.Length == 0)
                return;

            // Validate ClusterType against whitelist to prevent SQL injection
            string effectiveClusterType = TestBound("ClusterType") ? ClusterType : _resolvedClusterType;
            string validatedClusterType = null;
            if (!String.IsNullOrEmpty(effectiveClusterType))
            {
                if (String.Equals(effectiveClusterType, "External", StringComparison.OrdinalIgnoreCase))
                    validatedClusterType = "External";
                else if (String.Equals(effectiveClusterType, "Wsfc", StringComparison.OrdinalIgnoreCase))
                    validatedClusterType = "Wsfc";
                else if (String.Equals(effectiveClusterType, "None", StringComparison.OrdinalIgnoreCase))
                    validatedClusterType = "None";
            }

            foreach (DbaInstanceParameter instance in SqlInstance)
            {
                object server;
                try
                {
                    Collection<PSObject> connResults = InvokeCommand.InvokeScript(
                        false, _connectScript, null,
                        new object[] { instance, SqlCredential, SqlCredential != null });

                    if (connResults == null || connResults.Count == 0)
                    {
                        StopFunction(
                            String.Format("Failed to connect to {0}", instance),
                            target: instance, isContinue: true);
                        TestFunctionInterrupt();
                        continue;
                    }
                    server = connResults[0];
                }
                catch (Exception ex)
                {
                    StopFunction(
                        "Failure",
                        errorRecord: new ErrorRecord(ex, "JoinDbaAvailabilityGroup_Connect", ErrorCategory.ConnectionError, instance),
                        target: instance, isContinue: true);
                    TestFunctionInterrupt();
                    continue;
                }

                // Get server version major to decide T-SQL vs SMO
                int versionMajor = GetVersionMajor(server);

                foreach (string ag in _agNames)
                {
                    string serverName = GetPropertyString(PSObject.AsPSObject(server), "Name") ?? instance.ToString();
                    if (ShouldProcess(serverName, String.Format("Joining {0}", ag)))
                    {
                        try
                        {
                            if (validatedClusterType != null && versionMajor >= 14)
                            {
                                InvokeCommand.InvokeScript(
                                    false, _joinWithClusterTypeScript, null,
                                    new object[] { server, ag, validatedClusterType });
                            }
                            else
                            {
                                InvokeCommand.InvokeScript(
                                    false, _joinSmoScript, null,
                                    new object[] { server, ag });
                            }
                        }
                        catch (Exception ex)
                        {
                            StopFunction(
                                String.Format("Failure joining {0}", ag),
                                errorRecord: new ErrorRecord(ex, "JoinDbaAvailabilityGroup_Join", ErrorCategory.InvalidOperation, ag),
                                target: ag, isContinue: true);
                            TestFunctionInterrupt();
                        }
                    }
                }
            }
        }

        #region Helpers

        /// <summary>
        /// Gets the server version major.
        /// </summary>
        private static int GetVersionMajor(object serverObj)
        {
            try
            {
                PSObject server = PSObject.AsPSObject(serverObj);
                object val = null;
                PSPropertyInfo prop = server.Properties["VersionMajor"];
                if (prop != null)
                    val = prop.Value;
                if (val is int intVal)
                    return intVal;
                int parsed;
                if (val != null && int.TryParse(val.ToString(), out parsed))
                    return parsed;
            }
            catch (Exception)
            {
                // Best effort
            }
            return 0;
        }

        #endregion Helpers
    }
}
