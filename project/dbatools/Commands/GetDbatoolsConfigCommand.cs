using System;
using System.Collections.Generic;
using System.Management.Automation;
using Dataplat.Dbatools.Configuration;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Retrieves dbatools module configuration settings and preferences.
    /// </summary>
    [Cmdlet("Get", "DbatoolsConfig", DefaultParameterSetName = "FullName")]
    [OutputType(typeof(Config))]
    public class GetDbatoolsConfigCommand : DbaBaseCmdlet
    {
        /// <summary>
        /// Specifies the complete configuration key in Module.Name format to retrieve specific dbatools settings.
        /// Supports wildcards for pattern matching across all configuration keys.
        /// </summary>
        [Parameter(ParameterSetName = "FullName", Position = 0)]
        public string FullName { get; set; } = "*";

        /// <summary>
        /// Specifies the configuration name to search for within a specific module.
        /// Supports wildcards for finding multiple related configuration names.
        /// </summary>
        [Parameter(ParameterSetName = "Module", Position = 1)]
        public string Name { get; set; } = "*";

        /// <summary>
        /// Specifies which dbatools module's configuration settings to retrieve.
        /// </summary>
        [Parameter(ParameterSetName = "Module", Position = 0)]
        public string Module { get; set; } = "*";

        /// <summary>
        /// Includes hidden configuration values that are normally not displayed in the output.
        /// </summary>
        [Parameter()]
        public SwitchParameter Force { get; set; }

        /// <summary>
        /// Filters and returns matching configuration entries from the ConfigurationHost.
        /// </summary>
        protected override void ProcessRecord()
        {
            List<Config> results = new List<Config>();

            if (ParameterSetName == "Module")
            {
                WildcardPattern namePattern = new WildcardPattern(Name, WildcardOptions.IgnoreCase);
                WildcardPattern modulePattern = new WildcardPattern(Module, WildcardOptions.IgnoreCase);

                foreach (Config config in ConfigurationHost.Configurations.Values)
                {
                    if (namePattern.IsMatch(config.Name) && modulePattern.IsMatch(config.Module))
                    {
                        if (!config.Hidden || Force.IsPresent)
                        {
                            results.Add(config);
                        }
                    }
                }
            }
            else
            {
                // FullName parameter set
                WildcardPattern fullNamePattern = new WildcardPattern(FullName, WildcardOptions.IgnoreCase);

                foreach (Config config in ConfigurationHost.Configurations.Values)
                {
                    if (fullNamePattern.IsMatch(config.FullName))
                    {
                        if (!config.Hidden || Force.IsPresent)
                        {
                            results.Add(config);
                        }
                    }
                }
            }

            results.Sort(CompareConfigByModuleThenName);

            foreach (Config config in results)
            {
                WriteObject(config);
            }
        }

        /// <summary>
        /// Compares two Config objects by Module then by Name for sorting.
        /// </summary>
        /// <param name="x">First config</param>
        /// <param name="y">Second config</param>
        /// <returns>Comparison result</returns>
        internal static int CompareConfigByModuleThenName(Config x, Config y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            int moduleCompare = String.Compare(x.Module, y.Module, StringComparison.OrdinalIgnoreCase);
            if (moduleCompare != 0)
                return moduleCompare;

            return String.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
        }
    }
}
