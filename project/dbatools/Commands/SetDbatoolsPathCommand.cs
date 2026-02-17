using Dataplat.Dbatools.Configuration;
using System;
using System.Management.Automation;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Configures or updates a path under a name. The path can be persisted using the -Register parameter.
    /// Paths configured this way can be retrieved using Get-DbatoolsPath.
    /// Note: ShouldProcess is intentionally not supported, matching the original PS1 design.
    /// </summary>
    [Cmdlet("Set", "DbatoolsPath", DefaultParameterSetName = "Default")]
    public class SetDbatoolsPathCommand : DbaBaseCmdlet
    {
        /// <summary>
        /// Specifies the alias name to associate with the path for easy retrieval.
        /// Use descriptive names like 'backups', 'scripts', or 'logs' to organize commonly used directory paths.
        /// The name can be referenced later with Get-DbatoolsPath to quickly access the stored path.
        /// </summary>
        [Parameter(Mandatory = true)]
        public string Name { get; set; }

        /// <summary>
        /// Specifies the directory path to store under the given name.
        /// Can be any valid file system path including network shares and mapped drives.
        /// </summary>
        [Parameter(Mandatory = true)]
        public string Path { get; set; }

        /// <summary>
        /// Persists the path configuration across PowerShell sessions and module reloads.
        /// Without this switch, the path mapping only exists for the current session.
        /// </summary>
        [Parameter(ParameterSetName = "Register", Mandatory = true)]
        public SwitchParameter Register { get; set; }

        /// <summary>
        /// Determines where the persistent configuration is stored when using -Register.
        /// UserDefault stores the setting for the current user only, while other scopes affect system-wide or module-level settings.
        /// </summary>
        [Parameter(ParameterSetName = "Register")]
        public ConfigScope Scope { get; set; } = ConfigScope.UserDefault;

        #region Private Resources
        private static readonly string _setConfigScript = @"
param($FullName, $Value)
Set-DbatoolsConfig -FullName $FullName -Value $Value
";
        private static readonly string _registerConfigScript = @"
param($FullName, $Scope)
Register-DbatoolsConfig -FullName $FullName -Scope $Scope
";
        private static readonly ScriptBlock _setConfigBlock = ScriptBlock.Create(_setConfigScript);
        private static readonly ScriptBlock _registerConfigBlock = ScriptBlock.Create(_registerConfigScript);
        #endregion Private Resources

        /// <summary>
        /// Builds the full configuration key name from the path alias name.
        /// </summary>
        /// <param name="name">The path alias name</param>
        /// <returns>The full config key in the format "Path.Managed.{name}"</returns>
        internal static string BuildConfigKey(string name)
        {
            return String.Format("Path.Managed.{0}", name);
        }

        /// <summary>
        /// Sets the config value and optionally registers for persistence.
        /// If the set operation fails, the register step is not attempted.
        /// </summary>
        protected override void ProcessRecord()
        {
            string fullName = BuildConfigKey(Name);

            try
            {
                InvokeCommand.InvokeScript(
                    false,
                    _setConfigBlock,
                    null,
                    fullName,
                    Path
                );
            }
            catch (Exception ex)
            {
                // Name is the target (the alias being configured)
                StopFunction(String.Format("Failed to set path '{0}'", Name), ex, null, Name);
                TestFunctionInterrupt();
                return;
            }

            if (Register.ToBool())
            {
                try
                {
                    InvokeCommand.InvokeScript(
                        false,
                        _registerConfigBlock,
                        null,
                        fullName,
                        Scope
                    );
                }
                catch (Exception ex)
                {
                    StopFunction(String.Format("Failed to register path '{0}'", Name), ex, null, Name);
                    TestFunctionInterrupt();
                    return;
                }
            }
        }
    }
}
