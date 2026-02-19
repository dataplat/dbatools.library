using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Resumes suspended data synchronization for availability group databases.
    /// Reconnects the synchronization process so secondary replicas can catch up to the primary replica.
    /// </summary>
    [Cmdlet("Resume", "DbaAgDbDataMovement", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.High)]
    public class ResumeDbaAgDbDataMovementCommand : DbaBaseCmdlet
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
        /// Specifies the name of the availability group containing the databases with suspended data movement.
        /// </summary>
        [Parameter()]
        public string AvailabilityGroup { get; set; }

        /// <summary>
        /// Specifies which availability group databases to resume data movement for.
        /// </summary>
        [Parameter()]
        public string[] Database { get; set; }

        /// <summary>
        /// Accepts availability group database objects from Get-DbaAgDatabase via pipeline input.
        /// Type is object[] because SMO types are loaded dynamically at runtime.
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        public object[] InputObject { get; set; }

        #endregion Parameters

        #region Static ScriptBlocks

        /// <summary>
        /// Script block for calling Get-DbaAgDatabase to resolve InputObject from SqlInstance.
        /// </summary>
        private static readonly ScriptBlock _getAgDbScript = ScriptBlock.Create(@"
param($si, $sc, $db, $ag, $hasCred)
$params = @{ SqlInstance = $si }
if ($hasCred) { $params['SqlCredential'] = $sc }
if ($db) { $params['Database'] = $db }
if ($ag) { $params['AvailabilityGroup'] = $ag }
Get-DbaAgDatabase @params
");

        /// <summary>
        /// Script block that calls ResumeDataMovement() on an AvailabilityDatabase object.
        /// </summary>
        private static readonly ScriptBlock _resumeScript = ScriptBlock.Create(@"
param($agdb)
$agdb.ResumeDataMovement()
$agdb
");

        #endregion Static ScriptBlocks

        /// <summary>
        /// Collected input objects across pipeline invocations.
        /// </summary>
        private List<object> _inputObjects = new List<object>();

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

            // Validate: Database and AvailabilityGroup required when using SqlInstance
            if (TestBound("SqlInstance"))
            {
                if (TestBoundNot("Database") && TestBoundNot("AvailabilityGroup"))
                {
                    StopFunction("You must specify one or more databases and one Availability Groups when using the SqlInstance parameter.");
                    return;
                }
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
                foreach (DbaInstanceParameter instance in SqlInstance)
                {
                    try
                    {
                        Collection<PSObject> results = InvokeCommand.InvokeScript(
                            false, _getAgDbScript, null,
                            new object[] { instance, SqlCredential, Database, AvailabilityGroup, SqlCredential != null });

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
                            String.Format("Failed to get AG databases from {0}", instance),
                            exception: ex, target: instance, isContinue: true);
                        TestFunctionInterrupt();
                    }
                }
            }
        }

        /// <summary>
        /// Processes each collected AG database and resumes data movement.
        /// </summary>
        protected override void EndProcessing()
        {
            if (TestFunctionInterrupt())
                return;

            foreach (object agdbObj in _inputObjects)
            {
                PSObject agdb = PSObject.AsPSObject(agdbObj);
                string dbName = GetPropertyString(agdb, "Name");
                string agName = GetAgName(agdb);

                string target = String.Format("{0}.{1}", agName ?? "AvailabilityGroup", dbName ?? "database");
                string action = "Resuming data movement";

                if (ShouldProcess(target, action))
                {
                    try
                    {
                        Collection<PSObject> results = InvokeCommand.InvokeScript(
                            false, _resumeScript, null, new object[] { agdbObj });

                        if (results != null)
                        {
                            foreach (PSObject result in results)
                            {
                                if (result != null)
                                    WriteObject(result);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        StopFunction(
                            String.Format("Failure resuming data movement on {0}", dbName ?? "database"),
                            errorRecord: new ErrorRecord(ex, "ResumeDbaAgDbDataMovement", ErrorCategory.InvalidOperation, agdbObj),
                            target: agdbObj, isContinue: true);
                        TestFunctionInterrupt();
                    }
                }
            }
        }

        #region Helpers

        /// <summary>
        /// Gets the AG name from an AvailabilityDatabase's parent.
        /// </summary>
        private static string GetAgName(PSObject agdb)
        {
            try
            {
                PSPropertyInfo parentProp = agdb.Properties["Parent"];
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

        /// <summary>
        /// Gets a string property value from a PSObject.
        /// </summary>
        private static string GetPropertyString(PSObject obj, string propertyName)
        {
            if (obj == null) return null;
            try
            {
                PSPropertyInfo prop = obj.Properties[propertyName];
                if (prop != null && prop.Value != null)
                    return prop.Value.ToString();
            }
            catch (Exception)
            {
                // Property may not exist
            }
            return null;
        }

        #endregion Helpers
    }
}
