using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Internal;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Retrieves detailed SQL Server build information including service pack, cumulative update,
    /// KB articles, and support lifecycle dates. Translates build numbers into meaningful patch
    /// levels, can query live instances, look up specific build numbers, search by KB article
    /// numbers, or find builds by major version with SP/CU combinations.
    /// </summary>
    [Cmdlet("Get", "DbaBuild", DefaultParameterSetName = "Build")]
    public class GetDbaBuildCommand : DbaBaseCmdlet
    {
        #region Parameters

        /// <summary>
        /// SQL Server build numbers to look up. Accepts version strings like "12.00.4502" or "13.0.5026".
        /// </summary>
        [Parameter()]
        public Version[] Build { get; set; }

        /// <summary>
        /// Knowledge Base article numbers to look up. Accepts formats like "KB4057119" or "4057119".
        /// </summary>
        [Parameter()]
        public string[] Kb { get; set; }

        /// <summary>
        /// SQL Server major version. Accepts formats like "SQL2016", "2016", or "2008R2".
        /// </summary>
        [Parameter()]
        [ValidateNotNullOrEmpty]
        public string MajorVersion { get; set; }

        /// <summary>
        /// Service pack level. Accepts formats like "SP1", "1", or "RTM". Defaults to "RTM".
        /// </summary>
        [Parameter()]
        [ValidateNotNullOrEmpty]
        [Alias("SP")]
        public string ServicePack { get; set; } = "RTM";

        /// <summary>
        /// Cumulative update level. Accepts formats like "CU5", "5", or "CU0".
        /// </summary>
        [Parameter()]
        [Alias("CU")]
        public string CumulativeUpdate { get; set; }

        /// <summary>
        /// Target SQL Server instances to query for build information.
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        public DbaInstanceParameter[] SqlInstance { get; set; }

        /// <summary>
        /// Credential for SQL Server authentication.
        /// </summary>
        [Parameter()]
        public PSCredential SqlCredential { get; set; }

        /// <summary>
        /// Forces an online refresh of the local build reference index.
        /// </summary>
        [Parameter()]
        public SwitchParameter Update { get; set; }

        #endregion Parameters

        #region State

        /// <summary>
        /// Whether SqlInstance came via pipeline input.
        /// </summary>
        private bool _isPipelineSqlInstance;

        /// <summary>
        /// Validated major version string (e.g., "2016", "2008R2").
        /// </summary>
        private string _majorVersion;

        /// <summary>
        /// Validated service pack string (e.g., "RTM", "SP1").
        /// </summary>
        private string _servicePack;

        /// <summary>
        /// Validated cumulative update string (e.g., "", "CU5").
        /// </summary>
        private string _cumulativeUpdate;

        /// <summary>
        /// The loaded build reference index data.
        /// </summary>
        private PSObject[] _indexData;

        /// <summary>
        /// Whether BeginProcessing completed successfully.
        /// </summary>
        private bool _beginSucceeded;

        /// <summary>
        /// Default display properties when SqlInstance is used (show all).
        /// </summary>
        private static readonly string[] AllDisplayProperties = new string[]
        {
            "SqlInstance", "Build", "NameLevel", "SPLevel", "CULevel",
            "KBLevel", "BuildLevel", "SupportedUntil", "MatchType", "Warning"
        };

        /// <summary>
        /// Default display properties when querying by Build/Kb/MajorVersion (exclude SqlInstance).
        /// </summary>
        private static readonly string[] NonInstanceDisplayProperties = new string[]
        {
            "Build", "NameLevel", "SPLevel", "CULevel",
            "KBLevel", "BuildLevel", "SupportedUntil", "MatchType", "Warning"
        };

        #endregion State

        #region Cmdlet Lifecycle

        /// <summary>
        /// Validates parameters, checks mutual exclusivity, and loads the build reference index.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            _isPipelineSqlInstance = MyInvocation.ExpectingInput;

            // Determine which exclusive parameter groups are bound
            List<string> complianceSpec = new List<string>();

            if (TestBound("Build"))
                complianceSpec.Add("Build");

            if (TestBound("Kb"))
                complianceSpec.Add("Kb");

            if (TestBound("MajorVersion") || TestBound("ServicePack") || TestBound("CumulativeUpdate"))
                complianceSpec.Add("MajorVersion");

            if (_isPipelineSqlInstance || TestBound("SqlInstance"))
                complianceSpec.Add("SqlInstance");

            if (complianceSpec.Count == 0 && !TestBound("Update") && !_isPipelineSqlInstance)
            {
                StopFunction("You need to choose at least one parameter.", category: ErrorCategory.InvalidArgument);
                return;
            }

            if (complianceSpec.Count > 1)
            {
                StopFunction(
                    String.Format("{0} are mutually exclusive. Please choose one or the other. Quitting.",
                        String.Join(", ", complianceSpec.ToArray())),
                    category: ErrorCategory.InvalidArgument);
                return;
            }

            // Validate that SP/CU require MajorVersion
            if ((TestBound("ServicePack") || TestBound("CumulativeUpdate")) && !TestBound("MajorVersion"))
            {
                StopFunction("-MajorVersion is required when specifying SP or CU.", category: ErrorCategory.InvalidArgument);
                return;
            }

            // Validate and normalize MajorVersion
            _majorVersion = MajorVersion;
            _servicePack = ServicePack;
            _cumulativeUpdate = CumulativeUpdate;

            if (_majorVersion != null)
            {
                string normalized = NormalizeMajorVersion(_majorVersion);
                if (normalized == null)
                {
                    StopFunction("Incorrect SQL Server version format: use SQL2XXX or just 2XXXX - SQL2012, SQL2008R2");
                    return;
                }
                _majorVersion = normalized;

                _servicePack = NormalizeServicePack(_servicePack);
                if (_servicePack == null)
                {
                    StopFunction("Incorrect SQL Server service pack format: use SPX, X or RTM, where X is a service pack number");
                    return;
                }

                if (_cumulativeUpdate != null)
                {
                    _cumulativeUpdate = NormalizeCumulativeUpdate(_cumulativeUpdate);
                    if (_cumulativeUpdate == null)
                    {
                        StopFunction("Incorrect SQL Server cumulative update format: use CUX or X, where X is a cumulative update number");
                        return;
                    }
                }
            }

            // Load build reference index
            try
            {
                _indexData = LoadBuildReferenceIndex(Update.ToBool());
            }
            catch (Exception ex)
            {
                StopFunction("Error loading SQL build reference", exception: ex);
                return;
            }

            _beginSucceeded = true;
        }

        /// <summary>
        /// Processes each input: resolves builds from SqlInstance, Build, Kb, or MajorVersion parameters.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (!_beginSucceeded || TestFunctionInterrupt())
                return;

            // SqlInstance loop
            if (SqlInstance != null)
            {
                foreach (DbaInstanceParameter instance in SqlInstance)
                {
                    object server;
                    try
                    {
                        server = ConnectInstance(instance);
                    }
                    catch (Exception ex)
                    {
                        StopFunction(
                            "Failure",
                            exception: ex,
                            target: instance,
                            isContinue: true,
                            category: ErrorCategory.ConnectionError);
                        TestFunctionInterrupt();
                        continue;
                    }

                    if (server == null)
                    {
                        StopFunction(
                            String.Format("Error occurred while establishing connection to {0}", instance),
                            target: instance,
                            isContinue: true,
                            category: ErrorCategory.ConnectionError);
                        TestFunctionInterrupt();
                        continue;
                    }

                    Version serverVersion = GetServerVersion(server);
                    if (serverVersion == null)
                    {
                        StopFunction(
                            String.Format("Unable to retrieve server version from {0}", instance),
                            target: instance,
                            isContinue: true,
                            category: ErrorCategory.ConnectionError);
                        TestFunctionInterrupt();
                        continue;
                    }

                    Dictionary<string, object> detected = ResolveBuild(serverVersion, null, null, null, null);
                    string domainInstanceName = GetServerProperty(server, "DomainInstanceName");

                    PSObject result = BuildOutputObject(
                        domainInstanceName,
                        serverVersion,
                        detected);
                    WriteObject(result);
                }
            }

            // Build loop
            if (Build != null)
            {
                foreach (Version buildStr in Build)
                {
                    Dictionary<string, object> detected = ResolveBuild(buildStr, null, null, null, null);

                    PSObject result = BuildOutputObject(
                        null,
                        buildStr,
                        detected);
                    SystemHelpers.SelectDefaultView(result, NonInstanceDisplayProperties);
                    WriteObject(result);
                }
            }

            // KB loop
            if (Kb != null)
            {
                foreach (string kbItem in Kb)
                {
                    Dictionary<string, object> detected = ResolveBuild(null, kbItem, null, null, null);

                    object detectedBuild = null;
                    if (detected.ContainsKey("Build"))
                        detectedBuild = detected["Build"];

                    PSObject result = BuildOutputObject(
                        null,
                        detectedBuild,
                        detected);
                    SystemHelpers.SelectDefaultView(result, NonInstanceDisplayProperties);
                    WriteObject(result);
                }
            }

            // MajorVersion lookup
            if (_majorVersion != null)
            {
                Dictionary<string, object> detected = ResolveBuild(null, null, _majorVersion, _servicePack, _cumulativeUpdate);

                object detectedBuild = null;
                if (detected.ContainsKey("Build"))
                    detectedBuild = detected["Build"];

                PSObject result = BuildOutputObject(
                    null,
                    detectedBuild,
                    detected);
                SystemHelpers.SelectDefaultView(result, NonInstanceDisplayProperties);
                WriteObject(result);
            }
        }

        #endregion Cmdlet Lifecycle

        #region Build Reference Index

        /// <summary>
        /// Loads the build reference index from disk, updating from module source if needed.
        /// Calls Update-DbaBuildReference if -Update was specified.
        /// </summary>
        private PSObject[] LoadBuildReferenceIndex(bool update)
        {
            string script = @"
param($update, $enableException)
$moduledirectory = (Get-Module dbatools | Where-Object ModuleBase -notmatch 'net[48]').ModuleBase
if (-not $moduledirectory) { $moduledirectory = (Get-Module dbatools).ModuleBase }
$orig_idxfile = Join-Path $moduledirectory 'bin\dbatools-buildref-index.json'
$DbatoolsData = Get-DbatoolsConfigValue -Name 'Path.DbatoolsData'
$writable_idxfile = Join-Path $DbatoolsData 'dbatools-buildref-index.json'

if (-not (Test-Path $orig_idxfile)) {
    Write-Warning 'Unable to read local SQL build reference file. Please check your module integrity or reinstall dbatools.'
}

if ((-not (Test-Path $orig_idxfile)) -and (-not (Test-Path $writable_idxfile))) {
    throw 'Build reference file not found, please check module health.'
}

if (-not (Test-Path $writable_idxfile)) {
    Copy-Item -Path $orig_idxfile -Destination $writable_idxfile -Force -ErrorAction Stop
    $result = Get-Content $orig_idxfile -Raw | ConvertFrom-Json
} elseif (Test-Path $orig_idxfile) {
    $module_content = Get-Content $orig_idxfile -Raw | ConvertFrom-Json
    $data_content = Get-Content $writable_idxfile -Raw | ConvertFrom-Json
    $module_time = Get-Date $module_content.LastUpdated
    $data_time = Get-Date $data_content.LastUpdated
    if ($module_time -gt $data_time) {
        Copy-Item -Path $orig_idxfile -Destination $writable_idxfile -Force -ErrorAction Stop
        $result = $module_content
    } else {
        $result = $data_content
    }
    if ($update) {
        Update-DbaBuildReference -EnableException -ErrorAction Stop
    }
} else {
    $result = Get-Content $writable_idxfile -Raw | ConvertFrom-Json
}

$LastUpdated = Get-Date -Date $result.LastUpdated
if ($LastUpdated -lt (Get-Date).AddDays(-45)) {
    Write-Warning ""Index is stale, last update on: $(Get-Date -Date $LastUpdated -Format s), try the -Update parameter to fetch the most up to date index""
}

$result.Data | Select-Object @{ Name = 'VersionObject'; Expression = { [version]$_.Version } }, *
";
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false,
                ScriptBlock.Create(script),
                null,
                update,
                EnableException.ToBool());

            if (results == null || results.Count == 0)
                return new PSObject[0];

            PSObject[] data = new PSObject[results.Count];
            for (int i = 0; i < results.Count; i++)
            {
                data[i] = results[i];
            }
            return data;
        }

        #endregion Build Reference Index

        #region Resolve Build

        /// <summary>
        /// Resolves build information from the index data based on the provided lookup criteria.
        /// Exactly one of build, kb, or majorVersion should be non-null (or none for Update-only).
        /// </summary>
        internal Dictionary<string, object> ResolveBuild(
            Version build,
            string kb,
            string majorVersion,
            string servicePack,
            string cumulativeUpdate)
        {
            PSObject[] idxVersion = null;
            string currentKb = null;

            if (build != null)
            {
                if (build.Minor != 0 && build.Minor != 50)
                {
                    WriteMessageAtLevel("Normalized Minor Version to account version aliases", MessageLevel.Debug, null);
                    int normalizedMinor = build.Minor - (build.Minor % 10);
                    build = new Version(build.Major, normalizedMinor, build.Build);
                }
                WriteMessageAtLevel(String.Format("Looking for {0}", build), MessageLevel.Verbose, null);

                idxVersion = FilterByMajorMinor(build.Major, build.Minor);
            }
            else if (kb != null)
            {
                WriteMessageAtLevel(String.Format("Looking for KB {0}", kb), MessageLevel.Verbose, null);
                currentKb = ParseKbNumber(kb);
                if (currentKb == null)
                {
                    StopFunction(String.Format("Wrong KB name {0}", kb));
                    return new Dictionary<string, object>();
                }

                PSObject kbVersion = FindByKb(currentKb);
                if (kbVersion != null)
                {
                    Version kbVerObj = GetPSObjectProperty<Version>(kbVersion, "VersionObject");
                    if (kbVerObj != null)
                        idxVersion = FilterByMajorMinor(kbVerObj.Major, kbVerObj.Minor);
                }
            }
            else if (majorVersion != null)
            {
                WriteMessageAtLevel(
                    String.Format("Looking for SQL {0} SP {1} CU {2}", majorVersion, servicePack, cumulativeUpdate),
                    MessageLevel.Verbose, null);

                PSObject nameVersion = FindByName(majorVersion);
                if (nameVersion != null)
                {
                    Version nameVerObj = GetPSObjectProperty<Version>(nameVersion, "VersionObject");
                    if (nameVerObj != null)
                        idxVersion = FilterByMajorMinor(nameVerObj.Major, nameVerObj.Minor);
                }
            }

            Dictionary<string, object> detected = new Dictionary<string, object>();
            detected["MatchType"] = "Approximate";

            int idxCount = idxVersion != null ? idxVersion.Length : 0;
            WriteMessageAtLevel(String.Format("We have {0} builds in store for this Release", idxCount), MessageLevel.Verbose, null);

            if (idxCount == 0)
            {
                WriteMessageAtLevel("No info in store for this Release", MessageLevel.Warning, null);
                detected["Warning"] = "No info in store for this Release";
                return detected;
            }

            PSObject lastVer = idxVersion[0];

            foreach (PSObject el in idxVersion)
            {
                string elName = GetPSObjectProperty<string>(el, "Name");
                if (elName != null)
                    detected["Name"] = elName;

                Version elVersionObj = GetPSObjectProperty<Version>(el, "VersionObject");

                if (build != null && elVersionObj != null && elVersionObj > build)
                {
                    detected["MatchType"] = "Approximate";
                    Version lastVersion = GetPSObjectProperty<Version>(lastVer, "VersionObject");
                    detected["Warning"] = String.Format("{0} not found, closest build we have is {1}",
                        build, lastVersion != null ? lastVersion.ToString() : "unknown");
                    break;
                }

                lastVer = el;
                detected["BuildLevel"] = elVersionObj;

                object elSP = GetPSObjectPropertyRaw(el, "SP");
                if (elSP != null)
                {
                    detected["SP"] = elSP;
                    detected["CU"] = null;
                }

                object elCU = GetPSObjectPropertyRaw(el, "CU");
                if (elCU != null)
                    detected["CU"] = elCU;

                object elSupportedUntil = GetPSObjectPropertyRaw(el, "SupportedUntil");
                if (elSupportedUntil != null)
                {
                    detected["SupportedUntil"] = ParseDateTime(elSupportedUntil);
                }

                string elVersion = GetPSObjectProperty<string>(el, "Version");
                detected["Build"] = elVersion;

                object elKBList = GetPSObjectPropertyRaw(el, "KBList");
                detected["KB"] = elKBList;

                bool elRetired = false;
                object elRetiredRaw = GetPSObjectPropertyRaw(el, "Retired");
                if (elRetiredRaw != null)
                {
                    try { elRetired = Convert.ToBoolean(elRetiredRaw); }
                    catch { /* ignore */ }
                }

                // Check for exact match by build
                if (build != null && elVersionObj != null && elVersionObj == build)
                {
                    detected["MatchType"] = "Exact";
                    if (elRetired)
                        detected["Warning"] = "This version has been officially retired by Microsoft";
                    break;
                }

                // Check for exact match by KB
                if (kb != null && currentKb != null && KBListContains(elKBList, currentKb))
                {
                    detected["MatchType"] = "Exact";
                    if (elRetired)
                        detected["Warning"] = "This version has been officially retired by Microsoft";
                    break;
                }

                // Check for exact match by MajorVersion/SP/CU
                if (majorVersion != null)
                {
                    object detectedSP = null;
                    if (detected.ContainsKey("SP"))
                        detectedSP = detected["SP"];

                    bool spMatches = SPContains(detectedSP, servicePack);
                    bool cuMatches = String.IsNullOrEmpty(cumulativeUpdate) ||
                        (elCU != null && String.Equals(elCU.ToString(), cumulativeUpdate, StringComparison.OrdinalIgnoreCase));

                    if (spMatches && cuMatches)
                    {
                        detected["MatchType"] = "Exact";
                        if (elRetired)
                            detected["Warning"] = "This version has been officially retired by Microsoft";
                        break;
                    }
                }
            }

            return detected;
        }

        #endregion Resolve Build

        #region Index Helpers

        /// <summary>
        /// Filters the index data to entries matching the given major.minor version prefix.
        /// </summary>
        private PSObject[] FilterByMajorMinor(int major, int minor)
        {
            string prefix = String.Format("{0}.{1}.", major, minor);
            List<PSObject> results = new List<PSObject>();
            foreach (PSObject item in _indexData)
            {
                string version = GetPSObjectProperty<string>(item, "Version");
                if (version != null && version.StartsWith(prefix, StringComparison.Ordinal))
                    results.Add(item);
            }
            return results.ToArray();
        }

        /// <summary>
        /// Finds the first index entry whose KBList contains the given KB number.
        /// </summary>
        private PSObject FindByKb(string kbNumber)
        {
            foreach (PSObject item in _indexData)
            {
                object kbList = GetPSObjectPropertyRaw(item, "KBList");
                if (KBListContains(kbList, kbNumber))
                    return item;
            }
            return null;
        }

        /// <summary>
        /// Finds the first index entry with the given Name property.
        /// </summary>
        private PSObject FindByName(string name)
        {
            foreach (PSObject item in _indexData)
            {
                string itemName = GetPSObjectProperty<string>(item, "Name");
                if (itemName != null && String.Equals(itemName, name, StringComparison.OrdinalIgnoreCase))
                    return item;
            }
            return null;
        }

        /// <summary>
        /// Checks whether a KBList property (string or array) contains the given KB number.
        /// </summary>
        internal static bool KBListContains(object kbList, string kbNumber)
        {
            if (kbList == null || kbNumber == null)
                return false;

            // Could be a single string or an array
            if (kbList is object[] arr)
            {
                foreach (object item in arr)
                {
                    if (item != null && String.Equals(item.ToString(), kbNumber, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            }

            if (kbList is PSObject psObj && psObj.BaseObject is object[] psArr)
            {
                foreach (object item in psArr)
                {
                    if (item != null && String.Equals(item.ToString(), kbNumber, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            }

            return String.Equals(kbList.ToString(), kbNumber, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Checks if SP property (which may be a string or array) contains the target SP level.
        /// </summary>
        internal static bool SPContains(object spValue, string targetSP)
        {
            if (spValue == null || targetSP == null)
                return false;

            if (spValue is object[] arr)
            {
                foreach (object item in arr)
                {
                    if (item != null && String.Equals(item.ToString(), targetSP, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            }

            if (spValue is PSObject psObj && psObj.BaseObject is object[] psArr)
            {
                foreach (object item in psArr)
                {
                    if (item != null && String.Equals(item.ToString(), targetSP, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            }

            return String.Equals(spValue.ToString(), targetSP, StringComparison.OrdinalIgnoreCase);
        }

        #endregion Index Helpers

        #region Validation Helpers

        /// <summary>
        /// Normalizes the major version string. Returns null if format is invalid.
        /// Accepts "SQL2016", "2016", "2008R2", etc.
        /// </summary>
        internal static string NormalizeMajorVersion(string input)
        {
            if (String.IsNullOrEmpty(input))
                return null;

            // Match optional "SQL" prefix followed by 4-digit year optionally followed by "R2"
            string cleaned = input.Trim();
            if (cleaned.StartsWith("SQL", StringComparison.OrdinalIgnoreCase))
                cleaned = cleaned.Substring(3);

            // Must be 4 digits optionally followed by R2
            if (cleaned.Length == 4 || (cleaned.Length == 6 && cleaned.EndsWith("R2", StringComparison.OrdinalIgnoreCase)))
            {
                string yearPart = cleaned.Length == 6 ? cleaned.Substring(0, 4) : cleaned;
                int year;
                if (Int32.TryParse(yearPart, out year) && year >= 2000 && year <= 2099)
                {
                    // Normalize R2 suffix to uppercase
                    if (cleaned.Length == 6)
                        return yearPart + "R2";
                    return cleaned;
                }
            }

            return null;
        }

        /// <summary>
        /// Normalizes the service pack string. Returns null if format is invalid.
        /// Accepts "SP1", "1", "0", "RTM".
        /// </summary>
        internal static string NormalizeServicePack(string input)
        {
            if (String.IsNullOrEmpty(input))
                return "RTM";

            string cleaned = input.Trim();

            // Already "RTM"
            if (String.Equals(cleaned, "RTM", StringComparison.OrdinalIgnoreCase))
                return "RTM";

            // "SP" prefix or just a number
            string numberPart = null;
            if (cleaned.StartsWith("SP", StringComparison.OrdinalIgnoreCase))
                numberPart = cleaned.Substring(2).Trim();
            else
                numberPart = cleaned;

            int spNumber;
            if (Int32.TryParse(numberPart, out spNumber) && spNumber >= 0)
            {
                if (spNumber == 0)
                    return "RTM";
                return String.Format("SP{0}", spNumber);
            }

            return null;
        }

        /// <summary>
        /// Normalizes the cumulative update string. Returns null if format is invalid.
        /// Accepts "CU5", "5", "CU0", "0".
        /// </summary>
        internal static string NormalizeCumulativeUpdate(string input)
        {
            if (String.IsNullOrEmpty(input))
                return "";

            string cleaned = input.Trim();

            string numberPart = null;
            if (cleaned.StartsWith("CU", StringComparison.OrdinalIgnoreCase))
                numberPart = cleaned.Substring(2).Trim();
            else
                numberPart = cleaned;

            int cuNumber;
            if (Int32.TryParse(numberPart, out cuNumber) && cuNumber >= 0)
            {
                if (cuNumber == 0)
                    return "";
                return String.Format("CU{0}", cuNumber);
            }

            return null;
        }

        /// <summary>
        /// Parses a KB string, stripping the optional "KB" prefix. Returns null if format is invalid.
        /// </summary>
        internal static string ParseKbNumber(string input)
        {
            if (String.IsNullOrEmpty(input))
                return null;

            string cleaned = input.Trim();
            if (cleaned.StartsWith("KB", StringComparison.OrdinalIgnoreCase))
                cleaned = cleaned.Substring(2);

            int dummy;
            if (Int32.TryParse(cleaned, out dummy))
                return cleaned;

            return null;
        }

        #endregion Validation Helpers

        #region Output Helpers

        /// <summary>
        /// Builds the output PSObject with all standard properties.
        /// </summary>
        private static PSObject BuildOutputObject(
            string sqlInstance,
            object build,
            Dictionary<string, object> detected)
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("SqlInstance", sqlInstance));
            obj.Properties.Add(new PSNoteProperty("Build", build));
            obj.Properties.Add(new PSNoteProperty("NameLevel", GetDictValue(detected, "Name")));
            obj.Properties.Add(new PSNoteProperty("SPLevel", GetDictValue(detected, "SP")));
            obj.Properties.Add(new PSNoteProperty("CULevel", GetDictValue(detected, "CU")));
            obj.Properties.Add(new PSNoteProperty("KBLevel", GetDictValue(detected, "KB")));
            obj.Properties.Add(new PSNoteProperty("BuildLevel", GetDictValue(detected, "BuildLevel")));
            obj.Properties.Add(new PSNoteProperty("SupportedUntil", GetDictValue(detected, "SupportedUntil")));
            obj.Properties.Add(new PSNoteProperty("MatchType", GetDictValue(detected, "MatchType")));
            obj.Properties.Add(new PSNoteProperty("Warning", GetDictValue(detected, "Warning")));
            return obj;
        }

        /// <summary>
        /// Safely gets a value from a dictionary, returning null if the key doesn't exist.
        /// </summary>
        private static object GetDictValue(Dictionary<string, object> dict, string key)
        {
            object value;
            if (dict != null && dict.TryGetValue(key, out value))
                return value;
            return null;
        }

        #endregion Output Helpers

        #region Connection Helpers

        /// <summary>
        /// Connects to a SQL Server instance via Connect-DbaInstance.
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

            Collection<PSObject> results = InvokeCommand.InvokeScript(false, ScriptBlock.Create(script), null, args);
            if (results != null && results.Count > 0)
                return results[0].BaseObject;
            return null;
        }

        /// <summary>
        /// Gets the Version from a server object.
        /// </summary>
        private Version GetServerVersion(object server)
        {
            try
            {
                PSObject pso = PSObject.AsPSObject(server);
                PSPropertyInfo prop = pso.Properties["Version"];
                if (prop != null && prop.Value != null)
                {
                    if (prop.Value is Version v)
                        return v;
                    return new Version(prop.Value.ToString());
                }
            }
            catch (Exception)
            {
                // Property access may fail
            }
            return null;
        }

        /// <summary>
        /// Gets a string property from a server object.
        /// </summary>
        private static string GetServerProperty(object server, string propertyName)
        {
            if (server == null)
                return null;
            try
            {
                PSObject pso = PSObject.AsPSObject(server);
                PSPropertyInfo prop = pso.Properties[propertyName];
                if (prop != null && prop.Value != null)
                    return prop.Value.ToString();
            }
            catch (Exception)
            {
                // Property may not exist
            }
            return null;
        }

        #endregion Connection Helpers

        #region PSObject Helpers

        /// <summary>
        /// Gets a typed property value from a PSObject.
        /// </summary>
        internal static T GetPSObjectProperty<T>(PSObject obj, string propertyName) where T : class
        {
            if (obj == null)
                return null;
            try
            {
                PSPropertyInfo prop = obj.Properties[propertyName];
                if (prop != null && prop.Value != null)
                {
                    if (prop.Value is T typedValue)
                        return typedValue;
                    if (prop.Value is PSObject psWrapped && psWrapped.BaseObject is T baseValue)
                        return baseValue;
                }
            }
            catch (Exception)
            {
                // Property may not exist
            }
            return null;
        }

        /// <summary>
        /// Gets the raw property value from a PSObject (may be wrapped in PSObject).
        /// </summary>
        internal static object GetPSObjectPropertyRaw(PSObject obj, string propertyName)
        {
            if (obj == null)
                return null;
            try
            {
                PSPropertyInfo prop = obj.Properties[propertyName];
                if (prop != null && prop.Value != null)
                {
                    if (prop.Value is PSObject psWrapped)
                        return psWrapped.BaseObject;
                    return prop.Value;
                }
            }
            catch (Exception)
            {
                // Property may not exist
            }
            return null;
        }

        /// <summary>
        /// Parses a datetime value from various formats.
        /// </summary>
        private static DateTime ParseDateTime(object value)
        {
            if (value is DateTime dt)
                return dt;
            if (value is DateTimeOffset dto)
                return dto.DateTime;
            DateTime parsed;
            if (DateTime.TryParse(value.ToString(), out parsed))
                return parsed;
            return DateTime.MinValue;
        }

        #endregion PSObject Helpers
    }
}
