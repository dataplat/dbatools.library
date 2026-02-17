using Dataplat.Dbatools.Configuration;
using Dataplat.Dbatools.Internal;
using Dataplat.Dbatools.Message;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Imports dbatools configuration settings from JSON files, URLs, raw JSON strings, or default module paths.
    /// </summary>
    [Cmdlet("Import", "DbatoolsConfig", DefaultParameterSetName = "Path")]
    public class ImportDbatoolsConfigCommand : DbaBaseCmdlet
    {
        /// <summary>
        /// Specifies the path to JSON configuration files, web URLs, or raw JSON strings to import settings from.
        /// </summary>
        [Parameter(Mandatory = true, ValueFromPipeline = true, ParameterSetName = "Path")]
        public string[] Path { get; set; }

        /// <summary>
        /// Specifies which dbatools module's configuration settings to import from default system locations.
        /// </summary>
        [Parameter(ParameterSetName = "ModuleName", Mandatory = true)]
        public string ModuleName { get; set; }

        /// <summary>
        /// Specifies which version of the module configuration schema to load when importing persisted settings.
        /// </summary>
        [Parameter(ParameterSetName = "ModuleName")]
        public int ModuleVersion { get; set; } = 1;

        /// <summary>
        /// Controls which configuration storage locations to search when importing module settings.
        /// </summary>
        [Parameter(ParameterSetName = "ModuleName")]
        public ConfigScope Scope { get; set; } = ConfigScope.FileUserLocal | ConfigScope.FileUserShared | ConfigScope.FileSystem;

        /// <summary>
        /// Specifies wildcard patterns to selectively import only matching configuration items from the source.
        /// </summary>
        [Parameter(ParameterSetName = "Path")]
        public string[] IncludeFilter { get; set; }

        /// <summary>
        /// Specifies wildcard patterns to exclude specific configuration items during import.
        /// </summary>
        [Parameter(ParameterSetName = "Path")]
        public string[] ExcludeFilter { get; set; }

        /// <summary>
        /// Returns the configuration items that would be imported without actually applying them.
        /// </summary>
        [Parameter(ParameterSetName = "Path")]
        public SwitchParameter Peek { get; set; }

        private static readonly string[] FailImportTags = new string[] { "fail", "import" };

        private WildcardPattern[] _excludePatterns;
        private WildcardPattern[] _includePatterns;

        /// <summary>
        /// Validates parameters, pre-compiles wildcard patterns, and logs bound parameter info.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            WriteMessageAtLevel(
                String.Format("Bound parameters: {0}", String.Join(", ", MyInvocation.BoundParameters.Keys)),
                MessageLevel.InternalComment,
                new string[] { "debug", "start", "param" });

            _excludePatterns = BuildPatterns(ExcludeFilter);
            _includePatterns = BuildPatterns(IncludeFilter);
        }

        /// <summary>
        /// Processes each Path input from the pipeline or handles ModuleName-based import.
        /// </summary>
        protected override void ProcessRecord()
        {
            // Handle explicit Path parameter set
            if (Path != null)
            {
                foreach (string item in Path)
                {
                    List<Hashtable> data = null;

                    try
                    {
                        if (item != null && item.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        {
                            data = ConfigurationHelpers.ReadConfigFromUrl(this, item);
                        }
                        else
                        {
                            string resolvedPath = ResolveSinglePath(item);
                            if (resolvedPath != null)
                            {
                                data = ConfigurationHelpers.ReadConfigFile(this, resolvedPath);
                            }
                            else
                            {
                                data = ConfigurationHelpers.ReadConfigFromJson(this, item);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        StopFunction(
                            String.Format("Failed to import {0}", item),
                            errorRecord: new ErrorRecord(ex, "ImportDbatoolsConfig_ImportFailed", ErrorCategory.InvalidData, item),
                            target: item,
                            isContinue: true,
                            tag: FailImportTags);
                        TestFunctionInterrupt();
                        continue;
                    }

                    if (data == null)
                        continue;

                    foreach (Hashtable element in data)
                    {
                        string fullName = element["FullName"] as string;
                        if (String.IsNullOrEmpty(fullName))
                            continue;

                        // Exclude filter runs first (matches PS1 labeled-continue :element pattern)
                        if (IsExcluded(fullName))
                            continue;

                        // Include filter
                        if (!IsIncluded(fullName))
                            continue;

                        if (Peek.IsPresent)
                        {
                            WriteObject(CreatePeekObject(element));
                        }
                        else
                        {
                            ApplyConfigElement(element, item);
                        }
                    }
                }
            }

            // Handle ModuleName parameter set
            // PS1 has no try/catch here - failures propagate as terminating errors
            if (!String.IsNullOrEmpty(ModuleName))
            {
                Hashtable data = ReadPersistedConfig(ModuleName, Scope, ModuleVersion);

                foreach (Hashtable value in data.Values)
                {
                    string fullName = value["FullName"] as string;
                    if (String.IsNullOrEmpty(fullName))
                        continue;

                    object configValue = value.ContainsKey("Value") ? value["Value"] : null;
                    bool keepPersisted = GetKeepPersisted(value);

                    if (!keepPersisted)
                    {
                        InvokeSetDbatoolsConfig(fullName, configValue, EnableException.ToBool());
                    }
                    else
                    {
                        // For KeepPersisted items, convert the persisted value first
                        string persistedValue = configValue as string;
                        object typeObj = value.ContainsKey("Type") ? value["Type"] : null;
                        ConfigurationValueType valueType = ConfigurationValueType.Unknown;
                        if (typeObj != null)
                        {
                            try { valueType = (ConfigurationValueType)Convert.ToInt32(typeObj); }
                            catch { }
                        }

                        object convertedValue = ConfigurationHost.ConvertFromPersistedValue(persistedValue, valueType);
                        InvokeSetDbatoolsConfig(fullName, convertedValue, EnableException.ToBool());
                    }
                }
            }
        }

        #region Helper Methods

        /// <summary>
        /// Builds pre-compiled WildcardPattern array from filter strings.
        /// </summary>
        private static WildcardPattern[] BuildPatterns(string[] filters)
        {
            if (filters == null || filters.Length == 0)
                return new WildcardPattern[0];

            WildcardPattern[] patterns = new WildcardPattern[filters.Length];
            for (int i = 0; i < filters.Length; i++)
            {
                patterns[i] = new WildcardPattern(filters[i], WildcardOptions.IgnoreCase);
            }
            return patterns;
        }

        /// <summary>
        /// Resolves a single file system path using Resolve-DbaPath.
        /// Returns null if the path cannot be resolved.
        /// </summary>
        internal string ResolveSinglePath(string path)
        {
            if (String.IsNullOrEmpty(path))
                return null;

            try
            {
                string script = @"
param($p)
try { Resolve-DbaPath -Path $p -SingleItem -Provider FileSystem } catch { $null }
";
                var results = InvokeCommand.InvokeScript(
                    false,
                    ScriptBlock.Create(script),
                    null,
                    path
                );

                if (results != null && results.Count > 0 && results[0] != null)
                {
                    object baseObj = results[0].BaseObject;
                    if (baseObj != null)
                        return baseObj.ToString();
                }
            }
            catch
            {
                // Path resolution failed - will fall back to raw JSON parsing
            }
            return null;
        }

        /// <summary>
        /// Tests whether a config element FullName matches any exclusion filter.
        /// Uses pre-compiled patterns from BeginProcessing.
        /// </summary>
        internal bool IsExcluded(string fullName)
        {
            if (_excludePatterns == null || _excludePatterns.Length == 0)
                return false;

            foreach (WildcardPattern pattern in _excludePatterns)
            {
                if (pattern.IsMatch(fullName))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Tests whether a config element FullName matches any inclusion filter.
        /// Returns true if no include filter is specified.
        /// Uses pre-compiled patterns from BeginProcessing.
        /// </summary>
        internal bool IsIncluded(string fullName)
        {
            if (_includePatterns == null || _includePatterns.Length == 0)
                return true;

            foreach (WildcardPattern pattern in _includePatterns)
            {
                if (pattern.IsMatch(fullName))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Creates a PSObject for Peek output matching the PS1 behavior.
        /// </summary>
        internal static PSObject CreatePeekObject(Hashtable element)
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("FullName", element.ContainsKey("FullName") ? element["FullName"] : null));
            obj.Properties.Add(new PSNoteProperty("Value", element.ContainsKey("Value") ? element["Value"] : null));

            object typeVal = null;
            if (element.ContainsKey("Type"))
                typeVal = element["Type"];
            obj.Properties.Add(new PSNoteProperty("Type", typeVal));

            obj.Properties.Add(new PSNoteProperty("KeepPersisted", GetKeepPersisted(element)));
            obj.Properties.Add(new PSNoteProperty("Enforced", GetBoolProperty(element, "Enforced")));
            obj.Properties.Add(new PSNoteProperty("Policy", GetBoolProperty(element, "Policy")));

            return obj;
        }

        /// <summary>
        /// Safely extracts the KeepPersisted boolean from a config element hashtable.
        /// </summary>
        internal static bool GetKeepPersisted(Hashtable element)
        {
            return GetBoolProperty(element, "KeepPersisted");
        }

        /// <summary>
        /// Safely extracts a boolean property from a config element hashtable.
        /// Returns false if the key is missing, null, or cannot be converted.
        /// </summary>
        internal static bool GetBoolProperty(Hashtable element, string key)
        {
            if (element == null || !element.ContainsKey(key))
                return false;

            object val = element[key];
            if (val is bool)
                return (bool)val;
            try { return Convert.ToBoolean(val); }
            catch { return false; }
        }

        /// <summary>
        /// Applies a single configuration element by calling Set-DbatoolsConfig.
        /// </summary>
        private void ApplyConfigElement(Hashtable element, string sourceItem)
        {
            string fullName = element["FullName"] as string;
            object value = element.ContainsKey("Value") ? element["Value"] : null;
            bool keepPersisted = GetKeepPersisted(element);

            try
            {
                if (!keepPersisted)
                {
                    // PS1 passes bare -EnableException (always $true) so the outer catch can handle it
                    InvokeSetDbatoolsConfig(fullName, value, true);
                }
                else
                {
                    string persistedValue = value as string;
                    object typeObj = element.ContainsKey("Type") ? element["Type"] : null;
                    string persistedType = null;
                    if (typeObj != null)
                        persistedType = typeObj.ToString();

                    InvokeSetDbatoolsConfigPersisted(fullName, persistedValue, persistedType);
                }
            }
            catch (Exception ex)
            {
                StopFunction(
                    String.Format("Failed to set '{0}'", fullName),
                    errorRecord: new ErrorRecord(ex, "ImportDbatoolsConfig_SetFailed", ErrorCategory.InvalidOperation, sourceItem),
                    target: sourceItem,
                    isContinue: true,
                    tag: FailImportTags);
                TestFunctionInterrupt();
            }
        }

        /// <summary>
        /// Invokes Set-DbatoolsConfig -FullName -Value -EnableException
        /// </summary>
        private void InvokeSetDbatoolsConfig(string fullName, object value, bool enableException)
        {
            string script = @"
param($fn, $val, $ee)
Set-DbatoolsConfig -FullName $fn -Value $val -EnableException:$ee
";
            InvokeCommand.InvokeScript(
                false,
                ScriptBlock.Create(script),
                null,
                fullName, value, enableException
            );
        }

        /// <summary>
        /// Invokes Set-DbatoolsConfig -FullName -PersistedValue -PersistedType
        /// </summary>
        private void InvokeSetDbatoolsConfigPersisted(string fullName, string persistedValue, string persistedType)
        {
            string script = @"
param($fn, $pv, $pt)
Set-DbatoolsConfig -FullName $fn -PersistedValue $pv -PersistedType $pt
";
            InvokeCommand.InvokeScript(
                false,
                ScriptBlock.Create(script),
                null,
                fullName, persistedValue, persistedType
            );
        }

        /// <summary>
        /// Reads persisted configuration from file system scopes, mirroring Read-DbatoolsConfigPersisted.
        /// Only supports file-based scopes (FileSystem, FileUserShared, FileUserLocal) since
        /// Import-DbatoolsConfig only passes file scopes by default.
        /// </summary>
        internal Hashtable ReadPersistedConfig(string moduleName, ConfigScope scope, int moduleVersion)
        {
            // Warn if registry-based scopes are requested (not supported in C# implementation)
            int registryScopeMask = (int)(ConfigScope.UserDefault | ConfigScope.UserMandatory |
                                         ConfigScope.SystemDefault | ConfigScope.SystemMandatory);
            if (((int)scope & registryScopeMask) != 0)
            {
                WriteMessageAtLevel(
                    "Registry-based config scopes are not supported in this implementation. Only file-based scopes (FileUserLocal, FileUserShared, FileSystem) will be read.",
                    MessageLevel.Warning, null);
            }

            Hashtable results = new Hashtable(StringComparer.OrdinalIgnoreCase);
            string fileName = String.Format("{0}-{1}.json", moduleName.ToLowerInvariant(), moduleVersion);

            // FileSystem (computer-wide) - lowest priority
            if ((scope & ConfigScope.FileSystem) != 0)
            {
                string scopePath = ExportDbatoolsConfigCommand.GetFileSystemPath();
                ReadScopeFile(results, scopePath, fileName);
            }

            // FileUserShared - medium priority
            if ((scope & ConfigScope.FileUserShared) != 0)
            {
                string scopePath = ExportDbatoolsConfigCommand.GetFileUserSharedPath();
                ReadScopeFile(results, scopePath, fileName);
            }

            // FileUserLocal - highest priority
            if ((scope & ConfigScope.FileUserLocal) != 0)
            {
                string scopePath = ExportDbatoolsConfigCommand.GetFileUserLocalPath();
                ReadScopeFile(results, scopePath, fileName);
            }

            return results;
        }

        /// <summary>
        /// Reads config items from a specific scope file and merges into results.
        /// Later calls overwrite earlier ones (higher priority wins).
        /// </summary>
        private void ReadScopeFile(Hashtable results, string scopePath, string fileName)
        {
            if (String.IsNullOrEmpty(scopePath))
                return;

            string filePath = System.IO.Path.Combine(scopePath, fileName);
            List<Hashtable> items = ConfigurationHelpers.ReadConfigFile(this, filePath);

            foreach (Hashtable item in items)
            {
                string fullName = item["FullName"] as string;
                if (!String.IsNullOrEmpty(fullName))
                {
                    results[fullName] = item;
                }
            }
        }

        #endregion Helper Methods
    }
}
