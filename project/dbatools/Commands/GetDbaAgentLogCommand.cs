using System;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Retrieves SQL Server Agent error log entries from one or more instances.
    /// Returns log entry objects with properties including LogDate, ProcessInfo, Text,
    /// and custom properties ComputerName, InstanceName, SqlInstance.
    /// </summary>
    [Cmdlet("Get", "DbaAgentLog")]
    public class GetDbaAgentLogCommand : DbaInstanceCmdlet
    {
        #region Parameters

        /// <summary>
        /// Specifies which numbered agent error log files to retrieve (0-9).
        /// Log 0 contains the most recent entries, while higher numbers contain older historical logs.
        /// </summary>
        [Parameter()]
        [ValidateRange(0, 9)]
        public int[] LogNumber { get; set; }

        #endregion Parameters

        /// <summary>
        /// Default display properties for the output objects.
        /// </summary>
        private static readonly string[] DefaultDisplayProperties = new string[]
        {
            "ComputerName", "InstanceName", "SqlInstance", "LogDate", "ProcessInfo", "Text"
        };

        private static readonly ScriptBlock ConnectWithCredScript =
            ScriptBlock.Create("param($i, $c) Connect-DbaInstance -SqlInstance $i -SqlCredential $c");

        private static readonly ScriptBlock ConnectScript =
            ScriptBlock.Create("param($i) Connect-DbaInstance -SqlInstance $i");

        private static readonly ScriptBlock ReadErrorLogWithNumberScript =
            ScriptBlock.Create("param($s, $n) $s.JobServer.ReadErrorLog($n)");

        private static readonly ScriptBlock ReadErrorLogScript =
            ScriptBlock.Create("param($s) $s.JobServer.ReadErrorLog()");

        /// <summary>
        /// Connects to each SQL Server instance and retrieves SQL Agent error log entries,
        /// adding custom properties for ComputerName, InstanceName, and SqlInstance.
        /// </summary>
        protected override void ProcessRecord()
        {
            foreach (DbaInstanceParameter instance in SqlInstance)
            {
                object server;
                try
                {
                    server = ConnectInstance(instance);
                    if (server == null)
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
                        errorRecord: new ErrorRecord(ex, "GetDbaAgentLog_ConnectionError", ErrorCategory.ConnectionError, instance),
                        target: instance,
                        isContinue: true,
                        category: ErrorCategory.ConnectionError);
                    TestFunctionInterrupt();
                    continue;
                }

                string computerName = GetServerPropertySafe(server, "ComputerName");
                string serviceName = GetServerPropertySafe(server, "ServiceName");
                string domainInstanceName = GetServerPropertySafe(server, "DomainInstanceName");

                if (LogNumber != null && LogNumber.Length > 0)
                {
                    foreach (int number in LogNumber)
                    {
                        try
                        {
                            Collection<PSObject> entries = ReadAgentErrorLog(server, number);
                            OutputEntries(entries, computerName, serviceName, domainInstanceName);
                        }
                        catch (Exception ex)
                        {
                            StopFunction(
                                "Could not read from SQL Server Agent",
                                exception: ex,
                                target: server,
                                isContinue: true);
                            TestFunctionInterrupt();
                            continue;
                        }
                    }
                }
                else
                {
                    try
                    {
                        Collection<PSObject> entries = ReadAgentErrorLog(server, null);
                        OutputEntries(entries, computerName, serviceName, domainInstanceName);
                    }
                    catch (Exception ex)
                    {
                        StopFunction(
                            "Could not read from SQL Server Agent",
                            exception: ex,
                            target: server,
                            isContinue: true);
                        TestFunctionInterrupt();
                    }
                }
            }
        }

        #region Helpers

        /// <summary>
        /// Connects to a SQL Server instance via Connect-DbaInstance.
        /// </summary>
        private object ConnectInstance(DbaInstanceParameter instance)
        {
            object[] args;
            ScriptBlock script;
            if (SqlCredential != null)
            {
                script = ConnectWithCredScript;
                args = new object[] { instance, SqlCredential };
            }
            else
            {
                script = ConnectScript;
                args = new object[] { instance };
            }

            Collection<PSObject> results = InvokeCommand.InvokeScript(false, script, null, args);
            if (results != null && results.Count > 0)
                return results[0].BaseObject;
            return null;
        }

        /// <summary>
        /// Reads the agent error log from the server's JobServer.
        /// </summary>
        private Collection<PSObject> ReadAgentErrorLog(object server, int? logNumber)
        {
            ScriptBlock script;
            object[] args;
            if (logNumber.HasValue)
            {
                script = ReadErrorLogWithNumberScript;
                args = new object[] { server, logNumber.Value };
            }
            else
            {
                script = ReadErrorLogScript;
                args = new object[] { server };
            }

            return InvokeCommand.InvokeScript(false, script, null, args);
        }

        /// <summary>
        /// Processes log entry objects, adding custom properties and writing to output.
        /// </summary>
        private void OutputEntries(Collection<PSObject> entries, string computerName, string serviceName, string domainInstanceName)
        {
            if (entries == null)
                return;

            foreach (PSObject entry in entries)
            {
                if (entry == null)
                    continue;

                WriteMessageAtLevel(
                    String.Format("Processing {0}", entry),
                    MessageLevel.Verbose, null);

                AddOrSetProperty(entry, "ComputerName", computerName);
                AddOrSetProperty(entry, "InstanceName", serviceName);
                AddOrSetProperty(entry, "SqlInstance", domainInstanceName);

                SetDefaultDisplayPropertySet(entry, DefaultDisplayProperties);

                WriteObject(entry);
            }
        }

        /// <summary>
        /// Gets a string property from a server object using PSObject property access.
        /// </summary>
        internal static string GetServerPropertySafe(object server, string propertyName)
        {
            if (server == null)
                return null;
            try
            {
                PSObject pso = PSObject.AsPSObject(server);
                PSPropertyInfo prop = pso.Properties[propertyName];
                if (prop != null && prop.Value != null)
                    return prop.Value.ToString();
            }
            catch (Exception)
            {
                // Property may not exist on this object type
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
