using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Management.Automation;
using SmaRunspaces = System.Management.Automation.Runspaces;
using System.Text.RegularExpressions;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;
using Microsoft.Data.SqlClient;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Executes T-SQL queries, scripts, and stored procedures against SQL Server instances
    /// with parameterized query support. This is the primary dbatools command for running
    /// custom SQL against your environment, supporting queries from strings, files, URLs,
    /// or SQL Server Management Objects.
    /// </summary>
    [Cmdlet("Invoke", "DbaQuery", DefaultParameterSetName = "Query")]
    public class InvokeDbaQueryCommand : DbaBaseCmdlet
    {
        #region Parameters

        /// <summary>
        /// The target SQL Server instance or instances.
        /// </summary>
        [Parameter(ValueFromPipeline = true, ParameterSetName = "Query", Position = 0)]
        [Parameter(ValueFromPipeline = true, ParameterSetName = "File", Position = 0)]
        [Parameter(ValueFromPipeline = true, ParameterSetName = "SMO", Position = 0)]
        public DbaInstanceParameter[] SqlInstance { get; set; }

        /// <summary>
        /// Credential object used to connect to the SQL Server Instance.
        /// </summary>
        [Parameter()]
        public PSCredential SqlCredential { get; set; }

        /// <summary>
        /// Specifies the target database context for query execution.
        /// </summary>
        [Parameter()]
        public string Database { get; set; }

        /// <summary>
        /// Contains the T-SQL commands to execute against the target instances.
        /// </summary>
        [Parameter(Mandatory = true, ParameterSetName = "Query")]
        public string Query { get; set; }

        /// <summary>
        /// Sets the command timeout in seconds before the query is cancelled.
        /// </summary>
        [Parameter()]
        public int QueryTimeout { get; set; } = -1;

        /// <summary>
        /// Specifies file paths, URLs, or directories containing SQL scripts to execute.
        /// </summary>
        [Parameter(Mandatory = true, ParameterSetName = "File")]
        [Alias("InputFile")]
        public object[] File { get; set; }

        /// <summary>
        /// Accepts SQL Server Management Objects (SMO) that will be scripted out and executed.
        /// Typed as object[] because the SMO types are loaded dynamically.
        /// PowerShell's parameter binder handles the type coercion from SqlSmoObject.
        /// </summary>
        [Parameter(Mandatory = true, ParameterSetName = "SMO")]
        public object[] SqlObject { get; set; }

        /// <summary>
        /// Controls the format of returned query results.
        /// </summary>
        [Parameter()]
        [ValidateSet("DataSet", "DataTable", "DataRow", "PSObject", "PSObjectArray", "SingleValue")]
        public string As { get; set; } = "DataRow";

        /// <summary>
        /// Provides parameters for safe execution of queries with dynamic values.
        /// </summary>
        [Parameter()]
        [Alias("SqlParameters")]
        public PSObject[] SqlParameter { get; set; }

        /// <summary>
        /// Adds the source SQL Server instance name as a column to query results.
        /// </summary>
        [Parameter()]
        public SwitchParameter AppendServerInstance { get; set; }

        /// <summary>
        /// Captures and returns T-SQL PRINT statements and RAISERROR messages along with query results.
        /// </summary>
        [Parameter()]
        public SwitchParameter MessagesToOutput { get; set; }

        /// <summary>
        /// Accepts database objects from the pipeline, typically from Get-DbaDatabase.
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        public object[] InputObject { get; set; }

        /// <summary>
        /// Sets the connection to use ReadOnly application intent.
        /// </summary>
        [Parameter()]
        public SwitchParameter ReadOnly { get; set; }

        /// <summary>
        /// Enables syntax and semantic validation without executing the actual statements.
        /// </summary>
        [Parameter()]
        public SwitchParameter NoExec { get; set; }

        /// <summary>
        /// Prepends SET QUOTED_IDENTIFIER ON to each batch before execution.
        /// </summary>
        [Parameter()]
        public SwitchParameter QuotedIdentifier { get; set; }

        /// <summary>
        /// Adds custom connection string parameters for specialized connection requirements.
        /// </summary>
        [Parameter()]
        public string AppendConnectionString { get; set; }

        /// <summary>
        /// Defines how the Query parameter should be interpreted.
        /// </summary>
        [Parameter()]
        public CommandType CommandType { get; set; } = CommandType.Text;

        #endregion Parameters

        #region Private State

        private List<string> _resolvedFiles;
        private List<string> _temporaryFiles;
        private static readonly Regex GoSplitterRegex = new Regex(@"(?smi)^[\s]*GO[\s]*$", RegexOptions.Compiled);

        #endregion Private State

        #region BeginProcessing

        /// <summary>
        /// Validates parameters and resolves file inputs.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            // Validate SqlParameter input
            if (TestBound("SqlParameter") && SqlParameter != null && SqlParameter.Length > 0)
            {
                object first = GetBaseObject(SqlParameter[0]);
                bool isValidSqlParam = first is SqlParameter;
                bool isValidDict = first is IDictionary;
                bool isDictArray = false;

                // Check if SqlParameter itself is an array of IDictionary
                if (SqlParameter.Length > 1 && first is IDictionary)
                {
                    // Multiple dictionaries passed - that's not allowed
                    isDictArray = true;
                }

                if (!isValidSqlParam && (!isValidDict || isDictArray))
                {
                    StopFunction("SqlParameter only accepts a single hashtable or Microsoft.Data.SqlClient.SqlParameter");
                    return;
                }
            }

            _resolvedFiles = new List<string>();
            _temporaryFiles = new List<string>();

            if (TestBound("File"))
            {
                ResolveFileInputs();
            }
            else if (TestBound("SqlObject"))
            {
                ResolveSqlObjectInputs();
            }
        }

        #endregion BeginProcessing

        #region ProcessRecord

        /// <summary>
        /// Processes each SQL Server instance or database input, executing the query.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (TestFunctionInterrupt()) return;

            // Mutual exclusion validations
            if (TestBoundAll("Database", "InputObject"))
            {
                StopFunction("You can't use -Database with piped databases",
                    category: ErrorCategory.InvalidArgument);
                return;
            }
            if (TestBoundAll("SqlInstance", "InputObject"))
            {
                StopFunction("You can't use -SqlInstance with piped databases",
                    category: ErrorCategory.InvalidArgument);
                return;
            }
            if (TestBoundNot("SqlInstance") && TestBoundNot("InputObject"))
            {
                StopFunction("Please provide either SqlInstance or InputObject",
                    category: ErrorCategory.InvalidArgument);
                return;
            }

            // Process piped database objects
            if (InputObject != null)
            {
                foreach (object inputObj in InputObject)
                {
                    ProcessDatabaseInput(inputObj);
                }
            }

            // Process SqlInstance connections
            if (SqlInstance != null)
            {
                foreach (DbaInstanceParameter instance in SqlInstance)
                {
                    ProcessInstanceInput(instance);
                }
            }
        }

        #endregion ProcessRecord

        #region EndProcessing

        /// <summary>
        /// Cleans up temporary files that were downloaded or generated.
        /// </summary>
        protected override void EndProcessing()
        {
            if (_temporaryFiles != null)
            {
                foreach (string item in _temporaryFiles)
                {
                    try
                    {
                        if (System.IO.File.Exists(item))
                        {
                            System.IO.File.Delete(item);
                        }
                    }
                    catch (Exception)
                    {
                        // Best-effort cleanup
                    }
                }
            }
        }

        #endregion EndProcessing

        #region Instance Processing

        /// <summary>
        /// Processes a single database input object from the pipeline.
        /// </summary>
        private void ProcessDatabaseInput(object inputObj)
        {
            object db = GetBaseObject(inputObj);
            if (db == null) return;

            // Check IsAccessible
            object isAccessible = GetPropertyValue(db, "IsAccessible");
            if (isAccessible is bool accessible && !accessible)
            {
                WriteMessageAtLevel(
                    String.Format("Database {0} is not accessible. Skipping.", db),
                    MessageLevel.Warning, null);
                return;
            }

            // Get the parent server and connection context
            object server = GetPropertyValue(db, "Parent");
            object conncontext = GetPropertyValue(server, "ConnectionContext");
            if (conncontext == null)
            {
                StopFunction(
                    String.Format("[{0}] Failed to get connection context", db),
                    target: server, isContinue: true);
                TestFunctionInterrupt();
                return;
            }

            // Get db name and check if we need to switch database context
            string dbName = GetPropertyValue(db, "Name") as string;
            string connDbName = GetPropertyValue(conncontext, "DatabaseName") as string;

            if (!String.Equals(connDbName, dbName, StringComparison.OrdinalIgnoreCase))
            {
                // Save StatementTimeout, copy connection and switch database
                object savedTimeout = GetPropertyValue(conncontext, "StatementTimeout");
                conncontext = InvokeDatabaseSwitch(conncontext, dbName);
                if (conncontext == null)
                {
                    StopFunction(
                        String.Format("[{0}] Failed to switch database context", db),
                        target: server, isContinue: true);
                    TestFunctionInterrupt();
                    return;
                }
                if (savedTimeout != null)
                {
                    SetPropertyValue(conncontext, "StatementTimeout", savedTimeout);
                }
            }

            try
            {
                ExecuteAgainstConnection(conncontext, null);
            }
            catch (Exception ex)
            {
                StopFunction(
                    String.Format("[{0}] Failed during execution", db),
                    exception: ex, target: server, isContinue: true);
                TestFunctionInterrupt();
            }
        }

        /// <summary>
        /// Processes a single SqlInstance input, connecting and executing queries.
        /// </summary>
        private void ProcessInstanceInput(DbaInstanceParameter instance)
        {
            WriteMessageAtLevel(
                String.Format("SqlInstance passed in, will work on: {0}", instance),
                MessageLevel.Debug, null);

            object server = null;
            bool startedWithOpenConnection = false;
            bool usedNonPooled = false;

            try
            {
                // Determine if we can reuse the existing connection
                startedWithOpenConnection = CanReuseConnection(instance);

                if (startedWithOpenConnection)
                {
                    WriteMessageAtLevel("Current connection will be reused", MessageLevel.Debug, null);
                    server = instance.InputObject;
                }
                else
                {
                    server = ConnectToInstance(instance);
                    usedNonPooled = true;
                }
            }
            catch (Exception ex)
            {
                StopFunction("Failure", exception: ex, target: instance, isContinue: true);
                TestFunctionInterrupt();
                return;
            }

            object conncontext = GetPropertyValue(server, "ConnectionContext");
            if (conncontext == null)
            {
                StopFunction(
                    String.Format("[{0}] Failed to get connection context", instance),
                    target: instance, isContinue: true);
                TestFunctionInterrupt();
                return;
            }

            try
            {
                ExecuteAgainstConnection(conncontext, GetPropertyValue(conncontext, "ServerInstance") as string);
            }
            catch (Exception ex)
            {
                StopFunction(
                    String.Format("[{0}] Failed during execution", instance),
                    exception: ex, target: instance, isContinue: true);
                TestFunctionInterrupt();
            }

            // Disconnect non-pooled connections we created
            if (usedNonPooled && !startedWithOpenConnection)
            {
                try
                {
                    string script = "param($s) $s | Disconnect-DbaInstance -Verbose:$false";
                    InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { server });
                }
                catch (Exception)
                {
                    // Best-effort disconnect
                }
            }
        }

        /// <summary>
        /// Determines if we can reuse the existing connection from the instance parameter.
        /// </summary>
        internal bool CanReuseConnection(DbaInstanceParameter instance)
        {
            if (instance == null || instance.InputObject == null)
                return false;

            // Check if the input is a Server SMO object
            string typeName = instance.InputObject.GetType().Name;
            if (typeName != "Server")
                return false;

            // No readonly intent requested
            if (ReadOnly.IsPresent)
                return false;

            // Get ConnectionContext once for all checks
            object connCtx = GetPropertyValue(instance.InputObject, "ConnectionContext");

            // Database is not set, or matches what's already connected
            if (!String.IsNullOrEmpty(Database))
            {
                string connDb = GetPropertyValue(connCtx, "DatabaseName") as string;
                if (!String.Equals(connDb, Database, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            // No AppendConnectionString
            if (!String.IsNullOrEmpty(AppendConnectionString))
                return false;

            // Not using ConnectAsUser
            string connectAsUserName = GetPropertyValue(connCtx, "ConnectAsUserName") as string;
            if (!String.IsNullOrEmpty(connectAsUserName))
                return false;

            return true;
        }

        /// <summary>
        /// Connects to a SQL Server instance via Connect-DbaInstance PowerShell command.
        /// </summary>
        private object ConnectToInstance(DbaInstanceParameter instance)
        {
            // Build the connection parameters
            string script;
            List<object> args = new List<object>();

            // When AppendConnectionString is used, pass instance as string to force new connection
            object instanceArg = instance;
            if (!String.IsNullOrEmpty(AppendConnectionString))
            {
                instanceArg = instance.ToString();
            }

            script = "param($inst, $cred, $db, $appIntent, $appendConn) " +
                     "Connect-DbaInstance -SqlInstance $inst -NonPooledConnection" +
                     " -Verbose:$false";
            args.Add(instanceArg);

            if (SqlCredential != null)
            {
                script += " -SqlCredential $cred";
            }
            args.Add(SqlCredential);

            if (!String.IsNullOrEmpty(Database))
            {
                script += " -Database $db";
            }
            args.Add(Database);

            if (ReadOnly.IsPresent)
            {
                script += " -ApplicationIntent $appIntent";
                args.Add("ReadOnly");
            }
            else
            {
                args.Add(null);
            }

            if (!String.IsNullOrEmpty(AppendConnectionString))
            {
                script += " -AppendConnectionString $appendConn";
            }
            args.Add(AppendConnectionString);

            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, ScriptBlock.Create(script), null, args.ToArray());

            if (results != null && results.Count > 0)
                return results[0].BaseObject;

            throw new InvalidOperationException(
                String.Format("Failed to connect to {0}", instance));
        }

        #endregion Instance Processing

        #region Query Execution

        /// <summary>
        /// Executes queries against a ServerConnection, handling files and direct queries.
        /// </summary>
        private void ExecuteAgainstConnection(object connContext, string serverInstance)
        {
            if (_resolvedFiles != null && _resolvedFiles.Count > 0)
            {
                foreach (string filePath in _resolvedFiles)
                {
                    if (filePath == null) continue;

                    string resolvedPath = ResolveFilePath(filePath);
                    string queryFromFile = System.IO.File.ReadAllText(resolvedPath);
                    ExecuteQuery(connContext, queryFromFile, serverInstance);
                }
            }
            else
            {
                ExecuteQuery(connContext, Query, serverInstance);
            }
        }

        /// <summary>
        /// Executes a single query string against the connection, splitting on GO batches.
        /// This implements the core Invoke-DbaAsync logic in C#.
        /// </summary>
        private void ExecuteQuery(object connContext, string query, string serverInstance)
        {
            // Get the underlying SqlConnection from the ServerConnection
            object sqlConnObj = GetPropertyValue(connContext, "SqlConnectionObject");
            SqlConnection conn = sqlConnObj as SqlConnection;
            if (conn == null)
            {
                throw new InvalidOperationException("Could not obtain SqlConnection from ServerConnection");
            }

            // Determine query timeout
            int timeout = QueryTimeout;
            if (timeout < 0)
            {
                // Use the connection's StatementTimeout
                object stmtTimeout = GetPropertyValue(connContext, "StatementTimeout");
                if (stmtTimeout is int t)
                    timeout = t;
                else
                    timeout = 0;
            }

            WriteMessageAtLevel("Stripping GOs from source", MessageLevel.Debug, null);

            // Split on GO batch separators
            string[] pieces = GoSplitterRegex.Split(query);

            foreach (string piece in pieces)
            {
                if (String.IsNullOrEmpty(piece) || piece.Trim().Length == 0)
                    continue;

                string runningStatement = piece;
                if (QuotedIdentifier.IsPresent)
                {
                    runningStatement = "SET QUOTED_IDENTIFIER ON; " + runningStatement;
                }
                if (NoExec.IsPresent)
                {
                    runningStatement = "SET NOEXEC ON; " + runningStatement + " ;SET NOEXEC OFF;";
                }

                DataSet ds = new DataSet();
                using (SqlCommand cmd = new SqlCommand())
                {
                    cmd.CommandText = runningStatement;
                    cmd.Connection = conn;
                    cmd.CommandType = CommandType;
                    cmd.CommandTimeout = timeout;

                    // Add SQL parameters
                    AddSqlParameters(cmd);

                    using (SqlDataAdapter da = new SqlDataAdapter(cmd))
                    {
                        if (MessagesToOutput.IsPresent)
                        {
                            ExecuteWithMessages(da, ds, conn);
                        }
                        else
                        {
                            ExecuteWithoutMessages(da, ds, conn);
                        }
                    }

                    // Append server instance column if requested
                    if (AppendServerInstance.IsPresent)
                    {
                        AppendServerInstanceColumn(ds, connContext);
                    }

                    cmd.Parameters.Clear();
                }

                // Output results in the requested format
                OutputResults(ds);
            }
        }

        /// <summary>
        /// Adds SQL parameters to the command from the SqlParameter property.
        /// </summary>
        internal void AddSqlParameters(SqlCommand cmd)
        {
            if (SqlParameter == null) return;

            object first = GetBaseObject(SqlParameter[0]);

            if (first is SqlParameter)
            {
                // Array of SqlParameter objects
                foreach (PSObject paramObj in SqlParameter)
                {
                    SqlParameter sqlParam = GetBaseObject(paramObj) as SqlParameter;
                    if (sqlParam != null)
                    {
                        cmd.Parameters.Add(sqlParam);
                    }
                }
            }
            else if (first is IDictionary dict)
            {
                // Single hashtable with key-value pairs
                foreach (DictionaryEntry entry in dict)
                {
                    string key = entry.Key.ToString();
                    if (entry.Value != null)
                    {
                        object val = GetBaseObject(entry.Value);
                        if (val is SqlParameter sqlParam)
                        {
                            if (sqlParam.ParameterName != key)
                            {
                                sqlParam.ParameterName = key;
                            }
                            cmd.Parameters.Add(sqlParam);
                        }
                        else
                        {
                            cmd.Parameters.AddWithValue(key, val);
                        }
                    }
                    else
                    {
                        cmd.Parameters.AddWithValue(key, DBNull.Value);
                    }
                }
            }
        }

        /// <summary>
        /// Executes the query with MessagesToOutput, streaming PRINT/RAISERROR messages.
        /// Uses a background runspace to capture messages via InfoMessage events.
        /// </summary>
        private void ExecuteWithMessages(SqlDataAdapter da, DataSet ds, SqlConnection conn)
        {
            SmaRunspaces.Runspace defaultRunspace = SmaRunspaces.Runspace.DefaultRunspace;
            SmaRunspaces.RunspacePool pool = SmaRunspaces.RunspaceFactory.CreateRunspacePool(1,
                Math.Max(Environment.ProcessorCount + 1, 2));
            pool.Open();

            try
            {
                ConcurrentQueue<string> queue = new ConcurrentQueue<string>();

                string scriptText = @"
param($da, $ds, $conn, $queue)
$conn.FireInfoMessageEventOnUserErrors = $false
$handler = [Microsoft.Data.SqlClient.SqlInfoMessageEventHandler] { $queue.Enqueue($_.Message) }
$conn.add_InfoMessage($handler)
$Err = $null
try {
    [void]$da.Fill($ds)
} catch {
    $Err = $_
} finally {
    $conn.remove_InfoMessage($handler)
}
return $Err
";
                using (PowerShell ps = PowerShell.Create())
                {
                    ps.AddScript(scriptText);
                    ps.AddArgument(da);
                    ps.AddArgument(ds);
                    ps.AddArgument(conn);
                    ps.AddArgument(queue);
                    ps.RunspacePool = pool;

                    IAsyncResult asyncResult = ps.BeginInvoke();

                    // Stream messages while the query runs
                    while (!asyncResult.IsCompleted)
                    {
                        string item;
                        if (queue.TryDequeue(out item))
                        {
                            WriteObject(item);
                        }
                        System.Threading.Thread.Sleep(10);
                    }

                    // Drain remaining messages
                    string remaining;
                    while (queue.TryDequeue(out remaining))
                    {
                        WriteObject(remaining);
                    }

                    // Check for errors
                    PSDataCollection<PSObject> results = ps.EndInvoke(asyncResult);

                    if (results != null && results.Count > 0 && results[0] != null)
                    {
                        object err = results[0].BaseObject;
                        if (err is ErrorRecord errRec && errRec.Exception != null)
                        {
                            throw errRec.Exception;
                        }
                        if (err is Exception ex)
                        {
                            throw ex;
                        }
                    }
                }
            }
            finally
            {
                pool.Close();
                pool.Dispose();
                SmaRunspaces.Runspace.DefaultRunspace = defaultRunspace;
            }
        }

        /// <summary>
        /// Executes the query without MessagesToOutput. When Verbose is active,
        /// wires up InfoMessage events to output PRINT/RAISERROR as verbose messages.
        /// </summary>
        private void ExecuteWithoutMessages(SqlDataAdapter da, DataSet ds, SqlConnection conn)
        {
            bool verbose = MyInvocation.BoundParameters.ContainsKey("Verbose")
                && ((SwitchParameter)MyInvocation.BoundParameters["Verbose"]).IsPresent;

            SqlInfoMessageEventHandler handler = null;
            if (verbose)
            {
                conn.FireInfoMessageEventOnUserErrors = false;
                handler = delegate(object sender, SqlInfoMessageEventArgs e)
                {
                    // Use WriteVerbose directly for InfoMessage events so messages appear on
                    // PowerShell's verbose stream (stream 4). WriteMessageAtLevel routes through
                    // InvokeCommand.InvokeScript which writes to a child context's stream,
                    // not the outer cmdlet's stream 4 that callers capture via 4>&1.
                    WriteVerbose(e.Message);
                };
                conn.InfoMessage += handler;
            }

            try
            {
                da.Fill(ds);
            }
            finally
            {
                if (verbose && handler != null)
                {
                    conn.InfoMessage -= handler;
                }
            }
        }

        /// <summary>
        /// Appends a ServerInstance column to the first DataTable in the DataSet.
        /// </summary>
        private void AppendServerInstanceColumn(DataSet ds, object connContext)
        {
            if (ds.Tables.Count == 0) return;

            string instanceName = GetPropertyValue(connContext, "ServerInstance") as string;
            DataColumn col = new DataColumn("ServerInstance", typeof(string));
            ds.Tables[0].Columns.Add(col);

            foreach (DataRow row in ds.Tables[0].Rows)
            {
                row["ServerInstance"] = instanceName;
            }
        }

        #endregion Query Execution

        #region Output Formatting

        /// <summary>
        /// Outputs results in the format specified by the -As parameter.
        /// </summary>
        private void OutputResults(DataSet ds)
        {
            switch (As)
            {
                case "DataSet":
                    WriteObject(ds);
                    break;

                case "DataTable":
                    foreach (DataTable table in ds.Tables)
                    {
                        WriteObject(table);
                    }
                    break;

                case "DataRow":
                    if (ds.Tables.Count > 0)
                    {
                        foreach (DataRow row in ds.Tables[0].Rows)
                        {
                            WriteObject(row);
                        }
                    }
                    break;

                case "PSObject":
                    foreach (DataTable table in ds.Tables)
                    {
                        foreach (DataRow row in table.Rows)
                        {
                            WriteObject(DataRowToPSObject(row));
                        }
                    }
                    break;

                case "PSObjectArray":
                    foreach (DataTable table in ds.Tables)
                    {
                        List<PSObject> rows = new List<PSObject>();
                        foreach (DataRow row in table.Rows)
                        {
                            rows.Add(DataRowToPSObject(row));
                        }
                        // Output as array (use , prefix equivalent: enumerateCollection = false)
                        WriteObject(rows.ToArray(), false);
                    }
                    break;

                case "SingleValue":
                    if (ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0
                        && ds.Tables[0].Columns.Count > 0)
                    {
                        string colName = ds.Tables[0].Columns[0].ColumnName;
                        foreach (DataRow row in ds.Tables[0].Rows)
                        {
                            object val = row[colName];
                            if (val == DBNull.Value) val = null;
                            WriteObject(val);
                        }
                    }
                    break;
            }
        }

        /// <summary>
        /// Converts a DataRow to a PSObject, replacing DBNull values with null.
        /// Equivalent to the DBNullScrubber C# type used in Invoke-DbaAsync.
        /// </summary>
        internal static PSObject DataRowToPSObject(DataRow row)
        {
            PSObject psObject = new PSObject();

            if (row != null && (row.RowState & DataRowState.Detached) != DataRowState.Detached)
            {
                foreach (DataColumn column in row.Table.Columns)
                {
                    object value = null;
                    if (!row.IsNull(column))
                    {
                        value = row[column];
                    }
                    psObject.Properties.Add(new PSNoteProperty(column.ColumnName, value));
                }
            }

            return psObject;
        }

        #endregion Output Formatting

        #region File Resolution

        /// <summary>
        /// Resolves file inputs from the -File parameter into a list of file paths.
        /// Handles strings (paths and URLs), FileInfo, and DirectoryInfo objects.
        /// </summary>
        private void ResolveFileInputs()
        {
            string tempPrefix = GenerateRandomPrefix();
            int tempCount = 0;

            foreach (object item in File)
            {
                if (item == null) continue;

                object baseItem = GetBaseObject(item);
                if (baseItem == null) continue;

                string typeName = baseItem.GetType().FullName;

                if (typeName == "System.IO.DirectoryInfo")
                {
                    DirectoryInfo dirInfo = (DirectoryInfo)baseItem;
                    if (!dirInfo.Exists)
                    {
                        StopFunction("Directory not found",
                            category: ErrorCategory.ObjectNotFound);
                        return;
                    }
                    foreach (FileInfo fi in dirInfo.GetFiles())
                    {
                        if (fi.Extension.Equals(".sql", StringComparison.OrdinalIgnoreCase))
                            _resolvedFiles.Add(fi.FullName);
                    }
                }
                else if (typeName == "System.IO.FileInfo")
                {
                    FileInfo fileInfo = (FileInfo)baseItem;
                    if (!fileInfo.Exists)
                    {
                        StopFunction("Directory not found.",
                            category: ErrorCategory.ObjectNotFound);
                        return;
                    }
                    _resolvedFiles.Add(fileInfo.FullName);
                }
                else if (baseItem is string strItem)
                {
                    // Determine if it's a URL or a file path
                    string scheme = null;
                    try
                    {
                        Uri uri = new Uri(strItem);
                        scheme = uri.Scheme;
                    }
                    catch (Exception)
                    {
                        scheme = null;
                    }

                    if (scheme != null && scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        // Download from URL
                        string tempFile = GetTempFilePath(tempPrefix, tempCount);
                        try
                        {
                            DownloadFile(strItem, tempFile);
                            _resolvedFiles.Add(tempFile);
                            _temporaryFiles.Add(tempFile);
                            tempCount++;
                        }
                        catch (Exception ex)
                        {
                            StopFunction(
                                String.Format("Failed to download file {0}", strItem),
                                exception: ex);
                            return;
                        }
                    }
                    else
                    {
                        // Resolve as file path
                        try
                        {
                            ResolvePathAndAdd(strItem);
                        }
                        catch (Exception ex)
                        {
                            StopFunction(
                                String.Format("Failed to resolve path: {0}", strItem),
                                exception: ex);
                            return;
                        }
                    }
                }
                else
                {
                    StopFunction(
                        String.Format("Unkown input type: {0}", typeName),
                        category: ErrorCategory.InvalidArgument);
                    return;
                }
            }
        }

        /// <summary>
        /// Resolves SqlObject inputs by scripting them out to temporary files.
        /// </summary>
        private void ResolveSqlObjectInputs()
        {
            string tempPrefix = GenerateRandomPrefix();
            int tempCount = 0;

            foreach (object obj in SqlObject)
            {
                if (obj == null) continue;

                string code;
                try
                {
                    // Use Export-DbaScript -Passthru to get the SQL script
                    string script = "param($obj) Export-DbaScript -InputObject $obj -Passthru -EnableException";
                    Collection<PSObject> results = InvokeCommand.InvokeScript(
                        false, ScriptBlock.Create(script), null, new object[] { obj });

                    if (results == null || results.Count == 0)
                    {
                        StopFunction(String.Format("Failed to generate script for object {0}", obj));
                        return;
                    }

                    // Join all results as the script text
                    List<string> lines = new List<string>();
                    foreach (PSObject r in results)
                    {
                        if (r != null) lines.Add(r.ToString());
                    }
                    code = String.Join(Environment.NewLine, lines.ToArray());
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("Failed to generate script for object {0}", obj),
                        exception: ex);
                    return;
                }

                try
                {
                    string tempFile = GetTempFilePath(tempPrefix, tempCount);
                    System.IO.File.WriteAllText(tempFile, code, System.Text.Encoding.UTF8);
                    _resolvedFiles.Add(tempFile);
                    _temporaryFiles.Add(tempFile);
                    tempCount++;
                }
                catch (Exception ex)
                {
                    StopFunction("Failed to write sql script to temp", exception: ex);
                    return;
                }
            }
        }

        /// <summary>
        /// Resolves a file path string, supporting wildcards and relative paths.
        /// </summary>
        private void ResolvePathAndAdd(string path)
        {
            string script = @"
param($p)
$paths = Resolve-Path $p | Select-Object -ExpandProperty Path | Get-Item -ErrorAction Stop
foreach ($path in $paths) {
    if (-not $path.PSIsContainer) {
        if ((New-Object uri -ArgumentList $path.FullName).Scheme -eq 'file') {
            $path.FullName
        }
    }
}
";
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, ScriptBlock.Create(script), null, new object[] { path });

            if (results != null)
            {
                foreach (PSObject r in results)
                {
                    if (r != null)
                    {
                        _resolvedFiles.Add(r.ToString());
                    }
                }
            }
        }

        /// <summary>
        /// Downloads a file from a URL to a local temp path.
        /// </summary>
        private void DownloadFile(string url, string destPath)
        {
            string script = @"
param($uri, $outFile)
try {
    Invoke-TlsWebRequest -Uri $uri -OutFile $outFile -ErrorAction Stop
} catch {
    (New-Object System.Net.WebClient).Proxy.Credentials = [System.Net.CredentialCache]::DefaultNetworkCredentials
    Invoke-TlsWebRequest -Uri $uri -OutFile $outFile -ErrorAction Stop
}
";
            InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null,
                new object[] { url, destPath });
        }

        #endregion File Resolution

        #region Database Context Switch

        /// <summary>
        /// Switches the database context on a ServerConnection by calling Copy().GetDatabaseConnection().
        /// </summary>
        private object InvokeDatabaseSwitch(object connContext, string dbName)
        {
            string script = @"
param($ctx, $db)
$savedTimeout = $ctx.StatementTimeout
$newCtx = $ctx.Copy().GetDatabaseConnection($db)
$newCtx.StatementTimeout = $savedTimeout
$newCtx
";
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, ScriptBlock.Create(script), null, new object[] { connContext, dbName });
                if (results != null && results.Count > 0)
                    return results[0].BaseObject;
            }
            catch (Exception)
            {
                // Fall through
            }
            return null;
        }

        #endregion Database Context Switch

        #region Utility Helpers

        /// <summary>
        /// Gets the base object from a PSObject wrapper, or returns the object itself.
        /// </summary>
        internal static object GetBaseObject(object obj)
        {
            if (obj == null) return null;
            if (obj is PSObject psObj)
                return psObj.BaseObject;
            return obj;
        }

        /// <summary>
        /// Gets a property value from an object via reflection.
        /// </summary>
        internal static object GetPropertyValue(object obj, string propertyName)
        {
            if (obj == null) return null;

            // Unwrap PSObject
            if (obj is PSObject psObj && psObj.BaseObject != null)
                obj = psObj.BaseObject;

            try
            {
                var prop = obj.GetType().GetProperty(propertyName);
                if (prop != null)
                    return prop.GetValue(obj);
            }
            catch (Exception)
            {
                // Property may not exist
            }
            return null;
        }

        /// <summary>
        /// Sets a property value on an object via reflection.
        /// </summary>
        private static void SetPropertyValue(object obj, string propertyName, object value)
        {
            if (obj == null) return;

            if (obj is PSObject psObj && psObj.BaseObject != null)
                obj = psObj.BaseObject;

            try
            {
                var prop = obj.GetType().GetProperty(propertyName);
                if (prop != null)
                    prop.SetValue(obj, value);
            }
            catch (Exception)
            {
                // Property may not exist or be read-only
            }
        }

        /// <summary>
        /// Resolves a file path using PowerShell's Resolve-Path.
        /// Falls back to the raw path if resolution fails.
        /// </summary>
        private string ResolveFilePath(string path)
        {
            try
            {
                string script = "param($p) (Resolve-Path -LiteralPath $p).ProviderPath";
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, ScriptBlock.Create(script), null, new object[] { path });
                if (results != null && results.Count > 0)
                    return results[0].ToString();
            }
            catch (Exception)
            {
                // Fallback to raw path
            }
            return path;
        }

        /// <summary>
        /// Generates a random lowercase alphabetic prefix for temp files.
        /// </summary>
        internal static string GenerateRandomPrefix()
        {
            return Guid.NewGuid().ToString("N").Substring(0, 10);
        }

        /// <summary>
        /// Gets a temporary file path for SQL script files.
        /// </summary>
        private string GetTempFilePath(string prefix, int count)
        {
            try
            {
                string script = "param() Get-DbatoolsPath -Name temp";
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, ScriptBlock.Create(script), null, new object[0]);
                if (results != null && results.Count > 0)
                {
                    string tempDir = results[0].ToString();
                    return Path.Combine(tempDir, String.Format("{0}-{1}.sql", prefix, count));
                }
            }
            catch (Exception)
            {
                // Fallback
            }
            return Path.Combine(Path.GetTempPath(), String.Format("{0}-{1}.sql", prefix, count));
        }

        #endregion Utility Helpers
    }
}
