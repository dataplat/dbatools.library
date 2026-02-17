using Dataplat.Dbatools.Configuration;
using Dataplat.Dbatools.Internal;
using Dataplat.Dbatools.Message;
using System;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Exports dbatools module configuration settings to a JSON file for backup or migration.
    /// </summary>
    [Cmdlet("Export", "DbatoolsConfig", DefaultParameterSetName = "FullName")]
    public class ExportDbatoolsConfigCommand : DbaBaseCmdlet
    {
        /// <summary>
        /// Specifies the complete configuration setting name to export, including the module prefix.
        /// </summary>
        [Parameter(ParameterSetName = "FullName", Mandatory = true)]
        public string FullName { get; set; }

        /// <summary>
        /// Filters configuration settings to export only those belonging to a specific dbatools module.
        /// </summary>
        [Parameter(ParameterSetName = "Module", Mandatory = true)]
        public string Module { get; set; }

        /// <summary>
        /// Specifies a pattern to match configuration setting names within the selected module, supporting wildcards.
        /// </summary>
        [Parameter(ParameterSetName = "Module", Position = 1)]
        public string Name { get; set; } = "*";

        /// <summary>
        /// Accepts configuration objects directly from Get-DbatoolsConfig for export to JSON.
        /// </summary>
        [Parameter(ParameterSetName = "Config", Mandatory = true, ValueFromPipeline = true)]
        public Config[] Config { get; set; }

        /// <summary>
        /// Exports module-specific configuration settings to predefined system locations rather than a custom path.
        /// </summary>
        [Parameter(ParameterSetName = "ModuleName", Mandatory = true)]
        public string ModuleName { get; set; }

        /// <summary>
        /// Specifies the version number to include in the exported configuration filename when using ModuleName parameter.
        /// </summary>
        [Parameter(ParameterSetName = "ModuleName")]
        public int ModuleVersion { get; set; } = 1;

        /// <summary>
        /// Determines where to save module configuration files when using ModuleName parameter.
        /// </summary>
        [Parameter(ParameterSetName = "ModuleName")]
        public ConfigScope Scope { get; set; } = ConfigScope.FileUserShared;

        /// <summary>
        /// Specifies the complete file path where the JSON configuration export will be saved.
        /// </summary>
        [Parameter(Position = 1, Mandatory = true, ParameterSetName = "Config")]
        [Parameter(Position = 1, Mandatory = true, ParameterSetName = "FullName")]
        [Parameter(Position = 2, Mandatory = true, ParameterSetName = "Module")]
        public string OutPath { get; set; }

        /// <summary>
        /// Excludes configuration settings that still have their original default values from the export.
        /// </summary>
        [Parameter()]
        public SwitchParameter SkipUnchanged { get; set; }

        private const int RegistryScopeMask = (int)(ConfigScope.UserDefault | ConfigScope.UserMandatory | ConfigScope.SystemDefault | ConfigScope.SystemMandatory);

        private List<Config> _items;

        /// <summary>
        /// Validates parameters and initializes the items collection.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            WriteMessageAtLevel(
                String.Format("Bound parameters: {0}", String.Join(", ", MyInvocation.BoundParameters.Keys)),
                MessageLevel.InternalComment,
                new string[] { "debug", "start", "param" });

            _items = new List<Config>();

            if (!String.IsNullOrEmpty(ModuleName) && ((int)Scope & RegistryScopeMask) != 0)
            {
                StopFunction(
                    "Cannot export modulecache to registry! Please pick a file scope for your export destination",
                    category: ErrorCategory.InvalidArgument,
                    tag: new string[] { "fail", "scope", "registry" });
                return;
            }
        }

        /// <summary>
        /// Accumulates config items from pipeline or fetches by FullName/Module.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (TestFunctionInterrupt()) return;

            if (!String.IsNullOrEmpty(ModuleName))
                return;

            if (Config != null)
            {
                foreach (Config item in Config)
                {
                    if (item != null)
                        _items.Add(item);
                }
                return;
            }

            if (!String.IsNullOrEmpty(FullName))
            {
                _items = GetConfigsByFullName(FullName);
            }
            else if (!String.IsNullOrEmpty(Module))
            {
                _items = GetConfigsByModule(Module, Name);
            }
        }

        /// <summary>
        /// Writes the collected configuration items to the output file or module scope paths.
        /// </summary>
        protected override void EndProcessing()
        {
            if (TestFunctionInterrupt()) return;

            if (String.IsNullOrEmpty(ModuleName))
            {
                List<Config> filtered = new List<Config>(_items.Count);
                foreach (Config item in _items)
                {
                    if (!SkipUnchanged.IsPresent || !item.Unchanged)
                    {
                        filtered.Add(item);
                    }
                }

                try
                {
                    ConfigurationHelpers.WriteConfigFile(this, filtered.ToArray(), OutPath, true);
                }
                catch (Exception ex)
                {
                    StopFunction(
                        "Failed to export to file",
                        errorRecord: new ErrorRecord(ex, "ExportDbatoolsConfig_ExportFailed", ErrorCategory.WriteError, OutPath),
                        tag: new string[] { "fail", "export" });
                    return;
                }
            }
            else
            {
                List<Config> moduleConfigs = GetModuleExportConfigs(ModuleName);
                string moduleFileName = String.Format("{0}-{1}.json", ModuleName.ToLowerInvariant(), ModuleVersion);

                if ((Scope & ConfigScope.FileUserLocal) != 0)
                {
                    string scopePath = GetFileUserLocalPath();
                    WriteModuleScopeFile(moduleConfigs, scopePath, moduleFileName, "FileUserLocal");
                }

                if ((Scope & ConfigScope.FileUserShared) != 0)
                {
                    string scopePath = GetFileUserSharedPath();
                    WriteModuleScopeFile(moduleConfigs, scopePath, moduleFileName, "FileUserShared");
                }

                if ((Scope & ConfigScope.FileSystem) != 0)
                {
                    string scopePath = GetFileSystemPath();
                    WriteModuleScopeFile(moduleConfigs, scopePath, moduleFileName, "FileSystem");
                }
            }
        }

        #region Helper Methods

        /// <summary>
        /// Gets configuration items matching a full name pattern from ConfigurationHost.
        /// Hidden configs are excluded to match Get-DbatoolsConfig default behavior.
        /// </summary>
        internal static List<Config> GetConfigsByFullName(string fullName)
        {
            List<Config> results = new List<Config>();
            WildcardPattern pattern = new WildcardPattern(fullName, WildcardOptions.IgnoreCase);

            foreach (Config config in ConfigurationHost.Configurations.Values)
            {
                if (!config.Hidden && pattern.IsMatch(config.FullName))
                {
                    results.Add(config);
                }
            }

            return results;
        }

        /// <summary>
        /// Gets configuration items matching a module and name pattern from ConfigurationHost.
        /// Hidden configs are excluded to match Get-DbatoolsConfig default behavior.
        /// </summary>
        internal static List<Config> GetConfigsByModule(string module, string name)
        {
            List<Config> results = new List<Config>();
            WildcardPattern modulePattern = new WildcardPattern(module, WildcardOptions.IgnoreCase);
            WildcardPattern namePattern = new WildcardPattern(
                String.IsNullOrEmpty(name) ? "*" : name,
                WildcardOptions.IgnoreCase);

            foreach (Config config in ConfigurationHost.Configurations.Values)
            {
                if (!config.Hidden && modulePattern.IsMatch(config.Module) && namePattern.IsMatch(config.Name))
                {
                    results.Add(config);
                }
            }

            return results;
        }

        /// <summary>
        /// Gets configuration items for module export: includes hidden items (equivalent to -Force),
        /// filtered by ModuleExport flag and not Unchanged.
        /// </summary>
        internal static List<Config> GetModuleExportConfigs(string moduleName)
        {
            List<Config> results = new List<Config>();
            WildcardPattern modulePattern = new WildcardPattern(moduleName, WildcardOptions.IgnoreCase);

            foreach (Config config in ConfigurationHost.Configurations.Values)
            {
                if (modulePattern.IsMatch(config.Module) && config.ModuleExport && !config.Unchanged)
                {
                    results.Add(config);
                }
            }

            return results;
        }

        /// <summary>
        /// Writes module export configs to a scope-specific path.
        /// </summary>
        private void WriteModuleScopeFile(List<Config> moduleConfigs, string scopePath, string fileName, string scopeName)
        {
            if (String.IsNullOrEmpty(scopePath))
            {
                WriteMessageAtLevel(
                    String.Format("Could not resolve the config path for scope {0}. Ensure the dbatools module is properly initialized.", scopeName),
                    MessageLevel.Warning, null);
                return;
            }

            string filePath = Path.Combine(scopePath, fileName);
            ConfigurationHelpers.WriteConfigFile(this, moduleConfigs.ToArray(), filePath, false);
        }

        /// <summary>
        /// Gets the PS version folder name: "PowerShell" for PS 6+, "WindowsPowerShell" for 5.x and below.
        /// </summary>
        internal static string GetPsVersionName()
        {
#if NETFRAMEWORK
            return "WindowsPowerShell";
#else
            return "PowerShell";
#endif
        }

        /// <summary>
        /// Computes the FileUserLocal config path, matching the PS1 logic in configuration.ps1.
        /// On Linux/macOS: $XDG_CONFIG_HOME or ~/.config/ + {psVersionName}/dbatools/
        /// On Windows: LocalApplicationData + {psVersionName}\dbatools\Config
        /// </summary>
        internal static string GetFileUserLocalPath()
        {
            string psVersionName = GetPsVersionName();

#if NETFRAMEWORK
            string localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (String.IsNullOrEmpty(localAppData))
                localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, psVersionName, "dbatools", "Config");
#else
            if (!FlowControl.TestWindows())
            {
                string xdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                if (String.IsNullOrEmpty(xdgConfig))
                    xdgConfig = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
                return Path.Combine(xdgConfig, psVersionName, "dbatools");
            }
            else
            {
                string localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
                if (String.IsNullOrEmpty(localAppData))
                    localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(localAppData, psVersionName, "dbatools", "Config");
            }
#endif
        }

        /// <summary>
        /// Computes the FileUserShared config path, matching the PS1 logic in configuration.ps1.
        /// On Linux/macOS: ~/.local/share/ + {psVersionName}/dbatools/
        /// On Windows: ApplicationData + {psVersionName}\dbatools\Config
        /// </summary>
        internal static string GetFileUserSharedPath()
        {
            string psVersionName = GetPsVersionName();

#if NETFRAMEWORK
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, psVersionName, "dbatools", "Config");
#else
            if (!FlowControl.TestWindows())
            {
                string localShare = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
                return Path.Combine(localShare, psVersionName, "dbatools");
            }
            else
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, psVersionName, "dbatools", "Config");
            }
#endif
        }

        /// <summary>
        /// Computes the FileSystem config path, matching the PS1 logic in configuration.ps1.
        /// On Linux/macOS: /etc/xdg/ + {psVersionName}/dbatools/
        /// On Windows: ProgramData + {psVersionName}\dbatools\Config
        /// </summary>
        internal static string GetFileSystemPath()
        {
            string psVersionName = GetPsVersionName();

#if NETFRAMEWORK
            string programData = Environment.GetEnvironmentVariable("ProgramData");
            if (String.IsNullOrEmpty(programData))
                programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            return Path.Combine(programData, psVersionName, "dbatools", "Config");
#else
            if (!FlowControl.TestWindows())
            {
                return Path.Combine("/etc", "xdg", psVersionName, "dbatools");
            }
            else
            {
                string programData = Environment.GetEnvironmentVariable("ProgramData");
                if (String.IsNullOrEmpty(programData))
                    programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                return Path.Combine(programData, psVersionName, "dbatools", "Config");
            }
#endif
        }

        #endregion Helper Methods
    }
}
