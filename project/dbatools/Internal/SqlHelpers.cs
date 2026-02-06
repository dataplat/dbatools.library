using System;
using System.Management.Automation;
using System.Text;

namespace Dataplat.Dbatools.Internal
{
    /// <summary>
    /// Static SQL Server utility helpers mirroring PS1 functions like
    /// Test-SqlAgent, Test-SqlSa, Get-SqlSaLogin, Get-SqlDefaultPaths,
    /// Invoke-SmoCheck, Hide-ConnectionString, etc.
    /// </summary>
    public static class SqlHelpers
    {
        /// <summary>
        /// Tests whether SQL Server Agent is running on the given server.
        /// Mirrors Test-SqlAgent.ps1.
        /// </summary>
        /// <param name="cmdlet">PSCmdlet for messaging</param>
        /// <param name="server">SMO Server object</param>
        /// <returns>True if agent is running</returns>
        public static bool TestAgent(PSCmdlet cmdlet, object server)
        {
            if (server == null)
                return false;

            try
            {
                dynamic srv = server;
                string status = srv.JobServer.Status.ToString();
                return String.Equals(status, "Running", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Tests whether the current login has sysadmin privileges.
        /// Mirrors Test-SqlSa.ps1.
        /// </summary>
        /// <param name="cmdlet">PSCmdlet for messaging</param>
        /// <param name="server">SMO Server object</param>
        /// <returns>True if current login is sysadmin</returns>
        public static bool TestSa(PSCmdlet cmdlet, object server)
        {
            if (server == null)
                return false;

            try
            {
                dynamic srv = server;
                string script = @"
param($srv)
$srv.ConnectionContext.ExecuteScalar('SELECT IS_SRVROLEMEMBER(''sysadmin'')') -eq 1
";
                var result = cmdlet.InvokeCommand.InvokeScript(
                    false,
                    ScriptBlock.Create(script),
                    null,
                    server
                );
                if (result != null && result.Count > 0)
                    return Convert.ToBoolean(result[0].BaseObject);
            }
            catch
            {
                // Fallback
            }
            return false;
        }

        /// <summary>
        /// Gets the sa login name (or equivalent) for the server.
        /// Mirrors Get-SaLoginName / Get-SqlSaLogin.ps1.
        /// </summary>
        /// <param name="server">SMO Server object</param>
        /// <returns>The sa login name</returns>
        public static string GetSaLogin(object server)
        {
            if (server == null)
                return "sa";

            try
            {
                dynamic srv = server;
                foreach (dynamic login in srv.Logins)
                {
                    if ((int)login.ID == 1)
                        return (string)login.Name;
                }
            }
            catch
            {
                // Fallback
            }
            return "sa";
        }

        /// <summary>
        /// Gets default SQL Server paths (Data, Log, Backup).
        /// Mirrors Get-SqlDefaultPaths.ps1.
        /// </summary>
        /// <param name="server">SMO Server object</param>
        /// <param name="type">Path type: "Data", "Log", or "Backup"</param>
        /// <returns>The default path</returns>
        public static string GetDefaultPaths(object server, string type)
        {
            if (server == null || String.IsNullOrEmpty(type))
                return null;

            try
            {
                dynamic srv = server;
                switch (type.ToLowerInvariant())
                {
                    case "data":
                        string dataPath = srv.DefaultFile;
                        if (String.IsNullOrEmpty(dataPath))
                            dataPath = srv.MasterDBPath;
                        return dataPath;

                    case "log":
                        string logPath = srv.DefaultLog;
                        if (String.IsNullOrEmpty(logPath))
                            logPath = srv.MasterDBLogPath;
                        return logPath;

                    case "backup":
                        return srv.BackupDirectory;

                    default:
                        return null;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Invokes SMO object's Create() method with error handling.
        /// Mirrors Invoke-Create.ps1.
        /// </summary>
        /// <param name="smoObject">The SMO object to create</param>
        public static void InvokeCreate(object smoObject)
        {
            if (smoObject == null)
                throw new ArgumentNullException("smoObject");

            dynamic obj = smoObject;
            obj.Create();
        }

        /// <summary>
        /// Invokes SMO object's Alter() method with error handling.
        /// Mirrors Invoke-Alter.ps1.
        /// </summary>
        /// <param name="smoObject">The SMO object to alter</param>
        public static void InvokeAlter(object smoObject)
        {
            if (smoObject == null)
                throw new ArgumentNullException("smoObject");

            dynamic obj = smoObject;
            obj.Alter();
        }

        /// <summary>
        /// Performs a basic SMO connectivity check.
        /// Mirrors Invoke-SmoCheck.ps1.
        /// </summary>
        /// <param name="server">SMO Server object</param>
        /// <returns>True if server is connected and responsive</returns>
        public static bool InvokeSmoCheck(object server)
        {
            if (server == null)
                return false;

            try
            {
                dynamic srv = server;
                int version = srv.VersionMajor;
                return version > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Starts a DBCC CHECKDB operation on the specified database.
        /// Mirrors Start-DbccCheck.ps1.
        /// </summary>
        /// <param name="server">SMO Server object</param>
        /// <param name="databaseName">Database to check</param>
        public static void StartDbccCheck(object server, string databaseName)
        {
            if (server == null || String.IsNullOrEmpty(databaseName))
                return;

            try
            {
                dynamic srv = server;
                string sql = String.Format("DBCC CHECKDB([{0}])", databaseName.Replace("]", "]]"));
                srv.ConnectionContext.ExecuteNonQuery(sql);
            }
            catch
            {
                throw;
            }
        }

        /// <summary>
        /// Gets SQLCMD variables from a SQL file content.
        /// Mirrors Get-SqlCmdVars.ps1 - extracts $(VariableName) patterns.
        /// </summary>
        /// <param name="sqlContent">SQL file content</param>
        /// <returns>Array of variable names found</returns>
        public static string[] GetCmdVars(string sqlContent)
        {
            if (String.IsNullOrEmpty(sqlContent))
                return new string[0];

            var matches = System.Text.RegularExpressions.Regex.Matches(sqlContent, @"\$\((\w+)\)");
            var result = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                if (match.Groups.Count > 1)
                    result.Add(match.Groups[1].Value);
            }

            string[] arr = new string[result.Count];
            result.CopyTo(arr);
            return arr;
        }
    }
}
