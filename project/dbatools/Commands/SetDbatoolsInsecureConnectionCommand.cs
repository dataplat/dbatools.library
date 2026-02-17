using Dataplat.Dbatools.Configuration;
using System;
using System.Management.Automation;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Reverts SQL Server connection security defaults to disable encryption and trust all certificates.
    /// Sets sql.connection.trustcert to true and sql.connection.encrypt to false.
    /// </summary>
    [Cmdlet("Set", "DbatoolsInsecureConnection")]
    public class SetDbatoolsInsecureConnectionCommand : DbaBaseCmdlet
    {
        private const string TrustCertKey = "sql.connection.trustcert";
        private const string EncryptKey = "sql.connection.encrypt";

        private static readonly ScriptBlock SetConfigScript = ScriptBlock.Create(
            "param($name, $val) Set-DbatoolsConfig -FullName $name -Value $val"
        );

        private static readonly ScriptBlock RegisterConfigScript = ScriptBlock.Create(
            "param($name, $scope) Register-DbatoolsConfig -FullName $name -Scope $scope"
        );

        #region Parameters
        /// <summary>
        /// Applies the insecure connection settings only to the current PowerShell session
        /// instead of persisting them permanently.
        /// </summary>
        [Parameter()]
        public SwitchParameter SessionOnly { get; set; }

        /// <summary>
        /// Specifies where to store the persistent connection settings when SessionOnly is not used.
        /// Defaults to UserDefault.
        /// </summary>
        [Parameter()]
        public ConfigScope Scope { get; set; } = ConfigScope.UserDefault;

        /// <summary>
        /// This parameter is deprecated and will be removed in a future release.
        /// The function now automatically handles registration of settings when SessionOnly is not specified.
        /// </summary>
        [Parameter()]
        public SwitchParameter Register { get; set; }
        #endregion Parameters

        /// <summary>
        /// Implements the process action: sets trustcert and encrypt config values,
        /// optionally registering them for persistence.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (Register.IsPresent)
            {
                WriteMessageWarning("The Register parameter is deprecated and will be removed in a future release.");
            }

            // Set sql.connection.trustcert = true
            try
            {
                InvokeCommand.InvokeScript(false, SetConfigScript, null, TrustCertKey, true);
            }
            catch (Exception ex)
            {
                StopFunction(String.Format("Failed to set configuration '{0}'", TrustCertKey), ex);
                TestFunctionInterrupt();
                return;
            }

            // Set sql.connection.encrypt = false
            try
            {
                InvokeCommand.InvokeScript(false, SetConfigScript, null, EncryptKey, false);
            }
            catch (Exception ex)
            {
                StopFunction(String.Format("Failed to set configuration '{0}'", EncryptKey), ex);
                TestFunctionInterrupt();
                return;
            }

            if (!SessionOnly.IsPresent)
            {
                // Register sql.connection.trustcert
                try
                {
                    InvokeCommand.InvokeScript(false, RegisterConfigScript, null, TrustCertKey, Scope);
                }
                catch (Exception ex)
                {
                    StopFunction(String.Format("Failed to register configuration '{0}'", TrustCertKey), ex);
                    TestFunctionInterrupt();
                    return;
                }

                // Register sql.connection.encrypt
                try
                {
                    InvokeCommand.InvokeScript(false, RegisterConfigScript, null, EncryptKey, Scope);
                }
                catch (Exception ex)
                {
                    StopFunction(String.Format("Failed to register configuration '{0}'", EncryptKey), ex);
                    TestFunctionInterrupt();
                    return;
                }
            }
        }
    }
}
