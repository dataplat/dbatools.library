using System;
using System.Collections.Generic;
using System.Data;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Tests if files or directories are accessible to the SQL Server service account
    /// using the master.dbo.xp_fileexist extended stored procedure.
    /// </summary>
    [OutputType(typeof(bool))]
    [OutputType(typeof(PSObject))]
    [Cmdlet("Test", "DbaPath")]
    public class TestDbaPathCommand : DbaInstanceCmdlet
    {
        /// <summary>
        /// Specifies the file or directory paths to test for accessibility from the
        /// SQL Server service account's perspective.
        /// </summary>
        [Parameter(Mandatory = true)]
        public object Path { get; set; }

        /// <summary>
        /// The batch size for grouping paths into a single query.
        /// </summary>
        private const int GroupSize = 100;

        /// <summary>
        /// Maximum path length accepted. Windows long path max is 32,767 characters.
        /// </summary>
        private const int MaxPathLength = 32767;

        /// <summary>
        /// Tracks whether the raw Path input was an array, which affects output type.
        /// </summary>
        private bool _rawPathIsArray;

        /// <summary>
        /// The paths cast to a string array for processing.
        /// </summary>
        private string[] _paths;

        /// <summary>
        /// Resolves the Path parameter into a string array and detects if input was an array.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            // Detect whether the raw input is an array (mirrors PS1: -not($RawPath -is [array]))
            _rawPathIsArray = IsArrayInput(Path);

            // Cast to string array (mirrors PS1: [string[]]$Path)
            _paths = ConvertToStringArray(Path);

            if (_paths == null || _paths.Length == 0)
            {
                StopFunction(
                    "Path parameter must contain at least one path.",
                    target: Path,
                    category: ErrorCategory.InvalidArgument);
                return;
            }
        }

        /// <summary>
        /// Processes each SQL Server instance, testing path accessibility via xp_fileexist.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (TestFunctionInterrupt())
                return;
            if (_paths == null || _paths.Length == 0)
                return;

            foreach (DbaInstanceParameter instance in SqlInstance)
            {
                // Connect via Connect-DbaInstance (still a PS1 function)
                object server = null;
                try
                {
                    server = ConnectInstance(instance);
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("Failed to connect to {0}", instance),
                        errorRecord: new ErrorRecord(ex, "TestDbaPath_ConnectionError", ErrorCategory.ConnectionError, instance),
                        target: instance,
                        isContinue: true,
                        category: ErrorCategory.ConnectionError);
                    TestFunctionInterrupt();
                    continue;
                }

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

                // Determine if we should return simple booleans (single path, single instance, non-array input)
                bool returnSimple = _paths.Length == 1
                    && SqlInstance.Length == 1
                    && !_rawPathIsArray;

                // Resolve server properties once per instance (used in multi-path output)
                string serverName = null;
                string serviceName = null;
                string computerName = null;
                if (!returnSimple)
                {
                    serverName = GetServerProperty(server, "Name") as string;
                    serviceName = GetServerProperty(server, "ServiceName") as string;
                    computerName = GetServerProperty(server, "ComputerName") as string;
                }

                // Process paths in batches of GroupSize
                for (int batchStart = 0; batchStart < _paths.Length; batchStart += GroupSize)
                {
                    int batchEnd = Math.Min(batchStart + GroupSize, _paths.Length);
                    int batchSize = batchEnd - batchStart;

                    // Build the SQL batch, tracking which path indices are valid
                    List<string> queryParts = new List<string>(batchSize);
                    List<int> validIndices = new List<int>(batchSize);
                    for (int i = batchStart; i < batchEnd; i++)
                    {
                        if (String.IsNullOrEmpty(_paths[i]))
                        {
                            StopFunction(
                                "Path cannot be null or empty.",
                                target: _paths[i],
                                isContinue: true,
                                category: ErrorCategory.InvalidArgument);
                            TestFunctionInterrupt();
                            continue;
                        }
                        if (_paths[i].Length > MaxPathLength)
                        {
                            StopFunction(
                                String.Format("Path exceeds maximum length ({0} chars)", _paths[i].Length),
                                target: _paths[i],
                                isContinue: true,
                                category: ErrorCategory.InvalidArgument);
                            TestFunctionInterrupt();
                            continue;
                        }
                        // Reject paths containing null bytes to prevent truncation attacks
                        if (_paths[i].IndexOf('\0') >= 0)
                        {
                            StopFunction(
                                String.Format("Path contains invalid null character: {0}", _paths[i]),
                                target: _paths[i],
                                isContinue: true,
                                category: ErrorCategory.InvalidArgument);
                            TestFunctionInterrupt();
                            continue;
                        }
                        // Escape single quotes to prevent SQL injection
                        string escapedPath = _paths[i].Replace("'", "''");
                        queryParts.Add(String.Format("EXEC master.dbo.xp_fileexist N'{0}'", escapedPath));
                        validIndices.Add(i);
                    }
                    if (queryParts.Count == 0)
                        continue;
                    string sql = String.Join(";", queryParts.ToArray());

                    // Execute via ConnectionContext.ExecuteWithResults
                    DataSet batchResult;
                    try
                    {
                        batchResult = ExecuteWithResults(server, sql);
                    }
                    catch (Exception ex)
                    {
                        StopFunction(
                            String.Format("Failed to execute xp_fileexist on {0}", instance),
                            exception: ex,
                            target: instance,
                            isContinue: true);
                        TestFunctionInterrupt();
                        break;
                    }

                    if (returnSimple)
                    {
                        // Single path, single instance, non-array: return bool
                        if (batchResult.Tables.Count > 0 && batchResult.Tables[0].Rows.Count > 0)
                        {
                            DataRow row = batchResult.Tables[0].Rows[0];
                            bool fileExists = Convert.ToInt32(row[0]) == 1;
                            bool isDirectory = Convert.ToInt32(row[1]) == 1;
                            WriteObject(fileExists || isDirectory);
                        }
                        else
                        {
                            WriteObject(false);
                        }
                        return;
                    }
                    else
                    {
                        // Multiple paths/instances/array: return PSCustomObjects
                        // xp_fileexist returns one result set per EXEC call
                        for (int tableIdx = 0; tableIdx < batchResult.Tables.Count && tableIdx < validIndices.Count; tableIdx++)
                        {
                            DataTable table = batchResult.Tables[tableIdx];
                            if (table.Rows.Count > 0)
                            {
                                DataRow row = table.Rows[0];
                                bool fileExists = Convert.ToInt32(row[0]) == 1;
                                bool isDirectory = Convert.ToInt32(row[1]) == 1;
                                bool doesPass = fileExists || isDirectory;

                                PSObject result = new PSObject();
                                result.Properties.Add(new PSNoteProperty("SqlInstance", serverName));
                                result.Properties.Add(new PSNoteProperty("InstanceName", serviceName));
                                result.Properties.Add(new PSNoteProperty("ComputerName", computerName));
                                result.Properties.Add(new PSNoteProperty("FilePath", _paths[validIndices[tableIdx]]));
                                result.Properties.Add(new PSNoteProperty("FileExists", doesPass));
                                result.Properties.Add(new PSNoteProperty("IsContainer", isDirectory));
                                WriteObject(result);
                            }
                        }
                    }
                }
            }
        }

        #region Private Helpers

        /// <summary>
        /// Connects to a SQL Server instance by invoking Connect-DbaInstance.
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

            var results = InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, args);
            if (results != null && results.Count > 0)
                return results[0].BaseObject;
            return null;
        }

        /// <summary>
        /// Executes a SQL query via the server's ConnectionContext.ExecuteWithResults method.
        /// Uses reflection to call $server.ConnectionContext.ExecuteWithResults($sql).
        /// Parameterization is not possible here because SMO's ExecuteWithResults only
        /// accepts raw SQL text and does not support SqlParameter bindings.
        /// The caller is responsible for escaping string literals (single-quote doubling).
        /// </summary>
        private static DataSet ExecuteWithResults(object server, string sql)
        {
            // Get ConnectionContext property
            object connectionContext = server.GetType().GetProperty("ConnectionContext").GetValue(server);
            if (connectionContext == null)
                throw new InvalidOperationException("Server object has no ConnectionContext property");

            // Call ExecuteWithResults method
            var method = connectionContext.GetType().GetMethod("ExecuteWithResults", new Type[] { typeof(string) });
            if (method == null)
                throw new InvalidOperationException("ConnectionContext does not support ExecuteWithResults");

            return (DataSet)method.Invoke(connectionContext, new object[] { sql });
        }

        /// <summary>
        /// Gets a property value from the server object using reflection.
        /// </summary>
        private static object GetServerProperty(object server, string propertyName)
        {
            try
            {
                var prop = server.GetType().GetProperty(propertyName);
                if (prop != null)
                    return prop.GetValue(server);
            }
            catch (System.Reflection.TargetInvocationException)
            {
                // Property getter may throw for disconnected/unavailable servers
            }
            catch (MemberAccessException)
            {
                // Property may not be accessible in constrained contexts
            }
            catch (InvalidOperationException)
            {
                // Property may not be valid in current server state
            }
            return null;
        }

        /// <summary>
        /// Determines if the input object represents an array type.
        /// Mirrors PS1: $RawPath -is [array]
        /// </summary>
        private static bool IsArrayInput(object input)
        {
            if (input == null)
                return false;

            // Unwrap PSObject
            object baseObj = input;
            if (input is PSObject psObj)
                baseObj = psObj.BaseObject;

            return baseObj is Array;
        }

        /// <summary>
        /// Converts the Path parameter (which may be a single value, array, or PSObject-wrapped)
        /// into a string array. Mirrors PS1: [string[]]$Path
        /// </summary>
        private static string[] ConvertToStringArray(object input)
        {
            if (input == null)
                return new string[0];

            // Unwrap PSObject
            object baseObj = input;
            if (input is PSObject psObj)
                baseObj = psObj.BaseObject;

            // If it's already a string
            if (baseObj is string singleStr)
                return new string[] { singleStr };

            // If it's an array of objects
            if (baseObj is object[] objArr)
            {
                List<string> result = new List<string>(objArr.Length);
                foreach (object item in objArr)
                {
                    if (item is PSObject psi)
                        result.Add(psi.BaseObject.ToString());
                    else if (item != null)
                        result.Add(item.ToString());
                }
                return result.ToArray();
            }

            // If it's a string array
            if (baseObj is string[] strArr)
                return strArr;

            // If it's an IEnumerable
            if (baseObj is System.Collections.IEnumerable enumerable)
            {
                List<string> result = new List<string>();
                foreach (object item in enumerable)
                {
                    if (item is PSObject psi)
                        result.Add(psi.BaseObject.ToString());
                    else if (item != null)
                        result.Add(item.ToString());
                }
                return result.ToArray();
            }

            // Fallback: single value
            return new string[] { baseObj.ToString() };
        }

        #endregion
    }
}
