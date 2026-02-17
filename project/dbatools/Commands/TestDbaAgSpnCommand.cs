using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Validates Service Principal Name registration for Availability Group listeners in Active Directory.
    /// Checks whether the required SPNs are properly registered in Active Directory for each
    /// Availability Group listener's service account.
    /// </summary>
    [Cmdlet("Test", "DbaAgSpn")]
    public class TestDbaAgSpnCommand : DbaBaseCmdlet
    {
        #region Parameters

        /// <summary>
        /// The target SQL Server instance or instances. Server version must be SQL Server version 2012 or higher.
        /// </summary>
        [Parameter()]
        public DbaInstanceParameter[] SqlInstance { get; set; }

        /// <summary>
        /// Login to the target instance using alternative credentials.
        /// </summary>
        [Parameter()]
        public PSCredential SqlCredential { get; set; }

        /// <summary>
        /// Alternative credential for connecting to Active Directory.
        /// </summary>
        [Parameter()]
        public PSCredential Credential { get; set; }

        /// <summary>
        /// Specifies which availability groups to validate SPNs for by name.
        /// If not specified, all availability groups will be tested.
        /// </summary>
        [Parameter()]
        public string[] AvailabilityGroup { get; set; }

        /// <summary>
        /// Specifies which AG listeners to validate SPNs for by listener name.
        /// If not specified, all listeners within the specified availability groups will be tested.
        /// </summary>
        [Parameter()]
        public string[] Listener { get; set; }

        /// <summary>
        /// Accepts availability group objects from Get-DbaAvailabilityGroup for pipeline processing.
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        public PSObject[] InputObject { get; set; }

        #endregion Parameters

        #region State

        /// <summary>
        /// Cache for AD lookup results to avoid duplicate lookups.
        /// </summary>
        private Dictionary<string, PSObject> _resultCache;

        /// <summary>
        /// Accumulated SPN objects to process.
        /// </summary>
        private List<PSObject> _spns;

        #endregion State

        #region Script Blocks

        private static readonly ScriptBlock _getAgScript =
            ScriptBlock.Create("param($inst, $ag, $cred, $ee) Get-DbaAvailabilityGroup -SqlInstance $inst -AvailabilityGroup $ag -SqlCredential $cred -EnableException:$ee");

        private static readonly ScriptBlock _getAgNoCred =
            ScriptBlock.Create("param($inst, $ag, $ee) Get-DbaAvailabilityGroup -SqlInstance $inst -AvailabilityGroup $ag -EnableException:$ee");

        private static readonly ScriptBlock _getListenerFiltered =
            ScriptBlock.Create("param($ag, $listener) $ag | Get-DbaAgListener -Listener $listener");

        private static readonly ScriptBlock _getListenerAll =
            ScriptBlock.Create("param($ag) $ag | Get-DbaAgListener");

        private static readonly ScriptBlock _getAdObject =
            ScriptBlock.Create("param($acct, $type, $cred) Get-DbaADObject -ADObject $acct -Type $type -Credential $cred -EnableException");

        private static readonly ScriptBlock _getAdObjectNoCred =
            ScriptBlock.Create("param($acct, $type) Get-DbaADObject -ADObject $acct -Type $type -EnableException");

        private static readonly ScriptBlock _getUnderlyingObjectScript =
            ScriptBlock.Create("param($o) $o.GetUnderlyingObject()");

        #endregion Script Blocks

        /// <summary>
        /// Default display properties excluding Credential and DomainName.
        /// </summary>
        private static readonly string[] DefaultDisplayProperties = new string[]
        {
            "ComputerName", "SqlInstance", "InstanceName", "SqlProduct",
            "InstanceServiceAccount", "RequiredSPN", "IsSet", "Cluster",
            "TcpEnabled", "Port", "DynamicPort", "Warning", "Error"
        };

        /// <summary>
        /// Initializes the caches for AD results and SPN accumulation.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();
            _resultCache = new Dictionary<string, PSObject>(StringComparer.OrdinalIgnoreCase);
            _spns = new List<PSObject>();
        }

        /// <summary>
        /// Processes each pipeline input, gathering AGs and building SPN check objects.
        /// Then validates each SPN against Active Directory.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (TestBoundNot("SqlInstance", "InputObject"))
            {
                StopFunction("You must supply either -SqlInstance or an Input Object");
                TestFunctionInterrupt();
                return;
            }

            List<PSObject> agObjects = new List<PSObject>();

            // Get AGs from SqlInstance if provided
            if (SqlInstance != null && SqlInstance.Length > 0)
            {
                foreach (DbaInstanceParameter instance in SqlInstance)
                {
                    try
                    {
                        Collection<PSObject> results;
                        if (SqlCredential != null)
                        {
                            results = InvokeCommand.InvokeScript(
                                false, _getAgScript, null,
                                new object[] { instance, AvailabilityGroup, SqlCredential, EnableException.ToBool() });
                        }
                        else
                        {
                            results = InvokeCommand.InvokeScript(
                                false, _getAgNoCred, null,
                                new object[] { instance, AvailabilityGroup, EnableException.ToBool() });
                        }

                        if (results != null)
                        {
                            foreach (PSObject r in results)
                            {
                                if (r != null)
                                    agObjects.Add(r);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        StopFunction(
                            String.Format("Failed to get availability groups from {0}", instance),
                            exception: ex,
                            target: instance,
                            isContinue: true);
                        TestFunctionInterrupt();
                        continue;
                    }
                }
            }

            // Add pipeline InputObject items
            if (InputObject != null)
            {
                foreach (PSObject inputAg in InputObject)
                {
                    if (inputAg != null)
                        agObjects.Add(inputAg);
                }
            }

            // Process each AG to build SPN entries
            foreach (PSObject ag in agObjects)
            {
                string agName = GetPropertyString(ag, "Name");
                string agParentName = GetNestedPropertyString(ag, "Parent", "Name");
                WriteMessageAtLevel(
                    String.Format("Processing {0} on {1}", agName, agParentName),
                    MessageLevel.Verbose, null);

                Collection<PSObject> listeners;
                try
                {
                    if (Listener != null && Listener.Length > 0)
                    {
                        listeners = InvokeCommand.InvokeScript(
                            false, _getListenerFiltered, null,
                            new object[] { ag, Listener });
                    }
                    else
                    {
                        listeners = InvokeCommand.InvokeScript(
                            false, _getListenerAll, null,
                            new object[] { ag });
                    }
                }
                catch (Exception ex)
                {
                    WriteMessageAtLevel(
                        String.Format("Failed to get listeners for availability group {0}: {1}", agName, ex.Message),
                        MessageLevel.Warning, null);
                    listeners = null;
                }

                if (listeners == null)
                    continue;

                foreach (PSObject aglistener in listeners)
                {
                    if (aglistener == null)
                        continue;

                    ProcessListener(aglistener);
                }
            }

            // Now validate each accumulated SPN against AD
            foreach (PSObject spn in _spns)
            {
                ValidateSpn(spn);
            }

            // Clear for next pipeline batch
            _spns.Clear();
        }

        #region Helpers

        /// <summary>
        /// Processes a single AG listener to build the two SPN entries (with and without port).
        /// </summary>
        private void ProcessListener(PSObject aglistener)
        {
            string listenerName = GetPropertyString(aglistener, "Name");
            string listenerParentName = GetNestedPropertyString(aglistener, "Parent", "Name");
            WriteMessageAtLevel(
                String.Format("Processing {0} on {1}", listenerName, listenerParentName),
                MessageLevel.Verbose, null);

            // Navigate: aglistener.Parent.Parent = server
            PSObject server = GetNestedObject(aglistener, "Parent", "Parent");
            if (server == null)
                return;

            // Build version string: VersionString + DatabaseEngineEdition + "Edition" + platform
            string platform = GetPlatformSuffix(server);
            string versionString = GetPropertyString(server, "VersionString");
            string edition = GetPropertyString(server, "DatabaseEngineEdition");
            string version = String.Format("{0} {1} Edition {2}", versionString, edition, platform);

            int port = 0;
            object portObj = GetPropertyValue(aglistener, "PortNumber");
            if (portObj != null)
            {
                try { port = Convert.ToInt32(portObj); }
                catch (Exception) { /* default 0 */ }
            }

            // Get FQDN and build host entry
            string fqdn = GetNestedPropertyString(server, "Information", "FullyQualifiedNetName");
            string dnsname = BuildDnsDomain(fqdn);
            string hostEntry = String.Format("{0}.{1}", listenerName, dnsname);

            string instanceName = GetPropertyString(aglistener, "InstanceName");
            if (String.IsNullOrEmpty(instanceName))
                instanceName = "MSSQLSERVER";

            // Build first SPN (without port for default, with instance name for named)
            string required;
            if (String.Equals(instanceName, "MSSQLSERVER", StringComparison.OrdinalIgnoreCase))
            {
                required = String.Format("MSSQLSvc/{0}", hostEntry);
            }
            else
            {
                required = String.Format("MSSQLSvc/{0}:{1}", hostEntry, instanceName);
            }

            string serviceAccount = GetPropertyString(server, "ServiceAccount");
            bool isClustered = false;
            object isClusteredObj = GetPropertyValue(server, "IsClustered");
            if (isClusteredObj is bool)
                isClustered = (bool)isClusteredObj;

            string sqlInstance = GetPropertyString(aglistener, "SqlInstance");

            // First SPN entry (name-based)
            _spns.Add(BuildSpnObject(
                fqdn, sqlInstance, instanceName, version,
                serviceAccount, required, isClustered, port, Credential));

            // Second SPN entry (port-based)
            string requiredPort = String.Format("MSSQLSvc/{0}:{1}", hostEntry, port);
            _spns.Add(BuildSpnObject(
                fqdn, sqlInstance, instanceName, version,
                serviceAccount, requiredPort, isClustered, port, Credential));
        }

        /// <summary>
        /// Builds a PSObject representing an SPN validation result.
        /// </summary>
        internal static PSObject BuildSpnObject(
            string computerName, string sqlInstance, string instanceName,
            string sqlProduct, string serviceAccount, string requiredSpn,
            bool isClustered, int port, PSCredential credential)
        {
            PSObject spn = new PSObject();
            spn.Properties.Add(new PSNoteProperty("ComputerName", computerName));
            spn.Properties.Add(new PSNoteProperty("SqlInstance", sqlInstance));
            spn.Properties.Add(new PSNoteProperty("InstanceName", instanceName));
            spn.Properties.Add(new PSNoteProperty("SqlProduct", sqlProduct));
            spn.Properties.Add(new PSNoteProperty("InstanceServiceAccount", serviceAccount));
            spn.Properties.Add(new PSNoteProperty("RequiredSPN", requiredSpn));
            spn.Properties.Add(new PSNoteProperty("IsSet", false));
            spn.Properties.Add(new PSNoteProperty("Cluster", isClustered));
            spn.Properties.Add(new PSNoteProperty("TcpEnabled", true));
            spn.Properties.Add(new PSNoteProperty("Port", port));
            spn.Properties.Add(new PSNoteProperty("DynamicPort", false));
            spn.Properties.Add(new PSNoteProperty("Warning", "None"));
            spn.Properties.Add(new PSNoteProperty("Error", "None"));
            spn.Properties.Add(new PSNoteProperty("Credential", credential));
            return spn;
        }

        /// <summary>
        /// Validates a single SPN object against Active Directory.
        /// </summary>
        private void ValidateSpn(PSObject spn)
        {
            string sqlInstance = GetPropertyString(spn, "SqlInstance");
            WriteMessageAtLevel(
                String.Format("Processing SPN on {0}", sqlInstance),
                MessageLevel.Verbose, null);

            string searchfor = "User";
            string serviceAccount = GetPropertyString(spn, "InstanceServiceAccount");

            // Check for virtual accounts (LocalSystem, NT SERVICE\*)
            if (String.Equals(serviceAccount, "LocalSystem", StringComparison.OrdinalIgnoreCase) ||
                (serviceAccount != null && serviceAccount.StartsWith("NT SERVICE\\", StringComparison.OrdinalIgnoreCase)))
            {
                WriteMessageAtLevel(
                    "Virtual account detected, changing target registration to computername",
                    MessageLevel.Verbose, null);
                // The PS1 references $resolved here but it is never defined, producing a broken
                // account name. This is a known PS1 bug. We fix it by deriving the machine account
                // from the ComputerName FQDN, which is the intended behavior per Microsoft SPN docs.
                string computerName = GetPropertyString(spn, "ComputerName");
                string domain = BuildDnsDomain(computerName);
                string machine = computerName;
                if (computerName != null && computerName.Contains("."))
                {
                    machine = computerName.Split('.')[0];
                }
                serviceAccount = String.Format("{0}\\{1}$", domain, machine);
                SetProperty(spn, "InstanceServiceAccount", serviceAccount);
                searchfor = "Computer";
            }
            else if (serviceAccount != null && serviceAccount.Contains("\\") && serviceAccount.EndsWith("$"))
            {
                // Managed Service Account
                WriteMessageAtLevel(
                    "Managed Service Account detected",
                    MessageLevel.Verbose, null);
                searchfor = "Computer";
            }

            // Cache-aware AD lookup
            PSObject result = null;
            if (!String.IsNullOrEmpty(serviceAccount) && !_resultCache.ContainsKey(serviceAccount))
            {
                WriteMessageAtLevel(
                    String.Format("Searching for {0}", serviceAccount),
                    MessageLevel.Verbose, null);
                try
                {
                    Collection<PSObject> adResults;
                    if (Credential != null)
                    {
                        adResults = InvokeCommand.InvokeScript(
                            false, _getAdObject, null,
                            new object[] { serviceAccount, searchfor, Credential });
                    }
                    else
                    {
                        adResults = InvokeCommand.InvokeScript(
                            false, _getAdObjectNoCred, null,
                            new object[] { serviceAccount, searchfor });
                    }

                    if (adResults != null && adResults.Count > 0)
                    {
                        result = adResults[0];
                        _resultCache[serviceAccount] = result;
                    }
                    else
                    {
                        _resultCache[serviceAccount] = null;
                    }
                }
                catch (Exception)
                {
                    if (!String.IsNullOrEmpty(serviceAccount))
                    {
                        WriteMessageAtLevel(
                            String.Format(
                                "AD lookup failure. This may be because the domain cannot be resolved for the SQL Server service account ({0}).",
                                serviceAccount),
                            MessageLevel.Warning, null);
                    }
                    // Do not cache on failure - allow retry for next SPN with same account
                    // (matches PS1 behavior where exceptions do not add to $resultCache)
                }
            }
            else if (!String.IsNullOrEmpty(serviceAccount))
            {
                result = _resultCache[serviceAccount];
            }

            if (result != null)
            {
                try
                {
                    // Call GetUnderlyingObject() on the result
                    object underlying = InvokeGetUnderlyingObject(result);
                    if (underlying != null)
                    {
                        PSObject underlyingPso = PSObject.AsPSObject(underlying);
                        // Check if servicePrincipalName contains the required SPN
                        string requiredSpn = GetPropertyString(spn, "RequiredSPN");
                        object spnPropValue = GetNestedPropertyValue(underlyingPso, "Properties", "servicePrincipalName");
                        if (spnPropValue != null && ContainsSPN(spnPropValue, requiredSpn))
                        {
                            SetProperty(spn, "IsSet", true);
                        }
                    }
                }
                catch (Exception)
                {
                    WriteMessageAtLevel(
                        String.Format(
                            "The SQL Service account ({0}) has been found, but you don't have enough permission to inspect its SPNs",
                            serviceAccount),
                        MessageLevel.Warning, null);
                    return;
                }
            }
            else
            {
                WriteMessageAtLevel(
                    "SQL Service account not found. Results may not be accurate.",
                    MessageLevel.Warning, null);
                // Output without checking IsSet, matching PS1 behavior
                SetDefaultDisplay(spn);
                WriteObject(spn);
                return;
            }

            // Set error if SPN is missing and TCP is enabled
            object isSet = GetPropertyValue(spn, "IsSet");
            bool isSetBool = false;
            if (isSet is bool)
                isSetBool = (bool)isSet;

            object tcpEnabled = GetPropertyValue(spn, "TcpEnabled");
            bool tcpEnabledBool = true;
            if (tcpEnabled is bool)
                tcpEnabledBool = (bool)tcpEnabled;

            if (!isSetBool && tcpEnabledBool)
            {
                SetProperty(spn, "Error", "SPN missing");
            }

            SetDefaultDisplay(spn);
            WriteObject(spn);
        }

        /// <summary>
        /// Extracts the platform suffix from the server's Platform property.
        /// The PS1 does: $server.Platform -split " " | Select-Object -Last 1
        /// </summary>
        internal static string GetPlatformSuffix(PSObject server)
        {
            string platform = GetPropertyString(server, "Platform");
            if (String.IsNullOrEmpty(platform))
                return "";

            string[] parts = platform.Split(' ');
            return parts[parts.Length - 1];
        }

        /// <summary>
        /// Builds the DNS domain name from an FQDN by removing the first segment.
        /// e.g., "server.domain.local" -> "domain.local"
        /// </summary>
        internal static string BuildDnsDomain(string fqdn)
        {
            if (String.IsNullOrEmpty(fqdn))
                return "";

            string[] parts = fqdn.Split('.');
            if (parts.Length <= 1)
                return "";

            return String.Join(".", parts, 1, parts.Length - 1);
        }

        /// <summary>
        /// Gets a string property value from a PSObject.
        /// </summary>
        internal static string GetPropertyString(PSObject obj, string propertyName)
        {
            if (obj == null)
                return null;
            try
            {
                PSPropertyInfo prop = obj.Properties[propertyName];
                if (prop != null && prop.Value != null)
                    return prop.Value.ToString();
            }
            catch (Exception)
            {
                // Property may not exist
            }
            return null;
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
                // Property may not exist
            }
            return null;
        }

        /// <summary>
        /// Sets or updates a property on a PSObject.
        /// </summary>
        internal static void SetProperty(PSObject obj, string name, object value)
        {
            if (obj == null)
                return;
            try
            {
                PSPropertyInfo existing = obj.Properties[name];
                if (existing != null)
                    existing.Value = value;
                else
                    obj.Properties.Add(new PSNoteProperty(name, value));
            }
            catch (Exception)
            {
                try
                {
                    obj.Properties.Remove(name);
                    obj.Properties.Add(new PSNoteProperty(name, value));
                }
                catch (Exception) { }
            }
        }

        /// <summary>
        /// Gets a nested property string like obj.Prop1.Prop2.
        /// </summary>
        internal static string GetNestedPropertyString(PSObject obj, string prop1, string prop2)
        {
            PSObject nested = GetNestedObject(obj, prop1);
            if (nested == null)
                return null;
            return GetPropertyString(nested, prop2);
        }

        /// <summary>
        /// Gets a nested PSObject: obj.Property.
        /// </summary>
        private static PSObject GetNestedObject(PSObject obj, string propertyName)
        {
            if (obj == null)
                return null;
            try
            {
                PSPropertyInfo prop = obj.Properties[propertyName];
                if (prop != null && prop.Value != null)
                    return PSObject.AsPSObject(prop.Value);
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>
        /// Gets a nested PSObject through two property levels: obj.Prop1.Prop2.
        /// </summary>
        private static PSObject GetNestedObject(PSObject obj, string prop1, string prop2)
        {
            PSObject first = GetNestedObject(obj, prop1);
            if (first == null)
                return null;
            return GetNestedObject(first, prop2);
        }

        /// <summary>
        /// Gets a nested property value through two levels: obj.Prop1[Prop2].
        /// Used for accessing Properties collection on DirectoryEntry-like objects.
        /// </summary>
        private static object GetNestedPropertyValue(PSObject obj, string collectionProp, string itemName)
        {
            if (obj == null)
                return null;
            try
            {
                PSPropertyInfo prop = obj.Properties[collectionProp];
                if (prop != null && prop.Value != null)
                {
                    PSObject collection = PSObject.AsPSObject(prop.Value);
                    // Try indexer access
                    PSPropertyInfo item = collection.Properties[itemName];
                    if (item != null)
                        return item.Value;
                }
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>
        /// Invokes GetUnderlyingObject() on a PSObject, returning the result.
        /// </summary>
        private object InvokeGetUnderlyingObject(PSObject obj)
        {
            if (obj == null)
                return null;
            try
            {
                PSMemberInfo member = obj.Members["GetUnderlyingObject"];
                if (member is PSMethodInfo method)
                {
                    return method.Invoke();
                }
            }
            catch (Exception) { }

            // Fallback: use pre-compiled script block
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false,
                    _getUnderlyingObjectScript,
                    null,
                    new object[] { obj });
                if (results != null && results.Count > 0)
                    return results[0].BaseObject;
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>
        /// Checks if a collection-like value contains the specified SPN string.
        /// Handles IEnumerable, arrays, and PropertyValueCollection.
        /// </summary>
        internal static bool ContainsSPN(object collection, string requiredSpn)
        {
            if (collection == null || requiredSpn == null)
                return false;

            // Check for string first (string is IEnumerable<char>, so must be handled before IEnumerable)
            if (collection is string singleValue)
                return String.Equals(singleValue, requiredSpn, StringComparison.OrdinalIgnoreCase);

            // Try as IEnumerable
            System.Collections.IEnumerable enumerable = collection as System.Collections.IEnumerable;
            if (enumerable != null)
            {
                foreach (object item in enumerable)
                {
                    if (item != null && String.Equals(item.ToString(), requiredSpn, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            }

            // Single value comparison
            return String.Equals(collection.ToString(), requiredSpn, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Sets the DefaultDisplayPropertySet to exclude Credential and DomainName.
        /// </summary>
        private static void SetDefaultDisplay(PSObject obj)
        {
            if (obj == null)
                return;
            try { obj.Members.Remove("PSStandardMembers"); }
            catch (Exception) { }

            try
            {
                obj.Members.Add(new PSMemberSet("PSStandardMembers", new PSMemberInfo[]
                {
                    new PSPropertySet("DefaultDisplayPropertySet", DefaultDisplayProperties)
                }));
            }
            catch (Exception) { }
        }

        #endregion Helpers
    }
}
