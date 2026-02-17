using System;
using System.Data;
using System.Data.SqlTypes;
using System.Management.Automation;
using Microsoft.Data.SqlClient;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Creates a SqlParameter object for use with parameterized queries and stored procedures.
    /// Returns a Microsoft.Data.SqlClient.SqlParameter configured with the specified properties,
    /// ready to be used with Invoke-DbaQuery or other data access operations.
    /// </summary>
    [OutputType(typeof(SqlParameter))]
    [Cmdlet("New", "DbaSqlParameter")]
    public class NewDbaSqlParameterCommand : DbaBaseCmdlet
    {
        /// <summary>
        /// Defines how string comparisons are performed when this parameter is used in SQL operations.
        /// </summary>
        [Parameter()]
        [ValidateSet("None", "IgnoreCase", "IgnoreNonSpace", "IgnoreKanaType", "IgnoreWidth", "BinarySort2", "BinarySort")]
        public string CompareInfo { get; set; }

        /// <summary>
        /// Specifies the .NET data type for the parameter using System.Data.DbType enumeration.
        /// </summary>
        [Parameter()]
        [ValidateSet("AnsiString", "Binary", "Byte", "Boolean", "Currency", "Date", "DateTime", "Decimal", "Double", "Guid", "Int16", "Int32", "Int64", "Object", "SByte", "Single", "String", "Time", "UInt16", "UInt32", "UInt64", "VarNumeric", "AnsiStringFixedLength", "StringFixedLength", "Xml", "DateTime2", "DateTimeOffset")]
        public string DbType { get; set; }

        /// <summary>
        /// Specifies whether the parameter passes data into the query, returns data, or both.
        /// </summary>
        [Parameter()]
        [ValidateSet("Input", "Output", "InputOutput", "ReturnValue")]
        public string Direction { get; set; }

        /// <summary>
        /// Enforces encryption of a parameter when using Always Encrypted.
        /// </summary>
        [Parameter()]
        public SwitchParameter ForceColumnEncryption { get; set; }

        /// <summary>
        /// Indicates whether the parameter can accept null values.
        /// </summary>
        [Parameter()]
        public SwitchParameter IsNullable { get; set; }

        /// <summary>
        /// Specifies the locale identifier (LCID) for regional formatting conventions.
        /// </summary>
        [Parameter()]
        public int LocaleId { get; set; }

        /// <summary>
        /// Specifies the starting position within the parameter value for binary or text data.
        /// </summary>
        [Parameter()]
        public string Offset { get; set; }

        /// <summary>
        /// Specifies the name of the parameter as it appears in the SQL query or stored procedure.
        /// </summary>
        [Parameter()]
        [Alias("Name")]
        public string ParameterName { get; set; }

        /// <summary>
        /// Defines the total number of digits for numeric data types.
        /// </summary>
        [Parameter()]
        public string Precision { get; set; }

        /// <summary>
        /// Specifies the number of decimal places for numeric data types.
        /// </summary>
        [Parameter()]
        public string Scale { get; set; }

        /// <summary>
        /// Defines the maximum length for variable-length data types. Use -1 for MAX types.
        /// </summary>
        [Parameter()]
        public int Size { get; set; }

        /// <summary>
        /// Maps the parameter to a specific column name in a DataTable or DataSet.
        /// </summary>
        [Parameter()]
        public string SourceColumn { get; set; }

        /// <summary>
        /// Indicates whether the source column allows null values.
        /// </summary>
        [Parameter()]
        public SwitchParameter SourceColumnNullMapping { get; set; }

        /// <summary>
        /// Specifies which version of data to use from a DataRow.
        /// </summary>
        [Parameter()]
        [ValidateSet("Original", "Current", "Proposed", "Default")]
        public string SourceVersion { get; set; }

        /// <summary>
        /// Specifies the SQL Server data type for the parameter.
        /// </summary>
        [Parameter()]
        [ValidateSet("BigInt", "Binary", "Bit", "Char", "DateTime", "Decimal", "Float", "Image", "Int", "Money", "NChar", "NText", "NVarChar", "Real", "UniqueIdentifier", "SmallDateTime", "SmallInt", "SmallMoney", "Text", "Timestamp", "TinyInt", "VarBinary", "VarChar", "Variant", "Xml", "Udt", "Structured", "Date", "Time", "DateTime2", "DateTimeOffset")]
        public string SqlDbType { get; set; }

        /// <summary>
        /// Sets the parameter value using SQL Server-specific data types.
        /// </summary>
        [Parameter()]
        public object SqlValue { get; set; }

        /// <summary>
        /// Specifies the user-defined table type name for table-valued parameters.
        /// </summary>
        [Parameter()]
        public string TypeName { get; set; }

        /// <summary>
        /// Specifies the name of a user-defined data type (UDT) or CLR type.
        /// </summary>
        [Parameter()]
        public string UdtTypeName { get; set; }

        /// <summary>
        /// Specifies the actual data value to pass to the SQL parameter.
        /// </summary>
        [Parameter()]
        public object Value { get; set; }

        /// <summary>
        /// Creates a SqlParameter with the specified properties and outputs it.
        /// </summary>
        protected override void ProcessRecord()
        {
            SqlParameter param = new SqlParameter();

            try
            {
                if (TestBound("CompareInfo"))
                {
                    param.CompareInfo = (SqlCompareOptions)Enum.Parse(typeof(SqlCompareOptions), CompareInfo, true);
                }

                if (TestBound("DbType"))
                {
                    param.DbType = (DbType)Enum.Parse(typeof(DbType), DbType, true);
                }

                if (TestBound("Direction"))
                {
                    param.Direction = (ParameterDirection)Enum.Parse(typeof(ParameterDirection), Direction, true);
                }

                if (TestBound("ForceColumnEncryption"))
                {
                    param.ForceColumnEncryption = ForceColumnEncryption.ToBool();
                }

                if (TestBound("IsNullable"))
                {
                    param.IsNullable = IsNullable.ToBool();
                }

                if (TestBound("LocaleId"))
                {
                    param.LocaleId = LocaleId;
                }

                if (TestBound("Offset"))
                {
                    param.Offset = Int32.Parse(Offset);
                }

                if (TestBound("ParameterName"))
                {
                    param.ParameterName = ParameterName;
                }

                if (TestBound("Precision"))
                {
                    param.Precision = Byte.Parse(Precision);
                }

                if (TestBound("Scale"))
                {
                    param.Scale = Byte.Parse(Scale);
                }

                if (TestBound("Size"))
                {
                    param.Size = Size;
                }

                if (TestBound("SourceColumn"))
                {
                    param.SourceColumn = SourceColumn;
                }

                if (TestBound("SourceColumnNullMapping"))
                {
                    param.SourceColumnNullMapping = SourceColumnNullMapping.ToBool();
                }

                if (TestBound("SourceVersion"))
                {
                    param.SourceVersion = (DataRowVersion)Enum.Parse(typeof(DataRowVersion), SourceVersion, true);
                }

                if (TestBound("SqlDbType"))
                {
                    param.SqlDbType = (System.Data.SqlDbType)Enum.Parse(typeof(System.Data.SqlDbType), SqlDbType, true);
                }

                if (TestBound("SqlValue"))
                {
                    param.SqlValue = SqlValue;
                }

                if (TestBound("TypeName"))
                {
                    param.TypeName = TypeName;
                }

                if (TestBound("UdtTypeName"))
                {
                    param.UdtTypeName = UdtTypeName;
                }

                if (TestBound("Value"))
                {
                    param.Value = Value;
                }

                WriteObject(param);
            }
            catch (Exception ex)
            {
                StopFunction("Failure", exception: ex);
                TestFunctionInterrupt();
                return;
            }
        }
    }
}
