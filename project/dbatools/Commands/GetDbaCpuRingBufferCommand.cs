using System;
using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Retrieves historical CPU utilization data from SQL Server's internal ring buffer for performance analysis.
    /// Queries sys.dm_os_ring_buffers to extract detailed CPU utilization history, providing minute-by-minute
    /// CPU usage breakdowns that help identify performance patterns and resource contention.
    /// </summary>
    [Cmdlet("Get", "DbaCpuRingBuffer")]
    public class GetDbaCpuRingBufferCommand : DbaInstanceCmdlet
    {
        private static readonly ScriptBlock ConnectScript =
            ScriptBlock.Create("param($i) Connect-DbaInstance -SqlInstance $i -MinimumVersion 9");
        private static readonly ScriptBlock ConnectWithCredScript =
            ScriptBlock.Create("param($i, $c) Connect-DbaInstance -SqlInstance $i -SqlCredential $c -MinimumVersion 9");
        private static readonly ScriptBlock TimestampQueryScript =
            ScriptBlock.Create("param($s, $q) ($s.Query($q))[0]");
        private static readonly ScriptBlock QueryScript =
            ScriptBlock.Create("param($s, $q) $s.Query($q)");

        #region Parameters

        /// <summary>
        /// Specifies how many minutes of historical CPU data to retrieve from the ring buffer.
        /// Defaults to 60 minutes.
        /// </summary>
        [Parameter()]
        public int CollectionMinutes { get; set; } = 60;

        #endregion Parameters

        /// <summary>
        /// Processes each SQL Server instance, querying the ring buffer for CPU utilization data.
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
                        errorRecord: new ErrorRecord(ex, "GetDbaCpuRingBuffer_ConnectionError", ErrorCategory.ConnectionError, instance),
                        target: instance,
                        isContinue: true,
                        category: ErrorCategory.ConnectionError);
                    TestFunctionInterrupt();
                    continue;
                }

                try
                {
                    // Get version major to determine timestamp query
                    int versionMajor = GetVersionMajor(server);

                    // Get current timestamp from sys.dm_os_sys_info
                    double currentTimestamp = GetCurrentTimestamp(server, versionMajor);

                    WriteMessageVerbose(
                        String.Format("Using current timestamp of {0}", currentTimestamp));

                    // Build and execute the ring buffer query
                    string sql = BuildRingBufferQuery(currentTimestamp, CollectionMinutes);

                    WriteMessageVerbose(
                        String.Format("Executing Sql Statement: {0}", sql));

                    // Get server identity properties
                    string computerName = GetServerProperty(server, "ComputerName");
                    string serviceName = GetServerProperty(server, "ServiceName");
                    string domainInstanceName = GetServerProperty(server, "DomainInstanceName");

                    // Execute the query and output results
                    Collection<PSObject> rows = ExecuteQuery(server, sql);
                    if (rows != null)
                    {
                        foreach (PSObject row in rows)
                        {
                            if (row == null) continue;

                            PSObject output = new PSObject();
                            output.Properties.Add(new PSNoteProperty("ComputerName", computerName));
                            output.Properties.Add(new PSNoteProperty("InstanceName", serviceName));
                            output.Properties.Add(new PSNoteProperty("SqlInstance", domainInstanceName));
                            output.Properties.Add(new PSNoteProperty("RecordId", GetRowValue(row, "record_id")));
                            output.Properties.Add(new PSNoteProperty("EventTime", GetRowValue(row, "EventTime")));
                            output.Properties.Add(new PSNoteProperty("SQLProcessUtilization", GetRowValue(row, "SQLProcessUtilization")));
                            output.Properties.Add(new PSNoteProperty("OtherProcessUtilization", GetRowValue(row, "OtherProcessUtilization")));
                            output.Properties.Add(new PSNoteProperty("SystemIdle", GetRowValue(row, "SystemIdle")));

                            WriteObject(output);
                        }
                    }
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("Failed to retrieve CPU ring buffer data from {0}", instance),
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
        /// Connects to a SQL Server instance via Connect-DbaInstance with minimum version 9.
        /// </summary>
        private object ConnectInstance(DbaInstanceParameter instance)
        {
            Collection<PSObject> results;
            if (SqlCredential != null)
            {
                results = InvokeCommand.InvokeScript(true, ConnectWithCredScript, null, new object[] { instance, SqlCredential });
            }
            else
            {
                results = InvokeCommand.InvokeScript(true, ConnectScript, null, new object[] { instance });
            }

            if (results != null && results.Count > 0)
                return results[0].BaseObject;
            return null;
        }

        /// <summary>
        /// Gets the major version number from the server object using direct PSObject access.
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
        /// Gets the current timestamp from sys.dm_os_sys_info. Uses different queries
        /// for SQL Server 2008 (version 10) and later vs SQL Server 2005 (version 9).
        /// The timestamp is a float from SQL but the PS1 interpolates it directly into
        /// the subsequent query string, so we preserve the double precision.
        /// </summary>
        private double GetCurrentTimestamp(object server, int versionMajor)
        {
            string tsQuery;
            if (versionMajor > 9)
            {
                tsQuery = "SELECT cpu_ticks / CONVERT(FLOAT, (cpu_ticks / ms_ticks)) AS TimeStamp FROM sys.dm_os_sys_info";
            }
            else
            {
                tsQuery = "SELECT cpu_ticks / CONVERT(FLOAT, cpu_ticks_in_ms) AS TimeStamp FROM sys.dm_os_sys_info";
            }

            Collection<PSObject> results = InvokeCommand.InvokeScript(true, TimestampQueryScript, null, new object[] { server, tsQuery });
            if (results != null && results.Count > 0 && results[0] != null)
            {
                object baseObj = results[0].BaseObject;
                if (baseObj is DataRow dataRow)
                {
                    object tsVal = dataRow["TimeStamp"];
                    return Convert.ToDouble(tsVal, CultureInfo.InvariantCulture);
                }
                return Convert.ToDouble(baseObj, CultureInfo.InvariantCulture);
            }
            throw new InvalidOperationException("Failed to retrieve current timestamp from sys.dm_os_sys_info");
        }

        /// <summary>
        /// Builds the ring buffer query SQL using the current timestamp and collection minutes.
        /// The timestamp is a double to preserve the float precision from SQL Server,
        /// matching how PowerShell interpolates the value directly into the query string.
        /// </summary>
        internal static string BuildRingBufferQuery(double currentTimestamp, int collectionMinutes)
        {
            // Use InvariantCulture to ensure decimal point (not comma) and no scientific notation
            string tsString = currentTimestamp.ToString("F0", CultureInfo.InvariantCulture);
            return String.Format(
                CultureInfo.InvariantCulture,
                @"WITH RingBufferSchedulerMonitor AS
                (
                    SELECT
                        timestamp,
                        CONVERT(XML, record) AS record
                    FROM sys.dm_os_ring_buffers
                    WHERE (ring_buffer_type = N'RING_BUFFER_SCHEDULER_MONITOR')
                    AND (record LIKE '%%')
                ), RingBufferSchedulerMonitorValues AS
                (
                    SELECT
                        record.value('(./Record/@id)[1]', 'int') AS record_id,
                        record.value('(./Record/SchedulerMonitorEvent/SystemHealth/SystemIdle)[1]', 'int') AS SystemIdle,
                        record.value('(./Record/SchedulerMonitorEvent/SystemHealth/ProcessUtilization)[1]', 'int') AS SQLProcessUtilization,
                        timestamp,
                        DATEADD(ss, (-1 * ({0} - [timestamp]))/1000, GETDATE()) AS EventTime
                    FROM RingBufferSchedulerMonitor
                )
                SELECT
                    SERVERPROPERTY('ServerName') AS ServerName,
                    record_id,
                    EventTime,
                    SQLProcessUtilization,
                    SystemIdle,
                    100 - SystemIdle - SQLProcessUtilization AS OtherProcessUtilization
                FROM RingBufferSchedulerMonitorValues
                WHERE EventTime > DATEADD(MINUTE, -{1}, GETDATE());",
                tsString,
                collectionMinutes);
        }

        /// <summary>
        /// Executes a SQL query against the server object using $server.Query().
        /// </summary>
        private Collection<PSObject> ExecuteQuery(object server, string sql)
        {
            return InvokeCommand.InvokeScript(true, QueryScript, null, new object[] { server, sql });
        }

        /// <summary>
        /// Gets a property value from a server object using direct PSObject property access.
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
                // Property may not exist on this object type
            }
            return String.Empty;
        }

        /// <summary>
        /// Gets a value from a query result row. Handles both DataRow and PSObject.
        /// </summary>
        internal static object GetRowValue(PSObject row, string columnName)
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

            // Fallback: try PSObject property
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

        #endregion Helpers
    }
}
