using System;
using System.Management.Automation;
using Dataplat.Dbatools.Configuration;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Retrieves a specific dbatools configuration value by its exact name.
    /// </summary>
    [Cmdlet("Get", "DbatoolsConfigValue")]
    public class GetDbatoolsConfigValueCommand : DbaBaseCmdlet
    {
        /// <summary>
        /// Specifies the exact configuration setting name in Module.Name format.
        /// </summary>
        [Parameter(Mandatory = true, Position = 0)]
        [Alias("Name")]
        public string FullName { get; set; }

        /// <summary>
        /// Provides a default value to return when the specified configuration setting doesn't exist or is set to null.
        /// </summary>
        [Parameter()]
        public object Fallback { get; set; }

        /// <summary>
        /// Forces the function to throw an error instead of returning null when no configuration value is found.
        /// </summary>
        [Parameter()]
        public SwitchParameter NotNull { get; set; }

        /// <summary>
        /// Looks up the configuration value and returns it, applying fallback and NotNull logic.
        /// </summary>
        protected override void ProcessRecord()
        {
            object temp = LookupConfigValue(FullName, Fallback);

            if (NotNull.IsPresent && temp == null)
            {
                // The PS1 always forces EnableException = $true for this error,
                // so StopFunction always throws a terminating error regardless
                // of the caller's EnableException preference.
                // We temporarily set EnableException to true; StopFunction then
                // calls ThrowTerminatingError which unwinds the stack, so the
                // restore line is only reached if EnableException was already true
                // and StopFunction threw before reaching the restore.
                EnableException = new SwitchParameter(true);
                StopFunction(
                    String.Format("No Configuration Value available for {0}", FullName),
                    target: FullName,
                    category: ErrorCategory.InvalidData
                );
                return;
            }

            WriteObject(temp);
        }

        /// <summary>
        /// Looks up a configuration value by full name with fallback and switch-safety conversion.
        /// Returns the config value, the fallback if not found, or null.
        /// </summary>
        /// <param name="fullName">The configuration key in Module.Name format</param>
        /// <param name="fallback">Fallback value if key not found or value is null</param>
        /// <returns>The resolved configuration value</returns>
        internal static object LookupConfigValue(string fullName, object fallback)
        {
            string key = fullName.ToLowerInvariant();

            object temp = null;
            Config config;
            if (ConfigurationHost.Configurations.TryGetValue(key, out config))
            {
                temp = config.Value;
            }

            if (temp == null)
            {
                return fallback;
            }

            // Prevent some potential [switch] parse issues
            string tempString = temp.ToString();
            bool? converted = ConvertSwitchSafetyValue(tempString);
            if (converted.HasValue)
            {
                return converted.Value;
            }

            return temp;
        }

        /// <summary>
        /// Converts a value string to true/false for switch parameter safety, or returns null if not a special string.
        /// Returns true for "Mandatory", false for "Optional", null otherwise.
        /// </summary>
        /// <param name="value">The value to check</param>
        /// <returns>true, false, or null</returns>
        internal static bool? ConvertSwitchSafetyValue(string value)
        {
            if (value == "Mandatory")
                return true;
            if (value == "Optional")
                return false;
            return null;
        }
    }
}
