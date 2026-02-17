using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Management.Automation;
using System.Net;
using System.Net.NetworkInformation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Resolves network names and returns detailed network information for SQL Server
    /// connection troubleshooting and validation. Uses multiple resolution methods
    /// including DNS lookups, ICMP ping tests, and WMI/CIM queries.
    /// </summary>
    [Cmdlet("Resolve", "DbaNetworkName")]
    public class ResolveDbaNetworkNameCommand : DbaBaseCmdlet
    {
        /// <summary>
        /// The target computer name, IP address, or SQL Server instance to resolve network information for.
        /// </summary>
        [Parameter(Position = 0, ValueFromPipeline = true)]
        public DbaInstanceParameter[] ComputerName { get; set; }

        /// <summary>
        /// Login to the target instance using alternative credentials.
        /// </summary>
        [Parameter()]
        public PSCredential Credential { get; set; }

        /// <summary>
        /// Enables DNS-only resolution mode for faster network name resolution
        /// without connecting to the target computer.
        /// </summary>
        [Parameter()]
        [Alias("FastParrot")]
        public SwitchParameter Turbo { get; set; }

        private ScriptBlock _sbGetConfigValue;
        private ScriptBlock _sbTestWindows;
        private ScriptBlock _sbGetCmObject;
        private ScriptBlock _sbGetDnsSuffix;

        /// <summary>
        /// Initializes script blocks and resolves the default ComputerName if not bound.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            _sbGetConfigValue = ScriptBlock.Create("param($fn) Get-DbatoolsConfigValue -FullName $fn");
            _sbTestWindows = ScriptBlock.Create("Test-Windows -NoWarn");
            _sbGetCmObject = ScriptBlock.Create(
                "param($cn, $cred) if ($cred) { Get-DbaCmObject -ComputerName $cn -Credential $cred -ClassName win32_ComputerSystem -EnableException } else { Get-DbaCmObject -ComputerName $cn -ClassName win32_ComputerSystem -EnableException }");
            _sbGetDnsSuffix = ScriptBlock.Create(
                "param($cn, $cred) if ($cred) { Invoke-Command2 -ComputerName $cn -Credential $cred -ScriptBlock { [System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().DomainName } -ErrorAction Stop -Raw } else { Invoke-Command2 -ComputerName $cn -ScriptBlock { [System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().DomainName } -ErrorAction Stop -Raw }");

            if (!TestBound("ComputerName"))
            {
                string envComputer = Environment.GetEnvironmentVariable("COMPUTERNAME");
                if (String.IsNullOrEmpty(envComputer))
                    envComputer = Environment.MachineName;
                ComputerName = new DbaInstanceParameter[] { new DbaInstanceParameter(envComputer) };
            }
        }

        /// <summary>
        /// Processes each computer name through DNS resolution and optional WMI/CIM queries.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (ComputerName == null)
                return;

            // Check bypass config
            if (GetConfigBoolValue("commands.resolve-dbanetworkname.bypass"))
            {
                foreach (DbaInstanceParameter computer in ComputerName)
                {
                    PSObject bypass = new PSObject();
                    string compStr = computer.ToString();
                    bypass.Properties.Add(new PSNoteProperty("InputName", compStr));
                    bypass.Properties.Add(new PSNoteProperty("ComputerName", compStr));
                    bypass.Properties.Add(new PSNoteProperty("IPAddress", compStr));
                    bypass.Properties.Add(new PSNoteProperty("DNSHostname", compStr));
                    bypass.Properties.Add(new PSNoteProperty("DNSDomain", compStr));
                    bypass.Properties.Add(new PSNoteProperty("Domain", compStr));
                    bypass.Properties.Add(new PSNoteProperty("DNSHostEntry", compStr));
                    bypass.Properties.Add(new PSNoteProperty("FQDN", compStr));
                    bypass.Properties.Add(new PSNoteProperty("FullComputerName", compStr));
                    WriteObject(bypass);
                }
                return;
            }

            // Check if non-Windows, force Turbo
            bool turbo = Turbo.IsPresent;
            if (!turbo && !TestIsWindows())
            {
                WriteMessageVerbose("Non-Windows client detected. Turbo (DNS resolution only) set to True");
                turbo = true;
            }

            foreach (DbaInstanceParameter computer in ComputerName)
            {
                string cName;
                if (computer.IsLocalHost)
                {
                    cName = Environment.GetEnvironmentVariable("COMPUTERNAME");
                    if (String.IsNullOrEmpty(cName))
                        cName = Environment.MachineName;
                }
                else
                {
                    cName = computer.ComputerName;
                }

                // Resolve IP address via DNS
                IPHostEntry resolved;
                try
                {
                    WriteMessageAtLevel(String.Format("Resolving {0} using .NET.Dns GetHostEntry", cName), MessageLevel.VeryVerbose, null);
                    resolved = Dns.GetHostEntry(cName);
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("DNS name {0} not found", cName),
                        exception: ex,
                        target: computer,
                        isContinue: true);
                    TestFunctionInterrupt();
                    continue;
                }

                // Sort by AddressFamily to prioritize IPv4
                IPAddress[] ipaddresses = resolved.AddressList
                    .OrderBy(a => a.AddressFamily)
                    .ToArray();

                if (ipaddresses.Length == 0)
                {
                    StopFunction(
                        String.Format("No IP addresses found for {0}", cName),
                        target: computer,
                        isContinue: true);
                    TestFunctionInterrupt();
                    continue;
                }

                string ipaddress = ipaddresses[0].ToString();

                // Try reverse DNS lookup
                string fqdn;
                try
                {
                    WriteMessageAtLevel(String.Format("Resolving {0} using .NET.Dns GetHostByAddress", ipaddress), MessageLevel.VeryVerbose, null);
#pragma warning disable 618
                    fqdn = Dns.GetHostByAddress(ipaddress).HostName;
#pragma warning restore 618
                }
                catch
                {
                    WriteMessageAtLevel(String.Format("Failed to resolve {0} using .NET.Dns GetHostByAddress", ipaddress), MessageLevel.Debug, null);
                    fqdn = resolved.HostName;
                }

                string dnsDomain = GetComputerDomainName(fqdn, cName);

                // Augment fqdn if needed
                if (fqdn.IndexOf('.') < 0 && !String.IsNullOrEmpty(dnsDomain))
                {
                    fqdn = String.Format("{0}.{1}", fqdn, dnsDomain);
                }

                string hostname = fqdn.Split('.')[0];

                // Build preliminary result
                PSObject result = new PSObject();
                result.Properties.Add(new PSNoteProperty("InputName", (object)computer));
                result.Properties.Add(new PSNoteProperty("ComputerName", hostname.ToUpper()));
                result.Properties.Add(new PSNoteProperty("IPAddress", ipaddress));
                result.Properties.Add(new PSNoteProperty("DNSHostname", hostname));
                result.Properties.Add(new PSNoteProperty("DNSDomain", dnsDomain));
                result.Properties.Add(new PSNoteProperty("Domain", dnsDomain));
                result.Properties.Add(new PSNoteProperty("DNSHostEntry", fqdn));
                result.Properties.Add(new PSNoteProperty("FQDN", fqdn));
                result.Properties.Add(new PSNoteProperty("FullComputerName", cName));

                if (turbo)
                {
                    WriteObject(result);
                    continue;
                }

                // Ping all IP addresses to find the first that responds
                try
                {
                    using (Ping ping = new Ping())
                    {
                        int timeout = 1000;
                        foreach (IPAddress ip in ipaddresses)
                        {
                            try
                            {
                                PingReply reply = ping.Send(ip, timeout);
                                if (reply.Status == IPStatus.Success)
                                {
                                    ipaddress = ip.ToString();
                                    break;
                                }
                            }
                            catch
                            {
                                // Individual ping may fail, continue to next IP
                            }
                        }
                    }
                }
                catch
                {
                    // Ping creation may fail, keep existing ipaddress
                }

                result.Properties["IPAddress"].Value = ipaddress;

                // Re-try reverse DNS if the responding IP differs
                if (ipaddresses[0].ToString() != ipaddress)
                {
                    try
                    {
                        WriteMessageAtLevel(String.Format("Resolving {0} using .NET.Dns GetHostByAddress", ipaddress), MessageLevel.VeryVerbose, null);
#pragma warning disable 618
                        fqdn = Dns.GetHostByAddress(ipaddress).HostName;
#pragma warning restore 618
                        dnsDomain = GetComputerDomainName(fqdn, cName);
                        if (fqdn.IndexOf('.') < 0 && !String.IsNullOrEmpty(dnsDomain))
                        {
                            fqdn = String.Format("{0}.{1}", fqdn, dnsDomain);
                        }
                        hostname = fqdn.Split('.')[0];

                        result.Properties["ComputerName"].Value = hostname.ToUpper();
                        result.Properties["DNSHostname"].Value = hostname;
                        result.Properties["DNSDomain"].Value = dnsDomain;
                        result.Properties["Domain"].Value = dnsDomain;
                        result.Properties["DNSHostEntry"].Value = fqdn;
                        result.Properties["FQDN"].Value = fqdn;
                    }
                    catch
                    {
                        WriteMessageAtLevel(String.Format("Failed to obtain a new name from {0}, re-using {1}", ipaddress, fqdn), MessageLevel.VeryVerbose, null);
                    }
                }

                // WMI/CIM: Get-DbaCmObject for win32_ComputerSystem
                WriteMessageAtLevel(String.Format("Getting domain name from the remote host {0}", fqdn), MessageLevel.Debug, null);
                try
                {
                    PSObject conn = InvokeGetCmObject(cName);
                    if (conn != null)
                    {
                        object nameObj = GetPSProperty(conn, "Name");
                        if (nameObj != null)
                            result.Properties["ComputerName"].Value = nameObj.ToString();

                        string dnsHostnameWmi = GetPSPropertyString(conn, "DNSHostname");
                        string dnsDomainWmi = GetPSPropertyString(conn, "Domain");

                        if (!String.IsNullOrEmpty(dnsHostnameWmi))
                        {
                            string wmiFqdn = String.Format("{0}.{1}", dnsHostnameWmi, dnsDomainWmi).TrimEnd('.');
                            result.Properties["FQDN"].Value = wmiFqdn;
                            result.Properties["DNSHostname"].Value = dnsHostnameWmi;
                            result.Properties["Domain"].Value = dnsDomainWmi;
                        }

                        // Get DNS suffix from remote host via Invoke-Command2
                        try
                        {
                            WriteMessageAtLevel(String.Format("Getting DNS domain from the remote host {0}", cName), MessageLevel.Debug, null);
                            string dnsSuffix = InvokeGetDnsSuffix(cName);
                            if (dnsSuffix != null)
                            {
                                result.Properties["DNSDomain"].Value = dnsSuffix;
                            }

                            string currentDnsHostname = GetPSPropertyString(result, "DNSHostname");
                            string fullComputerName;
                            if (!String.IsNullOrEmpty(dnsSuffix))
                            {
                                fullComputerName = String.Format("{0}.{1}", currentDnsHostname, dnsSuffix);
                            }
                            else
                            {
                                fullComputerName = currentDnsHostname;
                            }
                            result.Properties["FullComputerName"].Value = fullComputerName;
                        }
                        catch
                        {
                            WriteMessageVerbose(String.Format("Unable to get DNS domain information from {0}", cName));
                        }
                    }
                }
                catch
                {
                    WriteMessageVerbose(String.Format("Unable to get domain name from {0}", cName));
                }

                // Final DNS host entry for the full computer name
                string fullNameForDns = result.Properties["FullComputerName"].Value as string;
                if (!String.IsNullOrEmpty(fullNameForDns))
                {
                    try
                    {
                        WriteMessageAtLevel(String.Format("Resolving {0} using .NET.Dns GetHostEntry", fullNameForDns), MessageLevel.VeryVerbose, null);
                        result.Properties["DNSHostEntry"].Value = Dns.GetHostEntry(fullNameForDns).HostName;
                    }
                    catch
                    {
                        WriteMessageVerbose(String.Format(".NET.Dns GetHostEntry failed for {0}", fullNameForDns));
                    }
                }

                WriteObject(result);
            }
        }

        #region Helper Methods

        /// <summary>
        /// Deduces the domain name based on resolved FQDN and original computer name.
        /// Matches the Get-ComputerDomainName helper in the original PS1.
        /// </summary>
        internal static string GetComputerDomainName(string fqdn, string computerName)
        {
            if (fqdn == null)
                fqdn = String.Empty;
            if (computerName == null)
                computerName = String.Empty;

            if (fqdn.IndexOf('.') < 0)
            {
                // FQDN has no dot
                if (computerName.IndexOf('.') >= 0)
                {
                    return computerName.Substring(computerName.IndexOf('.') + 1);
                }
                else
                {
                    string userDnsDomain = Environment.GetEnvironmentVariable("USERDNSDOMAIN");
                    if (!String.IsNullOrEmpty(userDnsDomain))
                        return userDnsDomain.ToLowerInvariant();
                    return String.Empty;
                }
            }
            else
            {
                return fqdn.Substring(fqdn.IndexOf('.') + 1);
            }
        }

        /// <summary>
        /// Gets a boolean config value via Get-DbatoolsConfigValue.
        /// </summary>
        private bool GetConfigBoolValue(string fullName)
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(false, _sbGetConfigValue, null, fullName);
                if (results != null && results.Count > 0 && results[0] != null)
                {
                    object val = results[0].BaseObject;
                    if (val is bool boolVal)
                        return boolVal;
                    if (val != null)
                    {
                        string strVal = val.ToString();
                        if (String.Equals(strVal, "True", StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
            catch
            {
                // Config may not be available
            }
            return false;
        }

        /// <summary>
        /// Tests if the current platform is Windows by calling Test-Windows -NoWarn.
        /// </summary>
        private bool TestIsWindows()
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(false, _sbTestWindows, null);
                if (results != null && results.Count > 0 && results[0] != null)
                {
                    object val = results[0].BaseObject;
                    if (val is bool boolVal)
                        return boolVal;
                }
            }
            catch
            {
                // Assume Windows if test fails
            }
            return true;
        }

        /// <summary>
        /// Invokes Get-DbaCmObject to get win32_ComputerSystem information.
        /// </summary>
        private PSObject InvokeGetCmObject(string computerName)
        {
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, _sbGetCmObject, null, computerName, Credential);
            if (results != null && results.Count > 0)
                return results[0];
            return null;
        }

        /// <summary>
        /// Invokes Invoke-Command2 to get the DNS suffix from a remote host.
        /// </summary>
        private string InvokeGetDnsSuffix(string computerName)
        {
            Collection<PSObject> results = InvokeCommand.InvokeScript(
                false, _sbGetDnsSuffix, null, computerName, Credential);
            if (results != null && results.Count > 0 && results[0] != null)
            {
                object val = results[0].BaseObject;
                if (val != null)
                    return val.ToString();
            }
            return null;
        }

        /// <summary>
        /// Gets a property value from a PSObject.
        /// </summary>
        internal static object GetPSProperty(PSObject obj, string propertyName)
        {
            if (obj == null)
                return null;

            try
            {
                PSPropertyInfo prop = obj.Properties[propertyName];
                if (prop != null)
                    return prop.Value;
            }
            catch
            {
                // Property may not exist
            }
            return null;
        }

        /// <summary>
        /// Gets a string property value from a PSObject.
        /// </summary>
        internal static string GetPSPropertyString(PSObject obj, string propertyName)
        {
            object val = GetPSProperty(obj, propertyName);
            if (val != null)
                return val.ToString();
            return null;
        }

        #endregion Helper Methods
    }
}