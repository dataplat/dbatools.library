using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Correlates SQL Server processes with Windows threads to identify which queries are consuming CPU resources.
    /// Queries both SQL Server process information and Windows thread performance data via WMI, then matches
    /// them together to show which SQL queries are consuming CPU at the operating system level.
    /// </summary>
    [Cmdlet("Get", "DbaCpuUsage")]
    public class GetDbaCpuUsageCommand : DbaBaseCmdlet
    {
        // Note: Inherits DbaBaseCmdlet rather than DbaInstanceCmdlet because this command
        // requires a separate Credential parameter for WMI/Windows authentication, which
        // conflicts with the "Credential" alias on SqlCredential in DbaInstanceCmdlet.

        private static readonly ScriptBlock ConnectScript =
            ScriptBlock.Create("param($i) Connect-DbaInstance -SqlInstance $i");
        private static readonly ScriptBlock ConnectWithCredScript =
            ScriptBlock.Create("param($i, $c) Connect-DbaInstance -SqlInstance $i -SqlCredential $c");
        private static readonly ScriptBlock GetProcessScript =
            ScriptBlock.Create("param($s) Get-DbaProcess -SqlInstance $s");
        private static readonly ScriptBlock GetCmObjectScript =
            ScriptBlock.Create("param($cn, $cls) Get-DbaCmObject -ComputerName $cn -ClassName $cls");
        private static readonly ScriptBlock GetCmObjectWithCredScript =
            ScriptBlock.Create("param($cn, $cls, $c) Get-DbaCmObject -ComputerName $cn -ClassName $cls -Credential $c");
        private static readonly ScriptBlock QueryScript =
            ScriptBlock.Create("param($s, $q) $s.Query($q)");
        private static readonly ScriptBlock SelectDefaultViewScript =
            ScriptBlock.Create("param($obj, $props) Select-DefaultView -InputObject $obj -Property $props");

        /// <summary>
        /// Default display properties for the output objects.
        /// </summary>
        private static readonly string[] DefaultDisplayProperties = new string[]
        {
            "ComputerName", "InstanceName", "SqlInstance", "Name",
            "ContextSwitchesPersec", "ElapsedTime", "IDProcess", "Spid",
            "PercentPrivilegedTime", "PercentProcessorTime", "PercentUserTime",
            "PriorityBase", "PriorityCurrent", "StartAddress",
            "ThreadStateValue", "ThreadWaitReasonValue", "Process", "Query"
        };

        #region Parameters

        /// <summary>
        /// The target SQL Server instance or instances.
        /// </summary>
        [Parameter(Mandatory = true, ValueFromPipeline = true)]
        public DbaInstanceParameter[] SqlInstance { get; set; }

        /// <summary>
        /// Login to the target instance using alternative SQL credentials.
        /// </summary>
        [Parameter()]
        public PSCredential SqlCredential { get; set; }

        /// <summary>
        /// Allows you to login to the Windows Server using alternative credentials for WMI access.
        /// </summary>
        [Parameter()]
        public PSCredential Credential { get; set; }

        /// <summary>
        /// Filters results to only show SQL Server threads with CPU usage at or above this percentage.
        /// </summary>
        [Parameter()]
        public int Threshold { get; set; }

        #endregion Parameters

        /// <summary>
        /// Processes each SQL Server instance, correlating SQL processes with Windows threads.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (TestFunctionInterrupt()) { return; }

            foreach (DbaInstanceParameter instance in SqlInstance)
            {
                object server;
                try
                {
                    server = ConnectInstance(instance);
                    if (server == null)
                    {
                        StopFunction(
                            String.Format("Failed to connect to {0}", instance),
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
                        errorRecord: new ErrorRecord(ex, "GetDbaCpuUsage_ConnectionError", ErrorCategory.ConnectionError, instance),
                        target: instance,
                        isContinue: true,
                        category: ErrorCategory.ConnectionError);
                    TestFunctionInterrupt();
                    continue;
                }

                try
                {
                    WriteMessageVerbose(String.Format("Processing instance {0}", instance));

                    // Get processes from the instance
                    Collection<PSObject> processes = InvokeGetDbaProcess(server);

                    // Get WMI thread data
                    Collection<PSObject> allThreads = InvokeGetCmObject(instance.ComputerName);

                    // Filter threads: name like 'sql*' and PercentProcessorTime >= Threshold
                    List<PSObject> threads = FilterThreads(allThreads, Threshold);

                    WriteMessageVerbose(String.Format("Found {0} SQL threads on {1}", threads.Count, instance.ComputerName));

                    // Get SPID-to-KPID mapping
                    int versionMajor = GetVersionMajor(server);
                    Collection<PSObject> spidCollection = GetSpidCollection(server, versionMajor);

                    // Get server identity properties
                    string computerName = GetServerProperty(server, "ComputerName");
                    string serviceName = GetServerProperty(server, "ServiceName");
                    string domainInstanceName = GetServerProperty(server, "DomainInstanceName");

                    foreach (PSObject thread in threads)
                    {
                        if (thread == null) continue;

                        // Get IDThread from the WMI object
                        object idThreadObj = GetPSObjectProperty(thread, "IDThread");
                        string idThreadStr = idThreadObj != null ? idThreadObj.ToString() : null;

                        // Find matching SPID
                        object spid = FindSpid(spidCollection, idThreadStr);

                        // Find matching process
                        PSObject process = FindProcess(processes, spid);

                        // Look up thread state and wait reason descriptions
                        object threadStateObj = GetPSObjectProperty(thread, "ThreadState");
                        object threadWaitReasonObj = GetPSObjectProperty(thread, "ThreadWaitReason");
                        string threadStateValue = GetThreadStateDescription(threadStateObj);
                        string threadWaitReasonValue = GetThreadWaitReasonDescription(threadWaitReasonObj);

                        // Get the query from the process
                        object lastQuery = null;
                        if (process != null)
                        {
                            lastQuery = GetPSObjectProperty(process, "LastQuery");
                        }

                        // Find processes matching the host process ID
                        object idProcessObj = GetPSObjectProperty(thread, "IDProcess");
                        List<PSObject> hostProcesses = FindProcessesByHostProcessId(processes, idProcessObj);

                        // Add custom properties to the thread object
                        AddOrSetProperty(thread, "ComputerName", computerName);
                        AddOrSetProperty(thread, "InstanceName", serviceName);
                        AddOrSetProperty(thread, "SqlInstance", domainInstanceName);
                        AddOrSetProperty(thread, "Processes", hostProcesses.ToArray());
                        AddOrSetProperty(thread, "ThreadStateValue", threadStateValue);
                        AddOrSetProperty(thread, "ThreadWaitReasonValue", threadWaitReasonValue);
                        AddOrSetProperty(thread, "Process", process);
                        AddOrSetProperty(thread, "Query", lastQuery);
                        AddOrSetProperty(thread, "Spid", spid);

                        // Apply Select-DefaultView
                        try
                        {
                            Collection<PSObject> viewResult = InvokeCommand.InvokeScript(
                                false, SelectDefaultViewScript, null,
                                new object[] { thread, DefaultDisplayProperties });
                            if (viewResult != null && viewResult.Count > 0)
                            {
                                WriteObject(viewResult[0]);
                            }
                            else
                            {
                                WriteObject(thread);
                            }
                        }
                        catch (Exception viewEx)
                        {
                            WriteMessageVerbose(String.Format("Select-DefaultView unavailable, using raw object: {0}", viewEx.Message));
                            WriteObject(thread);
                        }
                    }
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("Failed to retrieve CPU usage data from {0}", instance),
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
        /// Connects to a SQL Server instance via Connect-DbaInstance.
        /// </summary>
        private object ConnectInstance(DbaInstanceParameter instance)
        {
            Collection<PSObject> results;
            if (SqlCredential != null)
            {
                results = InvokeCommand.InvokeScript(false, ConnectWithCredScript, null, new object[] { instance, SqlCredential });
            }
            else
            {
                results = InvokeCommand.InvokeScript(false, ConnectScript, null, new object[] { instance });
            }

            if (results != null && results.Count > 0)
                return results[0].BaseObject;
            return null;
        }

        /// <summary>
        /// Invokes Get-DbaProcess to get processes from the server.
        /// </summary>
        private Collection<PSObject> InvokeGetDbaProcess(object server)
        {
            return InvokeCommand.InvokeScript(false, GetProcessScript, null, new object[] { server });
        }

        /// <summary>
        /// Invokes Get-DbaCmObject to get Win32_PerfFormattedData_PerfProc_Thread WMI objects.
        /// </summary>
        private Collection<PSObject> InvokeGetCmObject(string computerName)
        {
            if (Credential != null)
            {
                return InvokeCommand.InvokeScript(false, GetCmObjectWithCredScript, null,
                    new object[] { computerName, "Win32_PerfFormattedData_PerfProc_Thread", Credential });
            }
            return InvokeCommand.InvokeScript(false, GetCmObjectScript, null,
                new object[] { computerName, "Win32_PerfFormattedData_PerfProc_Thread" });
        }

        /// <summary>
        /// Filters WMI thread objects: Name like 'sql*' and PercentProcessorTime >= threshold.
        /// </summary>
        internal static List<PSObject> FilterThreads(Collection<PSObject> allThreads, int threshold)
        {
            List<PSObject> filtered = new List<PSObject>();
            if (allThreads == null) return filtered;

            foreach (PSObject thread in allThreads)
            {
                if (thread == null) continue;

                object nameObj = GetPSObjectProperty(thread, "Name");
                if (nameObj == null) continue;
                string name = nameObj.ToString();
                if (!name.StartsWith("sql", StringComparison.OrdinalIgnoreCase)) continue;

                object pptObj = GetPSObjectProperty(thread, "PercentProcessorTime");
                if (pptObj == null) continue;

                int ppt;
                if (pptObj is int intVal)
                {
                    ppt = intVal;
                }
                else if (pptObj is long longVal)
                {
                    ppt = (int)longVal;
                }
                else if (pptObj is ulong ulongVal)
                {
                    ppt = (int)ulongVal;
                }
                else
                {
                    if (!Int32.TryParse(pptObj.ToString(), out ppt))
                        continue;
                }

                if (ppt >= threshold)
                {
                    filtered.Add(thread);
                }
            }
            return filtered;
        }

        /// <summary>
        /// Gets the SPID-to-KPID mapping from SQL Server.
        /// Uses different queries for SQL 2000 (version 8) vs newer versions.
        /// </summary>
        private Collection<PSObject> GetSpidCollection(object server, int versionMajor)
        {
            string sql;
            if (versionMajor == 8)
            {
                sql = "SELECT spid, kpid FROM sysprocesses";
            }
            else
            {
                sql = @"SELECT t.os_thread_id AS kpid, s.session_id AS spid
FROM sys.dm_exec_sessions s
JOIN sys.dm_exec_requests er ON s.session_id = er.session_id
JOIN sys.dm_os_workers w ON er.task_address = w.task_address
JOIN sys.dm_os_threads t ON w.thread_address = t.thread_address";
            }

            return InvokeCommand.InvokeScript(false, QueryScript, null, new object[] { server, sql });
        }

        /// <summary>
        /// Finds the SPID matching a given thread ID (kpid) in the SPID collection.
        /// </summary>
        internal static object FindSpid(Collection<PSObject> spidCollection, string idThread)
        {
            if (spidCollection == null || idThread == null) return null;

            foreach (PSObject row in spidCollection)
            {
                if (row == null) continue;
                object kpidVal = GetRowColumnValue(row, "kpid");
                if (kpidVal != null && kpidVal.ToString() == idThread)
                {
                    return GetRowColumnValue(row, "spid");
                }
            }
            return null;
        }

        /// <summary>
        /// Finds a process matching the given SPID from the process collection.
        /// </summary>
        internal static PSObject FindProcess(Collection<PSObject> processes, object spid)
        {
            if (processes == null || spid == null) return null;
            string spidStr = spid.ToString();

            foreach (PSObject proc in processes)
            {
                if (proc == null) continue;
                object procSpid = GetPSObjectProperty(proc, "Spid");
                if (procSpid != null && procSpid.ToString() == spidStr)
                    return proc;
            }
            return null;
        }

        /// <summary>
        /// Finds all processes matching the given host process ID.
        /// </summary>
        internal static List<PSObject> FindProcessesByHostProcessId(Collection<PSObject> processes, object idProcess)
        {
            List<PSObject> result = new List<PSObject>();
            if (processes == null || idProcess == null) return result;
            string idStr = idProcess.ToString();

            foreach (PSObject proc in processes)
            {
                if (proc == null) continue;
                object hostPid = GetPSObjectProperty(proc, "HostProcessID");
                if (hostPid != null && hostPid.ToString() == idStr)
                    result.Add(proc);
            }
            return result;
        }

        /// <summary>
        /// Gets the major version number from the server object.
        /// </summary>
        internal static int GetVersionMajor(object server)
        {
            if (server == null) return 0;
            try
            {
                PSObject pso = PSObject.AsPSObject(server);
                PSPropertyInfo prop = pso.Properties["VersionMajor"];
                if (prop != null && prop.Value != null)
                {
                    if (prop.Value is int intVal)
                        return intVal;
                    int result;
                    if (Int32.TryParse(prop.Value.ToString(), out result))
                        return result;
                }
            }
            catch (Exception)
            {
                // Property may not exist
            }
            return 0;
        }

        /// <summary>
        /// Gets a property value from a server object using PSObject property access.
        /// </summary>
        internal static string GetServerProperty(object server, string propertyName)
        {
            if (server == null) return String.Empty;
            try
            {
                PSObject pso = PSObject.AsPSObject(server);
                PSPropertyInfo prop = pso.Properties[propertyName];
                if (prop != null && prop.Value != null)
                    return prop.Value.ToString();
            }
            catch (Exception)
            {
                // Property may not exist
            }
            return String.Empty;
        }

        /// <summary>
        /// Gets a property value from a PSObject.
        /// </summary>
        internal static object GetPSObjectProperty(PSObject obj, string propertyName)
        {
            if (obj == null) return null;
            try
            {
                PSPropertyInfo prop = obj.Properties[propertyName];
                if (prop != null)
                {
                    object val = prop.Value;
                    if (val == DBNull.Value) return null;
                    return val;
                }
            }
            catch (Exception)
            {
                // Property may not exist
            }
            return null;
        }

        /// <summary>
        /// Gets a column value from a query result row. Handles both DataRow and PSObject.
        /// </summary>
        internal static object GetRowColumnValue(PSObject row, string columnName)
        {
            if (row == null) return null;

            object baseObj = row.BaseObject;
            if (baseObj is DataRow dataRow)
            {
                try
                {
                    object val = dataRow[columnName];
                    if (val == DBNull.Value) return null;
                    return val;
                }
                catch (Exception)
                {
                    return null;
                }
            }

            // Fallback: PSObject property
            try
            {
                PSPropertyInfo prop = row.Properties[columnName];
                if (prop != null)
                {
                    object val = prop.Value;
                    if (val == DBNull.Value) return null;
                    return val;
                }
            }
            catch (Exception)
            {
                // Ignore
            }
            return null;
        }

        /// <summary>
        /// Adds or updates a NoteProperty on a PSObject.
        /// </summary>
        private static void AddOrSetProperty(PSObject obj, string name, object value)
        {
            try
            {
                PSPropertyInfo existing = obj.Properties[name];
                if (existing != null)
                {
                    existing.Value = value;
                    return;
                }
            }
            catch (Exception)
            {
                // Property doesn't exist, add it
            }
            obj.Properties.Add(new PSNoteProperty(name, value));
        }

        /// <summary>
        /// Gets a human-readable description for a thread state value.
        /// Maps numeric ThreadState values to descriptive strings.
        /// </summary>
        internal static string GetThreadStateDescription(object threadState)
        {
            if (threadState == null) return null;
            int state;
            if (threadState is int intVal)
            {
                state = intVal;
            }
            else if (threadState is uint uintVal)
            {
                state = (int)uintVal;
            }
            else if (threadState is long longVal)
            {
                state = (int)longVal;
            }
            else
            {
                if (!Int32.TryParse(threadState.ToString(), out state))
                    return null;
            }

            switch (state)
            {
                case 0: return "Initialized. It is recognized by the microkernel.";
                case 1: return "Ready. It is prepared to run on the next available processor.";
                case 2: return "Running. It is executing.";
                case 3: return "Standby. It is about to run. Only one thread may be in this state at a time.";
                case 4: return "Terminated. It is finished executing.";
                case 5: return "Waiting. It is not ready for the processor. When ready, it will be rescheduled.";
                case 6: return "Transition. The thread is waiting for resources other than the processor.";
                case 7: return "Unknown. The thread state is unknown.";
                default: return null;
            }
        }

        /// <summary>
        /// Gets a human-readable description for a thread wait reason value.
        /// Maps numeric ThreadWaitReason values to descriptive strings.
        /// </summary>
        internal static string GetThreadWaitReasonDescription(object threadWaitReason)
        {
            if (threadWaitReason == null) return null;
            int reason;
            if (threadWaitReason is int intVal)
            {
                reason = intVal;
            }
            else if (threadWaitReason is uint uintVal)
            {
                reason = (int)uintVal;
            }
            else if (threadWaitReason is long longVal)
            {
                reason = (int)longVal;
            }
            else
            {
                if (!Int32.TryParse(threadWaitReason.ToString(), out reason))
                    return null;
            }

            switch (reason)
            {
                case 0: return "Executive";
                case 1: return "FreePage";
                case 2: return "PageIn";
                case 3: return "PoolAllocation";
                case 4: return "ExecutionDelay";
                case 5: return "FreePage";
                case 6: return "PageIn";
                case 7: return "Executive";
                case 8: return "FreePage";
                case 9: return "PageIn";
                case 10: return "PoolAllocation";
                case 11: return "ExecutionDelay";
                case 12: return "FreePage";
                case 13: return "PageIn";
                case 14: return "EventPairHigh";
                case 15: return "EventPairLow";
                case 16: return "LPCReceive";
                case 17: return "LPCReply";
                case 18: return "VirtualMemory";
                case 19: return "PageOut";
                case 20: return "Unknown";
                default: return null;
            }
        }

        #endregion Helpers
    }
}
