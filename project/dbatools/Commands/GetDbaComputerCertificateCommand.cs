using System;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Retrieves X.509 certificates from Windows certificate stores that can be used for SQL Server TLS encryption.
    /// Scans certificate stores to find certificates suitable for enabling SQL Server network encryption.
    /// </summary>
    [Cmdlet("Get", "DbaComputerCertificate")]
    public class GetDbaComputerCertificateCommand : DbaBaseCmdlet
    {
        /// <summary>
        /// The target computer(s) to scan for certificates. Defaults to localhost.
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        public DbaInstanceParameter[] ComputerName { get; set; }

        /// <summary>
        /// Allows you to login to the target computer using alternative credentials.
        /// </summary>
        [Parameter()]
        public PSCredential Credential { get; set; }

        /// <summary>
        /// Specifies which Windows certificate store location to search. Defaults to LocalMachine.
        /// </summary>
        [Parameter()]
        public string[] Store { get; set; }

        /// <summary>
        /// Specifies which certificate folder within the store to search. Defaults to My (Personal certificates).
        /// </summary>
        [Parameter()]
        public string[] Folder { get; set; }

        /// <summary>
        /// Specifies the file system path to a certificate file to load and analyze.
        /// </summary>
        [Parameter()]
        public string Path { get; set; }

        /// <summary>
        /// Filters certificates by their intended usage. Service returns only certificates with Server Authentication capability, All returns every certificate.
        /// </summary>
        [Parameter()]
        [ValidateSet("All", "Service")]
        public string Type { get; set; }

        /// <summary>
        /// Filters results to return only certificates with the specified thumbprint(s).
        /// </summary>
        [Parameter()]
        public string[] Thumbprint { get; set; }

        private static readonly ScriptBlock _certRetrievalScript =
            ScriptBlock.Create(GetCertRetrievalScript());
        private static readonly ScriptBlock _invokeNoCredScript = ScriptBlock.Create(
            "param($cn, $sb, $args) Invoke-Command2 -ComputerName $cn -ScriptBlock $sb -ArgumentList $args -ErrorAction Stop");
        private static readonly ScriptBlock _invokeWithCredScript = ScriptBlock.Create(
            "param($cn, $cred, $sb, $args) Invoke-Command2 -ComputerName $cn -Credential $cred -ScriptBlock $sb -ArgumentList $args -ErrorAction Stop");
        private static readonly ScriptBlock _getStoresScript = ScriptBlock.Create(
            "Get-ChildItem Cert: | Select-Object -ExpandProperty Location");
        private static readonly ScriptBlock _getFoldersScript = ScriptBlock.Create(
            "Get-ChildItem Cert: | Select-Object -ExpandProperty StoreNames | Select-Object -ExpandProperty Keys");
        private static readonly ScriptBlock _invokeRawNoCredScript = ScriptBlock.Create(
            "param($cn, $sb) Invoke-Command2 -ComputerName $cn -ScriptBlock $sb -Raw -ErrorAction Stop");
        private static readonly ScriptBlock _invokeRawWithCredScript = ScriptBlock.Create(
            "param($cn, $cred, $sb) Invoke-Command2 -ComputerName $cn -Credential $cred -ScriptBlock $sb -Raw -ErrorAction Stop");
        private static readonly ScriptBlock _selectDefaultViewScript = ScriptBlock.Create(
            "param($obj) $obj | Select-DefaultView -Property ComputerName, Store, Folder, Name, DnsNameList, Thumbprint, NotBefore, NotAfter, Subject, Issuer, Algorithm");

        /// <summary>
        /// Initializes default parameter values.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            if (!TestBound("ComputerName"))
            {
                string envComputer = Environment.GetEnvironmentVariable("COMPUTERNAME");
                if (String.IsNullOrEmpty(envComputer))
                    envComputer = Environment.MachineName;
                ComputerName = new DbaInstanceParameter[] { new DbaInstanceParameter(envComputer) };
            }

            if (!TestBound("Store"))
            {
                Store = new string[] { "LocalMachine" };
            }

            if (!TestBound("Folder"))
            {
                Folder = new string[] { "My" };
            }

            if (!TestBound("Type"))
            {
                Type = "Service";
            }
        }

        /// <summary>
        /// Processes each computer, retrieving certificates from the specified store and folder combinations.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (ComputerName == null)
                return;

            foreach (DbaInstanceParameter computer in ComputerName)
            {
                string[] currentStores = Store;
                string[] currentFolders = Folder;

                // Handle Store = "All" - query remote computer for available stores
                if (currentStores != null && currentStores.Length == 1 &&
                    String.Equals(currentStores[0], "All", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        currentStores = GetRemoteStores(computer);
                    }
                    catch (Exception ex)
                    {
                        StopFunction(
                            "Issue connecting to computer",
                            errorRecord: new ErrorRecord(ex, "GetStores", ErrorCategory.ConnectionError, computer),
                            target: computer,
                            isContinue: true);
                        TestFunctionInterrupt();
                        continue;
                    }
                }

                // Handle Folder = "All" - query remote computer for available folders
                if (currentFolders != null && currentFolders.Length == 1 &&
                    String.Equals(currentFolders[0], "All", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        currentFolders = GetRemoteFolders(computer);
                    }
                    catch (Exception ex)
                    {
                        StopFunction(
                            "Issue connecting to computer",
                            errorRecord: new ErrorRecord(ex, "GetFolders", ErrorCategory.ConnectionError, computer),
                            target: computer,
                            isContinue: true);
                        TestFunctionInterrupt();
                        continue;
                    }
                }

                if (currentStores == null || currentFolders == null)
                    continue;

                foreach (string currentStore in currentStores)
                {
                    foreach (string currentFolder in currentFolders)
                    {
                        try
                        {
                            Collection<PSObject> results = InvokeRemoteCertRetrieval(
                                computer, currentStore, currentFolder);

                            if (results != null)
                            {
                                foreach (PSObject result in results)
                                {
                                    if (result != null)
                                    {
                                        // Apply Select-DefaultView
                                        Collection<PSObject> viewResults = InvokeCommand.InvokeScript(
                                            false, _selectDefaultViewScript, null, result);
                                        if (viewResults != null)
                                        {
                                            foreach (PSObject viewResult in viewResults)
                                            {
                                                if (viewResult != null)
                                                    WriteObject(viewResult);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            StopFunction(
                                "Issue connecting to computer",
                                errorRecord: new ErrorRecord(ex, "GetCertificates", ErrorCategory.ConnectionError, computer),
                                target: computer,
                                isContinue: true);
                            TestFunctionInterrupt();
                        }
                    }
                }
            }
        }

        #region Helper Methods

        /// <summary>
        /// Invokes the certificate retrieval script block on a remote computer via Invoke-Command2.
        /// </summary>
        private Collection<PSObject> InvokeRemoteCertRetrieval(
            DbaInstanceParameter computer, string store, string folder)
        {
            // $Type is passed as a 5th argument. The original PS1 scriptblock only declared 4 params
            // ($Thumbprint, $Store, $Folder, $Path) and relied on closure scope for $Type.
            // Closure capture works locally but not across a remoting boundary where Invoke-Command2
            // serializes only the ArgumentList. This fix ensures $Type filtering works for remote targets.
            object[] args = new object[] { Thumbprint, store, folder, Path, Type };

            if (Credential != null)
            {
                return InvokeCommand.InvokeScript(
                    false, _invokeWithCredScript, null,
                    computer, Credential, _certRetrievalScript, args);
            }
            else
            {
                return InvokeCommand.InvokeScript(
                    false, _invokeNoCredScript, null,
                    computer, _certRetrievalScript, args);
            }
        }

        /// <summary>
        /// Queries the remote computer for available certificate store locations.
        /// </summary>
        private string[] GetRemoteStores(DbaInstanceParameter computer)
        {
            Collection<PSObject> results;
            if (Credential != null)
            {
                results = InvokeCommand.InvokeScript(
                    false, _invokeRawWithCredScript, null,
                    computer, Credential, _getStoresScript);
            }
            else
            {
                results = InvokeCommand.InvokeScript(
                    false, _invokeRawNoCredScript, null,
                    computer, _getStoresScript);
            }

            return ConvertPSObjectsToStringArray(results);
        }

        /// <summary>
        /// Queries the remote computer for available certificate folder keys.
        /// </summary>
        private string[] GetRemoteFolders(DbaInstanceParameter computer)
        {
            Collection<PSObject> results;
            if (Credential != null)
            {
                results = InvokeCommand.InvokeScript(
                    false, _invokeRawWithCredScript, null,
                    computer, Credential, _getFoldersScript);
            }
            else
            {
                results = InvokeCommand.InvokeScript(
                    false, _invokeRawNoCredScript, null,
                    computer, _getFoldersScript);
            }

            return ConvertPSObjectsToStringArray(results);
        }

        /// <summary>
        /// Converts a collection of PSObjects to a string array.
        /// </summary>
        internal static string[] ConvertPSObjectsToStringArray(Collection<PSObject> objects)
        {
            if (objects == null || objects.Count == 0)
                return null;

            string[] result = new string[objects.Count];
            for (int i = 0; i < objects.Count; i++)
            {
                result[i] = objects[i] != null ? objects[i].ToString() : null;
            }
            return result;
        }

        /// <summary>
        /// Returns the PowerShell script block that retrieves certificates from a certificate store.
        /// This runs on the target computer via Invoke-Command2.
        /// </summary>
        internal static string GetCertRetrievalScript()
        {
            return @"
param (
    $Thumbprint,
    $Store,
    $Folder,
    $Path,
    $Type
)

if ($Path) {
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $Certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2
    $Certificate.Import($bytes, $null, [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::DefaultKeySet)
    return $Certificate
}

function Get-CoreCertStore {
    [CmdletBinding()]
    param (
        [ValidateSet('CurrentUser', 'LocalMachine')]
        [string]$Store,
        # Note: 'AuthRoot, CertificateAuthority' is a single ValidateSet entry inherited from the original PS1.
        # AuthRoot and CertificateAuthority are separate StoreName values but were combined in the original code.
        [ValidateSet('AddressBook', 'AuthRoot, CertificateAuthority', 'Disallowed', 'My', 'Root', 'TrustedPeople', 'TrustedPublisher')]
        [string]$Folder,
        [ValidateSet('ReadOnly', 'ReadWrite')]
        [string]$Flag = 'ReadOnly'
    )

    $storename = [System.Security.Cryptography.X509Certificates.StoreLocation]::$Store
    $foldername = [System.Security.Cryptography.X509Certificates.StoreName]::$Folder
    $flags = [System.Security.Cryptography.X509Certificates.OpenFlags]::$Flag
    $certstore = New-Object System.Security.Cryptography.X509Certificates.X509Store -ArgumentList $foldername, $storename
    $certstore.Open($flags)

    $certstore
}

function Get-CoreCertificate {
    [CmdletBinding()]
    param (
        [ValidateSet('CurrentUser', 'LocalMachine')]
        [string]$Store,
        [ValidateSet('AddressBook', 'AuthRoot, CertificateAuthority', 'Disallowed', 'My', 'Root', 'TrustedPeople', 'TrustedPublisher')]
        [string]$Folder,
        [ValidateSet('ReadOnly', 'ReadWrite')]
        [string]$Flag = 'ReadOnly',
        [string[]]$Thumbprint,
        [System.Security.Cryptography.X509Certificates.X509Store[]]$InputObject
    )

    if (-not $InputObject) {
        $InputObject += Get-CoreCertStore -Store $Store -Folder $Folder -Flag $Flag
    }

    $certs = ($InputObject).Certificates

    if ($Thumbprint) {
        $certs = $certs | Where-Object Thumbprint -in $Thumbprint
    }

    foreach ($c in $certs) {
        Add-Member -Force -InputObject $c -NotePropertyName Algorithm -NotePropertyValue $c.SignatureAlgorithm.FriendlyName
        Add-Member -Force -InputObject $c -NotePropertyName ComputerName -NotePropertyValue $env:ComputerName
        # FriendlyName refuses to work remotely, so Name is used as a workaround
        Add-Member -Force -InputObject $c -NotePropertyName Name -NotePropertyValue $c.FriendlyName.ToString()
        Add-Member -Force -InputObject $c -NotePropertyName Store -NotePropertyValue $Store
        Add-Member -Force -InputObject $c -NotePropertyName Folder -NotePropertyValue $Folder -Passthru
    }
}

if ($Thumbprint) {
    try {
        Write-Verbose ""Searching Cert:\$Store\$Folder""
        Get-CoreCertificate -Store $Store -Folder $Folder -Thumbprint $Thumbprint
    } catch {
        # don't care - there's a weird issue with remoting where an exception gets thrown for no apparent reason
        $null = 1
    }
} else {
    try {
        Write-Verbose ""Searching Cert:\$Store\$Folder""
        if ($Type -eq 'Service') {
            Get-CoreCertificate -Store $Store -Folder $Folder | Where-Object EnhancedKeyUsageList -match '1\.3\.6\.1\.5\.5\.7\.3\.1'
        } else {
            Get-CoreCertificate -Store $Store -Folder $Folder
        }
    } catch {
        # don't care - there's a weird issue with remoting where an exception gets thrown for no apparent reason
        $null = 1
    }
}
";
        }

        #endregion Helper Methods
    }
}
