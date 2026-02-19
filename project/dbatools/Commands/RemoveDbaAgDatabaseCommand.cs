using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Removes databases from availability groups on SQL Server instances.
    /// This stops replication and high availability protection for those databases while preserving
    /// the actual database files on each replica. Supports pipeline input from Get-DbaAgDatabase.
    /// </summary>
    [Cmdlet("Remove", "DbaAgDatabase", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.High)]
    public class RemoveDbaAgDatabaseCommand : DbaBaseCmdlet
    {
        #region Parameters

        /// <summary>
        /// The target SQL Server instance or instances. Server version must be SQL Server version 2012 or higher.
        /// </summary>
        [Parameter()]
        public DbaInstanceParameter[] SqlInstance { get; set; }

        /// <summary>
        /// Login to the target instance using alternative credentials. Accepts PowerShell credentials (Get-Credential).
        /// Windows Authentication, SQL Server Authentication, Active Directory - Password, and Active Directory - Integrated are all supported.
        /// For MFA support, please use Connect-DbaInstance.
        /// </summary>
        [Parameter()]
        public PSCredential SqlCredential { get; set; }

        /// <summary>
        /// Specifies which databases to remove from their availability groups. Accepts multiple database names as an array.
        /// Required when using the SqlInstance parameter.
        /// </summary>
        [Parameter()]
        public string[] Database { get; set; }

        /// <summary>
        /// Limits the operation to databases within specific availability groups. When specified, only databases
        /// belonging to these availability groups will be removed.
        /// </summary>
        [Parameter()]
        public string[] AvailabilityGroup { get; set; }

        /// <summary>
        /// Accepts availability group database objects from Get-DbaAgDatabase or database objects from Get-DbaDatabase
        /// through the pipeline. This enables efficient batch operations and complex filtering scenarios.
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        public object[] InputObject { get; set; }

        #endregion Parameters

        #region Static ScriptBlocks

        /// <summary>
        /// Calls Get-DbaAgDatabase with appropriate parameters.
        /// </summary>
        private static readonly ScriptBlock _getDbaAgDatabaseScript = ScriptBlock.Create(@"
param($si, $sc, $db, $ag, $hasCred, $hasDb, $hasAg)
$params = @{ SqlInstance = $si }
if ($hasCred) { $params['SqlCredential'] = $sc }
if ($hasDb) { $params['Database'] = $db }
if ($hasAg) { $params['AvailabilityGroup'] = $ag }
Get-DbaAgDatabase @params
");

        /// <summary>
        /// Gets the server name from the database's AG parent hierarchy.
        /// AvailabilityDatabase.Parent = AvailabilityGroup, AvailabilityGroup.Parent = Server.
        /// </summary>
        private static readonly ScriptBlock _getServerNameScript = ScriptBlock.Create(@"
param($db) $db.Parent.Parent.Name
");

        /// <summary>
        /// Drops the database from the availability group and returns the AG name.
        /// Uses the Parent.AvailabilityDatabases collection to ensure correct object reference.
        /// </summary>
        private static readonly ScriptBlock _dropAgDatabaseScript = ScriptBlock.Create(@"
param($db)
$agName = $db.Parent.Name
$db.Parent.AvailabilityDatabases[$db.Name].Drop()
$agName
");

        #endregion Static ScriptBlocks

        /// <summary>
        /// Processes each pipeline item or SqlInstance-based query to remove databases from availability groups.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (TestFunctionInterrupt())
                return;

            // Validate: need SqlInstance or InputObject
            if (TestBoundNot("SqlInstance", "InputObject"))
            {
                StopFunction("You must supply either -SqlInstance or an Input Object");
                return;
            }

            // Validate: SqlInstance requires Database
            if (TestBound("SqlInstance") && TestBoundNot("Database"))
            {
                StopFunction("You must specify one or more databases and one or more Availability Groups when using the SqlInstance parameter.");
                return;
            }

            // Handle Database-type InputObject: extract names to Database array
            // PS1: if ($InputObject[0].GetType().Name -eq 'Database') { $Database += $InputObject.Name }
            if (InputObject != null && InputObject.Length > 0)
            {
                string typeName = GetBaseTypeName(InputObject[0]);
                if (string.Equals(typeName, "Database", StringComparison.OrdinalIgnoreCase))
                {
                    List<string> dbNames = new List<string>();
                    if (Database != null)
                    {
                        dbNames.AddRange(Database);
                    }
                    foreach (object obj in InputObject)
                    {
                        string name = GetPropertyString(PSObject.AsPSObject(obj), "Name");
                        if (name != null)
                        {
                            dbNames.Add(name);
                        }
                    }
                    Database = dbNames.ToArray();
                }
            }

            // If SqlInstance provided, fetch AG databases and add to InputObject
            // PS1: $InputObject += Get-DbaAgDatabase -SqlInstance $SqlInstance -SqlCredential $SqlCredential -Database $Database
            if (TestBound("SqlInstance"))
            {
                List<object> items = new List<object>();
                if (InputObject != null)
                {
                    items.AddRange(InputObject);
                }

                Collection<PSObject> agDbs = GetDbaAgDatabase();
                if (agDbs != null)
                {
                    foreach (PSObject db in agDbs)
                    {
                        if (db != null)
                        {
                            items.Add(db.BaseObject ?? db);
                        }
                    }
                }
                InputObject = items.ToArray();
            }

            if (InputObject == null)
                return;

            foreach (object dbObj in InputObject)
            {
                if (dbObj == null)
                    continue;
                ProcessDatabase(dbObj);
            }
        }

        /// <summary>
        /// Processes a single database object: validates via ShouldProcess, drops from AG, and outputs result.
        /// </summary>
        private void ProcessDatabase(object dbObj)
        {
            PSObject db = PSObject.AsPSObject(dbObj);
            string dbName = GetPropertyString(db, "Name");
            string serverName = GetServerName(dbObj);

            // PS1: if ($Pscmdlet.ShouldProcess($db.Parent.Parent.Name, "Removing availability group database $db"))
            if (ShouldProcess(serverName ?? dbName ?? "Unknown",
                String.Format("Removing availability group database {0}", dbName ?? "Unknown")))
            {
                try
                {
                    string agName = DropAgDatabase(dbObj);

                    PSObject output = new PSObject();
                    output.Properties.Add(new PSNoteProperty("ComputerName", GetPropertyString(db, "ComputerName")));
                    output.Properties.Add(new PSNoteProperty("InstanceName", GetPropertyString(db, "InstanceName")));
                    output.Properties.Add(new PSNoteProperty("SqlInstance", GetPropertyString(db, "SqlInstance")));
                    output.Properties.Add(new PSNoteProperty("AvailabilityGroup", agName));
                    output.Properties.Add(new PSNoteProperty("Database", dbName));
                    output.Properties.Add(new PSNoteProperty("Status", "Removed"));
                    WriteObject(output);
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("Failed to remove {0} from availability group", dbName),
                        errorRecord: new ErrorRecord(ex, "RemoveDbaAgDatabase", ErrorCategory.InvalidOperation, dbObj),
                        target: dbObj, isContinue: true);
                    TestFunctionInterrupt();
                }
            }
        }

        #region Helpers

        /// <summary>
        /// Gets the simple type name of an object's base type, unwrapping PSObject if necessary.
        /// </summary>
        private static string GetBaseTypeName(object obj)
        {
            if (obj == null)
                return null;
            object baseObj = obj;
            if (obj is PSObject pso && pso.BaseObject != null)
                baseObj = pso.BaseObject;
            return baseObj.GetType().Name;
        }

        /// <summary>
        /// Gets the server name from the AG database's parent hierarchy via ScriptBlock.
        /// </summary>
        private string GetServerName(object dbObj)
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false, _getServerNameScript, null, new object[] { dbObj });
                if (results != null && results.Count > 0 && results[0] != null)
                    return results[0].BaseObject as string ?? results[0].ToString();
            }
            catch (Exception ex)
            {
                WriteMessageAtLevel(
                    String.Format("Could not resolve server name from object hierarchy: {0}", ex.Message),
                    MessageLevel.Debug, null);
            }
            return null;
        }

        /// <summary>
        /// Drops the database from the availability group and returns the AG name.
        /// </summary>
        private string DropAgDatabase(object dbObj)
        {
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, _dropAgDatabaseScript, null, new object[] { dbObj });
            if (results != null && results.Count > 0 && results[0] != null)
                return results[0].BaseObject as string ?? results[0].ToString();
            return null;
        }

        /// <summary>
        /// Calls Get-DbaAgDatabase with the current SqlInstance, SqlCredential, Database, and AvailabilityGroup parameters.
        /// </summary>
        private Collection<PSObject> GetDbaAgDatabase()
        {
            try
            {
                return InvokeCommand.InvokeScript(false, _getDbaAgDatabaseScript, null,
                    new object[]
                    {
                        SqlInstance,
                        SqlCredential,
                        Database,
                        AvailabilityGroup,
                        SqlCredential != null,
                        Database != null && Database.Length > 0,
                        TestBound("AvailabilityGroup")
                    });
            }
            catch (Exception ex)
            {
                StopFunction(
                    "Failed to get availability group databases",
                    errorRecord: new ErrorRecord(ex, "RemoveDbaAgDatabase_GetAgDb", ErrorCategory.ConnectionError, SqlInstance),
                    target: SqlInstance, isContinue: true);
                TestFunctionInterrupt();
                return null;
            }
        }

        #endregion Helpers
    }
}
