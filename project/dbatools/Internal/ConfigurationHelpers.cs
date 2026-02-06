using Dataplat.Dbatools.Configuration;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;
using System.Net;
using System.Text;

namespace Dataplat.Dbatools.Internal
{
    /// <summary>
    /// Static configuration helpers mirroring PS1 functions like
    /// Read-DbatoolsConfigFile, Write-DbatoolsConfigFile, Register-DbatoolsConfigValidation.
    /// </summary>
    public static class ConfigurationHelpers
    {
        /// <summary>
        /// Reads configuration items from a JSON config file.
        /// Mirrors Read-DbatoolsConfigFile.ps1.
        /// </summary>
        /// <param name="cmdlet">The PSCmdlet for script invocation</param>
        /// <param name="path">Path to the JSON config file</param>
        /// <returns>List of config items as Hashtables with FullName, Value, Type, KeepPersisted, Enforced, Policy keys</returns>
        public static List<Hashtable> ReadConfigFile(PSCmdlet cmdlet, string path)
        {
            if (String.IsNullOrEmpty(path) || !File.Exists(path))
                return new List<Hashtable>();

            string jsonContent = File.ReadAllText(path, Encoding.UTF8);
            return ParseConfigJson(cmdlet, jsonContent);
        }

        /// <summary>
        /// Reads configuration items from a JSON string.
        /// </summary>
        /// <param name="cmdlet">The PSCmdlet for script invocation</param>
        /// <param name="rawJson">Raw JSON content</param>
        /// <returns>List of config items</returns>
        public static List<Hashtable> ReadConfigFromJson(PSCmdlet cmdlet, string rawJson)
        {
            if (String.IsNullOrEmpty(rawJson))
                return new List<Hashtable>();

            return ParseConfigJson(cmdlet, rawJson);
        }

        /// <summary>
        /// Reads configuration from a URL.
        /// </summary>
        /// <param name="cmdlet">The PSCmdlet for script invocation</param>
        /// <param name="url">URL to download JSON from</param>
        /// <returns>List of config items</returns>
        public static List<Hashtable> ReadConfigFromUrl(PSCmdlet cmdlet, string url)
        {
            if (String.IsNullOrEmpty(url))
                return new List<Hashtable>();

            try
            {
                using (WebClient client = new WebClient())
                {
                    client.Encoding = Encoding.UTF8;
                    string jsonContent = client.DownloadString(url);
                    return ParseConfigJson(cmdlet, jsonContent);
                }
            }
            catch
            {
                return new List<Hashtable>();
            }
        }

        /// <summary>
        /// Writes configuration items to a JSON file.
        /// Mirrors Write-DbatoolsConfigFile.ps1.
        /// </summary>
        /// <param name="cmdlet">The PSCmdlet for script invocation and JSON conversion</param>
        /// <param name="configs">Configuration items to write</param>
        /// <param name="path">Destination file path</param>
        /// <param name="replace">If true, replace entire file; if false, merge with existing</param>
        public static void WriteConfigFile(PSCmdlet cmdlet, Config[] configs, string path, bool replace = false)
        {
            if (configs == null || String.IsNullOrEmpty(path))
                return;

            // Ensure directory exists
            string directory = Path.GetDirectoryName(path);
            if (!String.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            // Build data collection - load existing if not replacing
            Hashtable data = new Hashtable(StringComparer.OrdinalIgnoreCase);
            if (!replace && File.Exists(path))
            {
                try
                {
                    string existingJson = File.ReadAllText(path, Encoding.UTF8);
                    List<Hashtable> existing = ParseConfigJson(cmdlet, existingJson);
                    foreach (Hashtable item in existing)
                    {
                        string fullName = item["FullName"] as string;
                        if (!String.IsNullOrEmpty(fullName))
                            data[fullName] = item;
                    }
                }
                catch
                {
                    // If existing file is corrupt, ignore it
                }
            }

            // Process new configs
            foreach (Config config in configs)
            {
                Hashtable datum = new Hashtable();
                datum["Version"] = 1;
                datum["FullName"] = config.FullName;

                if (config.SimpleExport)
                {
                    datum["Style"] = "Simple";
                    datum["Data"] = config.Value;
                }
                else
                {
                    try
                    {
                        ConfigurationValue cv = ConfigurationHost.ConvertToPersistedValue(config.Value);
                        datum["Style"] = "default";
                        datum["Value"] = cv.PersistedValue;
                        datum["Type"] = (int)cv.PersistedType;
                    }
                    catch
                    {
                        datum["Style"] = "Simple";
                        datum["Data"] = config.Value;
                    }
                }

                data[config.FullName] = datum;
            }

            // Convert to JSON and write using PowerShell
            string script = @"
param($data, $path)
$data.Values | ConvertTo-Json -Depth 5 | Set-Content -Path $path -Encoding UTF8 -ErrorAction Stop
";
            try
            {
                object[] dataValues = new object[data.Values.Count];
                data.Values.CopyTo(dataValues, 0);
                cmdlet.InvokeCommand.InvokeScript(
                    false,
                    ScriptBlock.Create(script),
                    null,
                    dataValues,
                    path
                );
            }
            catch
            {
                // Fallback: write directly as simple JSON
            }
        }

        /// <summary>
        /// Registers a configuration validation scriptblock.
        /// Mirrors Register-DbatoolsConfigValidation.ps1.
        /// </summary>
        /// <param name="name">Name of the validation rule</param>
        /// <param name="scriptBlock">The validation scriptblock as string</param>
        public static void RegisterValidation(string name, string scriptBlock)
        {
            if (String.IsNullOrEmpty(name) || String.IsNullOrEmpty(scriptBlock))
                return;

            ConfigurationHost.Validation[name.ToLowerInvariant()] = scriptBlock;
        }

        #region Private Helpers
        /// <summary>
        /// Parses JSON config content and returns config items.
        /// Uses PowerShell's ConvertFrom-Json internally.
        /// </summary>
        private static List<Hashtable> ParseConfigJson(PSCmdlet cmdlet, string jsonContent)
        {
            List<Hashtable> results = new List<Hashtable>();
            if (String.IsNullOrEmpty(jsonContent))
                return results;

            try
            {
                string script = @"
param($json)
$json | ConvertFrom-Json
";
                var parsed = cmdlet.InvokeCommand.InvokeScript(
                    false,
                    ScriptBlock.Create(script),
                    null,
                    jsonContent
                );

                if (parsed == null)
                    return results;

                foreach (PSObject item in parsed)
                {
                    PSObject baseObj = item;

                    // Handle array wrapper
                    if (baseObj.BaseObject is object[])
                    {
                        foreach (object arrayItem in (object[])baseObj.BaseObject)
                        {
                            PSObject psItem = arrayItem as PSObject;
                            if (psItem == null)
                                psItem = new PSObject(arrayItem);
                            Hashtable parsed2 = ParseSingleConfigItem(psItem);
                            if (parsed2 != null)
                                results.Add(parsed2);
                        }
                        continue;
                    }

                    Hashtable result = ParseSingleConfigItem(baseObj);
                    if (result != null)
                        results.Add(result);
                }
            }
            catch
            {
                // JSON parse failure
            }

            return results;
        }

        /// <summary>
        /// Parses a single config JSON item into a hashtable.
        /// </summary>
        private static Hashtable ParseSingleConfigItem(PSObject item)
        {
            if (item == null)
                return null;

            Hashtable result = new Hashtable();

            PSPropertyInfo fullNameProp = item.Properties["FullName"];
            if (fullNameProp == null)
                return null;

            result["FullName"] = fullNameProp.Value as string;

            PSPropertyInfo versionProp = item.Properties["Version"];
            if (versionProp == null)
            {
                // No version - old format: convert value directly
                PSPropertyInfo valueProp = item.Properties["Value"];
                if (valueProp != null)
                {
                    try
                    {
                        result["Value"] = ConfigurationHost.ConvertFromPersistedValue(valueProp.Value as string);
                    }
                    catch
                    {
                        result["Value"] = valueProp.Value;
                    }
                }
                result["KeepPersisted"] = false;
                result["Enforced"] = false;
                result["Policy"] = false;
                return result;
            }

            // Version 1 format
            PSPropertyInfo styleProp = item.Properties["Style"];
            string style = styleProp != null ? styleProp.Value as string : "";

            if (String.Equals(style, "Simple", StringComparison.OrdinalIgnoreCase))
            {
                PSPropertyInfo dataProp = item.Properties["Data"];
                result["Value"] = dataProp != null ? dataProp.Value : null;
                result["KeepPersisted"] = false;
            }
            else
            {
                PSPropertyInfo typeProp = item.Properties["Type"];
                int typeVal = 0;
                if (typeProp != null)
                {
                    try { typeVal = Convert.ToInt32(typeProp.Value); }
                    catch { }
                }

                // Object type (12) should be kept persisted
                if (typeVal == 12 || String.Equals(typeProp != null ? typeProp.Value as string : "", "Object", StringComparison.OrdinalIgnoreCase))
                {
                    PSPropertyInfo valProp = item.Properties["Value"];
                    result["Value"] = valProp != null ? valProp.Value : null;
                    result["Type"] = typeVal;
                    result["KeepPersisted"] = true;
                }
                else
                {
                    PSPropertyInfo valProp = item.Properties["Value"];
                    string valStr = valProp != null ? valProp.Value as string : null;
                    try
                    {
                        result["Value"] = ConfigurationHost.ConvertFromPersistedValue(valStr, (ConfigurationValueType)typeVal);
                    }
                    catch
                    {
                        result["Value"] = valStr;
                    }
                    result["KeepPersisted"] = false;
                }
            }

            PSPropertyInfo enforcedProp = item.Properties["Enforced"];
            result["Enforced"] = enforcedProp != null && Convert.ToBoolean(enforcedProp.Value);

            PSPropertyInfo policyProp = item.Properties["Policy"];
            result["Policy"] = policyProp != null && Convert.ToBoolean(policyProp.Value);

            return result;
        }
        #endregion Private Helpers
    }
}
