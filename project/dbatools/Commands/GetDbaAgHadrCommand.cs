using System;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Retrieves the High Availability Disaster Recovery (HADR) service status for SQL Server instances.
    /// Returns one object per instance with ComputerName, InstanceName, SqlInstance, and IsHadrEnabled properties.
    /// </summary>
    [Cmdlet("Get", "DbaAgHadr")]
    public class GetDbaAgHadrCommand : DbaInstanceCmdlet
    {
        /// <summary>
        /// Default display properties for the output objects.
        /// </summary>
        private static readonly string[] DefaultDisplayProperties = new string[]
        {
            "ComputerName", "InstanceName", "SqlInstance", "IsHadrEnabled"
        };

        /// <summary>
        /// Processes each SQL Server instance to retrieve the HADR enabled status.
        /// </summary>
        protected override void ProcessRecord()
        {
            foreach (DbaInstanceParameter instance in SqlInstance)
            {
                object rawServer;
                try
                {
                    rawServer = ConnectInstance(instance);
                    if (rawServer == null)
                    {
                        StopFunction(
                            "Failure",
                            target: instance,
                            isContinue: true,
                            category: ErrorCategory.ConnectionError);
                        TestFunctionInterrupt();
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    StopFunction(
                        "Failure",
                        errorRecord: new ErrorRecord(ex, "GetDbaAgHadr_ConnectionError", ErrorCategory.ConnectionError, instance),
                        target: instance,
                        isContinue: true,
                        category: ErrorCategory.ConnectionError);
                    TestFunctionInterrupt();
                    continue;
                }

                try
                {
                    PSObject server = PSObject.AsPSObject(rawServer);

                    // Read server properties
                    string computerName = GetPropertyString(server, "ComputerName");
                    string serviceName = GetPropertyString(server, "ServiceName");
                    string domainInstanceName = GetPropertyString(server, "DomainInstanceName");

                    // Add NoteProperties matching PS1 Add-Member -Force behavior
                    AddOrSetProperty(server, "ComputerName", computerName);
                    AddOrSetProperty(server, "InstanceName", serviceName);
                    AddOrSetProperty(server, "SqlInstance", domainInstanceName);

                    // Set default display properties
                    SetDefaultDisplayPropertySet(server, DefaultDisplayProperties);

                    WriteObject(server);
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("Failed to process {0}", instance),
                        exception: ex,
                        target: instance,
                        isContinue: true);
                    TestFunctionInterrupt();
                    continue;
                }
            }
        }

        #region Helpers

        /// <summary>
        /// Connects to a SQL Server instance using Connect-DbaInstance.
        /// Returns the BaseObject from the SMO Server result, or null on failure.
        /// </summary>
        private object ConnectInstance(DbaInstanceParameter instance)
        {
            string script;
            object[] args;
            if (SqlCredential != null)
            {
                script = "param($i, $c) Connect-DbaInstance -SqlInstance $i -SqlCredential $c";
                args = new object[] { instance, SqlCredential };
            }
            else
            {
                script = "param($i) Connect-DbaInstance -SqlInstance $i";
                args = new object[] { instance };
            }

            Collection<PSObject> results = InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, args);
            if (results != null && results.Count > 0)
                return results[0].BaseObject;
            return null;
        }

        /// <summary>
        /// Gets a string property value from a PSObject.
        /// </summary>
        internal static string GetPropertyString(PSObject obj, string propertyName)
        {
            if (obj == null)
                return null;
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
