using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Identifies tables containing binary columns and their associated filename columns for file extraction.
    /// </summary>
    [Cmdlet("Get", "DbaBinaryFileTable")]
    [OutputType("Microsoft.SqlServer.Management.Smo.Table")]
    public class GetDbaBinaryFileTableCommand : DbaBaseCmdlet
    {
        /// <summary>
        /// The target SQL Server instance or instances. Used when no InputObject is provided.
        /// </summary>
        [Parameter(Position = 0)]
        public DbaInstanceParameter[] SqlInstance { get; set; }

        /// <summary>
        /// Login to the target instance using alternative credentials.
        /// </summary>
        [Parameter()]
        public PSCredential SqlCredential { get; set; }

        /// <summary>
        /// Specifies which databases to scan for tables containing binary columns. Accepts wildcards.
        /// </summary>
        [Parameter()]
        public string[] Database { get; set; }

        /// <summary>
        /// Targets specific tables to analyze for binary columns. Supports wildcards.
        /// </summary>
        [Parameter()]
        public string[] Table { get; set; }

        /// <summary>
        /// Restricts the search to tables within specific database schemas. Accepts wildcards.
        /// </summary>
        [Parameter()]
        public string[] Schema { get; set; }

        /// <summary>
        /// Accepts table objects piped directly from Get-DbaDbTable.
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        public PSObject[] InputObject { get; set; }

        /// <summary>
        /// Default display properties matching the PS1 Select-DefaultView output.
        /// </summary>
        private static readonly string[] DefaultDisplayProperties = new string[]
        {
            "ComputerName", "InstanceName", "SqlInstance", "Database", "Schema",
            "Name", "BinaryColumn", "FileNameColumn"
        };

        /// <summary>
        /// Cached ScriptBlock for calling Get-DbaDbTable to avoid re-parsing on each invocation.
        /// </summary>
        private static readonly ScriptBlock GetDbaDbTableScript = ScriptBlock.Create(
            "param($inst, $cred, $db, $tbl, $sch) " +
            "Get-DbaDbTable -SqlInstance $inst -SqlCredential $cred " +
            "-Database $db -Table $tbl -Schema $sch -EnableException"
        );

        /// <summary>
        /// Processes each pipeline item or fetches tables from SQL Server instances,
        /// then identifies binary and filename columns on each table.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (TestFunctionInterrupt()) return;

            List<PSObject> tables = new List<PSObject>();

            if (InputObject == null || InputObject.Length == 0)
            {
                try
                {
                    Collection<PSObject> result = InvokeGetDbaDbTable();
                    if (result != null)
                    {
                        foreach (PSObject obj in result)
                        {
                            if (obj != null)
                                tables.Add(obj);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // PS1 passes $PSItem (ErrorRecord); C# can only pass the Exception.
                    // The original error ID is lost but the message is preserved.
                    StopFunction("Failed to get tables", exception: ex);
                    return;
                }
            }
            else
            {
                foreach (PSObject obj in InputObject)
                {
                    if (obj != null)
                        tables.Add(obj);
                }
            }

            WriteMessageVerbose(String.Format("Found {0} tables", tables.Count));

            foreach (PSObject tbl in tables)
            {
                ProcessTable(tbl);
            }
        }

        #region Helpers

        /// <summary>
        /// Invokes Get-DbaDbTable to retrieve tables when no InputObject is provided.
        /// Always passes -EnableException so errors are thrown and caught by the outer try/catch.
        /// </summary>
        private Collection<PSObject> InvokeGetDbaDbTable()
        {
            return InvokeCommand.InvokeScript(
                false,
                GetDbaDbTableScript,
                null,
                SqlInstance,
                SqlCredential,
                Database,
                Table,
                Schema
            );
        }

        /// <summary>
        /// Processes a single table to find binary and filename columns,
        /// adds NoteProperties, and outputs the table if binary columns are found.
        /// </summary>
        private void ProcessTable(PSObject tbl)
        {
            List<string> binaryColumns = new List<string>();
            List<string> fileNameColumns = new List<string>();

            // Access the Columns collection via PSObject property reflection
            object columnsObj = GetPropertyValue(tbl, "Columns");
            if (columnsObj == null)
                return;

            IEnumerable columns = columnsObj as IEnumerable;
            if (columns == null)
                return;

            foreach (object col in columns)
            {
                PSObject colObj = PSObject.AsPSObject(col);
                string colName = GetPropertyString(colObj, "Name");
                if (String.IsNullOrEmpty(colName))
                    continue;

                // Check DataType.Name for binary/varbinary/image
                object dataTypeObj = GetPropertyValue(colObj, "DataType");
                if (dataTypeObj != null)
                {
                    PSObject dataType = PSObject.AsPSObject(dataTypeObj);
                    string dataTypeName = GetPropertyString(dataType, "Name");
                    if (IsBinaryDataType(dataTypeName))
                    {
                        binaryColumns.Add(colName);
                    }
                }

                // Check if column name contains "name" (matches PS1: Where-Object Name -Match Name)
                if (IsFileNameColumn(colName))
                {
                    fileNameColumns.Add(colName);
                }
            }

            // Get server/database/table names for verbose messages
            string tableName = GetPropertyString(tbl, "Name") ?? "<Unknown>";
            string databaseName = GetParentName(tbl);
            string serverName = GetGrandparentName(tbl);

            // Emit verbose messages before the binary guard to match PS1 ordering
            if (fileNameColumns.Count > 1)
            {
                WriteMessageVerbose(String.Format(
                    "Multiple column names match the phrase 'name' in {0} in {1} on {2}. Please specify the column to use with -FileNameColumn",
                    tableName, databaseName, serverName));
            }

            if (binaryColumns.Count > 1)
            {
                WriteMessageVerbose(String.Format(
                    "Multiple columns have a binary datatype in {0} in {1} on {2}.",
                    tableName, databaseName, serverName));
            }

            if (binaryColumns.Count == 0)
                return;

            // Build property values matching PS1 behavior:
            // Single match = string, multiple matches = string[]
            object binaryValue = BuildColumnValue(binaryColumns);
            object fileNameValue = BuildColumnValue(fileNameColumns);

            AddOrSetProperty(tbl, "BinaryColumn", binaryValue);
            AddOrSetProperty(tbl, "FileNameColumn", fileNameValue);
            SetDefaultDisplayPropertySet(tbl, DefaultDisplayProperties);
            WriteObject(tbl);
        }

        /// <summary>
        /// Determines if a column data type name represents a binary type (binary, varbinary, image).
        /// </summary>
        internal static bool IsBinaryDataType(string dataTypeName)
        {
            if (String.IsNullOrEmpty(dataTypeName))
                return false;
            return dataTypeName.IndexOf("binary", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   String.Equals(dataTypeName, "image", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determines if a column name matches the filename pattern (contains "name").
        /// </summary>
        internal static bool IsFileNameColumn(string columnName)
        {
            if (String.IsNullOrEmpty(columnName))
                return false;
            return columnName.IndexOf("name", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Converts a list of column names to a single string (1 item) or string[] (multiple items).
        /// Returns null for empty lists.
        /// </summary>
        internal static object BuildColumnValue(List<string> columns)
        {
            if (columns == null || columns.Count == 0)
                return null;
            if (columns.Count == 1)
                return columns[0];
            return columns.ToArray();
        }

        /// <summary>
        /// Gets a raw property value from a PSObject.
        /// </summary>
        internal static object GetPropertyValue(PSObject obj, string propertyName)
        {
            if (obj == null)
                return null;
            try
            {
                PSPropertyInfo prop = obj.Properties[propertyName];
                if (prop != null)
                    return prop.Value;
            }
            catch (Exception)
            {
                // Property may not exist or getter may throw
            }
            return null;
        }

        /// <summary>
        /// Gets the name of the table's parent object (Database).
        /// </summary>
        internal static string GetParentName(PSObject tbl)
        {
            object parent = GetPropertyValue(tbl, "Parent");
            if (parent == null)
                return "<Unknown>";
            PSObject parentObj = PSObject.AsPSObject(parent);
            return GetPropertyString(parentObj, "Name") ?? "<Unknown>";
        }

        /// <summary>
        /// Gets the name of the table's grandparent object (Server).
        /// </summary>
        internal static string GetGrandparentName(PSObject tbl)
        {
            object db = GetPropertyValue(tbl, "Parent");
            if (db == null)
                return "<Unknown>";
            PSObject dbObj = PSObject.AsPSObject(db);
            object server = GetPropertyValue(dbObj, "Parent");
            if (server == null)
                return "<Unknown>";
            PSObject serverObj = PSObject.AsPSObject(server);
            return GetPropertyString(serverObj, "Name") ?? "<Unknown>";
        }

        /// <summary>
        /// Adds or updates a NoteProperty on a PSObject.
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
        /// Sets the DefaultDisplayPropertySet on a PSObject (equivalent to Select-DefaultView).
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
