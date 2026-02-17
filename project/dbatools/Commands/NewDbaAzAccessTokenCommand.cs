using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Security;
using Dataplat.Dbatools.Configuration;
using Dataplat.Dbatools.Message;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Generates OAuth2 access tokens for Azure SQL Database and other Azure services authentication.
    /// Supports Managed Identity, Service Principal, and Renewable Service Principal authentication types.
    /// </summary>
    [OutputType(typeof(string))]
    [Cmdlet("New", "DbaAzAccessToken")]
    public class NewDbaAzAccessTokenCommand : DbaBaseCmdlet
    {
        /// <summary>
        /// Specifies the authentication method for generating the access token.
        /// ManagedIdentity uses Azure VM identity, ServicePrincipal uses application credentials,
        /// and RenewableServicePrincipal creates tokens that automatically refresh.
        /// </summary>
        [Parameter(Mandatory = true)]
        [ValidateSet("ManagedIdentity", "ServicePrincipal", "RenewableServicePrincipal")]
        public string Type { get; set; }

        /// <summary>
        /// Determines which Azure service resource to generate the token for.
        /// Defaults to AzureSqlDb for database connections.
        /// </summary>
        [Parameter()]
        [ValidateSet("AzureSqlDb", "ResourceManager", "DataLake", "EventHubs", "KeyVault", "ServiceBus", "Storage")]
        public string Subtype { get; set; }

        /// <summary>
        /// Optional configuration object for advanced token generation scenarios.
        /// </summary>
        [Parameter()]
        public object Config { get; set; }

        /// <summary>
        /// When using the ServicePrincipal type, a Credential is required.
        /// The username is the App ID and Password is the App Password.
        /// </summary>
        [Parameter()]
        public PSCredential Credential { get; set; }

        /// <summary>
        /// Specifies the Azure Active Directory tenant ID or domain name for Service Principal authentication.
        /// </summary>
        [Parameter()]
        public string Tenant { get; set; }

        /// <summary>
        /// Certificate thumbprint for Managed Service Identity authentication.
        /// </summary>
        [Parameter()]
        public string Thumbprint { get; set; }

        /// <summary>
        /// Specifies the certificate store location for MSI certificates.
        /// </summary>
        [Parameter()]
        [ValidateSet("CurrentUser", "LocalMachine")]
        public string Store { get; set; }

        /// <summary>
        /// Default IMDS API version for Managed Identity token requests.
        /// </summary>
        private const string DefaultImdsApiVersion = "2018-04-02";

        /// <summary>
        /// The resolved resource URL based on Subtype.
        /// </summary>
        private string _resource;

        /// <summary>
        /// The resolved API version for Managed Identity requests.
        /// </summary>
        private string _version;

        /// <summary>
        /// Resolves default parameter values from configuration and validates inputs.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            // Apply default for Subtype if not bound
            if (!TestBound("Subtype"))
            {
                Subtype = "AzureSqlDb";
            }

            // Load defaults from dbatools configuration for unbound parameters
            if (!TestBound("Tenant"))
            {
                Tenant = GetConfigValue("azure.tenantid") as string;
            }

            if (!TestBound("Thumbprint"))
            {
                Thumbprint = GetConfigValue("azure.certificate.thumbprint") as string;
            }

            if (!TestBound("Store"))
            {
                Store = GetConfigValue("azure.certificate.store") as string;
            }

            // For ServicePrincipal types, resolve credential from config if not provided
            if (Type == "ServicePrincipal" || Type == "RenewableServicePrincipal")
            {
                if (Credential == null)
                {
                    object appId = GetConfigValue("azure.appid");
                    object clientSecret = GetConfigValue("azure.clientsecret");

                    if (appId != null && clientSecret != null)
                    {
                        string appIdStr = appId.ToString();
                        SecureString securePassword = null;
                        if (clientSecret is SecureString ss)
                        {
                            securePassword = ss;
                        }
                        else
                        {
                            securePassword = new System.Net.NetworkCredential("", clientSecret.ToString()).SecurePassword;
                        }

                        if (!String.IsNullOrEmpty(appIdStr) && securePassword != null)
                        {
                            Credential = new PSCredential(appIdStr, securePassword);
                        }
                    }
                }

                if (Credential == null && String.IsNullOrEmpty(Tenant))
                {
                    StopFunction("You must specify a Credential and Tenant when using ServicePrincipal or RenewableServicePrincipal");
                    return;
                }

                if (Credential == null)
                {
                    StopFunction("A Credential is required for ServicePrincipal or RenewableServicePrincipal authentication. Provide -Credential with the App ID as username and App Secret as password, or configure azure.appid and azure.clientsecret.");
                    return;
                }
            }

            // Resolve resource URL from Config or Subtype
            if (Config != null)
            {
                _resource = GetResourceFromConfig(Config);
                _version = GetVersionFromConfig(Config);
            }

            if (String.IsNullOrEmpty(_resource))
            {
                _resource = GetResourceUrl(Subtype);
            }
        }

        /// <summary>
        /// Generates the access token based on the specified Type.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (TestFunctionInterrupt())
            {
                return;
            }

            try
            {
                switch (Type)
                {
                    case "ManagedIdentity":
                        ProcessManagedIdentity();
                        break;
                    case "ServicePrincipal":
                        ProcessServicePrincipal();
                        break;
                    case "RenewableServicePrincipal":
                        ProcessRenewableServicePrincipal();
                        break;
                }
            }
            catch (Exception ex)
            {
                StopFunction(
                    String.Format("Failed to generate {0} access token", Type),
                    exception: ex,
                    target: Type);
            }
        }

        #region Token Acquisition Methods

        /// <summary>
        /// Acquires an access token using the Azure Instance Metadata Service (IMDS).
        /// Note: IMDS uses HTTP (not HTTPS), so TLS settings do not affect this call.
        /// The PS1 used Invoke-TlsWebRequest which was a no-op for HTTP endpoints.
        /// </summary>
        private void ProcessManagedIdentity()
        {
            string version = _version;
            if (String.IsNullOrEmpty(version))
            {
                version = DefaultImdsApiVersion;
            }

            // Use Invoke-TlsWebRequest for consistency with the PS1 implementation.
            // IMDS uses HTTP so TLS is not relevant, but Invoke-TlsWebRequest is a
            // thin wrapper around Invoke-WebRequest that ensures TLS 1.2 is available.
            string script = @"
param($version, $resource)
$params = @{
    Uri     = ""http://169.254.169.254/metadata/identity/oauth2/token?api-version=$version&resource=$resource""
    Method  = 'GET'
    Headers = @{ Metadata = 'true' }
}
$response = Invoke-TlsWebRequest @params -UseBasicParsing -ErrorAction Stop
($response.Content | ConvertFrom-Json).access_token
";
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false,
                ScriptBlock.Create(script),
                null,
                version, _resource);

            if (results != null && results.Count > 0 && results[0] != null)
            {
                WriteObject(results[0].BaseObject);
            }
        }

        /// <summary>
        /// Acquires an access token using Service Principal credentials via OAuth2 client_credentials flow.
        /// Note: The original PS1 used the deprecated ADAL library (Microsoft.IdentityModel.Clients.ActiveDirectory)
        /// which was .NET Framework-only. This implementation uses direct OAuth2 REST calls instead,
        /// which works on both .NET Framework and .NET Core. The PS1 blocked this path on PowerShell Core
        /// due to ADAL incompatibility; that restriction is no longer needed with the REST approach.
        /// </summary>
        private void ProcessServicePrincipal()
        {
            string authority = String.Format("https://login.microsoftonline.com/{0}/oauth2/token", Tenant);
            string clientId = Credential.UserName;
            string clientSecretStr = Credential.GetNetworkCredential().Password;

            string script = @"
param($authority, $clientId, $clientSecret, $resource)
$body = @{
    grant_type    = 'client_credentials'
    client_id     = $clientId
    client_secret = $clientSecret
    resource      = $resource
}
$result = Invoke-RestMethod -Uri $authority -Method Post -Body $body -ErrorAction Stop
$result.access_token
";
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false,
                ScriptBlock.Create(script),
                null,
                authority, clientId, clientSecretStr, _resource);

            if (results != null && results.Count > 0 && results[0] != null)
            {
                WriteObject(results[0].BaseObject);
            }
            else
            {
                StopFunction("Failed to acquire access token from Service Principal authentication");
                return;
            }
        }

        /// <summary>
        /// Creates a renewable service principal token object that implements IRenewableToken.
        /// The token auto-refreshes when GetAccessToken() is called.
        /// Note: The output object contains the ClientSecret in plain text as a public property,
        /// which is required for token renewal. This matches the original PS1 behavior.
        /// Callers should treat this object as sensitive and avoid logging or serializing it.
        /// </summary>
        private void ProcessRenewableServicePrincipal()
        {
            string clientSecretStr = Credential.GetNetworkCredential().Password;
            string userId = Credential.UserName;

            // Create the PsObjectIRenewableToken via Add-Type (matches PS1 behavior exactly).
            // The type implements IRenewableToken so SMO/Connect-DbaInstance can use it directly.
            string addTypeScript = @"
param($clientSecret, $resource, $tenant, $userId)
try {
    [PsObjectIRenewableToken] | Out-Null
} catch {
    $source = @""
    using System;
    using Microsoft.SqlServer.Management.Common;
    using System.Management.Automation;
    using System.Collections.ObjectModel;
    using System.Management.Automation.Runspaces;

    public class PsObjectIRenewableToken : IRenewableToken {
        public String GetAccessToken() {
            PowerShell psCmd = PowerShell.Create().AddScript(@""param(`$this)$({
            $authority = """"https://login.microsoftonline.com/$($this.Tenant)/oauth2/token""""
            $parameter = @{
                grant_type='client_credentials'
                client_id=$this.UserID
                client_secret=$this.ClientSecret
                resource=$this.Resource
            }

            $body = (@(foreach ($param in $parameter.GetEnumerator()) {
                """"$($param.key)=$([Uri]::EscapeDataString($param.Value.ToString()))""""
            }) -join '&')

            $bearerInfo = Invoke-RestMethod -Uri $authority -Method Post -Body $body
            $this.TokenExpiry = [DateTimeOffset]::FromUnixTimeSeconds($BearerInfo.expires_on)
            return $bearerInfo.access_token
            }.ToString().Replace('""""','""""""""'))"").AddArgument(this);

            Collection<string> results = psCmd.Invoke<string>();
            if (psCmd.Streams.Error.Count > 0) {
                throw psCmd.Streams.Error[0].Exception;
            }

            psCmd.Dispose();

            if (results.Count == 1) {
                return results[0];
            } else {
                return String.Empty;
            }
        }

        public System.DateTimeOffset TokenExpiry { get; set;  }
        public String Resource { get; set; }
        public System.String Tenant { get; set; }
        public System.String UserId { get; set; }
        public string ClientSecret { get; set; }
    }
""@
    Add-Type -TypeDefinition $source -ReferencedAssemblies ([Microsoft.SqlServer.Management.Common.IRenewableToken].Assembly,
        [PowerShell].Assembly,
        [Microsoft.SqlServer.Management.Common.IRenewableToken].Assembly.GetReferencedAssemblies()[0])
}
$result = New-Object PsObjectIRenewableToken -Property @{
    ClientSecret = $clientSecret
    Resource     = $resource
    Tenant       = $tenant
    UserID       = $userId
}
$result
";
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(
                    false,
                    ScriptBlock.Create(addTypeScript),
                    null,
                    clientSecretStr, _resource, Tenant, userId);

                if (results != null && results.Count > 0 && results[0] != null)
                {
                    WriteObject(results[0]);
                }
            }
            catch (Exception ex)
            {
                StopFunction(
                    "Failed to create renewable token for service principal",
                    exception: ex,
                    target: Type);
                return;
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Gets the resource URL for a given Azure service subtype.
        /// </summary>
        internal static string GetResourceUrl(string subtype)
        {
            if (String.IsNullOrEmpty(subtype))
                return "https://database.windows.net/";

            switch (subtype)
            {
                case "AzureSqlDb":
                    return "https://database.windows.net/";
                case "ResourceManager":
                    return "https://management.azure.com/";
                case "KeyVault":
                    return "https://vault.azure.net/";
                case "DataLake":
                    return "https://datalake.azure.net/";
                case "EventHubs":
                    return "https://eventhubs.azure.net/";
                case "ServiceBus":
                    return "https://servicebus.azure.net/";
                case "Storage":
                    return "https://storage.azure.com/";
                default:
                    return "https://database.windows.net/";
            }
        }

        /// <summary>
        /// Extracts the Resource property from a Config object (Hashtable or PSObject).
        /// </summary>
        internal static string GetResourceFromConfig(object config)
        {
            if (config == null)
                return null;

            if (config is Hashtable ht)
            {
                if (ht.ContainsKey("Resource"))
                    return ht["Resource"] as string;
                return null;
            }

            if (config is PSObject pso)
            {
                PSPropertyInfo prop = pso.Properties["Resource"];
                if (prop != null && prop.Value != null)
                    return prop.Value.ToString();
                return null;
            }

            return null;
        }

        /// <summary>
        /// Extracts the Version property from a Config object (Hashtable or PSObject).
        /// </summary>
        internal static string GetVersionFromConfig(object config)
        {
            if (config == null)
                return null;

            if (config is Hashtable ht)
            {
                if (ht.ContainsKey("Version"))
                    return ht["Version"] as string;
                return null;
            }

            if (config is PSObject pso)
            {
                PSPropertyInfo prop = pso.Properties["Version"];
                if (prop != null && prop.Value != null)
                    return prop.Value.ToString();
                return null;
            }

            return null;
        }

        /// <summary>
        /// Reads a value from the dbatools configuration system.
        /// </summary>
        private static object GetConfigValue(string fullName)
        {
            Config config;
            if (ConfigurationHost.Configurations.TryGetValue(fullName, out config))
            {
                return config.Value;
            }
            return null;
        }

        #endregion
    }
}
