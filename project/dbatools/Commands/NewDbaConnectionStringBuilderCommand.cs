using System;
using System.Management.Automation;
using System.Reflection;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Creates a SqlConnectionStringBuilder object for constructing properly formatted
    /// SQL Server connection strings. Supports both Microsoft.Data.SqlClient (default) and
    /// System.Data.SqlClient (legacy) providers.
    /// </summary>
    [OutputType("Microsoft.Data.SqlClient.SqlConnectionStringBuilder")]
    [OutputType("System.Data.SqlClient.SqlConnectionStringBuilder")]
    [Cmdlet("New", "DbaConnectionStringBuilder")]
    public class NewDbaConnectionStringBuilderCommand : DbaBaseCmdlet
    {
        /// <summary>
        /// An existing SQL Server connection string to use as the foundation for the builder object.
        /// </summary>
        [Parameter(Position = 0, ValueFromPipeline = true)]
        public string[] ConnectionString { get; set; }

        /// <summary>
        /// The application name that identifies your script to SQL Server in monitoring tools.
        /// Defaults to "dbatools Powershell Module".
        /// </summary>
        [Parameter()]
        public string ApplicationName { get; set; }

        /// <summary>
        /// The SQL Server instance name for the connection string.
        /// </summary>
        [Parameter()]
        [Alias("SqlInstance")]
        public string DataSource { get; set; }

        /// <summary>
        /// Credential object used to connect to the SQL Server Instance.
        /// </summary>
        [Parameter()]
        public PSCredential SqlCredential { get; set; }

        /// <summary>
        /// The default database context for the connection.
        /// </summary>
        [Parameter()]
        [Alias("Database")]
        public string InitialCatalog { get; set; }

        /// <summary>
        /// Enables Windows Authentication for the connection.
        /// </summary>
        [Parameter()]
        public SwitchParameter IntegratedSecurity { get; set; }

        /// <summary>
        /// The SQL Server login name for SQL Server Authentication.
        /// </summary>
        [Parameter()]
        public string UserName { get; set; }

        /// <summary>
        /// The password for SQL Server Authentication.
        /// </summary>
        [Parameter()]
        public string Password { get; set; }

        /// <summary>
        /// Enables Multiple Active Result Sets (MARS).
        /// </summary>
        [Parameter()]
        [Alias("MARS")]
        public SwitchParameter MultipleActiveResultSets { get; set; }

        /// <summary>
        /// Enables Always Encrypted functionality for the connection.
        /// </summary>
        [Parameter()]
        [Alias("AlwaysEncrypted")]
        [ValidateSet("Enabled")]
        public string ColumnEncryptionSetting { get; set; }

        /// <summary>
        /// Creates the builder using the older System.Data.SqlClient library.
        /// </summary>
        [Parameter()]
        public SwitchParameter Legacy { get; set; }

        /// <summary>
        /// Disables connection pooling for this connection.
        /// </summary>
        [Parameter()]
        public SwitchParameter NonPooledConnection { get; set; }

        /// <summary>
        /// The workstation identifier that appears in SQL Server logs.
        /// Defaults to the current computer name.
        /// </summary>
        [Parameter()]
        public string WorkstationID { get; set; }

        private static readonly string NewBuilderScript = @"
param($cs)
New-Object Microsoft.Data.SqlClient.SqlConnectionStringBuilder $cs
";

        private static readonly string NewLegacyBuilderScript = @"
param($cs)
New-Object System.Data.SqlClient.SqlConnectionStringBuilder $cs
";

        private ScriptBlock _newBuilderScriptBlock;
        private ScriptBlock _newLegacyBuilderScriptBlock;

        // Cached bound-state and resolved defaults
        private string _resolvedApplicationName;
        private string _resolvedWorkstationID;
        private bool _workstationIDBound;
        private bool _dataSourceBound;
        private bool _initialCatalogBound;
        private bool _integratedSecurityBound;
        private bool _marsBound;
        private bool _nonPooledConnectionBound;
        private string _effectiveUserName;
        private string _effectivePassword;
        private bool _credentialValidationFailed;

        // Cached reflection objects resolved once per builder type
        private MethodInfo _shouldSerializeMethod;
        private PropertyInfo _itemIndexer;

        /// <summary>
        /// Resolves default parameter values, validates credentials, and pre-compiles script blocks.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            // Validate mutually exclusive credential parameters
            if (SqlCredential != null && (!String.IsNullOrEmpty(UserName) || !String.IsNullOrEmpty(Password)))
            {
                StopFunction(
                    "You can only specify SQL Credential or Username/Password, not both.",
                    target: null);
                TestFunctionInterrupt();
                _credentialValidationFailed = true;
                return;
            }

            _newBuilderScriptBlock = ScriptBlock.Create(NewBuilderScript);
            _newLegacyBuilderScriptBlock = ScriptBlock.Create(NewLegacyBuilderScript);

            _resolvedApplicationName = TestBound("ApplicationName") ? ApplicationName : "dbatools Powershell Module";

            _workstationIDBound = TestBound("WorkstationID");
            _resolvedWorkstationID = _workstationIDBound ? WorkstationID : Environment.MachineName;

            _dataSourceBound = TestBound("DataSource");
            _initialCatalogBound = TestBound("InitialCatalog");
            _integratedSecurityBound = TestBound("IntegratedSecurity");
            _marsBound = TestBound("MultipleActiveResultSets");
            _nonPooledConnectionBound = TestBound("NonPooledConnection");

            // Extract credentials from SqlCredential if provided
            _effectiveUserName = UserName;
            _effectivePassword = Password;
            if (SqlCredential != null)
            {
                _effectiveUserName = SqlCredential.UserName;
                _effectivePassword = SqlCredential.GetNetworkCredential().Password;
            }
        }

        /// <summary>
        /// Processes each connection string from pipeline input and builds a SqlConnectionStringBuilder.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (_credentialValidationFailed)
                return;

            bool pooling = !NonPooledConnection.ToBool();

            // Default to empty string array if not provided
            string[] connectionStrings = ConnectionString;
            if (connectionStrings == null)
            {
                connectionStrings = new string[] { "" };
            }

            foreach (string cs in connectionStrings)
            {
                try
                {
                    object builder = CreateBuilder(cs);
                    if (builder == null)
                    {
                        continue;
                    }

                    // Resolve reflection objects once from the first builder's type
                    if (_shouldSerializeMethod == null)
                    {
                        ResolveReflection(builder);
                    }

                    ConfigureBuilder(builder, pooling);
                    WriteObject(builder);
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("Failed to create connection string builder for input: {0}", cs),
                        exception: ex,
                        target: cs,
                        isContinue: true);
                    TestFunctionInterrupt();
                    continue;
                }
            }
        }

        /// <summary>
        /// Creates a SqlConnectionStringBuilder object via PowerShell script.
        /// </summary>
        private object CreateBuilder(string cs)
        {
            ScriptBlock sb = Legacy.ToBool() ? _newLegacyBuilderScriptBlock : _newBuilderScriptBlock;
            var results = InvokeCommand.InvokeScript(
                false,
                sb,
                null,
                cs);

            if (results != null && results.Count > 0 && results[0] != null)
            {
                return results[0].BaseObject;
            }
            return null;
        }

        /// <summary>
        /// Resolves MethodInfo and PropertyInfo from the builder type once for reuse.
        /// </summary>
        private void ResolveReflection(object builder)
        {
            Type builderType = builder.GetType();
            _shouldSerializeMethod = builderType.GetMethod("ShouldSerialize", new Type[] { typeof(string) });
            _itemIndexer = builderType.GetProperty("Item", new Type[] { typeof(string) });
        }

        /// <summary>
        /// Configures the builder object by setting properties using cached reflection.
        /// </summary>
        private void ConfigureBuilder(object builder, bool pooling)
        {
            // Application Name: only set default if not already serialized in the connection string
            if (!CallShouldSerialize(builder, "Application Name"))
            {
                CallSetValue(builder, "Application Name", _resolvedApplicationName);
            }

            if (_dataSourceBound)
            {
                CallSetValue(builder, "Data Source", DataSource);
            }

            if (_initialCatalogBound)
            {
                CallSetValue(builder, "Initial Catalog", InitialCatalog);
            }

            if (_integratedSecurityBound)
            {
                CallSetValue(builder, "Integrated Security", IntegratedSecurity.ToBool());
            }

            // User ID and Integrated Security fallback
            if (!String.IsNullOrEmpty(_effectiveUserName))
            {
                CallSetValue(builder, "User ID", _effectiveUserName);
            }
            else if (!IntegratedSecurity.ToBool())
            {
                CallSetValue(builder, "Integrated Security", false);
            }

            if (!String.IsNullOrEmpty(_effectivePassword))
            {
                CallSetValue(builder, "Password", _effectivePassword);
            }

            // Workstation ID: explicit override takes priority, otherwise apply default if not in connection string
            if (_workstationIDBound || !CallShouldSerialize(builder, "Workstation ID"))
            {
                CallSetValue(builder, "Workstation ID", _resolvedWorkstationID);
            }

            if (_marsBound)
            {
                CallSetValue(builder, "MultipleActiveResultSets", MultipleActiveResultSets.ToBool());
            }

            if (!String.IsNullOrEmpty(ColumnEncryptionSetting))
            {
                CallSetValue(builder, "Column Encryption Setting", "Enabled");
            }

            // Pooling: explicit override takes priority, otherwise apply default if not in connection string
            if (_nonPooledConnectionBound || !CallShouldSerialize(builder, "Pooling"))
            {
                CallSetValue(builder, "Pooling", pooling);
            }
        }

        /// <summary>
        /// Calls ShouldSerialize on the builder using the cached MethodInfo.
        /// </summary>
        private bool CallShouldSerialize(object builder, string keyword)
        {
            if (_shouldSerializeMethod == null)
                return false;

            object result = _shouldSerializeMethod.Invoke(builder, new object[] { keyword });
            if (result is bool boolResult)
            {
                return boolResult;
            }
            return false;
        }

        /// <summary>
        /// Sets a value on the builder using the cached PropertyInfo indexer.
        /// </summary>
        private void CallSetValue(object builder, string keyword, object value)
        {
            if (_itemIndexer == null)
                return;

            _itemIndexer.SetValue(builder, value, new object[] { keyword });
        }

        /// <summary>
        /// Calls ShouldSerialize on the builder to check if a keyword already has a value.
        /// Static version for unit testing.
        /// </summary>
        internal static bool ShouldSerialize(object builder, string keyword)
        {
            if (builder == null)
                return false;

            var method = builder.GetType().GetMethod("ShouldSerialize", new Type[] { typeof(string) });
            if (method != null)
            {
                object result = method.Invoke(builder, new object[] { keyword });
                if (result is bool boolResult)
                {
                    return boolResult;
                }
            }
            return false;
        }

        /// <summary>
        /// Sets a value on the builder using its indexer. Static version for unit testing.
        /// </summary>
        internal static void SetBuilderValue(object builder, string keyword, object value)
        {
            if (builder == null)
                return;

            var indexer = builder.GetType().GetProperty("Item", new Type[] { typeof(string) });
            if (indexer != null)
            {
                indexer.SetValue(builder, value, new object[] { keyword });
            }
        }

        /// <summary>
        /// Gets a value from the builder using its indexer. Static version for unit testing.
        /// </summary>
        internal static object GetBuilderValue(object builder, string keyword)
        {
            if (builder == null)
                return null;

            var indexer = builder.GetType().GetProperty("Item", new Type[] { typeof(string) });
            if (indexer != null)
            {
                return indexer.GetValue(builder, new object[] { keyword });
            }
            return null;
        }
    }
}
