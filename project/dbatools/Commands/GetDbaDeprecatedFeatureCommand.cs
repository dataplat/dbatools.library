using System;
using System.Collections.ObjectModel;
using System.Data;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Identifies deprecated SQL Server features currently in use with their usage counts from performance counters.
    /// Queries sys.dm_os_performance_counters to find which deprecated features have been used on the instance.
    /// </summary>
    [Cmdlet("Get", "DbaDeprecatedFeature")]
    public class GetDbaDeprecatedFeatureCommand : DbaInstanceCmdlet
    {
        private static readonly ScriptBlock ConnectScript =
            ScriptBlock.Create("param($i) Connect-DbaInstance -SqlInstance $i -MinimumVersion 9");
        private static readonly ScriptBlock ConnectWithCredScript =
            ScriptBlock.Create("param($i, $c) Connect-DbaInstance -SqlInstance $i -SqlCredential $c -MinimumVersion 9");
        private static readonly ScriptBlock QueryScript =
            ScriptBlock.Create("param($s, $q) $s.Query($q)");

        /// <summary>
        /// The SQL query to retrieve deprecated features with usage counts greater than zero.
        /// </summary>
        internal static readonly string DeprecatedFeatureQuery =
            "SELECT LTRIM(RTRIM(instance_name)) AS DeprecatedFeature, cntr_value AS UsageCount FROM sys.dm_os_performance_counters WHERE object_name LIKE '%SQL%Deprecated Features%' AND cntr_value > 0";

        /// <summary>
        /// Processes each SQL Server instance, querying for deprecated feature usage.
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
                        errorRecord: new ErrorRecord(ex, "GetDbaDeprecatedFeature_ConnectionError", ErrorCategory.ConnectionError, instance),
                        target: instance,
                        isContinue: true,
                        category: ErrorCategory.ConnectionError);
                    TestFunctionInterrupt();
                    continue;
                }

                try
                {
                    string computerName = GetServerProperty(server, "ComputerName");
                    string serviceName = GetServerProperty(server, "ServiceName");
                    string domainInstanceName = GetServerProperty(server, "DomainInstanceName");

                    Collection<PSObject> rows = ExecuteQuery(server, DeprecatedFeatureQuery);
                    if (rows != null)
                    {
                        foreach (PSObject row in rows)
                        {
                            if (row == null) continue;

                            PSObject output = new PSObject();
                            output.Properties.Add(new PSNoteProperty("ComputerName", computerName));
                            output.Properties.Add(new PSNoteProperty("InstanceName", serviceName));
                            output.Properties.Add(new PSNoteProperty("SqlInstance", domainInstanceName));
                            output.Properties.Add(new PSNoteProperty("DeprecatedFeature", GetRowValue(row, "DeprecatedFeature")));
                            output.Properties.Add(new PSNoteProperty("UsageCount", GetRowValue(row, "UsageCount")));

                            WriteObject(output);
                        }
                    }
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("Failed to retrieve deprecated features from {0}", instance),
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
        /// Executes a SQL query against the server object using $server.Query().
        /// </summary>
        private Collection<PSObject> ExecuteQuery(object server, string sql)
        {
            return InvokeCommand.InvokeScript(false, QueryScript, null, new object[] { server, sql });
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
